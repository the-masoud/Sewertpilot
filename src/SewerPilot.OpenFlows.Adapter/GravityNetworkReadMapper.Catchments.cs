using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    internal static partial class GravityNetworkReadMapper
    {
        // Runoff drivers are source observations, outside the conduit/node design fingerprint.
        // A readable quantity without vendor units remains unresolved, including numeric zero.
        private static readonly Dictionary<string, bool> NativeCatchmentFields = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            { "ElementCount", false }, { "Label", false }, { "HMIActiveTopologyIsActive", false },
            { "AreaDefinitionType", false }, { "RunoffMethodVariant", false },
            { "EffectiveArea", true }, { "EffectiveStormEventID", false },
            { "Hydrologic_OutflowNode", false }, { "Hydrologic_ReceivingNode", false },
            { "Hydrologic_ApplyGroundWater", false }, { "Hydrologic_UseScaledArea", false },
            { "Hydrologic_Area", true }, { "HMIGeometryScaledArea", true },
            { "RunoffMethodType", false }, { "Hydrologic_TimeOfConcentration", true },
            { "RationalMethodType_RationalCCoefficient", false },
            { "RainfallRunoff_UseLocalRainfall", false }, { "RainfallRunoff_StormEventID", false }
        };

        private static void MapNativeCatchments(List<NativeSourceParameter> result, object dataset, object scenario, int sid)
        {
            try
            {
                int type = ResolveElementTypeId(ResolveDomainElementType(), "CatchmentElementManager");
                var manager = type < 0 ? null : NativeCall(dataset, "DomainElementManager", new[] { typeof(int) }, type);
                MapNativeCatchmentElements(result, manager, scenario, sid);
                MapNativeSystemRainfall(result, dataset, manager, scenario, sid);
                ResolveNativeCatchmentLoading(result, sid);
            }
            catch (Exception error)
            {
                result.Add(NativeMissing("Catchment", "ElementCount", null, sid, "SEMANTICS_UNRESOLVED", "NATIVE_READ_FAILED:" + NativeError(error)));
            }
        }

        internal static void MapNativeCatchmentElements(List<NativeSourceParameter> result, object manager, object scenario, int sid)
        {
            var gap = NativeMissing("Catchment", "ElementCount", null, sid, "NOT_AVAILABLE", "CATCHMENT_MANAGER_NOT_AVAILABLE");
            if (manager == null) { result.Add(gap); return; }
            try
            {
                if (sid <= 0) { result.Add(NativeFailure(gap, "SOURCE_IDENTITY_UNPROVEN")); return; }
                var ids = NativeCall(manager, "ElementIDs", Type.EmptyTypes);
                var rawCount = NativeProperty(ids, "Count");
                if (!(rawCount is int count) || count < 0 || count > 100000)
                { result.Add(NativeFailure(gap, "CATCHMENT_ENUMERATION_CONTRACT_UNPROVEN")); return; }
                var indexer = ids.GetType().GetProperty("Item", new[] { typeof(int) });
                if (count > 0 && (indexer == null || !indexer.CanRead))
                { result.Add(NativeFailure(gap, "CATCHMENT_ENUMERATION_CONTRACT_UNPROVEN")); return; }
                var sourceIds = new SortedSet<int>();
                for (int i = 0; i < count; i++)
                {
                    var raw = indexer.GetValue(ids, new object[] { i });
                    if (!(raw is int id) || id <= 0 || !sourceIds.Add(id))
                    { result.Add(NativeFailure(gap, "CATCHMENT_ENUMERATION_IDENTITY_UNPROVEN")); return; }
                }
                result.Add(new NativeSourceParameter { Category = "Catchment", ParameterIdentity = "ElementCount", ScenarioId = sid,
                    Value = count, NativeValueType = typeof(int).FullName, UnitAvailability = "NOT_APPLICABLE",
                    Availability = "NATIVE_CONFIGURATION_OBSERVED", SourceClassification = "PROVEN",
                    SourceProvenance = manager.GetType().FullName + ".ElementIDs().Count; validated distinct positive IDs; enumeration is not proof of active runoff or design loading" });
                foreach (int id in sourceIds)
                {
                    foreach (var target in NativeCatchmentFields)
                    {
                        if (target.Key == "ElementCount" || target.Key == "RunoffMethodVariant" ||
                            target.Key == "EffectiveArea" || target.Key == "EffectiveStormEventID") continue;
                        try
                        {
                            bool alternativeRequired = target.Key != "Label" && target.Key != "HMIGeometryScaledArea";
                            object field = CatchmentField(manager, target.Key);
                            var observation = ReadNativeParameter("Catchment", target.Key, field, id, sid, scenario, alternativeRequired);
                            result.Add(observation);
                            if (target.Key == "RunoffMethodType" && observation.SourceClassification == "PROVEN")
                            {
                                var element = NativeCall(manager, "Element", new[] { typeof(int) }, id);
                                var method = NativeProperty(element, "RunoffMethodType");
                                if (method != null && method.GetType().IsEnum && Convert.ToInt64(method) == Convert.ToInt64(observation.Value))
                                {
                                    observation.NativeEnumName = Enum.GetName(method.GetType(), method);
                                    observation.SourceProvenance += "; matching element.RunoffMethodType native enum=" + method.GetType().FullName;
                                }
                                var variant = NativeCall(element, "RunoffMethodTypeObject", Type.EmptyTypes);
                                if (variant != null)
                                {
                                    var v = observation.Copy(); v.ParameterIdentity = "RunoffMethodVariant";
                                    v.Value = variant.GetType().FullName; v.NativeValueType = typeof(string).FullName; v.NativeEnumName = null;
                                    v.SourceProvenance += "; element.RunoffMethodTypeObject().GetType().FullName"; result.Add(v);
                                }
                            }
                        }
                        catch (Exception error)
                        {
                            result.Add(NativeMissing("Catchment", target.Key, id, sid, "SEMANTICS_UNRESOLVED", "NATIVE_READ_FAILED:" + NativeError(error)));
                        }
                    }
                }
            }
            catch (Exception error) { result.Add(NativeFailure(gap, "NATIVE_READ_FAILED:" + NativeError(error))); }
        }

        internal static object CatchmentField(object manager, string name)
        {
            var field = NativeDomainField(manager, name);
            if (field != null) return field;
            // These fields are declared by the native Hydrologic scope, not the default scope.
            if (name != "Hydrologic_UseScaledArea" && name != "RationalMethodType_RationalCCoefficient" && name != "AreaDefinitionType") return null;
            return NativeCall(manager, "DomainElementField", new[] { typeof(string), typeof(string) }, name, "Hydrologic");
        }

        internal static void ResolveNativeCatchmentLoading(List<NativeSourceParameter> values, int sid)
        {
            var source = values.ToArray();
            foreach (int id in source.Where(p => p.Category == "Catchment" && p.SourceId.HasValue).Select(p => p.SourceId.Value).Distinct())
            {
                Func<string, NativeSourceParameter> get = key => source.SingleOrDefault(p => p.Category == "Catchment" && p.SourceId == id && p.ParameterIdentity == key);
                var selector = get("Hydrologic_UseScaledArea");
                var area = NativeMissing("Catchment", "EffectiveArea", id, sid, "NOT_AVAILABLE", "AREA_SELECTOR_UNPROVEN");
                if (CatchmentObserved(selector, sid) && selector.Value is bool scaled)
                {
                    var selected = get(scaled ? "HMIGeometryScaledArea" : "Hydrologic_Area");
                    area.Reason = "SELECTED_AREA_OR_AREA_UNITS_UNPROVEN";
                    if (CatchmentObserved(selected, sid) && selected.Unit != null &&
                        string.Equals(selected.Unit.Dimension, "area", StringComparison.OrdinalIgnoreCase) && selected.Value != null &&
                        (scaled || (selected.AlternativeTypeId == selector.AlternativeTypeId && selected.AlternativeId == selector.AlternativeId)))
                    {
                        double number = Convert.ToDouble(selected.Value, System.Globalization.CultureInfo.InvariantCulture);
                        if (!double.IsNaN(number) && !double.IsInfinity(number) && number >= 0)
                        {
                            area = selected.Copy(); area.ParameterIdentity = "EffectiveArea";
                            area.AlternativeTypeId = selector.AlternativeTypeId; area.AlternativeId = selector.AlternativeId;
                            area.ScenarioAssignedAlternativeId = selector.ScenarioAssignedAlternativeId;
                            area.Availability = "DERIVED_FROM_NATIVE_CONFIGURATION";
                            area.SourceProvenance += "; selected by native Hydrologic_UseScaledArea=" + scaled + "; " + selector.SourceProvenance;
                        }
                    }
                }
                values.Add(area);
                var local = get("RainfallRunoff_UseLocalRainfall");
                var effective = NativeMissing("Catchment", "EffectiveStormEventID", id, sid, "NOT_AVAILABLE", "RAINFALL_SELECTOR_UNPROVEN");
                if (CatchmentObserved(local, sid) && local.Value is bool useLocal)
                {
                    var selected = useLocal ? get("RainfallRunoff_StormEventID") : source.SingleOrDefault(p => p.Category == "Rainfall" && p.ParameterIdentity == "SystemStormEventID");
                    effective.Reason = useLocal ? "LOCAL_EVENT_REFERENCE_UNPROVEN" : "GLOBAL_EVENT_REFERENCE_UNPROVEN";
                    if (CatchmentObserved(selected, sid) && selected.Value is string reference && !string.IsNullOrWhiteSpace(reference) &&
                        selected.AlternativeTypeId == local.AlternativeTypeId && selected.AlternativeId == local.AlternativeId)
                    {
                        effective = local.Copy(); effective.ParameterIdentity = "EffectiveStormEventID"; effective.Value = reference;
                        effective.NativeValueType = typeof(string).FullName; effective.Availability = "DERIVED_FROM_NATIVE_CONFIGURATION";
                        effective.SourceProvenance += "; selected " + (useLocal ? "local" : "scenario system") + " event; " + selected.SourceProvenance;
                    }
                }
                values.Add(effective);
            }
        }

        private static bool CatchmentObserved(NativeSourceParameter p, int sid) => p != null && p.ScenarioId == sid &&
            p.SourceClassification == "PROVEN" && p.Availability == "NATIVE_CONFIGURATION_OBSERVED" &&
            (!p.AlternativeId.HasValue || p.AlternativeId == p.ScenarioAssignedAlternativeId);
    }
}
