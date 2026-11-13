using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    // Bounded, strictly read-only result-access observation for the Azadegan sanitary sewer
    // model. It is not a trust authority: it never composes engineering acceptance, never
    // recalculates, never saves and never accepts a substitute engine, element or timestep.
    internal sealed class P16AzadeganObservation
    {
        public P16AzadeganObservation() { }
        public string Status { get; set; } = "RESULT_CONTRACT_UNAVAILABLE";
        public string OpenedModelPath { get; set; }
        public int? DataSetId { get; set; }
        public string DataSetGuid { get; set; }
        public int? ActiveScenarioBefore { get; set; }
        public int? ActiveScenarioAfter { get; set; }
        public int? RequestedScenarioId { get; set; }
        public int? CurrentPhysicalAlternative { get; set; }
        public int? CurrentCalculationOptions { get; set; }
        public string Engine { get; set; }
        public string ResultManagerEngineName { get; set; }
        public int? ResultManagerScenarioId { get; set; }
        public bool ResultManagerAvailable { get; set; }
        public bool? ResultManagerWasActive { get; set; }
        public bool? HasResults { get; set; }
        public bool? FlowFieldSupported { get; set; }
        public int SupportedFieldCount { get; set; }
        public string[] SupportedFieldNames { get; set; } = new string[0];
        public string FieldRuntimeType { get; set; }
        public string FieldName { get; set; }
        public string RecordName { get; set; }
        public bool? FieldHasResults { get; set; }
        public string RequestedElementLabel { get; set; }
        public int? RequestedElementId { get; set; }
        public string ObservedElementLabel { get; set; }
        public int? ObservedElementId { get; set; }
        public int LabelMatchCount { get; set; }
        public bool ElementIdentityVerified { get; set; }
        public string LabelEvidence { get; set; }
        public int? TimeStepIndex { get; set; }
        public double? TimeSeconds { get; set; }
        public int TimeStepCount { get; set; }
        public double[] AvailableTimeSeconds { get; set; } = new double[0];
        public string WorkingUnitSymbol { get; set; }
        public string WorkingUnitIdentity { get; set; }
        public string StorageUnitIdentity { get; set; }
        public string UnitEvidence { get; set; }
        public string ValueClrType { get; set; }
        public string RawValue { get; set; }
        public string RawValueClassification { get; set; }
        public int FlowReadInvocations { get; set; }
        public bool FreshnessVerified { get; set; }
        public string FreshnessEvidence { get; set; }
        public bool ResultCleanupCompleted { get; set; }
        public string CleanupError { get; set; }
        public string FailureOperation { get; set; }
        public string ErrorType { get; set; }
        public int? ErrorHResult { get; set; }
        public string ErrorMessage { get; set; }
    }

    internal static class P16AzadeganReadOnlyResultHost
    {
        internal const string Operation = "InternalP16AzadeganReadOnlyResult";
        internal const string LaunchSwitch = "--internal-p16-azadegan-result";
        internal const string EvidenceKey = "P16AzadeganObservationJson";
        internal const string PassFlag = "P16AzadeganExtractionPassed";
        internal const string PassStatus = "PASS_READ_ONLY_EXTRACTION_MECHANICS";
        internal const string BlockedFailureCode = "P16_AZADEGAN_RESULT_ACCESS_BLOCKED";

        internal const string Engine = "DelawareProductEngine";
        internal const string FlowField = "BasicFlowResults_Flow";
        internal const string FlowRecord = "BasicFlowResults";
        internal const string ElementLabel = "SSW-001";
        internal const int ScenarioId = 353;
        internal const int PhysicalAlternativeId = 352;
        internal const int CalculationOptionsId = 29;
        internal const int ElementId = 119;
        internal const double TargetTimeSeconds = 0d;
        internal const string PhysicalAlternativeTypeId = "4";

        internal const string ModelFileName = "Azadegab ssw 26.10.25.stsw";
        private const string NameScenarioManager = "Scenario" + "Manager";
        private const int MaxFields = 1024, MaxTimeSteps = 10000, MaxConduits = 20000;

        // The complete family copied for this bounded attempt: the four model state files plus
        // the two scenario-353 companions. No other scenario variant is copied or opened.
        internal static readonly string[] Family =
        {
            ModelFileName,
            ModelFileName + ".dwh",
            ModelFileName + ".profiles.dwh",
            ModelFileName + ".sqlite",
            ModelFileName + "_1_353-swrc-gvf.out",
            ModelFileName + "_1_353.cop"
        };

        internal static string WorkingRoot => Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "ReadOnly");

        internal static Dictionary<string, string> Context(string copyId, string inventoryDigest) => new Dictionary<string, string>
        {
            { "P16AzadeganScenarioId", ScenarioId.ToString(CultureInfo.InvariantCulture) },
            { "P16AzadeganPhysicalAlternativeId", PhysicalAlternativeId.ToString(CultureInfo.InvariantCulture) },
            { "P16AzadeganCalculationOptionsId", CalculationOptionsId.ToString(CultureInfo.InvariantCulture) },
            { "P16AzadeganEngine", Engine },
            { "P16AzadeganElementLabel", ElementLabel },
            { "P16AzadeganElementId", ElementId.ToString(CultureInfo.InvariantCulture) },
            { "P16AzadeganTimeSeconds", "0" },
            { "P16AzadeganCopyId", copyId },
            { "P16AzadeganInventorySHA256", inventoryDigest }
        };

        internal static string ExpectedModelPath(string copyId) => Path.Combine(WorkingRoot, copyId, ModelFileName);

        // Labels only. Keys are exact UnitIndex enum literals verified present in the installed
        // Haestad.Support metadata; no numeric conversion and no unit inference is performed.
        internal static string FlowUnitSymbol(string enumName)
        {
            switch (enumName)
            {
                case "CubicFeetPerSecond": return "ft3/s";
                case "CubicMetersPerSecond": return "m3/s";
                case "LitersPerSecond": return "L/s";
                case "LitersPerMinute": return "L/min";
                case "CubicFeetPerMinute": return "ft3/min";
                case "CubicMetersPerMinute": return "m3/min";
                case "GallonsPerMinute": return "gal/min";
                case "CubicMetersPerHour": return "m3/h";
                case "CubicFeetPerDay": return "ft3/d";
                case "GallonsPerDay": return "gal/d";
                case "CubicMetersPerDay": return "m3/d";
                case "AcreFeetPerDay": return "acre-ft/d";
                default: return null; // Unknown literal: the unit stays unverified.
            }
        }

        internal static string InventoryDigest(string directory)
        {
            WorkingCopyIntegrity.RejectLinks(directory);
            var paths = Directory.GetFiles(directory);
            var names = paths.Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var text = new StringBuilder();
            long total = 0;
            foreach (string name in names)
            {
                string path = Path.Combine(directory, name);
                WorkingCopyIntegrity.RejectLinks(path);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > 128L * 1024 * 1024 || (total += info.Length) > 256L * 1024 * 1024)
                    throw new InvalidDataException("Azadegan working-copy file is absent or exceeds the bounded manifest size.");
                text.Append(name).Append('|').Append(info.Length.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(WorkingCopyIntegrity.Fingerprint(path).SHA256).Append('\n');
            }
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(new UTF8Encoding(false).GetBytes(text.ToString()))).Replace("-", "");
        }

        private static void VerifyInventory(string directory)
        {
            RuntimeHostProtocol.ControlledDirectory(directory, "ReadOnly");
            WorkingCopyIntegrity.RejectLinks(directory);
            var names = Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            if (!names.SequenceEqual(Family.OrderBy(n => n, StringComparer.Ordinal)))
                throw new InvalidDataException("Azadegan read-only attempt requires exactly its six recorded files.");
        }

        internal static void ValidateRequest(HostRequest request)
        {
            if (request == null || request.Operation != Operation) throw new InvalidDataException("Azadegan request operation mismatch.");
            if (request.EngineeringPlan != null || request.WriteAuthorizationTokenId != null ||
                request.WriteAuthorizationScope != null || request.WriteAuthorizationTargetWorkingPath != null)
                throw new InvalidDataException("Azadegan read-only attempt permits no mutation plan or write authority.");
            if (request.Diagnostics == null) throw new InvalidDataException("Azadegan request diagnostics are absent.");

            string copyId;
            if (!request.Diagnostics.TryGetValue("P16AzadeganCopyId", out copyId) ||
                !System.Text.RegularExpressions.Regex.IsMatch(copyId ?? "", "^[a-f0-9]{32}$") ||
                request.WorkingCopyId != copyId)
                throw new InvalidDataException("Azadegan request copy identity is not a controlled per-run identifier.");

            string modelPath = ExpectedModelPath(copyId);
            if (!string.Equals(Path.GetFullPath(request.WorkingModelPath ?? ""), Path.GetFullPath(modelPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Azadegan request must target the disposable read-only working copy only.");

            string directory = Path.GetDirectoryName(modelPath);
            VerifyInventory(directory);
            WorkingCopyIntegrity.RejectLinks(modelPath);

            string digest;
            if (!request.Diagnostics.TryGetValue("P16AzadeganInventorySHA256", out digest) ||
                !System.Text.RegularExpressions.Regex.IsMatch(digest ?? "", "^[A-F0-9]{64}$"))
                throw new InvalidDataException("Azadegan request inventory digest is malformed.");
            if (!string.Equals(InventoryDigest(directory), digest, StringComparison.Ordinal))
                throw new InvalidDataException("Azadegan working copy does not match its declared inventory digest.");

            var expected = Context(copyId, digest);
            if (request.Diagnostics.Count != expected.Count ||
                expected.Any(pair => !request.Diagnostics.ContainsKey(pair.Key) || request.Diagnostics[pair.Key] != pair.Value))
                throw new InvalidDataException("Azadegan request requires the exact bounded scenario/engine/element/time context.");
        }

        private static Type Contract(string assembly, string name)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.GetName().Name == assembly).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Exact loaded contract assembly unavailable: " + assembly);
            return matches[0].GetType(name, true, false);
        }

        private sealed class Calls
        {
            internal string Last;
            internal object Invoke(object target, Type contract, string name, Type returns, Type[] parameters, params object[] values)
            {
                Last = contract.FullName + "." + name;
                if (target == null || !contract.IsInstanceOfType(target)) throw new InvalidOperationException("Contract instance unavailable: " + Last);
                var method = contract.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, parameters, null);
                if (method == null || method.ReturnType != returns || method.ContainsGenericParameters)
                    throw new MissingMethodException("Exact signature unavailable: " + Last);
                return RuntimeStageJournal.Call("P16Az:" + Last, () => method.Invoke(target, values),
                    value => value is bool || value is int ? value : value?.GetType().FullName);
            }
            internal object Get(object target, Type contract, string name, Type returns) =>
                Invoke(target, contract, "get_" + name, returns, Type.EmptyTypes);
        }

        private static string ReadLabel(Calls calls, object element, Type elementContract, P16AzadeganObservation seen)
        {
            var declared = elementContract.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance);
            if (declared != null && declared.PropertyType == typeof(string))
            {
                seen.LabelEvidence = "Haestad.Domain.IModelingElement.Label";
                return (string)calls.Get(element, elementContract, "Label", typeof(string));
            }
            var runtime = element.GetType().GetProperty("Label", BindingFlags.Public | BindingFlags.Instance);
            if (runtime != null && runtime.PropertyType == typeof(string))
            {
                seen.LabelEvidence = "runtime type property " + element.GetType().FullName + ".Label";
                return runtime.GetValue(element, null) as string;
            }
            throw new MissingMemberException("No readable conduit label member is exposed: " + element.GetType().FullName);
        }

        internal static void ExecuteRead(object dataset, object project, HostRequest request, ModelReadEvidence evidence)
        {
            var seen = new P16AzadeganObservation
            {
                OpenedModelPath = request.WorkingModelPath,
                RequestedScenarioId = ScenarioId,
                RequestedElementLabel = ElementLabel,
                RequestedElementId = ElementId,
                Engine = Engine,
                FlowReadInvocations = 0,
                FreshnessVerified = false,
                FreshnessEvidence = "No calculation lineage, run event or save was produced or verified by this attempt; the result is pre-existing project state."
            };
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

                object scenario = calls.Invoke(scenarioManager, elementsType, "Element", elementType, new[] { typeof(int) }, ScenarioId);
                if (!scenarioType.IsInstanceOfType(scenario)) throw new InvalidOperationException("Requested scenario 353 is not an IScenario.");
                seen.CurrentPhysicalAlternative = (int)calls.Invoke(scenario, scenarioType, "AlternativeID", typeof(int), new[] { typeof(int) }, int.Parse(PhysicalAlternativeTypeId, CultureInfo.InvariantCulture));
                seen.CurrentCalculationOptions = (int)calls.Invoke(scenario, scenarioType, "CalculationOptionsID", typeof(int), new[] { typeof(string) }, Engine);
                if (seen.CurrentPhysicalAlternative != PhysicalAlternativeId || seen.CurrentCalculationOptions != CalculationOptionsId)
                {
                    seen.Status = "SCENARIO_CONTEXT_MISMATCH";
                    return; // No assignment, copy or recalculation is attempted.
                }

                manager = calls.Invoke(scenario, scenarioType, "ResultManager", resultManagerType, new[] { typeof(string) }, Engine);
                seen.ResultManagerAvailable = manager != null;
                if (manager == null) { seen.Status = "RESULT_MANAGER_UNAVAILABLE"; return; }

                seen.ResultManagerEngineName = (string)calls.Get(manager, resultManagerType, "NumericalEngineTypeName", typeof(string));
                object resultScenario = calls.Get(manager, resultManagerType, "Scenario", scenarioType);
                seen.ResultManagerScenarioId = ScenarioAlternativeCapabilityMapper.GetElementId(resultScenario);
                if (seen.ResultManagerEngineName != Engine || seen.ResultManagerScenarioId != ScenarioId)
                {
                    seen.Status = "RESULT_MANAGER_CONTEXT_MISMATCH";
                    return; // A result manager for another engine or scenario is never accepted.
                }

                seen.ResultManagerWasActive = (bool)calls.Get(manager, resultManagerType, "IsActive", typeof(bool));
                if (seen.ResultManagerWasActive != true)
                {
                    resultOpenAttempted = true;
                    calls.Invoke(manager, resultManagerType, "Open", typeof(void), Type.EmptyTypes);
                }
                seen.HasResults = (bool)calls.Get(manager, resultManagerType, "HasResults", typeof(bool));
                if (seen.HasResults != true) { seen.Status = "RESULTS_MISSING"; return; }

                int conduitTypeId = int.Parse(evidence.Diagnostics["ElementTypeValueDiagnosticOnly"], CultureInfo.InvariantCulture);
                object conduitManager = calls.Invoke(dataset, dataType, "DomainElementManager", domainManagerType, new[] { typeof(int) }, conduitTypeId);
                if (!(bool)calls.Invoke(conduitManager, elementsType, "Exists", typeof(bool), new[] { typeof(int) }, ElementId))
                {
                    seen.Status = "ELEMENT_IDENTITY_UNVERIFIED";
                    seen.FailureOperation = "Exists(" + ElementId + ")";
                    return; // Never substitute another conduit.
                }

                // Independent label-to-ID verification: the requested ID must carry the requested
                // label, and that label must be unique in the conduit manager.
                var idsMethod = WaterObjectsLifecycleHost.ElementIDs(conduitManager.GetType());
                if (idsMethod == null) throw new MissingMethodException("Conduit ElementIDs member unavailable.");
                object ids = idsMethod.Invoke(conduitManager, null);
                var indexer = WaterObjectsLifecycleHost.Indexer(ids.GetType());
                int count = (int)ids.GetType().GetProperty("Count").GetValue(ids, null);
                if (count <= 0 || count > MaxConduits) throw new InvalidOperationException("Conduit count is out of bounds: " + count);
                var matches = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    int id = (int)indexer.GetValue(ids, new object[] { i });
                    object element = calls.Invoke(conduitManager, elementsType, "Element", elementType, new[] { typeof(int) }, id);
                    string label = ReadLabel(calls, element, elementType, seen);
                    if (string.Equals(label, ElementLabel, StringComparison.Ordinal)) matches.Add(id);
                }
                seen.LabelMatchCount = matches.Count;
                if (matches.Count != 1 || matches[0] != ElementId)
                {
                    seen.Status = "ELEMENT_IDENTITY_UNVERIFIED";
                    seen.FailureOperation = "label scan for " + ElementLabel;
                    seen.ErrorMessage = "Label matches: " + string.Join(",", matches.Select(m => m.ToString(CultureInfo.InvariantCulture)));
                    return;
                }
                object requested = calls.Invoke(conduitManager, elementsType, "Element", elementType, new[] { typeof(int) }, ElementId);
                seen.ObservedElementId = (int)calls.Get(requested, elementType, "Id", typeof(int));
                seen.ObservedElementLabel = ReadLabel(calls, requested, elementType, seen);
                if (seen.ObservedElementId != ElementId || !string.Equals(seen.ObservedElementLabel, ElementLabel, StringComparison.Ordinal))
                {
                    seen.Status = "ELEMENT_IDENTITY_UNVERIFIED";
                    seen.FailureOperation = "element identity readback";
                    return;
                }
                seen.ElementIdentityVerified = true;

                var collectionType = Contract("Haestad.Support", "Haestad.Support.Support.FieldCollection");
                object supported = calls.Invoke(conduitManager, domainManagerType, "SupportedResultFields", collectionType, new[] { typeof(string) }, Engine);
                var enumerable = supported as IEnumerable;
                if (enumerable == null) throw new InvalidOperationException("Supported field collection is not enumerable.");
                var names = new List<string>();
                foreach (object field in enumerable)
                {
                    if (names.Count >= MaxFields) throw new InvalidOperationException("Supported field enumeration exceeds its bound.");
                    string name = (string)calls.Get(field, fieldType, "Name", typeof(string));
                    if (name == null || name.Length > 256) throw new InvalidOperationException("Supported field name is invalid.");
                    names.Add(name);
                }
                seen.SupportedFieldNames = names.ToArray();
                seen.SupportedFieldCount = names.Count;
                seen.FlowFieldSupported = names.Count(n => n == FlowField) == 1;
                if (seen.FlowFieldSupported != true) { seen.Status = "RESULT_FIELD_UNSUPPORTED"; return; }

                object flowField = calls.Invoke(conduitManager, domainManagerType, "ResultField", fieldType,
                    new[] { typeof(string), typeof(string), typeof(string) }, FlowField, Engine, FlowRecord);
                seen.FieldRuntimeType = flowField?.GetType().FullName;
                if (flowField == null) { seen.Status = "RESULT_FIELD_UNSUPPORTED"; return; }
                seen.FieldName = (string)calls.Get(flowField, fieldType, "Name", typeof(string));
                seen.RecordName = FlowRecord; // Explicit record argument bound above; never substituted.
                if (seen.FieldName != FlowField) throw new InvalidOperationException("Returned field identity mismatch: " + seen.FieldName);

                var resultFieldType = Contract("Haestad.Domain", "Haestad.Domain.IResultField");
                var variantType = Contract("Haestad.Domain", "Haestad.Domain.IResultTimeVariantField");
                if (!variantType.IsInstanceOfType(flowField)) throw new InvalidOperationException("Flow field lacks the exact time-variant contract.");
                seen.FieldHasResults = (bool)calls.Invoke(flowField, resultFieldType, "HasResults", typeof(bool), new[] { typeof(int) }, ScenarioId);
                if (seen.FieldHasResults != true) { seen.Status = "RESULTS_MISSING"; return; }

                var timeUnitType = Contract("Haestad.Support", "Haestad.Support.Units.TimeUnit");
                var times = (double[])calls.Invoke(manager, resultManagerType, "TimeSteps", typeof(double[]), new[] { timeUnitType }, Enum.Parse(timeUnitType, "Seconds", false));
                seen.TimeStepCount = times == null ? 0 : times.Length;
                if (times == null || times.Length == 0) { seen.Status = "RESULTS_MISSING"; return; }
                if (times.Length > MaxTimeSteps) throw new InvalidOperationException("Available timestep vector exceeds its bound.");
                seen.AvailableTimeSeconds = times;
                int index;
                try { index = P16CalculatedFlowDiagnostic.SelectTime(times); }
                catch (InvalidDataException error)
                {
                    seen.Status = "RESULT_TIME_UNAVAILABLE";
                    seen.FailureOperation = "TimeSteps";
                    seen.ErrorMessage = error.Message;
                    return; // No index is inferred; the available times are reported instead.
                }
                seen.TimeStepIndex = index;
                seen.TimeSeconds = times[index];
                if (seen.TimeSeconds != TargetTimeSeconds) { seen.Status = "RESULT_TIME_UNAVAILABLE"; return; }

                var unitized = Contract("Haestad.Domain", "Haestad.Domain.IUnitizedField");
                var unitIndex = Contract("Haestad.Support", "Haestad.Support.Units.UnitConversionManager+UnitIndex");
                if (unitized.IsInstanceOfType(flowField))
                {
                    object working = calls.Get(flowField, unitized, "WorkingUnitIndex", unitIndex);
                    object storage = calls.Get(flowField, unitized, "StorageUnitIndex", unitIndex);
                    string workingName = Enum.GetName(unitIndex, working);
                    seen.WorkingUnitIdentity = unitIndex.FullName + ":" + workingName + ":" + Convert.ToInt64(working, CultureInfo.InvariantCulture);
                    seen.StorageUnitIdentity = unitIndex.FullName + ":" + Enum.GetName(unitIndex, storage) + ":" + Convert.ToInt64(storage, CultureInfo.InvariantCulture);
                    seen.WorkingUnitSymbol = FlowUnitSymbol(workingName);
                    seen.UnitEvidence = "Native IUnitizedField working-unit enum literal only; symbol is a label for that exact literal, GetValue is unconverted.";
                }
                else
                {
                    seen.UnitEvidence = "IUnitizedField is not exposed; unit identity cannot be verified.";
                }
                if (string.IsNullOrEmpty(seen.WorkingUnitSymbol)) { seen.Status = "RESULT_UNIT_UNVERIFIED"; return; }

                seen.FlowReadInvocations = 1;
                object raw = calls.Invoke(flowField, variantType, "GetValue", typeof(object),
                    new[] { typeof(int), typeof(int), typeof(int) }, ElementId, ScenarioId, index);
                seen.ValueClrType = raw?.GetType().FullName ?? "null";
                if (raw is double)
                {
                    double value = (double)raw;
                    seen.RawValue = value.ToString("R", CultureInfo.InvariantCulture);
                    seen.RawValueClassification = double.IsNaN(value) ? "DOUBLE_NAN" : double.IsInfinity(value) ? "DOUBLE_INFINITY" : "FINITE_DOUBLE_SENTINEL_SEMANTICS_UNVERIFIED";
                    if (!double.IsNaN(value) && !double.IsInfinity(value)) seen.Status = PassStatus;
                }
                else if (raw is float)
                {
                    float value = (float)raw;
                    seen.RawValue = value.ToString("R", CultureInfo.InvariantCulture);
                    seen.RawValueClassification = float.IsNaN(value) ? "SINGLE_NAN" : float.IsInfinity(value) ? "SINGLE_INFINITY" : "FINITE_SINGLE_SENTINEL_SEMANTICS_UNVERIFIED";
                    if (!float.IsNaN(value) && !float.IsInfinity(value)) seen.Status = PassStatus;
                }
                else if (raw is decimal || raw is int || raw is long || raw is short || raw is uint || raw is ulong || raw is ushort || raw is byte || raw is sbyte)
                {
                    seen.RawValue = ((IFormattable)raw).ToString(null, CultureInfo.InvariantCulture);
                    seen.RawValueClassification = "NUMERIC_CLR_TYPE_SEMANTICS_UNVERIFIED";
                    seen.Status = PassStatus;
                }
                else
                {
                    seen.RawValueClassification = raw == null ? "NULL" : "NON_NUMERIC_RETURN_NO_CONVERSION";
                }
                if (seen.Status != PassStatus) seen.Status = "RESULT_VALUE_UNAVAILABLE";
            }
            catch (Exception error)
            {
                var exact = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                seen.Status = exact is FileNotFoundException || exact is DirectoryNotFoundException ? "RESULTS_MISSING" : "RESULT_CONTRACT_UNAVAILABLE";
                seen.FailureOperation = calls.Last;
                seen.ErrorType = exact.GetType().FullName;
                seen.ErrorHResult = exact.HResult;
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
                catch (Exception error)
                {
                    seen.ResultCleanupCompleted = false;
                    seen.CleanupError = error.ToString();
                    seen.Status = "RESULT_CONTRACT_UNAVAILABLE";
                }
                try
                {
                    if (scenarioManager != null && scenarioManagerType != null && seen.ActiveScenarioBefore.HasValue)
                    {
                        seen.ActiveScenarioAfter = (int)calls.Get(scenarioManager, scenarioManagerType, "ActiveScenarioID", typeof(int));
                        if (seen.ActiveScenarioAfter != seen.ActiveScenarioBefore) throw new InvalidOperationException("Active scenario changed during the read-only attempt.");
                    }
                }
                catch (Exception error)
                {
                    seen.CleanupError = (seen.CleanupError ?? "") + error;
                    seen.Status = "RESULT_CONTRACT_UNAVAILABLE";
                }
                evidence.Diagnostics[EvidenceKey] = RuntimeHostProtocol.Serializer().Serialize(seen);
                evidence.Diagnostics["P16AzadeganStatus"] = seen.Status;
                if (seen.Status == PassStatus && seen.ResultCleanupCompleted) evidence.Diagnostics[PassFlag] = "true";
                else if (evidence.FailureCode == null) evidence.FailureCode = BlockedFailureCode;
            }
        }

        internal static void ValidateObservation(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 512 * 1024) throw new InvalidDataException("Azadegan observation is absent or oversized.");
            P16AzadeganObservation seen;
            try
            {
                var serializer = RuntimeHostProtocol.Serializer();
                var fields = serializer.DeserializeObject(json) as Dictionary<string, object>;
                var properties = typeof(P16AzadeganObservation).GetProperties();
                if (fields == null || fields.Count != properties.Length || properties.Any(p => !fields.ContainsKey(p.Name)))
                    throw new InvalidDataException();
                seen = serializer.Deserialize<P16AzadeganObservation>(json);
            }
            catch (Exception) { throw new InvalidDataException("Malformed or noncanonical Azadegan observation."); }

            string[] allowed = { PassStatus, "RESULTS_MISSING", "RESULT_MANAGER_UNAVAILABLE", "RESULT_MANAGER_CONTEXT_MISMATCH",
                "SCENARIO_CONTEXT_MISMATCH", "ELEMENT_IDENTITY_UNVERIFIED", "RESULT_FIELD_UNSUPPORTED", "RESULT_TIME_UNAVAILABLE",
                "RESULT_UNIT_UNVERIFIED", "RESULT_VALUE_UNAVAILABLE", "RESULT_CONTRACT_UNAVAILABLE" };
            if (seen == null || !allowed.Contains(seen.Status) || seen.FreshnessVerified || !seen.ResultCleanupCompleted ||
                seen.CleanupError != null || seen.FlowReadInvocations < 0 || seen.FlowReadInvocations > 1 ||
                seen.SupportedFieldNames == null || seen.SupportedFieldNames.Length > MaxFields ||
                seen.SupportedFieldCount != seen.SupportedFieldNames.Length ||
                seen.SupportedFieldNames.Any(n => n == null || n.Length > 256) ||
                seen.AvailableTimeSeconds == null || seen.AvailableTimeSeconds.Length > MaxTimeSteps ||
                seen.TimeStepCount != seen.AvailableTimeSeconds.Length)
                throw new InvalidDataException("Azadegan observation violates bounded read-only/cleanup semantics.");

            if (seen.Status != PassStatus) return;
            if (seen.FlowReadInvocations != 1 || seen.ResultManagerAvailable != true || seen.HasResults != true ||
                seen.FieldHasResults != true || seen.FlowFieldSupported != true || !seen.ElementIdentityVerified ||
                seen.CurrentPhysicalAlternative != PhysicalAlternativeId || seen.CurrentCalculationOptions != CalculationOptionsId ||
                seen.ResultManagerEngineName != Engine || seen.ResultManagerScenarioId != ScenarioId ||
                seen.ActiveScenarioBefore != seen.ActiveScenarioAfter || seen.Engine != Engine ||
                seen.RequestedElementId != ElementId || seen.ObservedElementId != ElementId ||
                seen.RequestedElementLabel != ElementLabel || seen.ObservedElementLabel != ElementLabel ||
                seen.LabelMatchCount != 1 ||
                seen.FieldName != FlowField || seen.RecordName != FlowRecord ||
                !seen.TimeStepIndex.HasValue || seen.TimeStepIndex.Value != P16CalculatedFlowDiagnostic.SelectTime(seen.AvailableTimeSeconds) ||
                seen.TimeSeconds != TargetTimeSeconds || seen.TimeSeconds != seen.AvailableTimeSeconds[seen.TimeStepIndex.Value] ||
                seen.WorkingUnitSymbol == null || string.IsNullOrEmpty(seen.WorkingUnitIdentity) ||
                string.IsNullOrEmpty(seen.StorageUnitIdentity) || seen.UnitEvidence == null ||
                seen.WorkingUnitSymbol != FlowUnitSymbol(UnitEnumName(seen.WorkingUnitIdentity)) ||
                !IsVerifiedFiniteValue(seen.ValueClrType, seen.RawValueClassification, seen.RawValue) ||
                seen.ErrorType != null || seen.FailureOperation != null)
                throw new InvalidDataException("Azadegan pass observation lacks exact engine, scenario, element, field, time, unit or finite-value evidence.");
        }

        // Identity format is "<UnitIndex full name>:<enum literal>:<numeric value>".
        internal static string UnitEnumName(string identity)
        {
            if (string.IsNullOrEmpty(identity)) return null;
            string[] parts = identity.Split(':');
            return parts.Length == 3 ? parts[1] : null;
        }

        internal static bool IsVerifiedFiniteValue(string clrType, string classification, string rawValue)
        {
            double value;
            if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            if (double.IsNaN(value) || double.IsInfinity(value)) return false;
            if ((clrType == "System.Double" || clrType == "System.Single") &&
                classification != null && classification.StartsWith("FINITE_", StringComparison.Ordinal)) return true;
            return clrType == "System.Decimal" && classification == "NUMERIC_CLR_TYPE_SEMANTICS_UNVERIFIED";
        }

        internal static void ValidateResult(HostResult result)
        {
            string json;
            if (!result.Diagnostics.TryGetValue(EvidenceKey, out json)) throw new InvalidDataException("Azadegan observation is missing.");
            ValidateObservation(json);
            if (!result.Diagnostics.TryGetValue(PassFlag, out string passed) || passed != "true")
                throw new InvalidDataException("Azadegan result did not carry the extraction pass flag.");
        }
    }

    internal sealed class P16AzadeganProofOutcome
    {
        public string SourceModelPath { get; set; }
        public string WorkingCopyDirectory { get; set; }
        public string WorkingModelPath { get; set; }
        public string InventoryDigest { get; set; }
        public List<string> SourceManifest { get; set; } = new List<string>();
        public List<string> WorkingManifest { get; set; } = new List<string>();
        public string RequestJson { get; set; }
        public string ResultJson { get; set; }
        public string ObservationJson { get; set; }
        public string Status { get; set; }
        public string FailureClassification { get; set; }
        public string FailureMessageSafe { get; set; }
        public int RuntimeExitCode { get; set; }
        public bool Passed { get; set; }
        public bool SourceIntegrityPreserved { get; set; }
        public bool WorkingIntegrityPreserved { get; set; }
        public List<string> SourceAfterManifest { get; set; } = new List<string>();
        public List<string> WorkingAfterManifest { get; set; } = new List<string>();
        public List<string> WorkingAddedFiles { get; set; } = new List<string>();
        public string RunDirectory { get; set; }
        public string Error { get; set; }
    }

    // Client-side orchestration for the single bounded attempt. It copies the declared family,
    // records both manifests, launches exactly one owned RuntimeHost process and records the
    // resulting counters. It performs no vendor call itself.
    internal static class P16AzadeganReadOnlyResultProof
    {
        private const int TimeoutMilliseconds = 300000;

        internal static List<string> Manifest(IEnumerable<string> paths)
        {
            var lines = new List<string>();
            foreach (string path in paths.OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
            {
                var info = new FileInfo(path);
                lines.Add(Path.GetFileName(path) + "|" + info.Length.ToString(CultureInfo.InvariantCulture) + "|" +
                    WorkingCopyIntegrity.Fingerprint(path).SHA256);
            }
            return lines;
        }

        internal static string DefaultRuntimeHostExecutable()
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(P16AzadeganReadOnlyResultProof).Assembly.Location));
                 dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SewerPilot.sln")))
                    return Path.Combine(dir.FullName, "src", "SewerPilot.OpenFlows.RuntimeHost", "bin", "Release", "net48",
                        "SewerPilot.OpenFlows.RuntimeHost.exe");
            }
            return null;
        }

        internal static P16AzadeganProofOutcome Execute(string sourceModelPath, string selectedRuntime, string runtimeHostExecutable = null)
        {
            var outcome = new P16AzadeganProofOutcome { SourceModelPath = Path.GetFullPath(sourceModelPath) };
            // Normalize the runtime root so the declared core inventory uses the same canonical
            // form the host returns; the SHA-256 identity is what actually binds the binding.
            selectedRuntime = Path.GetFullPath(selectedRuntime);
            WorkingCopyIntegrity working = null;
            try
            {
                string sourceDirectory = Path.GetDirectoryName(outcome.SourceModelPath);
                var family = P16AzadeganReadOnlyResultHost.Family.Select(n => Path.Combine(sourceDirectory, n)).ToArray();
                foreach (string file in family)
                    if (!File.Exists(file)) throw new FileNotFoundException("Declared model family file is absent: " + file);
                outcome.SourceManifest = Manifest(family);

                working = WorkingCopyIntegrity.Create(outcome.SourceModelPath, family);
                outcome.WorkingCopyDirectory = working.DirectoryPath;
                outcome.WorkingModelPath = working.ModelPath;
                outcome.WorkingManifest = Manifest(P16AzadeganReadOnlyResultHost.Family.Select(n => Path.Combine(working.DirectoryPath, n)));
                outcome.InventoryDigest = P16AzadeganReadOnlyResultHost.InventoryDigest(working.DirectoryPath);

                string copyId = Path.GetFileName(working.DirectoryPath);
                string executable = runtimeHostExecutable ?? DefaultRuntimeHostExecutable();
                if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
                    throw new FileNotFoundException("RuntimeHost executable is unavailable: " + (executable ?? "<unresolved>"));

                var coreFiles = OpenFlowsRuntimeCandidate.CoreAssemblyNames
                    .Select(name => Path.Combine(selectedRuntime, name + ".dll")).ToArray();
                var request = new HostRequest
                {
                    SchemaVersion = 1,
                    RunId = Guid.NewGuid().ToString("N"),
                    Operation = P16AzadeganReadOnlyResultHost.Operation,
                    WorkingModelPath = working.ModelPath,
                    WorkingCopyId = copyId,
                    RuntimeCandidateCount = 1,
                    DetectedArchitecture = "x64",
                    SelectedRuntime = selectedRuntime,
                    CoreFiles = coreFiles.Select(path => new HostMetadata
                    {
                        SimpleName = Path.GetFileNameWithoutExtension(path),
                        Path = path,
                        SHA256 = WorkingCopyIntegrity.Fingerprint(path).SHA256
                    }).ToArray(),
                    Diagnostics = P16AzadeganReadOnlyResultHost.Context(copyId, outcome.InventoryDigest)
                };
                outcome.RequestJson = RuntimeHostProtocol.Serializer().Serialize(request);

                string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "RuntimeHost", request.RunId);
                RuntimeHostProtocol.ControlledDirectory(directory, "RuntimeHost");
                Directory.CreateDirectory(directory);
                outcome.RunDirectory = directory;
                string requestPath = Path.Combine(directory, "request.json");
                string resultPath = Path.Combine(directory, "result.json");
                RuntimeHostProtocol.AtomicWrite(requestPath, request);

                var start = new System.Diagnostics.ProcessStartInfo(executable,
                    P16AzadeganReadOnlyResultHost.LaunchSwitch + " \"" + requestPath + "\" \"" + resultPath + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executable)
                };
                // The child sets its own stage-journal path from the run directory, so no environment
                // dictionary is materialized here. The API key is scrubbed in the parent instead,
                // which avoids the case-insensitive proxy-variable collision in ProcessStartInfo.
                string atriaKey = Environment.GetEnvironmentVariable("ATRIA_API_KEY");
                if (atriaKey != null) Environment.SetEnvironmentVariable("ATRIA_API_KEY", null);
                System.Diagnostics.Process process;
                try { process = System.Diagnostics.Process.Start(start); }
                finally { if (atriaKey != null) Environment.SetEnvironmentVariable("ATRIA_API_KEY", atriaKey); }
                using (process)
                {
                    if (!process.WaitForExit(TimeoutMilliseconds))
                    {
                        process.Kill();
                        throw new TimeoutException("RuntimeHost execution exceeded its bound.");
                    }
                    outcome.RuntimeExitCode = process.ExitCode;
                }

                if (!File.Exists(resultPath))
                    throw new InvalidOperationException("RuntimeHost produced no result document (exit code " + outcome.RuntimeExitCode + ").");
                outcome.ResultJson = File.ReadAllText(resultPath);
                var result = RuntimeHostProtocol.ParseResult(resultPath, request);
                outcome.FailureClassification = result.FailureClassification;
                outcome.FailureMessageSafe = result.FailureMessageSafe;
                outcome.Passed = result.Success;
                string observation;
                if (result.Diagnostics.TryGetValue(P16AzadeganReadOnlyResultHost.EvidenceKey, out observation))
                {
                    outcome.ObservationJson = observation;
                    outcome.Status = result.Diagnostics.ContainsKey("P16AzadeganStatus")
                        ? result.Diagnostics["P16AzadeganStatus"] : null;
                }
                else
                {
                    outcome.Status = "OBSERVATION_ABSENT";
                }
            }
            catch (Exception error)
            {
                outcome.Error = error.ToString();
                if (outcome.Status == null) outcome.Status = "ATTEMPT_ABORTED";
            }
            finally
            {
                try
                {
                    outcome.SourceAfterManifest = Manifest(P16AzadeganReadOnlyResultHost.Family.Select(n =>
                        Path.Combine(Path.GetDirectoryName(outcome.SourceModelPath), n)));
                    outcome.SourceIntegrityPreserved = outcome.SourceAfterManifest.SequenceEqual(outcome.SourceManifest);
                }
                catch (Exception error) { outcome.Error = (outcome.Error ?? "") + "\nSource re-hash failed: " + error; }
                if (working != null)
                {
                    var evidence = new ModelReadEvidence();
                    try
                    {
                        working.Complete(evidence);
                        outcome.WorkingIntegrityPreserved = evidence.WorkingIntegrity;
                        outcome.WorkingAddedFiles = evidence.TransientFiles ?? new List<string>();
                        outcome.WorkingAfterManifest = Manifest(P16AzadeganReadOnlyResultHost.Family.Select(n =>
                            Path.Combine(working.DirectoryPath, n)));
                    }
                    catch (Exception error) { outcome.Error = (outcome.Error ?? "") + "\nWorking re-hash failed: " + error; }
                }
            }
            return outcome;
        }
    }
}
