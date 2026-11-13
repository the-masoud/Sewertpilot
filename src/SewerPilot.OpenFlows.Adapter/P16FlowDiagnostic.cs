using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    // Internal TEST_ONLY availability proof. Never composes result trust or engineering execution.
    internal sealed class P16FlowObservation
    {
        public P16FlowObservation() { }
        public string Status { get; set; } = "RESULT_CONTRACT_UNAVAILABLE";
        public string OpenedModelPath { get; set; }
        public int? DataSetId { get; set; }
        public string DataSetGuid { get; set; }
        public int? ScenarioId { get; set; }
        public int? ActiveScenarioBefore { get; set; }
        public int? ActiveScenarioAfter { get; set; }
        public int? CurrentPhysicalAlternative { get; set; }
        public int? CurrentCalculationOptions { get; set; }
        public string Engine { get; set; }
        public bool ResultManagerAvailable { get; set; }
        public bool? ResultManagerWasActive { get; set; }
        public bool? HasResults { get; set; }
        public bool? FlowFieldSupported { get; set; }
        public int SupportedFieldCount { get; set; }
        public string[] SupportedFieldNames { get; set; } = new string[0];
        public string FieldRuntimeType { get; set; }
        public bool? FieldHasResults { get; set; }
        public int? ElementId { get; set; }
        public int? TimeStepIndex { get; set; }
        public double? TimeSeconds { get; set; }
        public int TimeStepCount { get; set; }
        public double[] AvailableTimeSeconds { get; set; } = new double[0];
        public string FieldName { get; set; }
        public string RecordName { get; set; }
        public string WorkingUnitSymbol { get; set; }
        public string WorkingUnitIdentity { get; set; }
        public string StorageUnitIdentity { get; set; }
        public string UnitEvidence { get; set; }
        public string ValueClrType { get; set; }
        public string RawValue { get; set; }
        public string RawValueClassification { get; set; }
        public int FlowReadInvocations { get; set; }
        public bool FreshnessVerified { get; set; }
        public bool ResultCleanupCompleted { get; set; }
        public string FailureOperation { get; set; }
        public string ErrorType { get; set; }
        public int? ErrorHResult { get; set; }
        public string ErrorMessage { get; set; }
        public string CleanupError { get; set; }
    }

    internal static class P16FlowDiagnostic
    {
        internal const string Operation = "InternalP16FlowAvailability";
        internal const string LaunchSwitch = "--internal-p16-flow";
        internal const string EvidenceKey = "P16FlowObservationJson";
        internal const string SourceDigest = "C0F8CECD3E1DA6350C7B180C16256E36D1814B2F73462FF49271F9B26424A67F";
        private const string Engine = "CivilStormDynamicEngine";
        private const string NameScenarioManager = "Scenario" + "Manager";
        private const int MaxFields = 1024, MaxTimeSteps = 10000;
        private static readonly string[] Family = { "Lesson_1.stsw", "Lesson_1.stsw.01.bak", "Lesson_1.stsw.dwh",
            "Lesson_1.stsw.dwh.01.bak", "Lesson_1.stsw.profiles.dwh", "Lesson_1.stsw.sqlite", "Lesson_1.stsw.sqlite.01.bak" };

        internal static void ValidateRequest(HostRequest request)
        {
            var expected = new Dictionary<string, string> {
                { "P16ScenarioId", "81" }, { "P16HistoricalPhysicalAlternativeId", "82" },
                { "P16HistoricalCalculationOptionsId", "67" }, { "P16Engine", Engine },
                { "P16SourceInventorySHA256", SourceDigest }, { "P16CopyInventorySHA256", SourceDigest },
                { "P16CopyId", Path.GetFileName(Path.GetDirectoryName(request.WorkingModelPath)) } };
            if (request.Operation != Operation || request.Diagnostics == null || request.Diagnostics.Count != expected.Count ||
                expected.Any(pair => !request.Diagnostics.ContainsKey(pair.Key) || request.Diagnostics[pair.Key] != pair.Value) ||
                request.EngineeringPlan != null || request.WriteAuthorizationTokenId != null || request.WriteAuthorizationScope != null ||
                request.WriteAuthorizationTargetWorkingPath != null || request.WorkingCopyId != expected["P16CopyId"] ||
                Path.GetFileName(request.WorkingModelPath) != "Lesson_1.stsw")
                throw new InvalidDataException("P16 diagnostic requires its exact TEST_ONLY context and no write authority.");
            RuntimeHostProtocol.ControlledDirectory(Path.GetDirectoryName(request.WorkingModelPath), "ReadOnly");
            if (InventoryDigest(Path.GetDirectoryName(request.WorkingModelPath)) != SourceDigest)
                throw new InvalidDataException("P16 diagnostic copied seven-file inventory does not match its pinned source.");
        }

        internal static string InventoryDigest(string directory)
        {
            WorkingCopyIntegrity.RejectLinks(directory);
            var paths = Directory.GetFiles(directory);
            if (paths.Length != Family.Length || !paths.Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(Family))
                throw new InvalidDataException("P16 diagnostic requires exactly the seven recorded files; no backup substitution.");
            var text = new StringBuilder();
            foreach (string name in Family)
            {
                string path = Path.Combine(directory, name);
                WorkingCopyIntegrity.RejectLinks(path);
                var info = new FileInfo(path);
                if (info.Length > 128L * 1024 * 1024) throw new InvalidDataException("P16 input exceeds its bound.");
                text.Append(name).Append('|').Append(info.Length.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(WorkingCopyIntegrity.Fingerprint(path).SHA256).Append('\n');
            }
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(new UTF8Encoding(false).GetBytes(text.ToString()))).Replace("-", "");
        }

        internal static void ValidateResult(HostResult result)
        {
            string json;
            if (!result.Diagnostics.TryGetValue(EvidenceKey, out json) || json.Length > 256 * 1024)
                throw new InvalidDataException("P16 diagnostic observation missing or oversized.");
            var observation = RuntimeHostProtocol.Serializer().Deserialize<P16FlowObservation>(json);
            if (observation == null || observation.FreshnessVerified || !observation.ResultCleanupCompleted ||
                observation.FlowReadInvocations < 0 || observation.FlowReadInvocations > 1 ||
                (observation.Status != "RESULTS_MISSING" && observation.Status != "RESULT_CONTRACT_UNAVAILABLE" &&
                 observation.Status != "VALUE_OBSERVED_UNTRUSTED") ||
                (observation.Status == "VALUE_OBSERVED_UNTRUSTED" && (observation.FlowReadInvocations != 1 ||
                 observation.HasResults != true || observation.FieldHasResults != true || observation.FlowFieldSupported != true ||
                 !observation.ElementId.HasValue || !observation.TimeStepIndex.HasValue || !observation.TimeSeconds.HasValue)))
                throw new InvalidDataException("P16 diagnostic observation violates untrusted/one-value/cleanup bounds.");
        }

        private static Type Contract(string assembly, string name)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.GetName().Name == assembly).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Exact loaded diagnostic contract assembly unavailable: " + assembly);
            return matches[0].GetType(name, true, false);
        }

        private sealed class Calls
        {
            internal string Last;
            internal object Invoke(object target, Type contract, string name, Type returns, Type[] parameters, params object[] values)
            {
                Last = contract.FullName + "." + name;
                if (target == null || !contract.IsInstanceOfType(target)) throw new InvalidOperationException("Diagnostic contract instance unavailable: " + Last);
                var method = contract.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, parameters, null);
                if (method == null || method.ReturnType != returns || method.ContainsGenericParameters)
                    throw new MissingMethodException("Exact diagnostic signature unavailable: " + Last);
                return RuntimeStageJournal.Call("P16:" + Last, () => method.Invoke(target, values),
                    value => value is bool || value is int ? value : value?.GetType().FullName);
            }
            internal object Get(object target, Type contract, string name, Type returns) =>
                Invoke(target, contract, "get_" + name, returns, Type.EmptyTypes);
        }

        internal static void Run(object dataset, object project, HostRequest request, ModelReadEvidence evidence, bool calculatedPackage = false)
        {
            var seen = new P16FlowObservation { OpenedModelPath = request.WorkingModelPath,
                ElementId = evidence.FirstConduitElementID, ResultCleanupCompleted = true };
            var calls = new Calls();
            object manager = null, scenarioManager = null;
            Type resultManagerType = null, scenarioManagerType = null;
            bool resultOpenAttempted = false;
            try
            {
                var dataType = Contract("Haestad.Domain", "Haestad.Domain.IDomainDataSet");
                var scenarioType = Contract("Haestad.Domain", "Haestad.Domain.IScenario");
                scenarioManagerType = Contract("Haestad.Domain", "Haestad.Domain.IScenarioManager");
                var elementsType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElementManager");
                var elementType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElement");
                var domainManagerType = Contract("Haestad.Domain", "Haestad.Domain.IDomainElementManager");
                var fieldType = Contract("Haestad.Support", "Haestad.Support.Support.IField");
                resultManagerType = Contract("Haestad.Domain", "Haestad.Domain.IResultManager");
                seen.DataSetId = (int)calls.Get(dataset, dataType, "Id", typeof(int));
                seen.DataSetGuid = ((Guid)calls.Get(dataset, dataType, "Guid", typeof(Guid))).ToString("D");
                scenarioManager = calls.Get(dataset, dataType, NameScenarioManager, scenarioManagerType);
                seen.ActiveScenarioBefore = (int)calls.Get(scenarioManager, scenarioManagerType, "ActiveScenarioID", typeof(int));
                if (calculatedPackage && (seen.DataSetId != 1 || seen.DataSetGuid != P16CalculatedFlowDiagnostic.DataSetGuid || seen.ActiveScenarioBefore != 81))
                    throw new InvalidOperationException("Calculated package dataset/active-scenario identity mismatch; no context is changed.");
                object scenario = calls.Invoke(scenarioManager, elementsType, "Element", elementType, new[] { typeof(int) }, 81);
                seen.ScenarioId = ScenarioAlternativeCapabilityMapper.GetElementId(scenario);
                if (seen.ScenarioId != 81 || !scenarioType.IsInstanceOfType(scenario)) throw new InvalidOperationException("Requested scenario 81 not identified.");
                seen.CurrentPhysicalAlternative = (int)calls.Invoke(scenario, scenarioType, "AlternativeID", typeof(int), new[] { typeof(int) }, 4);
                seen.CurrentCalculationOptions = (int)calls.Invoke(scenario, scenarioType, "CalculationOptionsID", typeof(int), new[] { typeof(string) }, Engine);
                if (seen.CurrentPhysicalAlternative != 82 || seen.CurrentCalculationOptions != 67)
                    throw new InvalidOperationException("Current context differs from historical 82/67; no assignment is changed.");
                manager = calls.Invoke(scenario, scenarioType, "ResultManager", resultManagerType, new[] { typeof(string) }, Engine);
                seen.ResultManagerAvailable = manager != null;
                if (manager == null) throw new InvalidOperationException("Requested result manager unavailable.");
                seen.Engine = (string)calls.Get(manager, resultManagerType, "NumericalEngineTypeName", typeof(string));
                object resultScenario = calls.Get(manager, resultManagerType, "Scenario", scenarioType);
                if (seen.Engine != Engine || ScenarioAlternativeCapabilityMapper.GetElementId(resultScenario) != 81)
                    throw new InvalidOperationException("Result-manager engine/scenario context mismatch.");
                seen.ResultManagerWasActive = (bool)calls.Get(manager, resultManagerType, "IsActive", typeof(bool));
                if (seen.ResultManagerWasActive != true)
                {
                    resultOpenAttempted = true;
                    seen.ResultCleanupCompleted = false;
                    calls.Invoke(manager, resultManagerType, "Open", typeof(void), Type.EmptyTypes);
                }
                seen.HasResults = (bool)calls.Get(manager, resultManagerType, "HasResults", typeof(bool));
                if (seen.HasResults != true) { seen.Status = "RESULTS_MISSING"; return; }

                int conduitTypeId = int.Parse(evidence.Diagnostics["ElementTypeValueDiagnosticOnly"], CultureInfo.InvariantCulture);
                object conduitManager = calls.Invoke(dataset, dataType, "DomainElementManager", domainManagerType, new[] { typeof(int) }, conduitTypeId);
                if (calculatedPackage)
                {
                    if (!(bool)calls.Invoke(conduitManager, elementsType, "Exists", typeof(bool), new[] { typeof(int) }, 27))
                        throw new InvalidOperationException("Requested conduit 27 is not present in the actual conduit manager.");
                    seen.ElementId = 27;
                }
                var collectionType = Contract("Haestad.Support", "Haestad.Support.Support.FieldCollection");
                object supported = calls.Invoke(conduitManager, domainManagerType, "SupportedResultFields", collectionType, new[] { typeof(string) }, Engine);
                var enumerable = supported as IEnumerable;
                if (enumerable == null) throw new InvalidOperationException("Supported field collection is not enumerable.");
                var names = new List<string>();
                foreach (object field in enumerable)
                {
                    if (names.Count >= MaxFields) throw new InvalidOperationException("Supported field enumeration exceeds its bound.");
                    string name = (string)calls.Get(field, fieldType, "Name", typeof(string));
                    if (name == null || name.Length > 256) throw new InvalidOperationException("Supported field name invalid.");
                    names.Add(name);
                }
                seen.SupportedFieldNames = names.ToArray(); seen.SupportedFieldCount = names.Count;
                seen.FlowFieldSupported = names.Count(name => name == "BasicFlowResults_Flow") == 1;
                if (seen.FlowFieldSupported != true)
                {
                    if (calculatedPackage) { seen.Status = "RESULT_FIELD_UNSUPPORTED"; return; }
                    throw new InvalidOperationException("Exact BasicFlowResults_Flow support absent or ambiguous.");
                }
                object flowField = calls.Invoke(conduitManager, domainManagerType, "ResultField", fieldType,
                    new[] { typeof(string), typeof(string), typeof(string) }, "BasicFlowResults_Flow", Engine, "BasicFlowResults");
                seen.FieldRuntimeType = flowField?.GetType().FullName;
                if (calculatedPackage)
                {
                    seen.FieldName = (string)calls.Get(flowField, fieldType, "Name", typeof(string));
                    seen.RecordName = "BasicFlowResults"; // Explicit record argument bound above; never substituted.
                    if (seen.FieldName != P16CalculatedFlowDiagnostic.FlowField) throw new InvalidOperationException("Returned field identity mismatch.");
                }
                var resultFieldType = Contract("Haestad.Domain", "Haestad.Domain.IResultField");
                var variantType = Contract("Haestad.Domain", "Haestad.Domain.IResultTimeVariantField");
                if (!variantType.IsInstanceOfType(flowField)) throw new InvalidOperationException("Flow field lacks the exact time-variant contract.");
                seen.FieldHasResults = (bool)calls.Invoke(flowField, resultFieldType, "HasResults", typeof(bool), new[] { typeof(int) }, 81);
                if (seen.FieldHasResults != true) { seen.Status = "RESULTS_MISSING"; return; }
                var timeUnitType = Contract("Haestad.Support", "Haestad.Support.Units.TimeUnit");
                var times = (double[])calls.Invoke(manager, resultManagerType, "TimeSteps", typeof(double[]), new[] { timeUnitType }, Enum.Parse(timeUnitType, "Seconds", false));
                seen.TimeStepCount = times == null ? 0 : times.Length;
                if (times == null || times.Length == 0) { seen.Status = "RESULTS_MISSING"; return; }
                if (times.Length > MaxTimeSteps || times.Any(time => double.IsNaN(time) || double.IsInfinity(time)))
                    throw new InvalidOperationException("Available timestep vector invalid or exceeds bound.");
                seen.TimeStepIndex = 0; seen.TimeSeconds = times[0]; // First actual reported time, not assumed time zero.
                if (calculatedPackage)
                {
                    seen.AvailableTimeSeconds = times;
                    seen.TimeStepIndex = P16CalculatedFlowDiagnostic.SelectTime(times);
                    seen.TimeSeconds = times[seen.TimeStepIndex.Value];
                }
                var unitized = Contract("Haestad.Domain", "Haestad.Domain.IUnitizedField");
                var unitIndex = Contract("Haestad.Support", "Haestad.Support.Units.UnitConversionManager+UnitIndex");
                if (unitized.IsInstanceOfType(flowField))
                {
                    object working = calls.Get(flowField, unitized, "WorkingUnitIndex", unitIndex);
                    object storage = calls.Get(flowField, unitized, "StorageUnitIndex", unitIndex);
                    seen.WorkingUnitIdentity = unitIndex.FullName + ":" + Enum.GetName(unitIndex, working) + ":" + Convert.ToInt64(working, CultureInfo.InvariantCulture);
                    seen.StorageUnitIdentity = unitIndex.FullName + ":" + Enum.GetName(unitIndex, storage) + ":" + Convert.ToInt64(storage, CultureInfo.InvariantCulture);
                    seen.UnitEvidence = "Native result-field indices only; no conversion or sign/sentinel semantics inferred.";
                }
                else seen.UnitEvidence = "IUnitizedField not exposed; unit identity unverified.";
                if (calculatedPackage)
                {
                    seen.WorkingUnitSymbol = P16CalculatedFlowDiagnostic.FlowUnit(seen.WorkingUnitIdentity);
                    if (seen.WorkingUnitSymbol == null) { seen.Status = "RESULT_UNIT_UNVERIFIED"; return; }
                    seen.UnitEvidence = "Actual IUnitizedField working-unit enum, matched to exact installed UnitIndex literal; GetValue remains native, no conversion. Sentinel semantics and freshness are not promoted.";
                }
                if (!seen.ElementId.HasValue || seen.ElementId.Value <= 0) throw new InvalidOperationException("No actual conduit ID established.");
                seen.FlowReadInvocations = 1;
                object raw = calls.Invoke(flowField, variantType, "GetValue", typeof(object),
                    new[] { typeof(int), typeof(int), typeof(int) }, seen.ElementId.Value, 81, seen.TimeStepIndex.Value);
                seen.ValueClrType = raw?.GetType().FullName ?? "null";
                if (raw is double)
                {
                    double value = (double)raw;
                    seen.RawValue = value.ToString("R", CultureInfo.InvariantCulture);
                    seen.RawValueClassification = double.IsNaN(value) ? "DOUBLE_NAN" : double.IsInfinity(value) ? "DOUBLE_INFINITY" : "FINITE_DOUBLE_SENTINEL_SEMANTICS_UNVERIFIED";
                    if (!double.IsNaN(value) && !double.IsInfinity(value)) seen.Status = "VALUE_OBSERVED_UNTRUSTED";
                }
                else if (raw is float)
                {
                    float value = (float)raw;
                    seen.RawValue = value.ToString("R", CultureInfo.InvariantCulture);
                    seen.RawValueClassification = float.IsNaN(value) ? "SINGLE_NAN" : float.IsInfinity(value) ? "SINGLE_INFINITY" : "FINITE_SINGLE_SEMANTICS_UNVERIFIED";
                    if (!float.IsNaN(value) && !float.IsInfinity(value)) seen.Status = "VALUE_OBSERVED_UNTRUSTED";
                }
                else if (raw is decimal || raw is int || raw is long || raw is short || raw is uint || raw is ulong || raw is ushort || raw is byte || raw is sbyte)
                {
                    seen.RawValue = ((IFormattable)raw).ToString(null, CultureInfo.InvariantCulture);
                    seen.RawValueClassification = "NUMERIC_CLR_TYPE_SEMANTICS_UNVERIFIED";
                    seen.Status = "VALUE_OBSERVED_UNTRUSTED";
                }
                else seen.RawValueClassification = raw == null ? "NULL" : "NON_NUMERIC_RETURN_NO_CONVERSION";
                if (calculatedPackage && seen.Status != "VALUE_OBSERVED_UNTRUSTED") seen.Status = "RESULT_VALUE_UNAVAILABLE";
            }
            catch (Exception error)
            {
                var exact = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                seen.Status = exact is FileNotFoundException || exact is DirectoryNotFoundException ? "RESULTS_MISSING" : "RESULT_CONTRACT_UNAVAILABLE";
                seen.FailureOperation = calls.Last; seen.ErrorType = exact.GetType().FullName; seen.ErrorHResult = exact.HResult;
                seen.ErrorMessage = exact.Message.Length <= 1024 ? exact.Message : exact.Message.Substring(0, 1024);
            }
            finally
            {
                try
                {
                    if (manager != null && (resultOpenAttempted || seen.ResultManagerWasActive == true))
                        calls.Invoke(manager, resultManagerType, "Close", typeof(void), Type.EmptyTypes);
                    seen.ResultCleanupCompleted = true;
                }
                catch (Exception error) { seen.ResultCleanupCompleted = false; seen.CleanupError = error.ToString(); seen.Status = "RESULT_CONTRACT_UNAVAILABLE"; }
                try
                {
                    if (scenarioManager != null && scenarioManagerType != null && seen.ActiveScenarioBefore.HasValue)
                    {
                        seen.ActiveScenarioAfter = (int)calls.Get(scenarioManager, scenarioManagerType, "ActiveScenarioID", typeof(int));
                        if (seen.ActiveScenarioAfter != seen.ActiveScenarioBefore) throw new InvalidOperationException("Active scenario changed during read-only diagnostic.");
                    }
                }
                catch (Exception error) { seen.CleanupError = (seen.CleanupError ?? "") + error; seen.Status = "RESULT_CONTRACT_UNAVAILABLE"; }
                evidence.Diagnostics[EvidenceKey] = RuntimeHostProtocol.Serializer().Serialize(seen);
            }
        }
    }
}
