using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum GravityReadinessSourceKind { Unavailable = 0, SuppliedP12Snapshot = 1, Historical = 2, TestOnly = 3 }
    public sealed class GravityEngineeringReadinessInput
    {
        public GravityEngineeringInputs EngineeringInputs { get; set; }
        public GravityReadinessSourceKind SourceKind { get; set; }
        public string HistoricalNetworkFingerprint { get; set; }
        public string ModelName { get; set; }
        public string SourceEvidenceReference { get; set; }
        public GravityOperatingMode OperatingMode { get; set; } = GravityOperatingMode.Enterprise;
        public bool Calculate { get; set; }
    }
    public sealed class GravityReadinessBlocker
    {
        public string Code { get; internal set; }
        public string Detail { get; internal set; }
        public int[] ConduitIds { get; internal set; }
        public int[] NodeIds { get; internal set; }
    }
    public sealed class GravityEngineeringReadiness
    {
        public string ProjectId { get; internal set; }
        public string ModelFingerprint { get; internal set; }
        public string ModelName { get; internal set; }
        public string SourceEvidenceReference { get; internal set; }
        public GravityReadinessSourceKind SourceKind { get; internal set; }
        public bool LiveRevalidatedByAssessment => false;
        public string NetworkFingerprint { get; internal set; }
        public string ReconstructedNetworkFingerprint { get; internal set; }
        public string DesignBasisReviewStatus { get; internal set; }
        public List<string> MissingEngineeringCriteria { get; internal set; } = new List<string>();
        public int[] MissingFlowOrientationConduitIds { get; internal set; }
        public int[] MissingOutfallDecisionNodeIds { get; internal set; }
        public string P13FeasibilityStatus { get; internal set; }
        public bool P14CandidateAvailable { get; internal set; }
        public string CandidateFingerprint { get; internal set; }
        public string PlanFingerprint { get; internal set; }
        public string ApprovalVerificationStatus { get; internal set; }
        public bool TrustedClientVerifierConfigured { get; internal set; }
        public bool TrustedRuntimeVerifierConfigured { get; internal set; }
        public bool TestOnly { get; internal set; }
        public bool ProductionExecutionEligible { get; internal set; }
        public List<GravityReadinessBlocker> BlockingReasons { get; internal set; } = new List<GravityReadinessBlocker>();
    }
    internal static class GravityEngineeringReadinessAssessment
    {
        internal static GravityEngineeringReadiness Assess(GravityEngineeringReadinessInput supplied,
            IGravityEngineeringApprovalVerifier verifier)
        {
            // Read-only domain inspection. No paths, process launch, working copies, Bentley initialization or solver.
            var source = supplied?.EngineeringInputs;
            var input = source == null ? new GravityEngineeringInputs() :
                RuntimeHostProtocol.Serializer().Deserialize<GravityEngineeringInputs>(GravityExecutionPlanBuilder.Json(source));
            var result = new GravityEngineeringReadiness { ProjectId = input.ProjectId, ModelFingerprint = input.ModelFingerprint,
                ModelName = supplied?.ModelName, SourceEvidenceReference = supplied?.SourceEvidenceReference,
                SourceKind = supplied?.SourceKind ?? GravityReadinessSourceKind.Unavailable,
                DesignBasisReviewStatus = input.DesignBasis?.CommonDesignBasis?.ReviewState.ToString() ?? "Missing",
                P13FeasibilityStatus = "Missing", P14CandidateAvailable = !string.IsNullOrWhiteSpace(input.CandidateJson),
                ApprovalVerificationStatus = verifier == null ? "TRUSTED_VERIFIER_UNAVAILABLE" : "NOT_EVALUATED_INCOMPLETE_INPUT",
                TrustedClientVerifierConfigured = verifier != null,
                TrustedRuntimeVerifierConfigured = GravityEngineeringApprovalComposition.RuntimeVerifier != null };
            var conduits = input.SourceRead?.Conduits?.Select(c => c.Id).Distinct().OrderBy(id => id).ToArray() ?? new int[0];
            var nodes = input.SourceRead?.Structures?.Select(n => n.Id).Distinct().OrderBy(id => id).ToArray() ?? new int[0];
            var outfalls = input.SourceRead?.Structures?.Where(n => n.RuntimeCategory == "Outfall").Select(n => n.Id)
                .Distinct().OrderBy(id => id).ToArray() ?? new int[0];
            Action<string, string, int[], int[]> block = (code, detail, c, n) => result.BlockingReasons.Add(
                new GravityReadinessBlocker { Code = code, Detail = detail, ConduitIds = c ?? conduits, NodeIds = n ?? nodes });
            if (string.IsNullOrWhiteSpace(input.ProjectId) || string.IsNullOrWhiteSpace(input.ModelFingerprint))
                block("PROJECT_MODEL_CONTEXT_MISSING", "Explicit project/model identity is required.", null, null);
            GravityNetworkSnapshot network = null;
            if (input.SourceRead == null) block("P12_NETWORK_MISSING", "Actual P12 source snapshot is unavailable.", null, null);
            else try { network = new GravityNetworkIngestionService().Ingest(input.SourceRead, input.ModelFingerprint, schemaVersion: 2);
                result.ReconstructedNetworkFingerprint = network.NetworkFingerprint; }
            catch (Exception error) { block("P12_NETWORK_INVALID", error.Message, null, null); }
            result.NetworkFingerprint = result.SourceKind == GravityReadinessSourceKind.Historical
                ? supplied.HistoricalNetworkFingerprint : result.ReconstructedNetworkFingerprint;
            if (result.SourceKind != GravityReadinessSourceKind.SuppliedP12Snapshot)
                block("SOURCE_AUTHORITY_UNAVAILABLE", "Historical, missing or TEST_ONLY data cannot authorize live execution.", null, null);
            if (input.DesignBasis == null)
            { result.MissingEngineeringCriteria.Add("Project-specific reviewed GravityFlow Design Basis");
                block("DESIGN_BASIS_MISSING", "No reviewed project Design Basis supplied.", null, null); }
            else
            {
                var validation = input.DesignBasis.Validate();
                foreach (var issue in validation.Issues.Where(i => i.Severity == DesignBasisValidationSeverity.Error))
                    result.MissingEngineeringCriteria.Add(issue.Field + ": " + issue.Message);
                foreach (var criterion in input.DesignBasis.EnumerateSectionsInCanonicalOrder().Where(s => s != null)
                    .SelectMany(s => s.Criteria ?? new List<GravityDesignCriterion>()).Where(c => c != null)
                    .Where(c => c.Requirement == GravityCriterionRequirement.Required &&
                        (c.Status != GravityCriterionStatus.Defined || c.Value == null || c.Provenance == null)))
                    result.MissingEngineeringCriteria.Add(criterion.Key);
                if (!validation.IsValid || result.MissingEngineeringCriteria.Count > 0)
                    block("ENGINEERING_CRITERIA_UNRESOLVED", "Required criteria/values/provenance are unresolved.", null, null);
                if (input.DesignBasis.CommonDesignBasis?.ReviewState != DesignBasisReviewState.Reviewed)
                    block("DESIGN_BASIS_UNREVIEWED", "The project Design Basis has not been reviewed.", null, null);
            }
            GravityVerticalDesignConstraintSet constraints = null;
            if (string.IsNullOrWhiteSpace(input.ConstraintsJson)) block("P13_CONSTRAINTS_MISSING", "No engineering constraint set supplied.", null, null);
            else try { constraints = GravityEngineeringWire.Constraints(input.ConstraintsJson); }
                catch (Exception error) { block("P13_CONSTRAINTS_INVALID", error.GetBaseException().Message, null, null); }
            result.MissingFlowOrientationConduitIds = conduits.Where(id => constraints == null ||
                !constraints.Orientations.ContainsKey(id.ToString(CultureInfo.InvariantCulture))).ToArray();
            result.MissingOutfallDecisionNodeIds = outfalls.Where(id => constraints == null || !constraints.OutfallConstraints.Any(c =>
                c.OutfallNodeId == id && (c.ProtectedInvert != null || c.ConnectedEndpointConstraint != null))).ToArray();
            if (result.MissingFlowOrientationConduitIds.Length > 0)
                block("FLOW_ORIENTATION_MISSING", "Explicit engineering orientation must not be inferred from Start/Stop.", result.MissingFlowOrientationConduitIds, new int[0]);
            if (result.MissingOutfallDecisionNodeIds.Length > 0)
                block("OUTFALL_DECISION_MISSING", "Explicit outfall boundary decisions are required.", new int[0], result.MissingOutfallDecisionNodeIds);
            if (network != null && constraints != null)
                try
                {
                    var feasibility = new GravityVerticalFeasibilityEngine().Analyze(network, constraints);
                    result.P13FeasibilityStatus = feasibility.Status.ToString();
                    if (string.IsNullOrWhiteSpace(input.FeasibilityJson) ||
                        GravityExecutionPlanBuilder.Json(feasibility) != GravityExecutionPlanBuilder.Json(RuntimeHostProtocol.Serializer().DeserializeObject(input.FeasibilityJson)) ||
                        (feasibility.Status != VerticalFeasibilityStatus.Feasible && feasibility.Status != VerticalFeasibilityStatus.FeasibleMultipleSolutions) ||
                        feasibility.DecisionRequests.Count != 0 || feasibility.Conflicts.Count != 0 || feasibility.IncompleteItems.Count != 0)
                        block("P13_FEASIBILITY_INVALID", "Supplied feasibility must match recomputation and have no blocking decisions/conflicts/incomplete items.", null, null);
                }
                catch (Exception error) { result.P13FeasibilityStatus = "Invalid"; block("P13_FEASIBILITY_INVALID", error.GetBaseException().Message, null, null); }
            else block("P13_FEASIBILITY_UNAVAILABLE", "Network and constraints are required to recompute feasibility.", null, null);
            if (!result.P14CandidateAvailable) block("P14_CANDIDATE_MISSING", "No genuine P14-generated candidate supplied.", null, null);
            bool singleOperator = supplied?.OperatingMode == GravityOperatingMode.SingleOperator;
            if (!singleOperator && input.Approval == null) block("ENGINEERING_APPROVAL_MISSING", "No explicit engineering execution decision supplied.", null, null);
            if (!singleOperator && verifier == null) block("TRUSTED_ENGINEERING_VERIFIER_UNAVAILABLE", "No trusted on-prem approval verifier configured.", null, null);
            if (!singleOperator && !result.TrustedRuntimeVerifierConfigured)
                block("RUNTIME_TRUSTED_VERIFIER_UNAVAILABLE", "Trusted RuntimeHost composition has no local provider; JSON/P5 cannot install one.", null, null);
            if (input.DesignBasis != null && network != null && constraints != null && result.P14CandidateAvailable && (singleOperator || input.Approval != null))
                try
                {
                    var plan = singleOperator ? GravityExecutionPlanBuilder.BuildSingleOperator(input, supplied.Calculate) : GravityExecutionPlanBuilder.Build(input);
                    result.CandidateFingerprint = plan.CandidateFingerprint; result.PlanFingerprint = plan.PlanFingerprint;
                    if (singleOperator)
                    {
                        result.ApprovalVerificationStatus = "OPERATOR_CONFIRMATION_REQUIRED";
                        block("OPERATOR_CONFIRMATION_REQUIRED", "Display this exact validated plan and obtain one-use Windows operator confirmation.", null, null);
                    }
                    else if (verifier != null)
                    {
                        var authorized = GravityEngineeringAuthorization.Verify(plan, verifier, false);
                        result.TestOnly = authorized.TestOnly || result.SourceKind == GravityReadinessSourceKind.TestOnly;
                        result.ApprovalVerificationStatus = result.TestOnly ? "TEST_ONLY_NON_ENGINEERING" : "VERIFIED";
                        if (result.TestOnly) block("TEST_ONLY_NON_ENGINEERING", "Deterministic fixture verification grants no live execution authority.", null, null);
                    }
                }
                catch (Exception error)
                { if (verifier != null) result.ApprovalVerificationStatus = "NOT_VERIFIED";
                    block("ENGINEERING_PLAN_NOT_VERIFIED", error.GetBaseException().Message, null, null); }
            result.TestOnly = result.TestOnly || result.SourceKind == GravityReadinessSourceKind.TestOnly;
            result.ProductionExecutionEligible = result.BlockingReasons.Count == 0 && !result.TestOnly &&
                result.ApprovalVerificationStatus == "VERIFIED";
            return result;
        }
    }
}
