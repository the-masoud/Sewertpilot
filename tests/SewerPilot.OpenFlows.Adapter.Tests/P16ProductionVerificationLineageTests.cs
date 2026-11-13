using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static void AssertLineageRejection(Action action, string expectedMessageSubstring, string label = null)
        {
            assertions++;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (ex.Message.IndexOf(expectedMessageSubstring, StringComparison.Ordinal) >= 0)
                    return;
                string prefix = label != null ? ("[" + label + "] ") : "";
                throw new InvalidOperationException(prefix + "Expected message containing '" + expectedMessageSubstring + "' but got: " + ex.Message);
            }
            string missingPrefix = label != null ? ("[" + label + "] ") : "";
            throw new InvalidOperationException(missingPrefix + "Expected exception containing '" + expectedMessageSubstring + "' was not thrown.");
        }

        public static void P16ProductionVerification_LineageBridgeContracts()
        {
            Console.WriteLine("=== P16-C5 Production Result Lineage Bridge to P17 Verification Tests ===");

            LineageBridge_ValidProductionReceiptAcceptedAgainstExactPlanAndLineage();
            LineageBridge_GenuineResultsWithMissingCriteriaDoNotClaimCompliance();
            LineageBridge_GenuineResultsWithPassingCriteriaGeneratesReport();
            LineageBridge_RejectsMismatchedSourcePlanIdentities();
            LineageBridge_RejectsMismatchedExecutionAndOutputIdentities();
            LineageBridge_RejectsTamperedResultContentAndMetrics();
            LineageBridge_TestOnlyAndCallerFlagsCannotObtainProductionAuthority();
            LineageBridge_GenericVerificationRemainsFailClosed();

            Console.WriteLine("=== All P16-C5 Lineage Bridge Contracts Verified Successfully ===");
        }

        private static void LineageBridge_ValidProductionReceiptAcceptedAgainstExactPlanAndLineage()
        {
            var req = CreateProductionReadbackTestRequest(calculate: true);
            var plan = req.EngineeringPlan;
            var genuineSet = CreateSampleGenuineResultSet(req, 101, 102);
            var hostResult = CreateProductionReadbackResult(req, genuineSet);
            var inv = CreateSampleProductionInventory(req, hostResult);
            var authority = P16ResultContentAuthority.FromProductionResult(req, hostResult, inv);

            Check(authority != null, "Authority created from genuine production result.");
            Check(authority.IsProductionReceipt, "Authority is a production receipt.");
            Check(authority.Lineage != null, "Authority exposes lineage bridge.");

            var lineage = authority.Lineage;
            Check(lineage.SourceProjectId == plan.Inputs.ProjectId, "SourceProjectId matches plan.");
            Check(lineage.SourceModelFingerprint == plan.Inputs.ModelFingerprint, "SourceModelFingerprint matches plan.");
            Check(lineage.SourceNetworkFingerprint == plan.NetworkFingerprint, "SourceNetworkFingerprint matches plan.");
            Check(lineage.SourceCandidateFingerprint == plan.CandidateFingerprint, "SourceCandidateFingerprint matches plan.");
            Check(lineage.ExecutionPlanFingerprint == plan.PlanFingerprint, "ExecutionPlanFingerprint matches plan.");
            Check(lineage.SourceScenarioId == plan.SourceScenarioId, "SourceScenarioId matches plan.");
            Check(lineage.SourcePhysicalAlternativeId == plan.SourcePhysicalAlternativeId, "SourcePhysicalAlternativeId matches plan.");
            Check(lineage.WorkingCopyId == req.WorkingCopyId, "WorkingCopyId matches request.");
            Check(lineage.CreatedScenarioId == 101, "CreatedScenarioId matches isolated scenario.");
            Check(lineage.CreatedPhysicalAlternativeId == 102, "CreatedPhysicalAlternativeId matches isolated alternative.");
            Check(lineage.OutputFingerprint == authority.OutputFingerprint, "OutputFingerprint matches authority.");
            Check(lineage.OutputInventoryCount == authority.OutputFiles.Count, "OutputInventoryCount matches authority.");
            Check(lineage.OutputModelFamilyFingerprint == authority.ModelFingerprint, "OutputModelFamilyFingerprint matches authority.");
            Check(lineage.ExecutionRunId == req.RunId, "ExecutionRunId matches request.");
            Check(lineage.ProofFingerprint == authority.ProofFingerprint, "ProofFingerprint matches authority.");
            Check(lineage.Engine == authority.Engine, "Engine matches authority.");
            Check(lineage.SolverStartedUtc <= lineage.SolverCompletedUtc && lineage.SolverCompletedUtc <= lineage.SavedUtc, "Lifecycle timestamps in order.");
            Check(!string.IsNullOrWhiteSpace(lineage.ObservedContentFingerprint), "ObservedContentFingerprint present.");

            var results = authority.CreateResultSet(req.WorkingCopyId, plan.NetworkFingerprint, plan.PlanFingerprint);
            var view = GravityHydraulicVerificationWorkflow.VerifyProductionResult(plan, authority, results, null);

            Check(view.DataKind == HydraulicResultDataKind.Genuine, "Valid receipt results in Genuine presentation.");
            Check(view.TrustedProvenanceEstablished, "Trusted provenance established.");
            Check(view.StatusToken == "RESULTS_GENUINE", "Status token is RESULTS_GENUINE.");
            Check(view.Lineage == lineage, "Lineage bridge attached to presentation.");
            Check(view.Report != null, "Verification report generated.");
            Check(view.PlanFingerprint == plan.PlanFingerprint, "Plan fingerprint matches.");
            Check(view.CandidateFingerprint == plan.CandidateFingerprint, "Candidate fingerprint matches.");
            Check(view.Describe().Contains("RESULTS_GENUINE"), "Describe() reflects RESULTS_GENUINE.");
            Check(view.Describe().Contains("sourceScenario=1"), "Describe() reflects source scenario.");
            Check(view.Describe().Contains("createdScenario=101"), "Describe() reflects created scenario.");
            Check(view.Describe().Contains("ResultRead=NotTested"), "Describe() reflects ResultRead boundary.");
        }

        private static void LineageBridge_GenuineResultsWithMissingCriteriaDoNotClaimCompliance()
        {
            var req = CreateProductionReadbackTestRequest(calculate: true);
            var plan = req.EngineeringPlan;
            var genuineSet = CreateSampleGenuineResultSet(req, 101, 102);
            var hostResult = CreateProductionReadbackResult(req, genuineSet);
            var inv = CreateSampleProductionInventory(req, hostResult);
            var authority = P16ResultContentAuthority.FromProductionResult(req, hostResult, inv);
            var results = authority.CreateResultSet(req.WorkingCopyId, plan.NetworkFingerprint, plan.PlanFingerprint);

            var viewEmpty = GravityHydraulicVerificationWorkflow.VerifyProductionResult(plan, authority, results, new HydraulicVerificationCriterion[0]);
            Check(viewEmpty.DataKind == HydraulicResultDataKind.Genuine, "Empty criteria results in Genuine data kind.");
            Check(!viewEmpty.ComplianceClaimed, "Missing criteria does not claim compliance.");
            Check(viewEmpty.MissingInputs.Any(m => m.Contains("No explicit hydraulic verification criteria")), "Missing criteria noted in MissingInputs.");
            Check(viewEmpty.Summary.Contains("engineering compliance cannot be assessed"), "Summary notes compliance cannot be assessed.");
            Check(viewEmpty.Describe().Contains("Engineering compliance claimed: no"), "Describe notes no compliance claimed.");

            var viewNull = GravityHydraulicVerificationWorkflow.VerifyProductionResult(plan, authority, results, null);
            Check(!viewNull.ComplianceClaimed, "Null criteria does not claim compliance.");
        }

        private static void LineageBridge_GenuineResultsWithPassingCriteriaGeneratesReport()
        {
            var req = CreateProductionReadbackTestRequest(calculate: true);
            var plan = req.EngineeringPlan;
            var genuineSet = CreateSampleGenuineResultSet(req, 101, 102);
            var hostResult = CreateProductionReadbackResult(req, genuineSet);
            var inv = CreateSampleProductionInventory(req, hostResult);
            var authority = P16ResultContentAuthority.FromProductionResult(req, hostResult, inv);
            var results = authority.CreateResultSet(req.WorkingCopyId, plan.NetworkFingerprint, plan.PlanFingerprint);

            var criterion = new HydraulicVerificationCriterion
            {
                CriterionKey = "TEST_MAX_FLOW",
                Metric = HydraulicMetricKind.Flow,
                Operator = HydraulicCriterionOperator.Maximum,
                Threshold = 10.0,
                Unit = "ft3/s"
            };
            var view = GravityHydraulicVerificationWorkflow.VerifyProductionResult(plan, authority, results, new[] { criterion });
            Check(view.DataKind == HydraulicResultDataKind.Genuine, "Presentation is Genuine with explicit criteria.");
            Check(view.Report != null, "Report is generated.");
            Check(view.Report.PassCount > 0, "Passing report recorded.");
            Check(!view.Report.HasViolations, "No violations reported.");
            Check(!view.ComplianceClaimed, "Automated code never claims compliance without authorized engineering approval.");
            Check(view.Summary == "Genuine hydraulic results verified against explicit criteria.", "Summary reflects verified against criteria.");
        }

        private static void LineageBridge_RejectsMismatchedSourcePlanIdentities()
        {
            var req = CreateProductionReadbackTestRequest(calculate: true);
            var plan = req.EngineeringPlan;
            var genuineSet = CreateSampleGenuineResultSet(req, 101, 102);
            var hostResult = CreateProductionReadbackResult(req, genuineSet);
            var inv = CreateSampleProductionInventory(req, hostResult);
            var authority = P16ResultContentAuthority.FromProductionResult(req, hostResult, inv);
            var results = authority.CreateResultSet(req.WorkingCopyId, plan.NetworkFingerprint, plan.PlanFingerprint);

            Func<GravityExecutionPlan> copyPlan = () =>
                RuntimeHostProtocol.Serializer().Deserialize<GravityExecutionPlan>(RuntimeHostProtocol.Serializer().Serialize(plan));

            // A. ProjectId mismatch
            var p1 = copyPlan(); p1.Inputs.ProjectId = "OTHER_PROJ";
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(p1, authority, results, null),
                "HYDRAULIC_RESULT_LINEAGE_MISMATCH: Source project identity");

            // B. ModelFingerprint mismatch
            var p2 = copyPlan(); p2.Inputs.ModelFingerprint = "OTHER_MODEL_FP";
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(p2, authority, results, null),
                "HYDRAULIC_RESULT_LINEAGE_MISMATCH: Source model fingerprint");

            // C. NetworkFingerprint mismatch
            var p3 = copyPlan(); p3.NetworkFingerprint = "OTHER_NET_FP";
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(p3, authority, results, null),
                "HYDRAULIC_RESULT_LINEAGE_MISMATCH: Lineage network fingerprint");

            // D. CandidateFingerprint mismatch
            var p4 = copyPlan(); p4.CandidateFingerprint = "OTHER_CAND_FP";
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(p4, authority, results, null),
                "HYDRAULIC_RESULT_LINEAGE_MISMATCH: Candidate fingerprint");

            // E. PlanFingerprint mismatch
            var p5 = copyPlan(); p5.PlanFingerprint = "OTHER_PLAN_FP";
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(p5, authority, results, null),
                "HYDRAULIC_RESULT_LINEAGE_MISMATCH: Execution plan fingerprint");

            // F. SourceScenarioId mismatch
            var p6 = copyPlan(); p6.SourceScenarioId = 999;
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(p6, authority, results, null),
                "HYDRAULIC_RESULT_LINEAGE_MISMATCH: Source scenario identity");

            // G. SourcePhysicalAlternativeId mismatch
            var p7 = copyPlan(); p7.SourcePhysicalAlternativeId = 999;
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(p7, authority, results, null),
                "HYDRAULIC_RESULT_LINEAGE_MISMATCH: Source physical alternative identity");
        }

        private static void LineageBridge_RejectsMismatchedExecutionAndOutputIdentities()
        {
            var req = CreateProductionReadbackTestRequest(calculate: true);
            var plan = req.EngineeringPlan;
            var genuineSet = CreateSampleGenuineResultSet(req, 101, 102);
            var hostResult = CreateProductionReadbackResult(req, genuineSet);
            var inv = CreateSampleProductionInventory(req, hostResult);
            var authority = P16ResultContentAuthority.FromProductionResult(req, hostResult, inv);

            // A. Mismatched WorkingCopyId (ProjectId on result)
            var r1 = authority.CreateResultSet(req.WorkingCopyId, plan.NetworkFingerprint, plan.PlanFingerprint);
            r1.ProjectId = "OTHER_WC";
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(plan, authority, r1, null),
                "HYDRAULIC_RESULT_LINEAGE_MISMATCH: Result working copy project identity");

            // B. Mismatched CreatedScenarioId
            var r2 = authority.CreateResultSet(req.WorkingCopyId, plan.NetworkFingerprint, plan.PlanFingerprint);
            r2.ScenarioId = 999;
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(plan, authority, r2, null),
                "HYDRAULIC_RESULT_LINEAGE_MISMATCH: Actual result scenario ID");

            // C. Mismatched CreatedPhysicalAlternativeId
            var r3 = authority.CreateResultSet(req.WorkingCopyId, plan.NetworkFingerprint, plan.PlanFingerprint);
            r3.PhysicalAlternativeId = 999;
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(plan, authority, r3, null),
                "HYDRAULIC_RESULT_LINEAGE_MISMATCH: Actual result physical alternative ID");

            // D. Mismatched NetworkFingerprint on results
            var r4 = authority.CreateResultSet(req.WorkingCopyId, plan.NetworkFingerprint, plan.PlanFingerprint);
            r4.NetworkFingerprint = "OTHER_NET";
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(plan, authority, r4, null),
                "HYDRAULIC_RESULT_LINEAGE_MISMATCH: Result network fingerprint");

            // E. Mismatched CandidateFingerprint on results
            var r5 = authority.CreateResultSet(req.WorkingCopyId, plan.NetworkFingerprint, plan.PlanFingerprint);
            r5.CandidateFingerprint = "OTHER_CAND";
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(plan, authority, r5, null),
                "HYDRAULIC_RESULT_LINEAGE_MISMATCH: Result candidate/plan fingerprint");

            // F. Output inventory tampering during authority capture
            var badInv = new IntegritySnapshot(inv.Fingerprint, inv.CapturedUtc,
                inv.Entries.Values.Take(1).ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase));
            AssertLineageRejection(() => P16ResultContentAuthority.FromProductionResult(req, hostResult, badInv),
                "STALE_OUTPUT_INVENTORY");
        }

        private sealed class TamperCase
        {
            public string Label { get; }
            public Action<HydraulicResultSet> Action { get; }

            public TamperCase(string label, Action<HydraulicResultSet> action)
            {
                Label = label;
                Action = action;
            }
        }

        private static void LineageBridge_RejectsTamperedResultContentAndMetrics()
        {
            var req = CreateProductionReadbackTestRequest(calculate: true);
            var plan = req.EngineeringPlan;
            var genuineSet = CreateSampleGenuineResultSet(req, 101, 102);
            var hostResult = CreateProductionReadbackResult(req, genuineSet);
            var inv = CreateSampleProductionInventory(req, hostResult);
            var authority = P16ResultContentAuthority.FromProductionResult(req, hostResult, inv);

            var tamperCases = new[]
            {
                new TamperCase("Element identity", r => r.Elements[0].ElementId = 9999),
                new TamperCase("Numeric value", r =>
                {
                    HydraulicMetricResult flowMetric = null;
                    foreach (var elem in r.Elements)
                    {
                        if (elem.ElementKind == "Conduit" && elem.Metrics != null)
                        {
                            flowMetric = elem.Metrics.FirstOrDefault(m =>
                                m.Metric == HydraulicMetricKind.Flow &&
                                m.Availability == HydraulicResultAvailability.Available &&
                                m.NumericValue.HasValue);
                            if (flowMetric != null) break;
                        }
                    }
                    if (flowMetric == null)
                        throw new InvalidOperationException("No available Conduit Flow observation found to tamper.");
                    flowMetric.NumericValue += 1.0;
                }),
                new TamperCase("Unit", r => r.Elements[0].Metrics[0].Unit = "m3/s"),
                new TamperCase("Metric identity", r => r.Elements[0].Metrics[0].Metric = HydraulicMetricKind.Hgl),
                new TamperCase("Calculation context", r => r.CalculationContext = "Tampered Context"),
                new TamperCase("Metric collection", r => r.Elements[0].Metrics.Clear()),
                new TamperCase("Result time", r => r.ResultTimeSeconds += 10.0),
                new TamperCase("Provenance description", r => r.Provenance.SourceDescription = "Tampered Description")
            };

            foreach (var tc in tamperCases)
            {
                var r = authority.CreateResultSet(req.WorkingCopyId, plan.NetworkFingerprint, plan.PlanFingerprint);
                string beforeFp = P16ResultContentAuthority.ExactContentFingerprint(r);
                tc.Action(r);
                string afterFp = P16ResultContentAuthority.ExactContentFingerprint(r);
                Check(!string.Equals(beforeFp, afterFp, StringComparison.Ordinal),
                    "Tamper operation '" + tc.Label + "' modified exact content fingerprint.");
                AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(plan, authority, r, null),
                    "CONTENT_MISMATCH", tc.Label);
            }
        }

        private static void LineageBridge_TestOnlyAndCallerFlagsCannotObtainProductionAuthority()
        {
            var req = CreateProductionReadbackTestRequest(calculate: true);
            var plan = req.EngineeringPlan;
            var genuineSet = CreateSampleGenuineResultSet(req, 101, 102);
            var hostResult = CreateProductionReadbackResult(req, genuineSet);
            var inv = CreateSampleProductionInventory(req, hostResult);
            var authority = P16ResultContentAuthority.FromProductionResult(req, hostResult, inv);

            // A. Caller setting IsTestOnly = true
            var testOnlyResults = authority.CreateResultSet(req.WorkingCopyId, plan.NetworkFingerprint, plan.PlanFingerprint);
            testOnlyResults.IsTestOnly = true;
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(plan, authority, testOnlyResults, null),
                "GENUINE_CONTENT_REQUIRED");

            // B. Non-production authority (from headless proof) cannot be used with VerifyProductionResult
            var proofAuthority = RecordedAuthority();
            Check(!proofAuthority.IsProductionReceipt, "Proof authority is not a production receipt.");
            Check(proofAuthority.Lineage == null, "Proof authority has no lineage bridge.");
            var proofResults = proofAuthority.CreateResultSet("P17_PROJECT", plan.NetworkFingerprint, plan.PlanFingerprint);
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.VerifyProductionResult(plan, proofAuthority, proofResults, null),
                "PRODUCTION_AUTHORITY_INVALID");

            // C. Caller-supplied strings cannot mint runtime trust
            Throws<NotSupportedException>(() =>
                HydraulicResultTrustProvenance.AcceptedResultAccessRuntime("CALLER_FP", "CALLER_REF"));

            // D. Null arguments fail closed
            Throws<ArgumentNullException>(() =>
                GravityHydraulicVerificationWorkflow.VerifyProductionResult(null, authority, genuineSet, null));
            Throws<ArgumentNullException>(() =>
                GravityHydraulicVerificationWorkflow.VerifyProductionResult(plan, null, genuineSet, null));

            // E. Reflection checks: no public constructors and no setter methods on P16ResultContentAuthority or ProductionResultLineageBridge
            Check(typeof(P16ResultContentAuthority).GetConstructors().Length == 0,
                "P16ResultContentAuthority has 0 public constructors.");
            Check(typeof(P16ResultContentAuthority).GetProperties().All(p => p.GetSetMethod(true) == null),
                "P16ResultContentAuthority has no property setters.");
            Check(typeof(ProductionResultLineageBridge).GetConstructors().Length == 0,
                "ProductionResultLineageBridge has 0 public constructors.");
            Check(typeof(ProductionResultLineageBridge).GetProperties().All(p => p.GetSetMethod(true) == null),
                "ProductionResultLineageBridge has no property setters.");
        }

        private static void LineageBridge_GenericVerificationRemainsFailClosed()
        {
            var req = CreateProductionReadbackTestRequest(calculate: true);
            var plan = req.EngineeringPlan;
            var genuineSet = CreateSampleGenuineResultSet(req, 101, 102);
            var hostResult = CreateProductionReadbackResult(req, genuineSet);
            var inv = CreateSampleProductionInventory(req, hostResult);
            var authority = P16ResultContentAuthority.FromProductionResult(req, hostResult, inv);
            var results = authority.CreateResultSet(req.WorkingCopyId, plan.NetworkFingerprint, plan.PlanFingerprint);

            // Calling generic Verify directly with production results fails closed because generic Verify requires results.ScenarioId == plan.SourceScenarioId (101 != 1)
            AssertLineageRejection(() => GravityHydraulicVerificationWorkflow.Verify(plan, results, null),
                "HYDRAULIC_RESULT_LINEAGE_MISMATCH: Result scenario/alternative identity does not match the validated plan.");

            // Calling generic Verify with IsTestOnly = false and no trust provenance returns Unavailable
            var untrusted = CreateSampleGenuineResultSet(req, plan.SourceScenarioId, plan.SourcePhysicalAlternativeId);
            untrusted.ProjectId = plan.Inputs.ProjectId;
            untrusted.ModelFingerprint = plan.Inputs.ModelFingerprint;
            untrusted.NetworkFingerprint = plan.NetworkFingerprint;
            untrusted.CandidateFingerprint = plan.CandidateFingerprint;
            untrusted.IsTestOnly = false;
            var untrustedPresentation = GravityHydraulicVerificationWorkflow.Verify(plan, untrusted, null);
            Check(untrustedPresentation.DataKind == HydraulicResultDataKind.Unavailable, "Untrusted IsTestOnly=false remains Unavailable.");
            Check(untrustedPresentation.StatusToken == "RESULTS_UNAVAILABLE", "Untrusted presentation token is RESULTS_UNAVAILABLE.");
        }
    }
}
