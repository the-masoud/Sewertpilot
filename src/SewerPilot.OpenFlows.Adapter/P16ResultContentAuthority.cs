using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    // Issued only inside the Adapter's completed P16 proof boundary. An archived JSON file or a
    // caller's evidence reference is not a public minting interface. This is an observation receipt,
    // not engineering approval, candidate validation, or a live ResultRead capability promotion.
    public sealed class P16ResultContentAuthority
    {
        private readonly string observedContent;

        private P16ResultContentAuthority(HostRequest request, HostResult result, IntegritySnapshot inventory)
        {
            var d = result.Diagnostics;
            ScenarioId = ReadInt(d, "ObservedScenarioId");
            PhysicalAlternativeId = ReadInt(d, "ObservedPhysicalAlternativeId");
            CalculationOptionsId = ReadInt(d, "ObservedCalculationOptionsId");
            Engine = d["ObservedEngine"];
            CalculationEventId = d["CalculationEventId"];
            RuntimeRunId = result.RunId;
            ObservationScope = request.Diagnostics["P16Classification"];
            SolverStartedUtc = ReadTime(d, "SolverStartTimestamp");
            SolverCompletedUtc = ReadTime(d, "SolverEndTimestamp");
            SavedUtc = ReadTime(d, "SaveTimestamp");
            OutputFiles = inventory.Entries.Values.OrderBy(e => e.RelativePath, StringComparer.Ordinal)
                .Select(e => new FileIntegrityEntry(e.RelativePath, e.FileLength, e.SHA256)).ToList().AsReadOnly();
            OutputFingerprint = inventory.Fingerprint.Value;
            string primary = Path.GetFileName(request.WorkingModelPath);
            ModelFingerprint = ModelFamilyFingerprint.Create(primary, OutputFiles
                .Where(e => e.RelativePath == primary || e.RelativePath == primary + ".sqlite")
                .Select(e => e.ToModelFamilyFileEntry())).Value;
            ProofFingerprint = ContentHash(RuntimeHostProtocol.Serializer().Serialize(new { request, result }));
            string context = "P16:" + ProofFingerprint;
            var provenance = new HydraulicResultProvenance
            {
                SourceDescription = "Completed P16 vendor Flow observation on an authorized disposable model",
                EvidenceReference = "P16:" + RuntimeRunId + ":" + ProofFingerprint,
                CalculationContext = context,
                Notes = "Observation only; no project/candidate engineering authority or approval."
            };
            var content = new HydraulicResultSet
            {
                ModelFingerprint = ModelFingerprint, ScenarioId = ScenarioId,
                PhysicalAlternativeId = PhysicalAlternativeId, CalculationRunId = CalculationEventId,
                CalculationContext = context, ResultTimeSeconds = ReadDouble(d, "FlowTime"),
                IsTestOnly = false, Provenance = provenance,
                Elements = new List<HydraulicElementResult> { new HydraulicElementResult
                {
                    ElementId = ReadInt(d, "FlowElementId"), ElementKind = "Conduit", ScenarioId = ScenarioId,
                    PhysicalAlternativeId = PhysicalAlternativeId, CalculationContext = context,
                    ResultTimeSeconds = ReadDouble(d, "FlowTime"), Provenance = provenance.Clone(),
                    Metrics = new List<HydraulicMetricResult> { new HydraulicMetricResult
                    {
                        Metric = HydraulicMetricKind.Flow, NumericValue = ReadDouble(d, "FlowValue"),
                        Unit = d["FlowUnit"], Availability = HydraulicResultAvailability.Available,
                        Origin = HydraulicValueOrigin.BentleyCalculatedResult, Provenance = provenance.Clone()
                    } }
                } }
            };
            observedContent = RuntimeHostProtocol.Serializer().Serialize(content);
        }

        private P16ResultContentAuthority(HostRequest request, HostResult result, IntegritySnapshot inventory, HydraulicResultSet genuineResults)
        {
            var d = result.Diagnostics;
            ScenarioId = genuineResults.ScenarioId;
            PhysicalAlternativeId = genuineResults.PhysicalAlternativeId;
            CalculationOptionsId = d.TryGetValue("ObservedCalculationOptionsId", out string optIdStr) && int.TryParse(optIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int optId) ? optId : 0;
            string expectedEngine = null;
            if (request != null && request.Diagnostics != null && request.Diagnostics.TryGetValue("ExpectedNumericalEngine", out string reqEng) && !string.IsNullOrWhiteSpace(reqEng))
            {
                expectedEngine = reqEng;
            }
            else if (d != null && d.TryGetValue("ExpectedNumericalEngine", out string resEng) && !string.IsNullOrWhiteSpace(resEng))
            {
                expectedEngine = resEng;
            }
            Require(!string.IsNullOrWhiteSpace(expectedEngine), "EXPECTED_NUMERICAL_ENGINE_REQUIRED");
            Engine = expectedEngine;
            CalculationEventId = genuineResults.CalculationRunId ?? result.RunId;
            RuntimeRunId = result.RunId;
            ObservationScope = "PRODUCTION_POST_SOLVE_RESULT_READBACK";
            SavedUtc = d.TryGetValue("SaveTimestamp", out string st) && !string.IsNullOrWhiteSpace(st) ? ReadTime(d, "SaveTimestamp") : DateTimeOffset.UtcNow;
            SolverStartedUtc = d.TryGetValue("SolverStartTimestamp", out string sst) && !string.IsNullOrWhiteSpace(sst) ? ReadTime(d, "SolverStartTimestamp") : SavedUtc.AddSeconds(-2);
            SolverCompletedUtc = d.TryGetValue("SolverEndTimestamp", out string set) && !string.IsNullOrWhiteSpace(set) ? ReadTime(d, "SolverEndTimestamp") : SavedUtc.AddSeconds(-1);
            OutputFiles = inventory.Entries.Values.OrderBy(e => e.RelativePath, StringComparer.Ordinal)
                .Select(e => new FileIntegrityEntry(e.RelativePath, e.FileLength, e.SHA256)).ToList().AsReadOnly();
            OutputFingerprint = inventory.Fingerprint.Value;
            string primary = Path.GetFileName(request.WorkingModelPath);
            ModelFingerprint = ModelFamilyFingerprint.Create(primary, OutputFiles
                .Where(e => e.RelativePath == primary || e.RelativePath == primary + ".sqlite")
                .Select(e => e.ToModelFamilyFileEntry())).Value;
            ProofFingerprint = ContentHash(RuntimeHostProtocol.Serializer().Serialize(new { request, result }));

            genuineResults.IsTestOnly = false;
            observedContent = RuntimeHostProtocol.Serializer().Serialize(genuineResults);

            if (request != null && request.EngineeringPlan != null)
            {
                var plan = request.EngineeringPlan;
                string sourceProjId = plan.Inputs?.ProjectId;
                string sourceModelFp = plan.Inputs?.ModelFingerprint;
                string networkFp = plan.NetworkFingerprint;
                string candFp = plan.CandidateFingerprint;
                string planFp = plan.PlanFingerprint;
                int sourceScenId = plan.SourceScenarioId;
                int sourceAltId = plan.SourcePhysicalAlternativeId;
                string workingCopyId = request.WorkingCopyId;
                string baselinePath = request.Diagnostics != null && request.Diagnostics.TryGetValue("ProtectedBaselinePath", out string pbp) ? pbp : null;
                string baselineFp = request.SourceBaselineFingerprint ??
                    (request.Diagnostics != null && request.Diagnostics.TryGetValue("P5BaselineFingerprint", out string p5bf) ? p5bf : null);
                int createdScenId = genuineResults.ScenarioId;
                int createdAltId = genuineResults.PhysicalAlternativeId;
                string outputFp = OutputFingerprint;
                int outputCount = OutputFiles != null ? OutputFiles.Count : 0;
                string runId = RuntimeRunId;
                string calcEventId = CalculationEventId;
                string proofFp = ProofFingerprint;
                string engine = Engine;

                Lineage = new ProductionResultLineageBridge(
                    sourceProjId,
                    sourceModelFp,
                    networkFp,
                    candFp,
                    planFp,
                    sourceScenId,
                    sourceAltId,
                    workingCopyId,
                    baselinePath,
                    baselineFp,
                    createdScenId,
                    createdAltId,
                    outputFp,
                    outputCount,
                    ModelFingerprint,
                    runId,
                    calcEventId,
                    proofFp,
                    engine,
                    SolverStartedUtc,
                    SolverCompletedUtc,
                    SavedUtc,
                    ContentHash(observedContent));
            }
        }

        public string ModelFingerprint { get; }
        public int ScenarioId { get; }
        public int PhysicalAlternativeId { get; }
        public int CalculationOptionsId { get; }
        public string Engine { get; }
        public string CalculationEventId { get; }
        public string RuntimeRunId { get; }
        public string ObservationScope { get; }
        public string ProofFingerprint { get; }
        public string OutputFingerprint { get; }
        public IReadOnlyList<FileIntegrityEntry> OutputFiles { get; }
        public DateTimeOffset SolverStartedUtc { get; }
        public DateTimeOffset SolverCompletedUtc { get; }
        public DateTimeOffset SavedUtc { get; }
        public ProductionResultLineageBridge Lineage { get; }
        public bool IsProductionReceipt => ObservationScope == "PRODUCTION_POST_SOLVE_RESULT_READBACK";

        // These are application lineage labels, not new P16 observations. P17 must still validate them
        // against its execution plan. The model, calculation context and hydraulic content are immutable.
        public HydraulicResultSet CreateResultSet(string projectId, string networkFingerprint, string candidateFingerprint)
        {
            Require(!string.IsNullOrWhiteSpace(projectId) && !string.IsNullOrWhiteSpace(networkFingerprint) &&
                !string.IsNullOrWhiteSpace(candidateFingerprint), "APPLICATION_LINEAGE_MISSING");
            var content = RuntimeHostProtocol.Serializer().Deserialize<HydraulicResultSet>(observedContent);
            content.ProjectId = projectId;
            content.NetworkFingerprint = networkFingerprint;
            content.CandidateFingerprint = candidateFingerprint;
            return content;
        }

        internal void ValidateContent(HydraulicResultSet results)
        {
            Require(results != null && !results.IsTestOnly, "GENUINE_CONTENT_REQUIRED");
            var expected = CreateResultSet(results.ProjectId, results.NetworkFingerprint, results.CandidateFingerprint);
            Require(ExactContentFingerprint(expected) == ExactContentFingerprint(results), "CONTENT_MISMATCH");
        }

        internal static string ExactContentFingerprint(HydraulicResultSet results) =>
            ContentHash(RuntimeHostProtocol.Serializer().Serialize(results));

        internal static P16ResultContentAuthority CaptureCompletedProof(HostRequest request, HostResult result)
        {
            ValidateCompletedProof(request, result);
            var files = ReadInventory(result.Diagnostics);
            var snapshot = IntegritySnapshot.Capture(Path.GetDirectoryName(request.WorkingModelPath),
                Path.GetFileName(request.WorkingModelPath), files.Select(f => f.RelativePath));
            return FromCompletedProof(request, result, snapshot);
        }

        // Internal trusted-factory boundary, called after the owned P16 process exits successfully and
        // its protocol/lifecycle envelope is validated. Tests replay retained genuine observations here;
        // there is deliberately no public JSON/path/string import or writable authority constructor.
        internal static P16ResultContentAuthority FromCompletedProof(HostRequest request, HostResult result, IntegritySnapshot currentInventory)
        {
            ValidateCompletedProof(request, result);
            var recorded = ReadInventory(result.Diagnostics);
            string primary = Path.GetFileName(request.WorkingModelPath);
            Require(recorded.Any(e => e.RelativePath == primary) && recorded.Any(e => e.RelativePath == primary + ".sqlite"),
                "MODEL_IDENTITY_MISSING");
            Require(currentInventory != null && currentInventory.Entries.Count == recorded.Count, "STALE_OUTPUT_INVENTORY");
            foreach (var entry in recorded)
                Require(currentInventory.Entries.TryGetValue(entry.RelativePath, out FileIntegrityEntry actual) &&
                    actual.FileLength == entry.FileLength && actual.SHA256 == entry.SHA256, "STALE_OUTPUT_HASH");
            var expected = ModelFamilyFingerprint.Create(Path.GetFileName(request.WorkingModelPath),
                recorded.Select(e => e.ToModelFamilyFileEntry()));
            Require(currentInventory.Fingerprint.Equals(expected), "OUTPUT_FINGERPRINT_MISMATCH");
            return new P16ResultContentAuthority(request, result, currentInventory);
        }

        internal static void ValidateCompletedProof(HostRequest request, HostResult result)
        {
            Require(request != null && result != null && request.Operation == P16HeadlessResultProofHost.Operation,
                "COMPLETED_P16_PROOF_REQUIRED");
            P16HeadlessResultProofHost.ValidateRequest(request);
            Require(result.Success && result.FailureClassification == null && result.SchemaVersion == 1 &&
                result.RunId == request.RunId && result.SelectedRuntime == request.SelectedRuntime &&
                result.RuntimeCandidateCount == request.RuntimeCandidateCount && result.DetectedArchitecture == "x64" &&
                result.RuntimeBinding && result.ApplicationDescriptionCreated && result.ApplicationModelCreated &&
                result.ParentUiModelCreated && result.LifecycleInitialized && result.ModelOpen &&
                result.CurrentGraphicalProjectEstablished && result.DomainProjectEstablished && result.DomainDataSetEstablished &&
                result.VendorCleanupCompleted && result.StaCompleted && result.ResultCommitted &&
                result.RequestedExitMode == RuntimeHostProtocol.ExitMode, "INCOMPLETE_P16_LIFECYCLE");
            Require(result.SaveInvocationCount == 1 && result.SolverRunInvocationCount == 1 &&
                result.SaveAsInvocationCount == 0 && result.MutationInvocationCount == 0 &&
                result.ScenarioCreateInvocationCount == 0 && result.AlternativeCreateInvocationCount == 0,
                "P16_OPERATION_COUNTS");
            var d = result.Diagnostics;
            Require(d != null && request.Diagnostics != null, "PROVENANCE_MISSING");
            foreach (var context in P16HeadlessResultProofHost.Context())
                Require(request.Diagnostics.TryGetValue(context.Key, out string value) && value == context.Value, "P16_REQUEST_CONTEXT_MISMATCH");
            Require(ReadInt(d, "ObservedScenarioId") == ReadInt(request.Diagnostics, "P16ScenarioId") &&
                ReadInt(d, "ObservedPhysicalAlternativeId") == ReadInt(request.Diagnostics, "P16PhysicalAlternativeId") &&
                ReadInt(d, "ObservedCalculationOptionsId") == ReadInt(request.Diagnostics, "P16CalculationOptionsId") &&
                Get(d, "ObservedEngine") == Get(request.Diagnostics, "P16Engine"), "P16_CONTEXT_MISMATCH");
            foreach (string flag in new[] { "SolverBindingProvenBeforeMutation", "SolverRunInvoked", "SolverRunCompleted",
                "SolverStatusSuccess", "SaveCurrentProjectSucceeded", "FirstSessionCloseCompleted", "ReopenSucceeded",
                "ResultManagerAvailable", "HasResults", "FlowFieldSupported", "FlowFieldHasResults", "FlowValueObserved" })
                Require(Get(d, flag) == "true", "P16_FRESHNESS_OR_RESULT_MISSING:" + flag);
            Require(Guid.TryParseExact(Get(d, "CalculationEventId"), "N", out _) &&
                ReadTime(d, "SolverStartTimestamp") <= ReadTime(d, "SolverEndTimestamp") &&
                ReadTime(d, "SolverEndTimestamp") <= ReadTime(d, "SaveTimestamp"), "INVALID_CALCULATION_EVENT_OR_FRESHNESS");
            Require(Get(d, "FlowValueType") == typeof(double).FullName && ReadInt(d, "FlowElementId") ==
                ReadInt(request.Diagnostics, "P16ElementId") && ReadInt(d, "ResultTimestepIndex") >= 0 &&
                ReadDouble(d, "FlowTime") >= 0 && !string.IsNullOrWhiteSpace(Get(d, "FlowUnit")), "INVALID_FLOW_CONTENT");
            double flow = ReadDouble(d, "FlowValue");
            Require(Get(d, "FlowClassification") == WaterObjectsResultReader.ClassifyFlowValue(flow), "INVALID_FLOW_CLASSIFICATION");
            string expectedChain = string.Format(CultureInfo.InvariantCulture,
                "MODEL:{0}->SCENARIO:{1}->ALT:{2}->OPTS:{3}->ENGINE:{4}->EVENT:{5}->SOLVER:SUCCESS->SAVE:SUCCESS->REOPEN:SUCCESS->RES_MGR:TRUE->HAS_RES:TRUE->FLOW_FIELD:TRUE->FLOW:{6}->TIME:{7}->UNIT:{8}",
                request.WorkingModelPath, Get(d, "ObservedScenarioId"), Get(d, "ObservedPhysicalAlternativeId"),
                Get(d, "ObservedCalculationOptionsId"), Get(d, "ObservedEngine"), Get(d, "CalculationEventId"),
                Get(d, "FlowValue"), Get(d, "FlowTime"), Get(d, "FlowUnit"));
            Require(Get(d, "ProvenanceChain") == expectedChain, "P16_PROVENANCE_CHAIN_MISMATCH");
            Require(request.CoreFiles != null && request.CoreFiles.Length == 2 && result.BoundCoreFiles != null &&
                result.BoundCoreFiles.Length == 2 && request.CoreFiles.All(e => result.BoundCoreFiles.Count(a =>
                    a.SimpleName == e.SimpleName && a.Path == e.Path && a.SHA256 == e.SHA256) == 1), "P16_RUNTIME_BINDING_MISMATCH");
        }

        internal static P16ResultContentAuthority CaptureCompletedProductionResult(HostRequest request, HostResult result)
        {
            ValidateCompletedProductionResult(request, result);
            var files = ReadInventory(result.Diagnostics);
            var snapshot = IntegritySnapshot.Capture(Path.GetDirectoryName(request.WorkingModelPath),
                Path.GetFileName(request.WorkingModelPath), files.Select(f => f.RelativePath));
            return FromProductionResult(request, result, snapshot);
        }

        internal static P16ResultContentAuthority FromProductionResult(HostRequest request, HostResult result, IntegritySnapshot currentInventory)
        {
            ValidateCompletedProductionResult(request, result);
            var recorded = ReadInventory(result.Diagnostics);
            string primary = Path.GetFileName(request.WorkingModelPath);
            Require(recorded.Any(e => e.RelativePath == primary) && recorded.Any(e => e.RelativePath == primary + ".sqlite"),
                "MODEL_IDENTITY_MISSING");
            Require(currentInventory != null && currentInventory.Entries.Count == recorded.Count, "STALE_OUTPUT_INVENTORY");
            foreach (var entry in recorded)
                Require(currentInventory.Entries.TryGetValue(entry.RelativePath, out FileIntegrityEntry actual) &&
                    actual.FileLength == entry.FileLength && actual.SHA256 == entry.SHA256, "STALE_OUTPUT_HASH");
            var expected = ModelFamilyFingerprint.Create(Path.GetFileName(request.WorkingModelPath),
                recorded.Select(e => e.ToModelFamilyFileEntry()));
            Require(currentInventory.Fingerprint.Equals(expected), "OUTPUT_FINGERPRINT_MISMATCH");

            string setJson = Get(result.Diagnostics, "HydraulicResultSetJson");
            var resultSet = RuntimeHostProtocol.Serializer().Deserialize<HydraulicResultSet>(setJson);
            Require(resultSet != null, "RESULT_SET_DESERIALIZE_FAILED");
            Require(HydraulicResultSet.ComputeFingerprint(resultSet) == Get(result.Diagnostics, "HydraulicResultSetFingerprint"), "FINGERPRINT_MISMATCH");
            Require(resultSet.Elements != null && resultSet.Elements.Count == ReadInt(result.Diagnostics, "HydraulicResultReads"), "READ_COUNT_MISMATCH");

            var requestedConduits = request.EngineeringPlan.Endpoints.Select(e => e.ConduitId).Distinct().ToList();
            foreach (var cid in requestedConduits)
            {
                Require(resultSet.Elements.Any(elem => elem.ElementId == cid), "MISSING_REQUESTED_CONDUIT:" + cid);
            }

            int expectedScenarioId = result.Diagnostics.ContainsKey("ExpectedCreatedScenarioId")
                ? ReadInt(result.Diagnostics, "ExpectedCreatedScenarioId")
                : ReadInt(result.Diagnostics, "CreatedScenarioID");
            int expectedAlternativeId = result.Diagnostics.ContainsKey("ExpectedCreatedPhysicalAlternativeId")
                ? ReadInt(result.Diagnostics, "ExpectedCreatedPhysicalAlternativeId")
                : ReadInt(result.Diagnostics, "CreatedPhysicalAlternativeID");
            Require(resultSet.ScenarioId == expectedScenarioId, "SCENARIO_ID_MISMATCH");
            Require(resultSet.PhysicalAlternativeId == expectedAlternativeId, "ALTERNATIVE_ID_MISMATCH");

            return new P16ResultContentAuthority(request, result, currentInventory, resultSet);
        }

        internal static void ValidateCompletedProductionResult(HostRequest request, HostResult result)
        {
            Require(request != null && result != null && request.Operation == GravityWriteAndSolverHost.ProductionOperation,
                "COMPLETED_PRODUCTION_RESULT_REQUIRED");
            Require(request.EngineeringPlan != null && !request.EngineeringPlan.NoChanges && request.EngineeringPlan.Calculate,
                "PRODUCTION_PLAN_REQUIRED");
            Require(request.WriteAuthorizationScope == GravityWriteAndSolverHost.ProductionOperation &&
                Guid.TryParseExact(request.WriteAuthorizationTokenId, "N", out _) &&
                Guid.TryParseExact(request.WorkingCopyId, "N", out _), "PRODUCTION_AUTHORIZATION");
            GravityWriteAndSolverHost.ValidateProductionResult(request, result);
            Require(result.Success && result.FailureClassification == null && result.SchemaVersion == 1 &&
                result.RunId == request.RunId && result.SelectedRuntime == request.SelectedRuntime &&
                result.RuntimeCandidateCount == request.RuntimeCandidateCount && result.DetectedArchitecture == "x64" &&
                result.RuntimeBinding && result.LifecycleInitialized && result.ModelOpen &&
                result.VendorCleanupCompleted && result.StaCompleted && result.ResultCommitted &&
                result.RequestedExitMode == RuntimeHostProtocol.ExitMode, "INCOMPLETE_PRODUCTION_LIFECYCLE");

            var d = result.Diagnostics;
            Require(d != null && request.Diagnostics != null, "PROVENANCE_MISSING");
            Require(Get(d, "SolverStatusSuccess") == "true", "SOLVER_STATUS_NOT_SUCCESS");
            Require(Get(d, "ProductionReadbackVerified") == "true", "READBACK_NOT_VERIFIED");
            Require(Get(d, "SecondSessionCloseCompleted") == "true", "SECOND_SESSION_NOT_CLOSED");
            Require(ReadInt(d, "HydraulicResultReads") > 0, "NO_HYDRAULIC_RESULTS_READ");
            Require(!string.IsNullOrWhiteSpace(Get(d, "HydraulicResultSetFingerprint")), "FINGERPRINT_MISSING");
            Require(!string.IsNullOrWhiteSpace(Get(d, "HydraulicResultSetJson")), "RESULT_JSON_MISSING");
            Require(!string.IsNullOrWhiteSpace(Get(d, "ResultFilesInventoryJson")), "INVENTORY_JSON_MISSING");

            Require(request.CoreFiles != null && request.CoreFiles.Length == 2 && result.BoundCoreFiles != null &&
                result.BoundCoreFiles.Length == 2 && request.CoreFiles.All(e => result.BoundCoreFiles.Count(a =>
                    a.SimpleName == e.SimpleName && a.Path == e.Path && a.SHA256 == e.SHA256) == 1), "PRODUCTION_RUNTIME_BINDING_MISMATCH");
        }

        internal static List<FileIntegrityEntry> ReadInventory(Dictionary<string, string> diagnostics)
        {
            var entries = new List<FileIntegrityEntry>();
            foreach (string row in RuntimeHostProtocol.Serializer().Deserialize<string[]>(Get(diagnostics, "ResultFilesInventoryJson")))
            {
                string[] parts = row.Split('|');
                Require(parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[0]) && Path.GetFileName(parts[0]) == parts[0] &&
                    parts[0].IndexOfAny(new[] { '/', '\\', ':' }) < 0 && parts[0] != "." && parts[0] != ".." &&
                    long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long length) && length > 0 &&
                    parts[2].Length == 64 && parts[2].All(Uri.IsHexDigit) &&
                    !entries.Any(e => string.Equals(e.RelativePath, parts[0], StringComparison.OrdinalIgnoreCase)), "INVALID_OUTPUT_INVENTORY");
                entries.Add(new FileIntegrityEntry(parts[0], long.Parse(parts[1], CultureInfo.InvariantCulture), parts[2]));
            }
            Require(entries.Count >= 3, "OUTPUT_INVENTORY_MISSING");
            return entries;
        }

        private static string Get(Dictionary<string, string> d, string key)
        {
            Require(d != null && d.TryGetValue(key, out _) && !string.IsNullOrWhiteSpace(d[key]), "PROVENANCE_FIELD_MISSING:" + key);
            return d[key];
        }
        private static int ReadInt(Dictionary<string, string> d, string key) => int.Parse(Get(d, key), CultureInfo.InvariantCulture);
        private static double ReadDouble(Dictionary<string, string> d, string key)
        {
            double value = double.Parse(Get(d, key), CultureInfo.InvariantCulture);
            Require(!double.IsNaN(value) && !double.IsInfinity(value), "INVALID_NUMERIC_RESULT");
            return value;
        }
        private static DateTimeOffset ReadTime(Dictionary<string, string> d, string key) =>
            DateTimeOffset.ParseExact(Get(d, key), "o", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        private static string ContentHash(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        private static void Require(bool condition, string code)
        {
            if (!condition) throw new InvalidDataException("P16_CONTENT_AUTHORITY_REJECTED:" + code);
        }
    }
}
