using System;
using System.IO;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum PathClassification
    {
        UnknownPath,
        ProtectedBaselinePath,
        WorkingCopyPath,
        TemporaryRuntimePath
    }

    public static class SafePathPolicy
    {
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));

            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new ArgumentException("Path contains invalid characters.", nameof(path));

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Path could not be normalized: " + ex.Message, nameof(path), ex);
            }

            RejectLinks(fullPath);

            return TrimTrailingSeparators(fullPath);
        }

        public static void RejectLinks(string fullPath)
        {
            for (string cursor = fullPath; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
            {
                if (File.Exists(cursor) || Directory.Exists(cursor))
                {
                    try
                    {
                        var attributes = File.GetAttributes(cursor);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            throw new IOException($"Path traverses a reparse point or link: {cursor}");
                    }
                    catch (IOException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        throw new IOException($"Unable to verify path attributes safely: {cursor}");
                    }
                }
            }
        }

        public static bool PathsEqual(string path1, string path2)
        {
            if (path1 == null || path2 == null) return false;
            string norm1 = Normalize(path1);
            string norm2 = Normalize(path2);
            return string.Equals(norm1, norm2, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSubpathOrSame(string basePath, string candidatePath)
        {
            string normBase = Normalize(basePath);
            string normCandidate = Normalize(candidatePath);

            if (string.Equals(normBase, normCandidate, StringComparison.OrdinalIgnoreCase))
                return true;

            string baseWithSep = normBase.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? normBase
                : normBase + Path.DirectorySeparatorChar;

            return normCandidate.StartsWith(baseWithSep, StringComparison.OrdinalIgnoreCase);
        }

        public static void ValidateWorkingCopyLocation(string baselineDirectory, string workingDirectory)
        {
            string normBaseline = Normalize(baselineDirectory);
            string normWorking = Normalize(workingDirectory);

            if (PathsEqual(normBaseline, normWorking))
                throw new InvalidOperationException("Working directory cannot be the same physical directory as baseline.");

            if (IsSubpathOrSame(normBaseline, normWorking))
                throw new InvalidOperationException("Working directory cannot be located inside the protected baseline directory.");

            if (IsSubpathOrSame(normWorking, normBaseline))
                throw new InvalidOperationException("Protected baseline directory cannot be located inside the working directory.");
        }

        public static string TrimTrailingSeparators(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string root = Path.GetPathRoot(path);
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
                return path;
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
