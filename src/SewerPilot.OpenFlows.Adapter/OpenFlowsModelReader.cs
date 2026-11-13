using System;
using System.Collections.Generic;
using System.Linq;
using SewerPilot.OpenFlows.Compatibility;
using SewerPilot.OpenFlows.Contracts;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class OpenFlowsModelReader : IOpenFlowsModelReader, IOpenFlowsRuntime, IOpenFlowsCapabilityProvider
    {
        private static readonly object ReadGate = new object();
        private bool disposed;
        public OpenFlowsModelReader() { Profile = EmptyProfile(); }
        public OpenFlowsCompatibilityProfile Profile { get; private set; }
        public IOpenFlowsCapabilityProvider CapabilityProvider => this;
        public CapabilityEvidence GetCapability(CapabilityIdentifier capability) => Profile.GetCapability(capability);
        public ModelReadResult ReadWithEvidence(string source)
        {
            lock (ReadGate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(OpenFlowsModelReader));
                var evidence = new ModelReadEvidence();
                var baseline = EmptyProfile();
                WorkingCopyIntegrity working = null;
                try
                {
                    var options = new OpenFlowsDiscoveryOptions(Environment.GetEnvironmentVariable("SEWERPILOT_OPENFLOWS_HOME"));
                    var discovery = RuntimeStageJournal.Call("P3RuntimeDiscovery", () =>
                        new OpenFlowsRuntimeDiscovery(new WindowsRuntimeLocationSource(),
                            new InstalledRuntimeInspector(path => new MetadataWorker().Inspect(path))).Discover(options),
                        value => new { CandidateCount = value.Candidates.Count });
                    var selection = RuntimeStageJournal.Call("RuntimeCandidateSelected", () => new OpenFlowsRuntimeSelector().Select(discovery, options));
                    evidence.Diagnostics["CandidateCount"] = discovery.Candidates.Count.ToString();
                    evidence.Diagnostics["Selection"] = selection.Status.ToString();
                    evidence.Diagnostics["VersionBasedSelection"] = "false";
                    if (selection.Status != OpenFlowsRuntimeSelectionStatus.Selected)
                    { evidence.FailureCode = "Runtime" + selection.Status; evidence.Exception = selection.Reason; }
                    else
                    {
                        working = WorkingCopyIntegrity.Create(source);
                        var outcome = new RuntimeHostClient().Read(RuntimeHostProtocol.Request(selection.Candidate, discovery.Candidates.Count, working.ModelPath));
                        evidence = outcome.Evidence;
                        baseline = ProcessProfile(selection.Candidate, outcome.Binding);
                        evidence.RuntimeSelected = true;
                        evidence.Diagnostics["CandidateCount"] = discovery.Candidates.Count.ToString();
                        evidence.Diagnostics["Selection"] = selection.Status.ToString();
                        evidence.Diagnostics["SelectedCandidate"] = selection.Candidate.CandidateId;
                        evidence.Diagnostics["SelectedRuntime"] = selection.Candidate.InstallRoot;
                        evidence.Diagnostics["DetectedProduct"] = selection.Candidate.Executable.ProductName;
                        evidence.Diagnostics["DetectedVersion"] = selection.Candidate.Executable.ProductVersion;
                        evidence.Diagnostics["DetectedBuild"] = selection.Candidate.Executable.FileVersion;
                        evidence.Diagnostics["DetectedArchitecture"] = selection.Candidate.Executable.Architecture;
                        evidence.Diagnostics["VersionBasedSelection"] = "false";
                    }
                }
                catch (Exception error) { evidence.FailureCode = "ModelReadFailure"; evidence.Exception = error.ToString(); }
                finally
                {
                    if (working != null)
                    {
                        evidence.SourceBefore = working.SourceBefore; evidence.WorkingBefore = working.WorkingBefore; evidence.WorkingCopyPath = working.ModelPath;
                        if (!evidence.Diagnostics.ContainsKey("RuntimeHostStarted") ||
                            (evidence.Diagnostics.ContainsKey("RuntimeHostProcessTerminated") && evidence.Diagnostics["RuntimeHostProcessTerminated"] == "true"))
                            working.Complete(evidence);
                    }
                }
                Profile = AcceptedProfile(baseline, evidence);
                return new ModelReadResult(evidence, Profile);
            }
        }
        IOpenFlowsModel IOpenFlowsModelReader.Read(string source)
        {
            if (Profile.GetCapability(CapabilityIdentifier.ModelRead).State != CapabilityState.Supported)
                throw new InvalidOperationException("Explicit successful ModelRead evidence is required; establish it through ReadWithEvidence.");
            var result = ReadWithEvidence(source);
            if (!result.Succeeded) throw new InvalidOperationException(result.Evidence.FailureCode);
            return result;
        }
        internal static OpenFlowsCompatibilityProfile AcceptedProfile(OpenFlowsCompatibilityProfile baseline, ModelReadEvidence evidence)
        {
            var entries = baseline.Capabilities.Values.Where(item =>
                !item.Capability.Equals(CapabilityIdentifier.ModelRead) &&
                !item.Capability.Equals(CapabilityIdentifier.ScenarioRead) &&
                !item.Capability.Equals(CapabilityIdentifier.ScenarioCreate) &&
                !item.Capability.Equals(CapabilityIdentifier.AlternativeRead) &&
                !item.Capability.Equals(CapabilityIdentifier.AlternativeCreate)).ToList();
            entries.Add(new CapabilityEvidence(CapabilityIdentifier.ModelRead,
                evidence.Accepted ? CapabilityState.Supported : CapabilityState.NotTested,
                evidence.Accepted ? CapabilityEvidenceKind.ModelRead : CapabilityEvidenceKind.NotTested,
                evidence.Accepted ? "First conduit ID read, closed, and all pre-existing hashes preserved; zero save/write calls." : "This operation did not establish ModelRead.",
                "P4 explicit model-read operation", DateTimeOffset.UtcNow));
            bool p6aProven = evidence.Accepted && evidence.Diagnostics != null && evidence.Diagnostics.ContainsKey("P6ACapabilityMapJson");
            entries.Add(new CapabilityEvidence(CapabilityIdentifier.ScenarioRead,
                p6aProven ? CapabilityState.Supported : CapabilityState.NotTested,
                p6aProven ? CapabilityEvidenceKind.RuntimeOperation : CapabilityEvidenceKind.NotTested,
                p6aProven ? "Model scenario collection, active scenario ID, and hierarchy read successfully from opened working copy." : "ScenarioRead not established.",
                "P6-A scenario read operation", DateTimeOffset.UtcNow));
            entries.Add(new CapabilityEvidence(CapabilityIdentifier.AlternativeRead,
                p6aProven ? CapabilityState.Supported : CapabilityState.NotTested,
                p6aProven ? CapabilityEvidenceKind.RuntimeOperation : CapabilityEvidenceKind.NotTested,
                p6aProven ? "Model alternative categories, counts, and active scenario assignments read successfully from opened working copy." : "AlternativeRead not established.",
                "P6-A alternative read operation", DateTimeOffset.UtcNow));

            bool p6bScenarioCreate = evidence.Accepted && evidence.Diagnostics != null &&
                evidence.Diagnostics.ContainsKey("P6BScenarioCreateProven") && evidence.Diagnostics["P6BScenarioCreateProven"] == "true";
            entries.Add(new CapabilityEvidence(CapabilityIdentifier.ScenarioCreate,
                p6bScenarioCreate ? CapabilityState.Supported : CapabilityState.NotTested,
                p6bScenarioCreate ? CapabilityEvidenceKind.RuntimeOperation : CapabilityEvidenceKind.NotTested,
                p6bScenarioCreate ? "Scenario created by copy and persisted in isolated design state." : "ScenarioCreate not established.",
                "P6-B scenario create operation", DateTimeOffset.UtcNow));

            bool p6bAltCreate = evidence.Accepted && evidence.Diagnostics != null &&
                evidence.Diagnostics.ContainsKey("P6BAlternativeCreateProven") && evidence.Diagnostics["P6BAlternativeCreateProven"] == "true";
            entries.Add(new CapabilityEvidence(CapabilityIdentifier.AlternativeCreate,
                p6bAltCreate ? CapabilityState.Supported : CapabilityState.NotTested,
                p6bAltCreate ? CapabilityEvidenceKind.RuntimeOperation : CapabilityEvidenceKind.NotTested,
                p6bAltCreate ? "Physical alternative created by copy, isolated to created scenario, and persisted." : "AlternativeCreate not established.",
                "P6-B alternative create operation", DateTimeOffset.UtcNow));

            return new OpenFlowsCompatibilityProfile(entries, baseline.Classification, baseline.ProductDisplayName,
                baseline.ProductVersion, baseline.ProductBuild, baseline.Architecture, baseline.Limitations);
        }
        private static OpenFlowsCompatibilityProfile ProcessProfile(OpenFlowsRuntimeCandidate candidate, OpenFlowsBindingResult binding)
        {
            var baseline = OpenFlowsProfileFactory.Create(candidate, binding);
            var entries = baseline.Capabilities.Values.Where(item => !item.Capability.Equals(CapabilityIdentifier.ManagedAssembliesLoad)).ToList();
            var load = baseline.GetCapability(CapabilityIdentifier.ManagedAssembliesLoad);
            entries.Add(new CapabilityEvidence(load.Capability, load.State, load.Kind,
                binding.Success ? "Two identified core assemblies loaded in the dedicated RuntimeHost default AppDomain." : "Core load has not been established.",
                "P4 one-shot process binding: " + candidate.CandidateId));
            return new OpenFlowsCompatibilityProfile(entries, baseline.Classification, baseline.ProductDisplayName,
                baseline.ProductVersion, candidate.Executable.FileVersion, baseline.Architecture,
                new[] { "Only the explicit bounded conduit ID read is eligible for ModelRead promotion.",
                    "Scenario, alternative, solver, result and write operations remain untested; licensing is outside scope." });
        }
        internal static OpenFlowsCompatibilityProfile EmptyProfile()
        {
            var entries = new List<CapabilityEvidence>();
            foreach (var id in new[] { CapabilityIdentifier.ProductDetected, CapabilityIdentifier.WaterObjectsPresent, CapabilityIdentifier.ManagedAssembliesLoad })
                entries.Add(new CapabilityEvidence(id, CapabilityState.NotEstablished, CapabilityEvidenceKind.NotEstablished, "Runtime not inspected.", "No read requested."));
            foreach (var id in new[] { CapabilityIdentifier.ModelRead, CapabilityIdentifier.ScenarioRead, CapabilityIdentifier.ScenarioCreate,
                CapabilityIdentifier.AlternativeRead, CapabilityIdentifier.AlternativeCreate, CapabilityIdentifier.SolverRun, CapabilityIdentifier.ResultRead, CapabilityIdentifier.ModelWrite })
                entries.Add(new CapabilityEvidence(id, CapabilityState.NotTested, CapabilityEvidenceKind.NotTested, "Operation untested.", "No read requested."));
            return new OpenFlowsCompatibilityProfile(entries, CompatibilityClassification.InsufficientData);
        }
        public void Dispose() { lock (ReadGate) { disposed = true; } }
    }
}
