using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    // Bounded, strictly read-only readback of already-persisted results for Azadegan Scenario 353
    // through the supported WaterObjects.NET model-result API (the domain result field), not the
    // low-level .out reader. It opens a disposable working copy, never the frozen customer source,
    // never runs a solver, never saves and never mutates an element, scenario or alternative.
    //
    // Engine basis: Scenario 353's persisted hydraulic results belong to the GVF pressure engine.
    // The earlier Delaware-engine readback reported HasResults=false for this scenario, which is
    // consistent with the result store being a GVF product (_1_353-swrc-gvf.out). The engine name,
    // field name and record name below are the installed-build string literals verified present in
    // Haestad.Domain metadata; they are not assumed to equal the output-reader field
    // CalculatedTotalGravityFlow, which is a separate vocabulary.
    internal sealed class P16AzadeganGvfObservation
    {
        public string Status { get; set; } = "GVF_RESULT_CONTRACT_UNAVAILABLE";
        public string OpenedModelPath { get; set; }
        public int? DataSetId { get; set; }
        public string DataSetGuid { get; set; }
        public int? ActiveScenarioBefore { get; set; }
        public int? ActiveScenarioAfter { get; set; }
        public int? RequestedScenarioId { get; set; }
        public int? CurrentPhysicalAlternative { get; set; }
        public int? CurrentCalculationOptions { get; set; }
        public int? ExpectedCalculationOptions { get; set; }
        public string Engine { get; set; }
        public string ResultManagerEngineName { get; set; }
        public int? ResultManagerScenarioId { get; set; }
        public bool ResultManagerAvailable { get; set; }
        public bool? ResultManagerWasActive { get; set; }
        public bool? HasResults { get; set; }
        public bool? FieldHasResults { get; set; }
        public bool? FieldIsActive { get; set; }
        public bool? FlowFieldSupported { get; set; }
        public int SupportedFieldCount { get; set; }
        public string[] SupportedFieldNames { get; set; } = new string[0];
        public string FieldRuntimeType { get; set; }
        public string FieldName { get; set; }
        public string RecordName { get; set; }
        public string RequestedElementLabel { get; set; }
        public int? RequestedElementId { get; set; }
        public string ObservedElementLabel { get; set; }
        public int? ObservedElementId { get; set; }
        public int LabelMatchCount { get; set; }
        public bool ElementIdentityVerified { get; set; }
        public string LabelEvidence { get; set; }
        public int ModelConduitCount { get; set; }
        public int RequestedConduitCount { get; set; }
        public int ReadConduitCount { get; set; }
        public int DistinctReadCount { get; set; }
        public bool CoverageComplete { get; set; }
        public int[] ConduitIds { get; set; } = new int[0];
        // Values are carried as invariant round-trip text, never as binary floats: the wire
        // serializer cannot round-trip NaN/Infinity, and a text form keeps the exact double
        // without any unit conversion or numeric reinterpretation.
        public string[] FlowValues { get; set; } = new string[0];
        public string[] OverTimeValues { get; set; } = new string[0];
        public int[] OverTimeLengths { get; set; } = new int[0];
        public bool[] OverTimeConsistent { get; set; } = new bool[0];
        public int[] UnreadableConduitIds { get; set; } = new int[0];
        public int OverTimeMismatchCount { get; set; }
        public int NonFiniteValueCount { get; set; }
        public string FocusedElementValue { get; set; }
        public string FocusedElementOverTimeValue { get; set; }
        public int? TimeStepIndex { get; set; }
        public double? TimeSeconds { get; set; }
        public int TimeStepCount { get; set; }
        public double[] AvailableTimeSeconds { get; set; } = new double[0];
        public string TimeStepSelectionBasis { get; set; }
        public string WorkingUnitSymbol { get; set; }
        public string WorkingUnitIdentity { get; set; }
        public string StorageUnitIdentity { get; set; }
        public string UnitEvidence { get; set; }
        public string ValueClrType { get; set; }
        public string ValueClassification { get; set; }
        public int OverTimeReadInvocations { get; set; }
        public int ValueReadInvocations { get; set; }
        public bool FreshnessVerified { get; set; }
        public string FreshnessEvidence { get; set; }
        public bool ResultCleanupCompleted { get; set; }
        public string CleanupError { get; set; }
        public string FailureOperation { get; set; }
        public string ErrorType { get; set; }
        public int? ErrorHResult { get; set; }
        public string ErrorMessage { get; set; }
    }

    internal static class P16AzadeganGvfReadbackHost
    {
        internal const string Operation = "InternalP16AzadeganGvfReadback";
        internal const string LaunchSwitch = "--internal-p16-azadegan-gvf-readback";
        internal const string EvidenceKey = "P16AzadeganGvfObservationJson";
        internal const string PassFlag = "P16AzadeganGvfReadbackPassed";
        internal const string PassStatus = "PASS_GVF_PERSISTED_RESULT_READBACK";
        internal const string BlockedFailureCode = "P16_AZADEGAN_GVF_READBACK_BLOCKED";

        // Installed-build literals, verified present in Haestad.Domain #Strings metadata.
        internal const string Engine = "GVFPressureEngine";
        internal const string FlowField = "BasicFlowResults_Flow";
        internal const string FlowRecord = "BasicFlowResults";

        internal const int ScenarioId = 353;
        internal const int PhysicalAlternativeId = 352;
        internal const int ExpectedCalculationOptionsId = 24;
        internal const string PhysicalAlternativeTypeId = "4";
        internal const string ElementLabel = "SSW-001";
        internal const int ElementId = 119;
        internal const double TargetTimeSeconds = 0d;

        private const int MaxFields = 1024, MaxTimeSteps = 10000, MaxConduits = 20000;

        internal static string ModelFileName => P16AzadeganReadOnlyResultHost.ModelFileName;
        internal static string[] Family => P16AzadeganReadOnlyResultHost.Family;
        internal static string WorkingRoot => P16AzadeganReadOnlyResultHost.WorkingRoot;
        internal static string ExpectedModelPath(string copyId) => P16AzadeganReadOnlyResultHost.ExpectedModelPath(copyId);

        internal static string FlowUnitSymbol(string enumName) => P16AzadeganReadOnlyResultHost.FlowUnitSymbol(enumName);
        internal static string UnitEnumName(string identity) => P16AzadeganReadOnlyResultHost.UnitEnumName(identity);
        internal static string InventoryDigest(string directory) => P16AzadeganReadOnlyResultHost.InventoryDigest(directory);

        internal static Dictionary<string, string> Context(string copyId, string inventoryDigest) => new Dictionary<string, string>
        {
            { "P16AzadeganGvfScenarioId", ScenarioId.ToString(CultureInfo.InvariantCulture) },
            { "P16AzadeganGvfPhysicalAlternativeId", PhysicalAlternativeId.ToString(CultureInfo.InvariantCulture) },
            { "P16AzadeganGvfExpectedCalculationOptionsId", ExpectedCalculationOptionsId.ToString(CultureInfo.InvariantCulture) },
            { "P16AzadeganGvfEngine", Engine },
            { "P16AzadeganGvfElementLabel", ElementLabel },
            { "P16AzadeganGvfElementId", ElementId.ToString(CultureInfo.InvariantCulture) },
            { "P16AzadeganGvfCopyId", copyId },
            { "P16AzadeganGvfInventorySHA256", inventoryDigest }
        };

        internal static void ValidateRequest(HostRequest request)
        {
            if (request == null || request.Operation != Operation) throw new InvalidDataException("Azadegan GVF readback operation mismatch.");
            if (request.EngineeringPlan != null || request.WriteAuthorizationTokenId != null ||
                request.WriteAuthorizationScope != null || request.WriteAuthorizationTargetWorkingPath != null)
                throw new InvalidDataException("Azadegan GVF readback permits no mutation plan or write authority.");
            if (request.Diagnostics == null) throw new InvalidDataException("Azadegan GVF readback diagnostics are absent.");

            string copyId;
            if (!request.Diagnostics.TryGetValue("P16AzadeganGvfCopyId", out copyId) ||
                !System.Text.RegularExpressions.Regex.IsMatch(copyId ?? "", "^[a-f0-9]{32}$") ||
                request.WorkingCopyId != copyId)
                throw new InvalidDataException("Azadegan GVF readback copy identity is not a controlled per-run identifier.");

            string modelPath = ExpectedModelPath(copyId);
            if (!string.Equals(Path.GetFullPath(request.WorkingModelPath ?? ""), Path.GetFullPath(modelPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Azadegan GVF readback must target the disposable read-only working copy only.");

            string directory = Path.GetDirectoryName(modelPath);
            RuntimeHostProtocol.ControlledDirectory(directory, "ReadOnly");
            WorkingCopyIntegrity.RejectLinks(directory);
            var names = Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            if (!names.SequenceEqual(Family.OrderBy(n => n, StringComparer.Ordinal)))
                throw new InvalidDataException("Azadegan GVF readback requires exactly its six recorded files.");
            WorkingCopyIntegrity.RejectLinks(modelPath);

            string digest;
            if (!request.Diagnostics.TryGetValue("P16AzadeganGvfInventorySHA256", out digest) ||
                !System.Text.RegularExpressions.Regex.IsMatch(digest ?? "", "^[A-F0-9]{64}$"))
                throw new InvalidDataException("Azadegan GVF readback inventory digest is malformed.");
            if (!string.Equals(InventoryDigest(directory), digest, StringComparison.Ordinal))
                throw new InvalidDataException("Azadegan GVF working copy does not match its declared inventory digest.");

            var expected = Context(copyId, digest);
            if (request.Diagnostics.Count != expected.Count ||
                expected.Any(pair => !request.Diagnostics.ContainsKey(pair.Key) || request.Diagnostics[pair.Key] != pair.Value))
                throw new InvalidDataException("Azadegan GVF readback requires the exact bounded scenario/engine/element context.");
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
                return RuntimeStageJournal.Call("P16Gvf:" + Last, () => method.Invoke(target, values),
                    value => value is bool || value is int ? value : value == null ? "<null>" : value.GetType().FullName);
            }
            internal object Get(object target, Type contract, string name, Type returns) =>
                Invoke(target, contract, "get_" + name, returns, Type.EmptyTypes);
        }

        private static string ReadLabel(Calls calls, object element, Type elementContract, P16AzadeganGvfObservation seen)
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
            var seen = new P16AzadeganGvfObservation
            {
                OpenedModelPath = request.WorkingModelPath,
                RequestedScenarioId = ScenarioId,
                RequestedElementLabel = ElementLabel,
                RequestedElementId = ElementId,
                Engine = Engine,
                ExpectedCalculationOptions = ExpectedCalculationOptionsId,
                FreshnessVerified = false,
                FreshnessEvidence = "Pre-existing persisted project state only. No solver run, save, calculation lineage or run event was produced or verified by this readback."
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
                var resultFieldType = Contract("Haestad.Domain", "Haestad.Domain.IResultField");
                var variantType = Contract("Haestad.Domain", "Haestad.Domain.IResultTimeVariantField");
                var unitizedType = Contract("Haestad.Domain", "Haestad.Domain.IUnitizedField");
                var unitIndex = Contract("Haestad.Support", "Haestad.Support.Units.UnitConversionManager+UnitIndex");
                resultManagerType = Contract("Haestad.Domain", "Haestad.Domain.IResultManager");

                seen.DataSetId = (int)calls.Get(dataset, dataType, "Id", typeof(int));
                seen.DataSetGuid = ((Guid)calls.Get(dataset, dataType, "Guid", typeof(Guid))).ToString("D");
                scenarioManager = calls.Get(dataset, dataType, "ScenarioManager", scenarioManagerType);
                seen.ActiveScenarioBefore = (int)calls.Get(scenarioManager, scenarioManagerType, "ActiveScenarioID", typeof(int));

                // The scenario is addressed by explicit id; the active scenario is never selected.
                object scenario = calls.Invoke(scenarioManager, elementsType, "Element", elementType, new[] { typeof(int) }, ScenarioId);
                if (!scenarioType.IsInstanceOfType(scenario)) throw new InvalidOperationException("Requested scenario 353 is not an IScenario.");
                seen.CurrentPhysicalAlternative = (int)calls.Invoke(scenario, scenarioType, "AlternativeID", typeof(int),
                    new[] { typeof(int) }, int.Parse(PhysicalAlternativeTypeId, CultureInfo.InvariantCulture));
                seen.CurrentCalculationOptions = (int)calls.Invoke(scenario, scenarioType, "CalculationOptionsID", typeof(int),
                    new[] { typeof(string) }, Engine);
                if (seen.CurrentPhysicalAlternative != PhysicalAlternativeId || seen.CurrentCalculationOptions != ExpectedCalculationOptionsId)
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

                // Independent label-to-ID verification: the requested id must carry the requested
                // label and that label must be unique in the conduit manager.
                var idsMethod = WaterObjectsLifecycleHost.ElementIDs(conduitManager.GetType());
                if (idsMethod == null) throw new MissingMethodException("Conduit ElementIDs member unavailable.");
                object ids = idsMethod.Invoke(conduitManager, null);
                var indexer = WaterObjectsLifecycleHost.Indexer(ids.GetType());
                int count = (int)ids.GetType().GetProperty("Count").GetValue(ids, null);
                if (count <= 0 || count > MaxConduits) throw new InvalidOperationException("Conduit count is out of bounds: " + count);
                seen.ModelConduitCount = count;
                var modelIds = new List<int>(count);
                var labelMatches = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    int id = (int)indexer.GetValue(ids, new object[] { i });
                    modelIds.Add(id);
                    object element = calls.Invoke(conduitManager, elementsType, "Element", elementType, new[] { typeof(int) }, id);
                    string label = ReadLabel(calls, element, elementType, seen);
                    if (string.Equals(label, ElementLabel, StringComparison.Ordinal)) labelMatches.Add(id);
                }
                modelIds.Sort();
                seen.LabelMatchCount = labelMatches.Count;
                if (labelMatches.Count != 1 || labelMatches[0] != ElementId)
                {
                    seen.Status = "ELEMENT_IDENTITY_UNVERIFIED";
                    seen.FailureOperation = "label scan for " + ElementLabel;
                    seen.ErrorMessage = "Label matches: " + string.Join(",", labelMatches.Select(m => m.ToString(CultureInfo.InvariantCulture)));
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
                seen.FieldRuntimeType = flowField == null ? null : flowField.GetType().FullName;
                if (flowField == null) { seen.Status = "RESULT_FIELD_UNSUPPORTED"; return; }
                seen.FieldName = (string)calls.Get(flowField, fieldType, "Name", typeof(string));
                seen.RecordName = FlowRecord; // Explicit record argument bound above; never substituted.
                if (seen.FieldName != FlowField) throw new InvalidOperationException("Returned field identity mismatch: " + seen.FieldName);
                if (!variantType.IsInstanceOfType(flowField)) throw new InvalidOperationException("Flow field lacks the exact time-variant contract.");

                seen.FieldHasResults = (bool)calls.Invoke(flowField, resultFieldType, "HasResults", typeof(bool), new[] { typeof(int) }, ScenarioId);
                seen.FieldIsActive = (bool)calls.Invoke(flowField, resultFieldType, "IsActive", typeof(bool), new[] { typeof(int) }, ScenarioId);
                if (seen.FieldHasResults != true) { seen.Status = "RESULTS_MISSING"; return; }

                var timeUnitType = Contract("Haestad.Support", "Haestad.Support.Units.TimeUnit");
                var times = (double[])calls.Invoke(manager, resultManagerType, "TimeSteps", typeof(double[]), new[] { timeUnitType }, Enum.Parse(timeUnitType, "Seconds", false));
                seen.TimeStepCount = times == null ? 0 : times.Length;
                if (times == null || times.Length == 0) { seen.Status = "RESULTS_MISSING"; return; }
                if (times.Length > MaxTimeSteps) throw new InvalidOperationException("Available timestep vector exceeds its bound.");
                seen.AvailableTimeSeconds = times;
                int index;
                try
                {
                    index = P16CalculatedFlowDiagnostic.SelectTime(times);
                    seen.TimeStepSelectionBasis = times.Length == 1
                        ? "SOLE_TIME_STEP_AT_TARGET_TIME" : "TARGET_TIME_PRESENT_IN_VECTOR";
                }
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

                if (unitizedType.IsInstanceOfType(flowField))
                {
                    object working = calls.Get(flowField, unitizedType, "WorkingUnitIndex", unitIndex);
                    object storage = calls.Get(flowField, unitizedType, "StorageUnitIndex", unitIndex);
                    string workingName = Enum.GetName(unitIndex, working);
                    seen.WorkingUnitIdentity = unitIndex.FullName + ":" + workingName + ":" + Convert.ToInt64(working, CultureInfo.InvariantCulture);
                    seen.StorageUnitIdentity = unitIndex.FullName + ":" + Enum.GetName(unitIndex, storage) + ":" + Convert.ToInt64(storage, CultureInfo.InvariantCulture);
                    seen.WorkingUnitSymbol = FlowUnitSymbol(workingName);
                    seen.UnitEvidence = "Native IUnitizedField WorkingUnitIndex/StorageUnitIndex enum literals read from the installed field; the symbol is a label for that exact literal and GetValue is unconverted.";
                }
                else
                {
                    seen.UnitEvidence = "IUnitizedField is not exposed; unit identity cannot be verified.";
                }
                if (string.IsNullOrEmpty(seen.WorkingUnitSymbol)) { seen.Status = "RESULT_UNIT_UNVERIFIED"; return; }

                // Complete applicable conduit set, associated by real element id. Every series is
                // read over time and indexed at the evidenced timestep, never at an assumed zero.
                var readIds = new List<int>(modelIds.Count);
                var values = new List<string>(modelIds.Count);
                var overTime = new List<string>(modelIds.Count);
                var overTimeLengths = new List<int>(modelIds.Count);
                var consistent = new List<bool>(modelIds.Count);
                var unreadable = new List<int>();
                foreach (int id in modelIds)
                {
                    object series = calls.Invoke(flowField, variantType, "GetValuesOverTime", typeof(Array), new[] { typeof(int), typeof(int) }, id, ScenarioId);
                    seen.OverTimeReadInvocations++;
                    var array = series as Array;
                    int length = array == null ? 0 : array.Length;
                    object overItem = array != null && index < length ? array.GetValue(index) : null;
                    double overValue = ToFiniteDouble(overItem, out bool overFinite);

                    object single = calls.Invoke(flowField, variantType, "GetValue", typeof(object), new[] { typeof(int), typeof(int), typeof(int) }, id, ScenarioId, index);
                    seen.ValueReadInvocations++;
                    double singleValue = ToFiniteDouble(single, out bool singleFinite);
                    if (seen.ValueClrType == null) seen.ValueClrType = single == null ? "null" : single.GetType().FullName;

                    bool ok = array != null && length == times.Length && overFinite && singleFinite;
                    if (!ok) unreadable.Add(id);
                    if (overFinite && singleFinite && !(overValue.Equals(singleValue))) seen.OverTimeMismatchCount++;
                    if (!overFinite || !singleFinite) seen.NonFiniteValueCount++;

                    readIds.Add(id);
                    values.Add(ValueText(singleValue, singleFinite));
                    overTime.Add(ValueText(overValue, overFinite));
                    overTimeLengths.Add(length);
                    consistent.Add(overFinite && singleFinite && overValue.Equals(singleValue));
                    if (id == ElementId)
                    {
                        seen.FocusedElementValue = ValueText(singleValue, singleFinite);
                        seen.FocusedElementOverTimeValue = ValueText(overValue, overFinite);
                    }
                }

                seen.ConduitIds = readIds.ToArray();
                seen.FlowValues = values.ToArray();
                seen.OverTimeValues = overTime.ToArray();
                seen.OverTimeLengths = overTimeLengths.ToArray();
                seen.OverTimeConsistent = consistent.ToArray();
                seen.UnreadableConduitIds = unreadable.ToArray();
                seen.RequestedConduitCount = modelIds.Count;
                seen.ReadConduitCount = readIds.Count;
                seen.DistinctReadCount = readIds.Distinct().Count();
                seen.CoverageComplete = seen.RequestedConduitCount > 0 &&
                    seen.ReadConduitCount == seen.RequestedConduitCount &&
                    seen.DistinctReadCount == seen.RequestedConduitCount &&
                    unreadable.Count == 0;

                seen.ValueClassification = seen.NonFiniteValueCount == 0
                    ? "FINITE_DOUBLE_SENTINEL_SEMANTICS_UNVERIFIED" : "NON_FINITE_VALUES_PRESENT";
                seen.Status = seen.CoverageComplete && seen.NonFiniteValueCount == 0 && seen.OverTimeMismatchCount == 0
                    ? PassStatus : "RESULT_VALUE_UNAVAILABLE";
            }
            catch (Exception error)
            {
                var exact = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                seen.Status = exact is FileNotFoundException || exact is DirectoryNotFoundException ? "RESULTS_MISSING" : "GVF_RESULT_CONTRACT_UNAVAILABLE";
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
                    seen.Status = "GVF_RESULT_CONTRACT_UNAVAILABLE";
                }
                try
                {
                    if (scenarioManager != null && scenarioManagerType != null && seen.ActiveScenarioBefore.HasValue)
                    {
                        seen.ActiveScenarioAfter = (int)calls.Get(scenarioManager, scenarioManagerType, "ActiveScenarioID", typeof(int));
                        if (seen.ActiveScenarioAfter != seen.ActiveScenarioBefore) throw new InvalidOperationException("Active scenario changed during the read-only readback.");
                    }
                }
                catch (Exception error)
                {
                    seen.CleanupError = (seen.CleanupError ?? "") + error;
                    seen.Status = "GVF_RESULT_CONTRACT_UNAVAILABLE";
                }
                evidence.Diagnostics[EvidenceKey] = RuntimeHostProtocol.Serializer().Serialize(seen);
                evidence.Diagnostics["P16AzadeganGvfStatus"] = seen.Status;
                if (seen.Status == PassStatus && seen.ResultCleanupCompleted) evidence.Diagnostics[PassFlag] = "true";
                else if (evidence.FailureCode == null) evidence.FailureCode = BlockedFailureCode;
            }
        }

        // Invariant round-trip text for a returned value. A non-finite or null return is recorded
        // as an explicit non-value rather than being coerced to a number.
        private static string ValueText(double value, bool finite) =>
            finite ? value.ToString("R", CultureInfo.InvariantCulture) : "NON_FINITE_OR_NULL";

        // Converts a vendor-returned numeric to double without any unit conversion. Anything that
        // is not a finite numeric primitive is reported as non-finite rather than coerced.
        private static double ToFiniteDouble(object value, out bool finite)
        {
            finite = false;
            if (value == null) return double.NaN;
            double parsed;
            if (value is double) parsed = (double)value;
            else if (value is float) parsed = (float)value;
            else if (value is decimal) parsed = (double)(decimal)value;
            else if (value is int) parsed = (int)value;
            else if (value is long) parsed = (long)value;
            else if (value is short) parsed = (short)value;
            else if (value is uint) parsed = (uint)value;
            else if (value is ulong) parsed = (ulong)value;
            else if (value is ushort) parsed = (ushort)value;
            else if (value is byte) parsed = (byte)value;
            else if (value is sbyte) parsed = (sbyte)value;
            else return double.NaN;
            if (double.IsNaN(parsed) || double.IsInfinity(parsed)) return double.NaN;
            finite = true;
            return parsed;
        }

        internal static void ValidateObservation(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 4 * 1024 * 1024) throw new InvalidDataException("Azadegan GVF observation is absent or oversized.");
            P16AzadeganGvfObservation seen;
            try
            {
                var serializer = RuntimeHostProtocol.Serializer();
                var fields = serializer.DeserializeObject(json) as Dictionary<string, object>;
                var properties = typeof(P16AzadeganGvfObservation).GetProperties();
                if (fields == null || fields.Count != properties.Length || properties.Any(p => !fields.ContainsKey(p.Name)))
                    throw new InvalidDataException();
                seen = serializer.Deserialize<P16AzadeganGvfObservation>(json);
            }
            catch (Exception) { throw new InvalidDataException("Malformed or noncanonical Azadegan GVF observation."); }

            string[] allowed = { PassStatus, "RESULTS_MISSING", "RESULT_MANAGER_UNAVAILABLE", "RESULT_MANAGER_CONTEXT_MISMATCH",
                "SCENARIO_CONTEXT_MISMATCH", "ELEMENT_IDENTITY_UNVERIFIED", "RESULT_FIELD_UNSUPPORTED", "RESULT_TIME_UNAVAILABLE",
                "RESULT_UNIT_UNVERIFIED", "RESULT_VALUE_UNAVAILABLE", "GVF_RESULT_CONTRACT_UNAVAILABLE" };
            if (seen == null || !allowed.Contains(seen.Status) || seen.FreshnessVerified || !seen.ResultCleanupCompleted ||
                seen.CleanupError != null || seen.OverTimeReadInvocations < 0 || seen.ValueReadInvocations < 0 ||
                seen.SupportedFieldNames == null || seen.SupportedFieldNames.Length > MaxFields ||
                seen.SupportedFieldCount != seen.SupportedFieldNames.Length ||
                seen.AvailableTimeSeconds == null || seen.AvailableTimeSeconds.Length > MaxTimeSteps ||
                seen.TimeStepCount != seen.AvailableTimeSeconds.Length ||
                seen.ConduitIds == null || seen.FlowValues == null || seen.OverTimeValues == null ||
                seen.OverTimeLengths == null || seen.OverTimeConsistent == null ||
                seen.ConduitIds.Length != seen.FlowValues.Length || seen.ConduitIds.Length != seen.OverTimeValues.Length ||
                seen.ConduitIds.Length != seen.OverTimeLengths.Length || seen.ConduitIds.Length != seen.OverTimeConsistent.Length ||
                seen.ConduitIds.Length > MaxConduits)
                throw new InvalidDataException("Azadegan GVF observation violates bounded read-only/cleanup semantics.");

            if (seen.Status != PassStatus) return;
            if (seen.OverTimeReadInvocations != seen.ReadConduitCount || seen.ValueReadInvocations != seen.ReadConduitCount ||
                seen.ResultManagerAvailable != true || seen.HasResults != true || seen.FieldHasResults != true ||
                seen.FlowFieldSupported != true || !seen.ElementIdentityVerified || !seen.CoverageComplete ||
                seen.CurrentPhysicalAlternative != PhysicalAlternativeId || seen.CurrentCalculationOptions != ExpectedCalculationOptionsId ||
                seen.ResultManagerEngineName != Engine || seen.ResultManagerScenarioId != ScenarioId ||
                seen.ActiveScenarioBefore != seen.ActiveScenarioAfter || seen.Engine != Engine ||
                seen.RequestedElementId != ElementId || seen.ObservedElementId != ElementId ||
                seen.RequestedElementLabel != ElementLabel || seen.ObservedElementLabel != ElementLabel ||
                seen.LabelMatchCount != 1 || seen.FieldName != FlowField || seen.RecordName != FlowRecord ||
                !seen.TimeStepIndex.HasValue || seen.TimeStepIndex.Value != P16CalculatedFlowDiagnostic.SelectTime(seen.AvailableTimeSeconds) ||
                seen.TimeSeconds != TargetTimeSeconds || seen.TimeSeconds != seen.AvailableTimeSeconds[seen.TimeStepIndex.Value] ||
                string.IsNullOrEmpty(seen.TimeStepSelectionBasis) ||
                seen.WorkingUnitSymbol == null || string.IsNullOrEmpty(seen.WorkingUnitIdentity) ||
                string.IsNullOrEmpty(seen.StorageUnitIdentity) || seen.UnitEvidence == null ||
                seen.WorkingUnitSymbol != FlowUnitSymbol(UnitEnumName(seen.WorkingUnitIdentity)) ||
                seen.NonFiniteValueCount != 0 || seen.OverTimeMismatchCount != 0 || seen.UnreadableConduitIds.Length != 0 ||
                seen.RequestedConduitCount != seen.ModelConduitCount || seen.ReadConduitCount != seen.RequestedConduitCount ||
                seen.DistinctReadCount != seen.RequestedConduitCount ||
                !seen.ConduitIds.Contains(ElementId) || string.IsNullOrEmpty(seen.FocusedElementValue) ||
                seen.ErrorType != null || seen.FailureOperation != null)
                throw new InvalidDataException("Azadegan GVF pass observation lacks exact engine, scenario, element, field, time, unit or complete finite coverage evidence.");
        }

        internal static void ValidateResult(HostResult result)
        {
            string json;
            if (!result.Diagnostics.TryGetValue(EvidenceKey, out json)) throw new InvalidDataException("Azadegan GVF observation is missing.");
            ValidateObservation(json);
            string passed;
            if (!result.Diagnostics.TryGetValue(PassFlag, out passed) || passed != "true")
                throw new InvalidDataException("Azadegan GVF result did not carry the readback pass flag.");
        }
    }
}
