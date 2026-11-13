using System;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class GravityEngineeringIntakeRequest
    {
        public GravityEngineeringReadinessInput Context { get; set; }
        public GravityVerticalDesignConstraintSet Constraints { get; set; }
        public GravityCandidateGenerationRequest CandidateRequest { get; set; }
        public string SelectedRecipeId { get; set; }
    }
    public sealed class GravityEngineeringIntakeResult
    {
        public string Status { get; internal set; }
        public bool EngineeringContentValidated { get; internal set; }
        public string NetworkFingerprint { get; internal set; }
        public string ConstraintFingerprint { get; internal set; }
        public string CandidateFingerprint { get; internal set; }
        public string RecipeId { get; internal set; }
        public GravityEngineeringInputs PreparedInputs { get; internal set; }
        public GravityEngineeringReadiness Readiness { get; internal set; }
        public string Rejection { get; internal set; }
        public int RuntimeHostInvocations => 0;
    }
    // Offline operator/library intake. Generates evidence from supplied decisions, never approval or design values.
    internal static class GravityEngineeringIntake
    {
        private static void Require(bool value, string reason) => GravityExecutionPlanBuilder.Require(value, reason);
        private static string Json(object value) => GravityExecutionPlanBuilder.Json(value);
        private static string Normalize(string value) => Json(RuntimeHostProtocol.Serializer().DeserializeObject(value));
        internal static GravityEngineeringIntakeResult Review(GravityEngineeringIntakeRequest request, IGravityEngineeringApprovalVerifier verifier)
        {
            var context = request?.Context;
            GravityEngineeringInputs input;
            try { input = context?.EngineeringInputs == null ? new GravityEngineeringInputs() :
                RuntimeHostProtocol.Serializer().Deserialize<GravityEngineeringInputs>(Json(context.EngineeringInputs)); }
            catch (Exception error)
            {
                var invalid = new GravityEngineeringIntakeResult { Status = "REJECTED_ENGINEERING_INPUT", Rejection = error.ToString(),
                    Readiness = GravityEngineeringReadinessAssessment.Assess(new GravityEngineeringReadinessInput {
                        OperatingMode = context?.OperatingMode ?? GravityOperatingMode.Enterprise, Calculate = context?.Calculate ?? false,
                        SourceKind = context?.SourceKind ?? GravityReadinessSourceKind.Unavailable, ModelName = context?.ModelName }, verifier) };
                invalid.Readiness.BlockingReasons.Add(new GravityReadinessBlocker { Code = "ENGINEERING_INTAKE_REJECTED",
                    Detail = invalid.Rejection, ConduitIds = new int[0], NodeIds = new int[0] });
                return invalid;
            }
            var result = new GravityEngineeringIntakeResult { Status = "INCOMPLETE_ENGINEERING_INPUT", PreparedInputs = input };
            var detachedContext = new GravityEngineeringReadinessInput { EngineeringInputs = input,
                OperatingMode = context?.OperatingMode ?? GravityOperatingMode.Enterprise, Calculate = context?.Calculate ?? false,
                SourceKind = context?.SourceKind ?? GravityReadinessSourceKind.Unavailable, ModelName = context?.ModelName,
                HistoricalNetworkFingerprint = context?.HistoricalNetworkFingerprint, SourceEvidenceReference = context?.SourceEvidenceReference };
            try
            {
                if (request?.Constraints != null)
                {
                    string explicitConstraints = Json(request.Constraints);
                    Require(string.IsNullOrWhiteSpace(input.ConstraintsJson) || Normalize(input.ConstraintsJson) == explicitConstraints, "INTAKE_CONSTRAINT_INPUT_MISMATCH");
                    input.ConstraintsJson = explicitConstraints;
                }
                if (input.SourceRead == null || input.DesignBasis == null || string.IsNullOrWhiteSpace(input.ModelFingerprint) ||
                    string.IsNullOrWhiteSpace(input.ConstraintsJson)) return Complete(result, detachedContext, verifier);
                GravityExecutionPlanBuilder.ValidateReviewedBasis(input);
                var network = new GravityNetworkIngestionService().Ingest(input.SourceRead, input.ModelFingerprint, schemaVersion: 2);
                Require(network.ScenarioContext.ScenarioId > 0 && network.ScenarioContext.PhysicalAlternativeId > 0, "SOURCE_CONTEXT");
                var constraints = GravityExecutionPlanBuilder.ValidateEngineeringConstraints(input, network);
                result.NetworkFingerprint = network.NetworkFingerprint; result.ConstraintFingerprint = constraints.VerticalConstraintFingerprint;
                var feasibility = new GravityVerticalFeasibilityEngine().Analyze(network, constraints);
                Require(string.IsNullOrWhiteSpace(input.FeasibilityJson) || Normalize(input.FeasibilityJson) == Json(feasibility), "FEASIBILITY_LINEAGE");
                input.FeasibilityJson = Json(feasibility);
                Require((feasibility.Status == VerticalFeasibilityStatus.Feasible || feasibility.Status == VerticalFeasibilityStatus.FeasibleMultipleSolutions) &&
                    feasibility.Conflicts.Count == 0 && feasibility.DecisionRequests.Count == 0 && feasibility.IncompleteItems.Count == 0, "FEASIBILITY");
                string requestJson = request.CandidateRequest == null ? input.GenerationRequestJson : Json(request.CandidateRequest);
                Require(request.CandidateRequest == null || string.IsNullOrWhiteSpace(input.GenerationRequestJson) ||
                    Normalize(input.GenerationRequestJson) == requestJson, "INTAKE_RECIPE_INPUT_MISMATCH");
                if (string.IsNullOrWhiteSpace(requestJson)) return Complete(result, detachedContext, verifier);
                var generation = GravityEngineeringWire.Read<GravityCandidateGenerationRequest>(RuntimeHostProtocol.Serializer().DeserializeObject(requestJson));
                GravityExecutionPlanBuilder.ValidateEngineeringGeneration(generation, constraints, feasibility);
                input.GenerationRequestJson = Json(generation);
                var generated = new GravityCandidateGenerator().Generate(network, constraints, feasibility, generation);
                Require(generated.Candidates.Count > 0, "CANDIDATE_RECONSTRUCTION");
                Require(!string.IsNullOrWhiteSpace(request.SelectedRecipeId) ||
                    (generated.Candidates.Count == 1 && generation.Recipes.Count <= 1), "INTAKE_CANDIDATE_SELECTION_REQUIRED");
                var selected = string.IsNullOrWhiteSpace(request.SelectedRecipeId) ? generated.Candidates.Single() :
                    generated.Candidates.SingleOrDefault(c => c.RecipeId == request.SelectedRecipeId);
                Require(selected != null, "INTAKE_CANDIDATE_SELECTION");
                Require(string.IsNullOrWhiteSpace(input.CandidateJson) || Normalize(input.CandidateJson) == Json(selected), "CANDIDATE_RECONSTRUCTION");
                GravityCandidateExecutor.ValidateLineage(selected, network.NetworkFingerprint, constraints.VerticalConstraintFingerprint);
                Require(selected.EndpointDesigns.Count == network.Conduits.Count * 2 &&
                    selected.EndpointDesigns.Select(e => e.ConduitEnd).Distinct().Count() == selected.EndpointDesigns.Count, "ENDPOINT_COVERAGE");
                foreach (var endpoint in selected.EndpointDesigns)
                {
                    var c = network.Conduits.Single(n => n.SourceElementId == endpoint.ConduitId);
                    bool start = endpoint.End == ConduitEndKind.Start;
                    var original = start ? c.ExistingPhysicalState.StartInvert : c.ExistingPhysicalState.StopInvert;
                    var unit = start ? c.ExistingPhysicalState.StartInvertUnit : c.ExistingPhysicalState.StopInvertUnit;
                    var inherited = start ? c.ExistingPhysicalState.SetInvertToStart : c.ExistingPhysicalState.SetInvertToStop;
                    Require(endpoint.NodeId == (start ? c.StartNodeId : c.StopNodeId) && original.HasValue &&
                        inherited.HasValue &&
                        GravityExecutionPlanBuilder.Finite(original.Value) && endpoint.ExistingInvert == original &&
                        GravityExecutionPlanBuilder.Finite(endpoint.ProposedInvert), "ENDPOINT_SOURCE");
                    var feet = new SourceUnitIdentity("length", "Feet", "feet", "ft");
                    Require(feet.Equals(unit) && feet.Equals(endpoint.Unit), "UNSUPPORTED_UNIT");
                }
                input.CandidateJson = Json(selected); result.CandidateFingerprint = selected.CandidateFingerprint; result.RecipeId = selected.RecipeId;
                result.EngineeringContentValidated = true; result.Status = "DESIGN_READY_APPROVAL_PENDING";
            }
            catch (Exception error) { result.Status = "REJECTED_ENGINEERING_INPUT"; result.Rejection = error.ToString(); }
            return Complete(result, detachedContext, verifier);
        }
        private static GravityEngineeringIntakeResult Complete(GravityEngineeringIntakeResult result, GravityEngineeringReadinessInput context,
            IGravityEngineeringApprovalVerifier verifier)
        {
            result.Readiness = GravityEngineeringReadinessAssessment.Assess(context, verifier);
            if (result.Rejection != null) result.Readiness.BlockingReasons.Add(new GravityReadinessBlocker {
                Code = "ENGINEERING_INTAKE_REJECTED", Detail = result.Rejection,
                ConduitIds = context.EngineeringInputs.SourceRead?.Conduits?.Select(c => c.Id).OrderBy(id => id).ToArray() ?? new int[0],
                NodeIds = context.EngineeringInputs.SourceRead?.Structures?.Select(n => n.Id).OrderBy(id => id).ToArray() ?? new int[0] });
            result.Readiness.ProductionExecutionEligible = result.Readiness.ProductionExecutionEligible && result.EngineeringContentValidated && result.Rejection == null;
            if (result.EngineeringContentValidated && context.OperatingMode == GravityOperatingMode.SingleOperator &&
                result.Readiness.BlockingReasons.All(b => b.Code == "OPERATOR_CONFIRMATION_REQUIRED")) result.Status = "DESIGN_READY_OPERATOR_CONFIRMATION_REQUIRED";
            if (result.EngineeringContentValidated && result.Readiness.BlockingReasons.Any(b => b.Code == "ENGINEERING_PLAN_NOT_VERIFIED"))
                result.Status = "REJECTED_ENGINEERING_APPROVAL";
            else if (result.EngineeringContentValidated && result.Readiness.ApprovalVerificationStatus == "TEST_ONLY_NON_ENGINEERING") result.Status = "TEST_ONLY_NON_ENGINEERING";
            else if (result.EngineeringContentValidated && result.Readiness.ApprovalVerificationStatus == "VERIFIED") result.Status =
                result.Readiness.ProductionExecutionEligible ? "ENGINEERING_EXECUTION_ELIGIBLE" : "APPROVAL_VERIFIED_RUNTIME_PROVIDER_MISSING";
            return result;
        }
    }
}
