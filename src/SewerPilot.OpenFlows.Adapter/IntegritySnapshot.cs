using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class FileIntegrityEntry
    {
        public FileIntegrityEntry(string relativePath, long fileLength, string sha256)
        {
            RelativePath = relativePath.Replace('\\', '/').TrimStart('/');
            FileLength = fileLength;
            SHA256 = sha256.ToUpperInvariant();
        }

        public string RelativePath { get; }
        public long FileLength { get; }
        public string SHA256 { get; }

        public ModelFamilyFileEntry ToModelFamilyFileEntry() =>
            new ModelFamilyFileEntry(RelativePath, FileLength, SHA256);
    }

    public sealed class IntegrityDifference
    {
        public IntegrityDifference(string kind, string relativePath, string expected, string actual, string description)
        {
            Kind = kind;
            RelativePath = relativePath;
            Expected = expected;
            Actual = actual;
            Description = description;
        }

        public string Kind { get; }
        public string RelativePath { get; }
        public string Expected { get; }
        public string Actual { get; }
        public string Description { get; }

        public override string ToString() => $"[{Kind}] {RelativePath}: {Description} (expected '{Expected}', got '{Actual}')";
    }

    public sealed class IntegrityComparisonResult
    {
        public IntegrityComparisonResult(bool isMatch, IReadOnlyList<IntegrityDifference> differences)
        {
            IsMatch = isMatch;
            Differences = differences ?? new List<IntegrityDifference>().AsReadOnly();
        }

        public bool IsMatch { get; }
        public IReadOnlyList<IntegrityDifference> Differences { get; }

        public static IntegrityComparisonResult Match() =>
            new IntegrityComparisonResult(true, new List<IntegrityDifference>().AsReadOnly());

        public static IntegrityComparisonResult Mismatch(IReadOnlyList<IntegrityDifference> differences) =>
            new IntegrityComparisonResult(false, differences);

        public override string ToString() =>
            IsMatch ? "Integrity MATCH" : $"Integrity MISMATCH ({Differences.Count} differences): {string.Join("; ", Differences)}";
    }

    public sealed class IntegritySnapshot
    {
        public IntegritySnapshot(ModelFamilyFingerprint fingerprint, DateTimeOffset capturedUtc, IDictionary<string, FileIntegrityEntry> entries)
        {
            Fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
            CapturedUtc = capturedUtc;
            Entries = new Dictionary<string, FileIntegrityEntry>(entries ?? new Dictionary<string, FileIntegrityEntry>(), StringComparer.OrdinalIgnoreCase);
        }

        public ModelFamilyFingerprint Fingerprint { get; }
        public DateTimeOffset CapturedUtc { get; }
        public IReadOnlyDictionary<string, FileIntegrityEntry> Entries { get; }

        public static IntegritySnapshot Capture(string directoryPath, string primaryFileName, IEnumerable<string> relativeFilePaths)
        {
            string normDir = SafePathPolicy.Normalize(directoryPath);
            if (!Directory.Exists(normDir))
                throw new DirectoryNotFoundException($"Directory not found: {normDir}");

            var fileEntries = new Dictionary<string, FileIntegrityEntry>(StringComparer.OrdinalIgnoreCase);
            var familyEntries = new List<ModelFamilyFileEntry>();

            using (var sha = SHA256.Create())
            {
                foreach (string relative in relativeFilePaths)
                {
                    if (string.IsNullOrWhiteSpace(relative)) continue;

                    string cleanRel = relative.Replace('\\', '/').TrimStart('/');
                    if (cleanRel.Contains(".."))
                        throw new ArgumentException($"Relative path must not contain traversal: {relative}", nameof(relativeFilePaths));

                    string fullPath = Path.Combine(normDir, cleanRel.Replace('/', Path.DirectorySeparatorChar));
                    SafePathPolicy.RejectLinks(fullPath);

                    if (!File.Exists(fullPath))
                    {
                        continue;
                    }

                    var info = new FileInfo(fullPath);
                    long length = info.Length;
                    string hashHex;
                    using (var stream = File.OpenRead(fullPath))
                    {
                        byte[] hashBytes = sha.ComputeHash(stream);
                        hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToUpperInvariant();
                    }

                    var entry = new FileIntegrityEntry(cleanRel, length, hashHex);
                    fileEntries[cleanRel] = entry;
                    familyEntries.Add(entry.ToModelFamilyFileEntry());
                }
            }

            var fingerprint = familyEntries.Count > 0
                ? ModelFamilyFingerprint.Create(primaryFileName, familyEntries)
                : new ModelFamilyFingerprint(new string('0', 64), "schema:empty");
            return new IntegritySnapshot(fingerprint, DateTimeOffset.UtcNow, fileEntries);
        }

        public static IntegrityComparisonResult Compare(IntegritySnapshot expected, IntegritySnapshot actual)
        {
            if (expected == null && actual == null) return IntegrityComparisonResult.Match();
            if (expected == null)
                return IntegrityComparisonResult.Mismatch(new[] { new IntegrityDifference("SnapshotMissing", "", "Present", "Null", "Expected snapshot is null.") });
            if (actual == null)
                return IntegrityComparisonResult.Mismatch(new[] { new IntegrityDifference("SnapshotMissing", "", "Present", "Null", "Actual snapshot is null.") });

            var diffs = new List<IntegrityDifference>();

            if (!string.Equals(expected.Fingerprint.Value, actual.Fingerprint.Value, StringComparison.Ordinal))
            {
                diffs.Add(new IntegrityDifference("FingerprintMismatch", "", expected.Fingerprint.Value, actual.Fingerprint.Value, "Model family fingerprint changed."));
            }

            foreach (var kvp in expected.Entries)
            {
                string rel = kvp.Key;
                var expEntry = kvp.Value;
                if (!actual.Entries.TryGetValue(rel, out var actEntry))
                {
                    diffs.Add(new IntegrityDifference("MissingFile", rel, expEntry.SHA256, "MISSING", $"Expected file '{rel}' is missing in actual snapshot."));
                    continue;
                }

                if (expEntry.FileLength != actEntry.FileLength)
                {
                    diffs.Add(new IntegrityDifference("LengthMismatch", rel, expEntry.FileLength.ToString(), actEntry.FileLength.ToString(), $"File length changed for '{rel}'."));
                }

                if (!string.Equals(expEntry.SHA256, actEntry.SHA256, StringComparison.Ordinal))
                {
                    diffs.Add(new IntegrityDifference("HashMismatch", rel, expEntry.SHA256, actEntry.SHA256, $"SHA-256 hash changed for '{rel}'."));
                }
            }

            foreach (var kvp in actual.Entries)
            {
                string rel = kvp.Key;
                if (!expected.Entries.ContainsKey(rel))
                {
                    diffs.Add(new IntegrityDifference("UnexpectedFile", rel, "ABSENT", kvp.Value.SHA256, $"Unexpected extra file '{rel}' present in actual snapshot."));
                }
            }

            return diffs.Count == 0 ? IntegrityComparisonResult.Match() : IntegrityComparisonResult.Mismatch(diffs.AsReadOnly());
        }
    }
}
