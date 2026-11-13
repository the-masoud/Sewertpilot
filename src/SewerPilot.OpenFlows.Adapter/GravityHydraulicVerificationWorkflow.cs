using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    // P17 application-level connection between an authorized execution plan and the P16 verification
    // services. It presents results with an explicit genuine / TEST_ONLY / unavailable distinction and
    // fails closed on stale lineage. It never creates approval, execution authority or a redesign.
    public enum HydraulicResultDataKind
    {
        Genuine = 1,
        TestOnly = 2,
        Unavailable = 3
    }

    // P16/P17 result-access provenance authority.
    // A genuine classification may only be established by provenance that an accepted result-access
    // runtime mints and binds to the exact result-set content. HydraulicResultSet.IsTestOnly is caller
    // controlled, so it can only ever downgrade a presentation (TestOnly) and can never establish
    // genuine authority: an ordinary caller that supplies a result set with IsTestOnly = false still
    // receives an unavailable presentation.
    // The typed P16 receipt is issued inside the completed proof boundary; free-form strings never
    // establish authority. This does not promote the global ResultRead capability or grant approval.
    public sealed class HydraulicResultTrustProvenance
    {
        private HydraulicResultTrustProvenance(string resultSetFingerprint, string runtimeEvidenceReference)
        {
            ResultSetFingerprint = resultSetFingerprint;
            RuntimeEvidenceReference = runtimeEvidenceReference;
        }

        // Binds trust to the exact result-set content it was established for; a different content
        // fingerprint never inherits an earlier trust decision.
        public string ResultSetFingerprint { get; private set; }
        public string RuntimeEvidenceReference { get; private set; }
        private string exactContentFingerprint;

        // Retained compatibility guard: strings alone cannot establish result-access authority.
        public static HydraulicResultTrustProvenance AcceptedResultAccessRuntime(string resultSetFingerprint, string runtimeEvidenceReference)
        {
            throw new NotSupportedException("RESULT_ACCESS_RUNTIME_UNAVAILABLE: " + HydraulicResultReadBoundary.MissingRuntimeProof);
        }

        public static HydraulicResultTrustProvenance AcceptedResultAccessRuntime(P16ResultContentAuthority authority, HydraulicResultSet results)
        {
            if (authority == null) throw new ArgumentNullException(nameof(authority));
            authority.ValidateContent(results);
            return new HydraulicResultTrustProvenance(HydraulicResultSet.ComputeFingerprint(results),
                "P16:" + authority.RuntimeRunId + ":" + authority.ProofFingerprint)
            {
                exactContentFingerprint = P16ResultContentAuthority.ExactContentFingerprint(results)
            };
        }

        internal bool Matches(HydraulicResultSet results, string fingerprint) =>
            string.Equals(ResultSetFingerprint, fingerprint, StringComparison.Ordinal) &&
            (exactContentFingerprint == null || string.Equals(exactContentFingerprint,
                P16ResultContentAuthority.ExactContentFingerprint(results), StringComparison.Ordinal));

        // Internal TEST_ONLY seam: proves the trusted presentation path deterministically without
        // granting production authority. It refuses any context that is not explicitly TEST_ONLY, so it
        // can never be reached by production composition.
        internal static HydraulicResultTrustProvenance TestOnly(string resultSetFingerprint, string testOnlyContext, string runtimeEvidenceReference)
        {
            GravityExecutionPlanBuilder.Require(!string.IsNullOrWhiteSpace(testOnlyContext) &&
                testOnlyContext.StartsWith("TEST_ONLY", StringComparison.Ordinal), "TEST_ONLY_REQUIRED");
            return new HydraulicResultTrustProvenance(resultSetFingerprint, runtimeEvidenceReference);
        }
    }

    public sealed class ProductionResultLineageBridge
    {
        internal ProductionResultLineageBridge(
            string sourceProjectId,
            string sourceModelFingerprint,
            string sourceNetworkFingerprint,
            string sourceCandidateFingerprint,
            string executionPlanFingerprint,
            int sourceScenarioId,
            int sourcePhysicalAlternativeId,
            string workingCopyId,
            string protectedBaselinePath,
            string protectedBaselineFingerprint,
            int createdScenarioId,
            int createdPhysicalAlternativeId,
            string outputFingerprint,
            int outputInventoryCount,
            string outputModelFamilyFingerprint,
            string executionRunId,
            string calculationEventId,
            string proofFingerprint,
            string engine,
            DateTimeOffset solverStartedUtc,
            DateTimeOffset solverCompletedUtc,
            DateTimeOffset savedUtc,
            string observedContentFingerprint)
        {
            SourceProjectId = sourceProjectId;
            SourceModelFingerprint = sourceModelFingerprint;
            SourceNetworkFingerprint = sourceNetworkFingerprint;
            SourceCandidateFingerprint = sourceCandidateFingerprint;
            ExecutionPlanFingerprint = executionPlanFingerprint;
            SourceScenarioId = sourceScenarioId;
            SourcePhysicalAlternativeId = sourcePhysicalAlternativeId;
            WorkingCopyId = workingCopyId;
            ProtectedBaselinePath = protectedBaselinePath;
            ProtectedBaselineFingerprint = protectedBaselineFingerprint;
            CreatedScenarioId = createdScenarioId;
            CreatedPhysicalAlternativeId = createdPhysicalAlternativeId;
            OutputFingerprint = outputFingerprint;
            OutputInventoryCount = outputInventoryCount;
            OutputModelFamilyFingerprint = outputModelFamilyFingerprint;
            ExecutionRunId = executionRunId;
            CalculationEventId = calculationEventId;
            ProofFingerprint = proofFingerprint;
            Engine = engine;
            SolverStartedUtc = solverStartedUtc;
            SolverCompletedUtc = solverCompletedUtc;
            SavedUtc = savedUtc;
            ObservedContentFingerprint = observedContentFingerprint;
        }

        public string SourceProjectId { get; }
        public string SourceModelFingerprint { get; }
        public string SourceNetworkFingerprint { get; }
        public string SourceCandidateFingerprint { get; }
        public string ExecutionPlanFingerprint { get; }
        public int SourceScenarioId { get; }
        public int SourcePhysicalAlternativeId { get; }
        public string WorkingCopyId { get; }
        public string ProtectedBaselinePath { get; }
        public string ProtectedBaselineFingerprint { get; }
        public int CreatedScenarioId { get; }
        public int CreatedPhysicalAlternativeId { get; }
        public string OutputFingerprint { get; }
        public int OutputInventoryCount { get; }
        public string OutputModelFamilyFingerprint { get; }
        public string ExecutionRunId { get; }
        public string CalculationEventId { get; }
        public string ProofFingerprint { get; }
        public string Engine { get; }
        public DateTimeOffset SolverStartedUtc { get; }
        public DateTimeOffset SolverCompletedUtc { get; }
        public DateTimeOffset SavedUtc { get; }
        public string ObservedContentFingerprint { get; }

        public void ValidateLineage(GravityExecutionPlan plan, HydraulicResultSet results)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (results == null) throw new ArgumentNullException(nameof(results));

            Action<bool, string> require = (valid, detail) =>
            {
                if (!valid) throw new InvalidOperationException("HYDRAULIC_RESULT_LINEAGE_MISMATCH: " + detail);
            };

            // 1. Source project and source model identity
            require(!string.IsNullOrWhiteSpace(SourceProjectId) &&
                string.Equals(SourceProjectId, plan.Inputs?.ProjectId, StringComparison.Ordinal),
                "Source project identity does not match the validated plan.");
            require(!string.IsNullOrWhiteSpace(SourceModelFingerprint) &&
                string.Equals(SourceModelFingerprint, plan.Inputs?.ModelFingerprint, StringComparison.OrdinalIgnoreCase),
                "Source model fingerprint does not match the validated plan.");

            // 2. Source network fingerprint
            require(!string.IsNullOrWhiteSpace(SourceNetworkFingerprint) &&
                string.Equals(SourceNetworkFingerprint, plan.NetworkFingerprint, StringComparison.Ordinal),
                "Lineage network fingerprint does not match the validated plan.");
            require(!string.IsNullOrWhiteSpace(results.NetworkFingerprint) &&
                string.Equals(results.NetworkFingerprint, plan.NetworkFingerprint, StringComparison.Ordinal),
                "Result network fingerprint does not match the validated plan.");

            // 3. Candidate fingerprint and exact execution-plan fingerprint as separate identities
            require(!string.IsNullOrWhiteSpace(SourceCandidateFingerprint) &&
                string.Equals(SourceCandidateFingerprint, plan.CandidateFingerprint, StringComparison.Ordinal),
                "Candidate fingerprint does not match the validated plan.");
            require(!string.IsNullOrWhiteSpace(ExecutionPlanFingerprint) &&
                string.Equals(ExecutionPlanFingerprint, plan.PlanFingerprint, StringComparison.Ordinal),
                "Execution plan fingerprint does not match the validated plan.");
            require(!string.IsNullOrWhiteSpace(results.CandidateFingerprint) &&
                (string.Equals(results.CandidateFingerprint, plan.PlanFingerprint, StringComparison.Ordinal) ||
                 string.Equals(results.CandidateFingerprint, plan.CandidateFingerprint, StringComparison.Ordinal)),
                "Result candidate/plan fingerprint does not match the validated plan.");

            // 4. Source scenario and physical alternative
            require(SourceScenarioId == plan.SourceScenarioId && plan.SourceScenarioId > 0,
                "Source scenario identity does not match the validated plan.");
            require(SourcePhysicalAlternativeId == plan.SourcePhysicalAlternativeId && plan.SourcePhysicalAlternativeId > 0,
                "Source physical alternative identity does not match the validated plan.");

            // 5. Authorized controlled working-copy identity and protected-baseline relationship
            require(!string.IsNullOrWhiteSpace(WorkingCopyId) &&
                string.Equals(results.ProjectId, WorkingCopyId, StringComparison.Ordinal),
                "Result working copy project identity does not match the authorized execution.");
            if (!string.IsNullOrWhiteSpace(ProtectedBaselineFingerprint))
            {
                require(string.Equals(ProtectedBaselineFingerprint, plan.Inputs?.ModelFingerprint, StringComparison.OrdinalIgnoreCase),
                    "Protected baseline fingerprint does not match the source model fingerprint.");
            }

            // 6. Actual created scenario and physical alternative
            require(CreatedScenarioId > 0 && results.ScenarioId == CreatedScenarioId,
                "Actual result scenario ID does not match the isolated execution scenario.");
            require(CreatedPhysicalAlternativeId > 0 && results.PhysicalAlternativeId == CreatedPhysicalAlternativeId,
                "Actual result physical alternative ID does not match the isolated execution alternative.");
            require(CreatedScenarioId != SourceScenarioId,
                "Actual isolated scenario ID must be distinct from source scenario ID.");
            require(CreatedPhysicalAlternativeId != SourcePhysicalAlternativeId,
                "Actual isolated physical alternative ID must be distinct from source alternative ID.");

            // 7. Genuine output inventory, output model-family fingerprint, and integrity evidence
            require(!string.IsNullOrWhiteSpace(OutputFingerprint),
                "Output fingerprint is missing from execution receipt.");
            require(OutputInventoryCount >= 3,
                "Output inventory count is insufficient for genuine production execution.");
            if (!string.IsNullOrWhiteSpace(OutputModelFamilyFingerprint))
            {
                require(string.Equals(results.ModelFingerprint, OutputModelFamilyFingerprint, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(results.ModelFingerprint, plan.NetworkFingerprint, StringComparison.Ordinal) ||
                    string.Equals(results.ModelFingerprint, plan.Inputs?.ModelFingerprint, StringComparison.OrdinalIgnoreCase),
                    "Result model fingerprint does not match the execution output model-family fingerprint.");
            }

            // 8. Completed solver lifecycle & runtime run
            require(!string.IsNullOrWhiteSpace(ExecutionRunId),
                "Execution run ID is missing from execution receipt.");
            require(!string.IsNullOrWhiteSpace(ProofFingerprint),
                "Proof fingerprint is missing from execution receipt.");
            require(SolverStartedUtc <= SolverCompletedUtc && SolverCompletedUtc <= SavedUtc,
                "Solver and save lifecycle timestamps are out of order.");

            // 9. Exact result content provenance
            require(!string.IsNullOrWhiteSpace(ObservedContentFingerprint),
                "Observed content fingerprint is missing from execution receipt.");
        }
    }

    public sealed class GravityHydraulicResultPresentation
    {
        // Canonical availability tokens shared with the application presentation surface.
        public const string UnavailableToken = "RESULTS_UNAVAILABLE";
        public const string TestOnlyToken = "RESULTS_TEST_ONLY";
        public const string GenuineToken = "RESULTS_GENUINE";

        public HydraulicResultDataKind DataKind { get; set; }
        public HydraulicVerificationReport Report { get; set; }
        public string PlanFingerprint { get; set; }
        public string CandidateFingerprint { get; set; }
        public string ResultFingerprint { get; set; }
        public string CriteriaFingerprint { get; set; }
        // True only when accepted result-access provenance bound to this exact content was supplied.
        public bool TrustedProvenanceEstablished { get; set; }
        // A TEST_ONLY fixture or an unavailable result set can never claim engineering compliance.
        public bool ComplianceClaimed { get; set; }
        public ProductionResultLineageBridge Lineage { get; set; }
        public List<string> MissingInputs { get; set; } = new List<string>();
        public string Summary { get; set; }

        // Truthful availability token for the application surface.
        public string StatusToken
        {
            get
            {
                switch (DataKind)
                {
                    case HydraulicResultDataKind.Genuine: return GenuineToken;
                    case HydraulicResultDataKind.TestOnly: return TestOnlyToken;
                    default: return UnavailableToken;
                }
            }
        }

        // Read-only presentation. It always states the ResultRead capability boundary and never renders
        // an engineering compliance claim unless genuine trusted results actually established one.
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("Hydraulic result status: ").Append(StatusToken).Append("\r\n");
            sb.Append("Summary: ").Append(Summary ?? "Hydraulic results are unavailable.").Append("\r\n");
            sb.Append("ResultRead=").Append(HydraulicResultReadBoundary.CapabilityStateName);
            if (TrustedProvenanceEstablished)
            {
                sb.Append(": capability-level runtime acceptance remains NotTested; provenance established for this execution receipt.\r\n");
            }
            else
            {
                sb.Append(": no accepted runtime evidence establishes vendor result access.\r\n");
            }
            sb.Append("Trusted result-access provenance established: ").Append(TrustedProvenanceEstablished ? "yes" : "no").Append("\r\n");
            sb.Append("Engineering compliance claimed: ").Append(ComplianceClaimed ? "yes" : "no").Append("\r\n");
            if (DataKind == HydraulicResultDataKind.TestOnly)
                sb.Append("TEST_ONLY: deterministic fixture results are not engineering acceptance.\r\n");
            if (Lineage != null)
                sb.Append("Lineage bridge: sourceScenario=").Append(Lineage.SourceScenarioId)
                  .Append(" createdScenario=").Append(Lineage.CreatedScenarioId)
                  .Append(" planFingerprint=").Append(Lineage.ExecutionPlanFingerprint ?? "").Append("\r\n");
            if (Report != null)
                sb.Append("Findings: pass=").Append(Report.PassCount)
                  .Append(" violation=").Append(Report.ViolationCount)
                  .Append(" notAvailable=").Append(Report.NotAvailableCount)
                  .Append(" notApplicable=").Append(Report.NotApplicableCount)
                  .Append(" invalidInput=").Append(Report.InvalidInputCount).Append("\r\n");
            if (MissingInputs != null)
                foreach (string missing in MissingInputs)
                    sb.Append("Action required: ").Append(missing).Append("\r\n");
            return sb.ToString();
        }
    }

    public static class GravityHydraulicVerificationWorkflow
    {
        public static GravityHydraulicResultPresentation Verify(GravityExecutionPlan plan,
            HydraulicResultSet results, IEnumerable<HydraulicVerificationCriterion> criteria)
        {
            return Verify(plan, results, criteria, null);
        }

        // The trusted-provenance overload. Genuine authority is established only by accepted result-access
        // provenance bound to this exact result-set content; HydraulicResultSet.IsTestOnly = false is a
        // caller-controlled flag and is never sufficient on its own.
        public static GravityHydraulicResultPresentation Verify(GravityExecutionPlan plan,
            HydraulicResultSet results, IEnumerable<HydraulicVerificationCriterion> criteria,
            HydraulicResultTrustProvenance trust)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (results == null) return Unavailable(plan, "No hydraulic result set was supplied.");

            ValidateLineage(plan, results);

            var criterionList = (criteria ?? Enumerable.Empty<HydraulicVerificationCriterion>()).Where(c => c != null).ToList();
            bool testOnly = results.IsTestOnly;
            bool hasElements = results.Elements != null && results.Elements.Count > 0;

            var presentation = new GravityHydraulicResultPresentation
            {
                PlanFingerprint = plan.PlanFingerprint,
                CandidateFingerprint = plan.CandidateFingerprint,
                ResultFingerprint = HydraulicResultSet.ComputeFingerprint(results),
                CriteriaFingerprint = HydraulicVerificationReport.ComputeCriteriaFingerprint(criterionList),
                ComplianceClaimed = false
            };
            bool trusted = trust != null && trust.Matches(results, presentation.ResultFingerprint);
            presentation.TrustedProvenanceEstablished = trusted;

            if (!hasElements)
            {
                presentation.DataKind = HydraulicResultDataKind.Unavailable;
                presentation.MissingInputs.Add("Hydraulic results are not present for any element.");
                presentation.Summary = "No genuine hydraulic results are available; engineering compliance cannot be assessed.";
                return presentation;
            }

            if (testOnly)
            {
                presentation.DataKind = HydraulicResultDataKind.TestOnly;
                presentation.Report = GravityHydraulicVerificationEngine.Verify(results, criterionList);
                if (criterionList.Count == 0)
                    presentation.MissingInputs.Add("No explicit hydraulic verification criteria were supplied from the reviewed Design Basis.");
                presentation.MissingInputs.Add("Result set is TEST_ONLY and cannot establish engineering compliance.");
                presentation.Summary = "TEST_ONLY hydraulic results verified deterministically; this is not engineering acceptance.";
                return presentation;
            }

            if (!trusted)
            {
                // Fail closed. Result data was supplied without accepted result-access provenance, so it is
                // not available as genuine engineering evidence and no compliance assessment is rendered.
                presentation.DataKind = HydraulicResultDataKind.Unavailable;
                presentation.MissingInputs.Add("Supplied result data carries no accepted result-access provenance; " +
                    "a caller-controlled IsTestOnly = false cannot establish genuine hydraulic results.");
                presentation.MissingInputs.Add("ResultRead=" + HydraulicResultReadBoundary.CapabilityStateName +
                    ": " + HydraulicResultReadBoundary.MissingRuntimeProof);
                presentation.Summary = "RESULTS_UNAVAILABLE: hydraulic result data was supplied without accepted result-access provenance.";
                return presentation;
            }

            presentation.DataKind = HydraulicResultDataKind.Genuine;
            presentation.Report = GravityHydraulicVerificationEngine.Verify(results, criterionList);
            if (criterionList.Count == 0)
                presentation.MissingInputs.Add("No explicit hydraulic verification criteria were supplied from the reviewed Design Basis.");
            presentation.Summary = criterionList.Count == 0
                ? "Genuine hydraulic results are available, but no explicit hydraulic verification criteria were supplied; engineering compliance cannot be assessed."
                : presentation.Report.HasViolations
                ? "Genuine hydraulic results verified: engineering violations reported."
                : "Genuine hydraulic results verified against explicit criteria.";
            return presentation;
        }

        // P16-C5 production result verification entry point.
        // Requires a valid P16ResultContentAuthority carrying an accepted production receipt lineage bridge.
        // Validates source plan and runtime execution lineage, verifies exact result content provenance,
        // and presents genuine results without weakening existing fail-closed checks or claiming engineering
        // compliance when reviewed criteria are missing.
        public static GravityHydraulicResultPresentation VerifyProductionResult(
            GravityExecutionPlan plan,
            P16ResultContentAuthority authority,
            HydraulicResultSet results,
            IEnumerable<HydraulicVerificationCriterion> criteria)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (authority == null) throw new ArgumentNullException(nameof(authority));
            if (results == null) return Unavailable(plan, "No hydraulic result set was supplied.");

            if (authority.Lineage == null || !authority.IsProductionReceipt)
                throw new InvalidOperationException("PRODUCTION_AUTHORITY_INVALID: Authority does not carry an accepted production receipt lineage bridge.");

            // 1. Validate complete lineage bridge against source plan and execution result
            authority.Lineage.ValidateLineage(plan, results);

            // 2. Validate exact result content against authority
            authority.ValidateContent(results);

            // 3. Establish trust provenance bound to authority and results
            var trust = HydraulicResultTrustProvenance.AcceptedResultAccessRuntime(authority, results);

            var criterionList = (criteria ?? Enumerable.Empty<HydraulicVerificationCriterion>()).Where(c => c != null).ToList();
            bool hasElements = results.Elements != null && results.Elements.Count > 0;

            var presentation = new GravityHydraulicResultPresentation
            {
                PlanFingerprint = plan.PlanFingerprint,
                CandidateFingerprint = plan.CandidateFingerprint,
                ResultFingerprint = HydraulicResultSet.ComputeFingerprint(results),
                CriteriaFingerprint = HydraulicVerificationReport.ComputeCriteriaFingerprint(criterionList),
                Lineage = authority.Lineage,
                TrustedProvenanceEstablished = true,
                ComplianceClaimed = false
            };

            if (!hasElements)
            {
                presentation.DataKind = HydraulicResultDataKind.Unavailable;
                presentation.MissingInputs.Add("Hydraulic results are not present for any element.");
                presentation.Summary = "No genuine hydraulic results are available; engineering compliance cannot be assessed.";
                return presentation;
            }

            presentation.DataKind = HydraulicResultDataKind.Genuine;
            presentation.Report = GravityHydraulicVerificationEngine.Verify(results, criterionList);

            if (criterionList.Count == 0)
            {
                presentation.MissingInputs.Add("No explicit hydraulic verification criteria were supplied from the reviewed Design Basis.");
                presentation.Summary = "Genuine hydraulic results are available, but no explicit hydraulic verification criteria were supplied; engineering compliance cannot be assessed.";
            }
            else if (presentation.Report.HasViolations)
            {
                presentation.Summary = "Genuine hydraulic results verified: engineering violations reported.";
            }
            else
            {
                presentation.Summary = "Genuine hydraulic results verified against explicit criteria.";
            }

            return presentation;
        }

        public static GravityHydraulicResultPresentation Unavailable(GravityExecutionPlan plan, string reason)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var presentation = new GravityHydraulicResultPresentation
            {
                DataKind = HydraulicResultDataKind.Unavailable,
                PlanFingerprint = plan.PlanFingerprint,
                CandidateFingerprint = plan.CandidateFingerprint,
                ComplianceClaimed = false,
                Summary = reason ?? "Hydraulic results are unavailable."
            };
            presentation.MissingInputs.Add("ResultRead=" + HydraulicResultReadBoundary.CapabilityStateName +
                ": " + HydraulicResultReadBoundary.MissingRuntimeProof);
            return presentation;
        }

        // Stale or mismatched lineage is rejected before any verification is reported.
        private static void ValidateLineage(GravityExecutionPlan plan, HydraulicResultSet results)
        {
            Action<bool, string> require = (valid, detail) =>
            {
                if (!valid) throw new InvalidOperationException("HYDRAULIC_RESULT_LINEAGE_MISMATCH: " + detail);
            };
            require(!string.IsNullOrWhiteSpace(plan.NetworkFingerprint) &&
                string.Equals(results.NetworkFingerprint, plan.NetworkFingerprint, StringComparison.Ordinal),
                "Result network fingerprint does not match the validated plan.");
            require(!string.IsNullOrWhiteSpace(plan.CandidateFingerprint) &&
                string.Equals(results.CandidateFingerprint, plan.CandidateFingerprint, StringComparison.Ordinal),
                "Result candidate fingerprint does not match the validated plan.");
            require(results.ScenarioId == plan.SourceScenarioId && results.PhysicalAlternativeId == plan.SourcePhysicalAlternativeId,
                "Result scenario/alternative identity does not match the validated plan.");
            require(string.Equals(results.ProjectId, plan.Inputs?.ProjectId, StringComparison.Ordinal) &&
                string.Equals(results.ModelFingerprint, plan.Inputs?.ModelFingerprint, StringComparison.OrdinalIgnoreCase),
                "Result project/model identity does not match the validated plan.");
        }
    }
}
