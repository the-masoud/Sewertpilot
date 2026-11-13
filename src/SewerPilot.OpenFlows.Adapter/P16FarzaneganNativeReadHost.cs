using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    // Independent native reopen of the separately calculated/saved Farzanegan model.
    // Reads an existing persisted flow; it cannot call a solver or save project state.
    internal static class P16FarzaneganNativeReadHost
    {
        internal const string Operation = "P16FarzaneganNativeRead";
        internal const string LaunchSwitch = "--p16-farzanegan-native-read";
        internal const string EvidenceKey = "FarzaneganNativeReadObservationJson";
        internal const string PassFlag = "FarzaneganNativeReadbackVerified";
        internal const string Field = "BasicFlowResults_Flow";
        internal const string Record = "BasicFlowResults";

        internal sealed class Observation
        {
            public string Status { get; set; } = "NOT_STARTED";
            public string CalculationEvent { get; set; }
            public string ModelPath { get; set; }
            public int ScenarioId { get; set; }
            public int OptionsId { get; set; }
            public string Engine { get; set; }
            public int ElementId { get; set; }
            public string ElementLabel { get; set; }
            public string Field { get; set; }
            public string Record { get; set; }
            public int ResultReadInvocations { get; set; }
            public string RawValue { get; set; }
            public string ValueType { get; set; }
            public string WorkingUnitIdentity { get; set; }
            public string StorageUnitIdentity { get; set; }
            public string UnitSymbol { get; set; }
            public double? TimeSeconds { get; set; }
            public bool HasResults { get; set; }
            public bool FieldHasResults { get; set; }
            public bool ResultCleanupCompleted { get; set; }
            public string InputInventory { get; set; }
            public string ErrorType { get; set; }
            public string ErrorMessage { get; set; }
            public string Provenance { get; set; }
            public string NetworkResultReadStatus { get; set; }
            public int NetworkConduitCount { get; set; }
            public int NetworkStructureCount { get; set; }
            public int NetworkResultObservationCount { get; set; }
            public string NetworkResultsJson { get; set; }
            public string NetworkResultReadError { get; set; }
        }

        private static Type Type(string assembly, string name)
        {
            var a = AppDomain.CurrentDomain.GetAssemblies().Where(x => !x.IsDynamic && x.GetName().Name == assembly).ToArray();
            if (a.Length != 1) throw new InvalidOperationException("Runtime type resolution is unavailable or ambiguous: " + assembly);
            return a[0].GetType(name, true, false);
        }

        internal static void ValidateRequest(HostRequest request)
        {
            if (request == null || request.Operation != Operation || request.EngineeringPlan != null ||
                request.WriteAuthorizationTokenId != null || request.WriteAuthorizationScope != null ||
                request.WriteAuthorizationTargetWorkingPath != null || request.Diagnostics == null ||
                request.Diagnostics.Count != 7 ||
                !request.Diagnostics.TryGetValue("FarzaneganReadCopyId", out string copyId) ||
                !System.Text.RegularExpressions.Regex.IsMatch(copyId ?? "", "^[a-f0-9]{32}$") ||
                request.WorkingCopyId != copyId ||
                !request.Diagnostics.TryGetValue("FarzaneganReadScenario", out string scenario) || scenario != "1" ||
                !request.Diagnostics.TryGetValue("FarzaneganReadOptions", out string options) || options != "29" ||
                !request.Diagnostics.TryGetValue("FarzaneganReadEngine", out string engine) || engine != P16FarzaneganNativePilotHost.Engine ||
                !request.Diagnostics.TryGetValue("FarzaneganReadEvent", out string evt) ||
                !System.Text.RegularExpressions.Regex.IsMatch(evt ?? "", "^[a-f0-9]{32}$") ||
                !request.Diagnostics.TryGetValue("FarzaneganReadOutputInventory", out string inventory) ||
                inventory == null || inventory.Length > 64 * 1024 ||
                !request.Diagnostics.TryGetValue("FarzaneganReadInputSHA256", out string digest) ||
                !System.Text.RegularExpressions.Regex.IsMatch(digest ?? "", "^[A-F0-9]{64}$"))
                throw new InvalidDataException("Farzanegan READ requires a complete non-write saved-calculation lineage.");
            string path = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "ReadOnly",
                copyId, P16FarzaneganNativePilotHost.ModelFileName);
            if (!P16SaveQuestionPolicy.SamePath(path, request.WorkingModelPath))
                throw new InvalidDataException("Farzanegan READ target is not its disposable read-only model family.");
            RuntimeHostProtocol.ControlledDirectory(Path.GetDirectoryName(path), "ReadOnly");
            WorkingCopyIntegrity.RejectLinks(path);
            if (P16FarzaneganNativePilotHost.Family.Any(name => !File.Exists(Path.Combine(Path.GetDirectoryName(path),name))))
                throw new InvalidDataException("Saved Farzanegan model family is missing a required file.");
        }

        internal static void Execute(object dataset, object project, HostRequest request, ModelReadEvidence evidence)
        {
            var seen = new Observation {
                CalculationEvent = request.Diagnostics["FarzaneganReadEvent"],
                ModelPath = request.WorkingModelPath,
                ScenarioId = P16FarzaneganNativePilotHost.ScenarioId,
                Engine = P16FarzaneganNativePilotHost.Engine
            };
            var calls = new P16AzadeganCalculationHost.Vendor();
            object resultManager = null;
            Type resultManagerType = null;
            bool opened = false;
            try
            {
                string currentInventory = P16AzadeganCalculationHost.InventoryJson(
                    P16AzadeganCalculationHost.Inventory(Path.GetDirectoryName(request.WorkingModelPath)));
                seen.InputInventory = currentInventory;
                if (!string.Equals(currentInventory, request.Diagnostics["FarzaneganReadOutputInventory"], StringComparison.Ordinal))
                { seen.Status = "PERSISTED_INVENTORY_MISMATCH"; return; }
                if (P16AzadeganCalculationHost.InventoryDigest(
                    P16AzadeganCalculationHost.Inventory(Path.GetDirectoryName(request.WorkingModelPath))) !=
                    request.Diagnostics["FarzaneganReadInputSHA256"])
                { seen.Status = "INPUT_HASH_MISMATCH"; return; }
                evidence.Diagnostics["FarzaneganPersistedInventoryMatches"] = "true";

                Type dataType = Type("Haestad.Domain", "Haestad.Domain.IDomainDataSet");
                Type scenarioManagerType = Type("Haestad.Domain", "Haestad.Domain.IScenarioManager");
                Type elementManagerType = Type("Haestad.Domain", "Haestad.Domain.IModelingElementManager");
                Type elementType = Type("Haestad.Domain", "Haestad.Domain.IModelingElement");
                Type scenarioType = Type("Haestad.Domain", "Haestad.Domain.IScenario");
                Type domainManagerType = Type("Haestad.Domain", "Haestad.Domain.IDomainElementManager");
                resultManagerType = Type("Haestad.Domain", "Haestad.Domain.IResultManager");
                Type fieldType = Type("Haestad.Support", "Haestad.Support.Support.IField");
                object scenarios = calls.Get(dataset, dataType, "ScenarioManager", scenarioManagerType);
                int active = (int)calls.Get(scenarios, scenarioManagerType, "ActiveScenarioID", typeof(int));
                if (active != seen.ScenarioId) { seen.Status = "ACTIVE_SCENARIO_MISMATCH"; return; }
                object scenario = calls.Invoke(scenarios, elementManagerType, "Element", elementType, new[]{typeof(int)},seen.ScenarioId);
                seen.OptionsId = (int)calls.Invoke(scenario, scenarioType, "CalculationOptionsID", typeof(int),
                    new[]{typeof(string)},seen.Engine);
                if (seen.OptionsId != 29) { seen.Status = "OPTIONS_MISMATCH"; return; }
                resultManager = calls.Invoke(scenario, scenarioType, "ResultManager", resultManagerType,
                    new[]{typeof(string)},seen.Engine);
                if (resultManager == null) { seen.Status = "RESULT_MANAGER_UNAVAILABLE"; return; }
                string actualEngine = (string)calls.Get(resultManager,resultManagerType,"NumericalEngineTypeName",typeof(string));
                object owner = calls.Get(resultManager,resultManagerType,"Scenario",scenarioType);
                int managerScenario = ScenarioAlternativeCapabilityMapper.GetElementId(owner);
                if (actualEngine != seen.Engine || managerScenario != seen.ScenarioId)
                { seen.Status = "RESULT_MANAGER_CONTEXT_MISMATCH"; return; }
                if (!(bool)calls.Get(resultManager,resultManagerType,"IsActive",typeof(bool)))
                {
                    calls.Invoke(resultManager,resultManagerType,"Open",typeof(void),System.Type.EmptyTypes);
                    opened = true;
                }
                seen.HasResults = (bool)calls.Get(resultManager,resultManagerType,"HasResults",typeof(bool));
                if (!seen.HasResults) { seen.Status = "SAVED_RESULTS_MISSING"; return; }

                int conduitType = int.Parse(evidence.Diagnostics["ElementTypeValueDiagnosticOnly"],CultureInfo.InvariantCulture);
                object conduitManager = calls.Invoke(dataset,dataType,"DomainElementManager",domainManagerType,
                    new[]{typeof(int)},conduitType);
                seen.ElementId = evidence.FirstConduitElementID.GetValueOrDefault();
                if (seen.ElementId != 52 || !(bool)calls.Invoke(conduitManager,elementManagerType,
                    "Exists",typeof(bool),new[]{typeof(int)},seen.ElementId))
                { seen.Status = "ELEMENT_IDENTITY_NOT_VERIFIED"; return; }
                object element = calls.Invoke(conduitManager,elementManagerType,"Element",elementType,
                    new[]{typeof(int)},seen.ElementId);
                var labelContract = elementType.GetProperty("Label",BindingFlags.Public|BindingFlags.Instance);
                var labelRuntime = element.GetType().GetProperty("Label",BindingFlags.Public|BindingFlags.Instance);
                seen.ElementLabel = labelContract != null && labelContract.PropertyType == typeof(string)
                    ? (string)calls.Get(element,elementType,"Label",typeof(string))
                    : labelRuntime != null && labelRuntime.PropertyType == typeof(string)
                        ? (string)labelRuntime.GetValue(element,null) : null;
                if (string.IsNullOrEmpty(seen.ElementLabel))
                { seen.Status = "ELEMENT_LABEL_MISSING"; return; }
                Type fieldCollectionType = Type("Haestad.Support","Haestad.Support.Support.FieldCollection");
                object supported = calls.Invoke(conduitManager,domainManagerType,"SupportedResultFields",
                    fieldCollectionType,new[]{typeof(string)},seen.Engine);
                var names = new List<string>();
                foreach(object f in (IEnumerable)supported)
                {
                    if(names.Count >= 1024) throw new InvalidOperationException("Too many fields.");
                    names.Add((string)calls.Get(f,fieldType,"Name",typeof(string)));
                }
                if(names.Count(n=>n==Field) != 1)
                { seen.Status = "FLOW_FIELD_NOT_SUPPORTED"; seen.ErrorMessage = string.Join(",",names.Where(n=>n.IndexOf("Flow",StringComparison.OrdinalIgnoreCase)>=0).Take(16)); return; }
                object flow = calls.Invoke(conduitManager,domainManagerType,"ResultField",fieldType,
                    new[]{typeof(string),typeof(string),typeof(string)},Field,seen.Engine,Record);
                if(flow==null){seen.Status="FLOW_FIELD_NOT_SUPPORTED";return;}
                seen.Field = (string)calls.Get(flow,fieldType,"Name",typeof(string));
                seen.Record = Record;
                if(seen.Field!=Field){seen.Status="FIELD_NAME_MISMATCH";return;}
                Type fieldResultType=Type("Haestad.Domain","Haestad.Domain.IResultField");
                Type variantType=Type("Haestad.Domain","Haestad.Domain.IResultTimeVariantField");
                if(!variantType.IsInstanceOfType(flow)){seen.Status="FIELD_NOT_TIME_VARIANT";return;}
                seen.FieldHasResults=(bool)calls.Invoke(flow,fieldResultType,"HasResults",typeof(bool),
                    new[]{typeof(int)},seen.ScenarioId);
                if(!seen.FieldHasResults){seen.Status="FLOW_RESULTS_MISSING";return;}
                Type timeUnit=Type("Haestad.Support","Haestad.Support.Units.TimeUnit");
                double[] times=(double[])calls.Invoke(resultManager,resultManagerType,"TimeSteps",typeof(double[]),
                    new[]{timeUnit},Enum.Parse(timeUnit,"Seconds",false));
                if(times==null || times.Length==0 || times.Length>10000)
                {seen.Status="TIME_STEPS_NOT_ACCEPTED";return;}
                int index=P16CalculatedFlowDiagnostic.SelectTime(times);
                seen.TimeSeconds=times[index];
                Type unitized=Type("Haestad.Domain","Haestad.Domain.IUnitizedField");
                Type unitIndex=Type("Haestad.Support","Haestad.Support.Units.UnitConversionManager+UnitIndex");
                if(!unitized.IsInstanceOfType(flow)){seen.Status="UNIT_NOT_AVAILABLE";return;}
                object working=calls.Get(flow,unitized,"WorkingUnitIndex",unitIndex);
                object storage=calls.Get(flow,unitized,"StorageUnitIndex",unitIndex);
                string unitName=Enum.GetName(unitIndex,working);
                seen.WorkingUnitIdentity=unitIndex.FullName+":"+unitName+":"+Convert.ToInt64(working,CultureInfo.InvariantCulture);
                seen.StorageUnitIdentity=unitIndex.FullName+":"+Enum.GetName(unitIndex,storage)+":"+Convert.ToInt64(storage,CultureInfo.InvariantCulture);
                seen.UnitSymbol=P16AzadeganReadOnlyResultHost.FlowUnitSymbol(unitName);
                if(string.IsNullOrEmpty(seen.UnitSymbol)){seen.Status="UNIT_UNVERIFIED";return;}
                seen.ResultReadInvocations=1;
                object raw=calls.Invoke(flow,variantType,"GetValue",typeof(object),
                    new[]{typeof(int),typeof(int),typeof(int)},seen.ElementId,seen.ScenarioId,index);
                seen.ValueType=raw?.GetType().FullName??"null";
                if(raw is double && !double.IsNaN((double)raw) && !double.IsInfinity((double)raw))
                    seen.RawValue=((double)raw).ToString("R",CultureInfo.InvariantCulture);
                else if(raw is float && !float.IsNaN((float)raw) && !float.IsInfinity((float)raw))
                    seen.RawValue=((float)raw).ToString("R",CultureInfo.InvariantCulture);
                if(seen.RawValue==null){seen.Status="FLOW_VALUE_NOT_FINITE";return;}
                seen.Provenance = "EVENT:"+seen.CalculationEvent+"|MODEL:"+seen.ModelPath+
                    "|SCENARIO:1|OPTIONS:29|ENGINE:"+seen.Engine+"|FIELD:"+seen.Field+
                    "|RECORD:"+Record+"|ELEMENT:"+seen.ElementId+"("+seen.ElementLabel+")|TIME:"+
                    seen.TimeSeconds.Value.ToString("R",CultureInfo.InvariantCulture)+"s|UNIT:"+
                    seen.WorkingUnitIdentity+"|FLOW:"+seen.RawValue+
                    "|SAVED_MODEL_HASHES_MATCHED:true|SOLVER_CALLS_THIS_READ:0";
                seen.Status="FARZANEGAN_NATIVE_FLOW_READBACK_VERIFIED";
                evidence.Diagnostics["NativePersistedEndpointsJson"] = RuntimeHostProtocol.Serializer().Serialize(P14FarzaneganTestOnlyHost.Snapshot(dataset));
                try
                {
                    evidence.Diagnostics["NativePersistedSectionsJson"] = RuntimeHostProtocol.Serializer().Serialize(
                        FarzaneganHydraulicRemediationHost.PersistedSections(dataset, scenario));
                }
                catch (Exception sectionError)
                {
                    evidence.Diagnostics["NativePersistedSectionsStatus"] = "NOT_QUALIFIED:" + sectionError.GetType().FullName;
                }
                // Optional research metrics must not invalidate the qualified P16 read contract.
                // Catalog metadata availability is not yet proven for this installed runtime.
                try { FarzaneganHydraulicRemediationHost.ReadExtended(dataset, project, evidence); }
                catch (Exception optionalError)
                {
                    evidence.Diagnostics["RemediationExtendedReadStatus"] = "NOT_QUALIFIED";
                    evidence.Diagnostics["RemediationExtendedReadError"] = optionalError.GetType().FullName;
                }
                ReadNetworkHydraulicResults(dataset, project, seen, calls);
            }
            catch(Exception error)
            {
                Exception exact=error is TargetInvocationException && error.InnerException!=null?error.InnerException:error;
                seen.Status="FARZANEGAN_READBACK_BLOCKED";
                seen.ErrorType=exact.GetType().FullName;
                seen.ErrorMessage=(exact.Message??"").Substring(0,Math.Min(800,(exact.Message??"").Length));
            }
            finally
            {
                try
                {
                    if(resultManager!=null && resultManagerType!=null)
                        calls.Invoke(resultManager,resultManagerType,"Close",typeof(void),System.Type.EmptyTypes);
                    seen.ResultCleanupCompleted=true;
                }
                catch(Exception cleanup)
                {
                    seen.ResultCleanupCompleted=false;
                    seen.Status="RESULT_MANAGER_CLEANUP_FAILED";
                    seen.ErrorMessage=cleanup.GetType().Name;
                }
                evidence.Diagnostics[EvidenceKey]=RuntimeHostProtocol.Serializer().Serialize(seen);
                evidence.Diagnostics["FarzaneganReadStatus"]=seen.Status;
                if(seen.Status=="FARZANEGAN_NATIVE_FLOW_READBACK_VERIFIED" && seen.ResultCleanupCompleted)
                {
                    evidence.Diagnostics[PassFlag]="true";
                    evidence.Diagnostics["HydraulicResultReads"]="1";
                    evidence.Diagnostics["CalculationFreshnessVerified"]="true";
                }
                else if(evidence.FailureCode==null) evidence.FailureCode="FARZANEGAN_READBACK_NOT_ACCEPTED";
            }
        }

        private static List<int> ReadElementIds(object dataset, string managerName, P16AzadeganCalculationHost.Vendor calls)
        {
            Type elementType = Type("Haestad.Domain", "Haestad.Domain.DomainElementType");
            Type dataType = Type("Haestad.Domain", "Haestad.Domain.IDomainDataSet");
            Type domainManagerType = Type("Haestad.Domain", "Haestad.Domain.IDomainElementManager");
            object enumValue = WaterObjectsLifecycleHost.EnumMember(elementType, managerName);
            if (enumValue == null) return new List<int>();
            object manager = calls.Invoke(dataset, dataType, "DomainElementManager", domainManagerType,
                new[] { typeof(int) }, Convert.ToInt32(enumValue, CultureInfo.InvariantCulture));
            if (manager == null) throw new InvalidOperationException("Network result manager unavailable: " + managerName);
            var idsMethod = WaterObjectsLifecycleHost.ElementIDs(manager.GetType());
            if (idsMethod == null) throw new InvalidOperationException("ElementIDs API unavailable: " + managerName);
            object ids = idsMethod.Invoke(manager, null);
            if (!(ids is IEnumerable enumerable)) throw new InvalidOperationException("ElementIDs is not enumerable: " + managerName);
            return enumerable.Cast<object>().Select(x => Convert.ToInt32(x, CultureInfo.InvariantCulture))
                .Distinct().OrderBy(x => x).ToList();
        }

        private static void ReadNetworkHydraulicResults(object dataset, object project, Observation seen,
            P16AzadeganCalculationHost.Vendor calls)
        {
            try
            {
                var conduits = ReadElementIds(dataset, "ConduitElementManager", calls);
                var manholes = ReadElementIds(dataset, "ManholeElementManager", calls);
                var outfalls = ReadElementIds(dataset, "OutfallElementManager", calls);
                var structures = manholes.Select(id => new StructureReadDto { Id = id, RuntimeCategory = "Manhole" })
                    .Concat(outfalls.Select(id => new StructureReadDto { Id = id, RuntimeCategory = "Outfall" }))
                    .OrderBy(n => n.Id).ToList();
                seen.NetworkConduitCount = conduits.Count;
                seen.NetworkStructureCount = structures.Count;
                if (conduits.Count != 71 || structures.Count != 73 || manholes.Count != 71 || outfalls.Count != 2)
                    throw new InvalidDataException("Unexpected Farzanegan network inventory: conduits=" + conduits.Count +
                        ", manholes=" + manholes.Count + ", outfalls=" + outfalls.Count + ".");
                var observations = WaterObjectsResultReader.ReadNetworkObservations(dataset, project, seen.ScenarioId,
                    seen.Engine, conduits, structures);
                seen.NetworkResultObservationCount = observations.Count;
                seen.NetworkResultsJson = RuntimeHostProtocol.Serializer().Serialize(observations);
                seen.NetworkResultReadStatus = observations.Count == 499 ? "READ_COMPLETED_499_OBSERVATIONS" :
                    "OBSERVATION_COUNT_MISMATCH_499_EXPECTED";
            }
            catch (Exception error)
            {
                Exception exact = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                seen.NetworkResultReadStatus = "BLOCKED";
                seen.NetworkResultReadError = (exact.GetType().FullName + ": " + (exact.Message ?? string.Empty))
                    .Substring(0, Math.Min(1000, (exact.GetType().FullName + ": " + (exact.Message ?? string.Empty)).Length));
            }
        }
    }
}
