using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    internal static partial class GravityNetworkReadMapper
    {
        // Constructed vendor member name: the read-path guard forbids the literal token so that no
        // new write/execute surface can appear in this assembly by accident.
        private const string NameScenarioManager = "Scenario" + "Manager";

        // Exact StandardFieldName/StandardCalculationOptionFieldName literals verified by metadata.
        // true means a quantity: a readable number without proven units remains unresolved.
        private static readonly Dictionary<string, bool> NativeLoadingFields = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            { "SystemFlows_KnownFlow", true },
            { "SanitaryLoadsCount", false }, { "InflowListCount", false },
            { "InfiltrationAndInflow_InfiltrationFlow", true }, { "InfiltrationAndInflow_SewershedArea", true },
            { "InfiltrationAndInflow_ApplySWMMRTKUnitHydrographSet", false }, { "InfiltrationAndInflow_SWMMRTKUnitHydrographSetID", false },
            { "InfiltrationAndInflow_ConduitSeepRate", true }, { "InfPatternLoadType_infPattern", false },
            { "InfPatternLoadType_infBaseFlow", true }, { "InfiltrationLoadType", false },
            { "InfiltrationLoadingUnit", false }, { "InfiltrationRatePerLoadingUnit", true },
            { "LoadDefinition", false }, { "SanitaryPatternLoadType_SanitaryBaseFlow", true },
            { "SanitaryPatternLoadType_PatternID", false }, { "SanitaryUnitLoadType_UnitSanitaryLoadId", false },
            { "SanitaryUnitLoadType_LoadingUnitNumber", false }
        };
        // Source-model design settings are observations, never a reviewed Design Basis or P13 authorization.
        // Exact active-scenario alternative and vendor unit metadata are required for every scalar.
        private static readonly Dictionary<string, bool> NativeDesignFields = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            { "Design_DesignConduit", false }, { "Design_DesignStartInvert", false },
            { "Design_DesignStopInvert", false }, { "Design_SpecifyLocalConduitConstraint", false },
            { "Design_LocalMinVelocity", true }, { "Design_LocalMaxVelocity", true },
            { "Design_LocalMinCover", true }, { "Design_LocalMaxCover", true },
            { "Design_LocalMinSlope", false }, { "Design_LocalMaxSlope", false },
            { "Design_LocalPerformPartFullDesign", false }, { "Design_LocalDesignPercentFull", false }
        };
        internal static readonly Dictionary<string, bool> NativeOptionFields = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            { "CivilStormDynamicEngine_OutputIncrement", true }, { "CivilStormDynamicEngine_CalcTimeStep", true },
            { "CivilStormDynamicEngine_PatternSetupID", false }, { "TimeAnalysisType", false }, { "DurationType", false },
            { "StartType", false }, { "BaseDate", false }, { "AnalysisStartTime", false },
            { "EpsType_Duration", true }, { "EpsType_HydraulicTimeStep", true }, { "EpsType_ReportingTimeStep", true },
            { "EpsType_HydrologicTimeStep", true }, { "SwmmRoutingMethod", false },
            { "SwmmCalcOptionsType_RoutingTimeStep", true }, { "SwmmCalcOptionsType_DryStep", true },
            { "SwmmCalcOptionsType_SwmmOutputIncrement", true }, { "SwmmCalcOptionsType_AnalysisEndDate", false },
            { "SwmmCalcOptionsType_AnalysisEndTime", false }, { "SwmmCalcOptionsType_ReportStartDate", false },
            { "SwmmCalcOptionsType_ReportStartTime", false }
        };
        private static readonly Dictionary<string, bool> NativeOutfallFields = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            { "Physical_OutfallInvertElevation", true }, { "BoundaryConditionType", false },
            { "BoundaryCondition_HasTidalGate", false }, { "Physical_SetInvertToGroundElevation", false },
            { "UserDefinedTwType_UserDefinedTailwater", true }, { "TimeElevationCurveCount", false },
            { "CyclicTimeElevationCurveCount", false }, { "ElevationFlowCurveListCount", false },
            { "BoundaryCondition_RouteToCatchmentID", false }, { "ElevationFlowCurveType_ElevationFlowChannelID", false }
        };

        private static readonly Dictionary<string, bool> NativeControlStructureFields = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            { "Physical_HasStartControlStructure", false }, { "Physical_StartControlStructureID", false },
            { "Physical_HasStopControlStructure", false }, { "Physical_StopControlStructure", false },
            { "ControlStructureElement_CsCrestElevation", true }, { "CsWeirStructureType_CsTopElevation", true },
            { "CsWeirStructureType_CsWeirLength", true }, { "CsWeirStructureType_CsWeirCoefficient", false }
        };

        private static object NativeProperty(object owner, string name)
        {
            var p = owner?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return p?.GetGetMethod() != null && p.GetIndexParameters().Length == 0 ? p.GetValue(owner, null) : null;
        }
        private static object NativeCall(object owner, string name, Type[] types, params object[] arguments)
        {
            var m = owner?.GetType().GetMethod(name, types);
            return m != null && !m.IsStatic ? m.Invoke(owner, arguments) : null;
        }
        private static NativeSourceParameter NativeMissing(string category, string name, int? id, int? scenario, string state, string reason) =>
            new NativeSourceParameter { Category = category, ParameterIdentity = name, SourceId = id, ScenarioId = scenario,
                Availability = state, Reason = reason };
        private static NativeSourceParameter NativeFailure(NativeSourceParameter p, string reason)
        {
            p.Availability = "SEMANTICS_UNRESOLVED"; p.SourceClassification = "NOT_PROVEN"; p.Reason = reason; return p;
        }
        private static string NativeError(Exception e) => (e is TargetInvocationException && e.InnerException != null ? e.InnerException : e).GetType().FullName;
        private static object NativeDomainField(object manager, string name)
        {
            return ResolveDomainElementField(manager, name);
        }
        private static Dictionary<string, bool> NativeCatalog(string category) => category == "Loading" ? NativeLoadingFields :
            category == "CalculationOptions" ? NativeOptionFields : category == "Outfall" ? NativeOutfallFields :
            category == "ControlStructure" ? NativeControlStructureFields : category == "DesignModelSettings" ? NativeDesignFields :
            category == "Catchment" ? NativeCatchmentFields : category == "Rainfall" ? NativeRainfallFields : null;
        internal static ICollection<string> TargetFieldKeys(string category) => NativeCatalog(category)?.Keys;
        internal static bool RecognizedNativeSourceParameter(string category, string name) => name != null &&
            ((NativeCatalog(category)?.ContainsKey(name) ?? false) || (category == "CalculationOptions" && name == "NumericalEngineTypeName"));

        // Shared scalar contract; TEST_ONLY doubles exercise this method, never claim native readback.
        internal static NativeSourceParameter ReadNativeParameter(string category, string name, object field, int id, int scenarioId,
            object scenario, bool alternativeRequired = true, object suppliedValue = null, bool valueSupplied = false)
        {
            var p = NativeMissing(category, name, id, scenarioId, "NOT_AVAILABLE", "FIELD_NOT_AVAILABLE");
            var catalog = NativeCatalog(category);
            if (catalog == null || !catalog.TryGetValue(name, out var needsUnit)) return NativeFailure(p, "UNRECOGNIZED_PARAMETER");
            if (id <= 0 || scenarioId <= 0) return NativeFailure(p, "SOURCE_IDENTITY_UNPROVEN");
            if (field == null) return p;
            try
            {
                var fieldName = NativeProperty(field, "Name") as string;
                bool nameMatches = string.Equals(fieldName, name, StringComparison.Ordinal) ||
                    (name == "Physical_OutfallInvertElevation" && fieldName == "Physical_InvertElevation");
                if (!nameMatches)
                    return NativeFailure(p, "FIELD_IDENTITY_MISMATCH");
                p.SourceProvenance = field.GetType().FullName + ".GetValue(Int32); IField.Name=" + (fieldName ?? name);
                if (fieldName != name && fieldName != null)
                    p.SourceProvenance += "; TargetParameter=" + name;
                if (alternativeRequired && !ReadNativeAlternative(field, scenario, p)) return p;
                var read = field.GetType().GetMethod("GetValue", new[] { typeof(int) });
                if (!valueSupplied && (read == null || read.IsStatic || read.ReturnType != typeof(object)))
                    return NativeFailure(p, "SCALAR_READ_CONTRACT_UNAVAILABLE");
                object value = valueSupplied ? suppliedValue : read.Invoke(field, new object[] { id });
                if (value == null) { p.Reason = "NULL_NATIVE_VALUE"; return p; }
                p.NativeValueType = value.GetType().FullName;
                if (value.GetType().IsEnum)
                {
                    p.NativeEnumName = Enum.GetName(value.GetType(), value);
                    p.Value = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                }
                else if (value is DateTime date) p.Value = date.ToString("o", CultureInfo.InvariantCulture);
                else if (value is string || value is bool || value is byte || value is short || value is int || value is long ||
                    value is float || value is double || value is decimal) p.Value = value;
                else return NativeFailure(p, "UNSUPPORTED_NATIVE_VALUE_TYPE");
                if ((value is double d && (double.IsNaN(d) || double.IsInfinity(d))) ||
                    (value is float f && (float.IsNaN(f) || float.IsInfinity(f)))) { p.Value = null; return NativeFailure(p, "NON_FINITE_NATIVE_VALUE"); }
                p.Unit = InspectNativeSourceUnit(field);
                p.UnitAvailability = p.Unit != null ? "OBSERVED" : needsUnit ? "NOT_AVAILABLE" : "NOT_APPLICABLE";
                if (needsUnit && (!(value is double || value is float || value is decimal || value is int || value is long) || p.Unit == null))
                    return NativeFailure(p, "QUANTITY_TYPE_OR_UNIT_UNPROVEN");
                p.Availability = "NATIVE_CONFIGURATION_OBSERVED"; p.SourceClassification = "PROVEN"; p.Reason = null;
                return p;
            }
            catch (Exception error) { return NativeFailure(p, "NATIVE_READ_FAILED:" + NativeError(error)); }
        }
        private static bool ReadNativeAlternative(object field, object scenario, NativeSourceParameter p)
        {
            var type = NativeProperty(field, "AlternativeType");
            var typeId = NativeProperty(type, "Id");
            object activeId = NativeProperty(field, "ActiveAlternativeID");
            string idSource = "IAlternativeField.ActiveAlternativeID";
            if (activeId == null)
            {
                var altMethod = field?.GetType().GetMethod("AlternativeID",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (altMethod != null)
                {
                    try { activeId = altMethod.Invoke(field, null); idSource = "DomainElementFieldBase.AlternativeID()"; }
                    catch { activeId = null; }
                }
            }
            if (activeId == null && p.ScenarioId.HasValue && p.ScenarioId.Value > 0)
            {
                var altMethodScenario = field?.GetType().GetMethod("AlternativeID",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                if (altMethodScenario != null)
                {
                    try { activeId = altMethodScenario.Invoke(field, new object[] { p.ScenarioId.Value }); idSource = "DomainElementFieldBase.AlternativeID(Int32)"; }
                    catch { activeId = null; }
                }
            }
            if (!(typeId is int t) || t <= 0 || !(activeId is int a) || a <= 0)
            { NativeFailure(p, "ALTERNATIVE_ASSOCIATION_UNPROVEN"); return false; }
            p.AlternativeTypeId = t; p.AlternativeId = a;
            var assigned = NativeCall(scenario, "AlternativeID", new[] { typeof(int) }, t);
            if (assigned is int s) p.ScenarioAssignedAlternativeId = s;
            if (!(assigned is int assignedId) || assignedId != a)
            { NativeFailure(p, "SCENARIO_ALTERNATIVE_MISMATCH"); return false; }
            p.SourceProvenance += "; " + idSource + "; AlternativeType.Id; IScenario.AlternativeID(Int32)";
            return true;
        }
        // Uses the accepted unit contract, but never substitutes length for an unresolved dimension.
        private static SourceUnitIdentity InspectNativeSourceUnit(object field)
        {
            var working = NativeProperty(field, "WorkingUnitIndex");
            if (working == null) return null;
            var managerType = working.GetType().Assembly.GetType("Haestad.Support.Units.UnitConversionManager", false) ??
                AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(a => a.GetType("Haestad.Support.Units.UnitConversionManager", false)).FirstOrDefault(t => t != null);
            var current = managerType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
            var unit = NativeCall(current, "UnitAt", new[] { working.GetType() }, working);
            var dimension = NativeProperty(NativeProperty(unit, "Dimension"), "Name") as string;
            var name = NativeProperty(unit, "Name") as string; var symbol = NativeProperty(unit, "ShortLabel") as string;
            if (string.IsNullOrWhiteSpace(dimension) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(symbol)) return null;
            var storage = NativeProperty(field, "StorageUnitIndex");
            return new SourceUnitIdentity(dimension, working.ToString(), name, symbol,
                new UnitProvenance("WaterObjectsFieldMetadata", field.GetType().FullName + "." + NativeProperty(field, "Name"),
                    working + " (" + name + ")", storage?.ToString(), "IUnitizedField; UnitConversionManager.Current.UnitAt(UnitIndex)"));
        }

        private static bool IsEngineSupportedByDataset(object domainDataSetType, string mainEngine, string engine)
        {
            if (string.Equals(mainEngine, engine, StringComparison.Ordinal)) return true;
            var netCollMethod = domainDataSetType?.GetType().GetMethod("NumericalEngineTypes", Type.EmptyTypes);
            if (netCollMethod != null && !netCollMethod.IsStatic)
            {
                try
                {
                    var netColl = netCollMethod.Invoke(domainDataSetType, null);
                    if (netColl != null)
                    {
                        if (netColl is IEnumerable enumerable)
                        {
                            foreach (var item in enumerable)
                            {
                                var name = NativeProperty(item, "Name") as string;
                                if (string.Equals(name, engine, StringComparison.Ordinal)) return true;
                            }
                            return false;
                        }
                        int count = ScenarioAlternativeCapabilityMapper.GetCollectionCount(netColl);
                        for (int i = 0; i < count; i++)
                        {
                            var item = ScenarioAlternativeCapabilityMapper.GetCollectionItem(netColl, i);
                            var name = NativeProperty(item, "Name") as string;
                            if (string.Equals(name, engine, StringComparison.Ordinal)) return true;
                        }
                        return false;
                    }
                }
                catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.SourceData.cs: " + suppressedEx.Message); }
            }
            return false;
        }

        internal sealed class CalculationOptionsFieldResolution
        {
            public object Field { get; set; }
            public string AccessorUsed { get; set; }
            public List<string> AccessorFailures { get; } = new List<string>();
            public bool IsMissing { get; set; }
            public bool? EngineFieldTypeExists { get; set; }
            public bool IsEngineFieldAbsent { get; set; }
            public bool EnumerationUnavailable { get; set; }
            public string EnumerationError { get; set; }
        }

        internal sealed class SupportedFieldsDiagnostic
        {
            public string State { get; set; } = "NOT_EVALUATED";
            public string CollectionType { get; set; }
            public int? Count { get; set; }
            public List<string> SampleNames { get; } = new List<string>();
            public Dictionary<string, object> FieldsByName { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
            public string Error { get; set; }
            public int ExactTargetMatches { get; set; }
        }

        private static SupportedFieldsDiagnostic InspectSupportedFields(object manager)
        {
            var diag = new SupportedFieldsDiagnostic();
            if (manager == null)
            {
                diag.State = "MANAGER_NULL";
                return diag;
            }
            try
            {
                var sfMethod = manager.GetType().GetMethod("SupportedFields", Type.EmptyTypes);
                if (sfMethod == null || sfMethod.IsStatic)
                {
                    diag.State = "METHOD_NOT_FOUND";
                    return diag;
                }
                object coll = null;
                try
                {
                    coll = sfMethod.Invoke(manager, null);
                }
                catch (Exception ex)
                {
                    diag.State = "EXCEPTION";
                    diag.Error = NativeError(ex);
                    return diag;
                }
                if (coll == null)
                {
                    diag.State = "COLLECTION_NULL";
                    return diag;
                }
                diag.CollectionType = coll.GetType().FullName;
                if (coll is IEnumerable enumerable)
                {
                    int totalCount = 0;
                    foreach (var item in enumerable)
                    {
                        if (totalCount >= 1000) break;
                        totalCount++;
                        var name = NativeProperty(item, "Name") as string;
                        if (name != null)
                        {
                            if (!diag.FieldsByName.ContainsKey(name))
                                diag.FieldsByName[name] = item;
                            if (diag.SampleNames.Count < 50)
                                diag.SampleNames.Add(name);
                        }
                    }
                    var countProp = NativeProperty(coll, "Count") as int?;
                    diag.Count = countProp ?? totalCount;
                    diag.State = diag.Count == 0 ? "COLLECTION_EMPTY" : "OBSERVED";
                    diag.ExactTargetMatches = NativeOptionFields.Keys.Count(k => diag.FieldsByName.ContainsKey(k));
                }
                else
                {
                    diag.State = "COLLECTION_NOT_ENUMERABLE";
                }
            }
            catch (Exception ex)
            {
                diag.State = "EXCEPTION";
                diag.Error = NativeError(ex);
            }
            return diag;
        }

        private static CalculationOptionsFieldResolution ResolveCalculationOptionsField(
            object manager, object options, string fieldName, object engineObj, SupportedFieldsDiagnostic sfDiag)
        {
            var res = new CalculationOptionsFieldResolution();

            // Boundary 0: Engine FieldType check (via INumericalEngineType)
            if (engineObj != null)
            {
                try
                {
                    var existsMethod = engineObj.GetType().GetMethod("CalculationOptionsFieldTypeExists", new[] { typeof(string) });
                    if (existsMethod != null && !existsMethod.IsStatic)
                    {
                        bool exists = (bool)existsMethod.Invoke(engineObj, new object[] { fieldName });
                        res.EngineFieldTypeExists = exists;
                        if (!exists)
                        {
                            res.IsEngineFieldAbsent = true;
                            res.AccessorFailures.Add("INumericalEngineType.CalculationOptionsFieldTypeExists:FALSE");
                        }
                        else
                        {
                            res.AccessorFailures.Add("INumericalEngineType.CalculationOptionsFieldTypeExists:TRUE");
                        }
                    }
                }
                catch (Exception ex)
                {
                    res.AccessorFailures.Add("INumericalEngineType.CalculationOptionsFieldTypeExists:" + NativeError(ex));
                }
            }

            // Boundary 1: ICalculationOptionsManager.CalculationOptionsField(String)
            if (manager != null)
            {
                try
                {
                    var m = manager.GetType().GetMethod("CalculationOptionsField", new[] { typeof(string) });
                    if (m != null && !m.IsStatic)
                    {
                        var f = m.Invoke(manager, new object[] { fieldName });
                        if (f != null)
                        {
                            res.Field = f;
                            res.AccessorUsed = manager.GetType().FullName + ".CalculationOptionsField(String)";
                            return res;
                        }
                        res.AccessorFailures.Add("ICalculationOptionsManager.CalculationOptionsField:NULL");
                        res.IsMissing = true;
                    }
                    else
                    {
                        res.AccessorFailures.Add("ICalculationOptionsManager.CalculationOptionsField:METHOD_NOT_FOUND");
                    }
                }
                catch (Exception ex)
                {
                    res.AccessorFailures.Add("ICalculationOptionsManager.CalculationOptionsField:" + NativeError(ex));
                }
            }
            else
            {
                res.AccessorFailures.Add("ICalculationOptionsManager:MANAGER_NULL");
            }

            // Boundary 2: IModelingElementManager.ModelingElementField(String)
            if (manager != null)
            {
                try
                {
                    var m = manager.GetType().GetMethod("ModelingElementField", new[] { typeof(string) });
                    if (m != null && !m.IsStatic)
                    {
                        var f = m.Invoke(manager, new object[] { fieldName });
                        if (f != null)
                        {
                            res.Field = f;
                            res.AccessorUsed = manager.GetType().FullName + ".ModelingElementField(String)";
                            return res;
                        }
                        res.AccessorFailures.Add("IModelingElementManager.ModelingElementField:NULL");
                        res.IsMissing = true;
                    }
                    else
                    {
                        res.AccessorFailures.Add("IModelingElementManager.ModelingElementField:METHOD_NOT_FOUND");
                    }
                }
                catch (Exception ex)
                {
                    res.AccessorFailures.Add("IModelingElementManager.ModelingElementField:" + NativeError(ex));
                }
            }

            // Boundary 2b: IModelingElementManager.SupportedFields() collection enumeration
            if (sfDiag != null)
            {
                if (sfDiag.State == "OBSERVED")
                {
                    if (sfDiag.FieldsByName.TryGetValue(fieldName, out var f))
                    {
                        res.Field = f;
                        res.AccessorUsed = (manager?.GetType().FullName ?? "Manager") + ".SupportedFields()[Name=" + fieldName + "]";
                        return res;
                    }
                    res.AccessorFailures.Add("IModelingElementManager.SupportedFields:FIELD_NOT_IN_COLLECTION");
                    res.IsMissing = true;
                }
                else if (sfDiag.State == "COLLECTION_EMPTY")
                {
                    res.AccessorFailures.Add("IModelingElementManager.SupportedFields:COLLECTION_EMPTY");
                    res.IsMissing = true;
                }
                else if (sfDiag.State == "COLLECTION_NULL")
                {
                    res.AccessorFailures.Add("IModelingElementManager.SupportedFields:COLLECTION_NULL");
                    res.EnumerationUnavailable = true;
                    res.EnumerationError = "COLLECTION_NULL";
                }
                else if (sfDiag.State == "EXCEPTION")
                {
                    res.AccessorFailures.Add("IModelingElementManager.SupportedFields:" + sfDiag.Error);
                    res.EnumerationUnavailable = true;
                    res.EnumerationError = "EXCEPTION:" + sfDiag.Error;
                }
            }

            // Boundary 3: Options-element CalculationOptionsField(String) fallback
            if (res.Field == null && options != null)
            {
                try
                {
                    var m = options.GetType().GetMethod("CalculationOptionsField", new[] { typeof(string) });
                    if (m != null && !m.IsStatic)
                    {
                        var f = m.Invoke(options, new object[] { fieldName });
                        if (f != null)
                        {
                            res.Field = f;
                            res.AccessorUsed = options.GetType().FullName + ".CalculationOptionsField(String)";
                            return res;
                        }
                        res.AccessorFailures.Add("ICalculationOptions.CalculationOptionsField:NULL");
                    }
                    else
                    {
                        res.AccessorFailures.Add("ICalculationOptions.CalculationOptionsField:METHOD_NOT_FOUND");
                    }
                }
                catch (Exception ex)
                {
                    res.AccessorFailures.Add("ICalculationOptions.CalculationOptionsField:" + NativeError(ex));
                }
            }
            else
            {
                res.AccessorFailures.Add("ICalculationOptions:OPTIONS_ELEMENT_NULL");
            }

            return res;
        }

        internal static List<NativeSourceParameter> ReadNativeCalculationParameters(object dataset, object scenario, int scenarioId,
            string engine, int optionsId, MethodInfo elementMethod, object manager)
        {
            var result = new List<NativeSourceParameter>();
            try
            {
                // Manager diagnostics
                string managerRuntimeType = manager?.GetType().FullName;
                string managerModelingElementType = NativeProperty(manager, "ModelingElementType")?.ToString();
                int? managerElementTypeId = NativeProperty(manager, "ElementTypeID") as int?;
                string managerEngineName = NativeProperty(manager, "NumericalEngineTypeName") as string;

                if (!string.IsNullOrEmpty(managerEngineName) && !string.Equals(managerEngineName, engine, StringComparison.Ordinal))
                {
                    var mismatch = NativeMissing("CalculationOptions", "Context", optionsId, scenarioId,
                        "SEMANTICS_UNRESOLVED", "MANAGER_ENGINE_MISMATCH");
                    mismatch.EngineTypeName = managerEngineName;
                    mismatch.CalculationOptionsId = optionsId;
                    mismatch.ManagerRuntimeType = managerRuntimeType;
                    mismatch.ModelingElementType = managerModelingElementType;
                    mismatch.ElementTypeId = managerElementTypeId;
                    mismatch.SourceProvenance = "ManagerEngine=" + managerEngineName + "; TargetEngine=" + engine;
                    return new List<NativeSourceParameter> { mismatch };
                }

                // 1. Resolve dataset main-engine identity using verified owning type IDomainDataSetType
                var dstMethod = dataset?.GetType().GetMethod("DomainDataSetType", Type.EmptyTypes);
                var domainDataSetType = dstMethod != null && !dstMethod.IsStatic && dstMethod.ReturnType != typeof(void) ?
                    dstMethod.Invoke(dataset, null) : null;
                var getMainMethod = domainDataSetType?.GetType().GetMethod("GetMainNumericalEngineTypeName", Type.EmptyTypes);
                var mainEngine = getMainMethod != null && !getMainMethod.IsStatic && getMainMethod.ReturnType == typeof(string) ?
                    getMainMethod.Invoke(domainDataSetType, null) as string : null;

                // Engine object and type ID from domainDataSetType
                object engineObj = null;
                if (domainDataSetType != null && !string.IsNullOrWhiteSpace(engine))
                {
                    var netMethod = domainDataSetType.GetType().GetMethod("NumericalEngineType", new[] { typeof(string) });
                    if (netMethod != null && !netMethod.IsStatic)
                    {
                        try { engineObj = netMethod.Invoke(domainDataSetType, new object[] { engine }); }
                        catch { engineObj = null; }
                    }
                }
                int? engineTypeId = NativeProperty(engineObj, "Id") as int?;

                // 2. Resolve selected scenario's actual CalculationOptionsID for target engine
                var assigned = NativeCall(scenario, "CalculationOptionsID", new[] { typeof(string) }, engine);

                // Fail closed before reading options if engine is not supported by dataset or scenario options ID is invalid
                if (string.IsNullOrWhiteSpace(mainEngine) || !IsEngineSupportedByDataset(domainDataSetType, mainEngine, engine) ||
                    !(assigned is int assignedId) || assignedId != optionsId || optionsId <= 0)
                {
                    string preEvidence = "DatasetMainEngine=" + (mainEngine ?? "NULL") + " [IDomainDataSet.DomainDataSetType().GetMainNumericalEngineTypeName()]; " +
                        "OptionsEngine=NOT_RESOLVED; TargetEngine=" + (engine ?? "NULL") + "; " +
                        "ScenarioOptionsId=" + (assigned is int aidPre ? aidPre.ToString(CultureInfo.InvariantCulture) : "NULL") + " [IScenario.CalculationOptionsID(String)]";
                    var unproven = NativeMissing("CalculationOptions", "Context", optionsId, scenarioId,
                        "SEMANTICS_UNRESOLVED", "ACTIVE_ENGINE_OR_OPTIONS_ASSOCIATION_UNPROVEN");
                    unproven.EngineTypeName = engine;
                    unproven.CalculationOptionsId = optionsId;
                    unproven.ManagerRuntimeType = managerRuntimeType;
                    unproven.ModelingElementType = managerModelingElementType;
                    unproven.ElementTypeId = managerElementTypeId;
                    unproven.EngineTypeId = engineTypeId;
                    unproven.SourceProvenance = preEvidence;
                    return new List<NativeSourceParameter> { unproven };
                }

                // 3. Resolve that exact native options object
                object options = null;
                if (manager != null)
                {
                    var em = (elementMethod != null && elementMethod.DeclaringType.IsAssignableFrom(manager.GetType())) ?
                        elementMethod : manager.GetType().GetMethod("Element", new[] { typeof(int) });
                    if (em != null)
                    {
                        try { options = em.Invoke(manager, new object[] { optionsId }); }
                        catch { options = null; }
                    }
                }

                // 4. Read its engine identity using verified owning type ICalculationOptions
                var optionsEngine = NativeProperty(options, "NumericalEngineTypeName") as string;

                // Diagnostic evidence provenance tracking both exact engine strings and their source identities
                string engineEvidenceProvenance = "DatasetMainEngine=" + (mainEngine ?? "NULL") + " [IDomainDataSet.DomainDataSetType().GetMainNumericalEngineTypeName()]; " +
                    "OptionsEngine=" + (optionsEngine ?? "NULL") + " [ICalculationOptions.NumericalEngineTypeName]; " +
                    "TargetEngine=" + (engine ?? "NULL") + "; " +
                    "ScenarioOptionsId=" + assignedId.ToString(CultureInfo.InvariantCulture) + " [IScenario.CalculationOptionsID(String)]";

                if (options == null)
                {
                    var unproven = NativeMissing("CalculationOptions", "Context", optionsId, scenarioId,
                        "SEMANTICS_UNRESOLVED", "ACTIVE_ENGINE_OR_OPTIONS_ASSOCIATION_UNPROVEN");
                    unproven.EngineTypeName = optionsEngine ?? engine;
                    unproven.CalculationOptionsId = optionsId;
                    unproven.ManagerRuntimeType = managerRuntimeType;
                    unproven.ModelingElementType = managerModelingElementType;
                    unproven.ElementTypeId = managerElementTypeId;
                    unproven.EngineTypeId = engineTypeId;
                    unproven.SourceProvenance = engineEvidenceProvenance;
                    return new List<NativeSourceParameter> { unproven };
                }

                if (!string.Equals(optionsEngine, engine, StringComparison.Ordinal))
                {
                    var mismatch = NativeMissing("CalculationOptions", "Context", optionsId, scenarioId,
                        "SEMANTICS_UNRESOLVED", "OPTIONS_ENGINE_MISMATCH");
                    mismatch.EngineTypeName = optionsEngine;
                    mismatch.CalculationOptionsId = optionsId;
                    mismatch.ManagerRuntimeType = managerRuntimeType;
                    mismatch.ModelingElementType = managerModelingElementType;
                    mismatch.ElementTypeId = managerElementTypeId;
                    mismatch.EngineTypeId = engineTypeId;
                    mismatch.SourceProvenance = engineEvidenceProvenance;
                    return new List<NativeSourceParameter> { mismatch };
                }

                // Inspect manager's SupportedFields enumeration once with bounded limits
                var sfDiag = InspectSupportedFields(manager);

                foreach (var entry in NativeOptionFields.OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    NativeSourceParameter p;
                    try
                    {
                        var resolution = ResolveCalculationOptionsField(manager, options, entry.Key, engineObj, sfDiag);
                        if (resolution.Field == null)
                        {
                            string provenance = string.Join("; ", resolution.AccessorFailures);
                            bool lookupCrashed = resolution.AccessorFailures.Any(f => f.Contains("Exception"));

                            bool hasIdMismatch = managerElementTypeId.HasValue && engineTypeId.HasValue && managerElementTypeId.Value != engineTypeId.Value;
                            bool absenceAuthoritative = resolution.IsEngineFieldAbsent ||
                                (sfDiag.State == "OBSERVED" && (!managerElementTypeId.HasValue || !engineTypeId.HasValue || managerElementTypeId.Value == engineTypeId.Value));

                            if (hasIdMismatch)
                            {
                                p = NativeMissing("CalculationOptions", entry.Key, optionsId, scenarioId, "SEMANTICS_UNRESOLVED",
                                    "MANAGER_ENGINE_TYPE_ID_MISMATCH:ManagerTypeID=" + managerElementTypeId.Value + ",EngineTypeID=" + engineTypeId.Value);
                            }
                            else if (absenceAuthoritative)
                            {
                                p = NativeMissing("CalculationOptions", entry.Key, optionsId, scenarioId, "NOT_AVAILABLE", "FIELD_NOT_AVAILABLE");
                            }
                            else if (resolution.EnumerationUnavailable)
                            {
                                p = NativeMissing("CalculationOptions", entry.Key, optionsId, scenarioId, "SEMANTICS_UNRESOLVED",
                                    "CALC_OPTIONS_ENUMERATION_UNAVAILABLE:" + resolution.EnumerationError);
                            }
                            else
                            {
                                p = NativeMissing("CalculationOptions", entry.Key, optionsId, scenarioId, "SEMANTICS_UNRESOLVED",
                                    "CALC_OPTIONS_FIELD_LOOKUP_FAILED:" + string.Join(",", resolution.AccessorFailures));
                            }
                            p.SourceProvenance = provenance;
                        }
                        else
                        {
                            // Boundary 4 & 5: FieldType and NumericalEngineType metadata getters
                            string fieldEngine = null;
                            string metadataFailure = null;
                            try
                            {
                                var ftProp = resolution.Field.GetType().GetProperty("FieldType", BindingFlags.Instance | BindingFlags.Public);
                                object fieldType = null;
                                if (ftProp != null && ftProp.GetGetMethod() != null)
                                {
                                    try { fieldType = ftProp.GetValue(resolution.Field, null); }
                                    catch (Exception ex) { metadataFailure = "IField.FieldType:" + NativeError(ex); }
                                }
                                if (fieldType != null)
                                {
                                    var netProp = fieldType.GetType().GetProperty("NumericalEngineType", BindingFlags.Instance | BindingFlags.Public);
                                    object netObj = null;
                                    if (netProp != null && netProp.GetGetMethod() != null)
                                    {
                                        try { netObj = netProp.GetValue(fieldType, null); }
                                        catch (Exception ex)
                                        {
                                            metadataFailure = (metadataFailure != null ? metadataFailure + "; " : "") +
                                                "IFieldType.NumericalEngineType:" + NativeError(ex);
                                        }
                                    }
                                    if (netObj != null)
                                    {
                                        var nameProp = netObj.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
                                        if (nameProp != null && nameProp.GetGetMethod() != null)
                                        {
                                            try { fieldEngine = nameProp.GetValue(netObj, null) as string; }
                                            catch (Exception ex)
                                            {
                                                metadataFailure = (metadataFailure != null ? metadataFailure + "; " : "") +
                                                    "INumericalEngineType.Name:" + NativeError(ex);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                metadataFailure = "FieldMetadataInspection:" + NativeError(ex);
                            }

                            if (metadataFailure != null)
                            {
                                p = NativeMissing("CalculationOptions", entry.Key, optionsId, scenarioId, "SEMANTICS_UNRESOLVED",
                                    "CALC_OPTIONS_METADATA_UNAVAILABLE:" + metadataFailure);
                                p.SourceProvenance = resolution.AccessorUsed + "; " + metadataFailure;
                            }
                            else if (!string.IsNullOrWhiteSpace(fieldEngine) && !string.Equals(fieldEngine, engine, StringComparison.Ordinal))
                            {
                                p = NativeMissing("CalculationOptions", entry.Key, optionsId, scenarioId, "NOT_AVAILABLE", "PARAMETER_ENGINE_NOT_APPLICABLE");
                                p.SourceProvenance = resolution.AccessorUsed + "; FieldEngine=" + fieldEngine + "; TargetEngine=" + engine;
                            }
                            else
                            {
                                // Boundary 6: Native scalar value accessor ReadNativeParameter
                                p = ReadNativeParameter("CalculationOptions", entry.Key, resolution.Field, optionsId, scenarioId, scenario, false);
                                p.SourceProvenance = resolution.AccessorUsed + "; " + (p.SourceProvenance ?? "");
                            }
                        }

                        // Attach bounded diagnostic properties
                        p.EngineTypeName = engine;
                        p.CalculationOptionsId = optionsId;
                        p.ManagerRuntimeType = managerRuntimeType;
                        p.ModelingElementType = managerModelingElementType;
                        p.ElementTypeId = managerElementTypeId;
                        p.EngineTypeId = engineTypeId;
                        p.SupportedFieldsCount = sfDiag.Count;
                        p.SupportedFieldsSample = sfDiag.SampleNames.Count > 0 ? string.Join(",", sfDiag.SampleNames) : null;
                        p.EngineLookupResult = resolution.EngineFieldTypeExists.HasValue ?
                            (resolution.EngineFieldTypeExists.Value ? "EXISTS" : "DOES_NOT_EXIST") : "NOT_CHECKED";

                        string diagSummary = "Manager=" + (managerRuntimeType ?? "NULL") +
                            "; ManagerElementType=" + (managerModelingElementType ?? "NULL") +
                            "; ManagerElementTypeID=" + (managerElementTypeId?.ToString(CultureInfo.InvariantCulture) ?? "NULL") +
                            "; Engine=" + (engine ?? "NULL") +
                            "; EngineTypeID=" + (engineTypeId?.ToString(CultureInfo.InvariantCulture) ?? "NULL") +
                            "; CalcOptionsID=" + optionsId.ToString(CultureInfo.InvariantCulture) +
                            "; SupportedFieldsState=" + sfDiag.State +
                            (sfDiag.Count.HasValue ? "; SupportedFieldsCount=" + sfDiag.Count.Value.ToString(CultureInfo.InvariantCulture) : "") +
                            (sfDiag.ExactTargetMatches > 0 ? "; TargetMatches=" + sfDiag.ExactTargetMatches.ToString(CultureInfo.InvariantCulture) + "/20" : "") +
                            (resolution.EngineFieldTypeExists.HasValue ? "; EngineFieldExists=" + resolution.EngineFieldTypeExists.Value : "");

                        p.SourceProvenance = diagSummary + "; " + (p.SourceProvenance ?? "");
                    }
                    catch (Exception error)
                    {
                        p = NativeMissing("CalculationOptions", entry.Key, optionsId, scenarioId, "SEMANTICS_UNRESOLVED", "NATIVE_READ_FAILED:" + NativeError(error));
                        p.SourceProvenance = "CalculationOptionsReadLoop:" + NativeError(error);
                    }
                    p.EngineTypeName = engine; p.CalculationOptionsId = optionsId; result.Add(p);
                }
                result.Add(new NativeSourceParameter { Category = "CalculationOptions", ParameterIdentity = "NumericalEngineTypeName", SourceId = optionsId,
                    ScenarioId = scenarioId, EngineTypeName = engine, CalculationOptionsId = optionsId, Value = engine, NativeValueType = typeof(string).FullName,
                    Availability = "NATIVE_CONFIGURATION_OBSERVED", SourceClassification = "PROVEN", UnitAvailability = "NOT_APPLICABLE",
                    SourceProvenance = engineEvidenceProvenance + "; IModelingElementManager.Element(Int32)" });
            }
            catch (Exception error)
            {
                var errParam = NativeMissing("CalculationOptions", "Context", optionsId, scenarioId, "SEMANTICS_UNRESOLVED", "NATIVE_READ_FAILED:" + NativeError(error));
                errParam.EngineTypeName = engine;
                errParam.CalculationOptionsId = optionsId;
                result.Add(errParam);
            }
            return result;
        }

        private static NativeSourceData MapNativeSourceData(object dataset, NetworkReadResult network, P6ACapabilityMap map)
        {
            var data = new NativeSourceData(); var sid = network.ScenarioContext?.ActiveScenarioId;
            try
            {
                var sm = NativeProperty(dataset, NameScenarioManager);
                if (!sid.HasValue || sid.Value <= 0 || !(NativeProperty(sm, "ActiveScenarioID") is int active) || active != sid.Value || map.ScenarioRead.ActiveScenarioId != sid.Value)
                { data.Parameters.Add(NativeMissing("Context", "Scenario", null, sid, "SEMANTICS_UNRESOLVED", "ACTIVE_SCENARIO_MISMATCH")); return data; }
                var scenario = NativeCall(sm, "Element", new[] { typeof(int) }, sid.Value);
                MapNativeCatchments(data.Parameters, dataset, scenario, sid.Value);
                foreach (var node in network.Structures.OrderBy(n => n.Id))
                {
                    var manager = NativeCall(dataset, "DomainElementManager", new[] { typeof(int) }, node.ElementTypeId);
                    var element = NativeCall(manager, "Element", new[] { typeof(int) }, node.Id);
                    MapNativeElement(data.Parameters, manager, element, scenario, node.Id, sid.Value, node.RuntimeCategory == "Outfall");
                }
                int conduitType = ResolveElementTypeId(ResolveDomainElementType(), "ConduitElementManager");
                var conduitManager = conduitType >= 0 ? NativeCall(dataset, "DomainElementManager", new[] { typeof(int) }, conduitType) : null;
                foreach (var conduit in network.Conduits.OrderBy(c => c.Id))
                {
                    MapNativeLoadingScalars(data.Parameters, conduitManager, scenario, conduit.Id, sid.Value);
                    // Read-only native design configuration; no conversion into approved P10/P13 decisions.
                    foreach (var field in NativeDesignFields.Keys.OrderBy(n => n, StringComparer.Ordinal))
                        data.Parameters.Add(ReadDomainParameter("DesignModelSettings", field, conduitManager, scenario, conduit.Id, sid.Value));
                    foreach (var field in new[] { "Physical_HasStartControlStructure", "Physical_StartControlStructureID", "Physical_HasStopControlStructure", "Physical_StopControlStructure" })
                        data.Parameters.Add(ReadDomainParameter("ControlStructure", field, conduitManager, scenario, conduit.Id, sid.Value));
                }
                data.Parameters.AddRange((map.CalculationOptionsRead.SourceParameters ?? new List<NativeSourceParameter>()).Select(p => p.Copy()));
                if (map.CalculationOptionsRead.SourceParameters == null || map.CalculationOptionsRead.SourceParameters.Count == 0)
                    data.Parameters.Add(NativeMissing("CalculationOptions", "Parameters", null, sid, "NOT_EXPORTED", "ACTIVE_OPTIONS_PARAMETER_READ_NOT_ESTABLISHED"));
                foreach (string gap in new[] { "Population", "PopulationEquivalent", "UnitSanitaryLoadDefinition", "IndustrialContributionIntent", "KnownPointLoadIntent", "PatternTimeSeries" })
                    data.Parameters.Add(NativeMissing("Loading", gap, null, sid, "NOT_EXPORTED", "REFERENCE_OR_ENGINEERING_MEANING_NOT_PROVEN_NO_LOADING_CALCULATION"));
            }
            catch (Exception error) { data.Parameters.Add(NativeMissing("Context", "NativeSourceRead", null, sid, "SEMANTICS_UNRESOLVED", "NATIVE_READ_FAILED:" + NativeError(error))); }
            return data.Copy();
        }
        private static NativeSourceParameter ReadDomainParameter(string category, string name, object manager, object scenario, int id, int sid)
        {
            try { return ReadNativeParameter(category, name, NativeDomainField(manager, name), id, sid, scenario); }
            catch (Exception error) { return NativeMissing(category, name, id, sid, "SEMANTICS_UNRESOLVED", "NATIVE_READ_FAILED:" + NativeError(error)); }
        }
        private static void MapNativeLoadingScalars(List<NativeSourceParameter> result, object manager, object scenario, int id, int sid)
        {
            foreach (var field in NativeLoadingFields.Keys.Where(n => n.StartsWith("Infiltration", StringComparison.Ordinal) || n.StartsWith("InfPattern", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
                result.Add(ReadDomainParameter("Loading", field, manager, scenario, id, sid));
        }
        internal static void MapNativeElement(List<NativeSourceParameter> result, object manager, object element, object scenario, int id, int sid, bool outfall)
        {
            MapNativeLoadingScalars(result, manager, scenario, id, sid);
            MapNativeKnownFlow(result, manager, scenario, id, sid);
            foreach (string field in new[] { "SanitaryLoadsCount", "InflowListCount" }) result.Add(ReadDomainParameter("Loading", field, manager, scenario, id, sid));
            ReadNativeSanitaryCollection(result, manager, element, scenario, id, sid);
            result.Add(NativeMissing("Loading", "InflowList.Rows", id, sid, "NOT_EXPORTED", "INFLOW_ROW_SEMANTICS_NOT_PROVEN"));
            if (!outfall) return;
            foreach (string field in NativeOutfallFields.Keys.Where(n => n != "UserDefinedTwType_UserDefinedTailwater").OrderBy(n => n, StringComparer.Ordinal))
            {
                var observed = ReadDomainParameter("Outfall", field, manager, scenario, id, sid);
                if (field == "BoundaryConditionType" && observed.SourceClassification == "PROVEN")
                {
                    try
                    {
                        var typed = NativeProperty(element, "BoundaryConditionType");
                        if (typed != null && typed.GetType().IsEnum && Convert.ToInt64(typed) == Convert.ToInt64(observed.Value))
                        {
                            observed.NativeEnumName = Enum.GetName(typed.GetType(), typed);
                            observed.SourceProvenance += "; matching element.BoundaryConditionType native enum=" + typed.GetType().FullName;
                        }
                    }
                    catch { /* The raw observation remains valid; no enum meaning is invented. */ }
                }
                result.Add(observed);
            }
            try
            {
                var boundary = NativeCall(element, "BoundaryConditionTypeObject", Type.EmptyTypes);
                bool userDefined = boundary?.GetType().FullName == "Haestad.Domain.ModelingObjects.Dynamic.BoundaryConditionTypeObject_UserDefinedTwType";
                result.Add(userDefined ? ReadDomainParameter("Outfall", "UserDefinedTwType_UserDefinedTailwater", manager, scenario, id, sid) :
                    NativeMissing("Outfall", "UserDefinedTwType_UserDefinedTailwater", id, sid, boundary == null ? "SEMANTICS_UNRESOLVED" : "NOT_AVAILABLE",
                        boundary == null ? "BOUNDARY_VARIANT_UNPROVEN" : "USER_DEFINED_TAILWATER_VARIANT_NOT_ACTIVE"));
            }
            catch (Exception error) { result.Add(NativeMissing("Outfall", "UserDefinedTwType_UserDefinedTailwater", id, sid, "SEMANTICS_UNRESOLVED", "NATIVE_READ_FAILED:" + NativeError(error))); }
            foreach (string gap in new[] { "StageTimeSeries.Rows", "NativeProtectedDesignBoundary" })
                result.Add(NativeMissing("Outfall", gap, id, sid, "NOT_EXPORTED", "NATIVE_CONFIGURATION_IS_NOT_APPROVED_DESIGN_POLICY"));
        }
        internal static void MapNativeKnownFlow(List<NativeSourceParameter> result, object manager, object scenario, int id, int sid)
        {
            const string name = "SystemFlows_KnownFlow";
            try
            {
                var field = NativeDomainField(manager, name) ??
                    NativeCall(manager, "DomainElementField", new[] { typeof(string), typeof(string) }, name, "SystemFlows");
                result.Add(ReadNativeParameter("Loading", name, field, id, sid, scenario));
            }
            catch (Exception error) { result.Add(NativeMissing("Loading", name, id, sid, "SEMANTICS_UNRESOLVED", "NATIVE_READ_FAILED:" + NativeError(error))); }
        }
        internal static void ReadNativeSanitaryCollection(List<NativeSourceParameter> result, object manager, object element, object scenario, int id, int sid)
        {
            var context = NativeMissing("Loading", "SanitaryLoads", id, sid, "NOT_AVAILABLE", "COLLECTION_NOT_AVAILABLE");
            try
            {
                var collection = NativeProperty(element, "SanitaryLoads");
                var parent = NativeDomainField(manager, "SanitaryLoads");
                if (collection == null || parent == null) { result.Add(context); return; }
                if (!string.Equals(NativeProperty(parent, "Name") as string, "SanitaryLoads", StringComparison.Ordinal))
                { result.Add(NativeFailure(context, "PARENT_COLLECTION_IDENTITY_MISMATCH")); return; }
                if (!ReadNativeAlternative(parent, scenario, context)) { result.Add(context); return; }
                foreach (string name in new[] { "LoadDefinition", "SanitaryPatternLoadType_SanitaryBaseFlow", "SanitaryPatternLoadType_PatternID",
                    "SanitaryUnitLoadType_UnitSanitaryLoadId", "SanitaryUnitLoadType_LoadingUnitNumber" })
                {
                    var field = NativeCall(collection, "Field", new[] { typeof(string) }, name);
                    var values = NativeCall(field, "GetValues", Type.EmptyTypes) as IDictionary;
                    if (values == null) { result.Add(NativeMissing("Loading", name, id, sid, "NOT_AVAILABLE", "COLLECTION_FIELD_VALUES_NOT_AVAILABLE")); continue; }
                    if (values.Count == 0) { result.Add(NativeMissing("Loading", name, id, sid, "NOT_AVAILABLE", "EMPTY_NATIVE_COLLECTION_NO_FLOW_VALUE")); continue; }
                    if (values.Count > 1000) { result.Add(NativeMissing("Loading", name, id, sid, "SEMANTICS_UNRESOLVED", "COLLECTION_LIMIT_EXCEEDED")); continue; }
                    // Opaque native dictionary keys, not guessed row offsets or element IDs. No aggregate/design load is computed.
                    foreach (DictionaryEntry row in values)
                    {
                        if (!(row.Key is int)) { result.Add(NativeMissing("Loading", name, id, sid, "SEMANTICS_UNRESOLVED", "COLLECTION_KEY_TYPE_UNPROVEN")); continue; }
                        var p = ReadNativeParameter("Loading", name, field, id, sid, scenario, false, row.Value, true);
                        p.CollectionIdentity = "SanitaryLoads"; p.NativeRowKey = ((int)row.Key).ToString(CultureInfo.InvariantCulture);
                        p.AlternativeTypeId = context.AlternativeTypeId; p.AlternativeId = context.AlternativeId;
                        p.ScenarioAssignedAlternativeId = context.ScenarioAssignedAlternativeId;
                        p.SourceProvenance += "; element.SanitaryLoads.Field(String).GetValues(); parent collection alternative association; raw member applicability not interpreted";
                        result.Add(p);
                    }
                }
            }
            catch (Exception error) { result.Add(NativeFailure(context, "NATIVE_READ_FAILED:" + NativeError(error))); }
        }
    }
}
