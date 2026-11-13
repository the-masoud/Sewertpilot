using System;
using System.Collections.Generic;
using SewerPilot.OpenFlows.Contracts;

namespace SewerPilot.OpenFlows.Contracts.Tests;

// Test-only in-memory behavior. No engine, file I/O, discovery or assembly loading.
internal sealed class FakeOpenFlowsRuntime : IOpenFlowsRuntime, IOpenFlowsCapabilityProvider,
    IOpenFlowsModelReader, IOpenFlowsScenarioService, IOpenFlowsAlternativeService,
    IOpenFlowsCalculationRunner, IOpenFlowsResultReader, IOpenFlowsModelWriter
{
    internal FakeOpenFlowsRuntime(OpenFlowsCompatibilityProfile profile) => Profile = profile;
    public OpenFlowsCompatibilityProfile Profile { get; }
    public IOpenFlowsCapabilityProvider CapabilityProvider => this;
    public CapabilityEvidence GetCapability(CapabilityIdentifier capability) => Profile.GetCapability(capability);
    internal int OperationCount { get; private set; }

    private void Require(CapabilityIdentifier capability)
    {
        var evidence = GetCapability(capability);
        if (evidence.State != CapabilityState.Supported)
            throw new NotSupportedException(capability + ": " + evidence.State.ToReportValue());
        OperationCount++;
    }

    public IOpenFlowsModel Read(string source)
    {
        Require(CapabilityIdentifier.ModelRead);
        return new FakeModel(new ModelIdentity(source));
    }
    IReadOnlyList<ScenarioIdentity> IOpenFlowsScenarioService.Read(IOpenFlowsModel model)
    {
        Require(CapabilityIdentifier.ScenarioRead);
        return Array.Empty<ScenarioIdentity>();
    }
    ScenarioIdentity IOpenFlowsScenarioService.Create(IOpenFlowsModel model, string name)
    {
        Require(CapabilityIdentifier.ScenarioCreate);
        return new ScenarioIdentity(name);
    }
    IReadOnlyList<AlternativeIdentity> IOpenFlowsAlternativeService.Read(IOpenFlowsModel model)
    {
        Require(CapabilityIdentifier.AlternativeRead);
        return Array.Empty<AlternativeIdentity>();
    }
    AlternativeIdentity IOpenFlowsAlternativeService.Create(IOpenFlowsModel model, string name)
    {
        Require(CapabilityIdentifier.AlternativeCreate);
        return new AlternativeIdentity(name);
    }
    public CalculationRunIdentity Run(IOpenFlowsModel model, ScenarioIdentity scenario)
    {
        Require(CapabilityIdentifier.SolverRun);
        return new CalculationRunIdentity("fake-run");
    }
    public IReadOnlyList<ElementIdentity> ReadElements(IOpenFlowsModel model, CalculationRunIdentity run)
    {
        Require(CapabilityIdentifier.ResultRead);
        return Array.Empty<ElementIdentity>();
    }
    public void WriteCopy(IOpenFlowsModel model, string destination) => Require(CapabilityIdentifier.ModelWrite);
    public void Dispose() { }
}

internal sealed class FakeModel : IOpenFlowsModel
{
    internal FakeModel(ModelIdentity identity) => Identity = identity;
    public ModelIdentity Identity { get; }
    public void Dispose() { }
}
