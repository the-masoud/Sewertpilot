using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace SewerPilot.OpenFlows.Adapter
{
    internal static partial class GravityNetworkReadMapper
    {
        private static readonly Dictionary<string, bool> NativeRainfallFields = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            { "SystemStormEventID", false }, { "SystemIDFDuration", true },
            { "StormEventsCollection_StormIdentifier", false }, { "StormEventsCollection_Label", false },
            { "StormEventsCollection_ReturnEvent", true }, { "StormEventType", false },
            { "IDFCurveCount", false }, { "IDFCurve_IDFTime", true }, { "IDFCurve_IDFIntensity", true }
        };

        internal static void MapNativeSystemRainfall(List<NativeSourceParameter> result, object dataset, object catchments, object scenario, int sid)
        {
            try
            {
                var association = result.FirstOrDefault(p => p.Category == "Catchment" && p.ParameterIdentity == "RainfallRunoff_UseLocalRainfall" && CatchmentObserved(p, sid));
                if (association?.AlternativeTypeId == null || association.AlternativeId == null)
                { result.Add(NativeMissing("Rainfall", "SystemStormEventID", null, sid, "NOT_AVAILABLE", "RAINFALL_ALTERNATIVE_UNPROVEN")); return; }
                int type = association.AlternativeTypeId.Value, alt = association.AlternativeId.Value;
                if (!(NativeCall(scenario, "AlternativeID", new[] { typeof(int) }, type) is int assigned) || assigned != alt)
                { result.Add(NativeMissing("Rainfall", "SystemStormEventID", alt, sid, "SEMANTICS_UNRESOLVED", "SCENARIO_ALTERNATIVE_MISMATCH")); return; }
                var manager = NativeCall(dataset, "AlternativeManager", new[] { typeof(int) }, type);
                foreach (string name in new[] { "SystemStormEventID", "SystemIDFDuration" })
                {
                    var field = NativeCall(manager, "SystemRecordField", new[] { typeof(string) }, name);
                    var p = ReadNativeParameter("Rainfall", name, field, alt, sid, scenario, false);
                    if (p.SourceClassification == "PROVEN")
                    {
                        if (!(NativeProperty(NativeProperty(field, "AlternativeType"), "Id") is int fieldType) || fieldType != type)
                            NativeFailure(p, "SYSTEM_FIELD_ALTERNATIVE_TYPE_MISMATCH");
                        else
                        {
                            p.AlternativeTypeId = type; p.AlternativeId = alt; p.ScenarioAssignedAlternativeId = assigned;
                            p.CollectionIdentity = "RainfallRunoffSystem";
                            p.SourceProvenance += "; ISystemRecordField.GetValue(scenario-assigned alternative ID); field.AlternativeType.Id verified";
                        }
                    }
                    result.Add(p);
                }
                try { MapNativeStormDefinitions(result, dataset, scenario, sid); }
                catch (Exception error) { result.Add(NativeMissing("Rainfall", "StormDefinitionRead", null, sid, "SEMANTICS_UNRESOLVED", "NATIVE_READ_FAILED:" + NativeError(error))); }
            }
            catch (Exception error) { result.Add(NativeMissing("Rainfall", result.Any(p => p.Category == "Rainfall" && p.ParameterIdentity == "SystemStormEventID") ? "SystemReadDiagnostic" : "SystemStormEventID", null, sid, "SEMANTICS_UNRESOLVED", "NATIVE_READ_FAILED:" + NativeError(error))); }
        }

        internal static void MapNativeStormDefinitions(List<NativeSourceParameter> result, object dataset, object scenario, int sid)
        {
            var manager = NativeProperty(dataset, "StormEventElementManager");
            var ids = NativeCall(manager, "ElementIDs", Type.EmptyTypes);
            if (!(NativeProperty(ids, "Count") is int count) || count < 0 || count > 1000)
            { result.Add(NativeMissing("Rainfall", "StormDefinitionRead", null, sid, "SEMANTICS_UNRESOLVED", "STORM_GROUP_ENUMERATION_UNPROVEN")); return; }
            var item = WaterObjectsLifecycleHost.Indexer(ids.GetType());
            for (int i = 0; i < count; i++)
            {
                if (!(item.GetValue(ids, new object[] { i }) is int id) || id <= 0) continue;
                var element = NativeCall(manager, "Element", new[] { typeof(int) }, id);
                var events = NativeProperty(element, "StormEventsCollection");
                // Generic support elements do not expose the typed collection property.
                if (events == null)
                {
                    var collectionField = NativeCall(manager, "SupportElementField", new[] { typeof(string) }, "StormEventsCollection");
                    if (string.Equals(NativeProperty(collectionField, "Name") as string, "StormEventsCollection", StringComparison.Ordinal))
                        events = NativeCall(collectionField, "GetValue", new[] { typeof(int) }, id);
                }
                if (events == null)
                { result.Add(NativeMissing("Rainfall", "StormDefinitionRead", id, sid, "SEMANTICS_UNRESOLVED", "STORM_GROUP_COLLECTION_UNAVAILABLE")); continue; }
                var entryKeys = new HashSet<int>();
                foreach (string name in new[] { "StormEventsCollection_StormIdentifier", "StormEventsCollection_Label", "StormEventsCollection_ReturnEvent", "StormEventType", "IDFCurveCount" })
                {
                    var field = NativeCall(events, "Field", new[] { typeof(string) }, name);
                    var rows = NativeCall(field, "GetValues", Type.EmptyTypes) as IDictionary;
                    if (rows == null || rows.Count > 1000) continue;
                    foreach (DictionaryEntry row in rows)
                    {
                        if (!(row.Key is int key)) continue;
                        entryKeys.Add(key);
                        var p = ReadNativeParameter("Rainfall", name, field, id, sid, scenario, false, row.Value, true);
                        p.CollectionIdentity = "StormEventsCollection"; p.NativeRowKey = key.ToString(CultureInfo.InvariantCulture);
                        p.SourceProvenance += "; native storm-group StormEventsCollection.Field(String).GetValues(); opaque event row key";
                        result.Add(p);
                    }
                }
                var curveField = NativeCall(events, "Field", new[] { typeof(string) }, "IDFCurve");
                if (entryKeys.Count == 0)
                    result.Add(NativeMissing("Rainfall", "StormDefinitionRead", id, sid, "SEMANTICS_UNRESOLVED", "STORM_ENTRY_ENUMERATION_UNPROVEN"));
                foreach (int entry in entryKeys.OrderBy(k => k))
                {
                    var curve = NativeCall(curveField, "GetValue", new[] { typeof(int) }, entry);
                    if (curve == null) continue;
                    foreach (string name in new[] { "IDFCurve_IDFTime", "IDFCurve_IDFIntensity" })
                    {
                        var field = NativeCall(curve, "Field", new[] { typeof(string) }, name);
                        var rows = NativeCall(field, "GetValues", Type.EmptyTypes) as IDictionary;
                        if (rows == null || rows.Count > 1000) continue;
                        foreach (DictionaryEntry row in rows)
                        {
                            if (!(row.Key is int key)) continue;
                            var p = ReadNativeParameter("Rainfall", name, field, id, sid, scenario, false, row.Value, true);
                            p.CollectionIdentity = "StormEventsCollection[" + entry.ToString(CultureInfo.InvariantCulture) + "].IDFCurve";
                            p.NativeRowKey = key.ToString(CultureInfo.InvariantCulture);
                            p.SourceProvenance += "; native nested storm-entry IDFCurve.Field(String).GetValues(); opaque event row=" + entry.ToString(CultureInfo.InvariantCulture);
                            result.Add(p);
                        }
                    }
                }
            }
        }
    }
}
