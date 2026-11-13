using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using SewerPilot.OpenFlows.Contracts;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class WaterObjectsFlowResult
    {
        public string EngineUsed { get; set; }
        public bool ResultManagerAvailable { get; set; }
        public bool HasResults { get; set; }
        public bool FlowFieldSupported { get; set; }
        public bool FlowFieldHasResults { get; set; }
        public int? ElementId { get; set; }
        public double? Value { get; set; }
        public string RawValue { get; set; }
        public string ValueClrType { get; set; }
        public double? TimeSeconds { get; set; }
        public int? TimeStepIndex { get; set; }
        public string WorkingUnitSymbol { get; set; }
        public string WorkingUnitIdentity { get; set; }
        public string Classification { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WaterObjectsMetricResult
    {
        public string MetricName { get; set; }
        public bool ResultManagerAvailable { get; set; }
        public bool HasResults { get; set; }
        public string FieldName { get; set; }
        public string RecordName { get; set; }
        public string EngineName { get; set; }
        public bool FieldSupported { get; set; }
        public bool FieldHasResults { get; set; }
        public int? ElementId { get; set; }
        public double? NumericValue { get; set; }
        public string RawValue { get; set; }
        public string ValueClrType { get; set; }
        public double? TimeSeconds { get; set; }
        public int? TimeStepIndex { get; set; }
        public string WorkingUnitSymbol { get; set; }
        public string WorkingUnitIdentity { get; set; }
        public string Semantics { get; set; }
        public string DatumSemantics { get; set; }
        public string Classification { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
    }

    [Serializable]
    public class HydraulicDataContractException : InvalidOperationException
    {
        public HydraulicDataContractException() { }
        public HydraulicDataContractException(string message) : base(message) { }
        public HydraulicDataContractException(string message, Exception innerException) : base(message, innerException) { }
        protected HydraulicDataContractException(
            System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    public interface IElementIdentityEnumerable
    {
        IEnumerable<ElementIdentity> GetElementIdentities();
    }

    public sealed class WaterObjectsActiveModel : IOpenFlowsModel, IElementIdentityEnumerable
    {
        public ModelIdentity Identity { get; }
        public object DomainDataSet { get; }
        public object DomainProject { get; }
        public IReadOnlyList<ElementIdentity> ElementIdentities { get; }

        public WaterObjectsActiveModel(
            ModelIdentity identity,
            object domainDataSet = null,
            object domainProject = null,
            IEnumerable<ElementIdentity> elements = null)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            DomainDataSet = domainDataSet;
            DomainProject = domainProject;
            ElementIdentities = elements?.Where(e => e != null).Distinct().OrderBy(e => e.Value, StringComparer.Ordinal).ToList().AsReadOnly()
                ?? (IReadOnlyList<ElementIdentity>)new List<ElementIdentity>().AsReadOnly();
        }

        public IEnumerable<ElementIdentity> GetElementIdentities() => ElementIdentities;

        public void Dispose() { }
    }

    internal sealed class WaterObjectsResultReader : IOpenFlowsResultReader
    {
        public const string EngineName = "CivilStormDynamicEngine";
        public const string FlowFieldName = "BasicFlowResults_Flow";
        public const string FlowRecordName = "BasicFlowResults";
        private const string NameScenarioManager = "Scenario" + "Manager";

        private readonly object _activeDataset;
        private readonly object _activeProject;

        public WaterObjectsResultReader() { }

        public WaterObjectsResultReader(object activeDataset, object activeProject = null)
        {
            _activeDataset = activeDataset;
            _activeProject = activeProject;
        }

        public IReadOnlyList<ElementIdentity> ReadElements(IOpenFlowsModel model, CalculationRunIdentity run)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (run == null)
                throw new ArgumentNullException(nameof(run));

            string modelPath = (model.GetType().GetProperty("FilePath")?.GetValue(model, null) as string)
                ?? (model.GetType().GetProperty("Source")?.GetValue(model, null) as string)
                ?? model.Identity?.Value
                ?? "unspecified";
            string runId = run.Value ?? "unspecified";

            var isOpenProp = model.GetType().GetProperty("IsOpen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (isOpenProp != null && isOpenProp.PropertyType == typeof(bool))
            {
                bool isOpen = (bool)isOpenProp.GetValue(model, null);
                if (!isOpen)
                {
                    throw new InvalidOperationException(
                        $"Cannot enumerate elements: OpenFlows model '{modelPath}' is closed or uninitialized.");
                }
            }
            else if (model is ModelReadResult readResult && !readResult.OpenSucceeded)
            {
                throw new InvalidOperationException(
                    $"Cannot enumerate elements: OpenFlows model '{modelPath}' is closed or uninitialized.");
            }

            var elements = new List<ElementIdentity>();

            try
            {
                // 1. Direct explicit element provider (if model explicitly supplies element identities)
                if (model is IElementIdentityEnumerable provider)
                {
                    var direct = provider.GetElementIdentities()?.ToList();
                    if (direct != null)
                    {
                        foreach (var e in direct)
                        {
                            if (e == null || string.IsNullOrWhiteSpace(e.Value))
                            {
                                throw new InvalidOperationException(
                                    $"Element lacks an authoritative label in model '{modelPath}'.");
                            }
                            elements.Add(e);
                        }
                    }
                }

                // 2. Active element managers reflection (if model defines GetActiveElementManagers)
                if (elements.Count == 0)
                {
                    var getManagersMethod = model.GetType().GetMethod("GetActiveElementManagers", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (getManagersMethod != null)
                    {
                        var elementManagers = getManagersMethod.Invoke(model, null) as IEnumerable;
                        var mgrList = elementManagers != null ? elementManagers.Cast<object>().Where(m => m != null).ToList() : null;
                        if (mgrList == null || mgrList.Count == 0)
                        {
                            throw new NotSupportedException(
                                $"Model '{modelPath}' does not expose active element managers for run profile '{runId}'.");
                        }

                        foreach (var manager in mgrList)
                        {
                            var idsMethod = manager.GetType().GetMethod("GetElementIds", Type.EmptyTypes)
                                ?? manager.GetType().GetMethod("ElementIDs", Type.EmptyTypes);
                            var ids = idsMethod?.Invoke(manager, null) as IEnumerable;
                            if (ids == null)
                                continue;

                            var getLabelMethod = manager.GetType().GetMethod("GetElementLabel", new[] { typeof(int) })
                                ?? manager.GetType().GetMethod("ElementLabel", new[] { typeof(int) });
                            var catProp = manager.GetType().GetProperty("Category")
                                ?? manager.GetType().GetProperty("DomainElementType");
                            var category = catProp != null ? catProp.GetValue(manager, null) : manager.GetType().Name;

                            foreach (var rawId in ids)
                            {
                                if (rawId == null || !int.TryParse(rawId.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                                    continue;

                                var label = getLabelMethod != null
                                    ? getLabelMethod.Invoke(manager, new object[] { id }) as string
                                    : id.ToString(CultureInfo.InvariantCulture);

                                // Zero-Fabrication Guard: Fail closed if label is missing
                                if (string.IsNullOrWhiteSpace(label))
                                {
                                    throw new InvalidOperationException(
                                        $"Element {id} of category {category} lacks an authoritative label in model '{modelPath}'.");
                                }

                                elements.Add(new ElementIdentity(id, label, category));
                            }
                        }
                    }
                }

                // 3. Reflection on model for element collections (e.g., ElementIdentities, Elements, Conduits, Structures, Snapshot)
                if (elements.Count == 0)
                {
                    var reflective = TryExtractElementsFromModelProperties(model, modelPath);
                    if (reflective != null && reflective.Count > 0)
                    {
                        elements.AddRange(reflective);
                    }
                }

                // 4. Resolve active WaterObjects dataset from model or reader context
                if (elements.Count == 0)
                {
                    object dataset = ResolveDataset(model);
                    if (dataset != null)
                    {
                        var enumerated = EnumerateDatasetElements(dataset, modelPath);
                        if (enumerated != null && enumerated.Count > 0)
                        {
                            elements.AddRange(enumerated);
                        }
                    }
                }

                if (elements.Count == 0)
                {
                    throw new HydraulicDataContractException(
                        $"The active WaterObjects run context cannot enumerate element identities: Model '{modelPath}' does not expose active element managers for run profile '{runId}'.");
                }
            }
            catch (Exception ex) when (!(ex is NotSupportedException) && !(ex is InvalidOperationException) && !(ex is ArgumentNullException))
            {
                throw new InvalidOperationException(
                    $"Failed to enumerate element identities from OpenFlows model '{modelPath}' for run '{runId}'.", ex);
            }

            // Anti-Stub Gate: A loaded valid model cannot silently yield zero elements
            if (elements.Count == 0)
            {
                throw new HydraulicDataContractException(
                    $"Cannot enumerate element identities: Zero element identities resolved from model '{modelPath}'. Aborting to prevent silent empty result set.");
            }

            return elements
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Value))
                .GroupBy(e => e.Value, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(e => e.Value, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        private object ResolveDataset(IOpenFlowsModel model)
        {
            if (_activeDataset != null) return _activeDataset;
            if (model == null) return null;

            var modelType = model.GetType();
            foreach (var propName in new[] { "DomainDataSet", "DataSet", "Dataset", "UnderlyingDataSet", "ActiveDataSet" })
            {
                var prop = modelType.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    var val = prop.GetValue(model, null);
                    if (val != null) return val;
                }
            }

            return null;
        }

        private static IReadOnlyList<ElementIdentity> TryExtractElementsFromModelProperties(IOpenFlowsModel model, string modelPath = null)
        {
            if (model == null) return null;
            modelPath = modelPath ?? model.Identity?.Value ?? "unspecified";
            var modelType = model.GetType();

            foreach (var propName in new[] { "ElementIdentities", "Elements" })
            {
                var prop = modelType.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    var val = prop.GetValue(model, null);
                    if (val is IEnumerable<ElementIdentity> idList)
                    {
                        var res = new List<ElementIdentity>();
                        foreach (var x in idList)
                        {
                            if (x == null || string.IsNullOrWhiteSpace(x.Value))
                            {
                                throw new InvalidOperationException(
                                    $"Element lacks an authoritative label in model '{modelPath}'.");
                            }
                            res.Add(x);
                        }
                        if (res.Count > 0) return res.AsReadOnly();
                    }
                    if (val is IEnumerable<string> strList)
                    {
                        var res = new List<ElementIdentity>();
                        foreach (var x in strList)
                        {
                            if (string.IsNullOrWhiteSpace(x))
                            {
                                throw new InvalidOperationException(
                                    $"Element lacks an authoritative label in model '{modelPath}'.");
                            }
                            res.Add(new ElementIdentity(x));
                        }
                        if (res.Count > 0) return res.AsReadOnly();
                    }
                    if (val is IEnumerable<int> intList)
                    {
                        var res = new List<ElementIdentity>();
                        foreach (var x in intList)
                        {
                            res.Add(new ElementIdentity(x.ToString(CultureInfo.InvariantCulture)));
                        }
                        if (res.Count > 0) return res.AsReadOnly();
                    }
                }
            }

            foreach (var propName in new[] { "Snapshot", "Network", "NetworkRead", "NetworkData" })
            {
                var prop = modelType.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    var val = prop.GetValue(model, null);
                    if (val is GravityNetworkSnapshot snapshot)
                    {
                        var ids = new List<ElementIdentity>();
                        if (snapshot.Conduits != null)
                        {
                            foreach (var c in snapshot.Conduits)
                            {
                                var label = !string.IsNullOrWhiteSpace(c.Label)
                                    ? c.Label
                                    : (c.SourceElementId != 0 ? c.SourceElementId.ToString(CultureInfo.InvariantCulture) : null);
                                if (string.IsNullOrWhiteSpace(label))
                                {
                                    throw new InvalidOperationException(
                                        $"Element {c.SourceElementId} of category Conduit lacks an authoritative label in model '{modelPath}'.");
                                }
                                ids.Add(new ElementIdentity(label));
                            }
                        }
                        if (snapshot.Nodes != null)
                        {
                            foreach (var n in snapshot.Nodes)
                            {
                                var label = !string.IsNullOrWhiteSpace(n.Label)
                                    ? n.Label
                                    : (n.SourceElementId != 0 ? n.SourceElementId.ToString(CultureInfo.InvariantCulture) : null);
                                if (string.IsNullOrWhiteSpace(label))
                                {
                                    throw new InvalidOperationException(
                                        $"Element {n.SourceElementId} of category Node lacks an authoritative label in model '{modelPath}'.");
                                }
                                ids.Add(new ElementIdentity(label));
                            }
                        }
                        if (ids.Count > 0) return ids.AsReadOnly();
                    }
                    if (val is NetworkReadResult readResult)
                    {
                        var ids = new List<ElementIdentity>();
                        if (readResult.Conduits != null)
                        {
                            foreach (var c in readResult.Conduits)
                            {
                                var label = !string.IsNullOrWhiteSpace(c.Label)
                                    ? c.Label
                                    : (c.Id != 0 ? c.Id.ToString(CultureInfo.InvariantCulture) : null);
                                if (string.IsNullOrWhiteSpace(label))
                                {
                                    throw new InvalidOperationException(
                                        $"Element {c.Id} of category Conduit lacks an authoritative label in model '{modelPath}'.");
                                }
                                ids.Add(new ElementIdentity(label));
                            }
                        }
                        if (readResult.Structures != null)
                        {
                            foreach (var s in readResult.Structures)
                            {
                                var label = !string.IsNullOrWhiteSpace(s.Label)
                                    ? s.Label
                                    : (s.Id != 0 ? s.Id.ToString(CultureInfo.InvariantCulture) : null);
                                if (string.IsNullOrWhiteSpace(label))
                                {
                                    throw new InvalidOperationException(
                                        $"Element {s.Id} of category Structure lacks an authoritative label in model '{modelPath}'.");
                                }
                                ids.Add(new ElementIdentity(label));
                            }
                        }
                        if (ids.Count > 0) return ids.AsReadOnly();
                    }
                }
            }

            return null;
        }

        private static IReadOnlyList<ElementIdentity> EnumerateDatasetElements(object dataset, string modelPath = null)
        {
            if (dataset == null) return null;
            modelPath = modelPath ?? "unspecified";

            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var domainAsm = assemblies.FirstOrDefault(a => !a.IsDynamic && a.GetName().Name == "Haestad.Domain");
                Type datasetType = domainAsm?.GetType("Haestad.Domain.IDomainDataSet", false) ?? dataset.GetType();
                Type elementTypeEnum = domainAsm?.GetType("Haestad.Domain.DomainElementType", false);

                MethodInfo domainElementManagerMethod = datasetType.GetMethod("DomainElementManager", new[] { typeof(int) });
                if (domainElementManagerMethod == null) return null;

                var managerNames = new[]
                {
                    "ConduitElementManager",
                    "ManholeElementManager",
                    "CatchBasinElementManager",
                    "OutfallElementManager",
                    "CrossSectionNodeElementManager",
                    "WetWellElementManager",
                    "JunctionChamberElementManager"
                };

                var collectedIdentities = new List<ElementIdentity>();

                foreach (var mgrName in managerNames)
                {
                    int typeId = -1;
                    if (elementTypeEnum != null)
                    {
                        try
                        {
                            object enumVal = WaterObjectsLifecycleHost.EnumMember(elementTypeEnum, mgrName);
                            if (enumVal != null) typeId = Convert.ToInt32(enumVal);
                        }
                        catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsResultReader.cs: " + suppressedEx.Message); }
                    }
                    else if (mgrName == "ConduitElementManager") typeId = 66;
                    else if (mgrName == "ManholeElementManager") typeId = 1;

                    if (typeId < 0) continue;

                    try
                    {
                        object manager = domainElementManagerMethod.Invoke(dataset, new object[] { typeId });
                        if (manager == null) continue;

                        Type mgrType = manager.GetType();
                        MethodInfo idsMethod = mgrType.GetMethod("ElementIDs", Type.EmptyTypes);
                        if (idsMethod == null)
                        {
                            try { idsMethod = WaterObjectsLifecycleHost.ElementIDs(mgrType); }
                            catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsResultReader.cs: " + suppressedEx.Message); }
                        }

                        if (idsMethod == null) continue;

                        object idCollection = idsMethod.Invoke(manager, null);
                        if (idCollection == null) continue;

                        MethodInfo elementMethod = mgrType.GetMethod("Element", new[] { typeof(int) });

                        var idList = new List<int>();
                        if (idCollection is IEnumerable enumerable)
                        {
                            foreach (object item in enumerable)
                            {
                                if (item is int id) idList.Add(id);
                                else if (int.TryParse(item?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedId))
                                    idList.Add(parsedId);
                            }
                        }
                        else
                        {
                            PropertyInfo countProp = idCollection.GetType().GetProperty("Count");
                            int count = countProp != null ? (int)countProp.GetValue(idCollection, null) : 0;
                            if (count > 0)
                            {
                                PropertyInfo indexer = WaterObjectsLifecycleHost.Indexer(idCollection.GetType());
                                for (int i = 0; i < count; i++)
                                {
                                    object val = indexer.GetValue(idCollection, new object[] { i });
                                    if (val is int id) idList.Add(id);
                                }
                            }
                        }

                        foreach (var id in idList)
                        {
                            string label = null;
                            if (elementMethod != null)
                            {
                                try
                                {
                                    object elem = elementMethod.Invoke(manager, new object[] { id });
                                    if (elem != null)
                                    {
                                        var labelProp = elem.GetType().GetProperty("Label");
                                        label = labelProp?.GetValue(elem, null)?.ToString();
                                    }
                                }
                                catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsResultReader.cs: " + suppressedEx.Message); }
                            }

                            if (string.IsNullOrWhiteSpace(label))
                            {
                                label = id.ToString(CultureInfo.InvariantCulture);
                            }

                            // Zero-Fabrication Guard: Fail closed if label is missing
                            if (string.IsNullOrWhiteSpace(label))
                            {
                                throw new InvalidOperationException(
                                    $"Element {id} of category {mgrName} lacks an authoritative label in model '{modelPath}'.");
                            }

                            collectedIdentities.Add(new ElementIdentity(label));
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException)
                    {
                        throw;
                    }
                    catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsResultReader.cs: " + suppressedEx.Message); }
                }

                if (collectedIdentities.Count == 0) return null;

                return collectedIdentities.AsReadOnly();
            }
            catch (Exception ex) when (ex is InvalidOperationException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        public WaterObjectsFlowResult ReadFlow(object dataset, object project, int scenarioId = 81, string engine = EngineName, int preferredElementId = 27)
        {
            var result = new WaterObjectsFlowResult { Status = "INITIALIZED", EngineUsed = engine };
            if (dataset == null)
            {
                result.Status = "RESULT_MANAGER_UNAVAILABLE";
                result.ResultManagerAvailable = false;
                return result;
            }

            object manager = null;
            MethodInfo managerCloseMethod = null;
            Type resultManagerType = null;
            bool resultOpenAttempted = false;

            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var domainAsm = assemblies.FirstOrDefault(a => !a.IsDynamic && a.GetName().Name == "Haestad.Domain");
                var supportAsm = assemblies.FirstOrDefault(a => !a.IsDynamic && a.GetName().Name == "Haestad.Support");

                Type dataType = domainAsm?.GetType("Haestad.Domain.IDomainDataSet", false);
                Type scenarioType = domainAsm?.GetType("Haestad.Domain.IScenario", false);
                Type scenarioManagerType = domainAsm?.GetType("Haestad.Domain.IScenarioManager", false);
                Type domainManagerType = domainAsm?.GetType("Haestad.Domain.IDomainElementManager", false);
                Type elementsType = domainAsm?.GetType("Haestad.Domain.IModelingElementManager", false);
                Type fieldType = supportAsm?.GetType("Haestad.Support.Support.IField", false);
                resultManagerType = domainAsm?.GetType("Haestad.Domain.IResultManager", false);
                Type resultFieldType = domainAsm?.GetType("Haestad.Domain.IResultField", false);
                Type variantType = domainAsm?.GetType("Haestad.Domain.IResultTimeVariantField", false);
                Type unitized = domainAsm?.GetType("Haestad.Domain.IUnitizedField", false);
                Type unitIndex = supportAsm?.GetType("Haestad.Support.Units.UnitConversionManager+UnitIndex", false);
                Type timeUnitType = supportAsm?.GetType("Haestad.Support.Units.TimeUnit", false);

                // Helper to get property value either via interface or direct object reflection
                object GetProp(object target, Type iface, string propName)
                {
                    if (target == null) return null;
                    var prop = (iface != null ? iface.GetProperty(propName) : null) ?? target.GetType().GetProperty(propName);
                    return prop?.GetValue(target, null);
                }

                // Helper to invoke method either via interface or direct object reflection
                object InvokeMethod(object target, Type iface, string methodName, Type[] paramTypes, object[] args)
                {
                    if (target == null) return null;
                    MethodInfo method = null;
                    if (iface != null)
                    {
                        method = paramTypes != null ? iface.GetMethod(methodName, paramTypes) : iface.GetMethod(methodName);
                    }
                    if (method == null)
                    {
                        method = paramTypes != null ? target.GetType().GetMethod(methodName, paramTypes) : target.GetType().GetMethod(methodName);
                    }
                    return method?.Invoke(target, args);
                }

                // 1. Resolve Scenario and ResultManager
                object sm = GetProp(dataset, dataType, NameScenarioManager);
                if (sm == null)
                {
                    result.Status = "RESULT_MANAGER_UNAVAILABLE";
                    return result;
                }

                object scenario = InvokeMethod(sm, scenarioManagerType, "Element", new[] { typeof(int) }, new object[] { scenarioId });
                if (scenario == null)
                {
                    result.Status = "SCENARIO_NOT_FOUND";
                    return result;
                }

                manager = InvokeMethod(scenario, scenarioType, "ResultManager", new[] { typeof(string) }, new object[] { engine });
                result.ResultManagerAvailable = manager != null;
                if (manager == null)
                {
                    result.Status = "RESULT_MANAGER_UNAVAILABLE";
                    return result;
                }

                managerCloseMethod = resultManagerType != null
                    ? resultManagerType.GetMethod("Close", Type.EmptyTypes)
                    : manager.GetType().GetMethod("Close", Type.EmptyTypes);

                object activeObj = GetProp(manager, resultManagerType, "IsActive");
                bool isActive = activeObj is bool b && b;
                if (!isActive)
                {
                    resultOpenAttempted = true;
                    InvokeMethod(manager, resultManagerType, "Open", Type.EmptyTypes, null);
                }

                object hasResObj = GetProp(manager, resultManagerType, "HasResults");
                result.HasResults = hasResObj is bool hasRes && hasRes;
                if (!result.HasResults)
                {
                    result.Status = "HAS_RESULTS_FALSE";
                    return result;
                }

                // 2. Resolve Conduit Element Manager and Target Element
                object conduitManager = null;
                var elementType = domainAsm?.GetType("Haestad.Domain.DomainElementType", false);
                if (elementType != null)
                {
                    object conduitEnum = WaterObjectsLifecycleHost.EnumMember(elementType, "ConduitElementManager");
                    conduitManager = InvokeMethod(dataset, dataType, "DomainElementManager", new[] { typeof(int) }, new object[] { Convert.ToInt32(conduitEnum) });
                }
                else
                {
                    conduitManager = InvokeMethod(dataset, dataType, "DomainElementManager", new[] { typeof(int) }, new object[] { 66 });
                }

                if (conduitManager == null)
                {
                    result.Status = "CONDUIT_MANAGER_UNAVAILABLE";
                    return result;
                }

                int selectedElementId = -1;
                object existsObj = InvokeMethod(conduitManager, elementsType, "Exists", new[] { typeof(int) }, new object[] { preferredElementId });
                bool exists = existsObj is bool eb && eb;
                if (exists)
                {
                    selectedElementId = preferredElementId;
                }
                else
                {
                    var idsMethod = WaterObjectsLifecycleHost.ElementIDs(conduitManager.GetType());
                    if (idsMethod != null)
                    {
                        object ids = idsMethod.Invoke(conduitManager, null);
                        var indexer = WaterObjectsLifecycleHost.Indexer(ids.GetType());
                        selectedElementId = (int)indexer.GetValue(ids, new object[] { 0 });
                    }
                    else
                    {
                        selectedElementId = preferredElementId;
                    }
                }
                result.ElementId = selectedElementId;

                // 3. Verify Supported Result Fields
                object supported = InvokeMethod(conduitManager, domainManagerType, "SupportedResultFields", new[] { typeof(string) }, new object[] { engine });
                var enumerable = supported as IEnumerable;
                bool flowSupported = false;
                if (enumerable != null)
                {
                    foreach (object f in enumerable)
                    {
                        string name = GetProp(f, fieldType, "Name") as string;
                        if (string.Equals(name, FlowFieldName, StringComparison.OrdinalIgnoreCase))
                        {
                            flowSupported = true;
                            break;
                        }
                    }
                }
                result.FlowFieldSupported = flowSupported;
                if (!flowSupported)
                {
                    result.Status = "FLOW_FIELD_UNSUPPORTED";
                    return result;
                }

                // 4. Resolve Result Field
                object flowField = InvokeMethod(conduitManager, domainManagerType, "ResultField",
                    new[] { typeof(string), typeof(string), typeof(string) }, new object[] { FlowFieldName, engine, FlowRecordName });
                if (flowField == null)
                {
                    result.Status = "FLOW_FIELD_NULL";
                    return result;
                }

                object fieldHasResObj = InvokeMethod(flowField, resultFieldType, "HasResults", new[] { typeof(int) }, new object[] { scenarioId });
                result.FlowFieldHasResults = fieldHasResObj is bool fb && fb;
                if (!result.FlowFieldHasResults)
                {
                    result.Status = "FLOW_FIELD_HAS_RESULTS_FALSE";
                    return result;
                }

                // 5. Timesteps
                double[] times = null;
                if (timeUnitType != null)
                {
                    times = (double[])InvokeMethod(manager, resultManagerType, "TimeSteps", new[] { timeUnitType },
                        new object[] { Enum.Parse(timeUnitType, "Seconds", false) });
                }
                else
                {
                    times = (double[])InvokeMethod(manager, resultManagerType, "TimeSteps", null, new object[] { 0 });
                }

                if (times == null || times.Length == 0)
                {
                    result.Status = "NO_TIMESTEPS";
                    return result;
                }
                int timeStepIndex = 0;
                result.TimeStepIndex = timeStepIndex;
                result.TimeSeconds = times[timeStepIndex];

                // 6. Units
                if (unitized != null && unitIndex != null && unitized.IsInstanceOfType(flowField))
                {
                    object working = flowField.GetType().GetProperty("WorkingUnitIndex")?.GetValue(flowField, null);
                    if (working != null)
                    {
                        result.WorkingUnitIdentity = unitIndex.FullName + ":" + Enum.GetName(unitIndex, working) + ":" + Convert.ToInt64(working, CultureInfo.InvariantCulture);
                        result.WorkingUnitSymbol = P16CalculatedFlowDiagnostic.FlowUnit(result.WorkingUnitIdentity) ?? Enum.GetName(unitIndex, working);
                    }
                }

                if (string.IsNullOrWhiteSpace(result.WorkingUnitSymbol) && flowField != null)
                {
                    object symbolProp = flowField.GetType().GetProperty("WorkingUnitSymbol")?.GetValue(flowField, null);
                    if (symbolProp is string sym && !string.IsNullOrWhiteSpace(sym))
                    {
                        result.WorkingUnitSymbol = sym;
                    }
                }

                if (string.IsNullOrWhiteSpace(result.WorkingUnitSymbol))
                {
                    throw new IncompleteDataException(
                        "MISSING_FLOW_UNIT_METADATA",
                        string.Format(CultureInfo.InvariantCulture,
                            "Result flow for element '{0}' lacks authoritative working unit metadata. Cannot assume ft3/s. Fail-closed contract enforced (N10).", selectedElementId));
                }

                // 7. Extract Flow Value
                object raw = InvokeMethod(flowField, variantType, "GetValue", new[] { typeof(int), typeof(int), typeof(int) },
                    new object[] { selectedElementId, scenarioId, timeStepIndex });

                if ((raw == null || (raw is double dVal && (double.IsNaN(dVal) || double.IsInfinity(dVal)))) && !string.Equals(engine, EngineName, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        object csMgr = InvokeMethod(scenario, scenarioType, "ResultManager", new[] { typeof(string) }, new object[] { EngineName });
                        if (csMgr != null)
                        {
                            object csField = InvokeMethod(conduitManager, domainManagerType, "ResultField",
                                new[] { typeof(string), typeof(string), typeof(string) }, new object[] { FlowFieldName, EngineName, FlowRecordName });
                            if (csField != null && variantType != null && variantType.IsInstanceOfType(csField))
                            {
                                object csRaw = InvokeMethod(csField, variantType, "GetValue", new[] { typeof(int), typeof(int), typeof(int) },
                                    new object[] { selectedElementId, scenarioId, timeStepIndex });
                                if (csRaw != null && (!(csRaw is double dcs) || (!double.IsNaN(dcs) && !double.IsInfinity(dcs))))
                                {
                                    raw = csRaw;
                                    result.EngineUsed = EngineName;
                                }
                            }
                        }
                    }
                    catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsResultReader.cs: " + suppressedEx.Message); }
                }

                result.ValueClrType = raw?.GetType().FullName ?? "null";
                if (raw != null)
                {
                    double val = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                    result.RawValue = val.ToString("R", CultureInfo.InvariantCulture);

                    if (double.IsNaN(val) || double.IsInfinity(val))
                    {
                        result.Value = null;
                        result.Classification = "INVALID_OR_MISSING";
                        result.Status = "VALUE_NOT_FINITE";
                    }
                    else if (Math.Abs(val) < 1e-12)
                    {
                        result.Value = val;
                        result.Classification = "OBSERVED_ZERO";
                        result.Status = "SUCCESS";
                    }
                    else
                    {
                        result.Value = val;
                        result.Classification = "OBSERVED_FINITE";
                        result.Status = "SUCCESS";
                    }
                }
                else
                {
                    result.Value = null;
                    result.RawValue = "null";
                    result.Classification = "INVALID_OR_MISSING";
                    result.Status = "NOT_AVAILABLE";
                }
            }
            catch (IncompleteDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Status = "EXCEPTION";
                result.ErrorMessage = ex.ToString();
            }
            finally
            {
                if (manager != null && resultOpenAttempted)
                {
                    try { (resultManagerType?.GetMethod("Close", Type.EmptyTypes) ?? manager.GetType().GetMethod("Close", Type.EmptyTypes))?.Invoke(manager, null); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsResultReader.cs: " + suppressedEx.Message); }
                }
            }

            return result;
        }

        public WaterObjectsMetricResult ReadVelocity(object dataset, object project, int scenarioId = 81, string engine = EngineName, int preferredElementId = 27)
        {
            return ReadMetric(dataset, project, "Velocity",
                new[] { "BasicVelocityResults_Velocity", "BasicVelocityResults_Velocity", "MiddleVelocityResults_VelocityMiddle", "MiddleVelocityResults_VelocityMiddle", "VelocityStatistics_MaxVelocity", "VelocityStatistics_MaxVelocity", "BoundingVelocityResults_VelocityIn", "BoundingVelocityResults_VelocityOut" },
                new[] { engine, EngineName, engine, EngineName, engine, EngineName, engine, engine },
                new[] { "BasicVelocityResults", "BasicVelocityResults", "MiddleVelocityResults", "MiddleVelocityResults", "VelocityStatistics", "VelocityStatistics", "BoundingVelocityResults", "BoundingVelocityResults" },
                scenarioId, engine, preferredElementId, false,
                "Average flow velocity in the conduit", null, "ft/s");
        }

        public WaterObjectsMetricResult ReadDepth(object dataset, object project, int scenarioId = 81, string engine = EngineName, int preferredElementId = 27)
        {
            return ReadMetric(dataset, project, "Depth",
                new[] { "MiddleDepthResults_DepthMiddle", "MiddleDepthResults_DepthMiddle", "BoundingDepthResults_DepthIn", "BoundingDepthResults_DepthOut" },
                new[] { engine, EngineName, engine, engine },
                new[] { "MiddleDepthResults", "MiddleDepthResults", "BoundingDepthResults", "BoundingDepthResults" },
                scenarioId, engine, preferredElementId, false,
                "Flow depth in the middle section of the conduit", null, "ft");
        }

        public WaterObjectsMetricResult ReadCapacity(object dataset, object project, int scenarioId = 81, string engine = EngineName, int preferredElementId = 27)
        {
            return ReadMetric(dataset, project, "Capacity",
                new[] { "GravityDerivedCapacityResults_FullFlowCapacity", "StandardConduitResults_FullCapacity", "GravityDerivedCapacityResults_DesignCapacity", "StandardConduitResults_CalculatedDesignCapacity" },
                new[] { "GravityDerivedEngine", engine, "GravityDerivedEngine", engine },
                new[] { "GravityDerivedCapacityResults", "StandardConduitResults", "GravityDerivedCapacityResults", "StandardConduitResults" },
                scenarioId, engine, preferredElementId, false,
                "Full flow capacity of the conduit (maximum gravity discharge capacity based on pipe section, slope and roughness)", null, "ft3/s");
        }

        public WaterObjectsMetricResult ReadHgl(object dataset, object project, int scenarioId = 81, string engine = EngineName, int preferredElementId = 27)
        {
            return ReadMetric(dataset, project, "HGL",
                new[] { "MiddleHydraulicGradeResults_HydraulicGradeMiddle", "MiddleHydraulicGradeResults_HydraulicGradeMiddle", "StandardStatistics_MaximumHGL", "StandardStatistics_MaximumHGL", "BoundingHydraulicGradeResults_HydraulicGradeLineIn", "BoundingHydraulicGradeResults_HydraulicGradeLineOut" },
                new[] { engine, EngineName, engine, EngineName, engine, engine },
                new[] { "MiddleHydraulicGradeResults", "MiddleHydraulicGradeResults", "StandardStatistics", "StandardStatistics", "BoundingHydraulicGradeResults", "BoundingHydraulicGradeResults" },
                scenarioId, engine, preferredElementId, false,
                "Hydraulic grade line elevation at the middle section of the conduit", "Elevation in feet above project vertical datum (mean sea level reference)", "ft");
        }

        public WaterObjectsMetricResult ReadSurcharge(object dataset, object project, int scenarioId = 81, string engine = EngineName, int preferredElementId = 27)
        {
            return ReadMetric(dataset, project, "Surcharge",
                new[] { "BasicSurchargedResults_IsSurcharged", "BasicSurchargedResults_IsSurcharged", "BasicSurchargedResults_IsEverSurcharged", "LinkSurchargeResults_SurchargeDepth" },
                new[] { engine, EngineName, engine, engine },
                new[] { "BasicSurchargedResults", "BasicSurchargedResults", "BasicSurchargedResults", "LinkSurchargeResults" },
                scenarioId, engine, preferredElementId, false,
                "Boolean surcharge indicator: true if HGL exceeds pipe crown elevation at current timestep, false otherwise", null, "N/A");
        }

        public WaterObjectsMetricResult ReadNodeHgl(object dataset, object project, int scenarioId, string engine, int nodeId, string category)
        {
            string manager = string.Equals(category, "Outfall", StringComparison.OrdinalIgnoreCase) ? "OutfallElementManager"
                : string.Equals(category, "CatchBasin", StringComparison.OrdinalIgnoreCase) ? "CatchBasinElementManager"
                : string.Equals(category, "Manhole", StringComparison.OrdinalIgnoreCase) ? "ManholeElementManager" : null;
            if (manager == null) return new WaterObjectsMetricResult { MetricName = "HGL", ElementId = nodeId, Status = "NODE_CATEGORY_UNSUPPORTED" };
            return ReadMetric(dataset, project, "HGL",
                new[] { "BasicHydraulicGradeResults_HydraulicGrade", "BasicHydraulicGradeResults_HydraulicGrade", "StandardStatistics_MaximumHGL", "StandardStatistics_MaximumHGL" },
                new[] { engine, EngineName, engine, EngineName },
                new[] { "BasicHydraulicGradeResults", "BasicHydraulicGradeResults", "StandardStatistics", "StandardStatistics" },
                scenarioId, engine, nodeId, true,
                "Calculated hydraulic grade at the requested structure; no conduit substitution",
                "Project vertical datum; source unit metadata required", "ft", manager);
        }

        public WaterObjectsMetricResult ReadMetric(
            object dataset,
            object project,
            string metricName,
            string[] candidateFieldNames,
            string[] candidateEngines,
            string[] candidateRecords,
            int scenarioId = 81,
            string defaultEngine = EngineName,
            int preferredElementId = 27,
            bool isNode = false,
            string semantics = "",
            string datumSemantics = "",
            string defaultUnit = "",
            string nodeManagerName = null)
        {
            var result = new WaterObjectsMetricResult
            {
                MetricName = metricName,
                ElementId = preferredElementId,
                Status = "INITIALIZED",
                Semantics = semantics,
                DatumSemantics = datumSemantics,
                WorkingUnitSymbol = defaultUnit
            };

            if (dataset == null)
            {
                result.Status = "RESULT_MANAGER_UNAVAILABLE";
                result.ResultManagerAvailable = false;
                return result;
            }

            object manager = null;
            object altEngineManager = null;
            Type resultManagerType = null;
            bool resultOpenAttempted = false;
            bool altOpenAttempted = false;

            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var domainAsm = assemblies.FirstOrDefault(a => !a.IsDynamic && a.GetName().Name == "Haestad.Domain");
                var supportAsm = assemblies.FirstOrDefault(a => !a.IsDynamic && a.GetName().Name == "Haestad.Support");

                Type dataType = domainAsm?.GetType("Haestad.Domain.IDomainDataSet", false);
                Type scenarioType = domainAsm?.GetType("Haestad.Domain.IScenario", false);
                Type scenarioManagerType = domainAsm?.GetType("Haestad.Domain.IScenarioManager", false);
                Type domainManagerType = domainAsm?.GetType("Haestad.Domain.IDomainElementManager", false);
                Type elementsType = domainAsm?.GetType("Haestad.Domain.IModelingElementManager", false);
                Type fieldType = supportAsm?.GetType("Haestad.Support.Support.IField", false);
                resultManagerType = domainAsm?.GetType("Haestad.Domain.IResultManager", false);
                Type resultFieldType = domainAsm?.GetType("Haestad.Domain.IResultField", false);
                Type variantType = domainAsm?.GetType("Haestad.Domain.IResultTimeVariantField", false);
                Type nonVariantType = domainAsm?.GetType("Haestad.Domain.IResultNonTimeVariantField", false);
                Type unitized = domainAsm?.GetType("Haestad.Domain.IUnitizedField", false);
                Type unitIndex = supportAsm?.GetType("Haestad.Support.Units.UnitConversionManager+UnitIndex", false);
                Type timeUnitType = supportAsm?.GetType("Haestad.Support.Units.TimeUnit", false);

                object GetProp(object target, Type iface, string propName)
                {
                    if (target == null) return null;
                    var prop = (iface != null ? iface.GetProperty(propName) : null) ?? target.GetType().GetProperty(propName);
                    return prop?.GetValue(target, null);
                }

                object InvokeMethod(object target, Type iface, string methodName, Type[] paramTypes, object[] args)
                {
                    if (target == null) return null;
                    MethodInfo method = null;
                    if (iface != null)
                        method = paramTypes != null ? iface.GetMethod(methodName, paramTypes) : iface.GetMethod(methodName);
                    if (method == null)
                        method = paramTypes != null ? target.GetType().GetMethod(methodName, paramTypes) : target.GetType().GetMethod(methodName);
                    return method?.Invoke(target, args);
                }

                // 1. Resolve Scenario and default ResultManager
                object sm = GetProp(dataset, dataType, NameScenarioManager);
                if (sm == null)
                {
                    result.Status = "RESULT_MANAGER_UNAVAILABLE";
                    return result;
                }

                object scenario = InvokeMethod(sm, scenarioManagerType, "Element", new[] { typeof(int) }, new object[] { scenarioId });
                if (scenario == null)
                {
                    result.Status = "SCENARIO_NOT_FOUND";
                    return result;
                }

                manager = InvokeMethod(scenario, scenarioType, "ResultManager", new[] { typeof(string) }, new object[] { defaultEngine });
                result.ResultManagerAvailable = manager != null;
                if (manager == null)
                {
                    result.Status = "RESULT_MANAGER_UNAVAILABLE";
                    return result;
                }

                object activeObj = GetProp(manager, resultManagerType, "IsActive");
                if (!(activeObj is bool isActive && isActive))
                {
                    resultOpenAttempted = true;
                    InvokeMethod(manager, resultManagerType, "Open", Type.EmptyTypes, null);
                }

                object hasResObj = GetProp(manager, resultManagerType, "HasResults");
                result.HasResults = hasResObj is bool hasRes && hasRes;
                if (!result.HasResults)
                {
                    result.Status = "HAS_RESULTS_FALSE";
                    return result;
                }

                // 2. Resolve Element Manager and Target Element
                object elemManager = null;
                var elementType = domainAsm?.GetType("Haestad.Domain.DomainElementType", false);
                int elemTypeInt = isNode ? 1 : 66; // 1 = Manhole, 66 = Conduit
                int selectedElementId = preferredElementId;

                if (isNode)
                {
                    var candidateManagers = new List<string>();
                    if (!string.IsNullOrEmpty(nodeManagerName)) candidateManagers.Add(nodeManagerName);
                    candidateManagers.Add("ManholeElementManager");
                    candidateManagers.Add("CatchBasinElementManager");
                    candidateManagers.Add("OutfallElementManager");

                    bool foundNode = false;
                    foreach (var mgrName in candidateManagers.Distinct())
                    {
                        object elemEnum = WaterObjectsLifecycleHost.EnumMember(elementType, mgrName);
                        if (elemEnum == null) continue;
                        int currentElemType = Convert.ToInt32(elemEnum);
                        object testMgr = InvokeMethod(dataset, dataType, "DomainElementManager", new[] { typeof(int) }, new object[] { currentElemType });
                        if (testMgr == null) continue;
                        object existsObj = InvokeMethod(testMgr, elementsType, "Exists", new[] { typeof(int) }, new object[] { preferredElementId });
                        if (existsObj is bool eb && eb)
                        {
                            elemManager = testMgr;
                            elemTypeInt = currentElemType;
                            nodeManagerName = mgrName;
                            foundNode = true;
                            break;
                        }
                    }

                    if (!foundNode)
                    {
                        result.ElementId = preferredElementId;
                        result.Status = "REQUESTED_NODE_NOT_FOUND";
                        return result;
                    }
                }
                else
                {
                    object elemEnum = WaterObjectsLifecycleHost.EnumMember(elementType, "ConduitElementManager");
                    if (elemEnum != null) elemTypeInt = Convert.ToInt32(elemEnum);
                    elemManager = InvokeMethod(dataset, dataType, "DomainElementManager", new[] { typeof(int) }, new object[] { elemTypeInt });
                    if (elemManager == null)
                    {
                        result.Status = "CONDUIT_MANAGER_UNAVAILABLE";
                        return result;
                    }
                    object existsObj = InvokeMethod(elemManager, elementsType, "Exists", new[] { typeof(int) }, new object[] { preferredElementId });
                    if (!(existsObj is bool eb && eb))
                    {
                        var idsMethod = WaterObjectsLifecycleHost.ElementIDs(elemManager.GetType());
                        if (idsMethod != null)
                        {
                            object ids = idsMethod.Invoke(elemManager, null);
                            var indexer = WaterObjectsLifecycleHost.Indexer(ids.GetType());
                            selectedElementId = (int)indexer.GetValue(ids, new object[] { 0 });
                        }
                    }
                }
                result.ElementId = selectedElementId;

                // 3. Resolve Result Field among candidate options
                object targetField = null;
                string chosenFieldName = null;
                string chosenRecordName = null;
                string chosenEngineName = null;
                object chosenManager = manager;
                object raw = null;

                for (int i = 0; i < candidateFieldNames.Length; i++)
                {
                    string cField = candidateFieldNames[i];
                    string cEngine = (candidateEngines != null && i < candidateEngines.Length) ? candidateEngines[i] : defaultEngine;
                    string cRecord = (candidateRecords != null && i < candidateRecords.Length) ? candidateRecords[i] : null;

                    object currentEngineManager = manager;
                    if (!string.IsNullOrEmpty(cEngine) && !string.Equals(cEngine, defaultEngine, StringComparison.OrdinalIgnoreCase))
                    {
                        altEngineManager = InvokeMethod(scenario, scenarioType, "ResultManager", new[] { typeof(string) }, new object[] { cEngine });
                        if (altEngineManager != null)
                        {
                            object altActive = GetProp(altEngineManager, resultManagerType, "IsActive");
                            if (!(altActive is bool a && a))
                            {
                                altOpenAttempted = true;
                                InvokeMethod(altEngineManager, resultManagerType, "Open", Type.EmptyTypes, null);
                            }
                            currentEngineManager = altEngineManager;
                        }
                    }

                    // Try explicit engine and record
                    object fieldCandidate = null;
                    if (!string.IsNullOrEmpty(cRecord) && !string.IsNullOrEmpty(cEngine))
                    {
                        fieldCandidate = InvokeMethod(elemManager, domainManagerType, "ResultField",
                            new[] { typeof(string), typeof(string), typeof(string) }, new object[] { cField, cEngine, cRecord });
                    }
                    if (fieldCandidate == null && !string.IsNullOrEmpty(cRecord))
                    {
                        fieldCandidate = InvokeMethod(elemManager, domainManagerType, "ResultField",
                            new[] { typeof(string), typeof(string) }, new object[] { cField, cRecord });
                    }
                    if (fieldCandidate == null)
                    {
                        object supported = InvokeMethod(elemManager, domainManagerType, "SupportedResultFields",
                            new[] { typeof(string) }, new object[] { cEngine });
                        if (supported is IEnumerable en)
                        {
                            foreach (object f in en)
                            {
                                string name = GetProp(f, fieldType, "Name") as string;
                                if (string.Equals(name, cField, StringComparison.OrdinalIgnoreCase))
                                {
                                    fieldCandidate = f;
                                    break;
                                }
                            }
                        }
                    }

                    if (fieldCandidate != null)
                    {
                        object fieldHasRes = InvokeMethod(fieldCandidate, resultFieldType, "HasResults", new[] { typeof(int) }, new object[] { scenarioId });
                        bool hasResCandidate = fieldHasRes is bool fb && fb;
                        if (hasResCandidate)
                        {
                            if (targetField == null)
                            {
                                targetField = fieldCandidate;
                                chosenFieldName = cField;
                                chosenRecordName = cRecord;
                                chosenEngineName = cEngine;
                                chosenManager = currentEngineManager;
                            }

                            // Attempt to read from this candidate at timestep 0
                            object candVal = null;
                            if (variantType != null && variantType.IsInstanceOfType(fieldCandidate))
                            {
                                candVal = InvokeMethod(fieldCandidate, variantType, "GetValue",
                                    new[] { typeof(int), typeof(int), typeof(int) },
                                    new object[] { selectedElementId, scenarioId, 0 });
                            }
                            else if (nonVariantType != null && nonVariantType.IsInstanceOfType(fieldCandidate))
                            {
                                candVal = InvokeMethod(fieldCandidate, nonVariantType, "GetValue",
                                    new[] { typeof(int), typeof(int) },
                                    new object[] { selectedElementId, scenarioId });
                            }
                            else if (fieldType != null)
                            {
                                candVal = InvokeMethod(fieldCandidate, fieldType, "GetValue",
                                    new[] { typeof(int) },
                                    new object[] { selectedElementId });
                            }

                            if (candVal != null)
                            {
                                if (candVal is bool)
                                {
                                    targetField = fieldCandidate;
                                    chosenFieldName = cField;
                                    chosenRecordName = cRecord;
                                    chosenEngineName = cEngine;
                                    chosenManager = currentEngineManager;
                                    raw = candVal;
                                    break;
                                }
                                if (candVal is double || candVal is float || candVal is int || candVal is long || candVal is decimal)
                                {
                                    double cd = Convert.ToDouble(candVal, CultureInfo.InvariantCulture);
                                    if (!double.IsNaN(cd) && !double.IsInfinity(cd))
                                    {
                                        targetField = fieldCandidate;
                                        chosenFieldName = cField;
                                        chosenRecordName = cRecord;
                                        chosenEngineName = cEngine;
                                        chosenManager = currentEngineManager;
                                        raw = candVal;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                result.FieldName = chosenFieldName ?? (candidateFieldNames.Length > 0 ? candidateFieldNames[0] : "");
                result.RecordName = chosenRecordName ?? "";
                result.EngineName = chosenEngineName ?? defaultEngine;
                result.FieldSupported = targetField != null;

                if (targetField == null)
                {
                    result.Status = (metricName == "HGL" || metricName == "Velocity") ? "NOT_AVAILABLE" : "FIELD_UNSUPPORTED";
                    return result;
                }

                object targetFieldHasRes = InvokeMethod(targetField, resultFieldType, "HasResults", new[] { typeof(int) }, new object[] { scenarioId });
                result.FieldHasResults = targetFieldHasRes is bool tfb && tfb;
                if (!result.FieldHasResults)
                {
                    result.Status = (metricName == "HGL" || metricName == "Velocity") ? "NOT_AVAILABLE" : "FIELD_HAS_RESULTS_FALSE";
                    return result;
                }

                // 4. Timestep resolution
                int timeStepIndex = 0;
                double timeSeconds = 0.0;
                double[] times = null;
                if (variantType != null && variantType.IsInstanceOfType(targetField))
                {
                    if (timeUnitType != null)
                    {
                        times = (double[])InvokeMethod(chosenManager, resultManagerType, "TimeSteps", new[] { timeUnitType },
                            new object[] { Enum.Parse(timeUnitType, "Seconds", false) });
                    }
                    else
                    {
                        times = (double[])InvokeMethod(chosenManager, resultManagerType, "TimeSteps", null, new object[] { 0 });
                    }
                    if (times != null && times.Length > 0)
                    {
                        timeSeconds = times[0];
                    }
                }
                result.TimeStepIndex = timeStepIndex;
                result.TimeSeconds = timeSeconds;

                // 5. Unit resolution
                if (unitized != null && unitIndex != null && unitized.IsInstanceOfType(targetField))
                {
                    object working = targetField.GetType().GetProperty("WorkingUnitIndex")?.GetValue(targetField, null);
                    if (working != null)
                    {
                        result.WorkingUnitIdentity = unitIndex.FullName + ":" + Enum.GetName(unitIndex, working) + ":" + Convert.ToInt64(working, CultureInfo.InvariantCulture);
                        string enumName = Enum.GetName(unitIndex, working);
                        result.WorkingUnitSymbol = P16CalculatedFlowDiagnostic.FlowUnit(result.WorkingUnitIdentity);
                        if (string.IsNullOrEmpty(result.WorkingUnitSymbol))
                        {
                            if (string.Equals(enumName, "VelocityFeetPerSecond", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(enumName, "FeetPerSecond", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(enumName, "FPS", StringComparison.OrdinalIgnoreCase))
                                result.WorkingUnitSymbol = "ft/s";
                            else if (string.Equals(enumName, "Feet", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(enumName, "Foot", StringComparison.OrdinalIgnoreCase))
                                result.WorkingUnitSymbol = "ft";
                            else if (string.Equals(enumName, "CubicFeetPerSecond", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(enumName, "CFS", StringComparison.OrdinalIgnoreCase))
                                result.WorkingUnitSymbol = P16CalculatedFlowDiagnostic.FlowUnit("Haestad.Support.Units.UnitConversionManager+UnitIndex:CubicFeetPerSecond:38");
                            else
                                result.WorkingUnitSymbol = enumName;
                        }
                    }
                }
                if (string.IsNullOrEmpty(result.WorkingUnitSymbol))
                {
                    result.WorkingUnitSymbol = defaultUnit;
                }

                // 6. Value extraction and non-finite resolution
                if (raw == null && targetField != null)
                {
                    if (variantType != null && variantType.IsInstanceOfType(targetField))
                    {
                        raw = InvokeMethod(targetField, variantType, "GetValue",
                            new[] { typeof(int), typeof(int), typeof(int) },
                            new object[] { selectedElementId, scenarioId, timeStepIndex });
                    }
                    else if (nonVariantType != null && nonVariantType.IsInstanceOfType(targetField))
                    {
                        raw = InvokeMethod(targetField, nonVariantType, "GetValue",
                            new[] { typeof(int), typeof(int) },
                            new object[] { selectedElementId, scenarioId });
                    }
                    else if (fieldType != null)
                    {
                        raw = InvokeMethod(targetField, fieldType, "GetValue",
                            new[] { typeof(int) },
                            new object[] { selectedElementId });
                    }
                }


                if (raw != null)
                {
                    result.RawValue = raw.ToString();
                    if (raw is bool b)
                    {
                        result.Classification = b ? "BOOLEAN_TRUE" : "BOOLEAN_FALSE";
                        result.Status = "SUCCESS";
                    }
                    else if (raw is double || raw is float || raw is int || raw is long || raw is short || raw is decimal)
                    {
                        double val = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                        result.RawValue = val.ToString("R", CultureInfo.InvariantCulture);
                        if (double.IsNaN(val) || double.IsInfinity(val))
                        {
                            result.NumericValue = null;
                            result.Classification = "INVALID_OR_MISSING";
                            result.Status = (metricName == "HGL" || metricName == "Velocity") ? "NOT_AVAILABLE" : "VALUE_NOT_FINITE";
                        }
                        else if (Math.Abs(val) < 1e-12)
                        {
                            result.NumericValue = val;
                            result.Classification = "OBSERVED_ZERO";
                            result.Status = "SUCCESS";
                        }
                        else
                        {
                            result.NumericValue = val;
                            result.Classification = "OBSERVED_FINITE";
                            result.Status = "SUCCESS";
                        }
                    }
                    else
                    {
                        result.Classification = "OBSERVED_VALUE";
                        result.Status = "SUCCESS";
                    }
                }
                else
                {
                    result.Classification = "INVALID_OR_MISSING";
                    result.Status = (metricName == "HGL" || metricName == "Velocity") ? "NOT_AVAILABLE" : "RAW_VALUE_NULL";
                }
            }
            catch (Exception ex)
            {
                result.Status = "EXCEPTION";
                result.ErrorMessage = ex.ToString();
            }
            finally
            {
                if (manager != null && resultOpenAttempted)
                {
                    try { (resultManagerType?.GetMethod("Close", Type.EmptyTypes) ?? manager.GetType().GetMethod("Close", Type.EmptyTypes))?.Invoke(manager, null); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsResultReader.cs: " + suppressedEx.Message); }
                }
                if (altEngineManager != null && altOpenAttempted)
                {
                    try { (resultManagerType?.GetMethod("Close", Type.EmptyTypes) ?? altEngineManager.GetType().GetMethod("Close", Type.EmptyTypes))?.Invoke(altEngineManager, null); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsResultReader.cs: " + suppressedEx.Message); }
                }
            }

            return result;
        }

        public static string ClassifyFlowValue(double? val)
        {
            if (!val.HasValue || double.IsNaN(val.Value) || double.IsInfinity(val.Value))
                return "INVALID_OR_MISSING";
            if (Math.Abs(val.Value) < 1e-12)
                return "OBSERVED_ZERO";
            return "OBSERVED_FINITE";
        }

        public static HydraulicResultSet ComposeNetworkResultSet(
            ResultCompositionContext context,
            NetworkGeometryInfo network,
            IEnumerable<HydraulicMetricObservation> observations)
        {
            return NetworkResultComposer.Compose(context, network, observations);
        }

        public static HydraulicResultSet ComposeNetworkResultSet(
            ResultCompositionContext context,
            GravityNetworkSnapshot snapshot,
            IEnumerable<HydraulicMetricObservation> observations)
        {
            return NetworkResultComposer.Compose(context, snapshot, observations);
        }

        public static HydraulicResultSet ComposeNetworkResultSet(
            ResultCompositionContext context,
            NetworkReadResult readResult,
            IEnumerable<HydraulicMetricObservation> observations)
        {
            return NetworkResultComposer.Compose(context, readResult, observations);
        }

        internal static void EnsureRequestedElement(int requestedId, int? actualId, string metricName)
        {
            if (actualId.HasValue && actualId.Value != requestedId)
            {
                throw new InvalidOperationException(
                    "REQUESTED_ELEMENT_MISMATCH: Requested conduit " + requestedId + " was replaced with fallback element " + actualId.Value + " during " + metricName + " read.");
            }
        }

        public static List<HydraulicMetricObservation> ReadNetworkObservations(
            object dataset,
            object project,
            int scenarioId,
            string numericalEngine,
            IEnumerable<int> conduitIds,
            IEnumerable<StructureReadDto> structures = null)
        {
            if (conduitIds == null) throw new ArgumentNullException(nameof(conduitIds));
            var reader = new WaterObjectsResultReader();
            var list = new List<HydraulicMetricObservation>();

            foreach (var conduitId in conduitIds)
            {
                // 1. Flow
                var flow = reader.ReadFlow(dataset, project, scenarioId, numericalEngine, conduitId);
                EnsureRequestedElement(conduitId, flow.ElementId, "Flow");
                var flowObs = HydraulicMetricObservation.FromFlowResult(flow, conduitId, numericalEngine);
                flowObs.ScenarioId = scenarioId;
                list.Add(flowObs);

                // 2. Velocity
                var vel = reader.ReadVelocity(dataset, project, scenarioId, numericalEngine, conduitId);
                EnsureRequestedElement(conduitId, vel.ElementId, "Velocity");
                var velObs = HydraulicMetricObservation.FromMetricResult(vel, HydraulicMetricKind.Velocity, "Conduit");
                velObs.ScenarioId = scenarioId;
                list.Add(velObs);

                // 3. Depth
                var depth = reader.ReadDepth(dataset, project, scenarioId, numericalEngine, conduitId);
                EnsureRequestedElement(conduitId, depth.ElementId, "Depth");
                var depthObs = HydraulicMetricObservation.FromMetricResult(depth, HydraulicMetricKind.Depth, "Conduit");
                depthObs.ScenarioId = scenarioId;
                list.Add(depthObs);

                // 4. Capacity
                var cap = reader.ReadCapacity(dataset, project, scenarioId, numericalEngine, conduitId);
                EnsureRequestedElement(conduitId, cap.ElementId, "Capacity");
                var capObs = HydraulicMetricObservation.FromMetricResult(cap, HydraulicMetricKind.Capacity, "Conduit");
                capObs.ScenarioId = scenarioId;
                list.Add(capObs);

                // 5. HGL
                var hgl = reader.ReadHgl(dataset, project, scenarioId, numericalEngine, conduitId);
                EnsureRequestedElement(conduitId, hgl.ElementId, "HGL");
                var hglObs = HydraulicMetricObservation.FromMetricResult(hgl, HydraulicMetricKind.Hgl, "Conduit");
                hglObs.ScenarioId = scenarioId;
                list.Add(hglObs);

                // 6. Surcharge
                var sur = reader.ReadSurcharge(dataset, project, scenarioId, numericalEngine, conduitId);
                EnsureRequestedElement(conduitId, sur.ElementId, "Surcharge");
                var surObs = HydraulicMetricObservation.FromMetricResult(sur, HydraulicMetricKind.Surcharge, "Conduit");
                surObs.ScenarioId = scenarioId;
                list.Add(surObs);
            }

            foreach (var node in structures ?? Enumerable.Empty<StructureReadDto>())
            {
                var hgl = reader.ReadNodeHgl(dataset, project, scenarioId, numericalEngine, node.Id, node.RuntimeCategory);
                EnsureRequestedElement(node.Id, hgl.ElementId, "StructureHGL");
                var observation = HydraulicMetricObservation.FromMetricResult(hgl, HydraulicMetricKind.Hgl, "Structure");
                observation.ScenarioId = scenarioId;
                list.Add(observation);
            }
            return list;
        }
    }
}
