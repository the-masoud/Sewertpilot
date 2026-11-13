using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    // This is an explicit engineer decision record, independent of P5 technical write permission.
    // This DTO is lineage input only. Trusted local verification is independently required by execution boundaries.
    public sealed class GravityExecutionApproval
    {
        public string ApprovalId { get; set; }
        public string ActorId { get; set; }
        public string DecisionReference { get; set; }
        public string ApprovedUtc { get; set; }
        public bool ExecuteEngineeringDesign { get; set; }
        public bool Calculate { get; set; }
        public string ProjectId { get; set; }
        public string ModelFingerprint { get; set; }
        public string DesignBasisId { get; set; }
        public string DesignBasisVersion { get; set; }
        public string DesignBasisFingerprint { get; set; }
        public string NetworkFingerprint { get; set; }
        public string ConstraintFingerprint { get; set; }
        public string CandidateId { get; set; }
        public string CandidateFingerprint { get; set; }
        public string RecipeId { get; set; }
    }

    public sealed class GravityEngineeringInputs
    {
        public string ProjectId { get; set; }
        public string ModelFingerprint { get; set; }
        public NetworkReadResult SourceRead { get; set; }
        public GravityFlowDesignBasis DesignBasis { get; set; }
        public string ConstraintsJson { get; set; }
        public string FeasibilityJson { get; set; }
        public string GenerationRequestJson { get; set; }
        public string CandidateJson { get; set; }
        public GravityExecutionApproval Approval { get; set; }
    }

    public sealed class GravityPlannedEndpoint
    {
        public int ConduitId { get; set; }
        public ConduitEndKind End { get; set; }
        public int NodeId { get; set; }
        public double Original { get; set; }
        public double Proposed { get; set; }
        public SourceUnitIdentity Unit { get; set; }
        public bool OriginalInherited { get; set; }
        public bool Changed { get; set; }
        public bool Localize { get; set; }
    }

    public sealed class GravityExecutionPlan
    {
        public int SchemaVersion { get; set; } = 1;
        public GravityEngineeringInputs Inputs { get; set; }
        public string NetworkFingerprint { get; set; }
        public string ConstraintFingerprint { get; set; }
        public string CandidateFingerprint { get; set; }
        public string DesignBasisFingerprint { get; set; }
        public string CandidateId { get; set; }
        public string RecipeId { get; set; }
        public int SourceScenarioId { get; set; }
        public int SourcePhysicalAlternativeId { get; set; }
        public List<GravityPlannedEndpoint> Endpoints { get; set; } = new List<GravityPlannedEndpoint>();
        public int EndpointWriteCount { get; set; }
        public int LocalizationWriteCount { get; set; }
        public int TotalFieldWriteCount { get; set; }
        public bool Calculate { get; set; }
        public bool NoChanges { get; set; }
        public string PlanFingerprint { get; set; }
    }

    // Rehydrates only the enumerated owned immutable P13/P14 types, through their validated constructors.
    // No type names from JSON, vendor references, or general object activation are accepted.
    internal static class GravityEngineeringWire
    {
        private static readonly Type[] Immutable = { typeof(GravityDesignOrientation), typeof(GravityConduitEnd),
            typeof(FixedEndpointElevationConstraint), typeof(ProtectedNodeElevationConstraint), typeof(OutfallVerticalConstraint),
            typeof(VerticalMatchConstraint), typeof(VerticalCoverConstraint), typeof(VerticalSlopeConstraint),
            typeof(SourceEngineeringValue), typeof(GravityCandidateRecipe), typeof(CandidateEndpointSelection),
            typeof(GravityCandidateGenerationRequest) };
        internal static T Read<T>(object value) => (T)Read(typeof(T), value);
        private static object Read(Type type, object value)
        {
            if (value == null) return null;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                Type item = type.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item));
                foreach (object row in (IEnumerable)value) list.Add(Read(item, row));
                return list;
            }
            if (!Immutable.Contains(type)) return RuntimeHostProtocol.Serializer().ConvertToType(value, type);
            var fields = new Dictionary<string, object>((Dictionary<string, object>)value, StringComparer.OrdinalIgnoreCase);
            var constructor = type.GetConstructors().Single();
            object[] args = constructor.GetParameters().Select(p => {
                if (!fields.TryGetValue(p.Name, out object field))
                    throw new InvalidOperationException("P15_BLOCKED_ENGINEERING_WIRE: " + type.Name + "." + p.Name);
                return Read(p.ParameterType, field);
            }).ToArray();
            return constructor.Invoke(args);
        }
        internal static GravityVerticalDesignConstraintSet Constraints(string json)
        {
            var fields = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(json);
            var orientations = ((Dictionary<string, object>)fields["Orientations"]).ToDictionary(
                p => int.Parse(p.Key, CultureInfo.InvariantCulture), p => Read<GravityDesignOrientation>(p.Value));
            return new GravityVerticalDesignConstraintSet((string)fields["SourceNetworkFingerprint"],
                (string)fields["DesignBasisId"], (string)fields["DesignBasisVersion"], orientations,
                Read<IEnumerable<FixedEndpointElevationConstraint>>(fields["FixedConstraints"]),
                Read<IEnumerable<ProtectedNodeElevationConstraint>>(fields["ProtectedConstraints"]),
                Read<IEnumerable<OutfallVerticalConstraint>>(fields["OutfallConstraints"]),
                Read<IEnumerable<VerticalMatchConstraint>>(fields["MatchConstraints"]),
                Read<IEnumerable<VerticalCoverConstraint>>(fields["CoverConstraints"]),
                Read<IEnumerable<VerticalSlopeConstraint>>(fields["SlopeConstraints"]));
        }
    }

    public static class GravityExecutionPlanBuilder
    {
        internal static void Require(bool value, string reason)
        { if (!value) throw new InvalidOperationException("P15_BLOCKED_" + reason); }
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        // Reflection property-cache order can change after a read. Compare owned JSON by ordinal member order,
        // preserving array order and every value; serialization still uses the established protocol serializer.
        internal static string Json(object value) => CanonicalJson(RuntimeHostProtocol.Serializer().Serialize(value));
        private static string CanonicalJson(string json) => RuntimeHostProtocol.Serializer().Serialize(
            Canonical(RuntimeHostProtocol.Serializer().DeserializeObject(json)));
        private static object Canonical(object value)
        {
            if (value is IDictionary<string, object> fields)
                return new SortedDictionary<string, object>(fields.ToDictionary(p => p.Key, p => Canonical(p.Value)), StringComparer.Ordinal);
            if (value is IEnumerable rows && !(value is string)) return rows.Cast<object>().Select(Canonical).ToArray();
            return value;
        }
        internal static string Hash(string value)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        public static GravityExecutionPlan Build(GravityNetworkSnapshot authoritativeNetwork, NetworkReadResult sourceRead,
            GravityFlowDesignBasis basis, GravityVerticalDesignConstraintSet constraints, VerticalFeasibilityResult feasibility,
            GravityCandidateGenerationRequest generation, GravityDesignCandidate candidate, GravityExecutionApproval approval)
        {
            Require(authoritativeNetwork != null && authoritativeNetwork.SchemaVersion == 2 && basis?.CommonDesignBasis != null,
                "AUTHORITATIVE_NETWORK");
            var actual = new GravityNetworkIngestionService().Ingest(sourceRead, authoritativeNetwork.SourceModelFingerprint, schemaVersion: 2);
            Require(actual.NetworkFingerprint == authoritativeNetwork.NetworkFingerprint &&
                GravityNetworkSnapshot.ComputeFingerprint(2, authoritativeNetwork.SourceModelFingerprint, authoritativeNetwork.ScenarioContext,
                    authoritativeNetwork.Nodes, authoritativeNetwork.Conduits) == authoritativeNetwork.NetworkFingerprint, "AUTHORITATIVE_NETWORK_FINGERPRINT");
            return Build(new GravityEngineeringInputs { ProjectId = basis.CommonDesignBasis.ProjectId,
                ModelFingerprint = authoritativeNetwork.SourceModelFingerprint, SourceRead = sourceRead, DesignBasis = basis,
                ConstraintsJson = Json(constraints), FeasibilityJson = Json(feasibility), GenerationRequestJson = Json(generation),
                CandidateJson = Json(candidate), Approval = approval });
        }
        public static GravityExecutionPlan Build(GravityEngineeringInputs supplied)
        {
            Require(supplied != null && supplied.DesignBasis != null && supplied.Approval != null, "EXTERNAL_ENGINEERING_INPUT");
            return BuildCore(supplied, supplied.Approval.Calculate, false);
        }
        public static GravityExecutionPlan BuildSingleOperator(GravityEngineeringInputs supplied, bool calculate)
        {
            Require(supplied != null && supplied.DesignBasis != null && supplied.Approval == null, "EXTERNAL_ENGINEERING_INPUT");
            return BuildCore(supplied, calculate, true);
        }
        private static GravityExecutionPlan BuildCore(GravityEngineeringInputs supplied, bool calculate, bool singleOperator)
        {
            // Detach all mutable DTOs, including provenance and approval, before validating them.
            var input = RuntimeHostProtocol.Serializer().Deserialize<GravityEngineeringInputs>(Json(supplied));
            var basis = input.DesignBasis;
            ValidateReviewedBasis(input);
            Require(!string.IsNullOrWhiteSpace(input.ModelFingerprint) && input.SourceRead != null, "SOURCE_IDENTITY");
            var network = new GravityNetworkIngestionService().Ingest(input.SourceRead, input.ModelFingerprint, schemaVersion: 2);
            Require(network.ScenarioContext.ScenarioId > 0 && network.ScenarioContext.PhysicalAlternativeId > 0 &&
                network.Conduits.All(c => c.SourceElementId > 0 && c.StartNodeId != c.StopNodeId), "SOURCE_CONTEXT");
            var constraints = ValidateEngineeringConstraints(input, network);
            var feasibility = new GravityVerticalFeasibilityEngine().Analyze(network, constraints);
            Require((feasibility.Status == VerticalFeasibilityStatus.Feasible || feasibility.Status == VerticalFeasibilityStatus.FeasibleMultipleSolutions) &&
                feasibility.Conflicts.Count == 0 && feasibility.DecisionRequests.Count == 0 && feasibility.IncompleteItems.Count == 0,
                "FEASIBILITY");
            Require(Json(feasibility) == CanonicalJson(input.FeasibilityJson), "FEASIBILITY_LINEAGE");
            var generation = GravityEngineeringWire.Read<GravityCandidateGenerationRequest>(
                RuntimeHostProtocol.Serializer().DeserializeObject(input.GenerationRequestJson));
            ValidateEngineeringGeneration(generation, constraints, feasibility);
            var generated = new GravityCandidateGenerator().Generate(network, constraints, feasibility, generation);
            // Compare the entire regenerated candidate: endpoints, topology, derived states, recipe, and provenance.
            var candidate = generated.Candidates.SingleOrDefault(c => Json(c) == CanonicalJson(input.CandidateJson));
            Require(candidate != null, "CANDIDATE_RECONSTRUCTION");
            GravityCandidateExecutor.ValidateLineage(candidate, network.NetworkFingerprint, constraints.VerticalConstraintFingerprint);
            Require(candidate.EndpointDesigns.Count == network.Conduits.Count * 2 &&
                candidate.EndpointDesigns.Select(e => e.ConduitEnd).Distinct().Count() == candidate.EndpointDesigns.Count, "ENDPOINT_COVERAGE");
            var plan = new GravityExecutionPlan { SchemaVersion = singleOperator ? 2 : 1, Inputs = input, NetworkFingerprint = network.NetworkFingerprint,
                ConstraintFingerprint = constraints.VerticalConstraintFingerprint, CandidateFingerprint = candidate.CandidateFingerprint,
                CandidateId = candidate.CandidateId, RecipeId = candidate.RecipeId,
                DesignBasisFingerprint = basis.CreateSnapshot().GravityContentFingerprint,
                SourceScenarioId = network.ScenarioContext.ScenarioId, SourcePhysicalAlternativeId = network.ScenarioContext.PhysicalAlternativeId,
                Calculate = calculate };
            foreach (var endpoint in candidate.EndpointDesigns.OrderBy(e => e.ConduitId).ThenBy(e => e.End))
            {
                var conduit = network.Conduits.Single(c => c.SourceElementId == endpoint.ConduitId);
                var source = conduit.ExistingPhysicalState;
                bool start = endpoint.End == ConduitEndKind.Start;
                Require(endpoint.NodeId == (start ? conduit.StartNodeId : conduit.StopNodeId), "ENDPOINT_TOPOLOGY");
                var unit = start ? source.StartInvertUnit : source.StopInvertUnit;
                var original = start ? source.StartInvert : source.StopInvert;
                var inherited = start ? source.SetInvertToStart : source.SetInvertToStop;
                var feet = new SourceUnitIdentity("length", "Feet", "feet", "ft");
                Require(feet.Equals(unit) && feet.Equals(endpoint.Unit), "UNSUPPORTED_UNIT");
                Require(original.HasValue && Finite(original.Value) && Finite(endpoint.ProposedInvert) && inherited.HasValue &&
                    endpoint.ExistingInvert == original, "ENDPOINT_SOURCE");
                bool changed = original.Value != endpoint.ProposedInvert;
                plan.Endpoints.Add(new GravityPlannedEndpoint { ConduitId = endpoint.ConduitId, End = endpoint.End, NodeId = endpoint.NodeId,
                    Original = original.Value, Proposed = endpoint.ProposedInvert, OriginalInherited = inherited.Value,
                    Unit = unit, Changed = changed, Localize = changed && inherited.Value });
            }
            plan.EndpointWriteCount = plan.Endpoints.Count(e => e.Changed);
            plan.LocalizationWriteCount = plan.Endpoints.Count(e => e.Localize);
            plan.TotalFieldWriteCount = checked(plan.EndpointWriteCount + plan.LocalizationWriteCount);
            plan.NoChanges = plan.EndpointWriteCount == 0;
            if (!singleOperator) ValidateApproval(plan);
            plan.PlanFingerprint = Hash(Json(plan));
            return plan;
        }
        public static void Validate(GravityExecutionPlan plan)
        {
            Require(plan != null && plan.SchemaVersion == 1, "PLAN_SCHEMA");
            var reconstructed = Build(plan.Inputs);
            Require(Json(reconstructed) == Json(plan), "PLAN_RECONSTRUCTION");
        }
        public static void ValidateSingleOperator(GravityExecutionPlan plan)
        {
            Require(plan != null && plan.SchemaVersion == 2, "SINGLE_OPERATOR_PLAN_SCHEMA");
            var reconstructed = BuildSingleOperator(plan.Inputs, plan.Calculate);
            Require(Json(reconstructed) == Json(plan), "PLAN_RECONSTRUCTION");
        }
        internal static void ValidateEngineeringPlan(GravityExecutionPlan plan)
        {
            if (plan?.SchemaVersion == 2) ValidateSingleOperator(plan);
            else Validate(plan);
        }
        private static void ValidateApproval(GravityExecutionPlan plan)
        {
            var a = plan.Inputs.Approval; var b = plan.Inputs.DesignBasis.CommonDesignBasis;
            Require(a.ExecuteEngineeringDesign && !string.IsNullOrWhiteSpace(a.ApprovalId) && !string.IsNullOrWhiteSpace(a.ActorId) &&
                !string.IsNullOrWhiteSpace(a.DecisionReference) && DateTimeOffset.TryParse(a.ApprovedUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTimeOffset approved) && approved <= DateTimeOffset.UtcNow &&
                a.ProjectId == plan.Inputs.ProjectId && a.ModelFingerprint == plan.Inputs.ModelFingerprint &&
                a.DesignBasisId == b.DesignBasisId && a.DesignBasisVersion == b.DesignBasisVersion &&
                a.DesignBasisFingerprint == plan.DesignBasisFingerprint && a.NetworkFingerprint == plan.NetworkFingerprint &&
                a.ConstraintFingerprint == plan.ConstraintFingerprint && a.CandidateId == plan.CandidateId &&
                a.CandidateFingerprint == plan.CandidateFingerprint && a.RecipeId == plan.RecipeId, "ENGINEERING_EXECUTION_APPROVAL");
        }
        internal static void ValidateReviewedBasis(GravityEngineeringInputs input)
        {
            var basis = input.DesignBasis;
            Require(basis != null && basis.Validate().IsValid && basis.CommonDesignBasis.ReviewState == DesignBasisReviewState.Reviewed &&
                !string.IsNullOrWhiteSpace(input.ProjectId) && basis.CommonDesignBasis.ProjectId == input.ProjectId, "DESIGN_BASIS");
            var criteria = basis.EnumerateSectionsInCanonicalOrder().SelectMany(s => s.Criteria).ToArray();
            Require(criteria.All(c => c.Requirement != GravityCriterionRequirement.Required ||
                (c.Status == GravityCriterionStatus.Defined && c.Value != null && c.Provenance != null)), "UNRESOLVED_CRITERION");
            foreach (var criterion in criteria.Where(c => c.Status == GravityCriterionStatus.Defined))
                ValidateEngineeringProvenance(criterion.Provenance);
        }
        internal static void ValidateEngineeringProvenance(StandardProvenance p)
        {
            Require(p != null && Enum.IsDefined(typeof(StandardSourceLayer), p.SourceLayer) &&
                !string.IsNullOrWhiteSpace(p.ParameterKey) &&
                (!string.IsNullOrWhiteSpace(p.StandardId) || !string.IsNullOrWhiteSpace(p.DocumentReference)), "ENGINEERING_PROVENANCE");
        }
        internal static GravityVerticalDesignConstraintSet ValidateEngineeringConstraints(GravityEngineeringInputs input, GravityNetworkSnapshot network)
        {
            var constraints = GravityEngineeringWire.Constraints(input.ConstraintsJson);
            var basis = input.DesignBasis.CommonDesignBasis;
            Require(constraints.SourceNetworkFingerprint == network.NetworkFingerprint && constraints.DesignBasisId == basis.DesignBasisId &&
                constraints.DesignBasisVersion == basis.DesignBasisVersion, "CONSTRAINT_LINEAGE");
            var stored = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(input.ConstraintsJson);
            Require((string)stored["VerticalConstraintFingerprint"] == constraints.VerticalConstraintFingerprint, "CONSTRAINT_FINGERPRINT");
            ValidateBasisDecisions(input.DesignBasis, constraints);
            ValidateConstraintTopology(network, constraints);
            return constraints;
        }
        internal static void ValidateEngineeringGeneration(GravityCandidateGenerationRequest generation,
            GravityVerticalDesignConstraintSet constraints, VerticalFeasibilityResult feasibility)
        {
            Require(generation != null && generation.SourceConstraintFingerprint == constraints.VerticalConstraintFingerprint, "RECIPE_LINEAGE");
            Require(feasibility.Status != VerticalFeasibilityStatus.FeasibleMultipleSolutions || generation.Recipes.Count > 0, "RECIPE_REQUIRED");
            Require(generation.Recipes.Select(r => r.RecipeId).Distinct(StringComparer.Ordinal).Count() == generation.Recipes.Count &&
                generation.Recipes.All(r => r.EndpointSelections.Select(e => e.ConduitEnd).Distinct().Count() == r.EndpointSelections.Count), "DUPLICATE_RECIPE_ENDPOINT");
            foreach (var recipe in generation.Recipes)
            {
                ValidateEngineeringProvenance(recipe.Provenance);
                foreach (var endpoint in recipe.EndpointSelections) ValidateEngineeringProvenance(endpoint.Provenance);
            }
        }
        private static void ValidateConstraintTopology(GravityNetworkSnapshot network, GravityVerticalDesignConstraintSet c)
        {
            Action<SourceEngineeringValue> quantity = value => Require(value != null && value.NumericValue.HasValue &&
                Finite(value.NumericValue.Value) && new SourceUnitIdentity("length", "Feet", "feet", "ft").Equals(value.Unit), "CONSTRAINT_QUANTITY");
            Action<GravityConduitEnd> end = e => Require(e != null && network.Conduits.Any(p => p.SourceElementId == e.ConduitId &&
                (e.End == ConduitEndKind.Start ? p.StartNodeId : p.StopNodeId) == e.NodeId), "CONSTRAINT_TOPOLOGY");
            foreach (var e in c.FixedConstraints) { end(e.ConduitEnd); quantity(e.InvertElevation); }
            foreach (var e in c.CoverConstraints) { end(e.ConduitEnd); quantity(e.GroundElevation); quantity(e.MinimumCover); }
            foreach (var e in c.MatchConstraints) foreach (var p in e.ParticipatingConduitEnds) end(p);
            foreach (var e in c.OutfallConstraints)
            {
                Require(network.Nodes.Any(n => n.SourceElementId == e.OutfallNodeId && n.Kind == GravityNodeKind.Outfall), "OUTFALL_TOPOLOGY");
                if (e.ProtectedInvert != null) quantity(e.ProtectedInvert);
                if (e.ConnectedEndpointConstraint != null) { end(e.ConnectedEndpointConstraint.ConduitEnd); quantity(e.ConnectedEndpointConstraint.InvertElevation); Require(e.ConnectedEndpointConstraint.ConduitEnd.NodeId == e.OutfallNodeId, "OUTFALL_TOPOLOGY"); }
            }
            foreach (var e in c.ProtectedConstraints) { quantity(e.Elevation); Require(network.Nodes.Any(n => n.SourceElementId == e.NodeId), "PROTECTED_NODE_TOPOLOGY"); }
            foreach (var e in c.Orientations.Values) Require(network.Conduits.Any(n => n.SourceElementId == e.ConduitId), "ORIENTATION_TOPOLOGY");
            foreach (var e in c.SlopeConstraints) Require(network.Conduits.Any(n => n.SourceElementId == e.ConduitId) &&
                (!e.MinimumSlope.HasValue || Finite(e.MinimumSlope.Value)) && (!e.MaximumSlope.HasValue || Finite(e.MaximumSlope.Value)), "SLOPE_TOPOLOGY");
        }
        internal static IEnumerable<object> ConstraintDecisions(GravityVerticalDesignConstraintSet c) =>
            c.Orientations.Values.Cast<object>().Concat(c.FixedConstraints).Concat(c.ProtectedConstraints)
                .Concat(c.OutfallConstraints).Concat(c.MatchConstraints).Concat(c.CoverConstraints).Concat(c.SlopeConstraints);
        private static void ValidateBasisDecisions(GravityFlowDesignBasis basis, GravityVerticalDesignConstraintSet constraints)
        {
            // P10 permits typed project criteria. Structured vertical decisions are explicit Text values containing
            // the canonical owned P13 decision JSON, keyed by that decision's provenance ParameterKey.
            // This binds the actual values, endpoints, units and boundary choices to reviewed Design Basis content;
            // an unrelated valid document with matching ID/version cannot supply engineering authority.
            foreach (object decision in ConstraintDecisions(constraints))
            {
                var provenance = (StandardProvenance)decision.GetType().GetProperty("Provenance").GetValue(decision, null);
                Require(provenance != null && !string.IsNullOrWhiteSpace(provenance.ParameterKey), "CONSTRAINT_BASIS_PROVENANCE");
                var criteria = basis.VerticalDesign.Criteria.Where(c => string.Equals(c.Key, provenance.ParameterKey, StringComparison.OrdinalIgnoreCase)).ToArray();
                Require(criteria.Length == 1 && criteria[0].Status == GravityCriterionStatus.Defined &&
                    criteria[0].Value?.Kind == StandardValueKind.Text && CanonicalJson(criteria[0].Value.TextValue) == Json(decision) &&
                    Json(criteria[0].Provenance) == Json(provenance), "CONSTRAINT_DESIGN_BASIS_DECISION");
            }
        }
    }
}
