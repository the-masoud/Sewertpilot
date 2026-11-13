using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    // Read-only qualification of the native catalog-size selection path.
    //
    // Purpose: prove (or refute) that the per-conduit catalog size selector - the persisted
    // CatalogPipeType_CatalogPipeSize reference - is reachable and writable through a supported
    // native field, and that the selected entry is the geometry the solver consumes.
    //
    // This host performs NO mutation, NO save and NO solver invocation. It records what the
    // installed runtime actually exposes instead of inferring capability from reflection metadata.
    internal static class P14CatalogSelectorDiscoveryHost
    {
        internal const string Operation = "P14CatalogSelectorProbe";
        internal const string LaunchSwitch = "--p14-catalog-selector-probe";
        internal const string EvidenceKey = "P14CatalogSelectorProbeJson";
        internal const string PassFlag = "P14CatalogSelectorQualified";

        internal const string SizeField = "CatalogPipeType_CatalogPipeSize";
        internal const string PipeIdField = "CatalogPipeType_CatalogPipeID";
        internal const string SizeCollectionField = "ClassSizeCollection";
        internal const string SizeCollectionCountField = "ClassSizeCollectionCount";
        internal const string SizeGuidField = "ClassSizeCollection_Guid";
        internal const string SizeLabelField = "ClassSizeCollection_Label";
        internal const string SizeInsideDiameterField = "ClassSizeCollection_InsideDiameter";
        internal const string SizeManningsNField = "ClassSizeCollection_ManningsN";
        internal const string SizeAvailableForDesignField = "ClassSizeCollection_AvailableForDesign";

        internal const int TargetConduitId = 150;
        internal const int TargetCatalogPipeId = 125;
        internal const int ExpectedConduitCount = 71;

        internal sealed class FieldContract
        {
            public string RequestedName { get; set; }
            public string Resolved { get; set; }
            public string RuntimeType { get; set; }
            public string FieldName { get; set; }
            public string FieldLabel { get; set; }
            public string FieldDataType { get; set; }
            public string DeclaringInterface { get; set; }
            public string LocalSetter { get; set; }
            public string InterfaceSetter { get; set; }
            public string ActiveAlternativeId { get; set; }
            public string ReadValue { get; set; }
            public string ReadValueType { get; set; }
            public string ErrorType { get; set; }
            public string ErrorMessage { get; set; }
        }

        internal sealed class SizeEntry
        {
            public string Guid { get; set; }
            public string Label { get; set; }
            public string InsideDiameterFt { get; set; }
            public string ManningsN { get; set; }
            public string AvailableForDesign { get; set; }
        }

        internal sealed class ConduitLink
        {
            public int Id { get; set; }
            public string PhysicalDiameterFt { get; set; }
            public string CatalogPipeId { get; set; }
            public string SelectedSizeGuid { get; set; }
            public string SelectedSizeLabel { get; set; }
            public string SelectedSizeInsideDiameterFt { get; set; }
            public string NativeShapeLabel { get; set; }
            public bool UseLocalShapeLabel { get; set; }
            public string Agreement { get; set; }
        }

        internal sealed class Observation
        {
            public string Status { get; set; } = "NOT_STARTED";
            public string ModelPath { get; set; }
            public int ScenarioId { get; set; }
            public int OptionsId { get; set; }
            public string Engine { get; set; }
            public int ConduitCount { get; set; }
            public int CatalogPipeCount { get; set; }
            public FieldContract SizeSelectionField { get; set; } = new FieldContract();
            public FieldContract PipeIdField { get; set; } = new FieldContract();
            public string SizeCollectionFieldType { get; set; }
            public string SizeCollectionStatus { get; set; }
            public string SizeCollectionError { get; set; }
            public int SizeEntryCount { get; set; }
            public List<SizeEntry> SizeEntries { get; set; } = new List<SizeEntry>();
            public string TwelveInchGuid { get; set; }
            public string TwelveInchLabel { get; set; }
            public string TwelveInchInsideDiameterFt { get; set; }
            public string TwelveInchAvailableForDesign { get; set; }
            public ConduitLink Target { get; set; } = new ConduitLink();
            public List<ConduitLink> NetworkLinkage { get; set; } = new List<ConduitLink>();
            public int LinkageAgreementCount { get; set; }
            public int LinkageDisagreementCount { get; set; }
            public string LinkageConclusion { get; set; }
            public string SelectionWriteMechanism { get; set; }
            public List<string> FieldInventory { get; set; } = new List<string>();
            public List<string> ResolutionAttempts { get; set; } = new List<string>();
            public string CollectionReadType { get; set; }
            public string CollectionReadSummary { get; set; }
            public string CollectionListManagerType { get; set; }
            public string SupportElementTypeIdentity { get; set; }
            public string SupportElementManagerStatus { get; set; }
            public int ConduitDomainElementTypeId { get; set; }
            public int PhysicalAlternativeTypeId { get; set; }
            public int PhysicalAlternativeId { get; set; }
            public bool SizeFieldDeclaredInAnyRegistry { get; set; }
            public string PublicApiGap { get; set; }
            public string Qualification { get; set; } = "UNQUALIFIED";
            public string QualificationReason { get; set; }
            public int MutationCalls { get; set; }
            public int SolverCalls { get; set; }
            public int SaveCalls { get; set; }
            public string ErrorType { get; set; }
            public string ErrorMessage { get; set; }
        }

        internal static void ValidateRequest(HostRequest request)
        {
            if (request == null || request.Operation != Operation || request.SchemaVersion != 1 ||
                request.EngineeringPlan != null || request.WriteAuthorizationTokenId != null ||
                request.WriteAuthorizationScope != null || request.WriteAuthorizationTargetWorkingPath != null ||
                request.Diagnostics == null || request.Diagnostics.Count != 5 ||
                !request.Diagnostics.TryGetValue("P14CatalogProbeCopyId", out string copyId) ||
                !System.Text.RegularExpressions.Regex.IsMatch(copyId ?? "", "^[a-f0-9]{32}$") ||
                request.WorkingCopyId != copyId ||
                !request.Diagnostics.TryGetValue("P14CatalogProbeScenario", out string scenario) || scenario != "1" ||
                !request.Diagnostics.TryGetValue("P14CatalogProbeOptions", out string options) || options != "29" ||
                !request.Diagnostics.TryGetValue("P14CatalogProbeEngine", out string engine) ||
                engine != P16FarzaneganNativePilotHost.Engine ||
                !request.Diagnostics.TryGetValue("P14CatalogProbeInputSHA256", out string digest) ||
                !System.Text.RegularExpressions.Regex.IsMatch(digest ?? "", "^[A-F0-9]{64}$"))
                throw new InvalidDataException("Catalog selector probe requires an exact bounded read-only model context.");
            string path = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "ReadOnly", copyId,
                P16FarzaneganNativePilotHost.ModelFileName);
            if (!P16SaveQuestionPolicy.SamePath(path, request.WorkingModelPath))
                throw new InvalidDataException("Catalog selector probe must target its own disposable read-only copy.");
            RuntimeHostProtocol.ControlledDirectory(Path.GetDirectoryName(path), "ReadOnly");
            WorkingCopyIntegrity.RejectLinks(path);
            if (P16FarzaneganNativePilotHost.Family.Any(name => !File.Exists(Path.Combine(Path.GetDirectoryName(path), name))))
                throw new InvalidDataException("The read-only Farzanegan model family is incomplete.");
        }

        private static object Get(object instance, string name)
        {
            var property = instance.GetType().GetProperty(name);
            if (property == null || property.GetGetMethod() == null)
                throw new MissingMemberException(instance.GetType().FullName, name);
            return property.GetValue(instance, null);
        }

        private static string Text(object value) => value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        // Describes the field object the installed runtime actually returned, including which
        // supported setter signatures it exposes. Metadata alone is never treated as a qualification.
        private static void Describe(FieldContract contract, string requested, object field)
        {
            contract.RequestedName = requested;
            if (field == null) { contract.Resolved = "false"; return; }
            contract.Resolved = "true";
            Type type = field.GetType();
            contract.RuntimeType = type.FullName;
            contract.FieldName = Text(type.GetProperty("Name")?.GetValue(field, null));
            contract.FieldLabel = Text(type.GetProperty("Label")?.GetValue(field, null));
            contract.FieldDataType = Text(type.GetProperty("FieldDataType")?.GetValue(field, null));
            var local = type.GetMethod("SetValue", new[] { typeof(int), typeof(bool), typeof(object) });
            contract.LocalSetter = local == null ? "ABSENT"
                : (local.IsStatic || local.ReturnType != typeof(void)) ? "INVALID_SIGNATURE"
                : "SetValue(int elementId, bool, object value)";
            var alternate = type.GetMethod("SetValue", new[] { typeof(int), typeof(object) });
            contract.InterfaceSetter = alternate == null ? "ABSENT" : "SetValue(int elementId, object value)";
            var declaring = type.GetInterfaces()
                .Where(i => i.GetMethod("SetValue", new[] { typeof(int), typeof(object) }) != null)
                .Select(i => i.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            contract.DeclaringInterface = declaring.Length == 0 ? "NONE" : string.Join(",", declaring);
            contract.ActiveAlternativeId = Text(type.GetProperty("ActiveAlternativeID")?.GetValue(field, null));
        }

        private static void ReadValue(FieldContract contract, object field, int id)
        {
            if (field == null) return;
            try
            {
                object raw = field.GetType().GetMethod("GetValue", new[] { typeof(int) }).Invoke(field, new object[] { id });
                contract.ReadValue = Text(raw);
                contract.ReadValueType = raw?.GetType().FullName ?? "null";
            }
            catch (Exception error)
            {
                Exception exact = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                contract.ErrorType = exact.GetType().FullName;
                contract.ErrorMessage = (exact.Message ?? string.Empty).Substring(0, Math.Min(400, (exact.Message ?? string.Empty).Length));
            }
        }

        // Reads every value of a native list field into a row-id keyed map. Read-only.
        private static Dictionary<int, string> Map(object field)
        {
            var map = new Dictionary<int, string>();
            if (field == null) return map;
            var values = field.GetType().GetMethod("GetValues", Type.EmptyTypes)?.Invoke(field, null) as IDictionary;
            if (values == null) return map;
            foreach (DictionaryEntry entry in values)
                map[Convert.ToInt32(entry.Key, CultureInfo.InvariantCulture)] = Text(entry.Value);
            return map;
        }

        internal static void Execute(object dataset, object project, HostRequest request, ModelReadEvidence evidence)
        {
            var seen = new Observation
            {
                ModelPath = request.WorkingModelPath,
                ScenarioId = P16FarzaneganNativePilotHost.ScenarioId,
                Engine = P16FarzaneganNativePilotHost.Engine
            };
            try
            {
                string inventory = P16AzadeganCalculationHost.InventoryJson(
                    P16AzadeganCalculationHost.Inventory(Path.GetDirectoryName(request.WorkingModelPath)));
                if (P16AzadeganCalculationHost.InventoryDigest(
                        P16AzadeganCalculationHost.Inventory(Path.GetDirectoryName(request.WorkingModelPath))) !=
                    request.Diagnostics["P14CatalogProbeInputSHA256"])
                { seen.Status = "INPUT_HASH_MISMATCH"; return; }

                Type dataType = Contract("Haestad.Domain", "Haestad.Domain.IDomainDataSet");
                Type scenarioManagerType = Contract("Haestad.Domain", "Haestad.Domain.IScenarioManager");
                Type elementManagerType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElementManager");
                Type elementType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElement");
                Type scenarioType = Contract("Haestad.Domain", "Haestad.Domain.IScenario");
                Type domainManagerType = Contract("Haestad.Domain", "Haestad.Domain.IDomainElementManager");
                var calls = new P16AzadeganCalculationHost.Vendor();

                object scenarioManager = calls.Get(dataset, dataType, "ScenarioManager", scenarioManagerType);
                int active = (int)calls.Get(scenarioManager, scenarioManagerType, "ActiveScenarioID", typeof(int));
                if (active != seen.ScenarioId) { seen.Status = "ACTIVE_SCENARIO_MISMATCH"; return; }
                object scenario = calls.Invoke(scenarioManager, elementManagerType, "Element", elementType,
                    new[] { typeof(int) }, seen.ScenarioId);
                seen.OptionsId = (int)calls.Invoke(scenario, scenarioType, "CalculationOptionsID", typeof(int),
                    new[] { typeof(string) }, seen.Engine);
                if (seen.OptionsId != 29) { seen.Status = "OPTIONS_MISMATCH"; return; }

                object conduitManager = P14FarzaneganTestOnlyHost.Manager(dataset);
                if (conduitManager == null) { seen.Status = "CONDUIT_MANAGER_UNAVAILABLE"; return; }
                seen.ConduitCount = P14FarzaneganTestOnlyHost.Snapshot(dataset).Length;
                if (seen.ConduitCount != ExpectedConduitCount) { seen.Status = "CONDUIT_COUNT_MISMATCH"; return; }

                // 1. Field-level contracts for the persisted catalog selection. The catalog reference
                //    is alternative-scoped, so the plain element-manager lookup is only one candidate.
                int physicalAlternativeTypeId = ScenarioAlternativeIsolationHost.ResolvePhysicalAlternativeTypeId(dataset);
                int physicalAlternativeId = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(scenario, physicalAlternativeTypeId);
                seen.PhysicalAlternativeTypeId = physicalAlternativeTypeId;
                seen.PhysicalAlternativeId = physicalAlternativeId;

                object fieldManager = calls.Get(dataset, dataType, "FieldManager", Contract("Haestad.Domain", "Haestad.Domain.IFieldManager"));
                object firstConduit = conduitManager.GetType().GetMethod("Element", new[] { typeof(int) }).Invoke(conduitManager, new object[] { TargetConduitId });
                int elementTypeId = Convert.ToInt32(Get(firstConduit, "DomainElementTypeID"), CultureInfo.InvariantCulture);
                seen.ConduitDomainElementTypeId = elementTypeId;
                Inventory(seen, fieldManager, "DomainElementFields(elementType=" + elementTypeId + ")", elementTypeId, -1);
                Inventory(seen, fieldManager, "DomainElementFields(elementType=" + elementTypeId + ",physicalAlt=" + physicalAlternativeId + ")",
                    elementTypeId, physicalAlternativeId);
                InventoryAlternative(seen,fieldManager,"AlternativeFields(exactPhysicalContext)",physicalAlternativeTypeId,elementTypeId);
                seen.SizeFieldDeclaredInAnyRegistry = P14CatalogPointerPersistenceHost.RegistryReportsSize(seen.FieldInventory);

                object sizeField = ResolveField(seen, fieldManager, dataset, conduitManager, SizeField, elementTypeId,
                    physicalAlternativeTypeId, physicalAlternativeId);
                Describe(seen.SizeSelectionField, SizeField, sizeField);
                ReadValue(seen.SizeSelectionField, sizeField, TargetConduitId);

                object pipeIdField = ResolveField(seen, fieldManager, dataset, conduitManager, PipeIdField, elementTypeId,
                    physicalAlternativeTypeId, physicalAlternativeId);
                Describe(seen.PipeIdField, PipeIdField, pipeIdField);
                ReadValue(seen.PipeIdField, pipeIdField, TargetConduitId);

                // 2. Enumerate the shared catalog through the native support element manager.
                var byGuid = new Dictionary<string, SizeEntry>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    Type supportType = Contract("Haestad.Domain", "Haestad.Domain.SupportElementType");
                    object catalogType = WaterObjectsLifecycleHost.EnumMember(supportType, "CatalogConduitElementManager");
                    if (catalogType == null) throw new MissingMemberException(supportType.FullName, "CatalogConduitElementManager");
                    Type supportManagerType = Contract("Haestad.Domain", "Haestad.Domain.ISupportElementManager");
                    object catalogManager = calls.Invoke(dataset, dataType, "SupportElementManager", supportManagerType,
                        new[] { typeof(int) }, Convert.ToInt32(catalogType, CultureInfo.InvariantCulture));
                    if (catalogManager == null) throw new InvalidOperationException("Catalog conduit support element manager unavailable.");
                    seen.CatalogPipeCount = (int)calls.Get(catalogManager,
                        Contract("Haestad.Support", "Haestad.Support.Support.IListManager"), "Count", typeof(int));

                    object collection = calls.Invoke(catalogManager, supportManagerType, "SupportElementField",
                        Contract("Haestad.Support", "Haestad.Support.Support.IField"),
                        new[] { typeof(string) }, SizeCollectionField);
                    seen.SizeCollectionFieldType = collection?.GetType().FullName ?? "null";
                    if (collection == null) throw new InvalidOperationException("ClassSizeCollection field is absent on the catalog conduit manager.");

                    // The collection field exposes an empty list manager through the supported
                    // ICollectionField contract; the row values themselves are read per column.
                    Type collectionContract = Contract("Haestad.Domain", "Haestad.Domain.ICollectionField");
                    Type collectionType = Contract("Haestad.Domain", "Haestad.Domain.ICollectionFieldListManager");
                    object listManager = collectionContract.IsInstanceOfType(collection)
                        ? calls.Invoke(collection, collectionContract, "GetEmptyCollectionFieldListManager", collectionType, Type.EmptyTypes)
                        : null;
                    // The empty list manager has no rows; the rows for a specific catalog conduit are
                    // bound to that support element. Read them through the same supported contract.
                    object boundManager = collection.GetType().GetMethod("GetValue", new[] { typeof(int) })
                        ?.Invoke(collection, new object[] { TargetCatalogPipeId });
                    if (boundManager != null && collectionType.IsInstanceOfType(boundManager)) listManager = boundManager;
                    seen.CollectionListManagerType = listManager?.GetType().FullName ?? "null";

                    try
                    {
                        object sizeElementType = collection.GetType().GetProperty("SupportElementType")?.GetValue(collection, null);
                        seen.SupportElementTypeIdentity = sizeElementType == null ? "null"
                            : Text(sizeElementType.GetType().GetProperty("Id")?.GetValue(sizeElementType, null)) + "/" +
                              Text(sizeElementType.GetType().GetProperty("Name")?.GetValue(sizeElementType, null));
                    }
                    catch (Exception typeError)
                    {
                        seen.SupportElementTypeIdentity = "ERROR:" + typeError.GetType().Name;
                    }

                    Type fieldType = Contract("Haestad.Support", "Haestad.Support.Support.IField");
                    object guids = calls.Invoke(listManager, collectionType, "Field", fieldType, new[] { typeof(string) }, SizeGuidField);
                    object labels = calls.Invoke(listManager, collectionType, "Field", fieldType, new[] { typeof(string) }, SizeLabelField);
                    object diameters = calls.Invoke(listManager, collectionType, "Field", fieldType, new[] { typeof(string) }, SizeInsideDiameterField);
                    object mannings = calls.Invoke(listManager, collectionType, "Field", fieldType, new[] { typeof(string) }, SizeManningsNField);
                    object available = calls.Invoke(listManager, collectionType, "Field", fieldType, new[] { typeof(string) }, SizeAvailableForDesignField);
                    var guidMap = Map(guids);
                    var labelMap = Map(labels);
                    var diameterMap = Map(diameters);
                    var manningsMap = Map(mannings);
                    var availableMap = Map(available);
                    seen.CollectionReadType = "ICollectionFieldListManager.Field(name).GetValues()";
                    seen.CollectionReadSummary = "guid=" + guidMap.Count + ",label=" + labelMap.Count +
                        ",insideDiameter=" + diameterMap.Count + ",manningsN=" + manningsMap.Count;
                    foreach (var row in guidMap.OrderBy(pair => pair.Key))
                    {
                        var entry = new SizeEntry
                        {
                            Guid = row.Value,
                            Label = labelMap.TryGetValue(row.Key, out string label) ? label : null,
                            InsideDiameterFt = diameterMap.TryGetValue(row.Key, out string diameter) ? diameter : null,
                            ManningsN = manningsMap.TryGetValue(row.Key, out string n) ? n : null,
                            AvailableForDesign = availableMap.TryGetValue(row.Key, out string flag) ? flag : null
                        };
                        seen.SizeEntries.Add(entry);
                        if (!string.IsNullOrEmpty(entry.Guid)) byGuid[entry.Guid] = entry;
                    }
                    seen.SizeEntryCount = seen.SizeEntries.Count;
                    seen.SizeCollectionStatus = "ENUMERATED";
                }
                catch (Exception error)
                {
                    Exception exact = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                    seen.SizeCollectionStatus = "BLOCKED";
                    seen.SizeCollectionError = (exact.GetType().FullName + ": " + (exact.Message ?? string.Empty))
                        .Substring(0, Math.Min(600, (exact.GetType().FullName + ": " + (exact.Message ?? string.Empty)).Length));
                }

                var twelve = seen.SizeEntries.FirstOrDefault(e => e.Label != null &&
                    e.Label.StartsWith("12", StringComparison.OrdinalIgnoreCase));
                seen.TwelveInchGuid = twelve?.Guid;
                seen.TwelveInchLabel = twelve?.Label;
                seen.TwelveInchInsideDiameterFt = twelve?.InsideDiameterFt;
                seen.TwelveInchAvailableForDesign = twelve?.AvailableForDesign;

                // 3. Prove the selector actually drives the physical geometry the solver consumes.
                var manager = P14FarzaneganTestOnlyHost.Manager(dataset);
                foreach (var row in P14FarzaneganTestOnlyHost.Snapshot(dataset))
                {
                    object conduit = manager.GetType().GetMethod("Element", new[] { typeof(int) }).Invoke(manager, new object[] { row.Id });
                    var link = new ConduitLink
                    {
                        Id = row.Id,
                        PhysicalDiameterFt = Number(Convert.ToDouble(Get(conduit, "ConduitDiameter"), CultureInfo.InvariantCulture)),
                        CatalogPipeId = CatalogId(conduit).ToString(CultureInfo.InvariantCulture),
                        NativeShapeLabel = Text(Get(conduit, "Physical_ConduitShapeLabel")),
                        UseLocalShapeLabel = Convert.ToBoolean(Get(conduit, "Physical_UseLocalConduitShapeLabel"), CultureInfo.InvariantCulture)
                    };
                    if (sizeField != null)
                    {
                        object raw = sizeField.GetType().GetMethod("GetValue", new[] { typeof(int) }).Invoke(sizeField, new object[] { row.Id });
                        link.SelectedSizeGuid = Text(raw);
                    }
                    if (link.SelectedSizeGuid != null && byGuid.TryGetValue(link.SelectedSizeGuid, out SizeEntry entry))
                    {
                        link.SelectedSizeLabel = entry.Label;
                        link.SelectedSizeInsideDiameterFt = entry.InsideDiameterFt;
                        bool agrees = double.TryParse(entry.InsideDiameterFt, NumberStyles.Float, CultureInfo.InvariantCulture, out double inside) &&
                                      Math.Abs(inside - double.Parse(link.PhysicalDiameterFt, CultureInfo.InvariantCulture)) < 1e-9;
                        link.Agreement = agrees ? "AGREES" : "DISAGREES";
                    }
                    else link.Agreement = "UNRESOLVED";
                    seen.NetworkLinkage.Add(link);
                }
                seen.LinkageAgreementCount = seen.NetworkLinkage.Count(l => l.Agreement == "AGREES");
                seen.LinkageDisagreementCount = seen.NetworkLinkage.Count(l => l.Agreement != "AGREES");
                seen.LinkageConclusion = seen.LinkageDisagreementCount == 0
                    ? "SELECTED_CATALOG_ENTRY_MATCHES_EFFECTIVE_DIAMETER_FOR_ALL_" + seen.ConduitCount + "_CONDUITS"
                    : "SELECTOR_AND_EFFECTIVE_DIAMETER_DISAGREE";
                seen.Target = seen.NetworkLinkage.Single(l => l.Id == TargetConduitId);

                // 4. Qualification is a conjunction of proven, not inferred, facts.
                var reasons = new List<string>();
                seen.PublicApiGap = seen.SizeFieldDeclaredInAnyRegistry
                    ? "NONE_OBSERVED"
                    : "The installed public domain API exposes ConduitTypeObject_CatalogConduitType.CatalogPipeType_CatalogPipeID only; " +
                      "no public member, field object or registered field named CatalogPipeType_CatalogPipeSize exists on the conduit element manager, " +
                      "the field manager, or any alternative field registry. The persisted catalog-size reference is therefore not writable " +
                      "through a supported public setter.";
                if (seen.SizeSelectionField.Resolved != "true") reasons.Add("size selector field is not resolvable on the conduit manager");
                if (seen.SizeSelectionField.LocalSetter == "ABSENT" && seen.SizeSelectionField.InterfaceSetter == "ABSENT")
                    reasons.Add("no supported setter signature on the size selector field");
                if (string.IsNullOrEmpty(seen.SizeSelectionField.ReadValue)) reasons.Add("current size selector value is unreadable");
                if (seen.SizeCollectionStatus != "ENUMERATED") reasons.Add("shared catalog sizes are not enumerable natively");
                if (string.IsNullOrEmpty(seen.TwelveInchGuid)) reasons.Add("no 12-inch catalog entry was observed");
                if (twelve != null && twelve.InsideDiameterFt != "1") reasons.Add("observed 12-inch entry is not 1.0 ft");
                if (twelve != null && twelve.AvailableForDesign != "True" && twelve.AvailableForDesign != "true")
                    reasons.Add("observed 12-inch entry is not available for design");
                if (seen.LinkageDisagreementCount != 0) reasons.Add("selector and effective diameter disagree on the current network");
                if (seen.Target.CatalogPipeId != TargetCatalogPipeId.ToString(CultureInfo.InvariantCulture))
                    reasons.Add("CO-40 is not on catalog pipe " + TargetCatalogPipeId);
                seen.SelectionWriteMechanism = seen.SizeSelectionField.LocalSetter != "ABSENT"
                    ? "IEditField SetValue(int elementId, bool, object value) on " + seen.SizeSelectionField.RuntimeType
                    : seen.SizeSelectionField.InterfaceSetter != "ABSENT"
                        ? "IEditField SetValue(int elementId, object value) on " + seen.SizeSelectionField.RuntimeType
                        : "NONE";
                seen.Qualification = reasons.Count == 0 ? "QUALIFIED" : "UNQUALIFIED";
                seen.QualificationReason = reasons.Count == 0
                    ? "Native selector field, shared 12-inch entry and selector-to-geometry linkage are all proven on the installed runtime."
                    : string.Join("; ", reasons);
                seen.Status = reasons.Count == 0 ? "CATALOG_SELECTOR_QUALIFIED" : "CATALOG_SELECTOR_UNQUALIFIED";
                seen.MutationCalls = 0;
                seen.SolverCalls = 0;
                seen.SaveCalls = 0;
            }
            catch (Exception error)
            {
                Exception exact = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                seen.Status = "CATALOG_SELECTOR_PROBE_BLOCKED";
                seen.ErrorType = exact.GetType().FullName;
                seen.ErrorMessage = (exact.Message ?? string.Empty).Substring(0, Math.Min(800, (exact.Message ?? string.Empty).Length));
            }
            finally
            {
                if(seen.Status=="CATALOG_SELECTOR_QUALIFIED" || seen.Status=="CATALOG_SELECTOR_UNQUALIFIED")
                    evidence.Diagnostics["NativePersistedEndpointsJson"]=RuntimeHostProtocol.Serializer().Serialize(P14FarzaneganTestOnlyHost.Snapshot(dataset));
                evidence.Diagnostics["NativeGvfCalculationMode"]=P14CatalogPointerPersistenceHost.GvfMode(dataset);
                evidence.Diagnostics[EvidenceKey] = RuntimeHostProtocol.Serializer().Serialize(seen);
                evidence.Diagnostics["P14CatalogSelectorStatus"] = seen.Status;
                // A completed probe is a successful read even when the answer is "not qualified".
                // Only a probe that could not reach a verdict is a failure.
                if (seen.Status == "CATALOG_SELECTOR_QUALIFIED")
                {
                    evidence.Diagnostics[PassFlag] = "true";
                    evidence.Diagnostics["P14CatalogSelectorProbeCompleted"] = "true";
                }
                else if (seen.Status == "CATALOG_SELECTOR_UNQUALIFIED")
                    evidence.Diagnostics["P14CatalogSelectorProbeCompleted"] = "true";
                else if (evidence.FailureCode == null) evidence.FailureCode = "P14_CATALOG_SELECTOR_PROBE_NOT_COMPLETED";
            }
        }

        private static int CatalogId(object conduit)
        {
            var method = conduit.GetType().GetMethod("ConduitTypeObject", Type.EmptyTypes);
            object variant = method?.Invoke(conduit, null);
            if (variant?.GetType().FullName != "Haestad.Domain.ModelingObjects.Dynamic.ConduitTypeObject_CatalogConduitType")
                throw new InvalidDataException("Native conduit must use the proven catalog type variant.");
            return (int)Get(variant, "CatalogPipeType_CatalogPipeID");
        }

        // Enumerates a native field registry deterministically (Count + indexed Item) and records
        // whether the persisted catalog-size field is declared there at all.
        private static List<string> Names(object fields)
        {
            var names = new List<string>();
            if (fields == null) return names;
            Type type = fields.GetType();
            object countValue = type.GetProperty("Count")?.GetValue(fields, null);
            int count = countValue == null ? 0 : Convert.ToInt32(countValue, CultureInfo.InvariantCulture);
            PropertyInfo item = type.GetProperty("Item", new[] { typeof(int) });
            if (item == null) return names;
            for (int i = 0; i < count && i < 4096; i++)
            {
                object field = item.GetValue(fields, new object[] { i });
                names.Add(Text(field?.GetType().GetProperty("Name")?.GetValue(field, null)) ?? "");
            }
            return names;
        }

        private static void Inventory(Observation seen, object fieldManager, string label, int domainElementTypeId, int alternativeId)
        {
            if (fieldManager == null) return;
            try
            {
                object fields = alternativeId < 0
                    ? fieldManager.GetType().GetMethod("DomainElementFields", new[] { typeof(int) })
                        ?.Invoke(fieldManager, new object[] { domainElementTypeId })
                    : fieldManager.GetType().GetMethod("DomainElementFields", new[] { typeof(int), typeof(int) })
                        ?.Invoke(fieldManager, new object[] { domainElementTypeId, alternativeId });
                if (fields == null) { seen.FieldInventory.Add(label + "|UNRESOLVED"); return; }
                var names = Names(fields);
                bool hasSize = names.Any(n => string.Equals(n, SizeField, StringComparison.Ordinal));
                seen.FieldInventory.Add(label + "|count=" + names.Count + "|hasSizeField=" + hasSize + "|catalog=" +
                    string.Join(",", names.Where(n => n.IndexOf("Catalog", StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderBy(n => n, StringComparer.Ordinal)));
            }
            catch (Exception error)
            {
                Exception exact = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                seen.FieldInventory.Add(label + "|ERROR:" + exact.GetType().Name);
            }
        }

        private static void InventoryAlternative(Observation seen, object fieldManager, string label, int alternativeTypeId, int domainElementTypeId)
        {
            if (fieldManager == null) return;
            try
            {
                object fields = fieldManager.GetType().GetMethod("AlternativeFields", new[] { typeof(int), typeof(int), typeof(int) })
                    ?.Invoke(fieldManager, new object[] { alternativeTypeId, domainElementTypeId, seen.PhysicalAlternativeId });
                if (fields == null) { seen.FieldInventory.Add(label + "|UNRESOLVED"); return; }
                var names = Names(fields);
                bool hasSize = names.Any(n => string.Equals(n, SizeField, StringComparison.Ordinal));
                if (names.Count == 0 && !hasSize) return;
                seen.FieldInventory.Add(label + "|count=" + names.Count + "|hasSizeField=" + hasSize + "|catalog=" +
                    string.Join(",", names.Where(n => n.IndexOf("Catalog", StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderBy(n => n, StringComparer.Ordinal)));
            }
            catch (Exception error)
            {
                Exception exact = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                seen.FieldInventory.Add(label + "|ERROR:" + exact.GetType().Name);
            }
        }

        // Tries every supported resolution route in a fixed order and records exactly which one
        // produced a field object. No setter is invoked here.
        private static object ResolveField(Observation seen, object fieldManager, object dataset, object conduitManager,
            string name, int elementTypeId, int alternativeTypeId, int alternativeId)
        {
            object resolved = null;
            void Attempt(string label, Func<object> action)
            {
                if (resolved != null) return;
                try
                {
                    object value = action();
                    seen.ResolutionAttempts.Add(label + "=" + (value == null ? "NULL" : value.GetType().FullName));
                    if (value != null) resolved = value;
                }
                catch (Exception error)
                {
                    Exception exact = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                    seen.ResolutionAttempts.Add(label + "=ERROR:" + exact.GetType().Name);
                }
            }

            Attempt("conduitManager.DomainElementField(name,Physical)",()=>P14NativeConduitLabelFormatter.PhysicalField(conduitManager,name));
            if(fieldManager!=null)
                Attempt("fieldManager.AlternativeField(exactPhysicalContext)",()=>fieldManager.GetType().GetMethod("AlternativeField",new[]{typeof(string),typeof(int),typeof(int),typeof(int)})
                    .Invoke(fieldManager,new object[]{name,alternativeTypeId,elementTypeId,alternativeId}));
            // Generic alternative fallback is reported honestly; it is not preferred over a scoped field.
            Attempt("alternative.AlternativeField(name,elementType=" + elementTypeId + ")",
                () =>
                {
                    Type dataType = Contract("Haestad.Domain", "Haestad.Domain.IDomainDataSet");
                    Type managerType = Contract("Haestad.Domain", "Haestad.Domain.IAlternativeManager");
                    Type alternativeType = Contract("Haestad.Domain", "Haestad.Domain.IAlternative");
                    Type fieldType = Contract("Haestad.Support", "Haestad.Support.Support.IField");
                    object manager = dataset.GetType().GetMethod("AlternativeManager", new[] { typeof(int) })
                        .Invoke(dataset, new object[] { alternativeTypeId });
                    if (manager == null) return null;
                    object alternative = manager.GetType().GetMethod("Element", new[] { typeof(int) })
                        .Invoke(manager, new object[] { alternativeId });
                    if (alternative == null || !alternativeType.IsInstanceOfType(alternative)) return null;
                    return alternativeType.GetMethod("AlternativeField", new[] { typeof(string), typeof(int) })
                        .Invoke(alternative, new object[] { name, elementTypeId });
                });
            if (fieldManager != null)
            {
                Attempt("fieldManager.AlternativeField(name,altType=" + alternativeTypeId + ",elementType=" + elementTypeId + ",altId=" + alternativeId + ")",
                    () => fieldManager.GetType().GetMethod("AlternativeField", new[] { typeof(string), typeof(int), typeof(int), typeof(int) })
                        ?.Invoke(fieldManager, new object[] { name, alternativeTypeId, elementTypeId, alternativeId }));
                Attempt("fieldManager.AlternativeField(name,elementType=" + elementTypeId + ",altType=" + alternativeTypeId + ",altId=" + alternativeId + ")",
                    () => fieldManager.GetType().GetMethod("AlternativeField", new[] { typeof(string), typeof(int), typeof(int), typeof(int) })
                        ?.Invoke(fieldManager, new object[] { name, elementTypeId, alternativeTypeId, alternativeId }));
            }
            Attempt("conduitManager.DomainElementField(name)",
                () => conduitManager.GetType().GetMethod("DomainElementField", new[] { typeof(string) })?.Invoke(conduitManager, new object[] { name }));
            return resolved;
        }

        private static Type Contract(string assemblyName, string typeName)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && a.GetName().Name == assemblyName).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Vendor contract assembly is missing or ambiguous: " + assemblyName);
            return matches[0].GetType(typeName, true, false);
        }
    }
}
