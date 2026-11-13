using System;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static GravityEngineeringInputs SingleInputs(bool changed = true)
        { var input = ProductionFixture(changed).Input; input.Approval = null; return input; }
        private static SingleOperatorPreview TestPreview(GravityExecutionPlan plan) => new SingleOperatorPreview(plan, Path.Combine(Path.GetTempPath(), "TEST_ONLY_NON_ENGINEERING.stsw"), true);
        private static void P15Mvp_PlanAndEnterpriseSeparation()
        {
            var plan = GravityExecutionPlanBuilder.BuildSingleOperator(SingleInputs(), true);
            GravityExecutionPlanBuilder.ValidateSingleOperator(plan);
            Check(plan.SchemaVersion == 2 && plan.Inputs.Approval == null && plan.Calculate, "SingleOperator engineering plan requires no Enterprise identity, approval DTO or verifier.");
            var enterprise = GravityExecutionPlanBuilder.Build(ProductionFixture().Input);
            BlockedBecause(() => GravityEngineeringAuthorization.RequireRuntimeApproval(enterprise), "TRUSTED_ENGINEERING_VERIFIER_UNAVAILABLE");
            BlockedBecause(() => new GravityEngineeringExecutionService().ExecuteEngineeringPlan(enterprise, null, null, 0, null), "TRUSTED_ENGINEERING_VERIFIER_UNAVAILABLE");
            BlockedBecause(() => new SingleOperatorExecutionService(new SewerPilotOperatingConfiguration(GravityOperatingMode.Enterprise)).ReviewInputs(new GravityEngineeringIntakeRequest()), "SINGLE_OPERATOR_MODE_DISABLED");
            BlockedBecause(() => GravityExecutionPlanBuilder.Validate(plan), "PLAN_SCHEMA");
            BlockedBecause(() => GravityExecutionPlanBuilder.Build(SingleInputs()), "EXTERNAL_ENGINEERING_INPUT");
            Check(GravityEngineeringApprovalComposition.RuntimeVerifier == null, "Enterprise fail-closed composition remains unconfigured; no fallback installed.");
        }
        private static void P15Mvp_ConfirmationBindingAndOneUse()
        {
            var plan = GravityExecutionPlanBuilder.BuildSingleOperator(SingleInputs(), false); var preview = TestPreview(plan);
            var confirmation = new SingleOperatorConfirmation(preview);
            Check(confirmation.OperatorSid == System.Security.Principal.WindowsIdentity.GetCurrent().User.Value, "Real Windows operator identity is audit only; no fabricated approver.");
            var permit = confirmation.Consume(plan, preview.ModelPath); permit.RequirePlan(plan);
            BlockedBecause(() => confirmation.Consume(plan, preview.ModelPath), "OPERATOR_CONFIRMATION_ALREADY_USED");
            var changed = GravityExecutionPlanBuilder.BuildSingleOperator(SingleInputs(changed: false), false);
            BlockedBecause(() => new SingleOperatorConfirmation(preview).Consume(changed, preview.ModelPath), "OPERATOR_CONFIRMATION_PLAN_MISMATCH");
            var intent = GravityExecutionPlanBuilder.BuildSingleOperator(SingleInputs(), true);
            BlockedBecause(() => new SingleOperatorConfirmation(preview).Consume(intent, preview.ModelPath), "OPERATOR_CONFIRMATION_PLAN_MISMATCH");
            BlockedBecause(() => new SingleOperatorConfirmation(preview).Consume(plan, preview.ModelPath + ".other"), "OPERATOR_CONFIRMATION_PLAN_MISMATCH");
            var mutable = permit.Materialize(); mutable.Endpoints[0].Proposed += 1;
            BlockedBecause(() => GravityExecutionPlanBuilder.ValidateSingleOperator(mutable), "PLAN_RECONSTRUCTION");
            Check(permit.Materialize().PlanFingerprint == preview.PlanFingerprint, "Frozen confirmed snapshot cannot be altered by a materialized DTO.");
            Check(typeof(SingleOperatorConfirmation).GetConstructors().Length == 0, "External JSON cannot manufacture an operator-confirmation object via public construction.");
        }
        private static void P15Mvp_UntrustedRequestAndModeMismatch()
        {
            var plan = GravityExecutionPlanBuilder.BuildSingleOperator(SingleInputs(), false);
            var request = new HostRequest { Operation = GravityWriteAndSolverHost.ProductionOperation, EngineeringPlan = plan,
                Diagnostics = new System.Collections.Generic.Dictionary<string, string> { { "OperatorConfirmed", "true" }, { "OperatingMode", "SingleOperator" } } };
            BlockedBecause(() => RuntimeHostProtocol.ValidateRequest(request), "OPERATOR_CONFIRMATION_REQUIRED");
            BlockedBecause(() => GravityWriteAndSolverHost.ValidateProductionRequest(request), "OPERATOR_CONFIRMATION_REQUIRED");
            BlockedBecause(() => GravityWriteAndSolverHost.ExecuteProduction(null, null, null, request, new ModelReadEvidence()), "OPERATOR_CONFIRMATION_REQUIRED");
            BlockedBecause(() => GravityExecutionAuthorization.InitializeRuntime(new SewerPilotOperatingConfiguration(GravityOperatingMode.Enterprise), request, Path.GetTempPath()), "OPERATING_MODE_PLAN_MISMATCH");
            request.EngineeringPlan = GravityExecutionPlanBuilder.Build(ProductionFixture().Input);
            BlockedBecause(() => GravityExecutionAuthorization.InitializeRuntime(new SewerPilotOperatingConfiguration(GravityOperatingMode.SingleOperator), request, Path.GetTempPath()), "OPERATING_MODE_PLAN_MISMATCH");
            BlockedBecause(() => RuntimeHostProtocol.ValidateRequest(request), "TRUSTED_ENGINEERING_VERIFIER_UNAVAILABLE");
            Check(request.Diagnostics["OperatingMode"] == "SingleOperator", "A caller mode flag cannot override failed Enterprise checks.");
        }
        private static void P15Mvp_InputReadinessAndValidation()
        {
            var service = new SingleOperatorExecutionService(new SewerPilotOperatingConfiguration(GravityOperatingMode.SingleOperator));
            var request = new GravityEngineeringIntakeRequest { Context = new GravityEngineeringReadinessInput { EngineeringInputs = SingleInputs(),
                SourceKind = GravityReadinessSourceKind.SuppliedP12Snapshot, Calculate = false } };
            var result = service.ReviewInputs(request);
            Check(result.EngineeringContentValidated && result.Status == "DESIGN_READY_OPERATOR_CONFIRMATION_REQUIRED" &&
                result.Readiness.BlockingReasons.All(b => b.Code == "OPERATOR_CONFIRMATION_REQUIRED"), "Engineering-ready MVP removes Enterprise blockers while still requiring explicit confirmation.");
            Check(!result.Readiness.ProductionExecutionEligible && result.PreparedInputs.Approval == null, "Readiness alone cannot authorize execution or create approval identifiers.");
            BlockedBecause(() => service.PreparePreview(request, Path.Combine(Path.GetTempPath(), "TEST_ONLY_NON_ENGINEERING.stsw")), "TEST_ONLY_NON_ENGINEERING");
            request.Context.EngineeringInputs.DesignBasis = null;
            var incomplete = service.ReviewInputs(request);
            Check(!incomplete.EngineeringContentValidated && incomplete.Readiness.BlockingReasons.Any(b => b.Code == "DESIGN_BASIS_MISSING"), "Missing basis blocks candidate preview/mutation while read-only readiness remains usable.");
            BlockedBecause(() => GravityExecutionPlanBuilder.BuildSingleOperator(request.Context.EngineeringInputs, false), "EXTERNAL_ENGINEERING_INPUT");
            var invalid = SingleInputs(); invalid.SourceRead.Conduits[0].StartInvert = double.NaN;
            Throws<GravityNetworkIngestionException>(() => GravityExecutionPlanBuilder.BuildSingleOperator(invalid, false));
            var explicitApproval = ProductionFixture().Input;
            BlockedBecause(() => GravityExecutionPlanBuilder.BuildSingleOperator(explicitApproval, false), "EXTERNAL_ENGINEERING_INPUT");
            var missing = new GravityEngineeringReadinessInput { EngineeringInputs = new GravityEngineeringInputs { SourceRead = ProductionFixture().Input.SourceRead }, OperatingMode = GravityOperatingMode.SingleOperator };
            var readiness = GravityEngineeringReadinessAssessment.Assess(missing, null);
            Check(readiness.MissingFlowOrientationConduitIds.Length > 0 && readiness.MissingOutfallDecisionNodeIds.Length > 0 &&
                !readiness.BlockingReasons.Any(b => b.Code.Contains("VERIFIER")), "Missing decisions are reported precisely without Enterprise dependencies.");
        }
        private static void P15Mvp_NoChangeAndProtectedSource()
        {
            var plan = GravityExecutionPlanBuilder.BuildSingleOperator(SingleInputs(false), true); var preview = TestPreview(plan);
            var permit = new SingleOperatorConfirmation(preview).Consume(plan, preview.ModelPath); int launches = 0;
            var outcome = SingleOperatorExecutionService.ExecuteConfirmed(permit, () => true, () => { launches++; throw new Exception("Must not execute"); });
            Check(outcome.NoChanges && launches == 0 && outcome.RuntimeInvocations == 0 && outcome.FieldWrites == 0 && outcome.Saves == 0 && outcome.SolverRuns == 0,
                "No-change confirmation produces zero writes/saves/solver/working-copy runtime invocation.");
            BlockedBecause(() => SingleOperatorExecutionService.ExecuteConfirmed(permit, () => false, () => outcome), "BASELINE_SOURCE_IDENTITY");
            var changed = GravityExecutionPlanBuilder.BuildSingleOperator(SingleInputs(), true); var changedPreview = TestPreview(changed);
            var synthetic = new SingleOperatorConfirmation(changedPreview).Consume(changed, changedPreview.ModelPath);
            BlockedBecause(() => SingleOperatorExecutionService.ExecuteConfirmed(synthetic, () => true, () => { launches++; return outcome; }), "TEST_ONLY_NON_ENGINEERING");
            var service = new SingleOperatorExecutionService(new SewerPilotOperatingConfiguration(GravityOperatingMode.SingleOperator));
            BlockedBecause(() => service.Execute(null, plan, null, null, 0, null), "OPERATOR_CONFIRMATION_REQUIRED");
            Check(launches == 0 && WriteAuthorizationBoundary.RequestAuthorization(null, null, "anything").IsAuthorized == false, "P5 protected-source authorization remains independent and mandatory.");
        }
        private static void P15Mvp_SharedBudgetsAndPreview()
        {
            var enterprise = GravityExecutionPlanBuilder.Build(ProductionFixture().Input);
            var single = GravityExecutionPlanBuilder.BuildSingleOperator(SingleInputs(), true); var preview = TestPreview(single);
            Check(single.EndpointWriteCount == enterprise.EndpointWriteCount && single.LocalizationWriteCount == enterprise.LocalizationWriteCount &&
                single.TotalFieldWriteCount == enterprise.TotalFieldWriteCount && GravityExecutionPlanBuilder.Json(single.Endpoints) == GravityExecutionPlanBuilder.Json(enterprise.Endpoints),
                "MVP shares exact validated endpoints/localization/write budgets with accepted execution.");
            Check(preview.Text == TestPreview(single).Text && preview.Text.Contains(single.PlanFingerprint) && preview.Text.Contains(single.CandidateFingerprint) &&
                preview.Text.Contains(single.Inputs.ProjectId) && preview.Text.Contains(single.SourceScenarioId.ToString()) &&
                preview.Text.Contains("ft") && preview.Text.Contains("Solver requested: True") && preview.Text.Contains("Conduit"), "Deterministic displayed preview includes model/project/scenario/candidate/original/proposed/units/solver intent.");
            Check(single.Endpoints.Count == enterprise.Endpoints.Count && GravityEngineeringApprovalComposition.RuntimeVerifier == null,
                "Existing source-state/isolation/persistence/watchdog tests remain active; no Enterprise provider installed.");
        }
    }
}
