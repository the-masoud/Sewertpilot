using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class ModelFamilyFileEntry : IEquatable<ModelFamilyFileEntry>
    {
        public ModelFamilyFileEntry(string relativePath, long fileLength, string sha256)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Relative path is required.", nameof(relativePath));
            if (fileLength < 0)
                throw new ArgumentException("File length cannot be negative.", nameof(fileLength));
            if (string.IsNullOrWhiteSpace(sha256) || sha256.Length != 64)
                throw new ArgumentException("A 64-character SHA-256 hex string is required.", nameof(sha256));

            RelativePath = relativePath.Replace('\\', '/').TrimStart('/');
            FileLength = fileLength;
            SHA256 = sha256.ToUpperInvariant();
        }

        public string RelativePath { get; }
        public long FileLength { get; }
        public string SHA256 { get; }

        public bool Equals(ModelFamilyFileEntry other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(RelativePath, other.RelativePath, StringComparison.OrdinalIgnoreCase) &&
                   FileLength == other.FileLength &&
                   string.Equals(SHA256, other.SHA256, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as ModelFamilyFileEntry);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(RelativePath);
                hash = (hash * 397) ^ FileLength.GetHashCode();
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(SHA256);
                return hash;
            }
        }

        public override string ToString() => $"{RelativePath}|{FileLength}|{SHA256}";
    }

    public sealed class ModelFamilyFingerprint : IEquatable<ModelFamilyFingerprint>
    {
        public const string CurrentSchemaVersion = "sewerpilot-model-family-v1";

        public ModelFamilyFingerprint(string value, string canonicalManifest)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
                throw new ArgumentException("A 64-character SHA-256 hex string is required.", nameof(value));
            Value = value.ToUpperInvariant();
            CanonicalManifest = canonicalManifest ?? string.Empty;
        }

        public string Value { get; }
        public string CanonicalManifest { get; }

        public static ModelFamilyFingerprint Create(string primaryFileName, IEnumerable<ModelFamilyFileEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(primaryFileName))
                throw new ArgumentException("Primary file name is required.", nameof(primaryFileName));
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            var entryList = entries.ToList();
            if (entryList.Count == 0)
                throw new ArgumentException("At least one family file entry is required.", nameof(entries));

            string canonicalPrimary = primaryFileName.Replace('\\', '/').TrimStart('/').ToUpperInvariant();

            var sorted = entryList
                .OrderBy(e => e.RelativePath.ToUpperInvariant(), StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            sb.Append("schema:").Append(CurrentSchemaVersion).Append('\n');
            sb.Append("primary:").Append(canonicalPrimary).Append('\n');
            foreach (var entry in sorted)
            {
                sb.Append("entry:")
                  .Append(entry.RelativePath.ToUpperInvariant())
                  .Append('|')
                  .Append(entry.FileLength.ToString(System.Globalization.CultureInfo.InvariantCulture))
                  .Append('|')
                  .Append(entry.SHA256)
                  .Append('\n');
            }

            string canonical = sb.ToString();
            using (var sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(canonical);
                byte[] hash = sha.ComputeHash(bytes);
                string hex = BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
                return new ModelFamilyFingerprint(hex, canonical);
            }
        }

        public bool Equals(ModelFamilyFingerprint other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as ModelFamilyFingerprint);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    public sealed class BaselineIdentity : IEquatable<BaselineIdentity>
    {
        public BaselineIdentity(ModelFamilyFingerprint fingerprint, string primaryModelName, IEnumerable<ModelFamilyFileEntry> familyEntries)
        {
            Fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
            PrimaryModelName = primaryModelName ?? throw new ArgumentNullException(nameof(primaryModelName));
            FamilyEntries = (familyEntries ?? throw new ArgumentNullException(nameof(familyEntries))).ToList().AsReadOnly();
        }

        public ModelFamilyFingerprint Fingerprint { get; }
        public string PrimaryModelName { get; }
        public IReadOnlyList<ModelFamilyFileEntry> FamilyEntries { get; }

        public bool Equals(BaselineIdentity other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Fingerprint.Equals(other.Fingerprint);
        }

        public override bool Equals(object obj) => Equals(obj as BaselineIdentity);
        public override int GetHashCode() => Fingerprint.GetHashCode();
        public override string ToString() => $"Baseline[{PrimaryModelName}:{Fingerprint}]";
    }

    public sealed class WorkingCopyIdentity : IEquatable<WorkingCopyIdentity>
    {
        public WorkingCopyIdentity(string workingCopyId, string sourceBaselineFingerprint, string workingCopyFamilyFingerprint, string workingDirectory, string primaryFilePath, DateTimeOffset createdUtc)
        {
            if (string.IsNullOrWhiteSpace(workingCopyId))
                throw new ArgumentException("Working copy ID is required.", nameof(workingCopyId));
            if (string.IsNullOrWhiteSpace(sourceBaselineFingerprint))
                throw new ArgumentException("Source baseline fingerprint is required.", nameof(sourceBaselineFingerprint));
            if (string.IsNullOrWhiteSpace(workingCopyFamilyFingerprint))
                throw new ArgumentException("Working copy family fingerprint is required.", nameof(workingCopyFamilyFingerprint));
            if (string.IsNullOrWhiteSpace(workingDirectory))
                throw new ArgumentException("Working directory is required.", nameof(workingDirectory));
            if (string.IsNullOrWhiteSpace(primaryFilePath))
                throw new ArgumentException("Primary file path is required.", nameof(primaryFilePath));

            WorkingCopyId = workingCopyId;
            SourceBaselineFingerprint = sourceBaselineFingerprint.ToUpperInvariant();
            WorkingCopyFamilyFingerprint = workingCopyFamilyFingerprint.ToUpperInvariant();
            WorkingDirectory = workingDirectory;
            PrimaryFilePath = primaryFilePath;
            CreatedUtc = createdUtc;
        }

        public string WorkingCopyId { get; }
        public string WorkingDirectory { get; }
        public string PrimaryFilePath { get; }
        public DateTimeOffset CreatedUtc { get; }
        public string SourceBaselineFingerprint { get; }
        public string WorkingCopyFamilyFingerprint { get; }

        public bool Equals(WorkingCopyIdentity other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(WorkingCopyId, other.WorkingCopyId, StringComparison.Ordinal) &&
                   string.Equals(SourceBaselineFingerprint, other.SourceBaselineFingerprint, StringComparison.Ordinal) &&
                   string.Equals(WorkingCopyFamilyFingerprint, other.WorkingCopyFamilyFingerprint, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as WorkingCopyIdentity);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(WorkingCopyId);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(SourceBaselineFingerprint);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(WorkingCopyFamilyFingerprint);
                return hash;
            }
        }
        public override string ToString() => $"WorkingCopy[{WorkingCopyId}:Source={SourceBaselineFingerprint}]";
    }

    public sealed class ModelSession
    {
        public ModelSession(string sessionId, string associatedBaselineFingerprint, string associatedWorkingCopyId, DateTimeOffset startedUtc)
        {
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;
            AssociatedBaselineFingerprint = associatedBaselineFingerprint;
            AssociatedWorkingCopyId = associatedWorkingCopyId;
            StartedUtc = startedUtc;
        }

        public string SessionId { get; }
        public string AssociatedBaselineFingerprint { get; }
        public string AssociatedWorkingCopyId { get; }
        public DateTimeOffset StartedUtc { get; }

        public static ModelSession Create(string baselineFingerprint, string workingCopyId) =>
            new ModelSession(Guid.NewGuid().ToString("N"), baselineFingerprint, workingCopyId, DateTimeOffset.UtcNow);
    }
}
