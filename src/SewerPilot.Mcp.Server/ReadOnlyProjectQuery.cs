using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.Mcp.Server
{
    public sealed class MetadataReadException : Exception
    {
        public string Classification { get; }
        public MetadataReadException(string classification) : base(classification)
        {
            Classification = classification;
        }
    }

    public sealed class ReadOnlyProjectQuery
    {
        public const long MaximumInputBytes = 16 * 1024 * 1024;
        private readonly string configuredDirectory;

        public ReadOnlyProjectQuery(string configuredDirectory)
        {
            this.configuredDirectory = configuredDirectory;
        }

        public ProjectInfo Read()
        {
            try
            {
                string directory = CanonicalDirectory(configuredDirectory);
                using (var lease = InputLease.Acquire(directory))
                {
                    // This accepted service validates stored state and audit without appending audit.
                    ProjectState state = ProjectStore.LoadProject(directory);
                    lease.VerifyOptionalAudit();
                    return new ProjectInfo
                    {
                        ProjectId = state.ProjectId,
                        ProjectName = state.ProjectName,
                        SchemaVersion = state.SchemaVersion,
                        StateRevision = state.StateRevision,
                        SelectedModule = state.SelectedModule,
                        StoredModelFingerprint = state.Model?.ProtectedBaselineFingerprint,
                        GravityFlowDesignBasisReference = Reference(state.DesignBasisReference),
                        StormFlowDesignBasisReference = Reference(state.StormFlowDesignBasisReference)
                    };
                }
            }
            catch (MetadataReadException) { throw; }
            catch (ProjectStorageException error)
            {
                // Classification comes from the accepted service; never forward its detailed message.
                throw new MetadataReadException(Regex.IsMatch(error.Classification, "^[A-Z_]{1,64}$")
                    ? error.Classification : "PROJECT_STORAGE_ERROR");
            }
            catch (UnauthorizedAccessException) { throw new MetadataReadException("PROJECT_ACCESS_DENIED"); }
            catch (IOException) { throw new MetadataReadException("PROJECT_IO_ERROR"); }
            catch (ArgumentException) { throw new MetadataReadException("PROJECT_PATH_INVALID"); }
            catch (NotSupportedException) { throw new MetadataReadException("PROJECT_PATH_INVALID"); }
        }

        private static BasisReference Reference(DesignBasisReference reference) => reference == null ? null :
            new BasisReference { ReferenceIdentifier = reference.ReferenceIdentifier, DesignBasisVersion = reference.DesignBasisVersion };

        public StoredDesignBasisSummary ReadDesignBasis(string module)
        {
            try
            {
                string directory = CanonicalDirectory(configuredDirectory);
                using (var lease = InputLease.Acquire(directory))
                {
                    ProjectState state = ProjectStore.LoadProject(directory);
                    StoredDesignArtifactReference reference = ReadOnlyDesignBasisInspection.Locate(state, module);
                    if (reference != null) lease.PinArtifact(directory, reference);
                    StoredDesignBasisSummary summary = ReadOnlyDesignBasisInspection.Read(directory, state, module);
                    lease.VerifyOptionalAudit();
                    return summary;
                }
            }
            catch (MetadataReadException) { throw; }
            catch (ProjectStorageException error) { throw new MetadataReadException(SafeClassification(error.Classification)); }
            catch (DesignBasisValidationException error) { throw new MetadataReadException(SafeClassification(error.Classification)); }
            catch (UnauthorizedAccessException) { throw new MetadataReadException("PROJECT_ACCESS_DENIED"); }
            catch (IOException) { throw new MetadataReadException("PROJECT_IO_ERROR"); }
            catch (ArgumentException) { throw new MetadataReadException("PROJECT_PATH_INVALID"); }
            catch (NotSupportedException) { throw new MetadataReadException("PROJECT_PATH_INVALID"); }
        }

        public StoredVerticalFeasibilitySummary ReadVerticalFeasibility()
        {
            try
            {
                string directory = CanonicalDirectory(configuredDirectory);
                using (var lease = InputLease.Acquire(directory))
                {
                    ProjectState state = ProjectStore.LoadProject(directory);
                    StoredDesignArtifactReference reference = ReadOnlyGravityCandidateInspection.Locate(state);
                    if (reference != null) lease.PinArtifact(directory, reference);
                    StoredVerticalFeasibilitySummary summary = ReadOnlyGravityCandidateInspection.AssessFeasibility(directory, state);
                    lease.VerifyOptionalAudit();
                    return summary;
                }
            }
            catch (MetadataReadException) { throw; }
            catch (ProjectStorageException error) { throw new MetadataReadException(SafeClassification(error.Classification)); }
            catch (DesignBasisValidationException error) { throw new MetadataReadException(SafeClassification(error.Classification)); }
            catch (UnauthorizedAccessException) { throw new MetadataReadException("PROJECT_ACCESS_DENIED"); }
            catch (IOException) { throw new MetadataReadException("PROJECT_IO_ERROR"); }
            catch (ArgumentException) { throw new MetadataReadException("PROJECT_PATH_INVALID"); }
            catch (NotSupportedException) { throw new MetadataReadException("PROJECT_PATH_INVALID"); }
        }

        public StoredGravityCandidatePreviewSummary ReadCandidatePreview(string candidateId = null, string recipeId = null)
        {
            try
            {
                string directory = CanonicalDirectory(configuredDirectory);
                using (var lease = InputLease.Acquire(directory))
                {
                    ProjectState state = ProjectStore.LoadProject(directory);
                    StoredDesignArtifactReference reference = ReadOnlyGravityCandidateInspection.Locate(state);
                    if (reference != null) lease.PinArtifact(directory, reference);
                    StoredGravityCandidatePreviewSummary summary = ReadOnlyGravityCandidateInspection.PreviewCandidates(directory, state, candidateId, recipeId);
                    lease.VerifyOptionalAudit();
                    return summary;
                }
            }
            catch (MetadataReadException) { throw; }
            catch (ProjectStorageException error) { throw new MetadataReadException(SafeClassification(error.Classification)); }
            catch (DesignBasisValidationException error) { throw new MetadataReadException(SafeClassification(error.Classification)); }
            catch (UnauthorizedAccessException) { throw new MetadataReadException("PROJECT_ACCESS_DENIED"); }
            catch (IOException) { throw new MetadataReadException("PROJECT_IO_ERROR"); }
            catch (ArgumentException) { throw new MetadataReadException("PROJECT_PATH_INVALID"); }
            catch (NotSupportedException) { throw new MetadataReadException("PROJECT_PATH_INVALID"); }
        }

        private static string SafeClassification(string value) => value != null && Regex.IsMatch(value, "^[A-Z_]{1,64}$") ? value : "PROJECT_STORAGE_ERROR";

        private static string CanonicalDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new MetadataReadException("PROJECT_DIR_REQUIRED");
            // Only an operator-configured absolute local drive path is supported, never UNC/devices/ADS.
            if (path.Length > 240 || path != path.Trim() || !Regex.IsMatch(path, "^[A-Za-z]:[\\\\/]") ||
                path.Substring(2).IndexOf(':') >= 0 || path.IndexOfAny(new[] { '*', '?', '\0' }) >= 0)
                throw new MetadataReadException("PROJECT_PATH_INVALID");
            string normalized = path.Replace('/', '\\');
            foreach (string part in normalized.Substring(3).Split('\\'))
                if (part == "." || part == ".." || (part.Length > 0 && (part.EndsWith(".") || part.EndsWith(" "))))
                    throw new MetadataReadException("PROJECT_PATH_INVALID");
            string full = Path.GetFullPath(normalized).TrimEnd('\\');
            if (full.Length <= 3 || new DriveInfo(Path.GetPathRoot(full)).DriveType == DriveType.Network)
                throw new MetadataReadException("PROJECT_PATH_INVALID");
            return full;
        }

        private sealed class InputLease : IDisposable
        {
            private readonly List<SafeFileHandle> handles = new List<SafeFileHandle>();
            private string auditPath;
            private bool auditAbsent;

            internal static InputLease Acquire(string directory)
            {
                var lease = new InputLease();
                try
                {
                    var ancestors = new Stack<string>();
                    for (string cursor = directory; cursor != null; cursor = Path.GetDirectoryName(cursor))
                        ancestors.Push(cursor);
                    // Pin each directory from root down; do not follow a substituted reparse point.
                    while (ancestors.Count != 0) lease.Open(ancestors.Pop(), true, false);
                    lease.Open(Path.Combine(directory, "project.json"), false, false);
                    lease.auditPath = Path.Combine(directory, "audit.jsonl");
                    lease.auditAbsent = !lease.Open(lease.auditPath, false, true);
                    lease.VerifyOptionalAudit();
                    return lease;
                }
                catch { lease.Dispose(); throw; }
            }

            private bool Open(string path, bool directory, bool optional)
            {
                // Read sharing only prevents replacement/growth of existing inputs during service reads.
                SafeFileHandle handle = CreateFile(path, directory ? 0x80u : 0x80000000u,
                    1, IntPtr.Zero, 3, 0x00200000u | (directory ? 0x02000000u : 0u), IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    int code = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    if (optional && code == 2) return false;
                    throw new MetadataReadException(code == 2 || code == 3 ? "PROJECT_INPUT_MISSING" :
                        code == 5 ? "PROJECT_ACCESS_DENIED" : code == 32 || code == 33 ? "PROJECT_INPUT_BUSY" : "PROJECT_IO_ERROR");
                }
                handles.Add(handle);
                if (!GetFileInformationByHandle(handle, out NativeFileInfo info))
                    throw new MetadataReadException("PROJECT_IO_ERROR");
                if ((info.Attributes & 0x400) != 0)
                    throw new MetadataReadException("PROJECT_REPARSE_POINT_REJECTED");
                if (((info.Attributes & 0x10) != 0) != directory)
                    throw new MetadataReadException("PROJECT_PATH_INVALID");
                if (!directory && (((ulong)info.SizeHigh << 32) | info.SizeLow) > (ulong)MaximumInputBytes)
                    throw new MetadataReadException("PROJECT_INPUT_TOO_LARGE");
                return true;
            }

            internal void VerifyOptionalAudit()
            {
                // Legacy projects may omit audit. Refuse a newly appearing audit instead of accepting
                // metadata from a changing input set; existing audit is pinned and size-checked above.
                if (auditAbsent && (File.Exists(auditPath) || Directory.Exists(auditPath)))
                    throw new MetadataReadException("PROJECT_INPUT_CHANGED");
            }

            internal void PinArtifact(string directory, StoredDesignArtifactReference reference)
            {
                try
                {
                    // Locator accepts only fixed module folders and strict immutable-reference tokens.
                    string folder = Path.Combine(directory, reference.FolderName);
                    Open(folder, true, false);
                    Open(Path.Combine(folder, reference.ReferenceIdentifier + ".json"), false, false);
                }
                catch (MetadataReadException error)
                {
                    if (error.Classification == "PROJECT_INPUT_MISSING")
                        throw new MetadataReadException(reference.FolderName == "engineering" ? "ENGINEERING_ARTIFACT_MISSING" : "STORMS_BASIS_MISSING");
                    throw;
                }
            }

            public void Dispose()
            {
                for (int i = handles.Count - 1; i >= 0; i--) handles[i].Dispose();
                handles.Clear();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFileInfo
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
            public uint VolumeSerialNumber, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint sharing,
            IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out NativeFileInfo info);
    }
}
