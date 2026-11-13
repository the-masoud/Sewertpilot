using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    // P18 minimal application integration: create, edit, validate, review, save and reopen a StormFlow
    // Design Basis against the existing project directory and project store. Offline only; no execution,
    // no approval, no Bentley runtime and no second project store.
    internal sealed class StormFlowDesignBasisWorkspace
    {
        private const string ReferencePrefix = "sf-";
        private readonly string directoryPath;

        // Identifies a StormFlow immutable content reference. Used both to validate StormFlow references and
        // to detect the legacy layout in which a StormFlow save had overwritten the shared reference.
        internal static bool IsStormFlowReference(string reference) =>
            !string.IsNullOrWhiteSpace(reference) && reference.StartsWith(ReferencePrefix, StringComparison.Ordinal);

        internal string ProjectId { get; private set; }
        internal string ProjectDirectory => directoryPath;
        internal StormFlowDesignBasis Basis { get; private set; }
        internal bool Dirty { get; private set; }

        private StormFlowDesignBasisWorkspace(string directory, string projectId, StormFlowDesignBasis basis)
        {
            directoryPath = directory;
            ProjectId = projectId;
            Basis = basis;
        }

        internal static StormFlowDesignBasisWorkspace Open(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Project directory must be specified.", nameof(directory));
            var path = Path.GetFullPath(directory);
            WorkingCopyIntegrity.RejectLinks(path);
            var state = ProjectStore.LoadProject(path);
            return new StormFlowDesignBasisWorkspace(path, state.ProjectId, LoadBasis(path, state));
        }

        private static string BasisPath(string directory, string reference)
        {
            var root = Path.GetFullPath(directory); WorkingCopyIntegrity.RejectLinks(root);
            var folder = Path.Combine(root, "stormflow"); WorkingCopyIntegrity.RejectLinks(folder);
            var path = Path.Combine(folder, reference + ".json"); WorkingCopyIntegrity.RejectLinks(path);
            return path;
        }

        private static StormFlowDesignBasis LoadBasis(string directory, ProjectState state)
        {
            // Preferred location: the dedicated StormFlow reference, which never collides with GravityFlow.
            var reference = state.StormFlowDesignBasisReference?.ReferenceIdentifier;
            // Legacy compatibility (schema-version-1 projects written before the dedicated field existed,
            // including projects whose shared reference was overwritten by a StormFlow save): the shared
            // reference is consulted only when it is provably a StormFlow reference. A GravityFlow Design
            // Basis identity is never reinterpreted as StormFlow content.
            if (string.IsNullOrWhiteSpace(reference) && IsStormFlowReference(state.DesignBasisReference?.ReferenceIdentifier))
                reference = state.DesignBasisReference.ReferenceIdentifier;
            if (string.IsNullOrWhiteSpace(reference)) return null;
            if (!IsStormFlowReference(reference))
                throw new ProjectStorageException("STORMS_REFERENCE_INVALID", "StormFlow Design Basis reference is malformed.");
            var path = BasisPath(directory, reference);
            if (!File.Exists(path)) throw new ProjectStorageException("STORMS_BASIS_MISSING", "Referenced StormFlow Design Basis content is missing.");
            var json = File.ReadAllText(path, Encoding.UTF8);
            if (ReferencePrefix + Hash(json) != reference)
                throw new ProjectStorageException("STORMS_BASIS_IDENTITY_MISMATCH", "StormFlow Design Basis content does not match its immutable reference.");
            var basis = StormFlowDesignBasisSerializer.Deserialize(json);
            if (basis.CommonDesignBasis == null || basis.CommonDesignBasis.ProjectId != state.ProjectId)
                throw new ProjectStorageException("STORMS_BASIS_IDENTITY_MISMATCH", "StormFlow Design Basis project identity does not match the project.");
            return basis;
        }

        internal void NewDraft(string basisVersion)
        {
            if (string.IsNullOrWhiteSpace(basisVersion)) throw new InvalidOperationException("STORMS_NEW_VERSION_REQUIRED");
            var next = new StormFlowDesignBasis
            {
                CommonDesignBasis = new DesignBasis
                {
                    DesignBasisId = Guid.NewGuid().ToString("D"),
                    DesignBasisVersion = basisVersion,
                    ProjectId = ProjectId
                }
            };
            Basis = next; Dirty = true;
        }

        private void Editable()
        {
            Require(Basis?.CommonDesignBasis != null, "CREATE_DRAFT_FIRST");
            Require(Basis.CommonDesignBasis.ReviewState == DesignBasisReviewState.Draft, "DRAFT_REQUIRED: Reviewed/review-pending content is locked.");
        }

        private static void Require(bool value, string detail)
        {
            if (!value) throw new InvalidOperationException("STORMS_WORKSPACE_" + detail);
        }

        internal void SetIdentity(string id, string basisVersion)
        {
            Editable();
            Require(!string.IsNullOrWhiteSpace(id), "DESIGN_BASIS_ID_REQUIRED");
            Basis.CommonDesignBasis.DesignBasisId = id.Trim();
            Basis.CommonDesignBasis.DesignBasisVersion = basisVersion?.Trim();
            Dirty = true;
        }

        internal void SetCommonMetadata(List<DesignBasisStandardReference> references, List<DesignBasisAssumption> assumptions, List<DesignBasisOverride> overrides)
        {
            Editable();
            var next = StormFlowDesignBasisSerializer.Deserialize(StormFlowDesignBasisSerializer.Serialize(Basis));
            next.CommonDesignBasis.StandardReferences = references ?? new List<DesignBasisStandardReference>();
            next.CommonDesignBasis.Assumptions = assumptions ?? new List<DesignBasisAssumption>();
            next.CommonDesignBasis.Overrides = overrides ?? new List<DesignBasisOverride>();
            var validation = next.CommonDesignBasis.Validate();
            Require(validation.IsValid, "COMMON_METADATA_INVALID: " + string.Join("; ", validation.Issues.Select(i => i.Message)));
            Basis = next; Dirty = true;
        }

        internal void PutCriterion(StormFlowSectionIdentifier section, StormDesignCriterion edited, string existingKey = null)
        {
            Editable();
            Require(edited != null && edited.Section == section, "CRITERION_SECTION_MISMATCH");
            var list = Basis.GetSection(section).Criteria;
            var existing = list.FirstOrDefault(c => c.Key == existingKey);
            Require(existing == null || (edited.Key == existing.Key && edited.Requirement == existing.Requirement), "EXISTING_CRITERION_IDENTITY_REQUIRED");
            var next = StormFlowDesignBasisSerializer.Deserialize(StormFlowDesignBasisSerializer.Serialize(Basis));
            var target = next.GetSection(section).Criteria;
            if (existing != null)
            {
                var index = Basis.GetSection(section).Criteria.IndexOf(existing);
                if (index >= 0 && index < target.Count) target.RemoveAt(index);
            }
            target.Add(edited.Clone());
            var validation = next.Validate();
            Require(!validation.Issues.Any(i => i.Field != null && i.Field.StartsWith(section.ToString(), StringComparison.OrdinalIgnoreCase)),
                "CRITERION_INVALID: " + string.Join("; ", validation.Issues.Where(i => i.Field != null && i.Field.StartsWith(section.ToString(), StringComparison.OrdinalIgnoreCase)).Select(i => i.Message)));
            if (edited.Status == StormCriterionStatus.Defined) GravityExecutionPlanBuilder.ValidateEngineeringProvenance(edited.Provenance);
            Basis = next; Dirty = true;
        }

        internal DesignBasisValidationResult ValidateBasis()
        {
            Require(Basis != null, "CREATE_DRAFT_FIRST");
            var validation = Basis.Validate();
            foreach (var c in Basis.EnumerateSectionsInCanonicalOrder().SelectMany(s => s.Criteria ?? new List<StormDesignCriterion>()))
            {
                if (c == null) continue;
                if (c.Requirement == StormCriterionRequirement.Required && (c.Status != StormCriterionStatus.Defined || c.Value == null))
                    validation.AddError("REQUIRED_CRITERION_UNRESOLVED", c.Section + "." + c.Key, "Required criterion remains undefined.");
                if (c.Status == StormCriterionStatus.Defined)
                {
                    try { GravityExecutionPlanBuilder.ValidateEngineeringProvenance(c.Provenance); }
                    catch (Exception e) { validation.AddError("ENGINEERING_PROVENANCE", c.Section + "." + c.Key, e.Message); }
                }
            }
            return validation;
        }

        internal void Transition(DesignBasisReviewState state, string note)
        {
            Require(!string.IsNullOrWhiteSpace(note), "REVIEW_NOTE_REQUIRED");
            if (state == DesignBasisReviewState.ReadyForReview)
            {
                var validation = ValidateBasis();
                Require(validation.IsValid, "REVIEW_VALIDATION: " + string.Join("; ", validation.Issues.Select(i => i.Message)));
            }
            using (var actor = WindowsIdentity.GetCurrent())
            {
                Require(actor?.User != null && !string.IsNullOrWhiteSpace(actor.Name), "OPERATOR_IDENTITY_UNAVAILABLE");
                Basis.TransitionReviewState(state, actor.Name + " [" + actor.User.Value + "]", note, DateTime.UtcNow);
            }
            Dirty = true;
        }

        // Persists the StormFlow Design Basis and returns the project state the store accepted, so the
        // caller can synchronize the active application workspace with the revision this save advanced.
        internal ProjectState PersistBasis()
        {
            Require(Basis != null && Basis.Validate().IsValid, "DRAFT_STORAGE_VALIDATION: " + string.Join("; ", Basis?.Validate().Issues.Select(i => i.Message) ?? new List<string>()));
            var json = StormFlowDesignBasisSerializer.Serialize(Basis);
            var reference = ReferencePrefix + Hash(json);
            var path = BasisPath(directoryPath, reference);

            var stored = ProjectStore.LoadProject(directoryPath);
            if (stored.ProjectId != ProjectId)
                throw new ProjectStorageException("PROJECT_STATE_CONFLICT", "The stored project identity changed; it will not be overwritten.");

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            if (File.Exists(path))
            {
                if (File.ReadAllText(path, Encoding.UTF8) != json)
                    throw new ProjectStorageException("STORMS_BASIS_IDENTITY_MISMATCH", "Immutable StormFlow Design Basis content must not be overwritten.");
            }
            else
            {
                var stage = path + "." + Guid.NewGuid().ToString("N") + ".staging";
                try
                {
                    var data = new UTF8Encoding(false).GetBytes(json);
                    using (var file = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(data, 0, data.Length); file.Flush(true); }
                    File.Move(stage, path);
                }
                finally { if (File.Exists(stage)) File.Delete(stage); }
            }

            // Artifact is durable before the project state can claim it.
            LoadBasis(directoryPath, new ProjectState { ProjectId = ProjectId, StormFlowDesignBasisReference = new DesignBasisReference { ReferenceIdentifier = reference } });
            // The copy is taken from the freshly stored state so a concurrent module save is either
            // preserved here or rejected by the revision check below — never silently overwritten.
            var copy = RuntimeHostProtocol.Serializer().Deserialize<ProjectState>(RuntimeHostProtocol.Serializer().Serialize(stored));
            copy.SelectedModule = ProjectState.FormatModule(ProjectModule.StormFlow);
            // Only the dedicated StormFlow reference is written. The GravityFlow DesignBasisReference and
            // EngineeringDecisionReferenceId are carried through untouched so both modules coexist.
            copy.StormFlowDesignBasisReference = new DesignBasisReference { ReferenceIdentifier = reference, DesignBasisVersion = Basis.CommonDesignBasis.DesignBasisVersion };
            var accepted = ProjectStore.SaveProject(directoryPath, copy, stored.StateRevision, new AuditRecord
            {
                ActionType = AuditActionType.ProjectStateUpdated,
                Module = ProjectState.FormatModule(ProjectModule.StormFlow),
                ModelFingerprint = copy.Model?.ProtectedBaselineFingerprint,
                ScenarioId = copy.ScenarioAlternativeState?.SelectedScenarioId,
                Payload = new Dictionary<string, string> { { "StormFlowDesignBasis", reference }, { "DesignBasisVersion", Basis.CommonDesignBasis.DesignBasisVersion } }
            });
            Dirty = false;
            return accepted;
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }
}
