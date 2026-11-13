using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        // Retained real P16 observations; no vendor process, model access or fabricated vendor double.
        private static HostRequest AuthorityRequest() => RuntimeHostProtocol.Serializer().Deserialize<HostRequest>(
            File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "P16AcceptedProof", "request.json")));
        private static HostResult AuthorityResult() => RuntimeHostProtocol.Serializer().Deserialize<HostResult>(
            File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "P16AcceptedProof", "result.json")));

        private static IntegritySnapshot AuthorityInventory(HostRequest request, HostResult result)
        {
            var entries = RuntimeHostProtocol.Serializer().Deserialize<string[]>(result.Diagnostics["ResultFilesInventoryJson"])
                .Select(row => row.Split('|')).Select(p => new FileIntegrityEntry(p[0], long.Parse(p[1], CultureInfo.InvariantCulture), p[2]))
                .ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
            return new IntegritySnapshot(ModelFamilyFingerprint.Create(Path.GetFileName(request.WorkingModelPath),
                entries.Values.Select(e => e.ToModelFamilyFileEntry())), DateTimeOffset.UtcNow, entries);
        }

        private static P16ResultContentAuthority RecordedAuthority()
        {
            var request = AuthorityRequest();
            var result = AuthorityResult();
            return P16ResultContentAuthority.FromCompletedProof(request, result, AuthorityInventory(request, result));
        }

        private static HydraulicResultSet AuthorityContent(P16ResultContentAuthority authority) =>
            authority.CreateResultSet("P17_CONTENT_AUTHORITY_TEST_PROJECT", "P17_CONTENT_AUTHORITY_TEST_NETWORK", "P17_CONTENT_AUTHORITY_TEST_CANDIDATE");

        private static GravityExecutionPlan AuthorityPlan(HydraulicResultSet result) => new GravityExecutionPlan
        {
            PlanFingerprint = "P17_CONTENT_AUTHORITY_TEST_PLAN", NetworkFingerprint = result.NetworkFingerprint,
            CandidateFingerprint = result.CandidateFingerprint, SourceScenarioId = result.ScenarioId,
            SourcePhysicalAlternativeId = result.PhysicalAlternativeId,
            Inputs = new GravityEngineeringInputs { ProjectId = result.ProjectId, ModelFingerprint = result.ModelFingerprint }
        };

        private static void AuthorityRejects(Action action, string code)
        {
            try { action(); }
            catch (InvalidDataException error)
            {
                Check(error.Message.Contains(code), "Authority rejection names " + code + "; actual: " + error.Message);
                return;
            }
            throw new Exception("Expected P16 content authority rejection: " + code);
        }

        private static void P17_ContentAuthority_AcceptsRecordedP16Observation()
        {
            var authority = RecordedAuthority();
            var result = AuthorityContent(authority);
            var recorded = AuthorityResult();
            var trust = HydraulicResultTrustProvenance.AcceptedResultAccessRuntime(authority, result);
            var view = GravityHydraulicVerificationWorkflow.Verify(AuthorityPlan(result), result,
                new HydraulicVerificationCriterion[0], trust);
            Check(view.DataKind == HydraulicResultDataKind.Genuine && view.TrustedProvenanceEstablished && !view.ComplianceClaimed,
                "Retained genuine P16 observation is content-bound without engineering approval or criteria.");
            Check(view.Summary == "Genuine hydraulic results are available, but no explicit hydraulic verification criteria were supplied; engineering compliance cannot be assessed.",
                "Genuine observations without criteria cannot claim an engineering assessment.");
            Check(view.Summary.IndexOf("verified against explicit criteria", StringComparison.OrdinalIgnoreCase) < 0,
                "The no-criteria summary does not claim verification against explicit criteria.");
            Check(view.MissingInputs.Any(m => m == "No explicit hydraulic verification criteria were supplied from the reviewed Design Basis."),
                "The missing-criteria warning remains present.");
            Check(view.Report != null && view.Report.Findings.Count == 0 && view.Report.PassCount == 0 && view.Report.ViolationCount == 0,
                "No criterion result or engineering compliance outcome is fabricated when criteria are absent.");
            Check(result.Elements.Single().Metrics.Single().NumericValue == double.Parse(recorded.Diagnostics["FlowValue"], CultureInfo.InvariantCulture) &&
                result.Elements.Single().Metrics.Single().Unit == recorded.Diagnostics["FlowUnit"], "Actual P16 value and unit are preserved; no fixture value is invented.");
            Check(authority.ScenarioId == int.Parse(recorded.Diagnostics["ObservedScenarioId"], CultureInfo.InvariantCulture) &&
                authority.PhysicalAlternativeId == int.Parse(recorded.Diagnostics["ObservedPhysicalAlternativeId"], CultureInfo.InvariantCulture) &&
                authority.CalculationOptionsId == int.Parse(recorded.Diagnostics["ObservedCalculationOptionsId"], CultureInfo.InvariantCulture) &&
                authority.Engine == recorded.Diagnostics["ObservedEngine"] && authority.CalculationEventId == recorded.Diagnostics["CalculationEventId"],
                "P16 observed scenario/alternative/options/engine/event identities are bound.");
            Check(authority.OutputFiles.Count == AuthorityInventory(AuthorityRequest(), recorded).Entries.Count &&
                authority.SolverStartedUtc <= authority.SolverCompletedUtc && authority.SolverCompletedUtc <= authority.SavedUtc,
                "Persisted outputs and the observed calculation/save freshness chain are retained.");
            Check(typeof(P16ResultContentAuthority).GetConstructors().Length == 0 &&
                typeof(P16ResultContentAuthority).GetProperties().All(p => p.GetSetMethod(true) == null), "Authority has no public constructor or mutable properties.");
            var files = authority.OutputFiles as IList<FileIntegrityEntry>;
            Check(files != null && files.IsReadOnly, "Output inventory has no mutable collection alias.");
            Throws<NotSupportedException>(() => files.Clear());
            result.Elements.Single().Metrics.Single().NumericValue = null;
            Check(AuthorityContent(authority).Elements.Single().Metrics.Single().NumericValue.HasValue,
                "Returned result objects cannot mutate the immutable observation.");
        }

        private static void P17_ContentAuthority_RejectsCallerStringsAndMissingAuthority()
        {
            var result = AuthorityContent(RecordedAuthority());
            Throws<NotSupportedException>(() => HydraulicResultTrustProvenance.AcceptedResultAccessRuntime(
                HydraulicResultSet.ComputeFingerprint(result), result.Provenance.EvidenceReference));
            Throws<ArgumentNullException>(() => HydraulicResultTrustProvenance.AcceptedResultAccessRuntime((P16ResultContentAuthority)null, result));
            Check(GravityHydraulicVerificationWorkflow.Verify(AuthorityPlan(result), result,
                new HydraulicVerificationCriterion[0]).DataKind == HydraulicResultDataKind.Unavailable, "Metadata strings without a receipt remain unavailable.");
            Check(AuthorityResult().ContentAuthority == null, "Deserializing even genuine IPC JSON cannot mint a completion authority.");
        }

        private static void P17_ContentAuthority_RejectsContentAndLineageTampering()
        {
            var authority = RecordedAuthority();
            Action<HydraulicResultSet>[] edits =
            {
                r => r.ModelFingerprint = "TAMPERED_MODEL",
                r => r.ScenarioId++, r => r.PhysicalAlternativeId++,
                r => r.CalculationRunId = Guid.NewGuid().ToString("N"),
                r => r.CalculationContext = "CALLER_ENGINE_OR_OPTIONS",
                r => r.Elements.Single().Metrics.Single().NumericValue += 1,
                r => r.Elements.Single().Metrics.Single().Unit = "TAMPERED_UNIT",
                r => r.Elements.Single().Metrics.Clear(),
                r => r.Provenance.Notes = "CALLER_PROVENANCE",
                r => r.ResultTimeSeconds += 1
            };
            foreach (var edit in edits)
            {
                var result = AuthorityContent(authority);
                var trust = HydraulicResultTrustProvenance.AcceptedResultAccessRuntime(authority, result);
                var plan = AuthorityPlan(result);
                edit(result);
                AuthorityRejects(() => HydraulicResultTrustProvenance.AcceptedResultAccessRuntime(authority, result), "CONTENT_MISMATCH");
                if (result.ModelFingerprint != authority.ModelFingerprint || result.ScenarioId != authority.ScenarioId || result.PhysicalAlternativeId != authority.PhysicalAlternativeId)
                    Throws<InvalidOperationException>(() => GravityHydraulicVerificationWorkflow.Verify(plan, result, new HydraulicVerificationCriterion[0], trust));
                else
                    Check(GravityHydraulicVerificationWorkflow.Verify(plan, result, new HydraulicVerificationCriterion[0], trust).DataKind == HydraulicResultDataKind.Unavailable,
                        "A previously issued receipt never transfers to altered content, including top-level time and free-form provenance.");
            }
            foreach (var edit in new Action<HydraulicResultSet>[] { r => r.ProjectId += "_OTHER", r => r.NetworkFingerprint += "_OTHER", r => r.CandidateFingerprint += "_OTHER" })
            {
                var result = AuthorityContent(authority);
                var plan = AuthorityPlan(result);
                var trust = HydraulicResultTrustProvenance.AcceptedResultAccessRuntime(authority, result);
                edit(result);
                Throws<InvalidOperationException>(() => GravityHydraulicVerificationWorkflow.Verify(plan, result, new HydraulicVerificationCriterion[0], trust));
            }
            var testOnly = AuthorityContent(authority);
            testOnly.IsTestOnly = true;
            AuthorityRejects(() => HydraulicResultTrustProvenance.AcceptedResultAccessRuntime(authority, testOnly), "GENUINE_CONTENT_REQUIRED");
        }

        private static void P17_ContentAuthority_RejectsTamperedP16ContextAndStaleOutputs()
        {
            string[] fields = { "ObservedScenarioId", "ObservedPhysicalAlternativeId", "ObservedCalculationOptionsId", "ObservedEngine", "CalculationEventId" };
            foreach (string field in fields)
            {
                var request = AuthorityRequest(); var result = AuthorityResult();
                var inventory = AuthorityInventory(request, result);
                result.Diagnostics[field] = field == "CalculationEventId" ? Guid.NewGuid().ToString("N") :
                    (field.EndsWith("Id", StringComparison.Ordinal) ? "999" : "TAMPERED");
                AuthorityRejects(() => P16ResultContentAuthority.FromCompletedProof(request, result, inventory),
                    field == "CalculationEventId" ? "P16_PROVENANCE_CHAIN_MISMATCH" : "P16_CONTEXT_MISMATCH");
            }
            foreach (string flag in new[] { "HasResults", "FlowFieldHasResults", "SolverStatusSuccess", "FirstSessionCloseCompleted", "ReopenSucceeded" })
            {
                var request = AuthorityRequest(); var result = AuthorityResult();
                var inventory = AuthorityInventory(request, result);
                result.Diagnostics[flag] = "false";
                AuthorityRejects(() => P16ResultContentAuthority.FromCompletedProof(request, result, inventory), "P16_FRESHNESS_OR_RESULT_MISSING");
            }
            {
                var request = AuthorityRequest(); var result = AuthorityResult();
                var inventory = AuthorityInventory(request, result);
                request.Diagnostics["P16Classification"] = "TEST_ONLY_FIXTURE";
                AuthorityRejects(() => P16ResultContentAuthority.FromCompletedProof(request, result, inventory), "P16_REQUEST_CONTEXT_MISMATCH");
            }
            {
                var request = AuthorityRequest(); var result = AuthorityResult();
                var inventory = AuthorityInventory(request, result);
                result.Diagnostics["SaveTimestamp"] = DateTimeOffset.MinValue.ToString("o", CultureInfo.InvariantCulture);
                AuthorityRejects(() => P16ResultContentAuthority.FromCompletedProof(request, result, inventory), "INVALID_CALCULATION_EVENT_OR_FRESHNESS");
            }
            {
                var request = AuthorityRequest(); var result = AuthorityResult();
                var inventory = AuthorityInventory(request, result);
                var entries = RuntimeHostProtocol.Serializer().Deserialize<string[]>(result.Diagnostics["ResultFilesInventoryJson"]);
                var parts = entries.Last().Split('|'); parts[2] = new string('0', 64); entries[entries.Length - 1] = string.Join("|", parts);
                result.Diagnostics["ResultFilesInventoryJson"] = RuntimeHostProtocol.Serializer().Serialize(entries);
                AuthorityRejects(() => P16ResultContentAuthority.FromCompletedProof(request, result, inventory), "STALE_OUTPUT_HASH");
            }
            {
                var request = AuthorityRequest(); var result = AuthorityResult();
                var inventory = AuthorityInventory(request, result);
                var wrong = new IntegritySnapshot(new ModelFamilyFingerprint(new string('0', 64), ""), inventory.CapturedUtc,
                    inventory.Entries.ToDictionary(e => e.Key, e => e.Value));
                AuthorityRejects(() => P16ResultContentAuthority.FromCompletedProof(request, result, wrong), "OUTPUT_FINGERPRINT_MISMATCH");
            }
        }
    }
}
