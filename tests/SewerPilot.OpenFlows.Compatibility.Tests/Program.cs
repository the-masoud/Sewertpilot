using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Compatibility;
using SewerPilot.OpenFlows.Contracts;

namespace SewerPilot.OpenFlows.Compatibility.Tests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            var tests = new Tuple<string, Action>[]
            {
                Tuple.Create("Zero runtime candidates return NotFound", (Action)ZeroCandidates),
                Tuple.Create("One usable candidate is selected", (Action)OneCandidate),
                Tuple.Create("Equivalent candidates report ambiguity", (Action)Ambiguity),
                Tuple.Create("Explicit override resolves ambiguity", (Action)OverrideResolves),
                Tuple.Create("Invalid override has no fallback", (Action)InvalidOverride),
                Tuple.Create("Version does not rank runtimes", (Action)NoVersionRanking),
                Tuple.Create("Metadata cannot change compatibility outcome", (Action)MetadataNonauthority),
                Tuple.Create("Missing core assembly is unusable", (Action)MissingAssembly),
                Tuple.Create("Mismatched assembly identity is rejected", (Action)AssemblyNameMismatch),
                Tuple.Create("Operational capabilities stay untested after core evidence", (Action)UntestedCapabilities),
                Tuple.Create("Public projects have no vendor compile references", (Action)CompileReferenceBoundary),
                Tuple.Create("Discovery uses supplied generic roots", (Action)GenericDiscoveryRoots)
            };

            int failures = 0;
            foreach (var test in tests)
            {
                try { test.Item2(); Console.WriteLine("PASS: " + test.Item1); }
                catch (Exception error) { failures++; Console.WriteLine("FAIL: " + test.Item1 + ": " + error.Message); }
            }
            Console.WriteLine("COMPATIBILITY_TESTS: " + tests.Length);
            Console.WriteLine("COMPATIBILITY_ASSERTIONS: " + assertions);
            Console.WriteLine("COMPATIBILITY_FAILURES: " + failures);
            return failures == 0 ? 0 : 1;
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            assertions++;
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }

        private static void ZeroCandidates()
        {
            var result = Discover(new OpenFlowsRuntimeCandidate[0], null);
            var selected = new OpenFlowsRuntimeSelector().Select(result, new OpenFlowsDiscoveryOptions());
            Check(result.Candidates.Count == 0 && selected.Status == OpenFlowsRuntimeSelectionStatus.NotFound,
                "An empty, complete scan must not select a runtime.");
        }

        private static void OneCandidate()
        {
            var candidate = Candidate("C:\\P3Synthetic\\Primary", "24.x");
            var selection = new OpenFlowsRuntimeSelector().Select(Discover(new[] { candidate }, null), new OpenFlowsDiscoveryOptions());
            Check(selection.Status == OpenFlowsRuntimeSelectionStatus.Selected && selection.Candidate.CandidateId == candidate.CandidateId,
                "The sole statically usable candidate should be selected.");
        }

        private static void Ambiguity()
        {
            var candidates = new[] { Candidate("C:\\P3Synthetic\\Alpha", "1.0"), Candidate("C:\\P3Synthetic\\Beta", "9.9") };
            var selection = new OpenFlowsRuntimeSelector().Select(Discover(candidates, null), new OpenFlowsDiscoveryOptions());
            Check(selection.Status == OpenFlowsRuntimeSelectionStatus.Ambiguous && selection.Candidate == null,
                "Multiple usable candidates must require caller choice.");
        }

        private static void OverrideResolves()
        {
            var candidates = new[] { Candidate("C:\\P3Synthetic\\Alpha", "1.0"), Candidate("C:\\P3Synthetic\\Beta", "9.9") };
            var root = candidates[0].InstallRoot;
            var discovery = Discover(candidates, root);
            var selection = new OpenFlowsRuntimeSelector().Select(discovery, new OpenFlowsDiscoveryOptions(root));
            Check(discovery.Candidates.Count == 1 && selection.Status == OpenFlowsRuntimeSelectionStatus.Selected &&
                selection.Candidate.CandidateId == candidates[0].CandidateId,
                "An explicit valid override should select only its matching candidate.");
        }

        private static void InvalidOverride()
        {
            var candidates = new[] { Candidate("C:\\P3Synthetic\\Alpha", "1.0"), Candidate("C:\\P3Synthetic\\Beta", "2.0") };
            const string invalid = "relative\\runtime";
            var discovery = Discover(candidates, invalid);
            var selection = new OpenFlowsRuntimeSelector().Select(discovery, new OpenFlowsDiscoveryOptions(invalid));
            Check(selection.Status == OpenFlowsRuntimeSelectionStatus.InvalidOverride && selection.Candidate == null,
                "Invalid override syntax cannot silently fall back to an automatic candidate.");
        }

        private static void NoVersionRanking()
        {
            var lower = Candidate("C:\\P3Synthetic\\Lower", "1.0");
            var higher = Candidate("C:\\P3Synthetic\\Higher", "99.99");
            var first = new OpenFlowsRuntimeSelector().Select(Discover(new[] { lower, higher }, null), new OpenFlowsDiscoveryOptions());
            var second = new OpenFlowsRuntimeSelector().Select(Discover(new[] { higher, lower }, null), new OpenFlowsDiscoveryOptions());
            Check(first.Status == OpenFlowsRuntimeSelectionStatus.Ambiguous && second.Status == OpenFlowsRuntimeSelectionStatus.Ambiguous,
                "Order and version metadata cannot break a genuine tie.");
        }

        private static void MetadataNonauthority()
        {
            var first = Candidate("C:\\P3Synthetic\\Stable", "1.0");
            var otherVersion = Candidate("C:\\P3Synthetic\\Stable", "arbitrary");
            var bindingFirst = new OpenFlowsBindingResult(true, first.Assemblies);
            var bindingOther = new OpenFlowsBindingResult(true, otherVersion.Assemblies);
            var selector = new OpenFlowsRuntimeSelector();
            var selectedFirst = selector.Select(Discover(new[] { first }, null), new OpenFlowsDiscoveryOptions());
            var selectedOther = selector.Select(Discover(new[] { otherVersion }, null), new OpenFlowsDiscoveryOptions());
            var profileFirst = OpenFlowsProfileFactory.Create(first, bindingFirst);
            var profileOther = OpenFlowsProfileFactory.Create(otherVersion, bindingOther);
            Check(selectedFirst.Status == selectedOther.Status && profileFirst.Classification == profileOther.Classification &&
                profileFirst.Capabilities.All(item => profileOther.GetCapability(item.Key).State == item.Value.State),
                "The same path/capability evidence has the same outcome for arbitrary version text.");
        }

        private static void MissingAssembly()
        {
            var complete = Candidate("C:\\P3Synthetic\\Missing", "1.0");
            var partial = new OpenFlowsRuntimeCandidate(complete.InstallRoot, complete.Executable,
                complete.Assemblies.Where(item => item.SimpleName == "Haestad.Support"), new[] { "Synthetic" },
                new[] { "Required core assembly missing." });
            Check(!partial.IsStaticallyUsable && new OpenFlowsRuntimeSelector().Select(
                Discover(new[] { partial }, null), new OpenFlowsDiscoveryOptions()).Status == OpenFlowsRuntimeSelectionStatus.NotFound,
                "Presence of only one required core assembly is insufficient.");
        }

        private static void AssemblyNameMismatch()
        {
            var correct = Candidate("C:\\P3Synthetic\\Mismatch", "1.0");
            var assemblies = correct.Assemblies.Select(item => item.SimpleName == "Haestad.Support"
                ? Assembly("Haestad.Support.Other", correct.InstallRoot) : item).ToArray();
            var mismatch = new OpenFlowsRuntimeCandidate(correct.InstallRoot, correct.Executable, assemblies,
                new[] { "Synthetic" });
            Check(!mismatch.IsStaticallyUsable && new OpenFlowsRuntimeSelector().Select(
                Discover(new[] { mismatch }, null), new OpenFlowsDiscoveryOptions()).Status == OpenFlowsRuntimeSelectionStatus.NotFound,
                "Simple-name mismatch cannot establish the required assembly family.");
        }

        private static void UntestedCapabilities()
        {
            var candidate = Candidate("C:\\P3Synthetic\\Capability", "arbitrary");
            var binding = new OpenFlowsBindingResult(true, candidate.Assemblies);
            var profile = OpenFlowsProfileFactory.Create(candidate, binding);
            Check(profile.GetCapability(CapabilityIdentifier.ProductDetected).State == CapabilityState.Supported &&
                profile.GetCapability(CapabilityIdentifier.WaterObjectsPresent).State == CapabilityState.Supported &&
                profile.GetCapability(CapabilityIdentifier.ManagedAssembliesLoad).State == CapabilityState.Supported,
                "Static metadata and supplied core load evidence map to their three bounded capabilities.");
            var operational = new[] { CapabilityIdentifier.ModelRead, CapabilityIdentifier.ScenarioRead,
                CapabilityIdentifier.ScenarioCreate, CapabilityIdentifier.AlternativeRead, CapabilityIdentifier.AlternativeCreate,
                CapabilityIdentifier.SolverRun, CapabilityIdentifier.ResultRead, CapabilityIdentifier.ModelWrite };
            Check(operational.All(id => profile.GetCapability(id).State == CapabilityState.NotTested &&
                profile.GetCapability(id).Kind == CapabilityEvidenceKind.NotTested),
                "No runtime-binding evidence promotes model or engineering operations.");
        }

        private static void CompileReferenceBoundary()
        {
            var assemblies = new[] { typeof(OpenFlowsRuntimeCandidate).Assembly, typeof(IOpenFlowsRuntime).Assembly };
            Check(assemblies.All(assembly => assembly.GetReferencedAssemblies().All(reference =>
                reference.Name.IndexOf("Haestad", StringComparison.OrdinalIgnoreCase) < 0 &&
                reference.Name.IndexOf("Bentley", StringComparison.OrdinalIgnoreCase) < 0 &&
                reference.Name.IndexOf("WaterObjects", StringComparison.OrdinalIgnoreCase) < 0)),
                "Product and contract projects contain no compile-time vendor assembly reference.");
        }

        private static void GenericDiscoveryRoots()
        {
            var candidate = Candidate("C:\\P3Synthetic\\GenericRoot", "arbitrary-version");
            var source = new FakeSource(new[] { new RuntimeLocation(candidate.InstallRoot, "SyntheticSource") });
            var inspector = new FakeInspector(new[] { candidate });
            var result = new OpenFlowsRuntimeDiscovery(source, inspector).Discover(new OpenFlowsDiscoveryOptions());
            Check(source.Reads == 1 && inspector.Inspected == 1 && result.Candidates.Count == 1 &&
                new OpenFlowsRuntimeSelector().Select(result, new OpenFlowsDiscoveryOptions()).Status == OpenFlowsRuntimeSelectionStatus.Selected,
                "Discovery can work from a caller-provided generic root without a product-year path.");
        }

        private static OpenFlowsDiscoveryResult Discover(IEnumerable<OpenFlowsRuntimeCandidate> candidates, string overrideRoot)
        {
            var roots = candidates.Select(candidate => new RuntimeLocation(candidate.InstallRoot, "SyntheticSource")).ToArray();
            var source = new FakeSource(roots);
            var inspector = new FakeInspector(candidates);
            var options = new OpenFlowsDiscoveryOptions(overrideRoot);
            return new OpenFlowsRuntimeDiscovery(source, inspector).Discover(options);
        }

        private static OpenFlowsRuntimeCandidate Candidate(string root, string productVersion)
        {
            root = Path.GetFullPath(root);
            var executable = new OpenFlowsAssemblyMetadata(Path.Combine(root, "SewerGEMS.exe"), "SewerGEMS",
                "EXECUTABLE", "1.0.0.0", "1.0.0.0", productVersion, "v4.0.30319",
                ".NETFramework,Version=v4.8", "x64", 0, "Synthetic OpenFlows Product");
            return new OpenFlowsRuntimeCandidate(root, executable,
                new[] { Assembly("Haestad.Support", root), Assembly("Haestad.Domain", root) },
                new[] { "SyntheticSource" });
        }

        private static OpenFlowsAssemblyMetadata Assembly(string name, string root) => new OpenFlowsAssemblyMetadata(
            Path.Combine(root, name + ".dll"), name, "SYNTHETIC", "1.0.0.0", "1.0.0.0", "arbitrary",
            "v4.0.30319", ".NETFramework,Version=v4.8", "AnyCPU", 9);

        private sealed class FakeSource : IRuntimeLocationSource
        {
            private readonly RuntimeLocation[] locations;
            internal FakeSource(RuntimeLocation[] locations) { this.locations = locations; }
            internal int Reads { get; private set; }
            public IEnumerable<RuntimeLocation> Read(OpenFlowsDiscoveryOptions options, DiscoveryDiagnostics diagnostics)
            { Reads++; return locations; }
        }

        private sealed class FakeInspector : IRuntimeCandidateInspector
        {
            private readonly OpenFlowsRuntimeCandidate[] candidates;
            internal FakeInspector(IEnumerable<OpenFlowsRuntimeCandidate> candidates) { this.candidates = candidates.ToArray(); }
            internal int Inspected { get; private set; }
            public IEnumerable<OpenFlowsRuntimeCandidate> Inspect(RuntimeLocation location,
                OpenFlowsDiscoveryOptions options, DiscoveryDiagnostics diagnostics)
            {
                Inspected++;
                return candidates.Where(candidate => string.Equals(candidate.InstallRoot, location.Root, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
