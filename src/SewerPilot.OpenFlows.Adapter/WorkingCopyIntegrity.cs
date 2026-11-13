using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace SewerPilot.OpenFlows.Adapter
{
    internal sealed class WorkingCopyIntegrity
    {
        internal List<FileFingerprint> SourceBefore { get; private set; }
        internal List<FileFingerprint> WorkingBefore { get; private set; }
        internal string DirectoryPath { get; private set; }
        internal string ModelPath { get; private set; }

        internal static void RejectLinks(string path)
        {
            for (string cursor = Path.GetFullPath(path); !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
                if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                    (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Model/runtime paths must not traverse filesystem links.");
        }
        internal static FileFingerprint Fingerprint(string path)
        {
            RejectLinks(path);
            using (var hash = SHA256.Create())
            using (var input = File.OpenRead(path))
                return new FileFingerprint(Path.GetFullPath(path), BitConverter.ToString(hash.ComputeHash(input)).Replace("-", ""));
        }
        internal static List<FileFingerprint> Snapshot(IEnumerable<FileFingerprint> before) =>
            before.Select(item => Fingerprint(item.Path)).ToList();
        internal static bool Equal(IEnumerable<FileFingerprint> before, IEnumerable<FileFingerprint> after)
        {
            var originals = before.ToArray();
            var observed = after.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
            return originals.Length > 0 && originals.All(item => observed.ContainsKey(item.Path) && observed[item.Path].SHA256 == item.SHA256);
        }
        internal static WorkingCopyIntegrity Create(string source) => Create(source, null);

        internal static WorkingCopyIntegrity Create(string source, IEnumerable<string> explicitFamily) => Create(source, explicitFamily, "ReadOnly");

        // The caller may supply an explicit, already-verified family (for example a model plus its
        // scenario result companions). When omitted, only the three established state files are used.
        // The category selects the controlled per-run root: read-only observation copies never share a
        // root with a copy that a bounded calculation is permitted to persist results into.
        internal static WorkingCopyIntegrity Create(string source, IEnumerable<string> explicitFamily, string category)
        {
            source = Path.GetFullPath(source);
            RejectLinks(source);
            if (!source.EndsWith(".stsw", StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
                throw new ArgumentException("An existing .stsw model is required.", nameof(source));
            // Exact filenames only; never enumerate the caller's directory.
            var files = RuntimeStageJournal.Call("SourceModelFamilyIdentified",
                () => (explicitFamily == null
                        ? new[] { source, source + ".sqlite", source + ".dwh" }
                        : explicitFamily.Select(Path.GetFullPath).ToArray())
                    .Where(File.Exists).ToArray(), value => value);
            if (explicitFamily != null && files.Length != explicitFamily.Count())
                throw new FileNotFoundException("An explicitly declared model family file is absent from the source copy.");
            var sourceBefore = RuntimeStageJournal.Call("SourceHashesCaptured", () => files.Select(Fingerprint).ToList(), value => value);
            string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", category ?? "ReadOnly", Guid.NewGuid().ToString("N"));
            RejectLinks(Path.GetDirectoryName(directory));
            Directory.CreateDirectory(directory);
            var copied = new List<FileFingerprint>();
            RuntimeStageJournal.Call("WorkingCopyFamilyCreated", () =>
            {
                foreach (string file in files) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)), false);
                return files.Select(file => Path.Combine(directory, Path.GetFileName(file))).ToArray();
            }, value => value);
            copied = RuntimeStageJournal.Call("WorkingCopyHashesCaptured", () => files.Select(file => Fingerprint(Path.Combine(directory, Path.GetFileName(file)))).ToList(), value => value);
            if (!sourceBefore.Select(item => item.SHA256).SequenceEqual(copied.Select(item => item.SHA256)))
                throw new IOException("Working copy differs from the source snapshot.");
            return new WorkingCopyIntegrity { SourceBefore = sourceBefore, WorkingBefore = copied,
                DirectoryPath = directory, ModelPath = Path.Combine(directory, Path.GetFileName(source)) };
        }
        // Places one declared external input (for example a terrain model the project references) into
        // an already-created working copy, at the location the model itself declares for it. The
        // destination is confined to the copy: a rooted path, a drive qualifier or any '..' segment is
        // refused rather than normalised, so a declared reference can never write outside the run root.
        // Nothing is overwritten and the copied bytes must match the verified source exactly.
        internal static string CopyDependency(string workingDirectory, string source, string relativeDestination)
        {
            if (string.IsNullOrWhiteSpace(relativeDestination))
                throw new InvalidDataException("A declared external input requires a relative destination.");
            string root = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string relative = relativeDestination.Replace('/', Path.DirectorySeparatorChar).Trim();
            if (relative.Length == 0 || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0 ||
                relative.Split(Path.DirectorySeparatorChar).Any(part => part == ".."))
                throw new InvalidDataException("A declared external input path may not leave the working copy.");
            string destination;
            try { destination = Path.GetFullPath(Path.Combine(root, relative)); }
            catch (Exception error) { throw new InvalidDataException("A declared external input path is malformed.", error); }
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A declared external input path may not leave the working copy.");
            if (!File.Exists(source)) throw new FileNotFoundException("A declared external input is absent.", source);
            RejectLinks(source);
            if (File.Exists(destination))
                throw new IOException("A declared external input would overwrite a file already in the working copy.");
            string parent = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(parent);
            RejectLinks(parent);
            File.Copy(source, destination, false);
            if (!string.Equals(Fingerprint(source).SHA256, Fingerprint(destination).SHA256, StringComparison.Ordinal))
                throw new IOException("A declared external input did not copy exactly.");
            return destination;
        }

        internal void Complete(ModelReadEvidence evidence)
        {
            evidence.SourceBefore = SourceBefore; evidence.WorkingBefore = WorkingBefore;
            evidence.WorkingCopyPath = ModelPath;
            try { evidence.WorkingAfter = RuntimeStageJournal.Call("WorkingCopyPostRunHashes", () => Snapshot(WorkingBefore), value => value); evidence.WorkingIntegrity = Equal(WorkingBefore, evidence.WorkingAfter); }
            catch (Exception error) { evidence.Exception = (evidence.Exception ?? "") + "\n" + error; }
            try { evidence.SourceAfter = RuntimeStageJournal.Call("SourceModelPostRunHashes", () => Snapshot(SourceBefore), value => value); evidence.SourceIntegrity = Equal(SourceBefore, evidence.SourceAfter); }
            catch (Exception error) { evidence.Exception = (evidence.Exception ?? "") + "\n" + error; }
            try
            {
                var before = WorkingBefore.Select(item => item.Path).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                var after = Directory.GetFileSystemEntries(DirectoryPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                evidence.TransientFiles = after.Except(before, StringComparer.OrdinalIgnoreCase).ToList();
                evidence.Diagnostics["WorkingInventoryBefore"] = RuntimeHostProtocol.Serializer().Serialize(before);
                evidence.Diagnostics["WorkingInventoryAfter"] = RuntimeHostProtocol.Serializer().Serialize(after);
                evidence.Diagnostics["WorkingInventoryAdded"] = RuntimeHostProtocol.Serializer().Serialize(evidence.TransientFiles);
                evidence.Diagnostics["WorkingInventoryRemoved"] = RuntimeHostProtocol.Serializer().Serialize(before.Except(after, StringComparer.OrdinalIgnoreCase).ToArray());
                if (evidence.TransientFiles.Count != 0) evidence.FailureCode = "P4_R9_BLOCKED_UNEXPECTED_WORKING_COPY_CHANGE";
            }
            catch (Exception error)
            { evidence.FailureCode = "IntegrityEvidenceFailure"; evidence.Exception = (evidence.Exception ?? "") + "\n" + error; }
            if (!evidence.SourceIntegrity) evidence.FailureCode = "HARD_FAIL_SOURCE_MODEL_MODIFIED";
            else if (!evidence.WorkingIntegrity) evidence.FailureCode = "P4_R9_BLOCKED_UNEXPECTED_WORKING_COPY_CHANGE";
            RuntimeStageJournal.Call("FinalIntegrityComparison", () => new { evidence.SourceIntegrity, evidence.WorkingIntegrity, evidence.TransientFiles }, value => value);
        }
    }
}
