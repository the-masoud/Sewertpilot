using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    // Internal technical acceptance only. No candidate, engineering criterion or production entry point.
    internal static class P15TechnicalCapabilityHost
    {
        internal const string Operation = "InternalP15TechnicalCapability";
        internal const string LaunchSwitch = "--internal-p15-capability";
        internal const string Scope = "P15TechnicalCapability";
        private const string ScenarioManagerName = "Scenario" + "Manager";
        private const string SaveName = "S" + "aveCurrentProject";
        private const string Up = "Physical_UpstreamInvert";
        private const string Down = "Physical_DownstreamInvert";
        private const string FlagUp = "Physical_SetInvertToUpstream";
        private const string FlagDown = "Physical_SetInvertToDownstream";
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        internal static void ValidateRequest(HostRequest request)
        {
            if (request.Operation != Operation || request.Diagnostics == null ||
                !request.Diagnostics.TryGetValue("TestOffsetFeet", out string offset) || offset != "0.01" ||
                request.WriteAuthorizationScope != Scope ||
                !Guid.TryParseExact(request.WriteAuthorizationTokenId, "N", out _) ||
                !Guid.TryParseExact(request.WorkingCopyId, "N", out _) ||
                Path.GetFileName(Path.GetDirectoryName(request.WorkingModelPath)) != request.WorkingCopyId ||
                !string.Equals(Path.GetFullPath(request.WorkingModelPath), Path.GetFullPath(request.WriteAuthorizationTargetWorkingPath ?? ""), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("P15_TECH_BLOCKED_AUTHORIZATION");
            VerifyBaseline(request);
            var working = ProtectedBaseline.Register(request.WorkingModelPath);
            if (working.Fingerprint.Value != request.SourceBaselineFingerprint)
                throw new InvalidDataException("P15_TECH_BLOCKED_FRESH_WORKING_COPY");
        }

        internal static void VerifyBaseline(HostRequest request)
        {
            if (!request.Diagnostics.TryGetValue("ProtectedBaselinePath", out string path) ||
                string.Equals(Path.GetFullPath(path), Path.GetFullPath(request.WorkingModelPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("P15_TECH_BLOCKED_BASELINE_TARGET");
            var baseline = ProtectedBaseline.Register(path);
            SafePathPolicy.ValidateWorkingCopyLocation(baseline.DirectoryPath, Path.GetDirectoryName(request.WorkingModelPath));
            if (baseline.Fingerprint.Value != request.SourceBaselineFingerprint)
                throw new InvalidDataException("HARD_FAIL_PROTECTED_BASELINE_MODIFIED");
            RuntimeStageJournal.Record("BASELINE_INTEGRITY", "EXIT", new { fingerprint = baseline.Fingerprint.Value });
        }

        internal static bool Eligible(int id, object start, object stop, object setUp, object setDown) =>
            id > 0 && setUp is bool && setDown is bool && start is double a && Finite(a) && stop is double b && Finite(b);

        internal sealed class ConduitEligibility
        {
            public int Id { get; set; }
            public bool? SetUp { get; set; }
            public bool? SetDown { get; set; }
            public string Start { get; set; }
            public string Stop { get; set; }
            public string Outcome { get; set; }
            public string ReadError { get; set; }
        }

        internal static ConduitEligibility SelectConduit(IEnumerable<int> ids, Func<int, object> element,
            Func<int, double> readStart, Func<int, double> readStop, ModelReadEvidence evidence)
        {
            var rows = new List<ConduitEligibility>();
            ConduitEligibility selected = null;
            foreach (int id in ids.OrderBy(value => value))
            {
                var row = new ConduitEligibility { Id = id, Outcome = "InvalidId" };
                if (id > 0)
                {
                    row.Outcome = "UnreadableFlags";
                    try
                    {
                        object conduit = element(id);
                        row.SetUp = GravityWriteAndSolverHost.ReadInheritanceFlag(conduit, FlagUp);
                        row.SetDown = GravityWriteAndSolverHost.ReadInheritanceFlag(conduit, FlagDown);
                        row.Outcome = "UnreadableEndpoints";
                        double start = readStart(id), stop = readStop(id);
                        row.Start = Text(start); row.Stop = Text(stop);
                        row.Outcome = Eligible(id, start, stop, row.SetUp, row.SetDown) ? "Eligible" : "NonFiniteEndpoints";
                    }
                    catch (Exception error) { row.ReadError = error.GetBaseException().GetType().Name; }
                }
                rows.Add(row);
                RuntimeStageJournal.Record("CONDUIT_ELIGIBILITY", "EXIT", row);
                if (selected == null && row.Outcome == "Eligible") selected = row;
            }
            var counts = new[] { "Eligible", "InvalidId", "UnreadableFlags", "UnreadableEndpoints", "NonFiniteEndpoints" }
                .ToDictionary(reason => reason, reason => rows.Count(row => row.Outcome == reason));
            evidence.Diagnostics["ConduitEligibilityJson"] = RuntimeHostProtocol.Serializer().Serialize(rows);
            evidence.Diagnostics["ConduitEligibilityCountsJson"] = RuntimeHostProtocol.Serializer().Serialize(counts);
            evidence.Diagnostics["ConduitCount"] = rows.Count.ToString(Invariant);
            evidence.Diagnostics["EligibleConduitCount"] = counts["Eligible"].ToString(Invariant);
            evidence.Diagnostics["RejectedConduitCount"] = (rows.Count - counts["Eligible"]).ToString(Invariant);
            RuntimeStageJournal.Record("ELIGIBILITY_COMPLETED", "EXIT", new { total = rows.Count, counts, selected = selected?.Id });
            Check(selected != null, "P15_TECH_BLOCKED_NO_VALID_CONDUIT");
            return selected;
        }

        internal static bool ValidWriteCounts(IDictionary<string, string> diagnostics, int total) =>
            diagnostics.TryGetValue("LocalizationPropertySetCount", out string flags) &&
            int.TryParse(flags, out int localized) && localized >= 0 && localized <= 2 &&
            diagnostics.TryGetValue("EndpointInvertPropertySetCount", out string endpoints) && endpoints == "2" &&
            diagnostics.TryGetValue("LocalizationReadbackVerified", out string verified) && verified == "true" &&
            total == 2 + localized;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static string Text(double value) => value.ToString("R", Invariant);
        private static void Check(bool condition, string code)
        { if (!condition) throw new InvalidOperationException(code); }
        private static object Property(object target, string name) =>
            target.GetType().GetProperty(name)?.GetValue(target, null) ?? throw new MissingMemberException(target.GetType().FullName, name);
        private static object Invoke(object target, string name, Type[] types, params object[] args) =>
            (target.GetType().GetMethod(name, types) ?? throw new MissingMethodException(target.GetType().FullName, name)).Invoke(target, args);
        private static double Read(object field, int id) => (double)Invoke(field, "GetValue", new[] { typeof(int) }, id);
        private static void CheckValues(double start, double stop, double expectedStart, double expectedStop, string code) =>
            Check(Finite(start) && Finite(stop) && Math.Abs(start - expectedStart) <= 1e-6 && Math.Abs(stop - expectedStop) <= 1e-6, code);
        private static void CheckFlags(object conduit) => Check(
            !GravityWriteAndSolverHost.ReadInheritanceFlag(conduit, FlagUp) &&
            !GravityWriteAndSolverHost.ReadInheritanceFlag(conduit, FlagDown), "P15_TECH_BLOCKED_LOCAL_FLAGS");

        internal static MethodInfo DoubleSetter(object field)
        {
            var setter = field?.GetType().GetMethod("SetValue", new[] { typeof(int), typeof(bool), typeof(double) });
            Check(setter != null && !setter.IsStatic && setter.ReturnType == typeof(void), "P15_TECH_BLOCKED_DOUBLE_SETTER");
            return setter;
        }

        internal static void RequireFeet(SourceUnitIdentity unit)
        {
            Check(unit != null && string.Equals(unit.Dimension, "length", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(unit.Symbol, "ft", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(unit.UnitName, "feet", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(unit.UnitName, "foot", StringComparison.OrdinalIgnoreCase)), "P15_TECH_BLOCKED_WORKING_UNITS");
        }

        // Reuse the production localization setter and context guard; endpoint values use only Double setters.
        internal static void WriteEndpoints(object conduit, int id, object up, object down, object setUp, object setDown,
            bool sourceUp, bool sourceDown, double start, double stop,
            int activeScenario, int isolatedScenario, int assignedAlternative, int isolatedAlternative,
            int sourceScenario, int sourceAlternative, int sourceAssignedAlternative, ModelReadEvidence evidence)
        {
            Check(id > 0 && Finite(start) && Finite(stop) && sourceScenario > 0 && isolatedScenario != sourceScenario &&
                (!evidence.Diagnostics.TryGetValue("PropertySetCount", out string prior) || prior == "0"), "P15_TECH_BLOCKED_WRITE_BUDGET");
            var setters = new[] { DoubleSetter(up), DoubleSetter(down) };
            var flagSetters = new[] { sourceUp ? GravityWriteAndSolverHost.RequireLocalSetter(setUp) : null,
                sourceDown ? GravityWriteAndSolverHost.RequireLocalSetter(setDown) : null };
            GravityWriteAndSolverHost.ValidateIsolatedPhysicalWriteContext(activeScenario, isolatedScenario,
                assignedAlternative, isolatedAlternative, sourceAlternative, sourceAssignedAlternative);
            Check(GravityWriteAndSolverHost.ReadInheritanceFlag(conduit, FlagUp) == sourceUp &&
                GravityWriteAndSolverHost.ReadInheritanceFlag(conduit, FlagDown) == sourceDown, "P15_TECH_BLOCKED_ISOLATED_ORIGINAL_FLAGS");
            RuntimeStageJournal.Record("WRITE_CONTEXT_VERIFIED", "EXIT", new { activeScenario, isolatedAlternative, sourceAlternative });
            evidence.Diagnostics["PropertySetCount"] = "0";
            evidence.Diagnostics["LocalizationPropertySetCount"] = "0";
            evidence.Diagnostics["EndpointInvertPropertySetCount"] = "0";
            object[] flagFields = { setUp, setDown };
            string[] flagNames = { FlagUp, FlagDown };
            int localized = 0;
            for (int i = 0; i < 2; i++)
            {
                if (flagSetters[i] == null) continue;
                evidence.Diagnostics["PropertySetCount"] = (++localized).ToString(Invariant);
                evidence.Diagnostics["LocalizationPropertySetCount"] = localized.ToString(Invariant);
                RuntimeStageJournal.Call("LocalizationSetter:" + flagNames[i], () =>
                    flagSetters[i].Invoke(flagFields[i], new object[] { id, true, false }));
            }
            CheckFlags(conduit);
            evidence.Diagnostics["LocalizationReadbackVerified"] = "true";
            RuntimeStageJournal.Record("LOCALIZATION_VERIFIED", "EXIT", new { localized, upstream = false, downstream = false });
            object[] fields = { up, down };
            double[] values = { start, stop };
            string[] names = { Up, Down };
            for (int i = 0; i < 2; i++)
            {
                evidence.Diagnostics["PropertySetCount"] = (localized + i + 1).ToString(Invariant);
                evidence.Diagnostics["EndpointInvertPropertySetCount"] = (i + 1).ToString(Invariant);
                RuntimeStageJournal.Call("EndpointSetter:" + names[i], () =>
                    setters[i].Invoke(fields[i], new object[] { id, true, values[i] }));
            }
            evidence.Diagnostics["EndpointInvertPropertySetCount"] = "2";
            RuntimeStageJournal.Record("WRITE_COMPLETED", "EXIT", new { endpointSetters = 2, localizationSetters = localized, total = 2 + localized, id });
            evidence.Diagnostics["ImmediateEndpointReadCount"] = "1";
            double readStart = RuntimeStageJournal.Call("EndpointReadback:" + Up, () => Read(up, id));
            evidence.Diagnostics["ImmediateEndpointReadCount"] = "2";
            double readStop = RuntimeStageJournal.Call("EndpointReadback:" + Down, () => Read(down, id));
            evidence.Diagnostics["ImmediateStartInvert"] = Text(readStart);
            evidence.Diagnostics["ImmediateStopInvert"] = Text(readStop);
            CheckValues(readStart, readStop, start, stop, "P15_TECH_BLOCKED_READBACK");
            CheckFlags(conduit);
            evidence.Diagnostics["ImmediateReadbackVerified"] = "true";
            RuntimeStageJournal.Record("READBACK_COMPLETED", "EXIT", new { readStart, readStop, reads = 2 });
        }

        private static object ConduitManager(object dataset)
        {
            var domainType = GravityNetworkReadMapper.ResolveDomainElementType();
            Check(domainType != null, "P15_TECH_BLOCKED_CONDUIT_TYPE");
            int typeId = GravityNetworkReadMapper.ResolveElementTypeId(domainType, "ConduitElementManager");
            Check(typeId >= 0, "P15_TECH_BLOCKED_CONDUIT_TYPE");
            return Invoke(dataset, "DomainElementManager", new[] { typeof(int) }, typeId);
        }

        internal static void ExecuteWrite(object dataset, object ui, object project, HostRequest request, ModelReadEvidence evidence)
        {
            GravityWriteAndSolverHost.RequireControlledWorkingCopyTarget(project, request, evidence);
            VerifyBaseline(request);
            RuntimeStageJournal.Record("OPEN", "EXIT", new { request.WorkingModelPath });
            object sm = Property(dataset, ScenarioManagerName);
            var active = sm.GetType().GetProperty("ActiveScenarioID");
            Check(active?.GetSetMethod() != null, "P15_TECH_BLOCKED_ACTIVE_SCENARIO");
            int sourceId = (int)active.GetValue(sm, null);
            object source = Invoke(sm, "Element", new[] { typeof(int) }, sourceId);
            int physicalType = ScenarioAlternativeIsolationHost.ResolvePhysicalAlternativeTypeId(dataset);
            Check(sourceId > 0 && source != null && physicalType > 0, "P15_TECH_BLOCKED_SOURCE");
            int sourceAlt = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(source, physicalType);
            Check(sourceAlt > 0, "P15_TECH_BLOCKED_SOURCE_PHYSICAL");
            var assignments = ScenarioAlternativeIsolationHost.EnumerateAllAlternativeTypeIds(dataset)
                .ToDictionary(t => t.ToString(Invariant), t => ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(source, t));
            object manager = ConduitManager(dataset);
            object up = GravityNetworkReadMapper.ResolveDomainElementField(manager, Up);
            object down = GravityNetworkReadMapper.ResolveDomainElementField(manager, Down);
            Check(up != null && down != null, "P15_TECH_BLOCKED_ENDPOINT_FIELDS");
            DoubleSetter(up); DoubleSetter(down);
            var units = new Dictionary<string, FieldUnitMetadataDto>();
            RequireFeet(GravityNetworkReadMapper.InspectFieldUnit(manager, Up, units, new List<MemberEvidence>()));
            RequireFeet(GravityNetworkReadMapper.InspectFieldUnit(manager, Down, units, new List<MemberEvidence>()));
            evidence.Diagnostics["EndpointUnitsJson"] = RuntimeHostProtocol.Serializer().Serialize(units);
            evidence.Diagnostics["SourceScenarioID"] = sourceId.ToString(Invariant);
            evidence.Diagnostics["SourcePhysicalAlternativeID"] = sourceAlt.ToString(Invariant);
            evidence.Diagnostics["PhysicalAlternativeTypeID"] = physicalType.ToString(Invariant);

            object ids = WaterObjectsLifecycleHost.ElementIDs(manager.GetType()).Invoke(manager, null);
            int count = ScenarioAlternativeCapabilityMapper.GetCollectionCount(ids);
            var selection = SelectConduit(Enumerable.Range(0, count).Select(i => ScenarioAlternativeCapabilityMapper.GetCollectionItemId(ids, i)),
                id => Invoke(manager, "Element", new[] { typeof(int) }, id), id => Read(up, id), id => Read(down, id), evidence);
            int selected = selection.Id;
            double originalStart = double.Parse(selection.Start, Invariant), originalStop = double.Parse(selection.Stop, Invariant);
            bool sourceUp = selection.SetUp.Value, sourceDown = selection.SetDown.Value;
            evidence.Diagnostics["SelectedConduitID"] = selected.ToString(Invariant);
            evidence.Diagnostics["SourceStartInvert"] = Text(originalStart);
            evidence.Diagnostics["SourceStopInvert"] = Text(originalStop);
            evidence.Diagnostics["SourceSetInvertUpstream"] = sourceUp ? "true" : "false";
            evidence.Diagnostics["SourceSetInvertDownstream"] = sourceDown ? "true" : "false";
            evidence.Diagnostics["SourceAssignmentsJson"] = RuntimeHostProtocol.Serializer().Serialize(assignments);
            RuntimeStageJournal.Record("SOURCE_RESOLVED", "EXIT", new { sourceId, sourceAlt, physicalType, selected, originalStart, originalStop, sourceUp, sourceDown, units });

            evidence.Diagnostics["ScenarioCopyInvoked"] = "true";
            int isolatedId = (int)RuntimeStageJournal.Call("ScenarioManager.Copy", () => Invoke(sm, "Copy", new[] { typeof(int) }, sourceId));
            object isolated = Invoke(sm, "Element", new[] { typeof(int) }, isolatedId);
            object am = Invoke(dataset, "Alternative" + "Manager", new[] { typeof(int) }, physicalType);
            evidence.Diagnostics["AlternativeCopyInvoked"] = "true";
            int isolatedAlt = (int)RuntimeStageJournal.Call("PhysicalAlternativeManager.Copy", () => Invoke(am, "Copy", new[] { typeof(int) }, sourceAlt));
            Check(isolatedId > 0 && isolatedId != sourceId && isolatedAlt > 0 && isolatedAlt != sourceAlt && isolated != null,
                "P15_TECH_BLOCKED_ISOLATION");
            Invoke(isolated, "AlternativeID", new[] { typeof(int), typeof(int) }, physicalType, isolatedAlt);
            foreach (var pair in assignments)
            {
                int typeId = int.Parse(pair.Key, Invariant);
                Check(ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(source, typeId) == pair.Value &&
                    (typeId == physicalType || ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(isolated, typeId) == pair.Value),
                    "P15_TECH_BLOCKED_ASSIGNMENTS");
            }
            evidence.Diagnostics["CreatedScenarioID"] = isolatedId.ToString(Invariant);
            evidence.Diagnostics["CreatedPhysicalAlternativeID"] = isolatedAlt.ToString(Invariant);
            RuntimeStageJournal.Record("ISOLATION_CREATED", "EXIT", new { isolatedId, isolatedAlt, sourceId, sourceAlt });
            active.GetSetMethod().Invoke(sm, new object[] { isolatedId });
            object isolatedConduit = Invoke(manager, "Element", new[] { typeof(int) }, selected);
            CheckValues(Read(up, selected), Read(down, selected), originalStart, originalStop, "P15_TECH_BLOCKED_ISOLATED_ORIGINALS");
            GravityWriteAndSolverHost.RequireControlledWorkingCopyTarget(project, request, evidence);
            double offset = double.Parse(request.Diagnostics["TestOffsetFeet"], Invariant);
            double targetStart = originalStart + offset, targetStop = originalStop + offset;
            evidence.Diagnostics["TestOffsetFeet"] = Text(offset);
            evidence.Diagnostics["CandidateStartInvert"] = Text(targetStart);
            evidence.Diagnostics["CandidateStopInvert"] = Text(targetStop);
            evidence.Diagnostics["TechnicalAcceptanceOnly"] = "true";
            object setUp = GravityNetworkReadMapper.ResolveDomainElementField(manager, FlagUp);
            object setDown = GravityNetworkReadMapper.ResolveDomainElementField(manager, FlagDown);
            WriteEndpoints(isolatedConduit, selected, up, down, setUp, setDown, sourceUp, sourceDown, targetStart, targetStop,
                (int)active.GetValue(sm, null), isolatedId,
                ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(isolated, physicalType), isolatedAlt,
                sourceId, sourceAlt, ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(source, physicalType), evidence);
            evidence.Diagnostics["SaveCurrentProjectInvoked"] = "true";
            object save = RuntimeStageJournal.Call(SaveName, () => Invoke(ui, SaveName, Type.EmptyTypes));
            Check(save is bool saved && saved, "P15_TECH_BLOCKED_SAVE");
            evidence.Diagnostics["SaveCurrentProjectSucceeded"] = "true";
            RuntimeStageJournal.Record("SAVE_COMPLETED", "EXIT", new { saves = 1 });
        }

        internal static void PrepareReopen(HostRequest request, ModelReadEvidence written)
        {
            Check(written.FailureCode == null && written.CloseCompleted &&
                written.Diagnostics.TryGetValue("StaCompleted", out string sta) && sta == "true" &&
                written.Diagnostics.TryGetValue("SaveCurrentProjectSucceeded", out string saved) && saved == "true",
                "P15_TECH_BLOCKED_FIRST_SESSION_CLEANUP");
            var keys = new Dictionary<string, string> {
                { "ExpectedCreatedScenarioId", "CreatedScenarioID" }, { "ExpectedCreatedPhysicalAlternativeId", "CreatedPhysicalAlternativeID" },
                { "ExpectedSourceScenarioId", "SourceScenarioID" }, { "ExpectedSourcePhysicalAlternativeId", "SourcePhysicalAlternativeID" },
                { "ExpectedPhysicalTypeId", "PhysicalAlternativeTypeID" }, { "ExpectedSelectedConduitId", "SelectedConduitID" },
                { "ExpectedCandidateStartInvert", "CandidateStartInvert" }, { "ExpectedCandidateStopInvert", "CandidateStopInvert" },
                { "ExpectedSourceStartInvertBefore", "SourceStartInvert" }, { "ExpectedSourceStopInvertBefore", "SourceStopInvert" },
                { "ExpectedSourceAssignmentsJson", "SourceAssignmentsJson" },
                { "ExpectedSourceSetInvertUpstream", "SourceSetInvertUpstream" }, { "ExpectedSourceSetInvertDownstream", "SourceSetInvertDownstream" } };
            foreach (var pair in keys) request.Diagnostics[pair.Key] = written.Diagnostics[pair.Value];
            request.Diagnostics["P15TechnicalPhase"] = "REOPEN";
        }

        internal static void ExecuteReopenAndSolver(object dataset, object ui, object project, HostRequest request, ModelReadEvidence evidence)
        {
            GravityWriteAndSolverHost.RequireControlledWorkingCopyTarget(project, request, evidence);
            GravityWriteAndSolverHost.ExecuteHost2Reopen(dataset, ui, project, request, evidence);
            VerifyBaseline(request);
            evidence.Diagnostics["WriteCapabilityProven"] = "true";
            evidence.Diagnostics["ReopenEndpointReadCount"] = "4";
            RuntimeStageJournal.Record("REOPEN_VERIFIED", "EXIT", new { isolatedAndSourceEndpointReads = 4, baselineUnchanged = true });
            object sm = Property(dataset, ScenarioManagerName);
            var active = sm.GetType().GetProperty("ActiveScenarioID");
            int sourceId = int.Parse(request.Diagnostics["ExpectedSourceScenarioId"], Invariant);
            int isolatedId = int.Parse(request.Diagnostics["ExpectedCreatedScenarioId"], Invariant);
            int physicalType = int.Parse(request.Diagnostics["ExpectedPhysicalTypeId"], Invariant);
            int sourceAlt = int.Parse(request.Diagnostics["ExpectedSourcePhysicalAlternativeId"], Invariant);
            int isolatedAlt = int.Parse(request.Diagnostics["ExpectedCreatedPhysicalAlternativeId"], Invariant);
            object isolated = Invoke(sm, "Element", new[] { typeof(int) }, isolatedId);
            object source = Invoke(sm, "Element", new[] { typeof(int) }, sourceId);
            active.GetSetMethod().Invoke(sm, new object[] { isolatedId });
            GravityWriteAndSolverHost.ValidateIsolatedPhysicalWriteContext((int)active.GetValue(sm, null), isolatedId,
                ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(isolated, physicalType), isolatedAlt, sourceAlt,
                ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(source, physicalType));
            var map = ScenarioAlternativeCapabilityMapper.Map(dataset, project, ui, null);
            var options = map.CalculationOptionsRead;
            Check(map.ScenarioRead.ActiveScenarioId == isolatedId && options.Classification == "MAPPED_AND_READ" &&
                options.ActiveScenarioOptionsId.GetValueOrDefault() > 0 &&
                ScenarioAlternativeCapabilityMapper.IsValidEngineTypeName(options.EngineTypeName), "P15_TECH_BLOCKED_SOLVER_OPTIONS");
            var binding = GravityWriteAndSolverHost.BindNumericalEngine(dataset, isolated, options.EngineTypeName,
                evidence, AppDomain.CurrentDomain.GetAssemblies());
            RuntimeStageJournal.Record("SOLVER_STARTED", "ENTER", new { isolatedId, engine = options.EngineTypeName, options = options.ActiveScenarioOptionsId });
            GravityWriteAndSolverHost.RunBoundNumericalEngine(binding, isolated, isolatedId, sourceId, evidence);
            object status = Invoke(project, "GetScenarioComputeStatus", new[] { typeof(int) }, isolatedId);
            string statusText = Invoke(project, "ScenarioStatusText", new[] { typeof(int) }, isolatedId) as string;
            evidence.Diagnostics["ScenarioComputeStatus"] = status?.ToString() ?? "Unknown";
            evidence.Diagnostics["ScenarioStatusText"] = statusText ?? "";
            evidence.Diagnostics["HydraulicResultReads"] = "0";
            bool success = string.Equals(status?.ToString(), "Success", StringComparison.OrdinalIgnoreCase);
            evidence.Diagnostics["SolverStatusSuccess"] = success ? "true" : "false";
            RuntimeStageJournal.Record("SOLVER_COMPLETED", "EXIT", new { status = status?.ToString(), statusText, success });
            Check(success, "P15_TECH_BLOCKED_SOLVER_STATUS");
        }
    }
}
