using System;
using System.Collections.Generic;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        // TEST_ONLY offline adaptations of the accepted P12/P13 fixtures. No approval or runtime invocation.
        private static GravityNetworkNode AutoNode(int id, GravityNodeKind kind = GravityNodeKind.Manhole, double? invert = 90,
            SourceUnitIdentity unit = null) => new GravityNetworkNode(id, kind, "TEST_ONLY_AUTO_" + id, null, null,
                new GravityElevationState(100, 100, invert, "TEST_ONLY", elevationUnit: unit ?? CreateFeetValue(0).Unit),
                propertyClassifications: new Dictionary<string, string> { { "InvertElevation", "OBSERVED_TEST_ONLY" } });
        private static GravityNetworkConduit AutoEdge(int id, int start, int stop) => new GravityNetworkConduit(id, "TEST_ONLY_AUTO_" + id,
            start, stop, new GravityConduitPhysicalState(length: 100, startInvert: 90, stopInvert: 90,
                lengthUnit: CreateFeetValue(0).Unit, startInvertUnit: CreateFeetValue(0).Unit, stopInvertUnit: CreateFeetValue(0).Unit));
        private static GravityNetworkSnapshot AutoNetwork(IEnumerable<GravityNetworkNode> nodes, params GravityNetworkConduit[] edges)
        {
            var accepted = CreateValidVerticalDesignNetwork(); var list = nodes.ToArray();
            var scenario = new GravityScenarioContext(accepted.ScenarioContext.ScenarioId, "TEST_ONLY_AUTO",
                accepted.ScenarioContext.TopologyAlternativeId, accepted.ScenarioContext.PhysicalAlternativeId, accepted.ScenarioContext.ActiveTopologyAlternativeId);
            return new GravityNetworkSnapshot(2, "TEST_ONLY_AUTO_MODEL", scenario, list, edges, null,
                GravityNetworkSnapshot.ComputeFingerprint(2, "TEST_ONLY_AUTO_MODEL", scenario, list, edges));
        }
        private static GravityNetworkSnapshot AutoTree() => AutoNetwork(new[] { AutoNode(101), AutoNode(102), AutoNode(103, GravityNodeKind.Outfall) },
            AutoEdge(201, 102, 101), AutoEdge(202, 102, 103));
        private static GravityAutomaticPreparationContext AutoContext(GravityNetworkSnapshot net, GravityVerticalDesignConstraintSet constraints = null,
            string policy = GravityAutomaticPreparationService.PolicyId, string version = GravityAutomaticPreparationService.PolicyVersion) =>
            new GravityAutomaticPreparationContext(net.SourceModelFingerprint, net.NetworkFingerprint, net.ScenarioContext, policy, version, constraints?.VerticalConstraintFingerprint);
        private static GravityAutomaticPreparationResult AutoPrepare(GravityNetworkSnapshot net, GravityVerticalDesignConstraintSet constraints = null) =>
            new GravityAutomaticPreparationService().Prepare(net, constraints, GravityAutomaticPreparationService.PolicyId,
                GravityAutomaticPreparationService.PolicyVersion, AutoContext(net, constraints));
        private static EngineeringDesignWorkspace AutoDraft()
        {
            var raw = CreateValidReadResultWithEndpoints(); raw.ScenarioContext.ActiveScenarioLabel = "TEST_ONLY_AUTO";
            foreach (var c in raw.Conduits) { c.Length = 100; c.LengthUnit = CreateFeetValue(0).Unit; c.Rise = 1.5; c.RiseUnit = c.LengthUnit; }
            var d = new EngineeringDesignWorkspace("TEST_ONLY_AUTO_PROJECT", new string('A', 64), raw); d.NewDraft("TEST_ONLY_AUTO_1"); return d;
        }
        private static GravityNetworkSnapshot AutoLive(EngineeringDesignWorkspace d) => new GravityNetworkIngestionService().Ingest(
            d.Inputs.SourceRead, d.Inputs.ModelFingerprint, schemaVersion: 2);
        private static GravityAutomaticPreparationResult AutoAccept(EngineeringDesignWorkspace d) => d.PrepareAutomaticDesign(
            GravityAutomaticPreparationService.PolicyId, GravityAutomaticPreparationService.PolicyVersion, AutoContext(AutoLive(d), d.Constraints));
        private static void AutoReason(GravityAutomaticPreparationResult result, string reason, params int[] conduits)
        {
            Check(result.Components.Any(c => conduits.All(id => c.ConduitIds.Contains(id)) && c.UnresolvedReasons.Any(r => r.StartsWith(reason, StringComparison.Ordinal))),
                "Exact unresolved component and affected IDs: " + reason);
        }

        private static void P13Auto_TreeAndProvenance()
        {
            var net = AutoTree(); var before = GravityExecutionPlanBuilder.Json(net); var result = AutoPrepare(net);
            Check(result.ContextFailures.Count == 0 && result.Components.Single().Eligible && result.DerivedOrientations.Count == 2, "Single-outfall tree eligible.");
            Check(result.DerivedOrientations[0].FromNodeId == 101 && result.DerivedOrientations[0].ToNodeId == 102 &&
                result.DerivedOrientations[1].FromNodeId == 102 && result.DerivedOrientations[1].ToNodeId == 103, "Actual endpoints route toward root, including reversed Start/Stop.");
            foreach (var o in result.DerivedOrientations)
            {
                var p = o.Provenance;
                Check(p.SourceLayer == StandardSourceLayer.ApplicationDefault && p.StandardId == GravityAutomaticPreparationService.PolicyId && p.StandardVersion == "1" &&
                    p.DocumentReference == null && p.Notes.Contains("DERIVED_DRAINAGE_ROUTING") && p.Notes.Contains(net.NetworkFingerprint) && p.Notes.Contains("RootOutfall=103") &&
                    p.Notes.Contains("Scenario=") && p.Notes.Contains("Topology=") && p.Notes.Contains("Start=") && p.Notes.Contains("Stop="), "Auditable application-policy provenance, no external authority.");
            }
            Check(before == GravityExecutionPlanBuilder.Json(net), "Pure derivation never edits snapshot.");
        }
        private static void P13Auto_BranchedAndIndependentComponents()
        {
            var net = AutoNetwork(new[] { AutoNode(101), AutoNode(102), AutoNode(103, GravityNodeKind.Outfall), AutoNode(104), AutoNode(111), AutoNode(112),
                AutoNode(121), AutoNode(122, GravityNodeKind.Outfall) }, AutoEdge(201, 102, 101), AutoEdge(202, 103, 102), AutoEdge(203, 104, 102),
                AutoEdge(211, 111, 112), AutoEdge(221, 122, 121));
            var r = AutoPrepare(net);
            Check(r.Components.Count == 3 && r.Components.Count(c => c.Eligible) == 2 && r.DerivedOrientations.Count == 4, "Independent branches/components evaluated separately.");
            Check(r.DerivedOrientations.Single(o => o.ConduitId == 203).ToNodeId == 102 && r.DerivedOrientations.Single(o => o.ConduitId == 221).ToNodeId == 122,
                "Each eligible tree follows its own outfall."); AutoReason(r, "NO_OUTFALL", 211);
        }
        private static void P13Auto_UnresolvedTopology()
        {
            var no = AutoPrepare(AutoNetwork(new[] { AutoNode(101), AutoNode(102) }, AutoEdge(201, 101, 102))); AutoReason(no, "NO_OUTFALL", 201);
            var many = AutoPrepare(AutoNetwork(new[] { AutoNode(101, GravityNodeKind.Outfall), AutoNode(102, GravityNodeKind.Outfall) }, AutoEdge(201, 101, 102)));
            AutoReason(many, "MULTIPLE_OUTFALLS", 201);
            var cycle = AutoPrepare(AutoNetwork(new[] { AutoNode(101), AutoNode(102), AutoNode(103, GravityNodeKind.Outfall) }, AutoEdge(201, 101, 102), AutoEdge(202, 102, 103), AutoEdge(203, 103, 101)));
            AutoReason(cycle, "CYCLIC_TOPOLOGY", 201, 202, 203);
            var parallel = AutoPrepare(AutoNetwork(new[] { AutoNode(101), AutoNode(102, GravityNodeKind.Outfall) }, AutoEdge(201, 101, 102), AutoEdge(202, 101, 102)));
            AutoReason(parallel, "CYCLIC_TOPOLOGY", 201, 202);
            var self = AutoPrepare(AutoNetwork(new[] { AutoNode(101, GravityNodeKind.Outfall) }, AutoEdge(201, 101, 101))); AutoReason(self, "CYCLIC_TOPOLOGY", 201);
            Check(new[] { no, many, cycle, parallel, self }.All(r => r.DerivedOrientations.Count == 0), "No arbitrary routing for rejected topology.");
        }
        private static void P13Auto_InvalidEndpointsAndExplicitConflicts()
        {
            foreach (int missing in new[] { 0, -1, 999 })
            {
                var r = AutoPrepare(AutoNetwork(new[] { AutoNode(101), AutoNode(103, GravityNodeKind.Outfall) }, AutoEdge(201, missing, 101), AutoEdge(202, 101, 103)));
                AutoReason(r, "INVALID_OR_UNRESOLVED_NODE", 201, 202); Check(r.DerivedOrientations.Count == 0, "Whole component unresolved with invalid endpoint.");
            }
            var duplicate = AutoPrepare(AutoNetwork(new[] { AutoNode(101), AutoNode(103, GravityNodeKind.Outfall) }, AutoEdge(201, 101, 103), AutoEdge(201, 101, 103)));
            AutoReason(duplicate, "INVALID_OR_DUPLICATE_CONDUIT", 201);
            var net = AutoTree(); var explicitDecision = new GravityVerticalDesignConstraintSetBuilder().SetOrientation(201, 102, 101, Ui02Provenance("TEST_ONLY.human" )).Build(net.NetworkFingerprint);
            string before = GravityExecutionPlanBuilder.Json(explicitDecision); var conflict = AutoPrepare(net, explicitDecision);
            AutoReason(conflict, "EXPLICIT_ORIENTATION_CONFLICT", 201, 202);
            Check(conflict.DerivedOrientations.Count == 0 && before == GravityExecutionPlanBuilder.Json(explicitDecision), "Never reverse existing human decision.");
            var disconnected = AutoNetwork(new[] { AutoNode(101), AutoNode(102), AutoNode(103, GravityNodeKind.Outfall), AutoNode(111), AutoNode(112, GravityNodeKind.Outfall) },
                AutoEdge(201, 102, 101), AutoEdge(202, 102, 103), AutoEdge(211, 111, 112));
            var conflicting = new GravityVerticalDesignConstraintSetBuilder().SetOrientation(201, 102, 101, Ui02Provenance("TEST_ONLY.human")).Build(disconnected.NetworkFingerprint);
            Check(AutoPrepare(disconnected, conflicting).DerivedOrientations.Single().ConduitId == 211, "Independent eligible component proceeds in pure report.");
        }
        private static void P13Auto_StaleContextsAndNoDefaultPolicy()
        {
            var net = AutoTree(); var service = new GravityAutomaticPreparationService();
            var none = service.Prepare(net, null, null, null, AutoContext(net)); Check(none.DerivedOrientations.Count == 0 && none.ContextFailures.Contains("EXPLICIT_SUPPORTED_POLICY_REQUIRED"), "No implicit policy activation.");
            var policy = service.Prepare(net, null, GravityAutomaticPreparationService.PolicyId, "1", AutoContext(net, version: "2"));
            Check(policy.ContextFailures.Contains("POLICY_CONTEXT_STALE") && policy.DerivedOrientations.Count == 0, "Stale active policy rejects requests.");
            var wrong = new GravityAutomaticPreparationContext("TEST_ONLY_OTHER", net.NetworkFingerprint, net.ScenarioContext, GravityAutomaticPreparationService.PolicyId, "1", null);
            Check(service.Prepare(net, null, GravityAutomaticPreparationService.PolicyId, "1", wrong).ContextFailures.Contains("SOURCE_CONTEXT_STALE"), "Model identity mismatch rejected.");
            var scenario = new GravityAutomaticPreparationContext(net.SourceModelFingerprint, net.NetworkFingerprint, new GravityScenarioContext(999, "TEST_ONLY_OTHER", 1, 2, 3), GravityAutomaticPreparationService.PolicyId, "1", null);
            Check(service.Prepare(net, null, GravityAutomaticPreparationService.PolicyId, "1", scenario).ContextFailures.Contains("SOURCE_CONTEXT_STALE"), "Scenario/topology context checked.");
            var stale = new GravityVerticalDesignConstraintSetBuilder().Build("TEST_ONLY_STALE_NETWORK"); var r = AutoPrepare(net, stale);
            Check(r.ContextFailures.Contains("CONSTRAINT_SOURCE_STALE") && r.DerivedOrientations.Count == 0, "Stale constraint source cannot derive.");
            var constraints = new GravityVerticalDesignConstraintSetBuilder().Build(net.NetworkFingerprint);
            Check(service.Prepare(net, constraints, GravityAutomaticPreparationService.PolicyId, "1", AutoContext(net)).ContextFailures.Contains("CONSTRAINT_CONTEXT_STALE"), "Current constraint fingerprint binds request.");
            var falsified = new GravityNetworkSnapshot(net.SchemaVersion, net.SourceModelFingerprint, net.ScenarioContext, net.Nodes, net.Conduits, null, "TEST_ONLY_FALSE_FINGERPRINT");
            Check(AutoPrepare(falsified).ContextFailures.Contains("SOURCE_CONTEXT_STALE"), "Snapshot content fingerprint recomputed.");
        }
        private static void P13Auto_OutfallObservationAndBoundaryAuthority()
        {
            var net = AutoTree(); var observed = AutoPrepare(net).Outfalls.Single();
            Check(observed.Classifications.Contains("SOURCE_VALUE_AVAILABLE") && observed.Classifications.Contains("BOUNDARY_UNRESOLVED") && !observed.Classifications.Contains("EXPLICIT_BOUNDARY_CONFIRMED"), "Source elevation is not boundary authority.");
            Check(observed.SourceElevations.InvertElevation == 90 && observed.SourceElevations.GroundElevation == 100 && observed.SourceElevations.RimElevation == 100 &&
                observed.SourceElevations.InvertElevationUnit.Equals(CreateFeetValue(0).Unit) && observed.PropertyClassifications["InvertElevation"] == "OBSERVED_TEST_ONLY" &&
                observed.ConnectedEndpoints.Single().Equals(new GravityConduitEnd(202, ConduitEndKind.Stop, 103)), "Actual values, units, classification and endpoint identity retained.");
            var free = new GravityVerticalDesignConstraintSetBuilder().AddOutfallConstraint(103, Ui02Provenance("TEST_ONLY.free")).Build(net.NetworkFingerprint);
            Check(AutoPrepare(net, free).Outfalls.Single().Classifications.Contains("BOUNDARY_UNRESOLVED"), "Empty/free declaration never supplies protected authority.");
            var fixedBoundary = new GravityVerticalDesignConstraintSetBuilder().AddOutfallConstraint(103, Ui02Provenance("TEST_ONLY.boundary"), CreateFeetValue(88)).Build(net.NetworkFingerprint);
            var confirmed = AutoPrepare(net, fixedBoundary).Outfalls.Single();
            Check(confirmed.Classifications.Contains("EXPLICIT_BOUNDARY_CONFIRMED") && confirmed.ExplicitBoundaries.Single().ProtectedInvert.NumericValue == 88 && confirmed.SourceElevations.InvertElevation == 90,
                "Explicit engineering boundary preserved independently of observed value.");
            var connected = new FixedEndpointElevationConstraint(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), CreateFeetValue(88), Ui02Provenance("TEST_ONLY.connected"));
            var linked = new GravityVerticalDesignConstraintSetBuilder().AddOutfallConstraint(103, Ui02Provenance("TEST_ONLY.boundary"), connectedEnd: connected).Build(net.NetworkFingerprint);
            Check(AutoPrepare(net, linked).Outfalls.Single().Classifications.Contains("EXPLICIT_BOUNDARY_CONFIRMED"), "Valid existing connected boundary confirmed.");
            var malformed = new GravityVerticalDesignConstraintSetBuilder().AddOutfallConstraint(103, Ui02Provenance("TEST_ONLY.boundary"), CreateFeetValue(double.NaN)).Build(net.NetworkFingerprint);
            Check(AutoPrepare(net, malformed).Outfalls.Single().Conflicts.Contains("EXPLICIT_BOUNDARY_INVALID"), "Invalid boundary quantities rejected.");
            var mismatch = AutoNetwork(new[] { AutoNode(101), AutoNode(103, GravityNodeKind.Outfall, 80) }, AutoEdge(201, 101, 103));
            Check(AutoPrepare(mismatch).Outfalls.Single().Classifications.Contains("SOURCE_CONFLICT"), "Node/endpoint observed invert conflict reported without inventing a boundary.");
            var absent = AutoNetwork(new[] { AutoNode(101), AutoNode(103, GravityNodeKind.Outfall, null) }, AutoEdge(201, 101, 103));
            Check(AutoPrepare(absent).Outfalls.Single().IncompleteItems.Contains("Invert_UNAVAILABLE"), "Missing source value explicitly reported.");
        }
        private static void P13Auto_AtomicDraftAndBasisIntegration()
        {
            var d = AutoDraft(); string before = d.Basis.CreateSnapshot().GravityContentFingerprint; var result = AutoAccept(d);
            Check(result.DerivedOrientations.Count == d.Inputs.SourceRead.Conduits.Count && d.Constraints.Orientations.Count == result.DerivedOrientations.Count, "Qualified proposals accepted into native constraints.");
            Check(d.Basis.CommonDesignBasis.ReviewState == DesignBasisReviewState.Draft && d.Inputs.Approval == null && before != d.Basis.CreateSnapshot().GravityContentFingerprint,
                "Basis fingerprint changes while review and authorization stay unresolved.");
            foreach (var o in d.Constraints.Orientations.Values)
            {
                var criterion = d.Basis.VerticalDesign.Criteria.Single(c => c.Key == o.Provenance.ParameterKey);
                Check(criterion.Value.Kind == StandardValueKind.Text && criterion.Value.TextValue == GravityExecutionPlanBuilder.Json(o) &&
                    GravityExecutionPlanBuilder.Json(criterion.Provenance) == GravityExecutionPlanBuilder.Json(o.Provenance), "Exact owned decision/provenance binding.");
            }
            Check(GravityExecutionPlanBuilder.ValidateEngineeringConstraints(d.Inputs, AutoLive(d)).VerticalConstraintFingerprint == d.Constraints.VerticalConstraintFingerprint,
                "Existing P15 basis-decision, topology and fingerprint validator accepts native binding.");
            var collision = AutoDraft(); var proposal = AutoPrepare(AutoLive(collision), collision.Constraints).DerivedOrientations.Last();
            collision.PutCriterion(GravityFlowSectionIdentifier.VerticalDesign, new GravityDesignCriterion { Key = proposal.Provenance.ParameterKey,
                Section = GravityFlowSectionIdentifier.VerticalDesign, Status = GravityCriterionStatus.Defined, Value = StandardValue.FromText("TEST_ONLY_COLLISION"), Provenance = Ui02Provenance("TEST_ONLY.unrelated") });
            string original = GravityExecutionPlanBuilder.Json(collision.Inputs); Ui02Reject(() => AutoAccept(collision), "DECISION_CRITERION_KEY_CONFLICT");
            Check(original == GravityExecutionPlanBuilder.Json(collision.Inputs) && collision.Constraints.Orientations.Count == 0, "Late criterion conflict rejects entire addition batch without partial stored mutations.");
            var conflict = AutoDraft(); var expected = AutoPrepare(AutoLive(conflict), conflict.Constraints).DerivedOrientations.First();
            conflict.SetOrientation(expected.ConduitId, expected.FromNodeId == conflict.End(expected.ConduitId, ConduitEndKind.Start).NodeId ? "Stop → Start" : "Start → Stop", Ui02Provenance("TEST_ONLY.explicit"));
            original = GravityExecutionPlanBuilder.Json(conflict.Inputs); Ui02Reject(() => AutoAccept(conflict), "AUTOMATIC_DECISION_CONFLICT");
            Check(original == GravityExecutionPlanBuilder.Json(conflict.Inputs), "Human conflict fails before stored changes.");
        }
        private static void P13Auto_ExplicitPreservationIdempotenceAndPolicyChange()
        {
            var d = AutoDraft(); var proposal = AutoPrepare(AutoLive(d), d.Constraints).DerivedOrientations.First();
            d.SetOrientation(proposal.ConduitId, proposal.FromNodeId == d.End(proposal.ConduitId, ConduitEndKind.Start).NodeId ? "Start → Stop" : "Stop → Start", Ui02Provenance("TEST_ONLY.human"));
            string human = GravityExecutionPlanBuilder.Json(d.Constraints.Orientations.Values.Single()); AutoAccept(d);
            Check(GravityExecutionPlanBuilder.Json(d.Constraints.Orientations.Values.Single(o => o.ConduitId == proposal.ConduitId)) == human, "Matching human orientation retained with original provenance.");
            string basis = d.Basis.CreateSnapshot().GravityContentFingerprint, constraint = d.Constraints.VerticalConstraintFingerprint;
            d.MarkSaved(); var repeated = AutoAccept(d);
            Check(repeated.DerivedOrientations.Count == 0 && !d.Dirty && basis == d.Basis.CreateSnapshot().GravityContentFingerprint && constraint == d.Constraints.VerticalConstraintFingerprint,
                "Identical acceptance is idempotent and adds no duplicate criteria.");
            var net = AutoTree(); Check(GravityExecutionPlanBuilder.Json(AutoPrepare(net)) == GravityExecutionPlanBuilder.Json(AutoPrepare(net)), "Pure result deterministic.");
            var reordered = new GravityNetworkSnapshot(net.SchemaVersion, net.SourceModelFingerprint, net.ScenarioContext, net.Nodes.Reverse(), net.Conduits.Reverse(), null, net.NetworkFingerprint);
            Check(GravityExecutionPlanBuilder.Json(AutoPrepare(net)) == GravityExecutionPlanBuilder.Json(AutoPrepare(reordered)), "Traversal independent of input enumeration.");
            d.Assess(); d.ChangeAutomaticPreparationPolicy(GravityAutomaticPreparationService.PolicyId, "2");
            Check(d.Constraints.Orientations.Count == 1 && GravityExecutionPlanBuilder.Json(d.Constraints.Orientations.Values.Single()) == human && d.Basis.VerticalDesign.Criteria.Count == 1 &&
                d.Feasibility == null && d.Candidates == null && d.Inputs.CandidateJson == null && d.Inputs.GenerationRequestJson == null && d.Inputs.Approval == null,
                "Explicit policy-version change invalidates owned derived decisions and dependent state, preserving human decision.");
            var input = GravityExecutionPlanBuilder.Json(d.Inputs);
            Ui02Reject(() => d.PrepareAutomaticDesign(GravityAutomaticPreparationService.PolicyId, "2", AutoContext(AutoLive(d), d.Constraints, version: "2")), "AUTOMATIC_CONTEXT_INVALID");
            Check(input == GravityExecutionPlanBuilder.Json(d.Inputs), "Unsupported policy cannot activate or edit state.");
        }
        private static void P13Auto_PersistenceAndSourceInvalidation()
        {
            var d = AutoDraft(); AutoAccept(d);
            var stored = RuntimeHostProtocol.Serializer().Deserialize<GravityEngineeringInputs>(GravityExecutionPlanBuilder.Json(d.Inputs));
            var restored = new EngineeringDesignWorkspace(d.Inputs.ProjectId, d.Inputs.ModelFingerprint, restored: stored);
            Check(!restored.HasLiveNetwork && restored.Constraints.VerticalConstraintFingerprint == d.Constraints.VerticalConstraintFingerprint &&
                restored.Basis.CreateSnapshot().GravityContentFingerprint == d.Basis.CreateSnapshot().GravityContentFingerprint, "Existing input/basis/wire roundtrip preserves derived content without live-source claim.");
            Ui02Reject(() => AutoAccept(restored), "LIVE_NETWORK_REQUIRED"); restored.BindNetwork(stored.SourceRead, stored.ModelFingerprint);
            Check(AutoAccept(restored).DerivedOrientations.Count == 0, "Matching reopen does not duplicate persisted derivation.");
            var changed = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(GravityExecutionPlanBuilder.Json(stored.SourceRead)); changed.Conduits[0].Length += 1;
            restored.Assess(); restored.BindNetwork(changed, stored.ModelFingerprint);
            Check(restored.SourceChanged && restored.Feasibility == null && restored.Inputs.CandidateJson == null, "Source change invalidates cached results.");
            Ui02Reject(() => AutoAccept(restored), "SOURCE_CHANGED"); Ui02Reject(() => restored.Assess(), "CURRENT_NETWORK_AND_DRAFT_REQUIRED");
            restored.NewDraft("TEST_ONLY_SOURCE_2"); Check(restored.Constraints.Orientations.Count == 0 && restored.Basis.VerticalDesign.Criteria.Count == 0, "Existing explicit revision removes stale source decisions and bindings.");
            var p = d.Constraints.Orientations.Values.First().Provenance; p.Notes += ";TEST_ONLY_TAMPER";
            AutoReason(AutoPrepare(AutoLive(d), d.Constraints), "DERIVED_CONTEXT_STALE", d.Inputs.SourceRead.Conduits.Select(c => c.Id).ToArray());
            Ui02Reject(() => d.Assess(), "AUTOMATIC_DECISIONS_STALE");
            Check(d.Feasibility == null && d.Inputs.CandidateJson == null, "Stale persisted derivation cannot supply P13/P14 state.");
        }
        private static void P13Auto_P13ConsumptionAndP14P15Gates()
        {
            var d = AutoDraft(); var original = d.Assess(); Check(original.DecisionRequests.Any(r => r.KnownFacts.ContainsKey("ConduitId")), "P13 originally requires directions.");
            AutoAccept(d); var f = d.Assess();
            Check(!f.DecisionRequests.Any(r => r.KnownFacts.ContainsKey("ConduitId")) && f.DecisionRequests.Any(r => r.KnownFacts.ContainsKey("OutfallNodeId")) &&
                f.Status == VerticalFeasibilityStatus.EngineeringDecisionRequired, "P13 consumes orientations while actual outfall blockers remain.");
            Check(!d.CanGenerate && d.Constraints.FixedConstraints.Count == 0 && d.Constraints.OutfallConstraints.Count == 0, "No source observations become fixed/boundary constraints or force feasibility.");
            Ui02Reject(() => d.GenerateCandidates(), "P13_GENERATION_GATE"); Ui02Reject(() => GravityExecutionPlanBuilder.BuildSingleOperator(d.Inputs, false), "DESIGN_BASIS");
            foreach (var n in AutoLive(d).Nodes.Where(n => n.Kind == GravityNodeKind.Outfall)) d.PutConstraint(new OutfallVerticalConstraint(n.SourceElementId, Ui02Provenance("TEST_ONLY.free." + n.SourceElementId)));
            foreach (var c in d.Inputs.SourceRead.Conduits)
                foreach (var end in new[] { ConduitEndKind.Start, ConduitEndKind.Stop }) d.PutConstraint(new FixedEndpointElevationConstraint(d.End(c.Id, end), CreateFeetValue(90), Ui02Provenance("TEST_ONLY.fixed." + c.Id + "." + end)));
            Check(d.Assess().Status == VerticalFeasibilityStatus.Feasible && d.GenerateCandidates().Candidates.Count == 1, "Existing P13/P14 numerical authorities permit exact TEST_ONLY profile only after explicit fixture constraints.");
            Check(d.Readiness().MissingOutfallDecisionNodeIds.Length > 0 && !d.Readiness().ProductionExecutionEligible && d.Inputs.Approval == null, "Free P13 outfall remains production-incomplete; no approval fabricated.");
            Ui02Reject(() => GravityExecutionPlanBuilder.BuildSingleOperator(d.Inputs, false), "DESIGN_BASIS");
            GravityExecutionPlanBuilder.ValidateEngineeringConstraints(d.Inputs, AutoLive(d));
            d.Inputs.DesignBasis.VerticalDesign.Criteria.First().Value = StandardValue.FromText("{}");
            Ui02Reject(() => GravityExecutionPlanBuilder.ValidateEngineeringConstraints(d.Inputs, AutoLive(d)), "CONSTRAINT_DESIGN_BASIS_DECISION");
        }
    }
}
