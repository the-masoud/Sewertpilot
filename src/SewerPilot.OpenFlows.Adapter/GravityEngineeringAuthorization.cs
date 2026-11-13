using System;

namespace SewerPilot.OpenFlows.Adapter
{
    // Implemented only by trusted local application composition, never deserialized or selected from a request.
    // The provider must independently authenticate the actor, check project permission and authoritative approval,
    // revocation/expiry, and compare every binding below against its local approval record.
    public interface IGravityEngineeringApprovalVerifier
    {
        GravityEngineeringApprovalVerification Verify(GravityEngineeringApprovalBinding binding);
    }

    public sealed class GravityEngineeringApprovalBinding
    {
        internal GravityEngineeringApprovalBinding(GravityExecutionPlan plan)
        {
            var a = plan.Inputs.Approval;
            ProjectId = plan.Inputs.ProjectId; ModelFingerprint = plan.Inputs.ModelFingerprint;
            DesignBasisId = a.DesignBasisId; DesignBasisVersion = a.DesignBasisVersion;
            DesignBasisFingerprint = plan.DesignBasisFingerprint; NetworkFingerprint = plan.NetworkFingerprint;
            ConstraintFingerprint = plan.ConstraintFingerprint; CandidateId = plan.CandidateId;
            CandidateFingerprint = plan.CandidateFingerprint; RecipeId = plan.RecipeId;
            PlanFingerprint = plan.PlanFingerprint; SourceScenarioId = plan.SourceScenarioId;
            SourcePhysicalAlternativeId = plan.SourcePhysicalAlternativeId; Calculate = plan.Calculate;
            ActorId = a.ActorId; ApprovalId = a.ApprovalId; DecisionReference = a.DecisionReference;
            ExecuteEngineeringDesign = a.ExecuteEngineeringDesign; ApprovedUtc = a.ApprovedUtc;
        }
        public string ProjectId { get; }
        public string ModelFingerprint { get; }
        public string DesignBasisId { get; }
        public string DesignBasisVersion { get; }
        public string DesignBasisFingerprint { get; }
        public string NetworkFingerprint { get; }
        public string ConstraintFingerprint { get; }
        public string CandidateId { get; }
        public string CandidateFingerprint { get; }
        public string RecipeId { get; }
        public string PlanFingerprint { get; }
        public int SourceScenarioId { get; }
        public int SourcePhysicalAlternativeId { get; }
        public bool Calculate { get; }
        public string ActorId { get; }
        public string ApprovalId { get; }
        public string DecisionReference { get; }
        public bool ExecuteEngineeringDesign { get; }
        public string ApprovedUtc { get; }
    }

    // This result is consumed exclusively from a trusted verifier call. It is not part of HostRequest or the plan.
    public sealed class GravityEngineeringApprovalVerification
    {
        public GravityEngineeringApprovalVerification(bool approved, string approvedPlanFingerprint, string decisionReference,
            DateTimeOffset validUntilUtc, string reason, bool testOnly = false)
        { Approved = approved; ApprovedPlanFingerprint = approvedPlanFingerprint; DecisionReference = decisionReference;
            ValidUntilUtc = validUntilUtc; Reason = reason; TestOnly = testOnly; }
        public bool Approved { get; }
        public string ApprovedPlanFingerprint { get; }
        public string DecisionReference { get; }
        public DateTimeOffset ValidUntilUtc { get; }
        public string Reason { get; }
        public bool TestOnly { get; }
    }

    internal sealed class AuthorizedGravityExecutionPlan
    {
        private readonly string frozenJson;
        private readonly DateTimeOffset validUntilUtc;
        internal AuthorizedGravityExecutionPlan(string json, GravityEngineeringApprovalVerification verification)
        { frozenJson = json; validUntilUtc = verification.ValidUntilUtc; TestOnly = verification.TestOnly; }
        internal bool TestOnly { get; }
        internal GravityExecutionPlan Materialize(bool requireLive)
        {
            GravityExecutionPlanBuilder.Require(validUntilUtc > DateTimeOffset.UtcNow, "ENGINEERING_APPROVAL_EXPIRED");
            GravityExecutionPlanBuilder.Require(!requireLive || !TestOnly, "TEST_ONLY_NON_ENGINEERING");
            var plan = RuntimeHostProtocol.Serializer().Deserialize<GravityExecutionPlan>(frozenJson);
            GravityExecutionPlanBuilder.Validate(plan);
            return plan;
        }
    }

    internal static class GravityEngineeringAuthorization
    {
        internal static AuthorizedGravityExecutionPlan Verify(GravityExecutionPlan supplied,
            IGravityEngineeringApprovalVerifier verifier, bool requireLive)
        {
            GravityExecutionPlanBuilder.Require(verifier != null, "TRUSTED_ENGINEERING_VERIFIER_UNAVAILABLE");
            // Clone before the verifier sees a binding; neither the caller nor verifier can alter the submitted plan.
            string frozen = GravityExecutionPlanBuilder.Json(supplied);
            var plan = RuntimeHostProtocol.Serializer().Deserialize<GravityExecutionPlan>(frozen);
            GravityExecutionPlanBuilder.Validate(plan);
            var binding = new GravityEngineeringApprovalBinding(plan);
            GravityEngineeringApprovalVerification decision;
            try { decision = verifier.Verify(binding); }
            catch (Exception error) { throw new InvalidOperationException("P15_BLOCKED_ENGINEERING_VERIFIER_FAILURE", error); }
            GravityExecutionPlanBuilder.Require(decision != null && decision.Approved,
                "ENGINEERING_APPROVAL_REJECTED: " + (decision?.Reason ?? "No trusted decision"));
            GravityExecutionPlanBuilder.Require(decision.ApprovedPlanFingerprint == binding.PlanFingerprint &&
                decision.DecisionReference == binding.DecisionReference, "ENGINEERING_APPROVAL_BINDING");
            var authorized = new AuthorizedGravityExecutionPlan(frozen, decision);
            authorized.Materialize(requireLive);
            return authorized;
        }
        internal static void RequireRuntimeApproval(GravityExecutionPlan plan) =>
            Verify(plan, GravityEngineeringApprovalComposition.RuntimeVerifier, true);
    }

    internal static class GravityEngineeringApprovalComposition
    {
        // Deliberately unconfigured. A real on-prem provider must be wired here in trusted RuntimeHost composition.
        // No request field, environment variable, plugin/type name or technical token can install a provider.
        internal static IGravityEngineeringApprovalVerifier RuntimeVerifier => null;
    }

    public sealed class GravityEngineeringExecutionService
    {
        private readonly IGravityEngineeringApprovalVerifier verifier;
        public GravityEngineeringExecutionService(IGravityEngineeringApprovalVerifier verifier = null)
        { this.verifier = verifier; }
        public GravityEngineeringReadiness AssessReadiness(GravityEngineeringReadinessInput input) =>
            GravityEngineeringReadinessAssessment.Assess(input, verifier);
        public GravityEngineeringIntakeResult ReviewEngineeringIntake(GravityEngineeringIntakeRequest input) =>
            GravityEngineeringIntake.Review(input, verifier);
        public GravityProductionExecutionOutcome ExecuteEngineeringPlan(GravityExecutionPlan plan, ProtectedBaseline baseline,
            SewerPilot.OpenFlows.Compatibility.OpenFlowsRuntimeCandidate runtime, int runtimeCandidateCount, string watchdogPythonPath)
        {
            var authorized = GravityEngineeringAuthorization.Verify(plan, verifier, true);
            var frozen = authorized.Materialize(true);
            GravityEngineeringAuthorization.RequireRuntimeApproval(frozen); // Before baseline access or working-copy creation.
            GravityExecutionPlanBuilder.Require(baseline != null && baseline.VerifyCurrentIntegrity().IsMatch &&
                baseline.Fingerprint.Value == frozen.Inputs.ModelFingerprint, "BASELINE_SOURCE_IDENTITY");
            if (frozen.NoChanges) return new GravityProductionExecutionOutcome {
                Status = "NO_CHANGES_NOT_EXECUTED", NoChanges = true, BaselineIntegrityVerified = true, RuntimeInvocations = 0 };
            return new RuntimeHostClient().ExecuteProduction(authorized, baseline, runtime, runtimeCandidateCount, watchdogPythonPath);
        }
    }
}
