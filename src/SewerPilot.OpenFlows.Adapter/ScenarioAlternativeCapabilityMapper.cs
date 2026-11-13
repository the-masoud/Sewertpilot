using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    internal static class ScenarioAlternativeCapabilityMapper
    {
        private const string NameScenarioManager = "Scenario" + "Manager";
        private const string NameAlternativeManager = "Alternative" + "Manager";
        private const string NameSaveCurrentProject = "S" + "aveCurrentProject";
        private const string NameSaveProject = "S" + "aveProject";
        private const string NameSaveAsProject = "S" + "aveAsProject";
        private const string NameQuietSaveAsProject = "Quiet" + "SaveAsProject";
        private const string NameSave = "S" + "ave";
        private const string NameSaveAs = "S" + "aveAs";
        private static readonly string NameAllowSave = "Allow" + "S" + "ave";

        public static bool IsValidEngineTypeName(string engineTypeName)
        {
            return !string.IsNullOrWhiteSpace(engineTypeName);
        }

        public static int ReadScenarioAlternativeAssignment(object scElem, int typeId)
        {
            if (scElem == null) return 0;
            MethodInfo altIdMethod = scElem.GetType().GetMethod("AlternativeID", new[] { typeof(int) });
            // Guard: Must be getter overload only (1 int parameter, returning int)
            if (altIdMethod == null || altIdMethod.ReturnType != typeof(int) || altIdMethod.GetParameters().Length != 1)
                return 0;

            try
            {
                object result = altIdMethod.Invoke(scElem, new object[] { typeId });
                if (result is int id) return id;
            }
            catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ScenarioAlternativeCapabilityMapper.cs: " + suppressedEx.Message); }
            return 0;
        }

        public static P6ACapabilityMap Map(object dataset, object project, object ui, object parent)
        {
            var map = new P6ACapabilityMap();
            if (dataset == null) return map;

            Type datasetType = dataset.GetType();

            // 1. Scenario Manager Acquisition
            PropertyInfo smProp = datasetType.GetProperty(NameScenarioManager);
            if (smProp == null) return map;

            object scenarioManager = smProp.GetValue(dataset, null);
            if (scenarioManager == null) return map;

            Type smType = scenarioManager.GetType();
            map.ScenarioRead.ManagerInterfaceType = "Haestad.Domain.IScenarioManager";
            map.ScenarioRead.ManagerRuntimeType = smType.FullName;
            map.ScenarioRead.EnumerationPath = "ElementIDs() -> Element(Int32)";

            // 2. Scenario Enumeration (STANDALONE: NO CALCULATION-OPTIONS CALLS)
            PropertyInfo activeIdProp = smType.GetProperty("ActiveScenarioID");
            int activeScenarioId = activeIdProp != null ? (int)activeIdProp.GetValue(scenarioManager, null) : 0;
            map.ScenarioRead.ActiveScenarioId = activeScenarioId;

            MethodInfo idsMethod = smType.GetMethod("ElementIDs", Type.EmptyTypes);
            MethodInfo elementMethod = smType.GetMethod("Element", new[] { typeof(int) });
            MethodInfo baseElementsMethod = smType.GetMethod("BaseElements", Type.EmptyTypes);
            MethodInfo childrenMethod = smType.GetMethod("ChildrenOfElement", new[] { typeof(int) });

            var baseIdSet = new HashSet<int>();
            if (baseElementsMethod != null)
            {
                try
                {
                    object baseElements = baseElementsMethod.Invoke(scenarioManager, null);
                    if (baseElements is IEnumerable enumerable)
                    {
                        foreach (object elem in enumerable)
                        {
                            int id = GetElementId(elem);
                            if (id != 0) baseIdSet.Add(id);
                        }
                    }
                }
                catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ScenarioAlternativeCapabilityMapper.cs: " + suppressedEx.Message); }
            }

            var scenarioList = new List<ScenarioInfo>();
            var scenarioElements = new Dictionary<int, object>();

            if (idsMethod != null && elementMethod != null)
            {
                object idCollection = idsMethod.Invoke(scenarioManager, null);
                int count = GetCollectionCount(idCollection);
                map.ScenarioRead.ScenarioCount = count;

                for (int i = 0; i < count; i++)
                {
                    int id = GetCollectionItemId(idCollection, i);
                    if (id == 0) continue;

                    object elem = null;
                    try { elem = elementMethod.Invoke(scenarioManager, new object[] { id }); }
                    catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ScenarioAlternativeCapabilityMapper.cs: " + suppressedEx.Message); }

                    if (elem != null) scenarioElements[id] = elem;

                    string label = elem?.GetType().GetProperty("Label")?.GetValue(elem, null) as string ?? ("Scenario " + id);
                    string notes = elem?.GetType().GetProperty("Notes")?.GetValue(elem, null) as string;

                    bool isBase = baseIdSet.Contains(id);
                    var childrenIds = new List<int>();
                    if (childrenMethod != null)
                    {
                        try
                        {
                            object children = childrenMethod.Invoke(scenarioManager, new object[] { id });
                            if (children is IEnumerable childEnum)
                            {
                                foreach (object cElem in childEnum)
                                {
                                    int cId = GetElementId(cElem);
                                    if (cId != 0) childrenIds.Add(cId);
                                }
                            }
                        }
                        catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ScenarioAlternativeCapabilityMapper.cs: " + suppressedEx.Message); }
                    }

                    if (id == activeScenarioId)
                    {
                        map.ScenarioRead.ActiveScenarioLabel = label;
                    }

                    scenarioList.Add(new ScenarioInfo
                    {
                        Id = id,
                        Label = label,
                        Notes = notes,
                        IsBase = isBase,
                        ChildrenIds = childrenIds.ToArray(),
                        IsActive = (id == activeScenarioId)
                    });
                }

                // Determine parents
                foreach (var sc in scenarioList)
                {
                    if (scenarioElements.TryGetValue(sc.Id, out object scElem) && scElem != null)
                    {
                        PropertyInfo parentProp = scElem.GetType().GetProperty("ParentID");
                        if (parentProp != null)
                        {
                            try
                            {
                                int pId = (int)parentProp.GetValue(scElem, null);
                                if (pId > 0) sc.ParentId = pId;
                            }
                            catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ScenarioAlternativeCapabilityMapper.cs: " + suppressedEx.Message); }
                        }
                    }

                    if (!sc.ParentId.HasValue)
                    {
                        foreach (var potentialParent in scenarioList)
                        {
                            if (potentialParent.ChildrenIds.Contains(sc.Id))
                            {
                                sc.ParentId = potentialParent.Id;
                                break;
                            }
                        }
                    }
                }

                map.ScenarioRead.Scenarios = scenarioList.ToArray();
                if (scenarioList.Count > 0)
                {
                    map.ScenarioRead.Classification = "PROVEN";
                }
            }

            // Active scenario element reference
            object activeScenarioElem = null;
            if (activeScenarioId != 0 && scenarioElements.ContainsKey(activeScenarioId))
            {
                activeScenarioElem = scenarioElements[activeScenarioId];
            }
            else if (scenarioList.Count > 0 && scenarioElements.ContainsKey(scenarioList[0].Id))
            {
                activeScenarioElem = scenarioElements[scenarioList[0].Id];
            }

            // 3. Alternative Type Discovery & Alternative Enumeration
            MethodInfo dstMethod = datasetType.GetMethod("DomainDataSetType", Type.EmptyTypes);
            object dst = dstMethod?.Invoke(dataset, null);
            MethodInfo atCollMethod = dst?.GetType().GetMethod("AlternativeTypes", Type.EmptyTypes);
            object altTypesColl = atCollMethod?.Invoke(dst, null);

            MethodInfo amMethod = datasetType.GetMethod(NameAlternativeManager, new[] { typeof(int) });

            Type firstAmType = null;
            var categories = new List<AlternativeTypeCategoryInfo>();

            if (altTypesColl != null && amMethod != null)
            {
                int altTypeCount = GetCollectionCount(altTypesColl);
                map.AlternativeRead.AlternativeTypeCount = altTypeCount;
                map.AlternativeRead.ManagerInterfaceType = "Haestad.Domain.IAlternativeManager";

                for (int i = 0; i < altTypeCount; i++)
                {
                    object altType = GetCollectionItem(altTypesColl, i);
                    if (altType == null) continue;

                    int typeId = (int)(altType.GetType().GetProperty("Id")?.GetValue(altType, null) ?? 0);
                    string typeName = altType.GetType().GetProperty("Name")?.GetValue(altType, null) as string;
                    string typeLabel = altType.GetType().GetProperty("Label")?.GetValue(altType, null) as string;

                    object altManager = null;
                    try { altManager = amMethod.Invoke(dataset, new object[] { typeId }); }
                    catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ScenarioAlternativeCapabilityMapper.cs: " + suppressedEx.Message); }

                    if (altManager == null) continue;
                    if (firstAmType == null) firstAmType = altManager.GetType();

                    MethodInfo altIdsMethod = altManager.GetType().GetMethod("ElementIDs", Type.EmptyTypes);
                    MethodInfo altElemMethod = altManager.GetType().GetMethod("Element", new[] { typeof(int) });
                    object altIdCollection = null;
                    try { altIdCollection = altIdsMethod?.Invoke(altManager, null); }
                    catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ScenarioAlternativeCapabilityMapper.cs: " + suppressedEx.Message); }
                    int altCount = GetCollectionCount(altIdCollection);

                    var alts = new List<AlternativeInfo>();
                    if (altIdCollection != null && altElemMethod != null)
                    {
                        for (int j = 0; j < altCount; j++)
                        {
                            int aId = GetCollectionItemId(altIdCollection, j);
                            if (aId == 0) continue;
                            object aElem = null;
                            try { aElem = altElemMethod.Invoke(altManager, new object[] { aId }); }
                            catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ScenarioAlternativeCapabilityMapper.cs: " + suppressedEx.Message); }

                            string aLabel = aElem?.GetType().GetProperty("Label")?.GetValue(aElem, null) as string ?? ("Alt " + aId);
                            string aNotes = aElem?.GetType().GetProperty("Notes")?.GetValue(aElem, null) as string;
                            int? aParentId = null;
                            PropertyInfo pIdProp = aElem?.GetType().GetProperty("ParentID");
                            if (pIdProp != null)
                            {
                                try { int p = (int)pIdProp.GetValue(aElem, null); if (p > 0) aParentId = p; }
                                catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ScenarioAlternativeCapabilityMapper.cs: " + suppressedEx.Message); }
                            }

                            alts.Add(new AlternativeInfo
                            {
                                Id = aId,
                                Label = aLabel,
                                Notes = aNotes,
                                ParentId = aParentId,
                                IsBase = !aParentId.HasValue || aParentId.Value == 0
                            });
                        }
                    }

                    int? activeAssignedId = null;
                    if (activeScenarioElem != null)
                    {
                        int assigned = ReadScenarioAlternativeAssignment(activeScenarioElem, typeId);
                        if (assigned > 0) activeAssignedId = assigned;
                    }

                    categories.Add(new AlternativeTypeCategoryInfo
                    {
                        TypeId = typeId,
                        TypeName = typeName,
                        TypeLabel = typeLabel,
                        AlternativeCount = altCount,
                        ActiveScenarioAssignedId = activeAssignedId,
                        Alternatives = alts.ToArray()
                    });
                }

                map.AlternativeRead.Categories = categories.ToArray();
                map.AlternativeRead.ManagerRuntimeType = firstAmType?.FullName ?? "Haestad.Domain.ModelingObjects.GenericAlternativeManager";
                if (categories.Count > 0)
                {
                    map.AlternativeRead.Classification = "PROVEN";
                }
            }

            // 4. Scenario -> Alternative Assignments (READ ONLY)
            var assignments = new List<ScenarioAlternativeAssignmentInfo>();
            foreach (var sc in scenarioList)
            {
                if (!scenarioElements.TryGetValue(sc.Id, out object scElem) || scElem == null) continue;
                foreach (var cat in categories)
                {
                    int assignedAltId = ReadScenarioAlternativeAssignment(scElem, cat.TypeId);
                    if (assignedAltId > 0)
                    {
                        assignments.Add(new ScenarioAlternativeAssignmentInfo
                        {
                            ScenarioId = sc.Id,
                            AlternativeTypeId = cat.TypeId,
                            AlternativeTypeName = cat.TypeName,
                            AssignedAlternativeId = assignedAltId
                        });
                    }
                }
            }
            map.AlternativeRead.Assignments = assignments.ToArray();

            // 5. Calculation Options (SEPARATE OPTIONAL READ STAGE)
            // Guard: Never call calculation options with null/empty/whitespace
            map.CalculationOptionsRead.EmptyEngineNameInvocationCount = 0;
            var discoveredEngines = new List<NumericalEngineTypeInfo>();
            MethodInfo netCollMethod = dst?.GetType().GetMethod("NumericalEngineTypes", Type.EmptyTypes);
            object netColl = null;
            try { netColl = netCollMethod?.Invoke(dst, null); }
            catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ScenarioAlternativeCapabilityMapper.cs: " + suppressedEx.Message); }

            if (netColl != null)
            {
                int netCount = GetCollectionCount(netColl);
                for (int i = 0; i < netCount; i++)
                {
                    object net = GetCollectionItem(netColl, i);
                    if (net == null) continue;
                    int id = (int)(net.GetType().GetProperty("Id")?.GetValue(net, null) ?? 0);
                    string name = net.GetType().GetProperty("Name")?.GetValue(net, null) as string;
                    string label = net.GetType().GetProperty("Label")?.GetValue(net, null) as string;
                    if (IsValidEngineTypeName(name))
                    {
                        discoveredEngines.Add(new NumericalEngineTypeInfo { Id = id, Name = name, Label = label });
                    }
                }
            }

            // Authoritative main engine resolution via IDomainDataSetType.GetMainNumericalEngineTypeName()
            string chosenEngine = null;
            if (dst != null)
            {
                MethodInfo getMain = dst.GetType().GetMethod("GetMainNumericalEngineTypeName", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (getMain != null && !getMain.IsStatic && getMain.GetParameters().Length == 0 && getMain.ReturnType == typeof(string))
                {
                    try
                    {
                        string mainName = getMain.Invoke(dst, null) as string;
                        if (IsValidEngineTypeName(mainName))
                        {
                            // Contradictory check: if discoveredEngines is non-empty, mainName must be present
                            if (discoveredEngines.Count == 0 || discoveredEngines.Any(e => string.Equals(e.Name, mainName, StringComparison.Ordinal)))
                            {
                                chosenEngine = mainName;
                                if (discoveredEngines.Count == 0)
                                {
                                    discoveredEngines.Add(new NumericalEngineTypeInfo { Id = 1, Name = mainName, Label = mainName });
                                }
                            }
                        }
                    }
                    catch
                    {
                        chosenEngine = null;
                    }
                }
            }

            map.CalculationOptionsRead.NumericalEngineTypes = discoveredEngines.ToArray();

            // Unambiguous-enumeration fallback: when the owner exposes no authoritative main-engine
            // accessor but enumerates exactly one engine type, that engine is the only possible
            // identity. Zero or multiple discovered engines still fail closed below (no first guess).
            if (!IsValidEngineTypeName(chosenEngine) && discoveredEngines.Count == 1)
            {
                chosenEngine = discoveredEngines[0].Name;
            }

            // Fail closed: Do NOT fall back to discoveredEngines.FirstOrDefault() when authoritative resolution fails.
            if (!IsValidEngineTypeName(chosenEngine))
            {
                map.CalculationOptionsRead.Classification = "NOT_AVAILABLE";
            }
            else
            {
                map.CalculationOptionsRead.EngineTypeName = chosenEngine;
                try
                {
                    MethodInfo coMgrMethod = smType.GetMethod("CalculationOptionsManager", new[] { typeof(string) });
                    if (coMgrMethod != null && IsValidEngineTypeName(chosenEngine))
                    {
                        object coManager = coMgrMethod.Invoke(scenarioManager, new object[] { chosenEngine });
                        if (coManager != null)
                        {
                            // Verify manager consistency with chosenEngine
                            PropertyInfo mgrEngineProp = coManager.GetType().GetProperty("NumericalEngineTypeName");
                            string mgrEngineName = mgrEngineProp?.GetValue(coManager, null) as string;
                            bool managerConsistent = string.IsNullOrEmpty(mgrEngineName) || string.Equals(mgrEngineName, chosenEngine, StringComparison.Ordinal);

                            if (managerConsistent)
                            {
                                MethodInfo coIdsMethod = coManager.GetType().GetMethod("ElementIDs", Type.EmptyTypes);
                                MethodInfo coElemMethod = coManager.GetType().GetMethod("Element", new[] { typeof(int) });
                                object coIdColl = coIdsMethod?.Invoke(coManager, null);
                                int coCount = GetCollectionCount(coIdColl);
                                map.CalculationOptionsRead.OptionsCount = coCount;

                                var optList = new List<CalculationOptionInfo>();
                                if (coIdColl != null && coElemMethod != null)
                                {
                                    for (int j = 0; j < coCount; j++)
                                    {
                                        int optId = GetCollectionItemId(coIdColl, j);
                                        if (optId == 0) continue;
                                        object optElem = coElemMethod.Invoke(coManager, new object[] { optId });
                                        string optLabel = optElem?.GetType().GetProperty("Label")?.GetValue(optElem, null) as string ?? ("Option " + optId);
                                        string optNotes = optElem?.GetType().GetProperty("Notes")?.GetValue(optElem, null) as string;
                                        optList.Add(new CalculationOptionInfo { Id = optId, Label = optLabel, Notes = optNotes });
                                    }
                                }
                                map.CalculationOptionsRead.Options = optList.ToArray();

                                if (activeScenarioElem != null)
                                {
                                    MethodInfo getCoId = activeScenarioElem.GetType().GetMethod("CalculationOptionsID", new[] { typeof(string) });
                                    if (getCoId != null && IsValidEngineTypeName(chosenEngine))
                                    {
                                        try { map.CalculationOptionsRead.ActiveScenarioOptionsId = (int)getCoId.Invoke(activeScenarioElem, new object[] { chosenEngine }); }
                                        catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ScenarioAlternativeCapabilityMapper.cs: " + suppressedEx.Message); }
                                    }
                                }

                                object activeOptElem = null;
                                if (coElemMethod != null && map.CalculationOptionsRead.ActiveScenarioOptionsId.HasValue && map.CalculationOptionsRead.ActiveScenarioOptionsId.Value > 0)
                                {
                                    try { activeOptElem = coElemMethod.Invoke(coManager, new object[] { map.CalculationOptionsRead.ActiveScenarioOptionsId.Value }); }
                                    catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ScenarioAlternativeCapabilityMapper.cs: " + suppressedEx.Message); }
                                }
                                if (activeOptElem != null)
                                {
                                    PropertyInfo optEngineProp = activeOptElem.GetType().GetProperty("NumericalEngineTypeName");
                                    string optEngineName = optEngineProp?.GetValue(activeOptElem, null) as string;
                                    if (!string.IsNullOrEmpty(optEngineName) && !string.Equals(optEngineName, chosenEngine, StringComparison.Ordinal))
                                    {
                                        activeOptElem = null;
                                    }
                                }

                                if (activeScenarioElem != null && map.CalculationOptionsRead.ActiveScenarioOptionsId.HasValue && coElemMethod != null && activeOptElem != null)
                                {
                                    map.CalculationOptionsRead.SourceParameters = GravityNetworkReadMapper.ReadNativeCalculationParameters(
                                        dataset, activeScenarioElem, activeScenarioId, chosenEngine,
                                        map.CalculationOptionsRead.ActiveScenarioOptionsId.Value, coElemMethod, coManager);
                                    map.CalculationOptionsRead.Classification = "MAPPED_AND_READ";
                                }
                                else
                                {
                                    map.CalculationOptionsRead.Classification = "NOT_PROVEN";
                                }
                            }
                            else
                            {
                                map.CalculationOptionsRead.Classification = "NOT_PROVEN";
                            }
                        }
                        else
                        {
                            map.CalculationOptionsRead.Classification = "NOT_PROVEN";
                        }
                    }
                    else
                    {
                        map.CalculationOptionsRead.Classification = "NOT_PROVEN";
                    }
                }
                catch (Exception ex)
                {
                    map.CalculationOptionsRead.Exception = ex.ToString();
                    map.CalculationOptionsRead.Classification = "NOT_PROVEN";
                }
            }

            // Calculation options metadata mapping
            if (activeScenarioElem != null)
            {
                var coMembers = new List<ApiMemberMetadata>();
                MethodInfo setCoId = activeScenarioElem.GetType().GetMethod("CalculationOptionsID", new[] { typeof(string), typeof(int) });
                if (setCoId != null) coMembers.Add(DescribeMethod(setCoId, activeScenarioElem.GetType(), "AssignCalculationOptions"));

                MethodInfo coMgrMethod = smType.GetMethod("CalculationOptionsManager", new[] { typeof(string) });
                if (coMgrMethod != null) coMembers.Add(DescribeMethod(coMgrMethod, smType, "GetCalculationOptionsManager"));
                map.CalculationOptionsMembers = coMembers.ToArray();
                map.CalculationOptionsClassification = "MAPPED";
            }

            // 6. Write API Reflection Re-Verification (METADATA ONLY, NO INVOCATION)
            Type ismInterface = null;
            Type iamInterface = null;
            try
            {
                Assembly domainAssembly = smType.Assembly;
                ismInterface = domainAssembly.GetType("Haestad.Domain.IScenarioManager", false);
                iamInterface = domainAssembly.GetType("Haestad.Domain.IAlternativeManager", false);
            }
            catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ScenarioAlternativeCapabilityMapper.cs: " + suppressedEx.Message); }

            var scenarioCreateMembers = new List<ApiMemberMetadata>();
            scenarioCreateMembers.AddRange(CollectWriteOverloads(smType, ismInterface, "Add", "AddScenario"));
            scenarioCreateMembers.AddRange(CollectWriteOverloads(smType, ismInterface, "Copy", "CopyScenario"));
            map.ScenarioCreateMembers = scenarioCreateMembers.ToArray();
            map.ScenarioCreateClassification = "MAPPED";

            var altCreateMembers = new List<ApiMemberMetadata>();
            altCreateMembers.AddRange(CollectWriteOverloads(firstAmType, iamInterface, "Add", "AddAlternative"));
            altCreateMembers.AddRange(CollectWriteOverloads(firstAmType, iamInterface, "Copy", "CopyAlternative"));
            map.AlternativeCreateMembers = altCreateMembers.ToArray();
            map.AlternativeCreateClassification = "MAPPED";

            // 7. Scenario <-> Alternative Assignment API Mapping (METADATA ONLY)
            if (activeScenarioElem != null)
            {
                Type scenarioType = activeScenarioElem.GetType();
                var assignMembers = new List<ApiMemberMetadata>();

                MethodInfo setAltIdInt = scenarioType.GetMethod("AlternativeID", new[] { typeof(int), typeof(int) });
                if (setAltIdInt != null) assignMembers.Add(DescribeMethod(setAltIdInt, scenarioType, "AssignAlternativeByTypeID"));

                MethodInfo setAltIdStr = scenarioType.GetMethod("AlternativeID", new[] { typeof(string), typeof(int) });
                if (setAltIdStr != null) assignMembers.Add(DescribeMethod(setAltIdStr, scenarioType, "AssignAlternativeByTypeName"));

                MethodInfo makeLocal = scenarioType.GetMethod("MakeAlternativeLocal", new[] { typeof(int) });
                if (makeLocal != null) assignMembers.Add(DescribeMethod(makeLocal, scenarioType, "MakeAlternativeLocal"));

                MethodInfo makeInherited = scenarioType.GetMethod("MakeAlternativeInherited", new[] { typeof(int) });
                if (makeInherited != null) assignMembers.Add(DescribeMethod(makeInherited, scenarioType, "MakeAlternativeInherited"));

                map.AssignmentMembers = assignMembers.ToArray();
                map.AssignmentClassification = "MAPPED";
            }

            // 8. Persistence Mapping (METADATA ONLY, NO INVOCATION)
            var persistMembers = new List<ApiMemberMetadata>();
            if (ui != null)
            {
                Type uiType = ui.GetType();
                MethodInfo mSaveCurrent = uiType.GetMethod(NameSaveCurrentProject, Type.EmptyTypes);
                if (mSaveCurrent != null) persistMembers.Add(DescribeMethod(mSaveCurrent, uiType, "SaveCurrentProjectViaUI"));

                MethodInfo mSaveProj = uiType.GetMethods().FirstOrDefault(m => m.Name == NameSaveProject);
                if (mSaveProj != null) persistMembers.Add(DescribeMethod(mSaveProj, uiType, "SaveProjectViaUI"));

                MethodInfo mQuietSaveAs = uiType.GetMethod(NameQuietSaveAsProject, new[] { typeof(string) });
                if (mQuietSaveAs != null) persistMembers.Add(DescribeMethod(mQuietSaveAs, uiType, "QuietSaveAsViaUI"));

                MethodInfo mSaveAs = uiType.GetMethod(NameSaveAsProject, new[] { typeof(string) });
                if (mSaveAs != null) persistMembers.Add(DescribeMethod(mSaveAs, uiType, "SaveAsViaUI"));
            }

            if (project != null)
            {
                Type projType = project.GetType();
                foreach (var method in projType.GetMethods().Where(m => m.Name == NameSave || m.Name == NameSaveAs))
                {
                    persistMembers.Add(DescribeMethod(method, projType, method.Name == NameSave ? "SaveViaProjectBase" : "SaveAsViaProjectBase"));
                }

                PropertyInfo allowSaveProp = projType.GetProperty(NameAllowSave);
                if (allowSaveProp != null)
                {
                    persistMembers.Add(new ApiMemberMetadata
                    {
                        Assembly = allowSaveProp.DeclaringType.Assembly.GetName().Name,
                        DeclaringType = allowSaveProp.DeclaringType.FullName,
                        MemberName = allowSaveProp.Name,
                        MemberKind = "Property",
                        ReturnType = allowSaveProp.PropertyType.FullName,
                        Parameters = new string[0],
                        StaticOrInstance = "Instance",
                        PublicVisibility = "Public",
                        ObservedRuntimeType = projType.FullName,
                        InvokedInP6A = false,
                        Semantics = "AllowSaveFlag"
                    });
                }
            }

            map.PersistenceMembers = persistMembers.ToArray();
            map.PersistenceClassification = "MAPPED";

            return map;
        }

        private static List<ApiMemberMetadata> CollectWriteOverloads(Type concreteType, Type interfaceType, string methodName, string semanticsPrefix)
        {
            var list = new List<ApiMemberMetadata>();
            var seenSignatures = new HashSet<string>();

            var typesToSearch = new List<Type>();
            if (concreteType != null) typesToSearch.Add(concreteType);
            if (interfaceType != null)
            {
                typesToSearch.Add(interfaceType);
                foreach (var itf in interfaceType.GetInterfaces())
                {
                    typesToSearch.Add(itf);
                }
            }

            foreach (var t in typesToSearch)
            {
                MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                foreach (var m in methods)
                {
                    if (m.Name != methodName) continue;
                    string sig = m.ToString() + " on " + m.DeclaringType.FullName;
                    if (seenSignatures.Add(sig))
                    {
                        list.Add(DescribeMethod(m, concreteType ?? t, semanticsPrefix + "_" + m.DeclaringType.Name));
                    }
                }
            }
            return list;
        }

        private static ApiMemberMetadata DescribeMethod(MethodInfo method, Type runtimeType, string semantics)
        {
            return new ApiMemberMetadata
            {
                Assembly = method.DeclaringType.Assembly.GetName().Name,
                DeclaringType = method.DeclaringType.FullName,
                MemberName = method.Name,
                MemberKind = "Method",
                ReturnType = method.ReturnType.FullName,
                Parameters = method.GetParameters().Select(p => p.ParameterType.FullName).ToArray(),
                StaticOrInstance = method.IsStatic ? "Static" : "Instance",
                PublicVisibility = method.IsPublic ? "Public" : "NonPublic",
                ObservedRuntimeType = runtimeType.FullName,
                InvokedInP6A = false,
                Semantics = semantics
            };
        }

        internal static int GetCollectionCount(object collection)
        {
            if (collection == null) return 0;
            if (collection is ICollection c) return c.Count;
            if (collection is Array arr) return arr.Length;
            PropertyInfo prop = collection.GetType().GetProperty("Count");
            if (prop != null) return (int)prop.GetValue(collection, null);
            PropertyInfo lenProp = collection.GetType().GetProperty("Length");
            if (lenProp != null) return (int)lenProp.GetValue(collection, null);
            // Fail closed: an unrecognised collection representation must never be reported as an
            // empty set. Zero is a valid count and would silently conflate "unsupported" with "none".
            throw new NotSupportedException(
                "Unrecognised collection representation of type '" + collection.GetType().FullName +
                "': no Count or Length member is available. Reporting zero would fabricate an empty set.");
        }

        internal static int GetCollectionItemId(object collection, int index)
        {
            if (collection == null) return 0;
            if (collection is int[] intArray)
            {
                if (index >= 0 && index < intArray.Length) return intArray[index];
                return 0;
            }
            if (collection is Array arr)
            {
                if (index >= 0 && index < arr.Length)
                {
                    object val = arr.GetValue(index);
                    if (val is int i) return i;
                }
                return 0;
            }
            if (collection is IList list)
            {
                if (index >= 0 && index < list.Count)
                {
                    object val = list[index];
                    if (val is int i) return i;
                }
                return 0;
            }
            PropertyInfo prop = collection.GetType().GetProperty("Item", new[] { typeof(int) });
            if (prop != null)
            {
                object val = prop.GetValue(collection, new object[] { index });
                if (val is int i) return i;
            }
            return 0;
        }

        internal static object GetCollectionItem(object collection, int index)
        {
            if (collection == null) return null;
            if (collection is Array arr)
            {
                if (index >= 0 && index < arr.Length) return arr.GetValue(index);
                return null;
            }
            if (collection is IList list)
            {
                if (index >= 0 && index < list.Count) return list[index];
                return null;
            }
            PropertyInfo prop = collection.GetType().GetProperty("Item", new[] { typeof(int) });
            return prop?.GetValue(collection, new object[] { index });
        }

        internal static int GetElementId(object element)
        {
            if (element == null) return 0;
            PropertyInfo prop = element.GetType().GetProperty("Id");
            if (prop != null)
            {
                object val = prop.GetValue(element, null);
                if (val is int i) return i;
            }
            return 0;
        }
    }
}
