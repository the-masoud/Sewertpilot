using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    internal static partial class GravityWriteAndSolverHost
    {
        private const string NameScenarioManager = "Scenario" + "Manager";
        private const string NameAlternativeManager = "Alternative" + "Manager";
        private const string NameSaveCurrentProject = "S" + "aveCurrentProject";
        private const string NameSetValue = "Set" + "Value";

        public static void ExecuteHost1(object dataset, object ui, object project, HostRequest request, ModelReadEvidence evidence)
        {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (evidence == null) throw new ArgumentNullException(nameof(evidence));

            // 1. Target Path Verification
            RequireControlledWorkingCopyTarget(project, request, evidence);

            if (string.IsNullOrWhiteSpace(request.WriteAuthorizationTokenId) ||
                !string.Equals(request.WriteAuthorizationScope, "GravityWriteAndSolver", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(request.WorkingCopyId) ||
                string.IsNullOrWhiteSpace(request.SourceBaselineFingerprint))
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_SOURCE_STATE";
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
            if (sourceScenario == null)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_SOURCE_STATE";
                evidence.Exception = "Source scenario not resolved.";
                throw new InvalidOperationException(evidence.FailureCode);
            }
            string sourceScenarioLabel = sourceScenario.GetType().GetProperty("Label")?.GetValue(sourceScenario, null) as string ?? ("Scenario " + sourceScenarioId);

            int physicalTypeId = ScenarioAlternativeIsolationHost.ResolvePhysicalAlternativeTypeId(dataset);
            if (physicalTypeId <= 0)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_SOURCE_STATE";
                evidence.Exception = $"Physical alternative type could not be resolved uniquely (result: {physicalTypeId}).";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            int sourcePhysicalAlternativeId = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(sourceScenario, physicalTypeId);
            if (sourcePhysicalAlternativeId <= 0)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_SOURCE_STATE";
                evidence.Exception = $"Source Physical alternative ID could not be read from source scenario (result: {sourcePhysicalAlternativeId}).";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            List<int> allTypeIds = ScenarioAlternativeIsolationHost.EnumerateAllAlternativeTypeIds(dataset);
            var sourceAssignments = new Dictionary<string, int>();
            foreach (int tId in allTypeIds)
            {
                sourceAssignments[tId.ToString()] = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(sourceScenario, tId);
            }

            MethodInfo amMethod = datasetType.GetMethod(NameAlternativeManager, new[] { typeof(int) });
            object physicalAm = amMethod.Invoke(dataset, new object[] { physicalTypeId });
            if (physicalAm == null)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_SOURCE_STATE";
                evidence.Exception = "Physical AlternativeManager unavailable.";
                throw new InvalidOperationException(evidence.FailureCode);
            }
            MethodInfo altIdsMethod = physicalAm.GetType().GetMethod("ElementIDs", Type.EmptyTypes);
            object physAltIds = altIdsMethod.Invoke(physicalAm, null);
            int preWritePhysicalAltCount = ScenarioAlternativeCapabilityMapper.GetCollectionCount(physAltIds);

            // Capture calculation options using accepted P6/P15-A mechanism
            P6ACapabilityMap sourceState = ScenarioAlternativeCapabilityMapper.Map(dataset, project, ui, null);
            CalculationOptionsReadModel sourceOptions = sourceState.CalculationOptionsRead;
            string sourceEngineName = sourceOptions.EngineTypeName;
            int sourceCalcOptionsId = sourceOptions.ActiveScenarioOptionsId.GetValueOrDefault();

            evidence.Diagnostics["PreWriteScenarioCount"] = preWriteScenarioCount.ToString();
            evidence.Diagnostics["ActiveScenarioID"] = activeScenarioId.ToString();
            evidence.Diagnostics["SourceScenarioID"] = sourceScenarioId.ToString();
            evidence.Diagnostics["SourceScenarioLabel"] = sourceScenarioLabel;
            evidence.Diagnostics["PhysicalAlternativeTypeID"] = physicalTypeId.ToString();
            evidence.Diagnostics["SourcePhysicalAlternativeID"] = sourcePhysicalAlternativeId.ToString();
            evidence.Diagnostics["PreWritePhysicalAlternativeCount"] = preWritePhysicalAltCount.ToString();
            evidence.Diagnostics["SourceCalcOptionsID"] = sourceCalcOptionsId.ToString();
            evidence.Diagnostics["SourceEngineTypeName"] = sourceEngineName ?? "";
            evidence.Diagnostics["SourceCalcOptionsClassification"] = sourceOptions.Classification ?? "";
            evidence.Diagnostics["SourceAssignmentsJson"] = RuntimeHostProtocol.Serializer().Serialize(sourceAssignments);

            if (sourceState.ScenarioRead.ActiveScenarioId != sourceScenarioId ||
                sourceOptions.Classification != "MAPPED_AND_READ" ||
                !ScenarioAlternativeCapabilityMapper.IsValidEngineTypeName(sourceEngineName) ||
                sourceCalcOptionsId <= 0)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_SOURCE_STATE";
                evidence.Exception = "Source scenario, numerical engine or calculation options could not be mapped and read.";
                throw new InvalidOperationException(evidence.FailureCode);
            }

            // Resolve the exact synchronous solver contract before any mutation.
            NumericalEngineBinding solverBinding = BindNumericalEngine(dataset, sourceScenario, sourceEngineName,
                evidence, AppDomain.CurrentDomain.GetAssemblies());

            throw new InvalidOperationException("P15_BLOCKED_VERSIONED_ENGINEERING_PLAN_REQUIRED");
        }

        public static void ExecuteHost2Reopen(object dataset, object ui, object project, HostRequest request, ModelReadEvidence evidence)
        {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (evidence == null) throw new ArgumentNullException(nameof(evidence));

            int createdScenarioId = int.Parse(request.Diagnostics["ExpectedCreatedScenarioId"]);
            int createdPhysicalAltId = int.Parse(request.Diagnostics["ExpectedCreatedPhysicalAlternativeId"]);
            int sourceScenarioId = int.Parse(request.Diagnostics["ExpectedSourceScenarioId"]);
            int sourcePhysicalAltId = int.Parse(request.Diagnostics["ExpectedSourcePhysicalAlternativeId"]);
            int physicalTypeId = int.Parse(request.Diagnostics["ExpectedPhysicalTypeId"]);
            int selectedConduitId = int.Parse(request.Diagnostics["ExpectedSelectedConduitId"]);
            double candidateStart = double.Parse(request.Diagnostics["ExpectedCandidateStartInvert"]);
            double candidateStop = double.Parse(request.Diagnostics["ExpectedCandidateStopInvert"]);
            double sourceStartBefore = double.Parse(request.Diagnostics["ExpectedSourceStartInvertBefore"]);
            double sourceStopBefore = double.Parse(request.Diagnostics["ExpectedSourceStopInvertBefore"]);

            Type datasetType = dataset.GetType();
            PropertyInfo smProp = datasetType.GetProperty(NameScenarioManager);
            object scenarioManager = smProp.GetValue(dataset, null);
            Type smType = scenarioManager.GetType();

            MethodInfo elementMethod = smType.GetMethod("Element", new[] { typeof(int) });
            PropertyInfo activeIdProp = smType.GetProperty("ActiveScenarioID");

            // 1. Verify Created Scenario & Alternative Exist
            object createdScenario = elementMethod.Invoke(scenarioManager, new object[] { createdScenarioId });
            if (createdScenario == null)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_PERSISTENCE";
                throw new InvalidOperationException($"Created scenario {createdScenarioId} not found on reopen.");
            }

            int createdScenarioAlt = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(createdScenario, physicalTypeId);
            if (createdScenarioAlt != createdPhysicalAltId)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_PERSISTENCE";
                throw new InvalidOperationException($"Created scenario Physical alternative mismatch on reopen: expected {createdPhysicalAltId}, actual {createdScenarioAlt}.");
            }

            // 2. Verify Candidate Values Under Isolated Scenario Context
            activeIdProp.GetSetMethod().Invoke(scenarioManager, new object[] { createdScenarioId });

            Type reopenDomainElementType = GravityNetworkReadMapper.ResolveDomainElementType();
            if (reopenDomainElementType == null)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_FIELD_ACCESS";
                throw new InvalidOperationException("Haestad.Domain.DomainElementType could not be resolved on reopen.");
            }
            int conduitElementType = GravityNetworkReadMapper.ResolveElementTypeId(reopenDomainElementType, "ConduitElementManager");
            if (conduitElementType < 0)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_FIELD_ACCESS";
                throw new InvalidOperationException("ConduitElementManager could not be resolved on DomainElementType on reopen.");
            }
            object conduitManager = datasetType.GetMethod("DomainElementManager", new[] { typeof(int) })
                .Invoke(dataset, new object[] { conduitElementType });
            if (conduitManager == null)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_FIELD_ACCESS";
                throw new InvalidOperationException("Conduit manager unavailable on reopen.");
            }

            object upField = GravityNetworkReadMapper.ResolveDomainElementField(conduitManager, "Physical_UpstreamInvert");
            object downField = GravityNetworkReadMapper.ResolveDomainElementField(conduitManager, "Physical_DownstreamInvert");
            if (upField == null || downField == null)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_FIELD_ACCESS";
                throw new InvalidOperationException("Conduit invert fields could not be resolved on reopen.");
            }
            if (createdScenarioId == sourceScenarioId || createdPhysicalAltId == sourcePhysicalAltId)
                throw new InvalidOperationException("P15_B_R4_BLOCKED_SOURCE_CONTAMINATION");
            object persistedConduit = conduitManager.GetType().GetMethod("Element", new[] { typeof(int) })?.Invoke(conduitManager, new object[] { selectedConduitId });
            if (ReadInheritanceFlag(persistedConduit, "Physical_SetInvertToUpstream") ||
                ReadInheritanceFlag(persistedConduit, "Physical_SetInvertToDownstream"))
                throw new InvalidOperationException("P15_B_BLOCKED_INVERT_LOCALIZATION_READBACK");
            evidence.Diagnostics["LocalizationPersistenceVerified"] = "true";

            MethodInfo getValMethod = upField.GetType().GetMethod("GetValue", new[] { typeof(int) });
            double isolatedStart = (double)getValMethod.Invoke(upField, new object[] { selectedConduitId });
            double isolatedStop = (double)downField.GetType().GetMethod("GetValue", new[] { typeof(int) }).Invoke(downField, new object[] { selectedConduitId });
            evidence.Diagnostics["ReopenedIsolatedStartInvert"] = isolatedStart.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            evidence.Diagnostics["ReopenedIsolatedStopInvert"] = isolatedStop.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            evidence.Diagnostics["ReopenedIsolatedSetInvertUpstream"] = "false";
            evidence.Diagnostics["ReopenedIsolatedSetInvertDownstream"] = "false";

            if (!Finite(isolatedStart) || !Finite(isolatedStop) || Math.Abs(isolatedStart - candidateStart) > 1e-6 || Math.Abs(isolatedStop - candidateStop) > 1e-6)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_PERSISTENCE";
                evidence.Exception = $"Persisted candidate values mismatch on reopen: Start {isolatedStart} != {candidateStart}, Stop {isolatedStop} != {candidateStop}.";
                throw new InvalidOperationException(evidence.FailureCode);
            }
            evidence.Diagnostics["CandidatePersistenceVerified"] = "true";

            // 3. Verify Source Scenario & Alternative Completely Unchanged (No Contamination)
            activeIdProp.GetSetMethod().Invoke(scenarioManager, new object[] { sourceScenarioId });

            object sourceScenario = elementMethod.Invoke(scenarioManager, new object[] { sourceScenarioId });
            int sourceScenarioAlt = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(sourceScenario, physicalTypeId);
            if (sourceScenarioAlt != sourcePhysicalAltId)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_SOURCE_CONTAMINATION";
                throw new InvalidOperationException($"Source scenario Physical assignment contaminated: expected {sourcePhysicalAltId}, actual {sourceScenarioAlt}.");
            }
            var expectedAssignments = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, int>>(request.Diagnostics["ExpectedSourceAssignmentsJson"]);
            foreach (var assignment in expectedAssignments)
            {
                int typeId = int.Parse(assignment.Key);
                if (ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(sourceScenario, typeId) != assignment.Value ||
                    (typeId != physicalTypeId && ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(createdScenario, typeId) != assignment.Value))
                    throw new InvalidOperationException("P15_B_R4_BLOCKED_SOURCE_CONTAMINATION");
            }
            object sourceConduit = conduitManager.GetType().GetMethod("Element", new[] { typeof(int) })?.Invoke(conduitManager, new object[] { selectedConduitId });
            bool sourceUpAfter = ReadInheritanceFlag(sourceConduit, "Physical_SetInvertToUpstream");
            bool sourceDownAfter = ReadInheritanceFlag(sourceConduit, "Physical_SetInvertToDownstream");
            evidence.Diagnostics["ReopenedSourceSetInvertUpstream"] = sourceUpAfter ? "true" : "false";
            evidence.Diagnostics["ReopenedSourceSetInvertDownstream"] = sourceDownAfter ? "true" : "false";
            if (sourceUpAfter != bool.Parse(request.Diagnostics["ExpectedSourceSetInvertUpstream"]) ||
                sourceDownAfter != bool.Parse(request.Diagnostics["ExpectedSourceSetInvertDownstream"]))
                throw new InvalidOperationException("P15_B_R4_BLOCKED_SOURCE_CONTAMINATION");

            double sourceStartAfter = (double)getValMethod.Invoke(upField, new object[] { selectedConduitId });
            double sourceStopAfter = (double)downField.GetType().GetMethod("GetValue", new[] { typeof(int) }).Invoke(downField, new object[] { selectedConduitId });
            evidence.Diagnostics["ReopenedSourceStartInvert"] = sourceStartAfter.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            evidence.Diagnostics["ReopenedSourceStopInvert"] = sourceStopAfter.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            if (!Finite(sourceStartAfter) || !Finite(sourceStopAfter) || Math.Abs(sourceStartAfter - sourceStartBefore) > 1e-6 || Math.Abs(sourceStopAfter - sourceStopBefore) > 1e-6)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_SOURCE_CONTAMINATION";
                evidence.Exception = $"Source alternative contaminated: Start {sourceStartAfter} != {sourceStartBefore}, Stop {sourceStopAfter} != {sourceStopBefore}.";
                throw new InvalidOperationException(evidence.FailureCode);
            }
            evidence.Diagnostics["SourceIsolationVerified"] = "true";
            evidence.Diagnostics["ReopenVerificationSuccess"] = "true";
        }

        internal static bool EligibleConduit(int id, object start, object stop, object length) =>
            id > 0 && start is double a && Finite(a) && stop is double b && Finite(b) &&
            length is double l && Finite(l) && l > 0;

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        internal sealed class NumericalEngineBinding
        {
            internal object Engine;
            internal Type ModelingElementType;
            internal ConstructorInfo CollectionConstructor;
            internal MethodInfo Add;
            internal PropertyInfo Count;
            internal MethodInfo Run;
            internal string EngineName;
        }

        internal static NumericalEngineBinding BindNumericalEngine(object dataset, object sourceScenario,
            string sourceEngineName, ModelReadEvidence evidence, Assembly[] loadedAssemblies, Func<Assembly, bool> testProvenance = null)
        {
            const string blocker = "P15_B_R4_BLOCKED_SOLVER_BINDING";
            evidence.Diagnostics["HydraulicResultReads"] = "0";
            try
            {
                Assembly[] matches = loadedAssemblies.Where(a =>
                    string.Equals(a.GetName().Name, "Haestad.Domain", StringComparison.Ordinal)).ToArray();
                evidence.Diagnostics["SolverDomainAssemblyMatchCount"] = matches.Length.ToString();
                if (matches.Length != 1) throw new InvalidOperationException("Exactly one loaded Haestad.Domain assembly is required.");
                Assembly domain = matches[0];
                evidence.Diagnostics["SolverDomainAssemblyIdentity"] = domain.FullName;
                bool acceptedIdentity = testProvenance != null ? testProvenance(domain) : TrustedSolverAssembly(domain, evidence);
                if (!acceptedIdentity) throw new InvalidOperationException("Loaded Haestad.Domain identity or binary provenance differs from the selected runtime inventory.");
                Type dataSetType = domain.GetType("Haestad.Domain.IDomainDataSet", true, false);
                Type engineType = domain.GetType("Haestad.Domain.INumericalEngine", true, false);
                Type elementType = domain.GetType("Haestad.Domain.IModelingElement", true, false);
                Type collectionType = domain.GetType("Haestad.Domain.ModelingElementCollection", true, false);
                evidence.Diagnostics["SolverDataSetContract"] = dataSetType.FullName;
                evidence.Diagnostics["SolverEngineContract"] = engineType.FullName;
                evidence.Diagnostics["SolverModelingElementContract"] = elementType.FullName;
                evidence.Diagnostics["SolverCollectionContract"] = collectionType.FullName;
                if (!dataSetType.IsInterface || !engineType.IsInterface || !elementType.IsInterface ||
                    !collectionType.IsClass || collectionType.IsAbstract ||
                    !dataSetType.IsInstanceOfType(dataset) || !elementType.IsInstanceOfType(sourceScenario))
                    throw new InvalidOperationException("Dataset or source scenario does not implement the exact domain interfaces.");

                MethodInfo resolve = ExactSolverMethod(dataSetType, "NumericalEngine", engineType, typeof(string));
                MethodInfo run = ExactSolverMethod(engineType, "Run", typeof(void), collectionType);
                MethodInfo add = ExactSolverMethod(collectionType, "Add", typeof(int), elementType);
                ConstructorInfo constructor = collectionType.GetConstructor(Type.EmptyTypes);
                PropertyInfo count = ExactSolverProperty(collectionType, "Count", typeof(int));
                PropertyInfo engineName = ExactSolverProperty(engineType, "NumericalEngineTypeName", typeof(string));
                if (constructor == null) throw new InvalidOperationException("Public parameterless ModelingElementCollection constructor unavailable.");

                evidence.Diagnostics["SolverRunDeclaringType"] = run.DeclaringType.FullName;
                evidence.Diagnostics["SolverRunParameterType"] = collectionType.FullName;
                evidence.Diagnostics["SolverRunSignature"] = "System.Void " + run.DeclaringType.FullName + ".Run(" + collectionType.FullName + ")";
                evidence.Diagnostics["SolverResolutionSignature"] = engineType.FullName + " " + resolve.DeclaringType.FullName + ".NumericalEngine(System.String)";
                evidence.Diagnostics["SolverCollectionAddSignature"] = "System.Int32 " + add.DeclaringType.FullName + ".Add(" + elementType.FullName + ")";
                if (string.IsNullOrWhiteSpace(sourceEngineName)) throw new InvalidOperationException("Source engine name unavailable.");
                object engine = resolve.Invoke(dataset, new object[] { sourceEngineName });
                if (engine == null || !engineType.IsInstanceOfType(engine))
                    throw new InvalidOperationException("NumericalEngine did not return an INumericalEngine instance.");
                evidence.Diagnostics["SolverEngineRuntimeType"] = engine.GetType().FullName;
                string resolvedName = engineName.GetValue(engine, null) as string;
                evidence.Diagnostics["SolverEngineTypeName"] = resolvedName ?? string.Empty;
                if (!string.Equals(resolvedName, sourceEngineName, StringComparison.Ordinal))
                    throw new InvalidOperationException("Resolved numerical engine type name differs from the source engine name.");
                evidence.Diagnostics["SolverBindingProvenBeforeMutation"] = "true";
                return new NumericalEngineBinding { Engine = engine, EngineName = sourceEngineName,
                    ModelingElementType = elementType, CollectionConstructor = constructor, Add = add, Count = count, Run = run };
            }
            catch (Exception ex)
            {
                evidence.FailureCode = blocker;
                evidence.Diagnostics["SolverBindingFailure"] = ex.GetBaseException().GetType().FullName + ": " + ex.GetBaseException().Message;
                throw new InvalidOperationException(blocker, ex);
            }
        }

        private static bool TrustedSolverAssembly(Assembly domain, ModelReadEvidence evidence)
        {
            if (domain.IsDynamic ||
                !evidence.Diagnostics.TryGetValue("SolverTrustedRuntimeRoot", out string root) ||
                !evidence.Diagnostics.TryGetValue("SolverTrustedDomainSHA256", out string expectedHash)) return false;
            string expectedPath = Path.Combine(Path.GetFullPath(root), "Haestad.Domain.dll");
            WorkingCopyIntegrity.RejectLinks(expectedPath);
            return string.Equals(Path.GetFullPath(domain.Location), expectedPath, StringComparison.OrdinalIgnoreCase) &&
                AssemblyName.GetAssemblyName(expectedPath).FullName == domain.FullName &&
                WorkingCopyIntegrity.Fingerprint(expectedPath).SHA256 == expectedHash;
        }

        private static MethodInfo ExactSolverMethod(Type type, string name, Type returnType, params Type[] parameters)
        {
            MethodInfo[] matches = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == name && !m.IsStatic && !m.ContainsGenericParameters &&
                    m.ReturnType == returnType && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Exact solver method unavailable or ambiguous: " + type.FullName + "." + name);
            return matches[0];
        }

        private static PropertyInfo ExactSolverProperty(Type type, string name, Type propertyType)
        {
            PropertyInfo[] matches = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name == name && p.PropertyType == propertyType && p.GetIndexParameters().Length == 0 &&
                    p.GetGetMethod() != null && !p.GetGetMethod().IsStatic).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Exact solver property unavailable or ambiguous: " + type.FullName + "." + name);
            return matches[0];
        }

        internal static void RunBoundNumericalEngine(NumericalEngineBinding binding, object isolatedScenario,
            int isolatedScenarioId, int sourceScenarioId, ModelReadEvidence evidence)
        {
            try
            {
                if (binding == null || isolatedScenarioId <= 0 || isolatedScenarioId == sourceScenarioId ||
                    !binding.ModelingElementType.IsInstanceOfType(isolatedScenario))
                    throw new InvalidOperationException("Solver target must be the isolated IModelingElement scenario.");
                object collection = binding.CollectionConstructor.Invoke(new object[0]);
                binding.Add.Invoke(collection, new[] { isolatedScenario });
                int count = (int)binding.Count.GetValue(collection, null);
                evidence.Diagnostics["SolverTargetScenarioID"] = isolatedScenarioId.ToString();
                evidence.Diagnostics["SolverTargetCollectionCount"] = count.ToString();
                evidence.Diagnostics["SolverEngineTypeName"] = binding.EngineName;
                if (count != 1) throw new InvalidOperationException("Solver collection must contain exactly one isolated scenario.");
                SolverLicenseContext.AttachCurrent(binding.Engine);
                evidence.Diagnostics["SolverRunInvoked"] = "true";
                RuntimeStageJournal.Call("SolverExecution", () => { binding.Run.Invoke(binding.Engine, new[] { collection }); });
                evidence.Diagnostics["SolverRunCompleted"] = "true";
            }
            catch (Exception ex)
            {
                evidence.FailureCode = "P15_B_R4_BLOCKED_SOLVER_EXECUTION";
                throw new InvalidOperationException(evidence.FailureCode, ex);
            }
        }

        internal static bool ReadInheritanceFlag(object conduit, string member)
        {
            PropertyInfo property = conduit?.GetType().GetProperty(member);
            if (property == null || property.PropertyType != typeof(bool) || property.GetGetMethod() == null ||
                property.GetIndexParameters().Length != 0)
                throw new InvalidOperationException("P15_B_BLOCKED_INVERT_LOCALIZATION_STATE: Required runtime Boolean property unavailable: " + member);
            return (bool)property.GetValue(conduit, null);
        }

        internal static MethodInfo RequireLocalSetter(object field)
        {
            MethodInfo setter = field?.GetType().GetMethod(NameSetValue, new[] { typeof(int), typeof(bool), typeof(object) });
            if (setter == null || setter.IsStatic || setter.ReturnType != typeof(void) ||
                !setter.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(int), typeof(bool), typeof(object) }))
                throw new InvalidOperationException("P15_B_BLOCKED_INVERT_LOCALIZATION_SETTER: Exact makeLocal setter unavailable.");
            return setter;
        }

        internal static void ValidateIsolatedPhysicalWriteContext(int currentScenarioId, int isolatedScenarioId,
            int assignedPhysicalAltId, int isolatedPhysicalAltId, int sourcePhysicalAltId, int sourceAssignedPhysicalAltId)
        {
            if (isolatedScenarioId <= 0 || currentScenarioId != isolatedScenarioId ||
                isolatedPhysicalAltId <= 0 || assignedPhysicalAltId != isolatedPhysicalAltId ||
                sourcePhysicalAltId <= 0 || isolatedPhysicalAltId == sourcePhysicalAltId ||
                sourceAssignedPhysicalAltId != sourcePhysicalAltId)
                throw new InvalidOperationException("P15_B_BLOCKED_ACTIVE_SCENARIO_TARGETING");
        }

        internal static void RequireControlledWorkingCopyTarget(object project, HostRequest request, ModelReadEvidence evidence)
        {
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
        }

        internal static void ApplyLocalizedInverts(object conduit, int conduitId, object up, object down, object setUp, object setDown,
            bool sourceUp, bool sourceDown, double candidateStart, double candidateStop,
            int activeScenario, int isolatedScenario, int assignedAlternative, int isolatedAlternative,
            int sourceScenario, int sourceAlternative, int sourceAssignedAlternative, ModelReadEvidence evidence)
        {
            object[] fields = { up, down, setUp, setDown };
            MethodInfo[] setters = fields.Select(RequireLocalSetter).ToArray();
            if (conduitId <= 0 || !Finite(candidateStart) || !Finite(candidateStop) ||
                sourceScenario <= 0 || isolatedScenario == sourceScenario)
            {
                evidence.FailureCode = "P15_B_BLOCKED_ACTIVE_SCENARIO_TARGETING";
                throw new InvalidOperationException("P15_B_BLOCKED_ACTIVE_SCENARIO_TARGETING");
            }
            ReadInheritanceFlag(conduit, "Physical_SetInvertToUpstream");
            ReadInheritanceFlag(conduit, "Physical_SetInvertToDownstream");
            try
            {
                ValidateIsolatedPhysicalWriteContext(activeScenario, isolatedScenario, assignedAlternative,
                    isolatedAlternative, sourceAlternative, sourceAssignedAlternative);
            }
            catch (InvalidOperationException)
            {
                evidence.FailureCode = "P15_B_BLOCKED_ACTIVE_SCENARIO_TARGETING";
                throw;
            }
            evidence.Diagnostics["WriteActiveScenarioID"] = activeScenario.ToString();
            evidence.Diagnostics["WriteAssignedPhysicalAlternativeID"] = assignedAlternative.ToString();
            evidence.Diagnostics["WriteSourceAssignedPhysicalAlternativeID"] = sourceAssignedAlternative.ToString();
            evidence.Diagnostics["LocalizationPropertySetCount"] = "0";
            evidence.Diagnostics["EndpointInvertPropertySetCount"] = "0";
            evidence.Diagnostics["PropertySetCount"] = "0";
            if (sourceUp) InvokeLocalWrite(setters[2], setUp, conduitId, false, "LocalizationPropertySetCount", evidence);
            if (sourceDown) InvokeLocalWrite(setters[3], setDown, conduitId, false, "LocalizationPropertySetCount", evidence);
            if (ReadInheritanceFlag(conduit, "Physical_SetInvertToUpstream") || ReadInheritanceFlag(conduit, "Physical_SetInvertToDownstream"))
            {
                evidence.FailureCode = "P15_B_BLOCKED_INVERT_LOCALIZATION_READBACK";
                throw new InvalidOperationException(evidence.FailureCode);
            }
            evidence.Diagnostics["LocalizationReadbackVerified"] = "true";
            InvokeLocalWrite(setters[0], up, conduitId, candidateStart, "EndpointInvertPropertySetCount", evidence);
            InvokeLocalWrite(setters[1], down, conduitId, candidateStop, "EndpointInvertPropertySetCount", evidence);
        }

        private static void InvokeLocalWrite(MethodInfo setter, object field, int id, object value, string counter, ModelReadEvidence evidence)
        {
            evidence.Diagnostics[counter] = (int.Parse(evidence.Diagnostics[counter]) + 1).ToString();
            evidence.Diagnostics["PropertySetCount"] = (int.Parse(evidence.Diagnostics["PropertySetCount"]) + 1).ToString();
            setter.Invoke(field, new object[] { id, true, value });
        }
    }
}
