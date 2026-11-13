using System;
using System.Collections.Generic;

namespace SewerPilot.OpenFlows.Contracts
{
    // Implementations must report evidence and reject operations lacking Supported state.
    // Application callers can inspect capabilities before obtaining/invoking a service.
    public interface IOpenFlowsCapabilityProvider
    {
        OpenFlowsCompatibilityProfile Profile { get; }
        CapabilityEvidence GetCapability(CapabilityIdentifier capability);
    }

    public interface IOpenFlowsRuntime : IDisposable
    {
        IOpenFlowsCapabilityProvider CapabilityProvider { get; }
    }

    public interface IOpenFlowsModel : IDisposable
    {
        ModelIdentity Identity { get; }
    }

    public interface IOpenFlowsModelReader
    {
        // Requires ModelRead. No persistence or model mutation is authorized by Read.
        IOpenFlowsModel Read(string source);
    }

    public interface IOpenFlowsScenarioService
    {
        // Requires ScenarioRead.
        IReadOnlyList<ScenarioIdentity> Read(IOpenFlowsModel model);
        // Requires ScenarioCreate; changes working state, not permission to persist it.
        ScenarioIdentity Create(IOpenFlowsModel model, string name);
    }

    public interface IOpenFlowsAlternativeService
    {
        // Requires AlternativeRead.
        IReadOnlyList<AlternativeIdentity> Read(IOpenFlowsModel model);
        // Requires AlternativeCreate; changes working state, not permission to persist it.
        AlternativeIdentity Create(IOpenFlowsModel model, string name);
    }

    public interface IOpenFlowsCalculationRunner
    {
        // Requires SolverRun. P2 specifies no calculation options or hydraulic criteria.
        CalculationRunIdentity Run(IOpenFlowsModel model, ScenarioIdentity scenario);
    }

    public interface IOpenFlowsResultReader
    {
        // Requires ResultRead. Only identifies elements with results; no hydraulic schema yet.
        IReadOnlyList<ElementIdentity> ReadElements(IOpenFlowsModel model, CalculationRunIdentity run);
    }

    public interface IOpenFlowsModelWriter
    {
        // Requires ModelWrite. A later protection layer must validate the controlled destination,
        // prevent baseline overwrite, and collect before/after hashes before any real write.
        void WriteCopy(IOpenFlowsModel model, string destination);
    }
}
