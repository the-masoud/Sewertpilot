using System;
using System.Globalization;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static HostRequest CalculatedRequest() => new HostRequest {
            SchemaVersion = 1, RunId = Guid.NewGuid().ToString("N"), Operation = P16CalculatedFlowDiagnostic.Operation,
            WorkingModelPath = P16CalculatedFlowDiagnostic.ModelPath, WorkingCopyId = P16CalculatedFlowDiagnostic.CopyId,
            RuntimeCandidateCount = 1, DetectedArchitecture = "x64", SelectedRuntime = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'),
            CoreFiles = SewerPilot.OpenFlows.Compatibility.OpenFlowsRuntimeCandidate.CoreAssemblyNames.Select(n => new HostMetadata {
                SimpleName = n, Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, n + ".dll"), SHA256 = new string('A', 64) }).ToArray(),
            Diagnostics = P16CalculatedFlowDiagnostic.Context() };

        private static void P16Calculated_RequestManifestAndAuthority()
        {
            var request = CalculatedRequest();
            RuntimeHostProtocol.ValidateRequest(request, false, null, true);
            Check(true, "Exact pinned twelve-file TEST_ONLY request accepted without vendor execution.");
            Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(request));
            foreach (string key in new[] { "P16ScenarioId", "P16PhysicalAlternativeId", "P16CalculationOptionsId", "P16Engine", "P16ElementId", "P16TimeSeconds", "P16ManifestSHA256", "P16Classification" })
            {
                var bad = CalculatedRequest(); bad.Diagnostics[key] = "FORBIDDEN";
                Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.ValidateRequest(bad));
            }
            foreach (Action<HostRequest> corrupt in new Action<HostRequest>[] {
                r => r.WriteAuthorizationTokenId = "forbidden", r => r.WriteAuthorizationTokenId = "",
                r => r.WriteAuthorizationScope = "Solver", r => r.WriteAuthorizationTargetWorkingPath = r.WorkingModelPath,
                r => r.SourceBaselineFingerprint = new string('A', 64), r => r.WorkingCopyId = Guid.NewGuid().ToString("N"),
                r => r.WorkingModelPath = r.WorkingModelPath + ".bak", r => r.Operation = RuntimeHostProtocol.OpGravityWriteAndSolver,
                r => r.Diagnostics.Add("IsTestOnly", "false") })
            {
                var bad = CalculatedRequest(); corrupt(bad);
                Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.ValidateRequest(bad));
            }
            var source = CalculatedRequest(); source.WorkingModelPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(source.WorkingModelPath)), "ec8c3d18e0c4429b8ce2973019786ca8", "Lesson_1.stsw");
            Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.ValidateRequest(source));
            Check(P16FlowDiagnostic.SourceDigest == "C0F8CECD3E1DA6350C7B180C16256E36D1814B2F73462FF49271F9B26424A67F", "Historical digest preserved.");
            Throws<InvalidDataException>(() => P16FlowDiagnostic.InventoryDigest(Path.GetDirectoryName(request.WorkingModelPath)));
            var names = Directory.GetFiles(Path.GetDirectoryName(request.WorkingModelPath)).Select(Path.GetFileName).ToArray();
            P16CalculatedFlowDiagnostic.ValidateInventoryEntries(names, P16CalculatedFlowDiagnostic.ManifestSHA256);
            Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.ValidateInventoryEntries(names, new string('0', 64)));
            Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.ValidateInventoryEntries(names.Concat(new[] { "unexpected.out" }), P16CalculatedFlowDiagnostic.ManifestSHA256));
            Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.ValidateInventoryEntries(names.Take(names.Length - 1), P16CalculatedFlowDiagnostic.ManifestSHA256));
            Check(!typeof(P16CalculatedFlowDiagnostic).IsPublic, "Calculated proof is not an exported product capability.");
        }

        private static P16FlowObservation CalculatedObservation() => new P16FlowObservation {
            Status = "VALUE_OBSERVED_UNTRUSTED", OpenedModelPath = P16CalculatedFlowDiagnostic.ModelPath,
            DataSetId = 1, DataSetGuid = P16CalculatedFlowDiagnostic.DataSetGuid, ScenarioId = 81,
            ActiveScenarioBefore = 81, ActiveScenarioAfter = 81, CurrentPhysicalAlternative = 82, CurrentCalculationOptions = 67,
            Engine = P16CalculatedFlowDiagnostic.Engine, ResultManagerAvailable = true, HasResults = true, FlowFieldSupported = true,
            SupportedFieldCount = 1, SupportedFieldNames = new[] { P16CalculatedFlowDiagnostic.FlowField }, FieldHasResults = true,
            FieldName = P16CalculatedFlowDiagnostic.FlowField, RecordName = P16CalculatedFlowDiagnostic.FlowRecord,
            ElementId = 27, TimeStepIndex = 0, TimeSeconds = 0, TimeStepCount = 3, AvailableTimeSeconds = new[] { 0d, 180d, 390d },
            WorkingUnitIdentity = "Haestad.Support.Units.UnitConversionManager+UnitIndex:CubicFeetPerSecond:38",
            StorageUnitIdentity = "Haestad.Support.Units.UnitConversionManager+UnitIndex:CubicFeetPerSecond:38", WorkingUnitSymbol = "ft3/s",
            ValueClrType = "System.Double", RawValue = "-0.125", RawValueClassification = "FINITE_DOUBLE_SENTINEL_SEMANTICS_UNVERIFIED",
            FlowReadInvocations = 1, ResultCleanupCompleted = true, FreshnessVerified = false };

        private static void ValidateCalculatedObservation(P16FlowObservation seen) =>
            P16CalculatedFlowDiagnostic.ValidateObservation(RuntimeHostProtocol.Serializer().Serialize(seen));

        private static void P16Calculated_ResultContextTimeAndUnits()
        {
            ValidateCalculatedObservation(CalculatedObservation());
            Check(true, "Bounded signed native value retained as untrusted, never engineering acceptance.");
            foreach (Action<P16FlowObservation> corrupt in new Action<P16FlowObservation>[] {
                r => r.ScenarioId = 1, r => r.CurrentPhysicalAlternative = 5, r => r.CurrentCalculationOptions = 14,
                r => r.Engine = "GVFPressureEngine", r => r.ElementId = 999, r => r.DataSetGuid = Guid.Empty.ToString(),
                r => r.ActiveScenarioAfter = 1, r => r.FieldName = "BasicVelocityResults_Velocity", r => r.RecordName = "Other",
                r => r.FlowFieldSupported = false, r => r.HasResults = false, r => r.FieldHasResults = false,
                r => r.TimeStepIndex = 2, r => r.TimeSeconds = 180, r => r.AvailableTimeSeconds = new[] { 0d, 0d, 390d },
                r => r.AvailableTimeSeconds = new[] { 0d, double.NaN, 390d }, r => r.TimeStepCount = 99,
                r => r.WorkingUnitSymbol = null, r => r.WorkingUnitSymbol = "m3/s", r => r.WorkingUnitIdentity = "unknown",
                r => r.StorageUnitIdentity = null, r => r.ValueClrType = "System.String", r => r.FlowReadInvocations = 2,
                r => r.FreshnessVerified = true, r => r.ResultCleanupCompleted = false, r => r.CleanupError = "failure" })
            {
                var bad = CalculatedObservation(); corrupt(bad);
                Throws<InvalidDataException>(() => ValidateCalculatedObservation(bad));
            }
            foreach (string raw in new[] { "NaN", "Infinity", "-Infinity", "not numeric", null })
            {
                var bad = CalculatedObservation(); bad.RawValue = raw;
                Throws<InvalidDataException>(() => ValidateCalculatedObservation(bad));
            }
            Check(P16CalculatedFlowDiagnostic.SelectTime(new[] { 0d, 180d, 390d }) == 0, "Actual irregular time vector, no assumed interval.");
            Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.SelectTime(new[] { 180d, 390d }));
            Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.SelectTime(new double[10001]));
            Check(P16CalculatedFlowDiagnostic.FlowUnit("Haestad.Support.Units.UnitConversionManager+UnitIndex:Feet:63") == null, "Length unit cannot establish Flow.");
        }

        private static void P16Calculated_UnavailablePayloadAndTrust()
        {
            foreach (string status in new[] { "RESULTS_MISSING", "RESULT_FIELD_UNSUPPORTED", "RESULT_UNIT_UNVERIFIED", "RESULT_VALUE_UNAVAILABLE", "RESULT_CONTRACT_UNAVAILABLE" })
            {
                var seen = new P16FlowObservation { Status = status, OpenedModelPath = P16CalculatedFlowDiagnostic.ModelPath,
                    ResultCleanupCompleted = true, RawValueClassification = "NULL" };
                ValidateCalculatedObservation(seen);
                Check(seen.RawValue == null && seen.FlowReadInvocations == 0, "Unavailable observations do not manufacture zero.");
            }
            var serializer = RuntimeHostProtocol.Serializer();
            string json = serializer.Serialize(CalculatedObservation());
            foreach (string bad in new[] { "{partial", "{}", new string('x', 256 * 1024 + 1), json.TrimEnd('}') + ",\"IsTestOnly\":false}", json.TrimEnd('}') + ",\"RESULTS_GENUINE\":true}" })
                Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.ValidateObservation(bad));
            string requestJson = serializer.Serialize(CalculatedRequest());
            var request = P16CalculatedFlowDiagnostic.ParseRequest(requestJson);
            RuntimeHostProtocol.ValidateRequest(request, false, null, true);
            Check(request.WriteAuthorizationTokenId == null && request.WriteAuthorizationScope == null && request.WriteAuthorizationTargetWorkingPath == null, "Actual CLR null survives JSON round trip.");
            foreach (string bad in new[] { "{partial", "{}", new string('x', 256 * 1024 + 1), requestJson.TrimEnd('}') + ",\"IsTestOnly\":false}" })
                Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.ParseRequest(bad));
            var originalMissing = new HostResult();
            originalMissing.Diagnostics[P16FlowDiagnostic.EvidenceKey] = serializer.Serialize(new P16FlowObservation { Status = "RESULTS_MISSING", ResultCleanupCompleted = true });
            P16FlowDiagnostic.ValidateResult(originalMissing);
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("Haestad.", StringComparison.Ordinal)), "Focused safety tests execute zero vendor code.");
        }
    }
}
