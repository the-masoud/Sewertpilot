using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    internal static partial class GravityNetworkReadMapper
    {
        public static NetworkReadResult Map(object dataset, object project, object ui, object parent, P6ACapabilityMap capabilityMap = null)
        {
            var result = new NetworkReadResult { Phase = "P12-A" };
            if (dataset == null)
            {
                result.Status = "DATASET_NULL";
                return result;
            }

            Type datasetType = dataset.GetType();
            var memberEvidenceList = new List<MemberEvidence>();

            Type elementTypeEnum = ResolveDomainElementType();
            if (elementTypeEnum == null)
            {
                result.Status = "P12_A_R1_BLOCKED_DOMAIN_ELEMENT_TYPE_RESOLUTION";
                result.TopologyCheck.Issues.Add("P12_A_R1_BLOCKED_DOMAIN_ELEMENT_TYPE_RESOLUTION: Haestad.Domain.DomainElementType could not be resolved from loaded assemblies.");
                return result;
            }

            // 1. Scenario Context
            var scenarioContext = new NetworkScenarioContext();
            try
            {
                PropertyInfo smProp = datasetType.GetProperty("Scenario" + "Manager");
                if (smProp != null)
                {
                    object sm = smProp.GetValue(dataset, null);
                    if (sm != null)
                    {
                        Type smType = sm.GetType();
                        PropertyInfo activeIdProp = smType.GetProperty("ActiveScenarioID");
                        if (activeIdProp != null)
                        {
                            int activeId = (int)activeIdProp.GetValue(sm, null);
                            scenarioContext.ActiveScenarioId = activeId;

                            MethodInfo elemMethod = smType.GetMethod("Element", new[] { typeof(int) });
                            if (elemMethod != null && activeId > 0)
                            {
                                object scElem = elemMethod.Invoke(sm, new object[] { activeId });
                                if (scElem != null)
                                {
                                    PropertyInfo labelProp = scElem.GetType().GetProperty("Label");
                                    scenarioContext.ActiveScenarioLabel = labelProp?.GetValue(scElem, null) as string ?? "";

                                    MethodInfo altIdMethod = scElem.GetType().GetMethod("AlternativeID", new[] { typeof(int) });
                                    if (altIdMethod != null)
                                    {
                                        scenarioContext.TopologyAlternativeId = ReadAltId(altIdMethod, scElem, 2);
                                        scenarioContext.PhysicalAlternativeId = ReadAltId(altIdMethod, scElem, 4);
                                        scenarioContext.ActiveTopologyAlternativeId = ReadAltId(altIdMethod, scElem, 3);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                scenarioContext.ActiveScenarioLabel = "Error: " + ex.Message;
            }
            result.ScenarioContext = scenarioContext;

            MethodInfo domainElementManagerMethod = datasetType.GetMethod("DomainElementManager", new[] { typeof(int) });
            RecordMember(memberEvidenceList, domainElementManagerMethod, "Haestad.Domain.IDomainDataSet", "DomainElementManager");

            int conduitTypeId = ResolveElementTypeId(elementTypeEnum, "ConduitElementManager");
            int outfallTypeId = ResolveElementTypeId(elementTypeEnum, "OutfallElementManager");

            // 2. Conduit Enumeration & Details
            if (domainElementManagerMethod != null)
            {
                try
                {
                    object conduitManager = domainElementManagerMethod.Invoke(dataset, new object[] { conduitTypeId });
                    if (conduitManager != null)
                    {
                        Type mgrType = conduitManager.GetType();
                        MethodInfo idsMethod = mgrType.GetMethod("ElementIDs", Type.EmptyTypes);
                        MethodInfo elementMethod = mgrType.GetMethod("Element", new[] { typeof(int) });

                        RecordMember(memberEvidenceList, idsMethod, mgrType.FullName, "ElementIDs");
                        RecordMember(memberEvidenceList, elementMethod, mgrType.FullName, "Element");

                        var upInvertUnit = InspectFieldUnit(conduitManager, "Physical_UpstreamInvert", result.FieldUnitMap, memberEvidenceList);
                        var downInvertUnit = InspectFieldUnit(conduitManager, "Physical_DownstreamInvert", result.FieldUnitMap, memberEvidenceList);
                        var scaledLengthUnit = InspectFieldUnit(conduitManager, "HMIGeometryScaledLength", result.FieldUnitMap, memberEvidenceList);
                        var userLengthUnit = InspectFieldUnit(conduitManager, "Physical_UserDefinedLength", result.FieldUnitMap, memberEvidenceList);
                        var diamUnit = InspectFieldUnit(conduitManager, "ConduitDiameter", result.FieldUnitMap, memberEvidenceList);
                        var riseUnit = InspectFieldUnit(conduitManager, "ConduitRise", result.FieldUnitMap, memberEvidenceList);
                        var spanUnit = InspectFieldUnit(conduitManager, "ConduitSpan", result.FieldUnitMap, memberEvidenceList);

                        InspectEndpointSemantics(conduitManager, upInvertUnit, downInvertUnit, result.FieldUnitMap, result.EndpointSemantics, memberEvidenceList);

                        if (idsMethod != null && elementMethod != null)
                        {
                            object idCollection = idsMethod.Invoke(conduitManager, null);
                            int conduitCount = GetCount(idCollection);
                            PropertyInfo indexer = WaterObjectsLifecycleHost.Indexer(idCollection.GetType());

                            for (int i = 0; i < conduitCount; i++)
                            {
                                int cid = (int)indexer.GetValue(idCollection, new object[] { i });
                                object ce = elementMethod.Invoke(conduitManager, new object[] { cid });
                                if (ce == null) continue;

                                Type ceType = ce.GetType();
                                var cDto = new ConduitReadDto { Id = cid };

                                cDto.Label = ReadProperty<string>(ceType, ce, "Label", memberEvidenceList);
                                cDto.StartNodeId = ReadProperty<int>(ceType, ce, "HMITopologyStartNodeID", memberEvidenceList);
                                cDto.StopNodeId = ReadProperty<int>(ceType, ce, "HMITopologyStopNodeID", memberEvidenceList);

                                cDto.UpstreamInvert = ReadNullableDouble(ceType, ce, "Physical_UpstreamInvert", memberEvidenceList);
                                cDto.DownstreamInvert = ReadNullableDouble(ceType, ce, "Physical_DownstreamInvert", memberEvidenceList);
                                cDto.UpstreamInvertUnit = upInvertUnit;
                                cDto.DownstreamInvertUnit = downInvertUnit;

                                cDto.StartInvert = cDto.UpstreamInvert;
                                cDto.StopInvert = cDto.DownstreamInvert;
                                cDto.StartInvertUnit = upInvertUnit;
                                cDto.StopInvertUnit = downInvertUnit;
                                cDto.StartInvertSourceField = "Physical_UpstreamInvert";
                                cDto.StopInvertSourceField = "Physical_DownstreamInvert";
                                cDto.StartNodeSourceMember = "HMITopologyStartNodeID";
                                cDto.StopNodeSourceMember = "HMITopologyStopNodeID";
                                cDto.SetInvertToStartSourceMember = "Physical_SetInvertToUpstream";
                                cDto.SetInvertToStopSourceMember = "Physical_SetInvertToDownstream";

                                cDto.SetInvertToStart = ReadProperty<bool>(ceType, ce, "Physical_SetInvertToUpstream", memberEvidenceList);
                                cDto.SetInvertToStop = ReadProperty<bool>(ceType, ce, "Physical_SetInvertToDownstream", memberEvidenceList);

                                bool isUserLen = ReadProperty<bool>(ceType, ce, "Physical_IsUserDefinedLength", memberEvidenceList);
                                if (isUserLen)
                                {
                                    cDto.Length = ReadNullableDouble(ceType, ce, "Physical_UserDefinedLength", memberEvidenceList);
                                    cDto.LengthSourceField = "Physical_UserDefinedLength";
                                    cDto.LengthUnit = userLengthUnit;
                                }
                                else
                                {
                                    cDto.Length = ReadNullableDouble(ceType, ce, "HMIGeometryScaledLength", memberEvidenceList);
                                    cDto.LengthSourceField = "HMIGeometryScaledLength";
                                    cDto.LengthUnit = scaledLengthUnit;
                                }

                                cDto.Diameter = ReadNullableDouble(ceType, ce, "ConduitDiameter", memberEvidenceList);
                                if (cDto.Diameter.HasValue) cDto.DiameterUnit = diamUnit;

                                cDto.Rise = ReadNullableDouble(ceType, ce, "ConduitRise", memberEvidenceList);
                                if (cDto.Rise.HasValue) cDto.RiseUnit = riseUnit;

                                cDto.Span = ReadNullableDouble(ceType, ce, "ConduitSpan", memberEvidenceList);
                                if (cDto.Span.HasValue) cDto.SpanUnit = spanUnit;

                                object shapeObj = ReadPropertyObj(ceType, ce, "ConduitShape", memberEvidenceList);
                                cDto.Shape = shapeObj?.ToString() ?? "";

                                object typeObj = ReadPropertyObj(ceType, ce, "ConduitType", memberEvidenceList);
                                cDto.ConduitType = typeObj?.ToString() ?? "";

                                cDto.Material = ReadProperty<string>(ceType, ce, "Physical_Material", memberEvidenceList) ?? "";
                                cDto.ManningsN = ReadNullableDouble(ceType, ce, "Physical_ManningsN", memberEvidenceList);

                                // Geometry points
                                object geomObj = ReadPropertyObj(ceType, ce, "Geometry", memberEvidenceList);
                                if (geomObj is IEnumerable geomEnum)
                                {
                                    foreach (object pt in geomEnum)
                                    {
                                        if (pt != null)
                                        {
                                            Type ptType = pt.GetType();
                                            double px = ReadProperty<double>(ptType, pt, "X", null);
                                            double py = ReadProperty<double>(ptType, pt, "Y", null);
                                            cDto.GeometryPoints.Add(new Point2DDto(px, py));
                                        }
                                    }
                                }

                                // Classifications
                                cDto.PropertyClassifications["StartNodeId"] = NetworkReadPropertyStatus.PROVEN.ToString();
                                cDto.PropertyClassifications["StopNodeId"] = NetworkReadPropertyStatus.PROVEN.ToString();
                                cDto.PropertyClassifications["StartInvert"] = cDto.StartInvert.HasValue ? NetworkReadPropertyStatus.PROVEN.ToString() : NetworkReadPropertyStatus.NOT_AVAILABLE.ToString();
                                cDto.PropertyClassifications["StopInvert"] = cDto.StopInvert.HasValue ? NetworkReadPropertyStatus.PROVEN.ToString() : NetworkReadPropertyStatus.NOT_AVAILABLE.ToString();
                                cDto.PropertyClassifications["SetInvertToStart"] = NetworkReadPropertyStatus.PROVEN.ToString();
                                cDto.PropertyClassifications["SetInvertToStop"] = NetworkReadPropertyStatus.PROVEN.ToString();
                                cDto.PropertyClassifications["Length"] = cDto.Length.HasValue ? NetworkReadPropertyStatus.PROVEN.ToString() : NetworkReadPropertyStatus.NOT_AVAILABLE.ToString();
                                cDto.PropertyClassifications["Diameter"] = cDto.Diameter.HasValue ? NetworkReadPropertyStatus.PROVEN.ToString() : NetworkReadPropertyStatus.NOT_AVAILABLE.ToString();
                                cDto.PropertyClassifications["UpstreamInvert"] = cDto.UpstreamInvert.HasValue ? NetworkReadPropertyStatus.PROVEN.ToString() : NetworkReadPropertyStatus.NOT_AVAILABLE.ToString();
                                cDto.PropertyClassifications["DownstreamInvert"] = cDto.DownstreamInvert.HasValue ? NetworkReadPropertyStatus.PROVEN.ToString() : NetworkReadPropertyStatus.NOT_AVAILABLE.ToString();
                                cDto.PropertyClassifications["Material"] = !string.IsNullOrWhiteSpace(cDto.Material) ? NetworkReadPropertyStatus.PROVEN.ToString() : NetworkReadPropertyStatus.NOT_AVAILABLE.ToString();
                                cDto.PropertyClassifications["ManningsN"] = cDto.ManningsN.HasValue ? NetworkReadPropertyStatus.PROVEN.ToString() : NetworkReadPropertyStatus.NOT_AVAILABLE.ToString();
                                cDto.PropertyClassifications["Geometry"] = cDto.GeometryPoints.Count > 0 ? NetworkReadPropertyStatus.PROVEN.ToString() : NetworkReadPropertyStatus.NOT_AVAILABLE.ToString();
                                if (cDto.Rise.HasValue && cDto.Rise.Value > 0)
                                    cDto.PropertyClassifications["Rise"] = NetworkReadPropertyStatus.PROVEN.ToString();
                                else
                                    cDto.PropertyClassifications["Rise"] = NetworkReadPropertyStatus.NOT_PROVEN.ToString();

                                string dClass = cDto.PropertyClassifications.TryGetValue("Diameter", out var dc) ? dc : null;
                                string rClass = cDto.PropertyClassifications.TryGetValue("Rise", out var rc) ? rc : null;
                                var res = EffectiveSectionHeightResolver.Resolve(cDto.Id, cDto.Shape, cDto.Diameter, cDto.DiameterUnit, dClass, cDto.Rise, cDto.RiseUnit, rClass);
                                if (res.IsProven)
                                {
                                    cDto.EffectiveSectionHeight = res.Value;
                                    cDto.EffectiveSectionHeightUnit = res.Unit;
                                    cDto.SectionHeightDerivation = res.Derivation;
                                    cDto.PropertyClassifications["EffectiveSectionHeight"] = NetworkReadPropertyStatus.PROVEN.ToString();
                                    cDto.PropertyClassifications["EffectiveHeight"] = NetworkReadPropertyStatus.PROVEN.ToString();
                                }
                                else if (!string.IsNullOrEmpty(res.InconsistencyReason))
                                {
                                    cDto.SectionHeightDerivation = res.InconsistencyReason;
                                    cDto.PropertyClassifications["EffectiveSectionHeight"] = res.Status.ToString();
                                }

                                result.Conduits.Add(cDto);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.TopologyCheck.Issues.Add("CONDUIT_READ_ERROR: " + ex.Message);
                }

                // 3. Structure Enumeration & Details across Categories
                var structureCategories = new List<KeyValuePair<int, string>>();
                void AddCategory(string managerEnumName, string catName)
                {
                    int tid = ResolveElementTypeId(elementTypeEnum, managerEnumName);
                    if (tid >= 0) structureCategories.Add(new KeyValuePair<int, string>(tid, catName));
                }
                AddCategory("ManholeElementManager", "Manhole");
                AddCategory("CatchBasinElementManager", "CatchBasin");
                if (outfallTypeId >= 0) structureCategories.Add(new KeyValuePair<int, string>(outfallTypeId, "Outfall"));
                AddCategory("CrossSectionNodeElementManager", "CrossSectionNode");
                AddCategory("WetWellElementManager", "WetWell");
                AddCategory("JunctionChamberElementManager", "JunctionChamber");

                foreach (var kvp in structureCategories)
                {
                    int typeId = kvp.Key;
                    string catName = kvp.Value;

                    try
                    {
                        object manager = domainElementManagerMethod.Invoke(dataset, new object[] { typeId });
                        if (manager == null) continue;

                        Type mgrType = manager.GetType();
                        MethodInfo idsMethod = mgrType.GetMethod("ElementIDs", Type.EmptyTypes);
                        MethodInfo elementMethod = mgrType.GetMethod("Element", new[] { typeof(int) });

                        if (idsMethod == null || elementMethod == null) continue;

                        var gUnit = InspectFieldUnit(manager, "Physical_GroundElevation", result.FieldUnitMap, memberEvidenceList);
                        var rUnit = InspectFieldUnit(manager, "Physical_RimElevation", result.FieldUnitMap, memberEvidenceList);
                        SourceUnitIdentity invUnit;
                        if (typeId == outfallTypeId)
                        {
                            invUnit = InspectFieldUnit(manager, "Physical_OutfallInvertElevation", result.FieldUnitMap, memberEvidenceList);
                        }
                        else
                        {
                            invUnit = InspectFieldUnit(manager, "Physical_InvertElevation", result.FieldUnitMap, memberEvidenceList);
                        }
                        var coordUnit = InspectFieldUnit(manager, "Geometry", result.FieldUnitMap, memberEvidenceList);

                        object idCollection = idsMethod.Invoke(manager, null);
                        int count = GetCount(idCollection);
                        if (count <= 0) continue;

                        PropertyInfo indexer = WaterObjectsLifecycleHost.Indexer(idCollection.GetType());

                        for (int i = 0; i < count; i++)
                        {
                            int nid = (int)indexer.GetValue(idCollection, new object[] { i });
                            object ne = elementMethod.Invoke(manager, new object[] { nid });
                            if (ne == null) continue;

                            Type neType = ne.GetType();
                            var sDto = new StructureReadDto
                            {
                                Id = nid,
                                RuntimeCategory = catName,
                                ElementTypeId = typeId
                            };

                            sDto.Label = ReadProperty<string>(neType, ne, "Label", memberEvidenceList);
                            sDto.X = ReadNullableDouble(neType, ne, "X", memberEvidenceList);
                            sDto.Y = ReadNullableDouble(neType, ne, "Y", memberEvidenceList);

                            sDto.GroundElevation = ReadNullableDouble(neType, ne, "Physical_GroundElevation", memberEvidenceList);
                            sDto.RimElevation = ReadNullableDouble(neType, ne, "Physical_RimElevation", memberEvidenceList);

                            if (typeId == outfallTypeId)
                            {
                                if (!sDto.RimElevation.HasValue && sDto.GroundElevation.HasValue)
                                {
                                    sDto.RimElevation = sDto.GroundElevation;
                                    sDto.RimElevationUnit = sDto.GroundElevationUnit ?? gUnit;
                                }
                                sDto.InvertElevation = ReadNullableDouble(neType, ne, "Physical_OutfallInvertElevation", memberEvidenceList);
                            }
                            else
                            {
                                sDto.InvertElevation = ReadNullableDouble(neType, ne, "Physical_InvertElevation", memberEvidenceList);
                            }

                            sDto.GroundElevationUnit = gUnit;
                            sDto.RimElevationUnit = rUnit;
                            sDto.InvertElevationUnit = invUnit;
                            sDto.CoordinateUnit = coordUnit;

                            sDto.PropertyClassifications["Coordinates"] = (sDto.X.HasValue && sDto.Y.HasValue) ? NetworkReadPropertyStatus.PROVEN.ToString() : NetworkReadPropertyStatus.NOT_AVAILABLE.ToString();
                            sDto.PropertyClassifications["GroundElevation"] = sDto.GroundElevation.HasValue ? NetworkReadPropertyStatus.PROVEN.ToString() : NetworkReadPropertyStatus.NOT_AVAILABLE.ToString();
                            sDto.PropertyClassifications["RimElevation"] = sDto.RimElevation.HasValue ? NetworkReadPropertyStatus.PROVEN.ToString() : NetworkReadPropertyStatus.NOT_AVAILABLE.ToString();
                            sDto.PropertyClassifications["InvertElevation"] = sDto.InvertElevation.HasValue ? NetworkReadPropertyStatus.PROVEN.ToString() : NetworkReadPropertyStatus.NOT_AVAILABLE.ToString();

                            result.Structures.Add(sDto);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.TopologyCheck.Issues.Add("STRUCTURE_READ_ERROR (" + catName + "): " + ex.Message);
                    }
                }
            }

            // Auto-resolve capability map if not supplied so SourceData is attached through application read path.
            if (capabilityMap == null && dataset != null)
            {
                try { capabilityMap = ScenarioAlternativeCapabilityMapper.Map(dataset, project, ui, parent); }
                catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.cs: " + suppressedEx.Message); }
            }
            if (capabilityMap != null) result.SourceData = MapNativeSourceData(dataset, result, capabilityMap);

            // 4. Topology Checks
            result.TopologyCheck = NetworkTopologyValidator.Validate(result);
            result.Status = result.TopologyCheck.IsValid ? "CAPABILITY_MAPPING_COMPLETE" : "TOPOLOGY_VALIDATION_FAILED";

            // 5. Unique API Members
            var seen = new HashSet<string>();
            foreach (var m in memberEvidenceList)
            {
                string key = m.DeclaringType + "." + m.MemberName;
                if (seen.Add(key))
                {
                    result.ApiMembers.Add(m);
                }
            }

            return result;
        }

        internal static Type ResolveDomainElementType(IEnumerable<Assembly> loadedAssemblies = null)
        {
            var pool = loadedAssemblies ?? AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic);
            var matches = pool.Where(a =>
            {
                try
                {
                    return string.Equals(a.GetName().Name, "Haestad.Domain", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }).ToList();

            if (matches.Count != 1) return null;
            return matches[0].GetType("Haestad.Domain.DomainElementType", false);
        }

        internal static int ResolveElementTypeId(Type enumType, string memberName)
        {
            if (enumType == null || !enumType.IsEnum) return -1;
            try
            {
                return Convert.ToInt32(Enum.Parse(enumType, memberName, false));
            }
            catch
            {
                return -1;
            }
        }

        private static int ReadAltId(MethodInfo altIdMethod, object target, int altType)
        {
            try
            {
                object val = altIdMethod.Invoke(target, new object[] { altType });
                if (val is int id) return id;
                throw new HydraulicDataContractException(
                    "Alternative identifier reader '" + altIdMethod.Name + "' returned a non-integer value for alternative type " + altType + ".");
            }
            catch (Exception ex) when (!(ex is HydraulicDataContractException))
            {
                // Fail closed: a reflection failure must never be reported as alternative id 0,
                // which is indistinguishable from a genuine "no alternative" result.
                throw new HydraulicDataContractException(
                    "Failed to read alternative identifier via '" + altIdMethod.Name + "' for alternative type " + altType + ".", ex);
            }
        }

        private static int GetCount(object collection)
        {
            if (collection == null) return 0;
            PropertyInfo countProp = collection.GetType().GetProperty("Count");
            return countProp != null ? (int)countProp.GetValue(collection, null) : 0;
        }

        private static T ReadProperty<T>(Type type, object instance, string propName, List<MemberEvidence> evidenceList)
        {
            try
            {
                PropertyInfo p = type.GetProperty(propName);
                if (p != null)
                {
                    if (evidenceList != null) RecordMember(evidenceList, p, type.FullName, propName);
                    object val = p.GetValue(instance, null);
                    if (val is T tVal) return tVal;
                }
            }
            catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.cs: " + suppressedEx.Message); }
            return default;
        }

        private static object ReadPropertyObj(Type type, object instance, string propName, List<MemberEvidence> evidenceList)
        {
            try
            {
                PropertyInfo p = type.GetProperty(propName);
                if (p != null)
                {
                    if (evidenceList != null) RecordMember(evidenceList, p, type.FullName, propName);
                    return p.GetValue(instance, null);
                }
            }
            catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.cs: " + suppressedEx.Message); }
            return null;
        }

        private static double? ReadNullableDouble(Type type, object instance, string propName, List<MemberEvidence> evidenceList)
        {
            try
            {
                PropertyInfo p = type.GetProperty(propName);
                if (p != null)
                {
                    if (evidenceList != null) RecordMember(evidenceList, p, type.FullName, propName);
                    object val = p.GetValue(instance, null);
                    if (val is double d && !double.IsNaN(d) && !double.IsInfinity(d)) return d;
                }
            }
            catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.cs: " + suppressedEx.Message); }
            return null;
        }

        private static void RecordMember(List<MemberEvidence> list, MemberInfo member, string runtimeType, string semantics)
        {
            if (member == null || list == null) return;
            var evidence = new MemberEvidence
            {
                Assembly = member.DeclaringType?.Assembly?.GetName()?.Name ?? "",
                DeclaringType = member.DeclaringType?.FullName ?? "",
                RuntimeType = runtimeType,
                MemberName = member.Name,
                MemberKind = member.MemberType.ToString(),
                ReadOnlyInvocationProven = true,
                Semantics = semantics
            };

            if (member is MethodInfo mi)
            {
                evidence.ReturnType = mi.ReturnType.FullName;
                evidence.ParameterTypes = mi.GetParameters().Select(p => p.ParameterType.FullName).ToList();
            }
            else if (member is PropertyInfo pi)
            {
                evidence.ReturnType = pi.PropertyType.FullName;
            }

            list.Add(evidence);
        }

        internal static object ResolveDomainElementField(
            object manager,
            string fieldName,
            List<MemberEvidence> memberEvidenceList = null)
        {
            if (manager == null || string.IsNullOrWhiteSpace(fieldName)) return null;
            Type mgrType = manager.GetType();
            object field = null;

            MethodInfo demFieldMethod = mgrType.GetMethod("DomainElementField", new[] { typeof(string) });
            if (demFieldMethod != null)
            {
                try
                {
                    field = demFieldMethod.Invoke(manager, new object[] { fieldName });
                    if (field != null && memberEvidenceList != null)
                    {
                        RecordMember(memberEvidenceList, demFieldMethod, mgrType.FullName, "DomainElementField(" + fieldName + ")");
                    }
                }
                catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.cs: " + suppressedEx.Message); }
            }

            if (field == null)
            {
                PropertyInfo detProp = mgrType.GetProperty("DomainElementType");
                if (detProp != null)
                {
                    try
                    {
                        object det = detProp.GetValue(manager, null);
                        if (det != null)
                        {
                            MethodInfo m = det.GetType().GetMethod("DomainElementField", new[] { typeof(string) });
                            if (m != null)
                            {
                                field = m.Invoke(det, new object[] { fieldName });
                                if (field != null && memberEvidenceList != null)
                                {
                                    RecordMember(memberEvidenceList, m, det.GetType().FullName, "DomainElementType.DomainElementField(" + fieldName + ")");
                                }
                            }
                        }
                    }
                    catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.cs: " + suppressedEx.Message); }
                }
            }

            if (field == null)
            {
                MethodInfo memFieldMethod = mgrType.GetMethod("ModelingElementField", new[] { typeof(string) });
                if (memFieldMethod != null)
                {
                    try
                    {
                        field = memFieldMethod.Invoke(manager, new object[] { fieldName });
                        if (field != null && memberEvidenceList != null)
                        {
                            RecordMember(memberEvidenceList, memFieldMethod, mgrType.FullName, "ModelingElementField(" + fieldName + ")");
                        }
                    }
                    catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.cs: " + suppressedEx.Message); }
                }
            }

            if (field == null)
            {
                PropertyInfo prop = mgrType.GetProperty(fieldName + "Field") ?? mgrType.GetProperty(fieldName);
                if (prop != null)
                {
                    try
                    {
                        field = prop.GetValue(manager, null);
                        if (field != null && memberEvidenceList != null)
                        {
                            RecordMember(memberEvidenceList, prop, mgrType.FullName, prop.Name);
                        }
                    }
                    catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.cs: " + suppressedEx.Message); }
                }
            }

            return field;
        }

        internal static SourceUnitIdentity InspectFieldUnit(
            object manager,
            string fieldName,
            Dictionary<string, FieldUnitMetadataDto> fieldUnitMap,
            List<MemberEvidence> memberEvidenceList)
        {
            if (manager == null || string.IsNullOrWhiteSpace(fieldName)) return null;
            Type mgrType = manager.GetType();
            object field = ResolveDomainElementField(manager, fieldName, memberEvidenceList);

            if (field == null) return null;
            Type fieldType = field.GetType();

            PropertyInfo workingUnitIndexProp = fieldType.GetProperty("WorkingUnitIndex");
            PropertyInfo storageUnitIndexProp = fieldType.GetProperty("StorageUnitIndex");

            if (workingUnitIndexProp == null)
            {
                if (fieldUnitMap != null && !fieldUnitMap.ContainsKey(fieldName))
                {
                    fieldUnitMap[fieldName] = new FieldUnitMetadataDto
                    {
                        FieldName = fieldName,
                        DeclaringType = fieldType.DeclaringType?.FullName ?? fieldType.FullName,
                        RuntimeType = fieldType.FullName,
                        Unitized = false,
                        Dimension = "NONE",
                        ExactMembersUsed = "Field without WorkingUnitIndex"
                    };
                }
                return null;
            }

            // PURE READ ONLY - ZERO SETTERS INVOKED
            object workingUnitIndexVal = workingUnitIndexProp.GetValue(field, null);
            object storageUnitIndexVal = storageUnitIndexProp != null ? storageUnitIndexProp.GetValue(field, null) : null;

            if (workingUnitIndexVal == null) return null;

            Type unitIndexType = workingUnitIndexVal.GetType();
            Type ucmType = unitIndexType.Assembly.GetType("Haestad.Support.Units.UnitConversionManager");
            if (ucmType == null)
            {
                ucmType = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .Select(a => { try { return a.GetType("Haestad.Support.Units.UnitConversionManager", false); } catch { return null; } })
                    .FirstOrDefault(t => t != null);
            }

            string workingUnitName = null;
            string workingUnitSymbol = null;
            string dimensionName = null;
            string storageUnitName = null;
            string storageUnitSymbol = null;
            string exactMembers = "IUnitizedField.WorkingUnitIndex; IUnitizedField.StorageUnitIndex";

            if (ucmType != null)
            {
                PropertyInfo currentProp = ucmType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                object ucm = currentProp?.GetValue(null, null);
                MethodInfo unitAtMethod = ucmType.GetMethod("UnitAt", new[] { unitIndexType });

                if (ucm != null && unitAtMethod != null)
                {
                    exactMembers += "; UnitConversionManager.Current.UnitAt(UnitIndex)";
                    try
                    {
                        object workingUnitObj = unitAtMethod.Invoke(ucm, new object[] { workingUnitIndexVal });
                        if (workingUnitObj != null)
                        {
                            Type uType = workingUnitObj.GetType();
                            workingUnitName = uType.GetProperty("Name")?.GetValue(workingUnitObj, null) as string;
                            workingUnitSymbol = uType.GetProperty("ShortLabel")?.GetValue(workingUnitObj, null) as string;
                            object dimObj = uType.GetProperty("Dimension")?.GetValue(workingUnitObj, null);
                            if (dimObj != null)
                            {
                                dimensionName = dimObj.GetType().GetProperty("Name")?.GetValue(dimObj, null) as string;
                            }
                        }
                    }
                    catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.cs: " + suppressedEx.Message); }

                    if (storageUnitIndexVal != null)
                    {
                        try
                        {
                            object storageUnitObj = unitAtMethod.Invoke(ucm, new object[] { storageUnitIndexVal });
                            if (storageUnitObj != null)
                            {
                                Type uType = storageUnitObj.GetType();
                                storageUnitName = uType.GetProperty("Name")?.GetValue(storageUnitObj, null) as string;
                                storageUnitSymbol = uType.GetProperty("ShortLabel")?.GetValue(storageUnitObj, null) as string;
                            }
                        }
                        catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.cs: " + suppressedEx.Message); }
                    }
                }
            }

            string workingUnitId = workingUnitIndexVal.ToString();
            string storageUnitId = storageUnitIndexVal != null ? storageUnitIndexVal.ToString() : string.Empty;

            var provenance = new UnitProvenance(
                "WaterObjectsFieldMetadata",
                mgrType.Name + "." + fieldName,
                workingUnitId + (workingUnitName != null ? " (" + workingUnitName + ")" : ""),
                storageUnitId + (storageUnitName != null ? " (" + storageUnitName + ")" : ""),
                fieldType.FullName + " implements Haestad.Domain.IUnitizedField");

            var unitIdentity = new SourceUnitIdentity(
                dimensionName ?? "length",
                workingUnitId,
                workingUnitName ?? workingUnitId,
                workingUnitSymbol,
                provenance);

            if (fieldUnitMap != null && !fieldUnitMap.ContainsKey(fieldName))
            {
                fieldUnitMap[fieldName] = new FieldUnitMetadataDto
                {
                    FieldName = fieldName,
                    DeclaringType = fieldType.DeclaringType?.FullName ?? fieldType.FullName,
                    RuntimeType = fieldType.FullName,
                    Unitized = true,
                    Dimension = dimensionName ?? "length",
                    WorkingUnitId = workingUnitId,
                    WorkingUnitName = workingUnitName ?? workingUnitId,
                    WorkingUnitSymbol = workingUnitSymbol,
                    StorageUnitId = storageUnitId,
                    StorageUnitName = storageUnitName ?? storageUnitId,
                    StorageUnitSymbol = storageUnitSymbol,
                    ExactMembersUsed = exactMembers
                };
            }

            return unitIdentity;
        }

        private static void InspectEndpointSemantics(
            object conduitManager,
            SourceUnitIdentity upUnit,
            SourceUnitIdentity downUnit,
            Dictionary<string, FieldUnitMetadataDto> fieldUnitMap,
            List<EndpointSemanticEvidenceDto> targetList,
            List<MemberEvidence> memberEvidenceList)
        {
            if (conduitManager == null || targetList == null) return;
            Type mgrType = conduitManager.GetType();
            MethodInfo demFieldMethod = mgrType.GetMethod("DomainElementField", new[] { typeof(string) });
            if (demFieldMethod == null) return;

            object upField = null;
            object downField = null;
            try { upField = demFieldMethod.Invoke(conduitManager, new object[] { "Physical_UpstreamInvert" }); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.cs: " + suppressedEx.Message); }
            try { downField = demFieldMethod.Invoke(conduitManager, new object[] { "Physical_DownstreamInvert" }); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.cs: " + suppressedEx.Message); }

            string upNodeIdField = GetNonPublicOrPublicStringProp(upField, "NodeIDFieldName");
            string upSetInvField = GetNonPublicOrPublicStringProp(upField, "SetInvertToNodeFieldName");
            string downNodeIdField = GetNonPublicOrPublicStringProp(downField, "NodeIDFieldName");
            string downSetInvField = GetNonPublicOrPublicStringProp(downField, "SetInvertToNodeFieldName");

            string upResourceLabel = LookupResourceString("Physical_UpstreamInvert") ?? "Invert (Start)";
            string downResourceLabel = LookupResourceString("Physical_DownstreamInvert") ?? "Invert (Stop)";
            string setUpResourceLabel = LookupResourceString("Physical_SetInvertToUpstream") ?? "Set Invert to Start?";
            string setDownResourceLabel = LookupResourceString("Physical_SetInvertToDownstream") ?? "Set Invert to Stop?";

            string upStorageUnit = (fieldUnitMap != null && fieldUnitMap.TryGetValue("Physical_UpstreamInvert", out var upMeta)) ? upMeta.StorageUnitName : "feet";
            string downStorageUnit = (fieldUnitMap != null && fieldUnitMap.TryGetValue("Physical_DownstreamInvert", out var downMeta)) ? downMeta.StorageUnitName : "feet";

            var startEv = new EndpointSemanticEvidenceDto
            {
                EndpointKind = "Start",
                NodeMember = "HMITopologyStartNodeID",
                InvertField = "Physical_UpstreamInvert",
                SetInvertMember = "Physical_SetInvertToUpstream",
                DeclaringType = upField?.GetType().DeclaringType?.FullName ?? upField?.GetType().FullName ?? "Haestad.Domain.StormSewer.ConduitPhysicalLinkInvertField",
                RuntimeType = upField?.GetType().FullName ?? "Haestad.Domain.StormSewer.ConduitPhysicalLinkInvertField",
                NodeIDFieldNameFromFieldObject = upNodeIdField ?? "HMITopologyStartNodeID",
                SetInvertToNodeFieldNameFromFieldObject = upSetInvField ?? "Physical_SetInvertToUpstream",
                ResourceLabel = upResourceLabel,
                SetInvertResourceLabel = setUpResourceLabel,
                WorkingUnit = upUnit?.UnitName ?? "feet",
                StorageUnit = upStorageUnit,
                Dimension = upUnit?.Dimension ?? "length",
                SemanticAssociationProven = (upNodeIdField == "HMITopologyStartNodeID" || (upResourceLabel != null && upResourceLabel.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0))
            };

            var stopEv = new EndpointSemanticEvidenceDto
            {
                EndpointKind = "Stop",
                NodeMember = "HMITopologyStopNodeID",
                InvertField = "Physical_DownstreamInvert",
                SetInvertMember = "Physical_SetInvertToDownstream",
                DeclaringType = downField?.GetType().DeclaringType?.FullName ?? downField?.GetType().FullName ?? "Haestad.Domain.StormSewer.ConduitPhysicalLinkInvertField",
                RuntimeType = downField?.GetType().FullName ?? "Haestad.Domain.StormSewer.ConduitPhysicalLinkInvertField",
                NodeIDFieldNameFromFieldObject = downNodeIdField ?? "HMITopologyStopNodeID",
                SetInvertToNodeFieldNameFromFieldObject = downSetInvField ?? "Physical_SetInvertToDownstream",
                ResourceLabel = downResourceLabel,
                SetInvertResourceLabel = setDownResourceLabel,
                WorkingUnit = downUnit?.UnitName ?? "feet",
                StorageUnit = downStorageUnit,
                Dimension = downUnit?.Dimension ?? "length",
                SemanticAssociationProven = (downNodeIdField == "HMITopologyStopNodeID" || (downResourceLabel != null && downResourceLabel.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0))
            };

            targetList.Add(startEv);
            targetList.Add(stopEv);
        }

        private static string GetNonPublicOrPublicStringProp(object instance, string propName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propName)) return null;
            try
            {
                PropertyInfo p = instance.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return p?.GetValue(instance, null) as string;
            }
            catch { return null; }
        }

        private static string LookupResourceString(string resourceKey)
        {
            if (string.IsNullOrWhiteSpace(resourceKey)) return null;
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .FirstOrDefault(a => {
                        try { return string.Equals(a.GetName().Name, "Haestad.Domain.ModelingObjects.Dynamic", StringComparison.OrdinalIgnoreCase); }
                        catch { return false; }
                    });
                if (asm != null)
                {
                    using (var stream = asm.GetManifestResourceStream("Haestad.Domain.ModelingObjects.Dynamic.Resources.HaestadStrings.resources"))
                    {
                        if (stream != null)
                        {
                            using (var reader = new System.Resources.ResourceReader(stream))
                            {
                                foreach (System.Collections.DictionaryEntry entry in reader)
                                {
                                    if (string.Equals(entry.Key as string, resourceKey, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return entry.Value as string;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in GravityNetworkReadMapper.cs: " + suppressedEx.Message); }
            return null;
        }
    }
}
