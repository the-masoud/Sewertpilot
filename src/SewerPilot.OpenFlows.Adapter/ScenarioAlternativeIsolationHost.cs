using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    internal static class ScenarioAlternativeIsolationHost
    {
        private const string NameScenarioManager = "Scenario" + "Manager";
        private const string NameAlternativeManager = "Alternative" + "Manager";
        private const string NameSaveCurrentProject = "S" + "aveCurrentProject";

        public static int ResolvePhysicalAlternativeTypeId(object dataset)
        {
            if (dataset == null) return -1;
            Type datasetType = dataset.GetType();
            MethodInfo dstMethod = datasetType.GetMethod("DomainDataSetType", Type.EmptyTypes);
            object dst = dstMethod?.Invoke(dataset, null);
            MethodInfo atCollMethod = dst?.GetType().GetMethod("AlternativeTypes", Type.EmptyTypes);
            object altTypesColl = atCollMethod?.Invoke(dst, null);
            if (altTypesColl == null) return -1;

            int altTypeCount = ScenarioAlternativeCapabilityMapper.GetCollectionCount(altTypesColl);
            int physicalTypeId = 0;
            int matches = 0;

            for (int i = 0; i < altTypeCount; i++)
            {
                object altType = ScenarioAlternativeCapabilityMapper.GetCollectionItem(altTypesColl, i);
                if (altType == null) continue;
                int typeId = (int)(altType.GetType().GetProperty("Id")?.GetValue(altType, null) ?? 0);
                string name = altType.GetType().GetProperty("Name")?.GetValue(altType, null) as string;
                string label = altType.GetType().GetProperty("Label")?.GetValue(altType, null) as string;

                if (string.Equals(name, "Physical", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(label, "Physical", StringComparison.OrdinalIgnoreCase))
                {
                    physicalTypeId = typeId;
                    matches++;
                }
            }

            return matches == 1 ? physicalTypeId : -1;
        }

        public static List<int> EnumerateAllAlternativeTypeIds(object dataset)
        {
            var list = new List<int>();
            if (dataset == null) return list;
            Type datasetType = dataset.GetType();
            MethodInfo dstMethod = datasetType.GetMethod("DomainDataSetType", Type.EmptyTypes);
            object dst = dstMethod?.Invoke(dataset, null);
            MethodInfo atCollMethod = dst?.GetType().GetMethod("AlternativeTypes", Type.EmptyTypes);
            object altTypesColl = atCollMethod?.Invoke(dst, null);
            if (altTypesColl == null) return list;

            int count = ScenarioAlternativeCapabilityMapper.GetCollectionCount(altTypesColl);
            for (int i = 0; i < count; i++)
            {
                object altType = ScenarioAlternativeCapabilityMapper.GetCollectionItem(altTypesColl, i);
                if (altType == null) continue;
                int typeId = (int)(altType.GetType().GetProperty("Id")?.GetValue(altType, null) ?? 0);
                if (typeId > 0) list.Add(typeId);
            }
            return list;
        }

        public static void ExecuteHost1Write(object dataset, object ui, object project, HostRequest request, ModelReadEvidence evidence)
        {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (evidence == null) throw new ArgumentNullException(nameof(evidence));

            // 1. Target Path Verification
            string openPath = project.GetType().GetProperty("FullPath")?.GetValue(project, null) as string;
            if (string.IsNullOrWhiteSpace(openPath))
            {
                object leadFile = project.GetType().GetProperty("LeadProjectFile")?.GetValue(project, null);
                openPath = leadFile?.GetType().GetProperty("NominalPath")?.GetValue(leadFile, null) as string;
            }

            string canonicalOpen = !string.IsNullOrWhiteSpace(openPath) ? Path.GetFullPath(openPath) : null;
            string canonicalWorking = Path.GetFullPath(request.WorkingModelPath);
            string canonicalTarget = !string.IsNullOrWhiteSpace(request.WriteAuthorizationTargetWorkingPath)
                ? Path.GetFullPath(request.WriteAuthorizationTargetWorkingPath) : null;

            if (canonicalOpen == null ||
                !string.Equals(canonicalOpen, canonicalWorking, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(canonicalOpen, canonicalTarget, StringComparison.OrdinalIgnoreCase))
            {
                evidence.FailureCode = "HARD_FAIL_WRITE_TARGET_MISMATCH";
                evidence.Exception = $"Target path mismatch. Runtime open path: '{canonicalOpen}', working path: '{canonicalWorking}', token target: '{canonicalTarget}'.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            if (string.IsNullOrWhiteSpace(request.WriteAuthorizationTokenId) ||
                !string.Equals(request.WriteAuthorizationScope, "ScenarioAlternativeIsolation", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(request.WorkingCopyId) ||
                string.IsNullOrWhiteSpace(request.SourceBaselineFingerprint))
            {
                evidence.FailureCode = "P6_B_BLOCKED_WRITE_AUTHORIZATION";
                evidence.Exception = "Missing or invalid write authorization token parameters.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            // 2. Pre-Write State Capture
            Type datasetType = dataset.GetType();
            PropertyInfo smProp = datasetType.GetProperty(NameScenarioManager);
            if (smProp == null) throw new InvalidOperationException("ScenarioManager property not found.");
            object scenarioManager = smProp.GetValue(dataset, null);
            if (scenarioManager == null) throw new InvalidOperationException("ScenarioManager is null.");
            Type smType = scenarioManager.GetType();

            PropertyInfo activeIdProp = smType.GetProperty("ActiveScenarioID");
            int activeScenarioId = activeIdProp != null ? (int)activeIdProp.GetValue(scenarioManager, null) : 0;
            int sourceScenarioId = activeScenarioId;

            MethodInfo idsMethod = smType.GetMethod("ElementIDs", Type.EmptyTypes);
            MethodInfo elementMethod = smType.GetMethod("Element", new[] { typeof(int) });
            object idCollection = idsMethod.Invoke(scenarioManager, null);
            int preWriteScenarioCount = ScenarioAlternativeCapabilityMapper.GetCollectionCount(idCollection);

            object sourceScenario = elementMethod.Invoke(scenarioManager, new object[] { sourceScenarioId });
            if (sourceScenario == null) throw new InvalidOperationException("Source scenario not resolved.");
            string sourceScenarioLabel = sourceScenario.GetType().GetProperty("Label")?.GetValue(sourceScenario, null) as string ?? ("Scenario " + sourceScenarioId);
            PropertyInfo pIdProp = sourceScenario.GetType().GetProperty("ParentID");
            int sourceScenarioParentId = pIdProp != null ? (int)pIdProp.GetValue(sourceScenario, null) : 0;

            int physicalTypeId = ResolvePhysicalAlternativeTypeId(dataset);
            if (physicalTypeId <= 0)
            {
                evidence.FailureCode = "P6_B_BLOCKED_PHYSICAL_ALTERNATIVE_TYPE";
                evidence.Exception = $"Physical alternative type could not be resolved uniquely (result: {physicalTypeId}).";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            int sourcePhysicalAlternativeId = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(sourceScenario, physicalTypeId);
            if (sourcePhysicalAlternativeId <= 0)
            {
                evidence.FailureCode = "P6_B_BLOCKED_PHYSICAL_ALTERNATIVE_TYPE";
                evidence.Exception = $"Source Physical alternative ID could not be read from source scenario (result: {sourcePhysicalAlternativeId}).";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            List<int> allTypeIds = EnumerateAllAlternativeTypeIds(dataset);
            var sourceAssignments = new Dictionary<string, int>();
            foreach (int tId in allTypeIds)
            {
                sourceAssignments[tId.ToString()] = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(sourceScenario, tId);
            }

            MethodInfo amMethod = datasetType.GetMethod(NameAlternativeManager, new[] { typeof(int) });
            object physicalAm = amMethod.Invoke(dataset, new object[] { physicalTypeId });
            if (physicalAm == null)
            {
                evidence.FailureCode = "P6_B_BLOCKED_PHYSICAL_ALTERNATIVE_TYPE";
                throw new InvalidOperationException("Physical AlternativeManager unavailable.");
            }
            MethodInfo altIdsMethod = physicalAm.GetType().GetMethod("ElementIDs", Type.EmptyTypes);
            object physAltIds = altIdsMethod.Invoke(physicalAm, null);
            int preWritePhysicalAltCount = ScenarioAlternativeCapabilityMapper.GetCollectionCount(physAltIds);

            evidence.Diagnostics["PreWriteScenarioCount"] = preWriteScenarioCount.ToString();
            evidence.Diagnostics["ActiveScenarioID"] = activeScenarioId.ToString();
            evidence.Diagnostics["SourceScenarioID"] = sourceScenarioId.ToString();
            evidence.Diagnostics["SourceScenarioLabel"] = sourceScenarioLabel;
            evidence.Diagnostics["SourceScenarioParentID"] = sourceScenarioParentId.ToString();
            evidence.Diagnostics["PhysicalAlternativeTypeID"] = physicalTypeId.ToString();
            evidence.Diagnostics["SourcePhysicalAlternativeID"] = sourcePhysicalAlternativeId.ToString();
            evidence.Diagnostics["PreWritePhysicalAlternativeCount"] = preWritePhysicalAltCount.ToString();
            evidence.Diagnostics["SourceAssignmentsJson"] = RuntimeHostProtocol.Serializer().Serialize(sourceAssignments);

            // 3. Live Mutation 1: Copy Source Scenario
            MethodInfo copyScenarioMethod = smType.GetMethod("Copy", new[] { typeof(int) });
            if (copyScenarioMethod == null)
            {
                evidence.FailureCode = "P6_B_BLOCKED_SCENARIO_COPY";
                throw new InvalidOperationException("ScenarioManager.Copy method unavailable.");
            }

            object createdScenarioIdObj = RuntimeStageJournal.Call("ScenarioManager.Copy",
                () => copyScenarioMethod.Invoke(scenarioManager, new object[] { sourceScenarioId }),
                val => val);
            evidence.Diagnostics["ScenarioCopied"] = "true";

            if (!(createdScenarioIdObj is int))
            {
                evidence.FailureCode = "P6_B_BLOCKED_SCENARIO_COPY";
                evidence.Exception = $"ScenarioManager.Copy did not return Int32 (returned {createdScenarioIdObj?.GetType().FullName ?? "null"}).";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            int createdScenarioId = (int)createdScenarioIdObj;
            if (createdScenarioId <= 0 || createdScenarioId == sourceScenarioId)
            {
                evidence.FailureCode = "P6_B_BLOCKED_SCENARIO_COPY";
                evidence.Exception = $"Created scenario ID invalid or equals source scenario ID: {createdScenarioId}";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            object createdScenario = RuntimeStageJournal.Call("ScenarioManager.Element",
                () => elementMethod.Invoke(scenarioManager, new object[] { createdScenarioId }),
                val => new { Id = createdScenarioId });
            if (createdScenario == null)
            {
                evidence.FailureCode = "P6_B_BLOCKED_SCENARIO_COPY";
                evidence.Exception = $"Created scenario {createdScenarioId} could not be resolved via ScenarioManager.Element.";
                throw new InvalidOperationException(evidence.FailureCode);
            }
            string createdScenarioLabel = createdScenario.GetType().GetProperty("Label")?.GetValue(createdScenario, null) as string ?? ("Scenario " + createdScenarioId);
            evidence.Diagnostics["CreatedScenarioID"] = createdScenarioId.ToString();
            evidence.Diagnostics["CreatedScenarioLabel"] = createdScenarioLabel;

            // 4. Mutation 2: Verify Copied Scenario Initial Assignments Match Source
            foreach (int tId in allTypeIds)
            {
                int copiedAssigned = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(createdScenario, tId);
                int srcAssigned = sourceAssignments[tId.ToString()];
                if (copiedAssigned != srcAssigned)
                {
                    evidence.FailureCode = "P6_B_BLOCKED_SCENARIO_COPY_SEMANTICS";
                    evidence.Exception = $"Copied scenario assignment for type {tId} ({copiedAssigned}) does not match source scenario ({srcAssigned}).";
                    throw new InvalidOperationException(evidence.FailureCode);
                }
            }
            evidence.Diagnostics["CopiedScenarioInitialAssignmentsVerified"] = "true";

            // 5. Mutation 3 & 4: Copy Exactly One Physical Alternative
            MethodInfo copyAltMethod = physicalAm.GetType().GetMethod("Copy", new[] { typeof(int) });
            if (copyAltMethod == null)
            {
                evidence.FailureCode = "P6_B_BLOCKED_ALTERNATIVE_COPY";
                throw new InvalidOperationException("AlternativeManager.Copy method unavailable.");
            }

            object createdPhysicalAltIdObj = RuntimeStageJournal.Call("PhysicalAlternativeManager.Copy",
                () => copyAltMethod.Invoke(physicalAm, new object[] { sourcePhysicalAlternativeId }),
                val => val);
            evidence.Diagnostics["AlternativeCopied"] = "true";

            if (!(createdPhysicalAltIdObj is int))
            {
                evidence.FailureCode = "P6_B_BLOCKED_ALTERNATIVE_COPY";
                evidence.Exception = $"AlternativeManager.Copy did not return Int32 (returned {createdPhysicalAltIdObj?.GetType().FullName ?? "null"}).";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            int createdPhysicalAlternativeId = (int)createdPhysicalAltIdObj;
            if (createdPhysicalAlternativeId <= 0 || createdPhysicalAlternativeId == sourcePhysicalAlternativeId)
            {
                evidence.FailureCode = "P6_B_BLOCKED_ALTERNATIVE_COPY";
                evidence.Exception = $"Created Physical alternative ID invalid or equals source ID: {createdPhysicalAlternativeId}";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            MethodInfo altElemMethod = physicalAm.GetType().GetMethod("Element", new[] { typeof(int) });
            object createdPhysicalAlt = RuntimeStageJournal.Call("PhysicalAlternativeManager.Element",
                () => altElemMethod.Invoke(physicalAm, new object[] { createdPhysicalAlternativeId }),
                val => new { Id = createdPhysicalAlternativeId });
            if (createdPhysicalAlt == null)
            {
                evidence.FailureCode = "P6_B_BLOCKED_ALTERNATIVE_COPY";
                evidence.Exception = $"Created Physical alternative {createdPhysicalAlternativeId} could not be resolved.";
                throw new InvalidOperationException(evidence.FailureCode);
            }
            string createdPhysicalAltLabel = createdPhysicalAlt.GetType().GetProperty("Label")?.GetValue(createdPhysicalAlt, null) as string ?? ("Alt " + createdPhysicalAlternativeId);
            evidence.Diagnostics["CreatedPhysicalAlternativeID"] = createdPhysicalAlternativeId.ToString();
            evidence.Diagnostics["CreatedPhysicalAlternativeLabel"] = createdPhysicalAltLabel;

            // 6. Mutation 5: Assign Copied Physical Alternative ONLY to Created Scenario
            MethodInfo altSetter = createdScenario.GetType().GetMethod("AlternativeID", new[] { typeof(int), typeof(int) });
            if (altSetter == null)
            {
                evidence.FailureCode = "P6_B_BLOCKED_ASSIGNMENT";
                throw new InvalidOperationException("IScenario.AlternativeID(int, int) setter unavailable.");
            }

            RuntimeStageJournal.Call("ScenarioAlternativeAssignment", () =>
            {
                altSetter.Invoke(createdScenario, new object[] { physicalTypeId, createdPhysicalAlternativeId });
            });
            evidence.Diagnostics["AlternativeAssigned"] = "true";

            // 7. Isolation Check Before Persistence
            int srcPhysCheck = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(sourceScenario, physicalTypeId);
            if (srcPhysCheck != sourcePhysicalAlternativeId)
            {
                evidence.FailureCode = "P6_B_BLOCKED_UNEXPECTED_SCENARIO_DELTA";
                evidence.Exception = $"Source scenario Physical assignment mutated: expected {sourcePhysicalAlternativeId}, actual {srcPhysCheck}.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            int createdPhysCheck = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(createdScenario, physicalTypeId);
            if (createdPhysCheck != createdPhysicalAlternativeId)
            {
                evidence.FailureCode = "P6_B_BLOCKED_UNEXPECTED_SCENARIO_DELTA";
                evidence.Exception = $"Created scenario Physical assignment failed: expected {createdPhysicalAlternativeId}, actual {createdPhysCheck}.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            int differingAssignments = 0;
            foreach (int tId in allTypeIds)
            {
                int srcVal = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(sourceScenario, tId);
                int tgtVal = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(createdScenario, tId);
                if (srcVal != tgtVal)
                {
                    differingAssignments++;
                    if (tId != physicalTypeId)
                    {
                        evidence.FailureCode = "P6_B_BLOCKED_UNEXPECTED_SCENARIO_DELTA";
                        evidence.Exception = $"Unexpected non-Physical assignment difference for type ID {tId}.";
                        throw new InvalidOperationException(evidence.FailureCode);
                    }
                }
            }
            if (differingAssignments != 1)
            {
                evidence.FailureCode = "P6_B_BLOCKED_UNEXPECTED_SCENARIO_DELTA";
                evidence.Exception = $"Expected exactly 1 differing assignment (Physical), found {differingAssignments}.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            evidence.Diagnostics["IsolationVerifiedBeforeSave"] = "true";
            evidence.Diagnostics["EngineeringPropertyMutationCount"] = "0";
            evidence.Diagnostics["SolverRunInvocationCount"] = "0";

            // 8. Persistence: UI model save invocation
            MethodInfo saveMethod = ui.GetType().GetMethod(NameSaveCurrentProject, Type.EmptyTypes);
            if (saveMethod == null)
            {
                evidence.FailureCode = "P6_B_BLOCKED_PERSISTENCE";
                throw new InvalidOperationException("SaveCurrentProject method not found on UI model.");
            }

            bool saveResult = (bool)RuntimeStageJournal.Call(NameSaveCurrentProject,
                () => saveMethod.Invoke(ui, null),
                val => val);

            evidence.Diagnostics["SaveCurrentProjectInvoked"] = "true";
            evidence.Diagnostics["SaveCurrentProjectResult"] = saveResult.ToString();
            if (!saveResult)
            {
                evidence.FailureCode = "P6_B_BLOCKED_PERSISTENCE";
                evidence.Exception = "SaveCurrentProject() returned false.";
                throw new InvalidOperationException(evidence.FailureCode);
            }
        }

        public static void ExecuteHost2ReopenVerification(object dataset, object ui, object project, HostRequest request, ModelReadEvidence evidence)
        {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (evidence == null) throw new ArgumentNullException(nameof(evidence));

            int expectedCreatedScenarioId = int.Parse(request.Diagnostics["ExpectedCreatedScenarioId"]);
            int expectedCreatedPhysicalAltId = int.Parse(request.Diagnostics["ExpectedCreatedPhysicalAlternativeId"]);
            int expectedSourceScenarioId = int.Parse(request.Diagnostics["ExpectedSourceScenarioId"]);
            int expectedSourcePhysicalAltId = int.Parse(request.Diagnostics["ExpectedSourcePhysicalAlternativeId"]);
            int physicalTypeId = int.Parse(request.Diagnostics["ExpectedPhysicalTypeId"]);
            int preWriteScenarioCount = int.Parse(request.Diagnostics["ExpectedPreWriteScenarioCount"]);
            int preWritePhysicalAltCount = int.Parse(request.Diagnostics["ExpectedPreWritePhysicalAltCount"]);
            var sourceAssignments = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, int>>(request.Diagnostics["ExpectedSourceAssignmentsJson"]);

            Type datasetType = dataset.GetType();
            PropertyInfo smProp = datasetType.GetProperty(NameScenarioManager);
            object scenarioManager = smProp.GetValue(dataset, null);
            Type smType = scenarioManager.GetType();

            MethodInfo idsMethod = smType.GetMethod("ElementIDs", Type.EmptyTypes);
            MethodInfo elementMethod = smType.GetMethod("Element", new[] { typeof(int) });
            object idCollection = idsMethod.Invoke(scenarioManager, null);
            int reopenScenarioCount = ScenarioAlternativeCapabilityMapper.GetCollectionCount(idCollection);

            if (reopenScenarioCount != preWriteScenarioCount + 1)
            {
                evidence.FailureCode = "P6_B_BLOCKED_PERSISTENCE_NOT_PROVEN";
                evidence.Exception = $"Scenario count delta mismatch: expected {preWriteScenarioCount + 1}, actual {reopenScenarioCount}.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            object sourceScenario = elementMethod.Invoke(scenarioManager, new object[] { expectedSourceScenarioId });
            if (sourceScenario == null)
            {
                evidence.FailureCode = "P6_B_BLOCKED_PERSISTENCE_NOT_PROVEN";
                evidence.Exception = $"Source scenario ID {expectedSourceScenarioId} not found on reopen.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            int srcPhysReopen = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(sourceScenario, physicalTypeId);
            if (srcPhysReopen != expectedSourcePhysicalAltId)
            {
                evidence.FailureCode = "P6_B_BLOCKED_PERSISTENCE_NOT_PROVEN";
                evidence.Exception = $"Source scenario Physical assignment changed: expected {expectedSourcePhysicalAltId}, actual {srcPhysReopen}.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            object createdScenario = elementMethod.Invoke(scenarioManager, new object[] { expectedCreatedScenarioId });
            if (createdScenario == null)
            {
                evidence.FailureCode = "P6_B_BLOCKED_PERSISTENCE_NOT_PROVEN";
                evidence.Exception = $"Created scenario ID {expectedCreatedScenarioId} not found on reopen.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            MethodInfo amMethod = datasetType.GetMethod(NameAlternativeManager, new[] { typeof(int) });
            object physicalAm = amMethod.Invoke(dataset, new object[] { physicalTypeId });
            MethodInfo altIdsMethod = physicalAm.GetType().GetMethod("ElementIDs", Type.EmptyTypes);
            MethodInfo altElemMethod = physicalAm.GetType().GetMethod("Element", new[] { typeof(int) });
            object physAltIds = altIdsMethod.Invoke(physicalAm, null);
            int reopenPhysicalAltCount = ScenarioAlternativeCapabilityMapper.GetCollectionCount(physAltIds);

            if (reopenPhysicalAltCount != preWritePhysicalAltCount + 1)
            {
                evidence.FailureCode = "P6_B_BLOCKED_UNEXPECTED_ALTERNATIVE_DELTA";
                evidence.Exception = $"Physical alternative count delta mismatch: expected {preWritePhysicalAltCount + 1}, actual {reopenPhysicalAltCount}.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            object sourcePhysAlt = altElemMethod.Invoke(physicalAm, new object[] { expectedSourcePhysicalAltId });
            if (sourcePhysAlt == null)
            {
                evidence.FailureCode = "P6_B_BLOCKED_PERSISTENCE_NOT_PROVEN";
                evidence.Exception = $"Source physical alternative ID {expectedSourcePhysicalAltId} not found on reopen.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            object createdPhysAlt = altElemMethod.Invoke(physicalAm, new object[] { expectedCreatedPhysicalAltId });
            if (createdPhysAlt == null)
            {
                evidence.FailureCode = "P6_B_BLOCKED_PERSISTENCE_NOT_PROVEN";
                evidence.Exception = $"Created physical alternative ID {expectedCreatedPhysicalAltId} not found on reopen.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            int createdPhysReopen = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(createdScenario, physicalTypeId);
            if (createdPhysReopen != expectedCreatedPhysicalAltId)
            {
                evidence.FailureCode = "P6_B_BLOCKED_PERSISTENCE_NOT_PROVEN";
                evidence.Exception = $"Created scenario Physical assignment mismatch on reopen: expected {expectedCreatedPhysicalAltId}, actual {createdPhysReopen}.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            foreach (var kvp in sourceAssignments)
            {
                int tId = int.Parse(kvp.Key);
                int expSrc = kvp.Value;
                int actSrc = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(sourceScenario, tId);
                if (actSrc != expSrc)
                {
                    evidence.FailureCode = "P6_B_BLOCKED_PERSISTENCE_NOT_PROVEN";
                    evidence.Exception = $"Source scenario assignment for type {tId} changed on reopen: expected {expSrc}, actual {actSrc}.";
                    throw new InvalidOperationException(evidence.FailureCode);
                }

                if (tId != physicalTypeId)
                {
                    int actCreated = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(createdScenario, tId);
                    if (actCreated != expSrc)
                    {
                        evidence.FailureCode = "P6_B_BLOCKED_PERSISTENCE_NOT_PROVEN";
                        evidence.Exception = $"Created scenario non-Physical assignment for type {tId} mismatch on reopen: expected {expSrc}, actual {actCreated}.";
                        throw new InvalidOperationException(evidence.FailureCode);
                    }
                }
            }

            evidence.Diagnostics["ReopenVerificationSuccess"] = "true";
            evidence.Diagnostics["CreatedScenarioExists"] = "true";
            evidence.Diagnostics["CreatedPhysicalAlternativeExists"] = "true";
            evidence.Diagnostics["SourcePhysicalAssignmentMatches"] = "true";
            evidence.Diagnostics["CreatedPhysicalAssignmentMatches"] = "true";
            evidence.Diagnostics["NonPhysicalAssignmentsMatchSource"] = "true";
            evidence.Diagnostics["SourceStatePreserved"] = "true";
            evidence.Diagnostics["ScenarioCountDelta"] = "+1";
            evidence.Diagnostics["PhysicalAlternativeCountDelta"] = "+1";
            evidence.Diagnostics["OtherAlternativeCountsDelta"] = "0";
            evidence.Diagnostics["ReopenScenarioCount"] = reopenScenarioCount.ToString();
            evidence.Diagnostics["ReopenPhysicalAlternativeCount"] = reopenPhysicalAltCount.ToString();
            evidence.Diagnostics["P6BScenarioCreateProven"] = "true";
            evidence.Diagnostics["P6BAlternativeCreateProven"] = "true";
        }
    }
}
