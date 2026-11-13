using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    // Production orchestration shares the accepted P5/P6 guards, P12 reader, P15 field setters and solver binding.
    internal static partial class GravityWriteAndSolverHost
    {
        internal const string ProductionOperation = "GravityEngineeringExecutionV1";
        internal const string ProductionPhase = "GravityEngineeringPhase";
        internal const string ProductionPhaseReopen = "REOPEN";
        internal const string ProductionPhaseReadback = "READBACK";
        private static void RequireProduction(bool value, string reason) => GravityExecutionPlanBuilder.Require(value, reason);
        private static object P(object target, string name) => target.GetType().GetProperty(name)?.GetValue(target, null)
            ?? throw new MissingMemberException(target.GetType().FullName, name);
        private static object M(object target, string name, Type[] parameters, params object[] args) =>
            (target.GetType().GetMethod(name, parameters) ?? throw new MissingMethodException(target.GetType().FullName, name)).Invoke(target, args);
        private static bool D(ModelReadEvidence evidence, string key) => evidence.Diagnostics.TryGetValue(key, out string value) && value == "true";
        private static int N(ModelReadEvidence evidence, string key) => evidence.Diagnostics.TryGetValue(key, out string value) && int.TryParse(value, out int n) ? n : 0;
        private static string InvertField(GravityPlannedEndpoint e) => e.End == ConduitEndKind.Start ? "Physical_UpstreamInvert" : "Physical_DownstreamInvert";
        private static string FlagField(GravityPlannedEndpoint e) => e.End == ConduitEndKind.Start ? "Physical_SetInvertToUpstream" : "Physical_SetInvertToDownstream";
        private static object ProductionConduits(object dataset)
        {
            Type type = GravityNetworkReadMapper.ResolveDomainElementType();
            RequireProduction(type != null, "CONDUIT_TYPE");
            int id = GravityNetworkReadMapper.ResolveElementTypeId(type, "ConduitElementManager");
            RequireProduction(id >= 0, "CONDUIT_TYPE");
            return M(dataset, "DomainElementManager", new[] { typeof(int) }, id);
        }
        internal static void ValidateProductionRequest(HostRequest request, SingleOperatorExecutionPermit singleOperatorPermit = null)
        {
            GravityExecutionAuthorization.Require(request, singleOperatorPermit);
            GravityExecutionPlanBuilder.ValidateEngineeringPlan(request.EngineeringPlan);
            RequireProduction(request.Operation == ProductionOperation && !request.EngineeringPlan.NoChanges &&
                request.WriteAuthorizationScope == ProductionOperation &&
                Guid.TryParseExact(request.WriteAuthorizationTokenId, "N", out _) &&
                Guid.TryParseExact(request.WorkingCopyId, "N", out _) &&
                Path.GetFileName(Path.GetDirectoryName(request.WorkingModelPath)) == request.WorkingCopyId &&
                string.Equals(Path.GetFullPath(request.WorkingModelPath), Path.GetFullPath(request.WriteAuthorizationTargetWorkingPath ?? ""), StringComparison.OrdinalIgnoreCase) &&
                request.SourceBaselineFingerprint == request.EngineeringPlan.Inputs.ModelFingerprint, "PRODUCTION_AUTHORIZATION");
            P15TechnicalCapabilityHost.VerifyBaseline(request);
            RequireProduction(ProtectedBaseline.Register(request.WorkingModelPath).Fingerprint.Value == request.SourceBaselineFingerprint, "FRESH_WORKING_COPY");
        }

        internal static void VerifyProductionNetwork(GravityExecutionPlan plan, NetworkReadResult actual, bool isolated)
        {
            RequireProduction(actual?.ScenarioContext != null, "RUNTIME_NETWORK");
            // Normalize only authorized changes. All other P12 physical, topology and node data must remain identical.
            var normalized = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(GravityExecutionPlanBuilder.Json(actual));
            RequireProduction(normalized.Conduits.Count == plan.Endpoints.Count / 2, "RUNTIME_NETWORK_COVERAGE");
            foreach (var endpoint in plan.Endpoints)
            {
                var conduit = normalized.Conduits.Single(c => c.Id == endpoint.ConduitId);
                bool start = endpoint.End == ConduitEndKind.Start;
                double? value = start ? conduit.StartInvert ?? conduit.UpstreamInvert : conduit.StopInvert ?? conduit.DownstreamInvert;
                bool? inherited = start ? conduit.SetInvertToStart : conduit.SetInvertToStop;
                double expected = isolated ? endpoint.Proposed : endpoint.Original;
                RequireProduction(value.HasValue && GravityExecutionPlanBuilder.Finite(value.Value) && Math.Abs(value.Value - expected) <= 1e-6 &&
                    inherited == (isolated && endpoint.Localize ? false : endpoint.OriginalInherited), "RUNTIME_ENDPOINT_STATE");
                if (isolated)
                {
                    if (start) { conduit.StartInvert = conduit.UpstreamInvert = endpoint.Original; conduit.SetInvertToStart = endpoint.OriginalInherited; }
                    else { conduit.StopInvert = conduit.DownstreamInvert = endpoint.Original; conduit.SetInvertToStop = endpoint.OriginalInherited; }
                }
            }
            if (isolated) normalized.ScenarioContext = plan.Inputs.SourceRead.ScenarioContext;
            var network = new GravityNetworkIngestionService().Ingest(normalized, plan.Inputs.ModelFingerprint, schemaVersion: 2);
            RequireProduction(network.NetworkFingerprint == plan.NetworkFingerprint, "RUNTIME_SOURCE_DRIFT");
        }

        // All endpoints are preflighted before the first write; budgets count attempted setters, including a throwing setter.
        internal static void ApplyProductionEndpoints(GravityExecutionPlan plan, Func<GravityPlannedEndpoint, double> read,
            Func<GravityPlannedEndpoint, bool> inherited, Action<GravityPlannedEndpoint> localize,
            Action<GravityPlannedEndpoint> write, Action guard, ModelReadEvidence evidence)
        {
            GravityExecutionPlanBuilder.ValidateEngineeringPlan(plan);
            RequireProduction(!plan.NoChanges && N(evidence, "PropertySetCount") == 0, "WRITE_BUDGET");
            guard();
            foreach (var endpoint in plan.Endpoints)
                RequireProduction(read(endpoint) == endpoint.Original && inherited(endpoint) == endpoint.OriginalInherited, "ISOLATED_ORIGINAL_STATE");
            evidence.Diagnostics["LocalizationPropertySetCount"] = "0";
            evidence.Diagnostics["EndpointInvertPropertySetCount"] = "0";
            evidence.Diagnostics["PropertySetCount"] = "0";
            RuntimeStageJournal.Record("WRITE_CONTEXT_VERIFIED", "EXIT", new { plan.PlanFingerprint });
            foreach (var endpoint in plan.Endpoints.Where(e => e.Localize))
            {
                guard();
                evidence.Diagnostics["LocalizationPropertySetCount"] = (N(evidence, "LocalizationPropertySetCount") + 1).ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics["PropertySetCount"] = (N(evidence, "PropertySetCount") + 1).ToString(CultureInfo.InvariantCulture);
                RuntimeStageJournal.Call("LocalizationSetter:" + FlagField(endpoint), () => localize(endpoint));
                RequireProduction(!inherited(endpoint), "LOCALIZATION_READBACK");
            }
            foreach (var endpoint in plan.Endpoints)
                RequireProduction(inherited(endpoint) == (endpoint.Localize ? false : endpoint.OriginalInherited), "LOCALIZATION_READBACK");
            evidence.Diagnostics["LocalizationReadbackVerified"] = "true";
            foreach (var endpoint in plan.Endpoints.Where(e => e.Changed))
            {
                guard();
                RequireProduction(!inherited(endpoint), "LOCAL_WRITE_FLAG");
                evidence.Diagnostics["EndpointInvertPropertySetCount"] = (N(evidence, "EndpointInvertPropertySetCount") + 1).ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics["PropertySetCount"] = (N(evidence, "PropertySetCount") + 1).ToString(CultureInfo.InvariantCulture);
                RuntimeStageJournal.Call("EndpointSetter:" + InvertField(endpoint), () => write(endpoint));
            }
            RequireProduction(N(evidence, "PropertySetCount") == plan.TotalFieldWriteCount &&
                N(evidence, "LocalizationPropertySetCount") == plan.LocalizationWriteCount &&
                N(evidence, "EndpointInvertPropertySetCount") == plan.EndpointWriteCount, "WRITE_BUDGET");
            RuntimeStageJournal.Record("WRITE_COMPLETED", "EXIT", new { plan.TotalFieldWriteCount, plan.EndpointWriteCount, plan.LocalizationWriteCount });
            int reads = 0;
            foreach (var endpoint in plan.Endpoints)
            {
                guard(); double value = read(endpoint); reads++;
                RequireProduction(GravityExecutionPlanBuilder.Finite(value) && Math.Abs(value - endpoint.Proposed) <= 1e-6, "IMMEDIATE_READBACK");
                RequireProduction(inherited(endpoint) == (endpoint.Localize ? false : endpoint.OriginalInherited), "IMMEDIATE_FLAGS");
            }
            evidence.Diagnostics["ImmediateEndpointReadCount"] = reads.ToString(CultureInfo.InvariantCulture);
            evidence.Diagnostics["ImmediateReadbackVerified"] = "true";
            RuntimeStageJournal.Record("READBACK_COMPLETED", "EXIT", new { reads });
        }

        internal static void ExecuteProduction(object dataset, object ui, object project, HostRequest request, ModelReadEvidence evidence)
        {
            GravityExecutionAuthorization.Require(request);
            var plan = request.EngineeringPlan;
            GravityExecutionPlanBuilder.ValidateEngineeringPlan(plan);
            RequireControlledWorkingCopyTarget(project, request, evidence);
            RuntimeStageJournal.Record("OPEN", "EXIT", new { request.WorkingCopyId });
            string phase = request.Diagnostics.TryGetValue(ProductionPhase, out string p) ? p : null;
            if (string.Equals(phase, ProductionPhaseReadback, StringComparison.Ordinal))
            {
                ExecuteProductionReadback(dataset, ui, project, request, evidence);
                return;
            }
            bool reopen = string.Equals(phase, ProductionPhaseReopen, StringComparison.Ordinal);
            object sm = P(dataset, NameScenarioManager);
            PropertyInfo active = sm.GetType().GetProperty("ActiveScenarioID");
            RequireProduction(active?.GetSetMethod() != null, "ACTIVE_SCENARIO");
            object source = M(sm, "Element", new[] { typeof(int) }, plan.SourceScenarioId);
            int physicalType = ScenarioAlternativeIsolationHost.ResolvePhysicalAlternativeTypeId(dataset);
            RequireProduction(source != null && physicalType > 0 &&
                ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(source, physicalType) == plan.SourcePhysicalAlternativeId, "SOURCE_PHYSICAL");
            if (!reopen) RequireProduction((int)active.GetValue(sm, null) == plan.SourceScenarioId, "SOURCE_SCENARIO");
            active.GetSetMethod().Invoke(sm, new object[] { plan.SourceScenarioId });
            VerifyProductionNetwork(plan, GravityNetworkReadMapper.Map(dataset, project, ui, null), false);
            var assignments = ScenarioAlternativeIsolationHost.EnumerateAllAlternativeTypeIds(dataset).ToDictionary(
                id => id.ToString(CultureInfo.InvariantCulture), id => ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(source, id));
            RequireProduction(assignments.Values.All(id => id > 0), "SOURCE_ASSIGNMENTS");
            object manager = ProductionConduits(dataset);
            var fields = new Dictionary<ConduitEndKind, object>();
            var flags = new Dictionary<ConduitEndKind, object>();
            foreach (var end in new[] { ConduitEndKind.Start, ConduitEndKind.Stop })
            {
                var endpoint = plan.Endpoints.First(e => e.End == end);
                fields[end] = GravityNetworkReadMapper.ResolveDomainElementField(manager, InvertField(endpoint));
                flags[end] = GravityNetworkReadMapper.ResolveDomainElementField(manager, FlagField(endpoint));
                P15TechnicalCapabilityHost.DoubleSetter(fields[end]);
                P15TechnicalCapabilityHost.RequireFeet(GravityNetworkReadMapper.InspectFieldUnit(manager, InvertField(endpoint),
                    new Dictionary<string, FieldUnitMetadataDto>(), new List<MemberEvidence>()));
                if (plan.Endpoints.Any(e => e.End == end && e.Localize)) RequireLocalSetter(flags[end]);
            }
            object isolated; int isolatedId, isolatedAlt;
            if (!reopen)
            {
                // Prove calculation contracts before copies; licensing itself stays deferred to the guarded run.
                if (plan.Calculate) ProductionSolverBinding(dataset, source, project, ui, evidence);
                evidence.Diagnostics["SourceAssignmentsJson"] = GravityExecutionPlanBuilder.Json(assignments);
                RuntimeStageJournal.Record("SOURCE_RESOLVED", "EXIT", new { plan.SourceScenarioId, plan.SourcePhysicalAlternativeId, plan.NetworkFingerprint });
                evidence.Diagnostics["ScenarioCopyInvoked"] = "true";
                isolatedId = (int)RuntimeStageJournal.Call("ScenarioManager.Copy", () => M(sm, "Copy", new[] { typeof(int) }, plan.SourceScenarioId));
                isolated = M(sm, "Element", new[] { typeof(int) }, isolatedId);
                object am = M(dataset, NameAlternativeManager, new[] { typeof(int) }, physicalType);
                evidence.Diagnostics["AlternativeCopyInvoked"] = "true";
                isolatedAlt = (int)RuntimeStageJournal.Call("PhysicalAlternativeManager.Copy", () => M(am, "Copy", new[] { typeof(int) }, plan.SourcePhysicalAlternativeId));
                RequireProduction(isolated != null && isolatedId > 0 && isolatedId != plan.SourceScenarioId &&
                    isolatedAlt > 0 && isolatedAlt != plan.SourcePhysicalAlternativeId, "ISOLATION");
                M(isolated, "AlternativeID", new[] { typeof(int), typeof(int) }, physicalType, isolatedAlt);
                evidence.Diagnostics["CreatedScenarioID"] = isolatedId.ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics["CreatedPhysicalAlternativeID"] = isolatedAlt.ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics["ExpectedCreatedScenarioId"] = isolatedId.ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics["ExpectedCreatedPhysicalAlternativeId"] = isolatedAlt.ToString(CultureInfo.InvariantCulture);
                RuntimeStageJournal.Record("ISOLATION_CREATED", "EXIT", new { isolatedId, isolatedAlt });
            }
            else
            {
                isolatedId = int.Parse(request.Diagnostics["ExpectedCreatedScenarioId"], CultureInfo.InvariantCulture);
                isolatedAlt = int.Parse(request.Diagnostics["ExpectedCreatedPhysicalAlternativeId"], CultureInfo.InvariantCulture);
                evidence.Diagnostics["CreatedScenarioID"] = isolatedId.ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics["CreatedPhysicalAlternativeID"] = isolatedAlt.ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics["ExpectedCreatedScenarioId"] = isolatedId.ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics["ExpectedCreatedPhysicalAlternativeId"] = isolatedAlt.ToString(CultureInfo.InvariantCulture);
                isolated = M(sm, "Element", new[] { typeof(int) }, isolatedId);
                var expectedAssignments = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, int>>(request.Diagnostics["ExpectedSourceAssignmentsJson"]);
                RequireProduction(expectedAssignments.Count == assignments.Count && expectedAssignments.All(expectedAssignment =>
                    assignments.TryGetValue(expectedAssignment.Key, out int value) && value == expectedAssignment.Value), "SOURCE_ASSIGNMENT_DRIFT");
            }
            VerifyProductionAssignments(assignments.ToDictionary(assignment => int.Parse(assignment.Key, CultureInfo.InvariantCulture), assignment => assignment.Value),
                id => ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(source, id),
                id => ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(isolated, id), physicalType,
                plan.SourcePhysicalAlternativeId, isolatedAlt);
            active.GetSetMethod().Invoke(sm, new object[] { isolatedId });
            Action guard = () => {
                RequireControlledWorkingCopyTarget(project, request, evidence);
                ValidateIsolatedPhysicalWriteContext((int)active.GetValue(sm, null), isolatedId,
                    ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(isolated, physicalType), isolatedAlt,
                    plan.SourcePhysicalAlternativeId, ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(source, physicalType));
            };
            guard();
            if (!reopen)
            {
                ApplyProductionEndpoints(plan,
                    e => (double)M(fields[e.End], "GetValue", new[] { typeof(int) }, e.ConduitId),
                    e => ReadInheritanceFlag(M(manager, "Element", new[] { typeof(int) }, e.ConduitId), FlagField(e)),
                    e => RequireLocalSetter(flags[e.End]).Invoke(flags[e.End], new object[] { e.ConduitId, true, false }),
                    e => P15TechnicalCapabilityHost.DoubleSetter(fields[e.End]).Invoke(fields[e.End], new object[] { e.ConduitId, true, e.Proposed }), guard, evidence);
                guard(); evidence.Diagnostics["SaveCurrentProjectInvoked"] = "true";
                object saved = RuntimeStageJournal.Call(NameSaveCurrentProject, () => M(ui, NameSaveCurrentProject, Type.EmptyTypes));
                RequireProduction(saved is bool ok && ok, "PERSISTENCE_RESULT_FALSE");
                evidence.Diagnostics["SaveCurrentProjectSucceeded"] = "true";
                RuntimeStageJournal.Record("SAVE_COMPLETED", "EXIT", new { saves = 1 });
                return;
            }
            VerifyProductionNetwork(plan, GravityNetworkReadMapper.Map(dataset, project, ui, null), true);
            evidence.Diagnostics["SourceIsolationVerified"] = "true";
            evidence.Diagnostics["ReopenVerificationSuccess"] = "true";
            RuntimeStageJournal.Record("REOPEN_VERIFIED", "EXIT", new { plan.PlanFingerprint, endpoints = plan.Endpoints.Count });
            if (plan.Calculate)
            {
                guard(); var binding = ProductionSolverBinding(dataset, isolated, project, ui, evidence);
                evidence.Diagnostics["BoundNumericalEngine"] = binding.EngineName;
                RequireProduction(evidence.Diagnostics["ProductionOptionsIdentity"] == request.Diagnostics["ExpectedProductionOptionsIdentity"], "CALCULATION_OPTIONS_DRIFT");
                RuntimeStageJournal.Record("SOLVER_STARTED", "ENTER", new { isolatedId, binding.EngineName });
                evidence.Diagnostics["SolverStartTimestamp"] = DateTime.UtcNow.ToString("o");
                RunBoundNumericalEngine(binding, isolated, isolatedId, plan.SourceScenarioId, evidence);
                evidence.Diagnostics["SolverEndTimestamp"] = DateTime.UtcNow.ToString("o");
                string status = M(project, "GetScenarioComputeStatus", new[] { typeof(int) }, isolatedId)?.ToString();
                string text = M(project, "ScenarioStatusText", new[] { typeof(int) }, isolatedId) as string;
                evidence.Diagnostics["ScenarioComputeStatus"] = status ?? "Unknown";
                evidence.Diagnostics["ScenarioStatusText"] = text ?? "";
                evidence.Diagnostics["SolverStatusSuccess"] = status == "Success" ? "true" : "false";
                RuntimeStageJournal.Record("SOLVER_COMPLETED", "EXIT", new { status, statusText = text });
                RequireProduction(status == "Success", "SOLVER_STATUS");

                guard(); evidence.Diagnostics["PostSolveSaveCurrentProjectInvoked"] = "true";
                object saved = RuntimeStageJournal.Call("PostSolveSaveCurrentProject", () => M(ui, NameSaveCurrentProject, Type.EmptyTypes));
                RequireProduction(saved is bool ok && ok, "POST_SOLVE_PERSISTENCE_RESULT_FALSE");
                evidence.Diagnostics["PostSolveSaveCurrentProjectSucceeded"] = "true";
                evidence.Diagnostics["PostSolveSaveTimestamp"] = DateTime.UtcNow.ToString("o");
                evidence.Diagnostics["SaveTimestamp"] = evidence.Diagnostics["PostSolveSaveTimestamp"];
                RuntimeStageJournal.Record("POST_SOLVE_SAVE_COMPLETED", "EXIT", new { saves = 1 });
            }
            else
            {
                evidence.Diagnostics["HydraulicResultReads"] = "0";
            }
            evidence.Diagnostics["ProductionExecutionVerified"] = "true";
        }

        private static NumericalEngineBinding ProductionSolverBinding(object dataset, object scenario, object project, object ui, ModelReadEvidence evidence)
        {
            var map = ScenarioAlternativeCapabilityMapper.Map(dataset, project, ui, null);
            var options = map.CalculationOptionsRead;
            RequireProduction(options.Classification == "MAPPED_AND_READ" && options.ActiveScenarioOptionsId.GetValueOrDefault() > 0 &&
                ScenarioAlternativeCapabilityMapper.IsValidEngineTypeName(options.EngineTypeName), "CALCULATION_OPTIONS");
            evidence.Diagnostics["ProductionOptionsIdentity"] = options.EngineTypeName + ":" + options.ActiveScenarioOptionsId;
            return BindNumericalEngine(dataset, scenario, options.EngineTypeName, evidence, AppDomain.CurrentDomain.GetAssemblies());
        }

        internal static void PrepareProductionReopen(HostRequest request, ModelReadEvidence written)
        {
            RequireProduction(written.FailureCode == null && written.CloseCompleted && D(written, "StaCompleted") &&
                D(written, "SaveCurrentProjectSucceeded"), "FIRST_SESSION_CLEANUP");
            RequireProduction(N(written, "PropertySetCount") == request.EngineeringPlan.TotalFieldWriteCount &&
                N(written, "LocalizationPropertySetCount") == request.EngineeringPlan.LocalizationWriteCount &&
                N(written, "EndpointInvertPropertySetCount") == request.EngineeringPlan.EndpointWriteCount, "WRITE_BUDGET");
            request.Diagnostics["ExpectedCreatedScenarioId"] = written.Diagnostics["CreatedScenarioID"];
            request.Diagnostics["ExpectedCreatedPhysicalAlternativeId"] = written.Diagnostics["CreatedPhysicalAlternativeID"];
            request.Diagnostics["ExpectedSourceAssignmentsJson"] = written.Diagnostics["SourceAssignmentsJson"];
            if (request.EngineeringPlan.Calculate) request.Diagnostics["ExpectedProductionOptionsIdentity"] = written.Diagnostics["ProductionOptionsIdentity"];
            request.Diagnostics[ProductionPhase] = ProductionPhaseReopen;
        }

        internal static void PrepareProductionReadback(HostRequest request, ModelReadEvidence solved)
        {
            RequireProduction(request.EngineeringPlan.Calculate, "CALCULATING_PLAN_REQUIRED");
            RequireProduction(solved.FailureCode == null && solved.CloseCompleted && D(solved, "StaCompleted") &&
                D(solved, "ReopenVerificationSuccess") && D(solved, "SourceIsolationVerified") &&
                D(solved, "SolverRunInvoked") && D(solved, "SolverRunCompleted") && D(solved, "SolverStatusSuccess") &&
                D(solved, "PostSolveSaveCurrentProjectSucceeded"), "SECOND_SESSION_CLEANUP");

            request.Diagnostics[ProductionPhase] = ProductionPhaseReadback;
            request.Diagnostics["ExpectedCreatedScenarioId"] = solved.Diagnostics["CreatedScenarioID"];
            request.Diagnostics["ExpectedCreatedPhysicalAlternativeId"] = solved.Diagnostics["CreatedPhysicalAlternativeID"];
            request.Diagnostics["ExpectedProductionOptionsIdentity"] = solved.Diagnostics["ProductionOptionsIdentity"];
            request.Diagnostics["ExpectedNumericalEngine"] = solved.Diagnostics.TryGetValue("BoundNumericalEngine", out string eng) ? eng : "CivilStormDynamicEngine";
        }

        internal static void ExecuteProductionReadback(object dataset, object ui, object project, HostRequest request, ModelReadEvidence evidence)
        {
            var plan = request.EngineeringPlan;
            RequireProduction(plan.Calculate, "CALCULATING_PLAN_REQUIRED");
            RequireControlledWorkingCopyTarget(project, request, evidence);
            RuntimeStageJournal.Record("READBACK_OPEN", "EXIT", new { request.WorkingCopyId });

            int isolatedId = int.Parse(request.Diagnostics["ExpectedCreatedScenarioId"], CultureInfo.InvariantCulture);
            int isolatedAlt = int.Parse(request.Diagnostics["ExpectedCreatedPhysicalAlternativeId"], CultureInfo.InvariantCulture);

            object sm = P(dataset, NameScenarioManager);
            object isolated = M(sm, "Element", new[] { typeof(int) }, isolatedId);
            RequireProduction(isolated != null, "ISOLATED_SCENARIO_NOT_FOUND");

            int physicalType = ScenarioAlternativeIsolationHost.ResolvePhysicalAlternativeTypeId(dataset);
            int actualAlt = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(isolated, physicalType);
            RequireProduction(actualAlt == isolatedAlt, "ISOLATED_PHYSICAL_MISMATCH");

            var readResult = GravityNetworkReadMapper.Map(dataset, project, ui, null);
            VerifyProductionNetwork(plan, readResult, true);

            var conduitIds = readResult.Conduits.Select(c => c.Id).OrderBy(id => id).ToList();
            RequireProduction(conduitIds.Count > 0, "NO_CONDUITS_FOR_READBACK");

            string engine = request.Diagnostics.TryGetValue("ExpectedNumericalEngine", out string eng) && !string.IsNullOrEmpty(eng)
                ? eng
                : WaterObjectsResultReader.EngineName;

            var observations = WaterObjectsResultReader.ReadNetworkObservations(dataset, project, isolatedId, engine, conduitIds, readResult.Structures);

            double? firstTime = null;
            foreach (var obs in observations)
            {
                if (obs.TimeSeconds.HasValue)
                {
                    if (!firstTime.HasValue) firstTime = obs.TimeSeconds.Value;
                    else if (Math.Abs(obs.TimeSeconds.Value - firstTime.Value) > 1e-6)
                        throw new InvalidOperationException("TIMESTEP_MISMATCH: Observation time " + obs.TimeSeconds.Value + " does not match first observation time " + firstTime.Value);
                }
            }

            var context = new ResultCompositionContext
            {
                ProjectId = request.WorkingCopyId,
                ModelFingerprint = plan.NetworkFingerprint,
                ScenarioId = isolatedId,
                PhysicalAlternativeId = isolatedAlt,
                CalculationRunId = request.RunId,
                CalculationContext = "Production post-solve result readback",
                ResultTimeSeconds = firstTime ?? 0.0,
                IsTestOnly = false
            };

            var resultSet = NetworkResultComposer.Compose(context, readResult, observations);
            ValidateResultCoverage(readResult, conduitIds, resultSet);

            string setJson = RuntimeHostProtocol.Serializer().Serialize(resultSet);
            RequireProduction(setJson.Length <= 512 * 1024, "RESULT_SET_PAYLOAD_LIMIT_EXCEEDED");

            string dir = Path.GetDirectoryName(request.WorkingModelPath);
            var filesList = new List<string>();
            foreach (string file in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".fld" || ext == ".hyg" || ext == ".out" || ext == ".txt" || ext == ".sqlite" || ext == ".stsw")
                {
                    var fi = new FileInfo(file);
                    string hash = WorkingCopyIntegrity.Fingerprint(file).SHA256;
                    filesList.Add(Path.GetFileName(file) + "|" + fi.Length + "|" + hash);
                }
            }

            evidence.Diagnostics["ResultFilesInventoryJson"] = RuntimeHostProtocol.Serializer().Serialize(filesList);
            evidence.Diagnostics["HydraulicResultSetJson"] = setJson;
            string fingerprint = HydraulicResultSet.ComputeFingerprint(resultSet);
            evidence.Diagnostics["HydraulicResultSetFingerprint"] = fingerprint;
            evidence.Diagnostics["HydraulicResultReads"] = resultSet.Elements.Count.ToString(CultureInfo.InvariantCulture);
            evidence.Diagnostics["ProductionReadbackVerified"] = "true";
            evidence.Diagnostics["ProductionExecutionVerified"] = "true";
            evidence.Diagnostics["CreatedScenarioID"] = isolatedId.ToString(CultureInfo.InvariantCulture);
            evidence.Diagnostics["CreatedPhysicalAlternativeID"] = isolatedAlt.ToString(CultureInfo.InvariantCulture);
            evidence.Diagnostics["ExpectedCreatedScenarioId"] = isolatedId.ToString(CultureInfo.InvariantCulture);
            evidence.Diagnostics["ExpectedCreatedPhysicalAlternativeId"] = isolatedAlt.ToString(CultureInfo.InvariantCulture);
            evidence.Diagnostics["ExpectedNumericalEngine"] = engine;
            RuntimeStageJournal.Record("READBACK_COMPLETED", "EXIT", new { reads = resultSet.Elements.Count, fingerprint });
        }

        internal static void ValidateResultCoverage(NetworkReadResult readResult, IEnumerable<int> requestedConduitIds, HydraulicResultSet resultSet)
        {
            RequireProduction(readResult != null, "RESULT_COMPOSITION_FAILED");
            RequireProduction(readResult.Conduits != null, "RESULT_COMPOSITION_FAILED");
            RequireProduction(readResult.Structures != null, "RESULT_COMPOSITION_FAILED");
            RequireProduction(requestedConduitIds != null, "RESULT_COMPOSITION_FAILED");
            RequireProduction(resultSet != null, "RESULT_COMPOSITION_FAILED");
            RequireProduction(resultSet.Elements != null, "RESULT_COMPOSITION_FAILED");

            var expectedIds = new HashSet<int>();
            foreach (var conduit in readResult.Conduits)
            {
                RequireProduction(conduit != null && conduit.Id > 0, "RESULT_COMPOSITION_FAILED");
                RequireProduction(expectedIds.Add(conduit.Id), "RESULT_COMPOSITION_FAILED");
            }

            foreach (var structure in readResult.Structures)
            {
                RequireProduction(structure != null && structure.Id > 0, "RESULT_COMPOSITION_FAILED");
                RequireProduction(expectedIds.Add(structure.Id), "RESULT_COMPOSITION_FAILED");
            }

            RequireProduction(expectedIds.Count > 0, "RESULT_COMPOSITION_FAILED");

            var actualIds = new HashSet<int>();
            foreach (var element in resultSet.Elements)
            {
                RequireProduction(element != null && element.ElementId > 0, "RESULT_COMPOSITION_FAILED");
                RequireProduction(actualIds.Add(element.ElementId), "RESULT_COMPOSITION_FAILED");
            }

            RequireProduction(resultSet.Elements.Count == actualIds.Count, "RESULT_COMPOSITION_FAILED");
            RequireProduction(actualIds.SetEquals(expectedIds), "RESULT_COMPOSITION_FAILED");

            var requestedIds = new HashSet<int>();
            foreach (int conduitId in requestedConduitIds)
            {
                RequireProduction(conduitId > 0, "RESULT_COMPOSITION_FAILED");
                RequireProduction(requestedIds.Add(conduitId), "RESULT_COMPOSITION_FAILED");
                RequireProduction(actualIds.Contains(conduitId), "RESULT_COMPOSITION_FAILED");
            }
            RequireProduction(requestedIds.Count > 0, "RESULT_COMPOSITION_FAILED");
        }

        internal static void VerifyProductionAssignments(IDictionary<int, int> original, Func<int, int> source,
            Func<int, int> isolated, int physicalType, int sourceAlternative, int isolatedAlternative)
        {
            RequireProduction(sourceAlternative > 0 && isolatedAlternative > 0 && isolatedAlternative != sourceAlternative &&
                original.TryGetValue(physicalType, out int physical) && physical == sourceAlternative &&
                original.All(p => p.Value > 0 && source(p.Key) == p.Value &&
                    isolated(p.Key) == (p.Key == physicalType ? isolatedAlternative : p.Value)), "ISOLATED_ASSIGNMENTS");
        }

        internal static bool ProductionSuccess(HostRequest request, ModelReadEvidence evidence)
        {
            var plan = request.EngineeringPlan;
            bool baseOk = evidence.FailureCode == null && evidence.CloseCompleted && D(evidence, "StaCompleted") &&
                D(evidence, "FirstSessionCloseCompleted") && D(evidence, "ProductionExecutionVerified") &&
                D(evidence, "ImmediateReadbackVerified") && D(evidence, "SaveCurrentProjectSucceeded") &&
                D(evidence, "ReopenVerificationSuccess") && D(evidence, "SourceIsolationVerified") &&
                N(evidence, "PropertySetCount") == plan.TotalFieldWriteCount &&
                N(evidence, "LocalizationPropertySetCount") == plan.LocalizationWriteCount &&
                N(evidence, "EndpointInvertPropertySetCount") == plan.EndpointWriteCount &&
                D(evidence, "ScenarioCopyInvoked") && D(evidence, "AlternativeCopyInvoked");

            if (!baseOk) return false;

            if (plan.Calculate)
            {
                bool solverOk = D(evidence, "SolverRunInvoked") && D(evidence, "SolverRunCompleted") && D(evidence, "SolverStatusSuccess") &&
                    D(evidence, "SolverLicenseAttached") && D(evidence, "LicenseCleanupCompleted") &&
                    evidence.Diagnostics.TryGetValue("ScenarioComputeStatus", out string status) && status == "Success" &&
                    D(evidence, "PostSolveSaveCurrentProjectSucceeded");

                if (!solverOk) return false;

                bool readbackOk = D(evidence, "ProductionReadbackVerified") && D(evidence, "SecondSessionCloseCompleted") &&
                    evidence.Diagnostics.TryGetValue("HydraulicResultReads", out string reads) &&
                    int.TryParse(reads, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && count > 0 &&
                    evidence.Diagnostics.ContainsKey("HydraulicResultSetFingerprint") &&
                    evidence.Diagnostics.ContainsKey("HydraulicResultSetJson");

                return readbackOk;
            }
            else
            {
                return !D(evidence, "SolverRunInvoked") &&
                    !D(evidence, "ProductionReadbackVerified") &&
                    evidence.Diagnostics.TryGetValue("HydraulicResultReads", out string reads) && reads == "0";
            }
        }

        internal static void ValidateProductionResult(HostRequest request, HostResult result)
        {
            GravityExecutionPlanBuilder.ValidateEngineeringPlan(request.EngineeringPlan);
            var plan = request.EngineeringPlan;
            int expectedSaves = (plan.Calculate && result.Diagnostics.TryGetValue("PostSolveSaveCurrentProjectInvoked", out string pss) && pss == "true") ? 2 : 1;
            RequireProduction(result.SaveInvocationCount == expectedSaves && result.SaveAsInvocationCount == 0 &&
                result.ScenarioCreateInvocationCount == 1 && result.AlternativeCreateInvocationCount == 1 &&
                result.MutationInvocationCount == plan.TotalFieldWriteCount &&
                result.SolverRunInvocationCount == (plan.Calculate ? 1 : 0), "RESULT_WRITE_BUDGET");
            RequireProduction(ProductionSuccess(request, new ModelReadEvidence { Diagnostics = result.Diagnostics,
                FailureCode = result.FailureClassification, CloseCompleted = result.VendorCleanupCompleted && result.StaCompleted }), "RESULT_PRODUCTION_ACCEPTANCE");
        }
    }
}
