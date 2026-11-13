using System;
using System.Collections.Generic;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        // TEST_ONLY synthetic criteria and adaptations of existing P12 fixtures. No review, approval or model runtime.
        private const string V2Min = "TEST_ONLY.minimum_slope_ratio", V2Max = "TEST_ONLY.maximum_slope_ratio", V2Cover = "TEST_ONLY.minimum_cover";
        private static StandardDefinition V2Standard(double min = .01, double max = .04, double cover = 2, string id = "TEST_ONLY_VERTICAL_STANDARD", string revision = "TEST_ONLY_REV1") =>
            new StandardDefinition { StandardId = id, Name = "TEST_ONLY_NON_ENGINEERING", Version = revision, SourceLayer = StandardSourceLayer.ProjectOverride,
                Parameters = new List<StandardParameter> { new StandardParameter(V2Min, StandardValue.FromNumber(min)), new StandardParameter(V2Max, StandardValue.FromNumber(max)),
                    new StandardParameter(V2Cover, StandardValue.FromQuantity(cover, "ft")) } };
        private static StandardsResolutionOptions V2Options(StandardDefinition s) => new StandardsResolutionOptions { SelectedProjectOverrideId = s.StandardId };
        private static GravityVerticalCriterionSelection V2Binding(string key, StandardDefinition s, StandardValueKind quantity) =>
            new GravityVerticalCriterionSelection(key, GravityFlowSectionIdentifier.PipeCriteria, key, s.SourceLayer, s.StandardId, s.Version, quantity);
        private static void V2Criteria(EngineeringDesignWorkspace d, StandardDefinition s)
        {
            foreach (var parameter in s.Parameters)
            {
                var resolved = StandardsResolver.Resolve(parameter.Key, new[] { s }, V2Options(s));
                d.PutCriterion(GravityFlowSectionIdentifier.PipeCriteria, new GravityDesignCriterion { Key = parameter.Key, Section = GravityFlowSectionIdentifier.PipeCriteria,
                    Requirement = GravityCriterionRequirement.Required, Status = GravityCriterionStatus.Defined, Value = resolved.EffectiveValue,
                    Provenance = resolved.EffectiveProvenance, Rationale = "TEST_ONLY_NON_ENGINEERING" }, parameter.Key);
            }
        }
        private static EngineeringDesignWorkspace V2Draft(StandardDefinition standard = null)
        {
            var d = AutoDraft(); var raw = d.Inputs.SourceRead;
            foreach (var n in raw.Structures) { n.GroundElevation = 100; n.GroundElevationUnit = CreateFeetValue(0).Unit; n.PropertyClassifications["GroundElevation"] = "PROVEN"; }
            foreach (var c in raw.Conduits) { c.Rise = 1.5; c.RiseUnit = CreateFeetValue(0).Unit; c.PropertyClassifications["Rise"] = "PROVEN"; }
            d.BindNetwork(raw, d.Inputs.ModelFingerprint); d.NewDraft("TEST_ONLY_VERTICAL_1"); V2Criteria(d, standard ?? V2Standard()); AutoAccept(d); return d;
        }
        private static GravityVerticalPreparationRequest V2Request(EngineeringDesignWorkspace d, StandardDefinition standard = null, bool maximum = true,
            GravityNetworkSnapshot network = null, GravityVerticalDesignConstraintSet constraints = null)
        {
            var s = standard ?? V2Standard(); var net = network ?? AutoLive(d); var set = constraints ?? d.Constraints;
            return new GravityVerticalPreparationRequest(new[] { s }, V2Options(s), V2Binding(V2Min, s, StandardValueKind.Number),
                maximum ? V2Binding(V2Max, s, StandardValueKind.Number) : null, V2Binding(V2Cover, s, StandardValueKind.Quantity),
                AutoContext(net, set), d.Basis.CommonDesignBasis.DesignBasisId, d.Basis.CommonDesignBasis.DesignBasisVersion);
        }
        private static GravityVerticalPreparationResult V2Prepare(EngineeringDesignWorkspace d, GravityVerticalPreparationRequest request = null,
            GravityNetworkSnapshot network = null, GravityVerticalDesignConstraintSet constraints = null) => new GravityAutomaticVerticalPreparationService().Prepare(
                network ?? AutoLive(d), d.Basis, constraints ?? d.Constraints, request ?? V2Request(d));
        private static GravityVerticalPreparationResult V2Accept(EngineeringDesignWorkspace d, StandardDefinition s = null, bool maximum = true) =>
            d.PrepareAutomaticVerticalConstraints(V2Request(d, s, maximum));
        private static GravityNetworkSnapshot V2Source(GravityNetworkSnapshot original, IEnumerable<GravityNetworkNode> nodes, IEnumerable<GravityNetworkConduit> conduits)
        {
            var n = nodes.ToArray(); var c = conduits.ToArray(); return new GravityNetworkSnapshot(2, original.SourceModelFingerprint, original.ScenarioContext, n, c, null,
                GravityNetworkSnapshot.ComputeFingerprint(2, original.SourceModelFingerprint, original.ScenarioContext, n, c));
        }
        private static GravityVerticalPreparationResult V2ChangedSource(EngineeringDesignWorkspace d, GravityNetworkSnapshot net)
        {
            // Existing human TEST_ONLY decisions explicitly rebound to the fixture's actual endpoints, not stale AUTO-01 declarations.
            var b = new GravityVerticalDesignConstraintSetBuilder();
            foreach (var o in d.Constraints.Orientations.Values) b.SetOrientation(o.ConduitId, o.FromNodeId, o.ToNodeId, Ui02Provenance("TEST_ONLY.source.orientation." + o.ConduitId));
            var set = b.Build(net.NetworkFingerprint, d.Basis.CommonDesignBasis.DesignBasisId, d.Basis.CommonDesignBasis.DesignBasisVersion);
            return V2Prepare(d, V2Request(d, network: net, constraints: set), net, set);
        }
        private static bool V2Reason(GravityVerticalPreparationResult r, string reason) => r.Items.Any(i => i.Reasons.Any(x => x.Contains(reason))) || r.Criteria.Any(c => c.UnresolvedReasons.Any(x => x.Contains(reason)));

        private static void P13Auto02_ResolvedSlopeBoundsAndTrace()
        {
            var d = V2Draft(); var r = V2Prepare(d);
            Check(r.ContextFailures.Count == 0 && r.Slopes.Count == 2 && r.Covers.Count == 4, "Valid resolved bounds and proven cover prepared independently.");
            Check(r.Slopes.All(s => s.MinimumSlope == .01 && s.MaximumSlope == .04), "Minimum/maximum ratios preserved exactly.");
            var minOnly = V2Prepare(d, V2Request(d, maximum: false)); Check(minOnly.Slopes.Count == 2 && minOnly.Slopes.All(s => s.MaximumSlope == null), "Unspecified maximum remains absent.");
            var resolved = StandardsResolver.Resolve(V2Min, new[] { V2Standard() }, V2Options(V2Standard()));
            foreach (var s in r.Slopes)
                Check(s.Provenance.SourceLayer == resolved.EffectiveSource && s.Provenance.StandardId == resolved.EffectiveStandardId && s.Provenance.StandardVersion == resolved.EffectiveVersion &&
                    s.Provenance.ParameterKey == "auto.vertical.slope." + s.ConduitId && s.Provenance.Notes.Contains(V2Min) && s.Provenance.Notes.Contains(V2Max) &&
                    s.Provenance.Notes.Contains(d.NetworkFingerprint) && s.Provenance.Notes.Contains("ResolvedDimensionlessRatioBounds"), "Actual standard provenance and original parameter/source/element evidence.");
            Check(r.Covers.All(c => c.Reference == VerticalCoverReference.GroundToCrown && c.GroundElevation.NumericValue == 100 && c.MinimumCover.NumericValue == 2 &&
                c.MinimumCover.Unit.Equals(CreateFeetValue(0).Unit)), "Existing GroundToCrown contract receives source ground and resolved Feet quantity.");
        }
        private static void P13Auto02_MissingAmbiguousAndConflictingCriteria()
        {
            var d = V2Draft(); var missing = V2Standard(); missing.Parameters.RemoveAll(p => p.Key == V2Min);
            var r = V2Prepare(d, V2Request(d, missing)); Check(r.Slopes.Count == 0 && r.Covers.Count == 4 && V2Reason(r, "STANDARD_VALUE_NOT_DEFINED"), "Missing slope criterion blocks slopes only.");
            var original = V2Request(d); var ambiguous = new GravityVerticalPreparationRequest(new[] { V2Standard(), V2Standard() }, original.SelectedStandards, original.MinimumSlope,
                original.MaximumSlope, original.MinimumCover, original.Current, original.DesignBasisId, original.DesignBasisVersion);
            Check(V2Reason(V2Prepare(d, ambiguous), "AMBIGUOUS_STANDARD_IDENTITY"), "Duplicate selected standard identity is ambiguous.");
            var publishedA = V2Standard(id: "TEST_ONLY_PUBLISHED_A"); publishedA.SourceLayer = StandardSourceLayer.PublishedStandard; publishedA.Parameters.RemoveAll(p => p.Key != V2Min);
            var publishedB = V2Standard(id: "TEST_ONLY_PUBLISHED_B"); publishedB.SourceLayer = StandardSourceLayer.PublishedStandard; publishedB.Parameters.RemoveAll(p => p.Key != V2Min);
            var resolverAmbiguous = new GravityVerticalPreparationRequest(new[] { V2Standard(), publishedA, publishedB }, original.SelectedStandards, original.MinimumSlope,
                original.MaximumSlope, original.MinimumCover, original.Current, original.DesignBasisId, original.DesignBasisVersion);
            r = V2Prepare(d, resolverAmbiguous); Check(V2Reason(r, "AMBIGUOUS_STANDARD_SOURCE") && r.Covers.Count == 4, "Actual resolver ambiguity is reported while independent cover resolves.");
            var selectedMissing = new GravityVerticalPreparationRequest(original.Standards, new StandardsResolutionOptions { SelectedProjectOverrideId = "TEST_ONLY_MISSING" },
                original.MinimumSlope, original.MaximumSlope, original.MinimumCover, original.Current, original.DesignBasisId, original.DesignBasisVersion);
            Check(V2Reason(V2Prepare(d, selectedMissing), "SELECTED_STANDARD_UNAVAILABLE"), "No resolver fallback from missing explicit selected standard.");
            var conflict = V2Standard(min: .05, max: .01); V2Criteria(d, conflict); r = V2Prepare(d, V2Request(d, conflict));
            Check(r.Slopes.Count == 0 && r.Covers.Count == 4 && V2Reason(r, "MINIMUM_SLOPE_EXCEEDS_MAXIMUM"), "Conflicting ratios unresolved while covers proceed.");
            var basisMismatch = V2Draft(); basisMismatch.Basis.PipeCriteria.Criteria.Single(c => c.Key == V2Min).Value = StandardValue.FromNumber(.02);
            Check(V2Reason(V2Prepare(basisMismatch), "BASIS_VALUE_OR_PROVENANCE_MISMATCH"), "Resolved standard must agree with defined criterion.");
            basisMismatch.Basis.PipeCriteria.Criteria.Single(c => c.Key == V2Min).Status = GravityCriterionStatus.NotDefined;
            Check(V2Reason(V2Prepare(basisMismatch), "DEFINED_BASIS_CRITERION_REQUIRED"), "Undefined criterion stays unresolved.");
        }
        private static void P13Auto02_TypesUnitsAndNoPercentConversion()
        {
            var s = V2Standard(); s.Parameters.Single(p => p.Key == V2Min).Value = StandardValue.FromQuantity(2, "%");
            s.Parameters.Single(p => p.Key == V2Cover).Value = StandardValue.FromQuantity(2, "m"); var d = V2Draft(s); var r = V2Prepare(d, V2Request(d, s));
            Check(r.Slopes.Count == 0 && r.Covers.Count == 0 && V2Reason(r, "NO_PERCENT_CONVERSION") && V2Reason(r, "FEET_LENGTH_QUANTITY_REQUIRED"), "Percent/unsupported length units never converted.");
            var ratio = V2Standard(min: 2); var numeric = V2Draft(ratio); r = V2Prepare(numeric, V2Request(numeric, ratio, maximum: false));
            Check(r.Slopes.Count == 2 && r.Slopes.All(x => x.MinimumSlope == 2), "Explicit Number ratio 2 remains 2, never .02.");
            var req = V2Request(numeric, ratio, maximum: false); var wrongType = new GravityVerticalPreparationRequest(req.Standards, req.SelectedStandards,
                V2Binding(V2Min, ratio, StandardValueKind.Quantity), null, req.MinimumCover, req.Current, req.DesignBasisId, req.DesignBasisVersion);
            Check(V2Reason(V2Prepare(numeric, wrongType), "PARAMETER_MAPPING_INVALID"), "Explicit quantity declaration required.");
            var nonfinite = V2Standard(); nonfinite.Parameters.Single(p => p.Key == V2Min).Value.NumberValue = double.NaN;
            var invalid = V2Draft(); Check(V2Reason(V2Prepare(invalid, V2Request(invalid, nonfinite)), "STANDARDS_VALIDATION_ERROR"), "Nonfinite standards rejected without invented value.");
        }
        private static void P13Auto02_SourceAvailabilityUnitsAndClassification()
        {
            var d = V2Draft(); var net = AutoLive(d); var first = net.Nodes.First(); var conduit = net.Conduits.First();
            var nodes = net.Nodes.Select(n => n == first ? new GravityNetworkNode(n.SourceElementId, n.Kind, n.Label, n.X, n.Y,
                new GravityElevationState(groundElevation: null, elevationUnit: CreateFeetValue(0).Unit), propertyClassifications: new Dictionary<string, string> { { "GroundElevation", "PROVEN" } }) : n);
            var r = V2ChangedSource(d, V2Source(net, nodes, net.Conduits));
            Check(r.Covers.Count == 3 && V2Reason(r, "GROUND_ELEVATION_FINITE_VALUE_REQUIRED"), "Missing ground blocks only its endpoint; no rim/coordinate substitution.");
            var edges = net.Conduits.Select(c => c == conduit ? new GravityNetworkConduit(c.SourceElementId, c.Label, c.StartNodeId, c.StopNodeId,
                new GravityConduitPhysicalState(length: 100, diameter: 1.5, rise: null, lengthUnit: CreateFeetValue(0).Unit),
                new Dictionary<string, string> { { "Rise", "PROVEN" } }) : c);
            r = V2ChangedSource(d, V2Source(net, net.Nodes, edges));
            Check(r.Covers.Count == 2 && V2Reason(r, "RISE_FINITE_POSITIVE_VALUE_REQUIRED"), "Diameter does not substitute for missing Rise.");
            edges = net.Conduits.Select(c => c == conduit ? new GravityNetworkConduit(c.SourceElementId, c.Label, c.StartNodeId, c.StopNodeId,
                new GravityConduitPhysicalState(length: 100, rise: 1.5, lengthUnit: CreateFeetValue(0).Unit, riseUnit: new SourceUnitIdentity("length", "m", "metres", "m")),
                new Dictionary<string, string> { { "Rise", "PROVEN" } }) : c);
            r = V2ChangedSource(d, V2Source(net, net.Nodes, edges)); Check(r.Covers.Count == 2 && V2Reason(r, "RISE_FEET_UNIT_REQUIRED"), "Unsupported Rise unit unresolved.");
            nodes = net.Nodes.Select(n => n == first ? new GravityNetworkNode(n.SourceElementId, n.Kind, n.Label, n.X, n.Y,
                new GravityElevationState(groundElevation: 100, elevationUnit: CreateFeetValue(0).Unit), propertyClassifications: new Dictionary<string, string> { { "GroundElevation", "SEMANTICS_UNRESOLVED" } }) : n);
            r = V2ChangedSource(d, V2Source(net, nodes, net.Conduits)); Check(r.Covers.Count == 3 && V2Reason(r, "GROUND_ELEVATION_CLASSIFICATION_NOT_PROVEN"), "Unproven ground classification remains unresolved.");
            nodes = net.Nodes.Select(n => n == first ? new GravityNetworkNode(n.SourceElementId, n.Kind, n.Label, n.X, n.Y,
                new GravityElevationState(groundElevation: 100, elevationUnit: new SourceUnitIdentity("length", "m", "metres", "m")),
                propertyClassifications: new Dictionary<string, string> { { "GroundElevation", "PROVEN" } }) : n);
            r = V2ChangedSource(d, V2Source(net, nodes, net.Conduits)); Check(r.Covers.Count == 3 && V2Reason(r, "GROUND_ELEVATION_FEET_UNIT_REQUIRED"), "Ground values in incompatible units are not converted.");
            edges = net.Conduits.Select(c => c == conduit ? new GravityNetworkConduit(c.SourceElementId, c.Label, c.StartNodeId, c.StopNodeId,
                new GravityConduitPhysicalState(length: double.NaN, rise: -1, lengthUnit: null, riseUnit: CreateFeetValue(0).Unit),
                new Dictionary<string, string> { { "Rise", "PROVEN" } }) : c);
            r = V2ChangedSource(d, V2Source(net, net.Nodes, edges)); Check(V2Reason(r, "FINITE_POSITIVE_LENGTH_REQUIRED") && V2Reason(r, "RISE_FINITE_POSITIVE_VALUE_REQUIRED"), "Invalid source magnitudes classified.");
        }
        private static void P13Auto02_ExplicitPreservationAndOutfallAuthority()
        {
            var d = V2Draft(); int id = d.Inputs.SourceRead.Conduits.First().Id; var end = d.End(id, ConduitEndKind.Start);
            var slope = new VerticalSlopeConstraint(id, .002, .05, Ui02Provenance("TEST_ONLY.human.slope"));
            var cover = new VerticalCoverConstraint(end, CreateFeetValue(100), CreateFeetValue(3), Ui02Provenance("TEST_ONLY.human.cover"));
            d.PutConstraint(slope); d.PutConstraint(cover);
            var unresolved = V2Prepare(d).Outfalls.Single(); Check(unresolved.SourceElevationObserved && !unresolved.DesignBoundaryAuthorized && !unresolved.ObservationAuthorizesBoundary &&
                unresolved.MissingAuthority != null, "Observed source elevation supplies no boundary authority.");
            int outfall = unresolved.Evidence.NodeId; d.PutConstraint(new OutfallVerticalConstraint(outfall, Ui02Provenance("TEST_ONLY.human.outfall"), CreateFeetValue(90)));
            string originalOrientation = GravityExecutionPlanBuilder.Json(d.Constraints.Orientations); var report = V2Accept(d);
            Check(report.Slopes.Count == 1 && report.Covers.Count == 3 && report.Items.Count(i => i.Status == "EXPLICIT_PRESERVED") == 2, "Human slope/cover preserved, no duplicate decisions.");
            Check(GravityExecutionPlanBuilder.Json(d.Constraints.SlopeConstraints.Single(s => s.ConduitId == id)) == GravityExecutionPlanBuilder.Json(slope) &&
                GravityExecutionPlanBuilder.Json(d.Constraints.CoverConstraints.Single(c => c.ConduitEnd.Equals(end))) == GravityExecutionPlanBuilder.Json(cover) &&
                GravityExecutionPlanBuilder.Json(d.Constraints.Orientations) == originalOrientation, "Actual explicit values/provenance and AUTO-01 orientations unchanged.");
            Check(report.Outfalls.Single().DesignBoundaryAuthorized && report.Outfalls.Single().Evidence.ExplicitBoundaries.Single().ProtectedInvert.NumericValue == 90 &&
                d.Constraints.OutfallConstraints.Count == 1, "Existing authorized boundary retained separately; no automatic boundary created.");
        }
        private static void P13Auto02_AtomicCriteriaAndIdempotence()
        {
            var d = V2Draft(); d.Assess(); var report = V2Accept(d);
            Check(d.Feasibility == null && d.Candidates == null && d.Inputs.FeasibilityJson == null && d.Inputs.CandidateJson == null && d.Inputs.Approval == null &&
                d.Basis.CommonDesignBasis.ReviewState == DesignBasisReviewState.Draft, "Effective edits invalidate dependent state without review or approval.");
            foreach (object decision in report.Slopes.Cast<object>().Concat(report.Covers))
            {
                var p = (StandardProvenance)decision.GetType().GetProperty("Provenance").GetValue(decision, null); var criterion = d.Basis.VerticalDesign.Criteria.Single(c => c.Key == p.ParameterKey);
                Check(criterion.Value.TextValue == GravityExecutionPlanBuilder.Json(decision) && GravityExecutionPlanBuilder.Json(criterion.Provenance) == GravityExecutionPlanBuilder.Json(p), "Exact native canonical decision/provenance criterion.");
            }
            Check(GravityExecutionPlanBuilder.ValidateEngineeringConstraints(d.Inputs, AutoLive(d)).VerticalConstraintFingerprint == d.Constraints.VerticalConstraintFingerprint, "Existing basis-decision/lineage validator accepts integration.");
            string basis = d.Basis.CreateSnapshot().GravityContentFingerprint, constraints = d.Constraints.VerticalConstraintFingerprint; d.MarkSaved(); var again = V2Accept(d);
            Check(again.Slopes.Count == 0 && again.Covers.Count == 0 && again.Items.All(i => i.Status == "CURRENT_DERIVED") && !d.Dirty &&
                basis == d.Basis.CreateSnapshot().GravityContentFingerprint && constraints == d.Constraints.VerticalConstraintFingerprint, "Identical acceptance is idempotent with deterministic fingerprints.");
            var pure = V2Draft(); Check(GravityExecutionPlanBuilder.Json(V2Prepare(pure)) == GravityExecutionPlanBuilder.Json(V2Prepare(pure)), "Pure preparation deterministic.");
            var collision = V2Draft(); string key = V2Prepare(collision).Covers.Last().Provenance.ParameterKey;
            collision.PutCriterion(GravityFlowSectionIdentifier.VerticalDesign, new GravityDesignCriterion { Key = key, Section = GravityFlowSectionIdentifier.VerticalDesign,
                Status = GravityCriterionStatus.Defined, Value = StandardValue.FromText("{}"), Provenance = Ui02Provenance("TEST_ONLY.collision") });
            string before = GravityExecutionPlanBuilder.Json(collision.Inputs); Ui02Reject(() => V2Accept(collision), "DECISION_CRITERION_KEY_CONFLICT");
            Check(before == GravityExecutionPlanBuilder.Json(collision.Inputs) && collision.Constraints.SlopeConstraints.Count == 0 && collision.Constraints.CoverConstraints.Count == 0, "Late conflict rejects entire batch before stored-state publication.");
        }
        private static void P13Auto02_StandardsBasisSourceAndReloadInvalidation()
        {
            var d = V2Draft(); V2Accept(d); var stored = RuntimeHostProtocol.Serializer().Deserialize<GravityEngineeringInputs>(GravityExecutionPlanBuilder.Json(d.Inputs));
            var reopened = new EngineeringDesignWorkspace(d.Inputs.ProjectId, d.Inputs.ModelFingerprint, stored.SourceRead, stored);
            Ui02Reject(() => reopened.Assess(), "VERTICAL_STANDARD_CONTEXT_REQUIRED");
            Check(V2Accept(reopened).Slopes.Count == 0 && reopened.Constraints.CoverConstraints.Count == 4, "Reload requires explicit current standards context; matching rebind adds no duplicates.");
            var other = V2Standard(id: "TEST_ONLY_OTHER_STANDARD"); var selectionChanged = V2Prepare(reopened, V2Request(reopened, other));
            Check(selectionChanged.HasStaleDerivedDecisions && V2Reason(selectionChanged, "BASIS_VALUE_OR_PROVENANCE_MISMATCH"), "Selected standard change cannot silently replace basis authority even for equal numeric values.");
            var changed = V2Standard(min: .02, revision: "TEST_ONLY_REV2"); V2Criteria(d, changed);
            Ui02Reject(() => d.Assess(), "VERTICAL_DECISIONS_STALE");
            var report = V2Prepare(d, V2Request(d, changed)); Check(report.HasStaleDerivedDecisions, "Resolved standard/criterion change detects stale derivations.");
            string original = d.Inputs.ConstraintsJson; Ui02Reject(() => V2Accept(d, changed), "VERTICAL_DECISIONS_STALE");
            Check(d.Inputs.ConstraintsJson == original && d.Inputs.CandidateJson == null, "Stale standard does not overwrite old decisions or restore candidate.");
            d.MarkSaved(); d.NewDraft("TEST_ONLY_VERTICAL_2"); Check(d.Constraints.SlopeConstraints.Count == 0 && d.Constraints.CoverConstraints.Count == 0 && d.Constraints.Orientations.Count == 2,
                "Explicit version revision removes owned bounds and criteria, preserves accepted orientations.");
            Check(V2Accept(d, changed).Slopes.Count == 2, "New explicit draft permits current resolved bounds.");
            d.SetIdentity(d.Basis.CommonDesignBasis.DesignBasisId, "TEST_ONLY_DIRECT_VERSION_CHANGE"); Ui02Reject(() => d.Assess(), "VERTICAL_DECISIONS_STALE");
            var source = V2Draft(); V2Accept(source); var raw = source.Inputs.SourceRead; raw.Conduits[0].Length += 1; source.BindNetwork(raw, source.Inputs.ModelFingerprint);
            Check(source.SourceChanged, "Network source identity changes detected."); Ui02Reject(() => source.Assess(), "CURRENT_NETWORK_AND_DRAFT_REQUIRED");
        }
        private static void P13Auto02_P13PropagationAndAuthorityGates()
        {
            var d = V2Draft(); V2Accept(d); var f = d.Assess();
            Check(f.Status == VerticalFeasibilityStatus.EngineeringDecisionRequired && f.DecisionRequests.Any(r => r.KnownFacts.ContainsKey("OutfallNodeId")), "Missing outfall authority remains a P13 blocker.");
            Check(f.EndpointRanges.Values.All(r => r.MaximumAllowedInvert <= 96.5) && f.EndpointRanges.Values.Any(r => r.MaximumAllowedInvert <= 95.5), "Existing P13 cover/slope propagation narrows endpoint bounds.");
            Ui02Reject(() => d.GenerateCandidates(), "P13_GENERATION_GATE"); Ui02Reject(() => GravityExecutionPlanBuilder.BuildSingleOperator(d.Inputs, false), "DESIGN_BASIS");
            var first = d.Inputs.SourceRead.Conduits.First(); d.PutConstraint(new FixedEndpointElevationConstraint(d.End(first.Id, ConduitEndKind.Start), CreateFeetValue(100), Ui02Provenance("TEST_ONLY.contradiction")));
            Check(d.Assess().Status == VerticalFeasibilityStatus.Infeasible, "Contradictory explicit fixed elevation remains infeasible under existing engine.");
            var positive = V2Draft(); V2Accept(positive); int index = 0;
            foreach (var c in positive.Inputs.SourceRead.Conduits)
            {
                double start = 92 - index++, stop = start - 1;
                positive.PutConstraint(new FixedEndpointElevationConstraint(positive.End(c.Id, ConduitEndKind.Start), CreateFeetValue(start), Ui02Provenance("TEST_ONLY.fixed.start." + c.Id)));
                positive.PutConstraint(new FixedEndpointElevationConstraint(positive.End(c.Id, ConduitEndKind.Stop), CreateFeetValue(stop), Ui02Provenance("TEST_ONLY.fixed.stop." + c.Id)));
            }
            int outfall = AutoLive(positive).Nodes.Single(n => n.Kind == GravityNodeKind.Outfall).SourceElementId;
            positive.PutConstraint(new OutfallVerticalConstraint(outfall, Ui02Provenance("TEST_ONLY.explicit.boundary"), CreateFeetValue(90)));
            Check(positive.Assess().Status == VerticalFeasibilityStatus.Feasible && positive.GenerateCandidates().Candidates.Count == 1, "Explicit TEST_ONLY exact profile uses existing P13/P14 unchanged.");
            Ui02Reject(() => GravityExecutionPlanBuilder.BuildSingleOperator(positive.Inputs, false), "DESIGN_BASIS"); Check(positive.Inputs.Approval == null, "No production approval or runtime authority fabricated.");
            positive.Basis.PipeCriteria.Criteria.Single(c => c.Key == V2Cover).Value = StandardValue.FromQuantity(3, "ft");
            Ui02Reject(() => positive.GenerateCandidates(), "VERTICAL_DECISIONS_STALE");
            Check(positive.Feasibility == null && positive.Inputs.CandidateJson == null, "Candidate generation detects stale criterion and invalidates cached plan inputs.");
        }
        private static void P13Auto02_CircularSectionHeightResolution()
        {
            // 1. Compare retained observations before using the later, unit-bearing P15 artifact.
            var repository = Repository();
            var p12Json = System.IO.File.ReadAllText(System.IO.Path.Combine(repository, "docs/evidence/p12/p12a_network_read.json"));
            var rawP12 = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(p12Json);
            var p15Json = System.IO.File.ReadAllText(System.IO.Path.Combine(repository,
                "docs/evidence/p15/unattended-application-acceptance/20261010-6c5b8777/real-network-read.json"));
            var rawP15 = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(p15Json);
            Check(rawP12.ScenarioContext != null && rawP15.ScenarioContext != null &&
                rawP15.ScenarioContext.ActiveScenarioId == 1 && rawP15.ScenarioContext.PhysicalAlternativeId == 5 &&
                GravityExecutionPlanBuilder.Json(rawP12.ScenarioContext) == GravityExecutionPlanBuilder.Json(rawP15.ScenarioContext),
                "P12 and P15 retain the same observed scenario and Physical/topology alternative identities.");
            Check(rawP12.Conduits.Select(c => c.Id).OrderBy(id => id).SequenceEqual(Enumerable.Range(27, 9)) &&
                rawP15.Conduits.Select(c => c.Id).OrderBy(id => id).SequenceEqual(Enumerable.Range(27, 9)),
                "Both observations contain exactly conduits 27-35 without duplicates.");
            Check(rawP12.Structures.Select(n => n.Id).OrderBy(id => id).SequenceEqual(Enumerable.Range(17, 10)) &&
                rawP15.Structures.Select(n => n.Id).OrderBy(id => id).SequenceEqual(Enumerable.Range(17, 10)),
                "Both observations contain exactly nodes 17-26.");
            foreach (var node in rawP15.Structures)
            {
                var legacyNode = rawP12.Structures.Single(n => n.Id == node.Id);
                Check(node.RuntimeCategory == legacyNode.RuntimeCategory && node.ElementTypeId == legacyNode.ElementTypeId,
                    "Corresponding node identities and categories agree.");
            }
            Check(rawP15.Structures.Where(n => n.RuntimeCategory == "Outfall").Select(n => n.Id).OrderBy(id => id)
                .SequenceEqual(new[] { 25, 26 }), "Observed outfalls remain nodes 25 and 26 in both sources.");
            Check(rawP15.FieldUnitMap.TryGetValue("ConduitDiameter", out var diameterMetadata) && diameterMetadata != null &&
                diameterMetadata.IsUnitized && diameterMetadata.FieldName == "ConduitDiameter" && diameterMetadata.Dimension == "length" &&
                diameterMetadata.WorkingUnitId == "Feet" && diameterMetadata.WorkingUnitName == "feet" && diameterMetadata.WorkingUnitSymbol == "ft" &&
                diameterMetadata.ExactMembersUsed.Contains("IUnitizedField.WorkingUnitIndex"),
                "P15 retains authoritative ConduitDiameter Feet working-unit field metadata.");
            foreach (var observed in rawP15.Conduits)
            {
                var legacy = rawP12.Conduits.Single(c => c.Id == observed.Id);
                Check(observed.StartNodeId == legacy.StartNodeId && observed.StopNodeId == legacy.StopNodeId &&
                    observed.Shape == "CircularPipe" && legacy.Shape == observed.Shape && observed.Diameter.HasValue &&
                    observed.Diameter.Value > 0 && !double.IsNaN(observed.Diameter.Value) && !double.IsInfinity(observed.Diameter.Value) &&
                    observed.Diameter == legacy.Diameter && observed.Rise == 0 && observed.Rise == legacy.Rise &&
                    observed.Length == legacy.Length && observed.Span == legacy.Span,
                    "Corresponding conduit endpoints, circular shape, positive diameter and raw geometry agree.");
                var unit = observed.DiameterUnit;
                Check(unit != null && unit.Dimension == "length" && unit.UnitId == "Feet" && unit.UnitName == "feet" && unit.Symbol == "ft" &&
                    unit.Provenance != null && unit.Provenance.Source == "WaterObjectsFieldMetadata" &&
                    unit.Provenance.FieldIdentity == "DelawareConduitElementManager.ConduitDiameter" &&
                    unit.Provenance.WorkingUnitIdentity == "Feet (feet)" && unit.Provenance.StorageUnitIdentity == "Feet (feet)" &&
                    observed.PropertyClassifications.TryGetValue("Diameter", out var diameterClass) && diameterClass == "PROVEN",
                    "Each P15 conduit carries its own proven diameter and authoritative Feet unit provenance.");
            }
            var snapshot = new GravityNetworkIngestionService().Ingest(rawP15, "0".PadRight(64, '0'), schemaVersion: 2);

            Check(snapshot.Conduits.Count == 9, "Retained genuine network contains exactly nine conduits (27-35).");
            for (int id = 27; id <= 35; id++)
            {
                var conduit = snapshot.Conduits.Single(c => c.SourceElementId == id);
                Check(conduit.ExistingPhysicalState.Shape == "CircularPipe", "Genuine conduit shape is CircularPipe.");
                Check(conduit.ExistingPhysicalState.Diameter.HasValue && conduit.ExistingPhysicalState.Diameter.Value > 0, "Diameter is positive and observed.");
                Check(conduit.ExistingPhysicalState.DiameterUnit != null && conduit.ExistingPhysicalState.DiameterUnit.Symbol == "ft", "Diameter unit is available and feet.");
                Check(conduit.ExistingPhysicalState.Rise.HasValue && conduit.ExistingPhysicalState.Rise.Value == 0, "Raw source Rise observation is preserved as 0.");
                Check(!conduit.PropertyClassifications.TryGetValue("Rise", out var rClass) || rClass != "PROVEN", "Rise classification is not proven.");

                // Verified effective section height resolution
                Check(conduit.ExistingPhysicalState.EffectiveSectionHeight.HasValue, "Effective section height resolved.");
                Check(conduit.ExistingPhysicalState.EffectiveSectionHeight.Value == conduit.ExistingPhysicalState.Diameter.Value, "Effective section height equals diameter.");
                Check(conduit.ExistingPhysicalState.EffectiveSectionHeightUnit != null && conduit.ExistingPhysicalState.EffectiveSectionHeightUnit.Symbol == "ft", "Effective height unit matches diameter unit.");
                Check(conduit.ExistingPhysicalState.SectionHeightDerivation == "DERIVED_FROM_CIRCULAR_DIAMETER", "Derivation provenance is DERIVED_FROM_CIRCULAR_DIAMETER.");
                Check(conduit.PropertyClassifications.TryGetValue("EffectiveSectionHeight", out var effClass) && effClass == "PROVEN", "EffectiveSectionHeight is classified PROVEN.");
            }

            // The legacy artifact remains a negative fixture; no units are borrowed from P15.
            Check(!rawP12.FieldUnitMap.ContainsKey("ConduitDiameter") && rawP12.Conduits.All(c => c.DiameterUnit == null),
                "Legacy P12 contains neither per-conduit diameter units nor ConduitDiameter field metadata.");
            Func<NetworkReadResult, string> rawGeometry = source => GravityExecutionPlanBuilder.Json(source.Conduits.Select(c => new
                { c.Id, c.StartNodeId, c.StopNodeId, c.Shape, c.Length, c.Diameter, c.Rise, c.Span, c.GeometryPoints }).ToArray());
            var legacyGeometry = rawGeometry(rawP12);
            var legacySnapshot = new GravityNetworkIngestionService().Ingest(rawP12, "0".PadRight(64, '0'), schemaVersion: 2);
            foreach (var conduit in legacySnapshot.Conduits)
            {
                var observed = rawP12.Conduits.Single(c => c.Id == conduit.SourceElementId);
                Check(observed.DiameterUnit == null && conduit.ExistingPhysicalState.DiameterUnit == null,
                    "Legacy ingestion leaves diameter units unavailable; no implicit Feet default.");
                Check(!conduit.ExistingPhysicalState.EffectiveSectionHeight.HasValue &&
                    conduit.ExistingPhysicalState.EffectiveSectionHeightUnit == null &&
                    (!conduit.PropertyClassifications.TryGetValue("EffectiveSectionHeight", out var legacyHeightClass) || legacyHeightClass != "PROVEN"),
                    "Legacy missing-unit evidence cannot promote an effective height to PROVEN.");
                Check(conduit.ExistingPhysicalState.Diameter == observed.Diameter && conduit.ExistingPhysicalState.Rise == observed.Rise &&
                    conduit.ExistingPhysicalState.Shape == observed.Shape && conduit.ExistingPhysicalState.Span == observed.Span &&
                    conduit.ExistingPhysicalState.Length == observed.Length,
                    "Legacy ingested physical geometry remains the original observation.");
            }
            Check(rawGeometry(rawP12) == legacyGeometry, "Legacy raw geometry and endpoint observations remain unchanged after ingestion.");

            // 2. Resolver fail-closed behavior for edge cases & conflicts
            var feet = CreateFeetValue(0).Unit;
            var meters = new SourceUnitIdentity("length", "m", "metres", "m");

            // Valid circular with positive diameter and unproven zero Rise
            var res = EffectiveSectionHeightResolver.Resolve("CircularPipe", 2.0, feet, "PROVEN", 0.0, feet, "UNPROVEN");
            Check(res.IsProven && res.EffectiveHeight == 2.0 &&
                  res.Provenance == "DERIVED_FROM_CIRCULAR_DIAMETER" && res.EffectiveHeightUnit.Equals(feet), "Resolver derives circular height from diameter.");

            // Conflicting positive source Rise (e.g., Rise=3.0 vs Diameter=2.0)
            var conflict = EffectiveSectionHeightResolver.Resolve("CircularPipe", 2.0, feet, "PROVEN", 3.0, feet, "PROVEN");
            Check(!conflict.IsProven && conflict.EffectiveHeight == null &&
                  conflict.IsConflict, "Conflicting positive Rise is rejected with ConflictWithObservedRise.");

            // Matching positive source Rise (e.g., Rise=2.0 vs Diameter=2.0)
            var matching = EffectiveSectionHeightResolver.Resolve("CircularPipe", 2.0, feet, "PROVEN", 2.0, feet, "PROVEN");
            Check(matching.IsProven && matching.EffectiveHeight == 2.0 &&
                  matching.Provenance == "VERIFIED_CIRCULAR_SECTION_HEIGHT", "Matching positive Rise adopts verified circular provenance.");

            // Missing or non-positive diameter
            Check(!EffectiveSectionHeightResolver.Resolve("CircularPipe", null, feet, "PROVEN", 0, feet, "UNPROVEN").IsProven, "Null diameter fails closed.");
            Check(!EffectiveSectionHeightResolver.Resolve("CircularPipe", 0, feet, "PROVEN", 0, feet, "UNPROVEN").IsProven, "Zero diameter fails closed.");
            Check(!EffectiveSectionHeightResolver.Resolve("CircularPipe", -1.5, feet, "PROVEN", 0, feet, "UNPROVEN").IsProven, "Negative diameter fails closed.");
            Check(!EffectiveSectionHeightResolver.Resolve("CircularPipe", double.NaN, feet, "PROVEN", 0, feet, "UNPROVEN").IsProven, "NaN diameter fails closed.");

            // Incompatible units
            Check(!EffectiveSectionHeightResolver.Resolve("CircularPipe", 2.0, meters, "PROVEN", 0, feet, "UNPROVEN").IsProven, "Meters diameter fails closed.");
            Check(!EffectiveSectionHeightResolver.Resolve("CircularPipe", 2.0, null, "PROVEN", 0, feet, "UNPROVEN").IsProven, "Null unit fails closed.");

            // Unproven diameter classification
            Check(!EffectiveSectionHeightResolver.Resolve("CircularPipe", 2.0, feet, "UNPROVEN", 0, feet, "UNPROVEN").IsProven, "Unproven diameter fails closed.");

            // Non-circular shapes fail closed when Rise is unproven/0
            Check(!EffectiveSectionHeightResolver.Resolve("BoxPipe", 2.0, feet, "PROVEN", 0, feet, "UNPROVEN").IsProven, "BoxPipe with unproven 0 Rise fails closed.");
            Check(!EffectiveSectionHeightResolver.Resolve("ArchPipe", 2.0, feet, "PROVEN", 0, feet, "UNPROVEN").IsProven, "ArchPipe with unproven 0 Rise fails closed.");
            Check(!EffectiveSectionHeightResolver.Resolve(null, 2.0, feet, "PROVEN", 0, feet, "UNPROVEN").IsProven, "Null shape fails closed.");

            // 3. P13 AUTO-02 preparation integration with circular pipes
            // Same explicit TEST_ONLY rebinding as V2ChangedSource; expose the set to assert its current lineage.
            Func<EngineeringDesignWorkspace, GravityNetworkSnapshot, GravityVerticalDesignConstraintSet> currentTestConstraints = (workspace, current) =>
            {
                var builder = new GravityVerticalDesignConstraintSetBuilder();
                foreach (var orientation in workspace.Constraints.Orientations.Values)
                    builder.SetOrientation(orientation.ConduitId, orientation.FromNodeId, orientation.ToNodeId,
                        Ui02Provenance("TEST_ONLY.source.orientation." + orientation.ConduitId));
                var set = builder.Build(current.NetworkFingerprint, workspace.Basis.CommonDesignBasis.DesignBasisId,
                    workspace.Basis.CommonDesignBasis.DesignBasisVersion);
                Check(!string.IsNullOrWhiteSpace(current.NetworkFingerprint) && set.SourceNetworkFingerprint == current.NetworkFingerprint,
                    "Reconstructed TEST_ONLY constraints bind the current network fingerprint.");
                Check(set.Orientations.Count == workspace.Constraints.Orientations.Count && set.Orientations.Values.All(o =>
                    workspace.Constraints.TryGetOrientation(o.ConduitId, out var original) &&
                    o.FromNodeId == original.FromNodeId && o.ToNodeId == original.ToNodeId),
                    "Explicit TEST_ONLY rebinding preserves every orientation and endpoint identity.");
                return set;
            };
            var d = V2Draft();
            var originalBasis = GravityExecutionPlanBuilder.Json(d.Basis);
            var originalRequest = V2Request(d);
            var raw = d.Inputs.SourceRead;
            foreach (var c in raw.Conduits)
            {
                c.Shape = "CircularPipe";
                c.Diameter = 2.0;
                c.DiameterUnit = feet;
                c.PropertyClassifications["Diameter"] = "PROVEN";
                c.Rise = 0.0;
                c.RiseUnit = feet;
                c.PropertyClassifications["Rise"] = "UNPROVEN";
            }
            d.BindNetwork(raw, d.Inputs.ModelFingerprint);
            var currentNetwork = AutoLive(d);
            var staleReport = V2Prepare(d);
            Check(staleReport.ContextFailures.Contains("CONSTRAINT_SOURCE_STALE") && staleReport.Covers.Count == 0 &&
                staleReport.Items.Where(i => i.Kind == "Cover").All(i => i.Reasons.Contains("CONSTRAINT_SOURCE_STALE")),
                "Intentionally retained stale constraints remain rejected for every cover endpoint.");
            var currentSet = currentTestConstraints(d, currentNetwork);
            Check(currentNetwork.Conduits.All(c => c.ExistingPhysicalState.EffectiveSectionHeight == 2.0 &&
                feet.Equals(c.ExistingPhysicalState.EffectiveSectionHeightUnit) &&
                c.ExistingPhysicalState.SectionHeightDerivation == "DERIVED_FROM_CIRCULAR_DIAMETER" &&
                c.PropertyClassifications.TryGetValue("EffectiveSectionHeight", out var heightClass) && heightClass == "PROVEN" &&
                c.ExistingPhysicalState.Rise == 0.0), "Current circular source preserves proven Feet height and unchanged raw Rise.");
            var currentRequest = V2Request(d, network: currentNetwork, constraints: currentSet);
            Check(GravityExecutionPlanBuilder.Json(d.Basis) == originalBasis &&
                GravityExecutionPlanBuilder.Json(currentRequest.Standards) == GravityExecutionPlanBuilder.Json(originalRequest.Standards) &&
                GravityExecutionPlanBuilder.Json(currentRequest.SelectedStandards) == GravityExecutionPlanBuilder.Json(originalRequest.SelectedStandards),
                "Current-source fixture preserves Design Basis identity, criteria and selected standards.");
            var report = V2Prepare(d, currentRequest, currentNetwork, currentSet);
            Check(report.ContextFailures.Count == 0 && report.Criteria.All(c => c.Eligible),
                "Positive preparation has current source/basis/constraint context and valid explicit standards.");
            Check(report.Covers.Count == 4, "All 4 cover constraints generated for circular pipes with unproven 0 Rise.");
            Check(report.Covers.All(cov => cov.MinimumCover.NumericValue == 2.0 && cov.GroundElevation.NumericValue == 100.0), "Ground elevation and cover criteria preserved.");

            // Conflicting positive Rise fails closed in preparation
            var dConflict = V2Draft();
            var rawConf = dConflict.Inputs.SourceRead;
            var confConduit = rawConf.Conduits.First();
            confConduit.Shape = "CircularPipe";
            confConduit.Diameter = 2.0;
            confConduit.DiameterUnit = feet;
            confConduit.PropertyClassifications["Diameter"] = "PROVEN";
            confConduit.Rise = 3.0; // Conflicting positive Rise!
            confConduit.RiseUnit = feet;
            confConduit.PropertyClassifications["Rise"] = "PROVEN";
            dConflict.BindNetwork(rawConf, dConflict.Inputs.ModelFingerprint);
            var conflictNetwork = AutoLive(dConflict);
            var conflictSet = currentTestConstraints(dConflict, conflictNetwork);
            var rConf = V2Prepare(dConflict, V2Request(dConflict, network: conflictNetwork, constraints: conflictSet), conflictNetwork, conflictSet);
            Check(rConf.ContextFailures.Count == 0 && rConf.Criteria.All(c => c.Eligible) && !V2Reason(rConf, "CONSTRAINT_SOURCE_STALE"),
                "Conflict preparation uses current lineage and valid standards, not a stale-source rejection.");
            var conflictItems = rConf.Items.Where(i => i.Kind == "Cover" && i.ConduitId == confConduit.Id).ToArray();
            Check(conflictItems.Length == 2 && conflictItems.All(i => i.Status == "UNRESOLVED" && i.Reasons.Contains("SECTION_HEIGHT_CONFLICT")) &&
                dConflict.Inputs.SourceRead.Conduits.Single(c => c.Id == confConduit.Id).Rise == 3.0,
                "Both conflicting conduit ends reject the unchanged positive Rise specifically for SECTION_HEIGHT_CONFLICT.");
            Check(rConf.Covers.Count == 2 && V2Reason(rConf, "SECTION_HEIGHT_CONFLICT"), "Conflicting positive Rise fails closed with SECTION_HEIGHT_CONFLICT.");

            // 4. Verification that no candidate is automatically selected or approved and original observations unchanged
            Check(d.Inputs.Approval == null, "No production approval fabricated.");
            Check(d.Candidates == null, "No candidate automatically selected or generated.");
            foreach (var c in d.Inputs.SourceRead.Conduits)
            {
                Check(c.Rise == 0.0, "Source conduit Rise observation remains exactly 0.0.");
                Check(c.Diameter == 2.0, "Source conduit Diameter observation remains exactly 2.0.");
            }
        }
    }
}
