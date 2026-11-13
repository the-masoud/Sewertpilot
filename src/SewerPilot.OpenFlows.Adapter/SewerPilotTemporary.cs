using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SewerPilot.OpenFlows.Adapter
{
    // Owns the two temporary boundaries the native host depends on and keeps them strictly apart.
    //
    //   * The TRUSTED root is where a controlled working copy is allowed to live. It is anchored to
    //     the user profile rather than to TEMP/TMP, so redirecting a child process's temporary
    //     environment can never move the boundary that validates working copies.
    //   * The ENGINE scratch root is private to exactly one host execution. It is never a valid
    //     working-copy location and can never stand in for one.
    //
    // Both live under the established SewerPilot temporary convention; only the category separates
    // them, and only the trusted categories are ever accepted as a model location.
    internal static class SewerPilotTemporary
    {
        internal const string Folder = "SewerPilot";
        internal const string Product = "OpenFlows";
        internal const string EngineCategory = "Engine";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetLongPathName(string shortPath, StringBuilder longPath, int bufferLength);

        // Windows reports TEMP with 8.3 segments on this platform while the profile anchor is a long
        // name, so two spellings of one directory are normal. Identity is therefore compared on the
        // long form and never on the raw string.
        internal static string Canonical(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string full = Path.GetFullPath(path);
            try
            {
                var buffer = new StringBuilder(1024);
                int length = GetLongPathName(full, buffer, buffer.Capacity);
                if (length > 0 && length < buffer.Capacity) full = Path.GetFullPath(buffer.ToString(0, length));
            }
            catch (Exception) { }
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        // The independent anchor. This is resolved from the operating system's per-user profile and is
        // never redirected by this application, so it is the only thing a carried root may be measured
        // against.
        internal static string Anchor()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(profile))
                throw new InvalidDataException("The application temporary anchor cannot be resolved on this workstation.");
            return Canonical(Path.Combine(profile, "Temp"));
        }

        // Resolves the root a controlled directory is measured against.
        //
        // With no carried root the process must have an intact temporary environment: if its own temp
        // no longer matches the anchor, the environment has been redirected and the boundary cannot be
        // established, so it fails closed instead of validating against the redirected location.
        //
        // A carried root is never trusted on its own. It must be rooted, present, free of reparse
        // points, and canonically identical to the anchor; anything else is refused.
        internal static string TrustedRoot(string carried)
        {
            string anchor = Anchor();
            if (string.IsNullOrWhiteSpace(carried))
            {
                string own = Path.GetTempPath();
                if (!string.Equals(Canonical(own), anchor, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "The trusted working boundary cannot be established in a process with a redirected temporary environment.");
                return own;
            }
            if (!Path.IsPathRooted(carried))
                throw new InvalidDataException("The carried trusted working root must be an absolute path.");
            string full = Path.GetFullPath(carried);
            WorkingCopyIntegrity.RejectLinks(full);
            if (!Directory.Exists(full))
                throw new InvalidDataException("The carried trusted working root is unavailable.");
            if (!string.Equals(Canonical(full), anchor, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The carried trusted working root is not the application temporary root.");
            return carried;
        }

        internal static string EngineRoot(string trustedRoot) =>
            Path.Combine(string.IsNullOrWhiteSpace(trustedRoot) ? Path.GetTempPath() : trustedRoot,
                Folder, Product, EngineCategory);

        // True when the path is the engine scratch root itself or anything beneath it. The engine
        // scratch tree is never a working-copy location, so this is checked explicitly as well as
        // through the category parent rule.
        internal static bool IsEngineScratch(string path, string trustedRoot)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string root;
            string full;
            try
            {
                root = Canonical(EngineRoot(trustedRoot));
                full = Canonical(path);
            }
            catch (Exception) { return false; }
            if (root == null || full == null) return false;
            return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        // Creates the single private scratch root for one host execution. The run identifier is the
        // only uniqueness key, so two concurrent events can never share a scratch tree and no model
        // name participates. An existing directory is a collision, never something to reuse.
        internal static string CreateEngineScratch(string runId, string trustedRoot)
        {
            if (!Regex.IsMatch(runId ?? "", "^[a-f0-9]{32}$"))
                throw new InvalidDataException("An engine scratch root requires a controlled run identifier.");
            string root = EngineRoot(trustedRoot);
            WorkingCopyIntegrity.RejectLinks(Path.GetDirectoryName(root));
            Directory.CreateDirectory(root);
            WorkingCopyIntegrity.RejectLinks(root);
            string scratch = Path.Combine(root, runId);
            if (Directory.Exists(scratch) || File.Exists(scratch))
                throw new IOException("Engine scratch root collision.");
            Directory.CreateDirectory(scratch);
            WorkingCopyIntegrity.RejectLinks(scratch);
            return scratch;
        }

        // A relative listing of what the vendor actually wrote into the scratch root. It is captured
        // before any cleanup so the redirected location is evidenced rather than merely asserted.
        internal static List<string> Inventory(string root)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return lines;
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                lines.Add(Relative(prefix, directory) + Path.DirectorySeparatorChar);
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                long bytes = -1;
                try { bytes = new FileInfo(file).Length; } catch (Exception) { }
                lines.Add(Relative(prefix, file) + "|" + bytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            lines.Sort(StringComparer.OrdinalIgnoreCase);
            return lines;
        }

        private static string Relative(string prefix, string path)
        {
            string full = Path.GetFullPath(path);
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(prefix.Length)
                : Path.GetFileName(full);
        }

        // Removes only the scratch root this execution created: the path must be exactly
        // <engine root>\<run id>, must not traverse a link, and the caller must already have reaped the
        // owned child process. Nothing outside that single directory is ever touched.
        internal static bool TryRemoveOwnedScratch(string scratch, string runId, string trustedRoot)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(scratch)) return false;
                string full = Canonical(scratch);
                string expected = Canonical(Path.Combine(EngineRoot(trustedRoot), runId ?? ""));
                if (!string.Equals(full, expected, StringComparison.OrdinalIgnoreCase)) return false;
                if (!Directory.Exists(full)) return false;
                WorkingCopyIntegrity.RejectLinks(full);
                Directory.Delete(full, true);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
