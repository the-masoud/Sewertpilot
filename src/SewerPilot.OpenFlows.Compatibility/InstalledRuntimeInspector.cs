using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SewerPilot.OpenFlows.Compatibility
{
    public sealed class InstalledRuntimeInspector : IRuntimeCandidateInspector
    {
        private readonly Func<string, OpenFlowsAssemblyMetadata> metadata;
        public InstalledRuntimeInspector() : this(OpenFlowsMetadataInspector.Inspect) { }
        // R9 reuses the same bounded discovery with reflection-only inspection in
        // the caller's default domain; historical P3 entry points keep isolation.
        internal InstalledRuntimeInspector(Func<string, OpenFlowsAssemblyMetadata> metadata)
        { this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata)); }
        public IEnumerable<OpenFlowsRuntimeCandidate> Inspect(RuntimeLocation location,
            OpenFlowsDiscoveryOptions options, DiscoveryDiagnostics diagnostics)
        {
            var candidates = new List<OpenFlowsRuntimeCandidate>();
            string root;
            try { root = RuntimePaths.NormalizeRoot(location.Root); RuntimePaths.RejectLinks(root); }
            catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException)
            { diagnostics.Incomplete("Discovery root rejected: " + error.GetType().Name); return candidates; }
            if (!Directory.Exists(root)) return candidates;
            var pending = new Queue<Tuple<string, int>>();
            pending.Enqueue(Tuple.Create(root, 0));
            int visited = 0;
            while (pending.Count > 0)
            {
                if (++visited > options.MaximumDirectoriesPerRoot) { diagnostics.Incomplete("Directory bound reached for a discovery root."); break; }
                var next = pending.Dequeue();
                try
                {
                    RuntimePaths.RejectLinks(next.Item1);
                    string executable = Path.Combine(next.Item1, "SewerGEMS.exe");
                    if (File.Exists(executable))
                    {
                        candidates.Add(InspectCandidate(next.Item1, executable, location.Source));
                        // A product root is a candidate unit. Do not descend into models, lessons or other layouts.
                        continue;
                    }
                    if (next.Item2 >= options.MaximumDepth) continue;
                    foreach (string directory in Directory.EnumerateDirectories(next.Item1).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                        if (pending.Count + visited >= options.MaximumDirectoriesPerRoot)
                        { diagnostics.Incomplete("Directory enumeration bound reached for a discovery root."); break; }
                        pending.Enqueue(Tuple.Create(directory, next.Item2 + 1));
                    }
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException)
                { diagnostics.Incomplete("Directory discovery failed: " + error.GetType().Name); }
            }
            return candidates;
        }

        private OpenFlowsRuntimeCandidate InspectCandidate(string root, string executable, string source)
        {
            var assemblies = new List<OpenFlowsAssemblyMetadata>();
            var errors = new List<string>();
            OpenFlowsAssemblyMetadata product;
            try { product = metadata(executable); }
            catch (Exception error)
            {
                errors.Add("Executable metadata unavailable: " + error.GetType().Name);
                product = new OpenFlowsAssemblyMetadata(executable, null, null, null, null, null, null, null, null, 0);
            }
            foreach (string name in OpenFlowsRuntimeCandidate.CoreAssemblyNames)
            {
                string path = Path.Combine(root, name + ".dll");
                if (!File.Exists(path)) { errors.Add("Required core assembly missing: " + name); continue; }
                try
                {
                    var inspected = metadata(path);
                    if (inspected.SimpleName != name) errors.Add("Required assembly simple-name mismatch: " + name);
                    assemblies.Add(inspected);
                }
                catch (Exception error) { errors.Add("Core metadata unavailable: " + name + ": " + error.GetType().Name); }
            }
            return new OpenFlowsRuntimeCandidate(root, product, assemblies, new[] { source }, errors);
        }
    }
}
