using System;
using System.Collections.Generic;
using System.Linq;
using SewerPilot.OpenFlows.Contracts;

namespace SewerPilot.OpenFlows.Compatibility
{
    public static class OpenFlowsProfileFactory
    {
        public static OpenFlowsAdapterFamily SelectAdapterFamily(OpenFlowsRuntimeCandidate candidate, OpenFlowsBindingResult binding)
        {
            return candidate != null && candidate.IsStaticallyUsable && CoreLoadEstablished(candidate, binding)
                ? OpenFlowsAdapterFamily.TraditionalWaterObjectsNetFx : OpenFlowsAdapterFamily.None;
        }

        private static bool CoreLoadEstablished(OpenFlowsRuntimeCandidate candidate, OpenFlowsBindingResult binding) =>
            binding != null && binding.Success && binding.Loaded.Count == 2 && OpenFlowsRuntimeCandidate.CoreAssemblyNames.All(name =>
                binding.Loaded.Count(a => a.SimpleName == name && RuntimePaths.IsDirectFile(candidate.InstallRoot, a.Path) &&
                    candidate.Assemblies.Any(expected => expected.SimpleName == name && expected.SHA256 == a.SHA256)) == 1);

        public static OpenFlowsCompatibilityProfile Create(OpenFlowsRuntimeCandidate candidate, OpenFlowsBindingResult binding)
        {
            if (candidate == null || binding == null) throw new ArgumentNullException("candidate/binding");
            bool product = candidate.Executable.SimpleName == "SewerGEMS";
            bool present = OpenFlowsRuntimeCandidate.CoreAssemblyNames.All(name => candidate.Assemblies.Count(a => a.SimpleName == name) == 1);
            bool loaded = CoreLoadEstablished(candidate, binding);
            var evidence = new List<CapabilityEvidence>
            {
                Entry(CapabilityIdentifier.ProductDetected, product ? CapabilityState.Supported : CapabilityState.NotEstablished,
                    CapabilityEvidenceKind.StaticMetadata, "Executable identity inspected; no application or model opened.", string.Join("; ", candidate.Sources)),
                Entry(CapabilityIdentifier.WaterObjectsPresent, present ? CapabilityState.Supported : CapabilityState.NotEstablished,
                    CapabilityEvidenceKind.StaticMetadata, "Required core assembly identities inventoried; presence only.", "Selected candidate: " + candidate.CandidateId),
                Entry(CapabilityIdentifier.ManagedAssembliesLoad, loaded ? CapabilityState.Supported : CapabilityState.NotEstablished,
                    loaded ? CapabilityEvidenceKind.AssemblyLoad : CapabilityEvidenceKind.StaticMetadata,
                    loaded ? "Two identified core assemblies loaded in a temporary AppDomain, then unloaded." : "Core load has not been established.",
                    "Bounded core binding: " + candidate.CandidateId)
            };
            foreach (var capability in new[] { CapabilityIdentifier.ModelRead, CapabilityIdentifier.ScenarioRead,
                CapabilityIdentifier.ScenarioCreate, CapabilityIdentifier.AlternativeRead, CapabilityIdentifier.AlternativeCreate,
                CapabilityIdentifier.SolverRun, CapabilityIdentifier.ResultRead, CapabilityIdentifier.ModelWrite })
                evidence.Add(Entry(capability, CapabilityState.NotTested, CapabilityEvidenceKind.NotTested,
                    "Operational APIs were not invoked in P3.", "P3 runtime-only boundary."));
            return new OpenFlowsCompatibilityProfile(evidence, loaded ? CompatibilityClassification.Partial : CompatibilityClassification.InsufficientData,
                candidate.Executable.ProductName, candidate.Executable.ProductVersion, null, candidate.Executable.Architecture,
                new[] { "Licensing, native/model initialization and all engineering operations remain untested.",
                    "Temporary core loads do not establish a persistent model session or exclude vendor-internal loader effects." });
        }

        private static CapabilityEvidence Entry(CapabilityIdentifier id, CapabilityState state, CapabilityEvidenceKind kind,
            string reason, string provenance) => new CapabilityEvidence(id, state, kind, reason,
                string.IsNullOrWhiteSpace(provenance) ? "Candidate metadata inspection." : provenance);
    }
}
