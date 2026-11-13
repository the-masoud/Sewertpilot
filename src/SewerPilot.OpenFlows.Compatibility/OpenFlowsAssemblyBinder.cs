using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Compatibility
{
    [Serializable]
    public sealed class OpenFlowsBindingResult
    {
        public OpenFlowsBindingResult(bool success, IEnumerable<OpenFlowsAssemblyMetadata> loaded, string error = null)
        { Success = success; Loaded = Array.AsReadOnly(loaded.ToArray()); Error = error; }
        public bool Success { get; }
        public IReadOnlyList<OpenFlowsAssemblyMetadata> Loaded { get; }
        public string Error { get; }
    }

    public sealed class OpenFlowsAssemblyBinder
    {
        public OpenFlowsBindingResult Bind(OpenFlowsRuntimeSelection selection)
        {
            if (selection == null || selection.Status != OpenFlowsRuntimeSelectionStatus.Selected || selection.Candidate == null)
                return Failure("Exactly one selected candidate is required.");
            if (!Environment.Is64BitProcess || Environment.Version.Major != 4)
                return Failure("Core binding requires an x64 CLR 4 process.");
            AppDomain domain = null;
            try
            {
                var candidate = selection.Candidate;
                if (!candidate.IsStaticallyUsable) return Failure("Candidate did not establish the required static core boundary.");
                domain = AppDomain.CreateDomain("SewerPilot.CoreBinding", null,
                    new AppDomainSetup { ApplicationBase = Path.GetDirectoryName(typeof(OpenFlowsAssemblyBinder).Assembly.Location) });
                var worker = (CoreBindingWorker)domain.CreateInstanceAndUnwrap(typeof(CoreBindingWorker).Assembly.FullName, typeof(CoreBindingWorker).FullName);
                return worker.Bind(candidate.InstallRoot, candidate.Assemblies.ToArray());
            }
            catch (Exception error) { return Failure("Isolated core load failed: " + error.GetType().Name); }
            finally { if (domain != null) AppDomain.Unload(domain); }
        }
        private static OpenFlowsBindingResult Failure(string reason) => new OpenFlowsBindingResult(false, Array.Empty<OpenFlowsAssemblyMetadata>(), reason);
    }

    // Public solely for cross-AppDomain activation; no vendor object leaves this worker.
    public sealed class CoreBindingWorker : MarshalByRefObject
    {
        public override object InitializeLifetimeService() => null;
        public OpenFlowsBindingResult Bind(string root, OpenFlowsAssemblyMetadata[] inventory)
        {
            var loaded = new List<OpenFlowsAssemblyMetadata>();
            var paths = new Dictionary<string, OpenFlowsAssemblyMetadata>(StringComparer.Ordinal);
            ResolveEventHandler resolver = null;
            try
            {
                root = RuntimePaths.NormalizeRoot(root);
                foreach (string name in OpenFlowsRuntimeCandidate.CoreAssemblyNames)
                {
                    var entry = inventory.Single(a => a.SimpleName == name);
                    if (!RuntimePaths.IsDirectFile(root, entry.Path) || !File.Exists(entry.Path)) throw new IOException("Required file is outside the selected root or missing.");
                    RuntimePaths.RejectLinks(entry.Path);
                    if (AssemblyName.GetAssemblyName(entry.Path).Name != name) throw new BadImageFormatException("Required assembly simple-name mismatch.");
                    if (RuntimePaths.HashFile(entry.Path) != entry.SHA256) throw new IOException("Required core hash changed since discovery.");
                    paths.Add(name, entry);
                }
                resolver = (sender, args) =>
                {
                    string name = new AssemblyName(args.Name).Name;
                    OpenFlowsAssemblyMetadata entry;
                    if (!paths.TryGetValue(name, out entry)) return null;
                    if (RuntimePaths.HashFile(entry.Path) != entry.SHA256) throw new IOException("Core dependency changed before resolution.");
                    return Assembly.LoadFrom(entry.Path);
                };
                AppDomain.CurrentDomain.AssemblyResolve += resolver;
                foreach (string name in OpenFlowsRuntimeCandidate.CoreAssemblyNames)
                {
                    var expected = paths[name];
                    var assembly = Assembly.LoadFrom(expected.Path);
                    if (assembly.GetName().Name != name || !string.Equals(Path.GetFullPath(assembly.Location), Path.GetFullPath(expected.Path), StringComparison.OrdinalIgnoreCase))
                        throw new BadImageFormatException("Loaded identity/path does not match selected inventory.");
                    string hash = RuntimePaths.HashFile(assembly.Location);
                    if (hash != expected.SHA256) throw new IOException("Core file changed while loading.");
                    loaded.Add(new OpenFlowsAssemblyMetadata(assembly.Location, assembly.GetName().Name, hash,
                        assembly.GetName().Version.ToString(), expected.FileVersion, expected.ProductVersion,
                        assembly.ImageRuntimeVersion, expected.TargetFramework, expected.Architecture, expected.CorFlags));
                }
                var vendor = AppDomain.CurrentDomain.GetAssemblies().Where(a =>
                    !a.IsDynamic && (
                    a.GetName().Name.StartsWith("Haestad.", StringComparison.Ordinal) ||
                    a.GetName().Name.StartsWith("Bentley.", StringComparison.Ordinal) ||
                    a.GetName().Name.StartsWith("WaterObjects", StringComparison.Ordinal))).ToArray();
                if (vendor.Length != 2 || vendor.Any(a => !paths.ContainsKey(a.GetName().Name)))
                    throw new InvalidOperationException("Core load exceeded the two-assembly boundary.");
                return new OpenFlowsBindingResult(true, loaded);
            }
            catch (Exception error) { return new OpenFlowsBindingResult(false, loaded, "Core binding rejected: " + error.GetType().Name); }
            finally { if (resolver != null) AppDomain.CurrentDomain.AssemblyResolve -= resolver; }
        }
    }
}
