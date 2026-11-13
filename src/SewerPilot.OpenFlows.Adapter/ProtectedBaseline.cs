using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class ProtectedBaseline
    {
        private ProtectedBaseline(
            BaselineIdentity identity,
            string primaryFilePath,
            string directoryPath,
            IReadOnlyList<string> familyRelativePaths,
            IntegritySnapshot initialSnapshot,
            DateTimeOffset registeredUtc)
        {
            Identity = identity;
            PrimaryFilePath = primaryFilePath;
            DirectoryPath = directoryPath;
            PrimaryFileName = Path.GetFileName(primaryFilePath);
            FamilyRelativePaths = familyRelativePaths;
            InitialSnapshot = initialSnapshot;
            RegisteredUtc = registeredUtc;
        }

        public BaselineIdentity Identity { get; }
        public string PrimaryFilePath { get; }
        public string DirectoryPath { get; }
        public string PrimaryFileName { get; }
        public IReadOnlyList<string> FamilyRelativePaths { get; }
        public ModelFamilyFingerprint Fingerprint => Identity.Fingerprint;
        public IntegritySnapshot InitialSnapshot { get; }
        public DateTimeOffset RegisteredUtc { get; }

        public static ProtectedBaseline Register(string primaryFilePath, IEnumerable<string> associatedRelativePaths = null)
        {
            string normalizedPrimary = SafePathPolicy.Normalize(primaryFilePath);
            if (!File.Exists(normalizedPrimary))
                throw new FileNotFoundException($"Primary model file does not exist: {normalizedPrimary}");

            string directory = Path.GetDirectoryName(normalizedPrimary);
            string primaryName = Path.GetFileName(normalizedPrimary);

            var relativeFiles = new List<string>();
            relativeFiles.Add(primaryName);

            if (associatedRelativePaths != null)
            {
                foreach (string rel in associatedRelativePaths)
                {
                    if (string.IsNullOrWhiteSpace(rel)) continue;
                    string clean = rel.Replace('\\', '/').TrimStart('/');
                    relativeFiles.Add(clean);
                }
            }
            else
            {
                string sqliteName = primaryName + ".sqlite";
                string dwhName = primaryName + ".dwh";
                if (File.Exists(Path.Combine(directory, sqliteName)))
                    relativeFiles.Add(sqliteName);
                if (File.Exists(Path.Combine(directory, dwhName)))
                    relativeFiles.Add(dwhName);
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in relativeFiles)
            {
                if (!seen.Add(file))
                    throw new ArgumentException($"Duplicate family entry rejected: '{file}'", nameof(associatedRelativePaths));
            }

            var snapshot = IntegritySnapshot.Capture(directory, primaryName, relativeFiles);
            var familyEntries = snapshot.Entries.Values.Select(e => e.ToModelFamilyFileEntry()).ToList();
            var identity = new BaselineIdentity(snapshot.Fingerprint, primaryName, familyEntries);

            return new ProtectedBaseline(
                identity,
                normalizedPrimary,
                directory,
                relativeFiles.AsReadOnly(),
                snapshot,
                DateTimeOffset.UtcNow);
        }

        public IntegrityComparisonResult VerifyCurrentIntegrity()
        {
            try
            {
                var current = IntegritySnapshot.Capture(DirectoryPath, PrimaryFileName, FamilyRelativePaths);
                return IntegritySnapshot.Compare(InitialSnapshot, current);
            }
            catch (Exception ex)
            {
                return IntegrityComparisonResult.Mismatch(new[] {
                    new IntegrityDifference("IntegrityVerificationException", "", "Verifiable", "Error", ex.Message)
                });
            }
        }
    }
}
