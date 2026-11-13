using System.Collections.Generic;
using SewerPilot.OpenFlows.Contracts;

namespace SewerPilot.OpenFlows.Contracts.Tests;

internal static class P1CapabilityFixture
{
    internal static readonly CapabilityIdentifier[] Unperformed =
    {
        CapabilityIdentifier.ModelRead, CapabilityIdentifier.ScenarioRead,
        CapabilityIdentifier.ScenarioCreate, CapabilityIdentifier.AlternativeRead,
        CapabilityIdentifier.AlternativeCreate, CapabilityIdentifier.SolverRun,
        CapabilityIdentifier.ResultRead, CapabilityIdentifier.ModelWrite
    };

    internal static OpenFlowsCompatibilityProfile Create(string? version = null, string? build = null)
    {
        const string source = "docs/compatibility/P1_OPENFLOWS_RUNTIME_BASELINE.md: Capability Matrix";
        var evidence = new List<CapabilityEvidence>
        {
            new(CapabilityIdentifier.ProductDetected, CapabilityState.Supported,
                CapabilityEvidenceKind.StaticMetadata, "Installed product identity was observed.", source),
            new(CapabilityIdentifier.WaterObjectsPresent, CapabilityState.Supported,
                CapabilityEvidenceKind.StaticMetadata, "Core API presence was observed; presence only.", source),
            new(CapabilityIdentifier.ManagedAssembliesLoad, CapabilityState.Supported,
                CapabilityEvidenceKind.AssemblyLoad, "Two core assemblies loaded; no operation was exercised.", source)
        };
        foreach (var capability in Unperformed)
            evidence.Add(new CapabilityEvidence(capability, CapabilityState.NotTested,
                CapabilityEvidenceKind.NotTested, "This operation was not exercised in P1.", source));
        return new OpenFlowsCompatibilityProfile(evidence, CompatibilityClassification.Partial,
            "OpenFlows Sewer / SewerGEMS", version, build, "x64",
            new[] { "Only product presence and two core assembly loads were proven." });
    }
}
