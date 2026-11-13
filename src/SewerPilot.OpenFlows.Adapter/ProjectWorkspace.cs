using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace SewerPilot.OpenFlows.Adapter
{
    // Operator project storage only. This class never requests engineering execution or a Bentley save.
    internal sealed class ProjectWorkspace
    {
        private int reading;
        internal string DirectoryPath { get; private set; }
        internal ProjectState State { get; private set; }
        internal bool Dirty { get; private set; }
        internal bool Reading => Volatile.Read(ref reading) != 0;
        internal ProtectedBaseline Baseline { get; private set; }
        internal NetworkReadResult Network { get; private set; }
        private ProjectWorkspace(string directory, ProjectState state) { DirectoryPath = directory; State = state; }

        internal static ProjectWorkspace Create(string directory, string name)
        {
            var path = Path.GetFullPath(directory);
            WorkingCopyIntegrity.RejectLinks(path);
            if (File.Exists(path) || (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()))
                throw new ProjectStorageException("PROJECT_STATE_CONFLICT", "Choose a new or empty project directory; existing files are not overwritten.");
            return new ProjectWorkspace(path, ProjectStore.CreateProject(path, name));
        }
        internal static ProjectWorkspace Open(string directory)
        {
            var path = Path.GetFullPath(directory);
            WorkingCopyIntegrity.RejectLinks(path);
            WorkingCopyIntegrity.RejectLinks(Path.Combine(path, "project.json"));
            var state = ProjectStore.LoadProject(path);
            EngineeringDesignStorage.Load(path, state);
            ProjectStore.AppendAudit(path, new AuditRecord { ActionType = AuditActionType.ProjectOpened, Module = state.SelectedModule });
            return new ProjectWorkspace(path, state);
        }
        internal string ModelReferenceStatus()
        {
            string model = State.Model?.BaselineLocationMetadata;
            if (string.IsNullOrWhiteSpace(model)) return "No model selected.";
            VerifyStoredModel(model);
            return "Saved model identity verified. Select the model to read current network data.";
        }
        private void VerifyStoredModel(string path)
        {
            var observed = ProtectedBaseline.Register(path);
            var expected = State.Model?.ProtectedBaselineFingerprint;
            if (!string.IsNullOrWhiteSpace(expected) &&
                !string.Equals(expected, observed.Fingerprint.Value, StringComparison.OrdinalIgnoreCase))
                throw new ProjectStorageException("SOURCE_MODEL_IDENTITY_CHANGED", "Source model family changed; stored identity is retained and no read or save is admitted.");
        }
        internal void BeginRead()
        {
            if (Interlocked.CompareExchange(ref reading, 1, 0) != 0)
                throw new InvalidOperationException("MODEL_LOAD_ALREADY_ACTIVE: A bounded read is already in progress.");
        }
        internal void EndRead() { Interlocked.Exchange(ref reading, 0); }
        internal void ReadNetwork(string path)
        {
            BeginRead();
            try
            {
                Baseline = null; Network = null;
                var baseline = ProtectedBaseline.Register(path);
                if (string.Equals(Path.GetFullPath(path), State.Model?.BaselineLocationMetadata, StringComparison.OrdinalIgnoreCase))
                    VerifyStoredModel(path);
                RuntimeStageJournal.Record("WorkspaceModelRead", "ENTER", new { Source = baseline.PrimaryFilePath });
                using (var reader = new OpenFlowsModelReader())
                {
                    var result = reader.ReadWithEvidence(baseline.PrimaryFilePath);
                    RuntimeStageJournal.Record("WorkspaceModelRead", "EXIT", new {
                        result.Succeeded, result.Evidence.FailureCode, result.Evidence.Exception, result.Evidence.Diagnostics });
                    if (!result.Succeeded || !result.Evidence.Diagnostics.TryGetValue("P12ANetworkReadJson", out string json))
                        throw new InvalidOperationException("READ_ONLY_MODEL_INSPECTION: " + result.Evidence.FailureCode + " " + result.Evidence.Exception);
                    AttachModel(baseline, RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(json));
                }
            }
            finally { EndRead(); }
        }
        internal void AttachModel(ProtectedBaseline baseline, NetworkReadResult network)
        {
            if (baseline == null || network?.ScenarioContext == null || network.ScenarioContext.ActiveScenarioId <= 0 || network.ScenarioContext.PhysicalAlternativeId <= 0)
                throw new InvalidOperationException("SOURCE_CONTEXT_UNAVAILABLE: Source scenario and Physical alternative are required.");
            if (!baseline.VerifyCurrentIntegrity().IsMatch)
                throw new InvalidOperationException("PROTECTED_BASELINE_CHANGED: Read-only source integrity failed.");
            var model = State.Model ?? new ProjectModelMetadata();
            model.BaselineLocationMetadata = baseline.PrimaryFilePath;
            model.PrimaryModelDisplayName = baseline.PrimaryFileName;
            // P7 storage requires lowercase; comparison remains the exact same case-independent hash identity.
            model.ProtectedBaselineFingerprint = baseline.Fingerprint.Value.ToLowerInvariant();
            State.Model = model;
            var scenario = State.ScenarioAlternativeState ?? new ProjectScenarioAlternativeState();
            scenario.SelectedScenarioId = network.ScenarioContext.ActiveScenarioId;
            scenario.ScenarioLabel = network.ScenarioContext.ActiveScenarioLabel;
            scenario.SelectedPhysicalAlternativeId = network.ScenarioContext.PhysicalAlternativeId;
            State.ScenarioAlternativeState = scenario;
            Baseline = baseline; Network = network; Dirty = true;
        }
        internal void SaveProjectState()
        {
            if (Reading) throw new InvalidOperationException("MODEL_LOAD_ALREADY_ACTIVE: Project saving waits for the bounded model read.");
            if (!string.IsNullOrWhiteSpace(State.Model?.BaselineLocationMetadata)) VerifyStoredModel(State.Model.BaselineLocationMetadata);
            var stored = ProjectStore.LoadProject(DirectoryPath);
            EngineeringDesignStorage.Load(DirectoryPath, State);
            if (stored.ProjectId != State.ProjectId)
                throw new ProjectStorageException("PROJECT_STATE_CONFLICT", "The stored project identity changed; it will not be overwritten.");
            // Saving a clone prevents a partial storage failure from changing the operator's expected revision.
            var copy = RuntimeHostProtocol.Serializer().Deserialize<ProjectState>(RuntimeHostProtocol.Serializer().Serialize(State));
            State = ProjectStore.SaveProject(DirectoryPath, copy, State.StateRevision, new AuditRecord {
                ActionType = AuditActionType.ProjectStateUpdated, Module = State.SelectedModule,
                ModelFingerprint = State.Model?.ProtectedBaselineFingerprint, ScenarioId = State.ScenarioAlternativeState?.SelectedScenarioId });
            Dirty = false;
        }
        internal void MarkEngineeringDirty() { Dirty = true; }
        internal void AcceptEngineeringSave(ProjectState saved) { State = saved; Dirty = false; }
        // Synchronizes the shared workspace with the revision the application's own StormFlow save just
        // accepted. Deliberately narrow and explicitly validated: only the fields that save owns are
        // adopted, so unsaved GravityFlow edits, the attached read-only network context, unsaved model
        // metadata and the dirty flag are preserved rather than replaced by a freshly loaded disk state.
        // A state that is not this application's own successful StormFlow save — a different project
        // identity, a revision that moved by more than the single step this save advanced, a missing
        // StormFlow reference, or a changed GravityFlow-owned field — fails closed and leaves the open
        // workspace untouched, so an unrelated external project change is never absorbed silently.
        internal void AcceptStormFlowSave(ProjectState accepted)
        {
            if (accepted == null)
                throw new ProjectStorageException("PROJECT_STATE_CONFLICT", "The StormFlow save returned no accepted project state.");
            if (accepted.ProjectId != State.ProjectId)
                throw new ProjectStorageException("PROJECT_STATE_CONFLICT", "The stored project identity changed; the open workspace will not adopt it.");
            if (!StormFlowDesignBasisWorkspace.IsStormFlowReference(accepted.StormFlowDesignBasisReference?.ReferenceIdentifier))
                throw new ProjectStorageException("PROJECT_STATE_CONFLICT", "The accepted state carries no StormFlow Design Basis reference.");
            if (accepted.StateRevision != State.StateRevision + 1)
                throw new ProjectStorageException("PROJECT_STATE_CONFLICT", "The project revision moved by more than this StormFlow save; reopen the project before saving again.");
            if (!SameDesignBasisReference(accepted.DesignBasisReference, State.DesignBasisReference) ||
                accepted.EngineeringDecisionReferenceId != State.EngineeringDecisionReferenceId ||
                accepted.CurrentDesignRunId != State.CurrentDesignRunId)
                throw new ProjectStorageException("PROJECT_STATE_CONFLICT", "The accepted StormFlow state changed GravityFlow-owned project fields; it will not be adopted.");
            State.StateRevision = accepted.StateRevision;
            State.StormFlowDesignBasisReference = accepted.StormFlowDesignBasisReference;
            State.SelectedModule = accepted.SelectedModule;
            State.UpdatedUtc = accepted.UpdatedUtc;
        }
        private static bool SameDesignBasisReference(DesignBasisReference left, DesignBasisReference right) =>
            left?.ReferenceIdentifier == right?.ReferenceIdentifier && left?.DesignBasisVersion == right?.DesignBasisVersion;
    }
}
