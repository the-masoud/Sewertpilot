using System;
using System.Collections.Generic;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        // Only offline TEST_ONLY fixtures. No review/approval fabrication, storage, GUI or model reader.
        private static EngineeringDesignWorkspace P3Draft(bool covers = true, bool slopes = true, int boundary = 1, double? secondRise = 1.5,
            double? maximumSlope = .04)
        {
            var d = AutoDraft();
            d.Inputs.SourceRead.Conduits.Single(c => c.Id == 202).Rise = secondRise;
            d.Inputs.SourceRead.Conduits.Single(c => c.Id == 202).RiseUnit = secondRise.HasValue ? CreateFeetValue(0).Unit : null;
            foreach (var structure in d.Inputs.SourceRead.Structures.Where(n => n.GroundElevation.HasValue))
            {
                structure.GroundElevationUnit = CreateFeetValue(0).Unit;
                structure.PropertyClassifications["GroundElevation"] = "PROVEN";
            }
            d.BindNetwork(d.Inputs.SourceRead, d.Inputs.ModelFingerprint);
            d.NewDraft("TEST_ONLY_AUTO03_1");
            d.SetIdentity("TEST_ONLY_AUTO03_BASIS", "TEST_ONLY_AUTO03_1"); AutoAccept(d);
            var net = AutoLive(d);
            foreach (var c in net.Conduits)
            {
                if (slopes) d.PutConstraint(new VerticalSlopeConstraint(c.SourceElementId, .01, maximumSlope, Ui02Provenance("TEST_ONLY.slope." + c.SourceElementId)));
                if (covers) foreach (var e in new[] { new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Start, c.StartNodeId),
                    new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Stop, c.StopNodeId) })
                    d.PutConstraint(new VerticalCoverConstraint(e, CreateFeetValue(100), CreateFeetValue(2), Ui02Provenance("TEST_ONLY.cover." + c.SourceElementId + "." + e.End)));
            }
            if (boundary != 0) d.PutConstraint(new OutfallVerticalConstraint(103, Ui02Provenance("TEST_ONLY.outfall"),
                boundary == 1 || boundary == 4 ? CreateFeetValue(90) : null,
                boundary == 3 || boundary == 4 ? new FixedEndpointElevationConstraint(new GravityConduitEnd(202, ConduitEndKind.Stop, 103),
                    CreateFeetValue(boundary == 4 ? 91 : 90), Ui02Provenance("TEST_ONLY.connected.boundary")) : null));
            return d;
        }
        private static GravityVerticalDesignProposalResolver.Result P3Resolve(EngineeringDesignWorkspace d, GravityNetworkSnapshot net = null,
            GravityVerticalDesignConstraintSet set = null, VerticalFeasibilityResult feasibility = null, GravityAutomaticPreparationContext context = null,
            string revision = "1", string basisFingerprint = null, GravityFlowDesignBasis basis = null)
        {
            net = net ?? AutoLive(d); set = set ?? d.Constraints; basis = basis ?? d.Basis;
            feasibility = feasibility ?? new GravityVerticalFeasibilityEngine().Analyze(net, set);
            context = context ?? AutoContext(net, set, GravityVerticalDesignProposalResolver.ObjectiveId, revision);
            return new GravityVerticalDesignProposalResolver().Resolve(net, basis, set, feasibility,
                GravityVerticalDesignProposalResolver.ObjectiveId, revision, context,
                basisFingerprint ?? GravityVerticalDesignProposalResolver.ContentFingerprint(basis));
        }
        private static double P3Value(GravityVerticalDesignProposalResolver.Result r, int id, ConduitEndKind kind) =>
            r.Endpoints.Single(e => e.ConduitId == id && e.End == kind).ProposedInvert;
        private static void P3Fixed(EngineeringDesignWorkspace d, int conduit, ConduitEndKind kind, int node, double invert) =>
            d.PutConstraint(new FixedEndpointElevationConstraint(new GravityConduitEnd(conduit, kind, node), CreateFeetValue(invert),
                Ui02Provenance("TEST_ONLY.fixed." + conduit + "." + kind), "TEST_ONLY_NON_ENGINEERING"));
        private static void P3Match(EngineeringDesignWorkspace d, VerticalMatchingMode mode) =>
            d.PutConstraint(new VerticalMatchConstraint(102, mode, new[] { new GravityConduitEnd(201, ConduitEndKind.Stop, 102),
                new GravityConduitEnd(202, ConduitEndKind.Start, 102) }, Ui02Provenance("TEST_ONLY.match." + mode)));
        private static void P3Blocked(GravityVerticalDesignProposalResolver.Result r, string status)
        {
            Check(r.Status == status, "Exact proposal rejection: " + status + "; actual=" + r.Status + "; " + string.Join("; ", r.Reasons));
            Check(r.Endpoints.Count == 0 && r.CandidateFingerprint == null && r.ProposalFingerprint == null && r.P14InvocationCount == 0,
                "Blocked input never reaches P14 materialization or emits values/identities.");
        }

        // Group 1: exact native P13/P14 materialization, unchanged values and fingerprint.
        private static void P13Auto03_ExactP14Materialization()
        {
            var d = P3Draft(); P3Fixed(d, 201, ConduitEndKind.Start, 101, 95); P3Fixed(d, 201, ConduitEndKind.Stop, 102, 94);
            P3Fixed(d, 202, ConduitEndKind.Start, 102, 94); P3Fixed(d, 202, ConduitEndKind.Stop, 103, 90);
            var net = AutoLive(d); var f = new GravityVerticalFeasibilityEngine().Analyze(net, d.Constraints); var r = P3Resolve(d, feasibility: f);
            Check(f.Status == VerticalFeasibilityStatus.Feasible && r.Status == "PROPOSED", "Exact profile proposed.");
            Check(r.P13InvocationCount == 1 && r.P14InvocationCount == 1 && r.RelaxationPasses == 0, "One P13 recomputation and one P14 call; no exact-value relaxation.");
            Check(r.CandidateRecipeId == "EXACT_CONSTRAINT_PROFILE" && r.Recipe.EndpointSelections.Count == 0, "Established P14 exact path used, no invented endpoint selections.");
            Check(r.Endpoints.All(e => e.SelectionSource == CandidateSelectionSource.ExactConstraint && e.ProposedInvert == f.EndpointRanges[e.ConduitEnd.ToString()].ExactInvert), "Every exact invert preserved.");
            var oracle = new GravityCandidateGenerator().Generate(net, d.Constraints, f, new GravityCandidateGenerationRequest(d.Constraints.VerticalConstraintFingerprint, null));
            Check(oracle.Candidates.Single().CandidateFingerprint == r.CandidateFingerprint, "Unchanged P14 exact fingerprint matches direct materialization.");
            Check(r.Provenance.StandardId == GravityVerticalDesignProposalResolver.ObjectiveId && r.Provenance.DocumentReference == null, "Objective policy provenance, no external standard or approval claim.");
        }

        // Group 2: analytically bounded greatest vector, not existing inverts or heuristic choices.
        private static void P13Auto03_BoundedGreatestVector()
        {
            var d = P3Draft(); var r = P3Resolve(d);
            Check(r.Status == "PROPOSED" && r.Endpoints.Count == 4, "Bounded multiple profile proposed.");
            Check(P3Value(r, 201, ConduitEndKind.Start) == 96.5 && P3Value(r, 201, ConduitEndKind.Stop) == 95.5 &&
                P3Value(r, 202, ConduitEndKind.Start) == 94 && P3Value(r, 202, ConduitEndKind.Stop) == 90, "Greatest profile follows cover, minimum drop and explicit outfall/maximum drop bounds.");
            Check(r.P13InvocationCount == 1 && r.P14InvocationCount == 1 && r.RelaxationPasses <= r.Endpoints.Count - 1, "Graph relaxation bounded by V-1; exactly one materialization.");
            Check(r.Recipe.EndpointSelections.Count == 4 && r.Recipe.EndpointSelections.All(e => e.Mode == EndpointSelectionMode.ExplicitValue && e.ExplicitElevation.Unit.Equals(CreateFeetValue(0).Unit)), "Every nonexact endpoint uses explicit canonical Feet values.");
            var source = AutoLive(d);
            Check(r.DerivedConduits.All(c => c.Slope >= .01 && c.Slope <= .04 && c.StartCover >= 2 &&
                (source.Nodes.Single(n => n.SourceElementId == c.ToNodeId).ExistingElevationState.GroundElevation.HasValue
                    ? c.StopCover >= 2 : !c.StopCover.HasValue)), "Native slopes and available covers satisfy constraints; missing outfall ground yields no invented cover.");
            Check(r.CandidateFingerprint.Length == 64 && r.ProposalFingerprint.Length == 64 && r.CandidateFingerprint != r.ProposalFingerprint,
                "Objective proposal and P14 candidate identities are separate.");
            Console.WriteLine("P13_AUTO03_EXAMPLE=" + GravityExecutionPlanBuilder.Json(new { r.Status, r.Objective, r.ObjectiveRevision,
                r.GeneratorVersion, r.NetworkFingerprint, r.DesignBasisContentFingerprint, r.ConstraintFingerprint, r.ConstraintContentFingerprint,
                r.FeasibilityIdentity, r.CandidateFingerprint, r.ProposalFingerprint, r.CandidateRecipeId, r.Endpoints, r.DerivedConduits,
                r.P13InvocationCount, r.P14InvocationCount, r.RelaxationPasses, TestOnly = true }));
        }

        // Group 3: independent P13 maxima violate the separately explicit boundary/max-drop coupling.
        private static void P13Auto03_CoupledMinimumMaximum()
        {
            var d = P3Draft(); P3Match(d, VerticalMatchingMode.InvertMatch);
            var f = new GravityVerticalFeasibilityEngine().Analyze(AutoLive(d), d.Constraints);
            double independentFrom = f.EndpointRanges[new GravityConduitEnd(202, ConduitEndKind.Start, 102).ToString()].MaximumAllowedInvert.Value;
            Check((independentFrom - 90) / 100 > .04, "Independent P13 endpoint maximum violates explicit outfall plus maximum slope.");
            var r = P3Resolve(d, feasibility: f);
            Check(r.Status == "PROPOSED" && P3Value(r, 201, ConduitEndKind.Start) == 96.5 && P3Value(r, 201, ConduitEndKind.Stop) == 94 &&
                P3Value(r, 202, ConduitEndKind.Start) == 94 && P3Value(r, 202, ConduitEndKind.Stop) == 90, "Coupled greatest vector respects both drops and equality.");
            Check(r.DerivedConduits.Single(c => c.ConduitId == 201).Slope == .025 && r.DerivedConduits.Single(c => c.ConduitId == 202).Slope == .04, "Min/max slope semantics stay ratios, no percent conversion.");
            Check(r.Evidence.Any(e => e.Contains("MinimumSlope:")) && r.Evidence.Any(e => e.Contains("MaximumSlope:")), "Both inequality directions recorded.");
            Check(r.P14InvocationCount == 1, "No alternative/retry after authoritative candidate acceptance.");
        }

        // Group 4: actual Rise offsets, invert equality, fixed-value preservation, unsupported meanings fail closed.
        private static void P13Auto03_MatchesAndFixedValues()
        {
            var invert = P3Draft(); P3Match(invert, VerticalMatchingMode.InvertMatch); P3Fixed(invert, 201, ConduitEndKind.Start, 101, 95);
            var a = P3Resolve(invert);
            Check(a.Status == "PROPOSED" && P3Value(a, 201, ConduitEndKind.Start) == 95 && P3Value(a, 201, ConduitEndKind.Stop) == P3Value(a, 202, ConduitEndKind.Start), "InvertMatch and fixed endpoint preserved.");
            Check(!a.Recipe.EndpointSelections.Any(e => e.ConduitEnd.ConduitId == 201 && e.ConduitEnd.End == ConduitEndKind.Start), "P13 exact endpoint never overwritten by explicit selection.");
            var crown = P3Draft(secondRise: 2.5); P3Match(crown, VerticalMatchingMode.CrownMatch); P3Fixed(crown, 201, ConduitEndKind.Start, 101, 96);
            var b = P3Resolve(crown);
            Check(b.Status == "PROPOSED" && P3Value(b, 201, ConduitEndKind.Stop) == 95 && P3Value(b, 202, ConduitEndKind.Start) == 94, "Unequal Rise yields different inverts and equal crowns.");
            Check(b.DerivedConduits.Single(c => c.ConduitId == 201).StopCrown == b.DerivedConduits.Single(c => c.ConduitId == 202).StartCrown, "Native P14 crowns match.");
            var protectedNode = P3Draft(); protectedNode.PutConstraint(new ProtectedNodeElevationConstraint(102, "Invert", CreateFeetValue(94), Ui02Provenance("TEST_ONLY.protected")));
            P3Blocked(P3Resolve(protectedNode), "UNSUPPORTED_CONSTRAINT");
            var wrongUnit = P3Draft(); wrongUnit.Inputs.SourceRead.Conduits[0].RiseUnit = new SourceUnitIdentity("length", "Meters", "meters", "m");
            var changed = AutoLive(wrongUnit);
            // Rebind the same explicit fixture decisions to the changed source; unsupported Rise units remain unsupported.
            var set = new GravityVerticalDesignConstraintSet(changed.NetworkFingerprint, wrongUnit.Constraints.DesignBasisId, wrongUnit.Constraints.DesignBasisVersion,
                wrongUnit.Constraints.Orientations.Values.ToDictionary(o => o.ConduitId), wrongUnit.Constraints.FixedConstraints, null, wrongUnit.Constraints.OutfallConstraints,
                null, wrongUnit.Constraints.CoverConstraints, wrongUnit.Constraints.SlopeConstraints);
            P3Blocked(P3Resolve(wrongUnit, net: changed, set: set), "UNSUPPORTED_SOURCE_QUANTITY");
        }

        // Group 5: cover and both supported explicit outfall bindings, no empty/free-outfall promotion.
        private static void P13Auto03_CoverAndOutfallBoundaries()
        {
            var d = P3Draft(boundary: 3);
            d.PutConstraint(new VerticalCoverConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(100), CreateFeetValue(4), Ui02Provenance("TEST_ONLY.additional.cover")));
            var r = P3Resolve(d);
            Check(r.Status == "PROPOSED" && P3Value(r, 201, ConduitEndKind.Start) == 94.5 && P3Value(r, 202, ConduitEndKind.Stop) == 90, "Stricter GroundToCrown cover and explicit connected endpoint enforced.");
            Check(r.DerivedConduits.Single(c => c.ConduitId == 201).StartCover == 4, "Available native cover preserved in Feet.");
            Check(r.Evidence.Any(e => e.StartsWith("Explicit connected outfall")) && r.Evidence.Any(e => e.StartsWith("GroundToCrown:")), "Actual boundary/cover evidence retained.");
            var protectedOnly = P3Resolve(P3Draft()); Check(protectedOnly.Status == "PROPOSED" && P3Value(protectedOnly, 202, ConduitEndKind.Stop) == 90, "Single incident protected outfall invert binds unambiguously.");
            P3Blocked(P3Resolve(P3Draft(boundary: 4)), "BOUND_OR_EXACT_VIOLATION");
        }

        // Group 6: no proposal for missing authority, infeasible/incomplete, unbounded or negative-cycle inputs.
        private static void P13Auto03_BlockedAndUnboundedProfiles()
        {
            P3Blocked(P3Resolve(P3Draft(boundary: 0)), "P13_ENGINEERINGDECISIONREQUIRED");
            P3Blocked(P3Resolve(P3Draft(boundary: 2)), "MISSING_BOUNDARY_AUTHORITY");
            var infeasible = P3Draft(); P3Fixed(infeasible, 201, ConduitEndKind.Start, 101, 90); P3Fixed(infeasible, 201, ConduitEndKind.Stop, 102, 94);
            P3Blocked(P3Resolve(infeasible), "P13_INFEASIBLE");
            P3Blocked(P3Resolve(P3Draft(secondRise: null)), "P13_INCOMPLETE");
            var unbounded = P3Resolve(P3Draft(covers: false, slopes: false)); P3Blocked(unbounded, "UNBOUNDED_ABOVE");
            Check(unbounded.Reasons.Single().Contains("201") && unbounded.Reasons.Single().Contains("202 (Start"), "Exact unbounded endpoints reported; no numeric sentinel fallback.");
            P3Blocked(P3Resolve(P3Draft(covers: false, maximumSlope: null)), "UNBOUNDED_ABOVE");

            // Small contradictory cycle is below P13's propagation threshold; the bounded witness still detects it.
            var d = P3Draft(); var net = AutoNetwork(new[] { AutoNode(101), AutoNode(102) }, AutoEdge(201, 101, 102), AutoEdge(202, 102, 101));
            var builder = new GravityVerticalDesignConstraintSetBuilder();
            foreach (var c in net.Conduits) { builder.SetOrientation(c.SourceElementId, c.StartNodeId, c.StopNodeId, Ui02Provenance("TEST_ONLY.cycle.direction." + c.SourceElementId));
                builder.AddSlopeConstraint(c.SourceElementId, 1e-9, null, Ui02Provenance("TEST_ONLY.cycle.slope." + c.SourceElementId)); }
            builder.AddMatchConstraint(101, VerticalMatchingMode.InvertMatch, new[] { new GravityConduitEnd(201, ConduitEndKind.Start, 101), new GravityConduitEnd(202, ConduitEndKind.Stop, 101) }, Ui02Provenance("TEST_ONLY.cycle.match.101"));
            builder.AddMatchConstraint(102, VerticalMatchingMode.InvertMatch, new[] { new GravityConduitEnd(201, ConduitEndKind.Stop, 102), new GravityConduitEnd(202, ConduitEndKind.Start, 102) }, Ui02Provenance("TEST_ONLY.cycle.match.102"));
            var set = builder.Build(net.NetworkFingerprint, d.Basis.CommonDesignBasis.DesignBasisId, d.Basis.CommonDesignBasis.DesignBasisVersion);
            var basis = GravityFlowDesignBasisSerializer.Deserialize(GravityFlowDesignBasisSerializer.Serialize(d.Basis)); basis.VerticalDesign.Criteria.Clear();
            foreach (var decision in GravityExecutionPlanBuilder.ConstraintDecisions(set))
            {
                var p = (StandardProvenance)decision.GetType().GetProperty("Provenance").GetValue(decision, null);
                basis.VerticalDesign.Criteria.Add(new GravityDesignCriterion { Key = p.ParameterKey, Section = GravityFlowSectionIdentifier.VerticalDesign,
                    Requirement = GravityCriterionRequirement.Required, Status = GravityCriterionStatus.Defined, Value = StandardValue.FromText(GravityExecutionPlanBuilder.Json(decision)), Provenance = p });
            }
            P3Blocked(P3Resolve(d, net: net, set: set, basis: basis), "CONTRADICTION_OR_NEGATIVE_CYCLE");
        }

        // Group 7: current source/context/basis/constraint/P13/objective identities are mandatory.
        private static void P13Auto03_StaleIdentityRejections()
        {
            var d = P3Draft(); var net = AutoLive(d); var c = AutoContext(net, d.Constraints, GravityVerticalDesignProposalResolver.ObjectiveId);
            P3Blocked(P3Resolve(d, context: new GravityAutomaticPreparationContext("TEST_ONLY_DIFFERENT_MODEL", c.NetworkFingerprint, c.Scenario, c.ActivePolicyId, c.ActivePolicyVersion, c.ConstraintFingerprint)), "SOURCE_CONTEXT_MISMATCH");
            P3Blocked(P3Resolve(d, context: new GravityAutomaticPreparationContext(c.ModelFingerprint, c.NetworkFingerprint, new GravityScenarioContext(c.Scenario.ScenarioId + 1, c.Scenario.ScenarioLabel,
                c.Scenario.TopologyAlternativeId, c.Scenario.PhysicalAlternativeId, c.Scenario.ActiveTopologyAlternativeId), c.ActivePolicyId, c.ActivePolicyVersion, c.ConstraintFingerprint)), "SOURCE_CONTEXT_MISMATCH");
            P3Blocked(P3Resolve(d, context: new GravityAutomaticPreparationContext(c.ModelFingerprint, c.NetworkFingerprint, c.Scenario, c.ActivePolicyId, c.ActivePolicyVersion, "TEST_ONLY_STALE_CONSTRAINT")), "CONSTRAINT_CONTEXT_MISMATCH");
            P3Blocked(P3Resolve(d, revision: "2"), "OBJECTIVE_UNSUPPORTED");
            P3Blocked(P3Resolve(d, context: AutoContext(net, d.Constraints, GravityVerticalDesignProposalResolver.ObjectiveId, "2")), "OBJECTIVE_CONTEXT_MISMATCH");
            P3Blocked(P3Resolve(d, basisFingerprint: new string('0', 64)), "DESIGN_BASIS_CONTENT_MISMATCH");
            var f = new GravityVerticalFeasibilityEngine().Analyze(net, d.Constraints); var ranges = f.EndpointRanges.ToDictionary(p => p.Key, p => p.Value);
            var first = ranges.Values.First(); ranges[first.ConduitEnd.ToString()] = new EndpointElevationRange(first.ConduitEnd, first.MinimumAllowedInvert, first.MaximumAllowedInvert + .1, first.ExactInvert, first.Unit);
            P3Blocked(P3Resolve(d, feasibility: new VerticalFeasibilityResult(f.Status, f.ConstraintFingerprint, ranges, f.Conflicts, f.DecisionRequests, f.IncompleteItems, f.PropagationPasses)), "FEASIBILITY_MISMATCH");
            var tampered = new GravityNetworkSnapshot(2, net.SourceModelFingerprint, net.ScenarioContext, net.Nodes, net.Conduits, null, new string('0', 64));
            P3Blocked(P3Resolve(d, net: tampered), "SOURCE_INVALID");
        }

        // Group 8: reorder/repeat stability and immutable, non-authoritative output; existing gates still reject Draft.
        private static void P13Auto03_DeterminismAndAuthorityUnchanged()
        {
            var d = P3Draft(); var before = GravityExecutionPlanBuilder.Json(d.Inputs); var net = AutoLive(d); var originalSet = d.Constraints;
            var a = P3Resolve(d); var b = P3Resolve(d);
            var reordered = new GravityNetworkSnapshot(2, net.SourceModelFingerprint, net.ScenarioContext, net.Nodes.Reverse(), net.Conduits.Reverse(), null, net.NetworkFingerprint);
            var set = new GravityVerticalDesignConstraintSet(net.NetworkFingerprint, originalSet.DesignBasisId, originalSet.DesignBasisVersion,
                originalSet.Orientations.Values.Reverse().ToDictionary(o => o.ConduitId), originalSet.FixedConstraints.Reverse(), originalSet.ProtectedConstraints.Reverse(),
                originalSet.OutfallConstraints.Reverse(), originalSet.MatchConstraints.Reverse(), originalSet.CoverConstraints.Reverse(), originalSet.SlopeConstraints.Reverse());
            var basis = GravityFlowDesignBasisSerializer.Deserialize(GravityFlowDesignBasisSerializer.Serialize(d.Basis));
            foreach (var section in basis.EnumerateSectionsInCanonicalOrder()) section.Criteria.Reverse();
            var r = P3Resolve(d, net: reordered, set: set, basis: basis);
            Check(a.Status == "PROPOSED" && b.Status == "PROPOSED" && r.Status == "PROPOSED", "All identical repeat/reorder requests succeed.");
            Check(a.ProposalFingerprint == b.ProposalFingerprint && a.ProposalFingerprint == r.ProposalFingerprint && a.CandidateFingerprint == r.CandidateFingerprint &&
                a.FeasibilityIdentity == r.FeasibilityIdentity && a.Recipe.RecipeId == r.Recipe.RecipeId, "Native candidate, objective proposal, recipe and recomputed P13 identities are stable.");
            var content = GravityVerticalDesignProposalResolver.ContentFingerprint(basis); basis.CommonDesignBasis.DesignBasisVersion = "TEST_ONLY_CHANGED_VERSION";
            Check(content != GravityVerticalDesignProposalResolver.ContentFingerprint(basis), "Content/version changes invalidate proposal input identity.");
            var detached = a.Provenance; detached.Notes = "TEST_ONLY_MUTATED_COPY"; var detachedRecipe = a.Recipe; detachedRecipe.Provenance.Notes = "TEST_ONLY_MUTATED_RECIPE_COPY";
            Check(a.Provenance.Notes.Contains("NON_AUTHORITATIVE_PROPOSAL") && a.Recipe.Provenance.Notes.Contains("NON_AUTHORITATIVE_PROPOSAL"), "Mutable provenance/recipe getters are detached copies.");
            Check(before == GravityExecutionPlanBuilder.Json(d.Inputs) && d.Inputs.CandidateJson == null && d.Inputs.Approval == null && d.Candidates == null && d.Recipes.Count == 0,
                "Pure proposal changes no project inputs, selected candidate, review, approval or recipe storage.");
            Check(d.Basis.CommonDesignBasis.ReviewState == DesignBasisReviewState.Draft, "Draft remains Draft.");
            Ui02Reject(() => GravityExecutionPlanBuilder.ValidateReviewedBasis(d.Inputs), "DESIGN_BASIS");
            Check(d.Inputs.SourceRead.WriteInvocationCounts.All(p => p.Value == 0), "All fixture runtime/write counters remain zero.");
        }
    }
}
