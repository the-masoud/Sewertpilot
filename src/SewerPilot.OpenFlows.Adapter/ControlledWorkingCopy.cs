using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class ControlledWorkingCopy : IDisposable
    {
        private bool isDisposed;
        private readonly List<string> cleanupFailures = new List<string>();

        /// <summary>
        /// Cleanup failures observed while disposing this working copy. Disposal is best-effort and
        /// must never abort teardown, but a cleanup failure is still recorded here so that it is
        /// observable rather than silently swallowed.
        /// </summary>
        internal IReadOnlyList<string> CleanupFailures
        {
            get { return cleanupFailures; }
        }

        private ControlledWorkingCopy(
            WorkingCopyIdentity identity,
            ProtectedBaseline baseline,
            string directoryPath,
            string primaryFilePath,
            IReadOnlyList<string> familyFilePaths,
            IntegritySnapshot initialSnapshot)
        {
            Identity = identity;
            Baseline = baseline;
            DirectoryPath = directoryPath;
            PrimaryFilePath = primaryFilePath;
            FamilyFilePaths = familyFilePaths;
            InitialSnapshot = initialSnapshot;
        }

        public WorkingCopyIdentity Identity { get; }
        public ProtectedBaseline Baseline { get; }
        public string DirectoryPath { get; }
        public string PrimaryFilePath { get; }
        public IReadOnlyList<string> FamilyFilePaths { get; }
        public IntegritySnapshot InitialSnapshot { get; }

        public static ControlledWorkingCopy Create(ProtectedBaseline baseline, string targetRootDirectory = null)
        {
            if (baseline == null)
                throw new ArgumentNullException(nameof(baseline));

            var baselineIntegrity = baseline.VerifyCurrentIntegrity();
            if (!baselineIntegrity.IsMatch)
                throw new InvalidOperationException($"Cannot create working copy: Protected baseline integrity failed ({baselineIntegrity})");

            string rootDir = string.IsNullOrWhiteSpace(targetRootDirectory)
                ? Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "Working")
                : SafePathPolicy.Normalize(targetRootDirectory);

            string workingCopyId = Guid.NewGuid().ToString("N");
            string workingDir = Path.Combine(rootDir, workingCopyId);

            SafePathPolicy.ValidateWorkingCopyLocation(baseline.DirectoryPath, workingDir);

            if (Directory.Exists(workingDir))
            {
                if (Directory.GetFileSystemEntries(workingDir).Length > 0)
                    throw new IOException($"Target working directory already exists and is not empty: {workingDir}");
            }
            else
            {
                Directory.CreateDirectory(workingDir);
            }

            var copiedPaths = new List<string>();
            bool copySucceeded = false;

            try
            {
                foreach (string relPath in baseline.FamilyRelativePaths)
                {
                    string sourceFile = Path.Combine(baseline.DirectoryPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                    string targetFile = Path.Combine(workingDir, relPath.Replace('/', Path.DirectorySeparatorChar));

                    string targetSubdir = Path.GetDirectoryName(targetFile);
                    if (!Directory.Exists(targetSubdir))
                        Directory.CreateDirectory(targetSubdir);

                    File.Copy(sourceFile, targetFile, false);
                    copiedPaths.Add(targetFile);
                }

                var workingSnapshot = IntegritySnapshot.Capture(workingDir, baseline.PrimaryFileName, baseline.FamilyRelativePaths);
                if (!string.Equals(workingSnapshot.Fingerprint.Value, baseline.Fingerprint.Value, StringComparison.Ordinal))
                {
                    throw new IOException($"Working copy fingerprint '{workingSnapshot.Fingerprint.Value}' differs from baseline fingerprint '{baseline.Fingerprint.Value}' immediately after copy.");
                }

                string primaryWorkingPath = Path.Combine(workingDir, baseline.PrimaryFileName);
                var identity = new WorkingCopyIdentity(
                    workingCopyId,
                    baseline.Fingerprint.Value,
                    workingSnapshot.Fingerprint.Value,
                    workingDir,
                    primaryWorkingPath,
                    DateTimeOffset.UtcNow);

                copySucceeded = true;
                return new ControlledWorkingCopy(identity, baseline, workingDir, primaryWorkingPath, copiedPaths.AsReadOnly(), workingSnapshot);
            }
            catch (Exception copyFailure)
            {
                if (!copySucceeded)
                {
                    try
                    {
                        if (Directory.Exists(workingDir))
                        {
                            foreach (string copied in copiedPaths)
                            {
                                if (File.Exists(copied)) File.Delete(copied);
                            }
                            Directory.Delete(workingDir, true);
                        }
                    }
                    catch (Exception cleanupFailure)
                    {
                        // The copy failure below is the authoritative outcome and is rethrown
                        // unchanged; the secondary cleanup failure is attached to it so that it
                        // remains observable instead of being discarded.
                        copyFailure.Data["WorkingCopyCleanupFailure"] = cleanupFailure;
                    }
                }
                throw;
            }
        }

        public IntegrityComparisonResult VerifyCurrentIntegrity()
        {
            try
            {
                var current = IntegritySnapshot.Capture(DirectoryPath, Baseline.PrimaryFileName, Baseline.FamilyRelativePaths);
                return IntegritySnapshot.Compare(InitialSnapshot, current);
            }
            catch (Exception ex)
            {
                return IntegrityComparisonResult.Mismatch(new[] {
                    new IntegrityDifference("IntegrityVerificationException", "", "Verifiable", "Error", ex.Message)
                });
            }
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, true);
                }
            }
            catch (Exception cleanupFailure)
            {
                // Best-effort cleanup: disposal must not throw, but the failure is retained on
                // CleanupFailures so that it is never silently swallowed.
                cleanupFailures.Add("WORKING_COPY_DISPOSE_CLEANUP_FAILED: " + cleanupFailure.Message);
            }
        }
    }
}
