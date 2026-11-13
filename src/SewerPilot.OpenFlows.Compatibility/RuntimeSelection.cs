using System;
using System.Collections.Generic;
using System.Linq;

namespace SewerPilot.OpenFlows.Compatibility
{
    public sealed class OpenFlowsRuntimeDiscovery
    {
        private readonly IRuntimeLocationSource source;
        private readonly IRuntimeCandidateInspector inspector;
        public OpenFlowsRuntimeDiscovery() : this(new WindowsRuntimeLocationSource(), new InstalledRuntimeInspector()) { }
        public OpenFlowsRuntimeDiscovery(IRuntimeLocationSource source, IRuntimeCandidateInspector inspector)
        { this.source = source ?? throw new ArgumentNullException(nameof(source)); this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector)); }

        public OpenFlowsDiscoveryResult Discover(OpenFlowsDiscoveryOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var diagnostics = new DiscoveryDiagnostics();
            var found = new List<OpenFlowsRuntimeCandidate>();
            if (!string.IsNullOrWhiteSpace(options.OverrideRoot))
            {
                try
                {
                    string root = RuntimePaths.NormalizeRoot(options.OverrideRoot);
                    found.AddRange(inspector.Inspect(new RuntimeLocation(root, "ExplicitOverride"),
                        new OpenFlowsDiscoveryOptions(root, 0, 1, options.MaximumRegistryEntries), diagnostics));
                }
                catch (ArgumentException) { diagnostics.Incomplete("Invalid override directory syntax."); }
            }
            else
                foreach (var location in source.Read(options, diagnostics).OrderBy(x => x.Root, StringComparer.OrdinalIgnoreCase))
                    found.AddRange(inspector.Inspect(location, options, diagnostics));
            var unique = found.GroupBy(c => c.InstallRoot, StringComparer.OrdinalIgnoreCase).Select(group =>
            {
                var first = group.First();
                return new OpenFlowsRuntimeCandidate(first.InstallRoot, first.Executable, first.Assemblies,
                    group.SelectMany(c => c.Sources), group.SelectMany(c => c.InspectionErrors));
            });
            return new OpenFlowsDiscoveryResult(unique, diagnostics);
        }
    }

    public sealed class OpenFlowsRuntimeSelector
    {
        public OpenFlowsRuntimeSelection Select(OpenFlowsDiscoveryResult discovery, OpenFlowsDiscoveryOptions options)
        {
            if (discovery == null || options == null) throw new ArgumentNullException("discovery/options");
            var usable = discovery.Candidates.Where(c => c.IsStaticallyUsable).ToArray();
            if (!string.IsNullOrWhiteSpace(options.OverrideRoot))
            {
                string root;
                try { root = RuntimePaths.NormalizeRoot(options.OverrideRoot); }
                catch (ArgumentException) { return Result(OpenFlowsRuntimeSelectionStatus.InvalidOverride, null, "Override is not a valid absolute directory."); }
                usable = usable.Where(c => string.Equals(c.InstallRoot, root, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (!discovery.Complete || usable.Length != 1)
                    return Result(OpenFlowsRuntimeSelectionStatus.InvalidOverride, null, "Override did not establish exactly one usable runtime; no fallback.");
            }
            else if (!discovery.Complete)
                return Result(OpenFlowsRuntimeSelectionStatus.IncompleteDiscovery, null, "Discovery coverage is incomplete; automatic selection cannot establish uniqueness.");
            if (usable.Length == 0) return Result(OpenFlowsRuntimeSelectionStatus.NotFound, null, "No statically usable core runtime was found.");
            if (usable.Length > 1) return Result(OpenFlowsRuntimeSelectionStatus.Ambiguous, null, "Multiple usable runtimes require an explicit install-root override.");
            return Result(OpenFlowsRuntimeSelectionStatus.Selected, usable[0], "Unique runtime with identified executable, core assembly names, framework and x64-compatible metadata; load validation still required.");
        }
        private static OpenFlowsRuntimeSelection Result(OpenFlowsRuntimeSelectionStatus status, OpenFlowsRuntimeCandidate candidate, string reason) =>
            new OpenFlowsRuntimeSelection(status, candidate, reason);
    }
}
