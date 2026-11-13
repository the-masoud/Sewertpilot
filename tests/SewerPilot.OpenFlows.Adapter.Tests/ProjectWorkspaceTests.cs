using System;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static string Ui01Directory() => Path.Combine(Path.GetTempPath(), "SewerPilot", "MvpUi01Tests", Guid.NewGuid().ToString("N"));
        private static void Ui01Reject(Action action, string classification)
        {
            try { action(); throw new Exception("Expected rejection: " + classification); }
            catch (ProjectStorageException e) { Check(e.Classification == classification, "Exact project-storage rejection: " + classification); }
        }
        private static NetworkReadResult Ui01Network() => new NetworkReadResult {
            ScenarioContext = new NetworkScenarioContext { ActiveScenarioId = 17, ActiveScenarioLabel = "TEST_ONLY", PhysicalAlternativeId = 23 } };
        private static void MvpUi01_ProjectRoundtripAndLegacy()
        {
            string root = Ui01Directory(); Directory.CreateDirectory(root); string model = Path.Combine(root, "TEST_ONLY.stsw"); File.WriteAllText(model, "TEST_ONLY_NOT_A_BENTLEY_MODEL");
            var w = ProjectWorkspace.Create(Path.Combine(root, "project"), "Operator project");
            w.State.SelectedModule = "GravityFlow"; w.State.CurrentDesignRunId = "existing-run";
            w.AttachModel(ProtectedBaseline.Register(model), Ui01Network());
            Check(w.Dirty && w.State.StateRevision == 1, "Read updates metadata without saving project or Bentley model.");
            w.SaveProjectState(); var reopened = ProjectWorkspace.Open(w.DirectoryPath);
            Check(reopened.State.ProjectId == w.State.ProjectId && reopened.State.ProjectName == "Operator project" && reopened.State.StateRevision == 2 && !reopened.Dirty,
                "Project identity/name/revision survives store create/save/open.");
            Check(reopened.State.Model.BaselineLocationMetadata == model && reopened.State.Model.ProtectedBaselineFingerprint == ProtectedBaseline.Register(model).Fingerprint.Value.ToLowerInvariant(), "Actual model path and exact normalized family fingerprint survive.");
            Check(reopened.State.ScenarioAlternativeState.SelectedScenarioId == 17 && reopened.State.ScenarioAlternativeState.SelectedPhysicalAlternativeId == 23 && reopened.State.CurrentDesignRunId == "existing-run", "Source IDs and unrelated existing metadata retained.");
            Check(reopened.Network == null && reopened.Baseline == null && reopened.ModelReferenceStatus().StartsWith("Saved model identity verified"), "Reopen verifies reference but never restores stale network or launches vendor reader.");
            var audit = ProjectStore.ReadAudit(w.DirectoryPath);
            Check(audit.Select(a => a.ActionType).SequenceEqual(new[] { AuditActionType.ProjectCreated, AuditActionType.ProjectStateUpdated, AuditActionType.ProjectOpened }), "Established audit contract records create/save/open.");
            var legacy = RuntimeHostProtocol.Serializer().Deserialize<ProjectState>("{\"SchemaVersion\":1,\"ProjectId\":\"12345678\",\"ProjectName\":\"Legacy\",\"StateRevision\":1,\"ScenarioAlternativeState\":{\"SelectedScenarioId\":17}}");
            ProjectStore.ValidateState(legacy);
            Check(!legacy.ScenarioAlternativeState.SelectedPhysicalAlternativeId.HasValue, "Schema-1 files without optional Physical field remain valid.");
            legacy.ScenarioAlternativeState.SelectedPhysicalAlternativeId = 0;
            Ui01Reject(() => ProjectStore.ValidateState(legacy), "MALFORMED_PROJECT_STATE");
        }
        private static void MvpUi01_MissingAndChangedModelRejected()
        {
            string root = Ui01Directory(); Directory.CreateDirectory(root); string model = Path.Combine(root, "TEST_ONLY.stsw"); File.WriteAllText(model, "TEST_ONLY_BEFORE");
            var w = ProjectWorkspace.Create(Path.Combine(root, "project"), "Source guard"); w.AttachModel(ProtectedBaseline.Register(model), Ui01Network()); w.SaveProjectState();
            string stored = File.ReadAllText(Path.Combine(w.DirectoryPath, "project.json"));
            File.WriteAllText(model, "TEST_ONLY_CHANGED");
            Ui01Reject(() => w.ModelReferenceStatus(), "SOURCE_MODEL_IDENTITY_CHANGED");
            Ui01Reject(() => w.SaveProjectState(), "SOURCE_MODEL_IDENTITY_CHANGED");
            Check(stored == File.ReadAllText(Path.Combine(w.DirectoryPath, "project.json")) && w.State.StateRevision == 2, "Changed source never replaces saved identity or revision.");
            w.State.Model.BaselineLocationMetadata = Path.Combine(root, "missing.stsw");
            try { w.ModelReferenceStatus(); throw new Exception("Missing source should fail"); } catch (FileNotFoundException) { Check(true, "Missing model reference is explicit failure before runtime."); }
        }
        private static void MvpUi01_ConflictMalformedAndInvalidDirectory()
        {
            string root = Ui01Directory(); var w = ProjectWorkspace.Create(root, "Original");
            Ui01Reject(() => ProjectWorkspace.Create(root, "Overwrite"), "PROJECT_STATE_CONFLICT");
            var newer = ProjectStore.LoadProject(root); ProjectStore.SaveProject(root, newer, 1);
            string disk = File.ReadAllText(Path.Combine(root, "project.json"));
            Ui01Reject(() => w.SaveProjectState(), "PROJECT_STATE_CONFLICT");
            Check(w.State.StateRevision == 1 && disk == File.ReadAllText(Path.Combine(root, "project.json")), "Stale operator revision preserves newer state and its own expected revision.");
            string invalid = Path.Combine(root, "ordinary-file"); File.WriteAllText(invalid, "preserve");
            Ui01Reject(() => ProjectWorkspace.Create(invalid, "Invalid"), "PROJECT_STATE_CONFLICT");
            File.WriteAllText(Path.Combine(root, "project.json"), "{");
            Ui01Reject(() => ProjectWorkspace.Open(root), "MALFORMED_PROJECT_STATE");
        }
        private static void MvpUi01_ReadGuardAndFailureRecovery()
        {
            var w = ProjectWorkspace.Create(Ui01Directory(), "Read guard"); w.BeginRead();
            try { w.ReadNetwork("TEST_ONLY_NEVER_VENDOR.stsw"); throw new Exception("Duplicate read should reject"); }
            catch (InvalidOperationException e) { Check(e.Message.StartsWith("MODEL_LOAD_ALREADY_ACTIVE") && w.Reading, "Duplicate load rejected before source/vendor operations."); }
            try { w.SaveProjectState(); throw new Exception("Save during load should reject"); }
            catch (InvalidOperationException) { Check(w.State.StateRevision == 1, "Saving during read rejected without revision change."); }
            w.EndRead();
            try { w.ReadNetwork(Path.Combine(w.DirectoryPath, "missing.stsw")); throw new Exception("Missing model should reject"); }
            catch (FileNotFoundException) { Check(!w.Reading && w.Network == null && !w.Dirty, "Failed source lookup resets bounded read guard and never binds fabricated data."); }
        }
        private static void MvpUi01_SourceProjectionExactValues()
        {
            var ft = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            var c = new ConduitReadDto { Id = 9, StartNodeId = 2, StopNodeId = 3, StartInvert = 1.234567890123, StopInvert = -4.5,
                StartInvertUnit = ft, StopInvertUnit = ft, SetInvertToStart = true, SetInvertToStop = false, StartInvertSourceField = "source-start", StopInvertSourceField = "source-stop" };
            var row = NetworkExplorerProjection.Conduit(c);
            Check(row.Cells.SequenceEqual(new[] { "9", "2", "3", "1.234567890123", "ft", "True", "-4.5", "ft", "False" }), "No rounding/conversion/inheritance inference in displayed source values.");
            var n = Ui01Network(); n.Conduits.Add(c);
            var s = new StructureReadDto { Id = 3, RuntimeCategory = "TEST_ONLY_TYPE", InvertElevation = -4.5, InvertElevationUnit = ft, X = 7, Y = 8, CoordinateUnit = ft };
            var structure = NetworkExplorerProjection.Structure(s, n);
            Check(structure.Cells[0] == "3" && structure.Cells[1] == "TEST_ONLY_TYPE" && structure.Cells[6] == "-4.5" && structure.Cells[7] == "ft" && structure.Cells[8] == "9", "Type, elevation, unit and connected IDs come from actual DTO fields only.");
            Check(row.Details.Contains("source-start") && structure.Details.Contains("X: 7") && structure.Details.Contains("no flow direction"), "Details show source fields and topology limits.");
        }
        private static void MvpUi01_UnavailableFieldsAndTopology()
        {
            var c = NetworkExplorerProjection.Conduit(new ConduitReadDto { Id = 1, StartInvert = double.NaN, StopInvert = double.PositiveInfinity, StartInvertUnit = SourceUnitIdentity.Unresolved() });
            Check(c.Cells.Skip(1).All(v => v == "Unavailable"), "Null/nonfinite endpoints, absent node IDs, inherited flags and units explicitly unavailable.");
            var s = NetworkExplorerProjection.Structure(new StructureReadDto { Id = 1 }, null);
            Check(s.Cells.Skip(1).All(v => v == "Unavailable"), "Absent structure values/type/topology explicitly unavailable.");
            Check(c.Details.Contains("Hydraulic-flow orientation is not approved or inferred"), "Bentley topology never promoted to engineering orientation.");
        }
    }
}
