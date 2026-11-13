using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Compatibility
{
    public sealed class OpenFlowsDiscoveryOptions
    {
        public OpenFlowsDiscoveryOptions(string overrideRoot = null, int maximumDepth = 4,
            int maximumDirectoriesPerRoot = 500, int maximumRegistryEntries = 2000)
        {
            if (maximumDepth < 0 || maximumDepth > 6 || maximumDirectoriesPerRoot < 1 ||
                maximumDirectoriesPerRoot > 1000 || maximumRegistryEntries < 1 || maximumRegistryEntries > 4000)
                throw new ArgumentOutOfRangeException("Discovery bounds exceed the allowed envelope.");
            OverrideRoot = overrideRoot;
            MaximumDepth = maximumDepth;
            MaximumDirectoriesPerRoot = maximumDirectoriesPerRoot;
            MaximumRegistryEntries = maximumRegistryEntries;
        }
        public string OverrideRoot { get; }
        public int MaximumDepth { get; }
        public int MaximumDirectoriesPerRoot { get; }
        public int MaximumRegistryEntries { get; }
    }

    public sealed class RuntimeLocation
    {
        public RuntimeLocation(string root, string source) { Root = root; Source = source; }
        public string Root { get; }
        public string Source { get; }
    }

    public interface IRuntimeLocationSource
    {
        IEnumerable<RuntimeLocation> Read(OpenFlowsDiscoveryOptions options, DiscoveryDiagnostics diagnostics);
    }
    public interface IRuntimeCandidateInspector
    {
        IEnumerable<OpenFlowsRuntimeCandidate> Inspect(RuntimeLocation location,
            OpenFlowsDiscoveryOptions options, DiscoveryDiagnostics diagnostics);
    }

    public sealed class DiscoveryDiagnostics
    {
        private readonly List<string> limitations = new List<string>();
        public bool Complete { get; private set; } = true;
        public IReadOnlyList<string> Limitations => limitations.AsReadOnly();
        public void Incomplete(string reason) { Complete = false; limitations.Add(reason); }
    }

    [Serializable]
    public sealed class OpenFlowsAssemblyMetadata
    {
        public OpenFlowsAssemblyMetadata(string path, string simpleName, string sha256,
            string assemblyVersion, string fileVersion, string productVersion, string imageRuntimeVersion,
            string targetFramework, string architecture, uint corFlags, string productName = null)
        {
            Path = path; SimpleName = simpleName; SHA256 = sha256; AssemblyVersion = assemblyVersion;
            FileVersion = fileVersion; ProductVersion = productVersion; ImageRuntimeVersion = imageRuntimeVersion;
            TargetFramework = targetFramework; Architecture = architecture; CorFlags = corFlags; ProductName = productName;
        }
        public string Path { get; }
        public string SimpleName { get; }
        public string SHA256 { get; }
        public string AssemblyVersion { get; }
        public string FileVersion { get; }
        public string ProductVersion { get; }
        public string ImageRuntimeVersion { get; }
        public string TargetFramework { get; }
        public string Architecture { get; }
        public uint CorFlags { get; }
        public string ProductName { get; }
    }

    public sealed class OpenFlowsRuntimeCandidate
    {
        public OpenFlowsRuntimeCandidate(string installRoot, OpenFlowsAssemblyMetadata executable,
            IEnumerable<OpenFlowsAssemblyMetadata> assemblies, IEnumerable<string> sources,
            IEnumerable<string> inspectionErrors = null)
        {
            InstallRoot = RuntimePaths.NormalizeRoot(installRoot);
            Executable = executable ?? throw new ArgumentNullException(nameof(executable));
            Assemblies = new ReadOnlyCollection<OpenFlowsAssemblyMetadata>((assemblies ?? throw new ArgumentNullException(nameof(assemblies))).ToList());
            Sources = new ReadOnlyCollection<string>((sources ?? throw new ArgumentNullException(nameof(sources))).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList());
            InspectionErrors = new ReadOnlyCollection<string>((inspectionErrors ?? Enumerable.Empty<string>()).ToList());
            using (var hash = SHA256.Create())
                CandidateId = "RT-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(InstallRoot.ToUpperInvariant()))).Replace("-", "");
        }
        public string CandidateId { get; }
        public string InstallRoot { get; }
        public OpenFlowsAssemblyMetadata Executable { get; }
        public IReadOnlyList<OpenFlowsAssemblyMetadata> Assemblies { get; }
        public IReadOnlyList<string> Sources { get; }
        public IReadOnlyList<string> InspectionErrors { get; }

        public bool IsStaticallyUsable => InspectionErrors.Count == 0 &&
            Executable.SimpleName == "SewerGEMS" && Executable.Architecture == "x64" &&
            IsNetFramework(Executable.TargetFramework) && RuntimePaths.IsDirectFile(InstallRoot, Executable.Path) &&
            CoreAssemblyNames.All(name => Assemblies.Count(a => a.SimpleName == name &&
                RuntimePaths.IsDirectFile(InstallRoot, a.Path) && IsNetFramework(a.TargetFramework) &&
                (a.Architecture == "AnyCPU" || a.Architecture == "x64")) == 1);

        internal static bool IsNetFramework(string target) => target != null &&
            target.StartsWith(".NETFramework,Version=", StringComparison.Ordinal);
        // Simple names are capability-family evidence, not exact vendor identity/version pins.
        public static IReadOnlyList<string> CoreAssemblyNames { get; } =
            Array.AsReadOnly(new[] { "Haestad.Support", "Haestad.Domain" });
    }

    public sealed class OpenFlowsDiscoveryResult
    {
        public OpenFlowsDiscoveryResult(IEnumerable<OpenFlowsRuntimeCandidate> candidates, DiscoveryDiagnostics diagnostics)
        {
            Candidates = Array.AsReadOnly(candidates.OrderBy(c => c.CandidateId, StringComparer.Ordinal).ToArray());
            Complete = diagnostics.Complete;
            Limitations = Array.AsReadOnly(diagnostics.Limitations.ToArray());
        }
        public IReadOnlyList<OpenFlowsRuntimeCandidate> Candidates { get; }
        public bool Complete { get; }
        public IReadOnlyList<string> Limitations { get; }
    }

    public enum OpenFlowsRuntimeSelectionStatus { NotFound, Selected, Ambiguous, InvalidOverride, IncompleteDiscovery }
    public enum OpenFlowsAdapterFamily { None, TraditionalWaterObjectsNetFx }

    public sealed class OpenFlowsRuntimeSelection
    {
        internal OpenFlowsRuntimeSelection(OpenFlowsRuntimeSelectionStatus status, OpenFlowsRuntimeCandidate candidate, string reason)
        { Status = status; Candidate = candidate; Reason = reason; }
        public OpenFlowsRuntimeSelectionStatus Status { get; }
        public OpenFlowsRuntimeCandidate Candidate { get; }
        public string Reason { get; }
    }

    internal static class RuntimePaths
    {
        internal static string NormalizeRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !System.IO.Path.IsPathRooted(root)) throw new ArgumentException("An absolute runtime directory is required.");
            string full = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            if (full.Length <= System.IO.Path.GetPathRoot(full).Length) throw new ArgumentException("A drive root is not a runtime directory.");
            return full;
        }
        internal static bool IsDirectFile(string root, string path)
        {
            try { return string.Equals(root, NormalizeRoot(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))), StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
        }
        internal static void RejectLinks(string path)
        {
            for (string cursor = System.IO.Path.GetFullPath(path); !string.IsNullOrEmpty(cursor); cursor = System.IO.Path.GetDirectoryName(cursor))
                if ((Directory.Exists(cursor) || File.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Runtime paths must not traverse filesystem links.");
        }
        internal static string HashFile(string path)
        {
            using (var hash = SHA256.Create()) using (var file = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "");
        }
    }
}
