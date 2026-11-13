using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace SewerPilot.OpenFlows.Adapter
{
    // Additive projection boundary. Never returns a workspace, criteria, deserializer or write operation.
    public static class ReadOnlyDesignBasisInspection
    {
        public static StoredDesignArtifactReference Locate(ProjectState state, string module)
        {
            RequireModule(module);
            if (state == null) throw new ArgumentNullException(nameof(state));
            string reference;
            string folder;
            if (module == "GravityFlow")
            {
                reference = state.EngineeringDecisionReferenceId;
                folder = "engineering";
                if (string.IsNullOrWhiteSpace(reference))
                {
                    if (state.DesignBasisReference != null && !StormFlowDesignBasisWorkspace.IsStormFlowReference(state.DesignBasisReference.ReferenceIdentifier))
                        throw Error("ENGINEERING_ARTIFACT_REFERENCE_MISSING");
                    return null;
                }
                if (!Regex.IsMatch(reference, "^ui02-[a-f0-9]{64}$")) throw Error("ENGINEERING_REFERENCE_INVALID");
            }
            else
            {
                reference = state.StormFlowDesignBasisReference?.ReferenceIdentifier;
                folder = "stormflow";
                if (string.IsNullOrWhiteSpace(reference) && StormFlowDesignBasisWorkspace.IsStormFlowReference(state.DesignBasisReference?.ReferenceIdentifier))
                    reference = state.DesignBasisReference.ReferenceIdentifier;
                if (string.IsNullOrWhiteSpace(reference))
                {
                    if (state.StormFlowDesignBasisReference != null) throw Error("STORMS_REFERENCE_INVALID");
                    return null;
                }
                if (!Regex.IsMatch(reference, "^sf-[a-f0-9]{64}$")) throw Error("STORMS_REFERENCE_INVALID");
            }
            return new StoredDesignArtifactReference(folder, reference);
        }

        public static StoredDesignBasisSummary Read(string directory, ProjectState state, string module)
        {
            StoredDesignArtifactReference artifactReference = Locate(state, module);
            SafeIdentifier(state.ProjectId);
            if (artifactReference == null) return new StoredDesignBasisSummary
            {
                ProjectId = state.ProjectId, Module = module, StorageStatus = "NOT_SAVED",
                StructuralValidationStatus = "NOT_AVAILABLE", Sections = Array.AsReadOnly(new StoredDesignSectionSummary[0])
            };
            DesignBasis common;
            DesignBasisReference reference;
            DesignBasisValidationResult validation;
            IEnumerable<StoredDesignSectionSummary> sections;
            string networkFingerprint = null;
            if (module == "GravityFlow")
            {
                // The accepted reader verifies immutable hash, project/basis/model/network/constraint lineage.
                var artifact = EngineeringDesignStorage.Load(directory, state);
                if (artifact == null) throw Error("ENGINEERING_ARTIFACT_REFERENCE_MISSING");
                var basis = artifact.Inputs.DesignBasis;
                common = basis.CommonDesignBasis;
                reference = common.ToProjectReference(); // Also preserves the authoritative legacy recovery path.
                validation = basis.Validate();
                sections = basis.GetAllSectionCompleteness().Select(s => new StoredDesignSectionSummary
                { Name = s.SectionId.ToString(), DefinedCriterionCount = s.DefinedCriterionCount, UndefinedCriterionCount = s.UndefinedCriterionCount, ValidationIssueCount = s.ValidationIssueCount });
                networkFingerprint = string.IsNullOrWhiteSpace(artifact.NetworkFingerprint) ? null : artifact.NetworkFingerprint;
            }
            else
            {
                // Open/LoadBasis only read project/audit/artifact; no editing/save methods are exposed or called.
                var basis = StormFlowDesignBasisWorkspace.Open(directory).Basis;
                if (basis == null) throw Error("STORMS_BASIS_MISSING");
                common = basis.CommonDesignBasis;
                validation = basis.Validate();
                if (!validation.IsValid) throw Error("DESIGN_BASIS_STRUCTURALLY_INVALID");
                var stored = state.StormFlowDesignBasisReference ?? state.DesignBasisReference;
                // The existing loader checks hash/project/module. Close its missing version-consistency boundary.
                if (stored?.DesignBasisVersion != common.DesignBasisVersion) throw Error("STORMS_REFERENCE_MISMATCH");
                reference = new DesignBasisReference { ReferenceIdentifier = artifactReference.ReferenceIdentifier, DesignBasisVersion = common.DesignBasisVersion };
                sections = basis.GetAllSectionCompleteness().Select(s => new StoredDesignSectionSummary
                { Name = s.SectionId.ToString(), DefinedCriterionCount = s.DefinedCriterionCount, UndefinedCriterionCount = s.UndefinedCriterionCount, ValidationIssueCount = s.ValidationIssueCount });
            }
            if (!validation.IsValid) throw Error("DESIGN_BASIS_STRUCTURALLY_INVALID");
            SafeIdentifier(reference.ReferenceIdentifier); SafeIdentifier(reference.DesignBasisVersion);
            return new StoredDesignBasisSummary
            {
                ProjectId = state.ProjectId, Module = module, StorageStatus = "STORED",
                ReferenceIdentifier = reference.ReferenceIdentifier, DesignBasisVersion = reference.DesignBasisVersion,
                ReviewState = common.ReviewState.ToString(), StructuralValidationStatus = "VALID",
                ValidationErrorCount = validation.Issues.Count(i => i.Severity == DesignBasisValidationSeverity.Error),
                ValidationWarningCount = validation.Issues.Count(i => i.Severity == DesignBasisValidationSeverity.Warning),
                Sections = Array.AsReadOnly(sections.ToArray()), StoredNetworkFingerprint = networkFingerprint
            };
        }

        private static void RequireModule(string module)
        { if (module != "GravityFlow" && module != "StormFlow") throw Error("DESIGN_BASIS_MODULE_UNSUPPORTED"); }
        private static void SafeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(c => char.IsControl(c) || c == '\\' || c == '/'))
                throw Error("DESIGN_BASIS_SUMMARY_IDENTITY_INVALID");
        }
        private static ProjectStorageException Error(string classification) => new ProjectStorageException(classification, "Stored Design Basis inspection rejected.");
    }

    public sealed class StoredDesignArtifactReference
    {
        public string FolderName { get; }
        public string ReferenceIdentifier { get; }
        internal StoredDesignArtifactReference(string folder, string reference) { FolderName = folder; ReferenceIdentifier = reference; }
    }
    public sealed class StoredDesignBasisSummary
    {
        public string Kind => "STORED_DESIGN_BASIS_SUMMARY";
        public string ProjectId { get; internal set; }
        public string Module { get; internal set; }
        public string StorageStatus { get; internal set; }
        public string ReferenceIdentifier { get; internal set; }
        public string DesignBasisVersion { get; internal set; }
        public string ReviewState { get; internal set; }
        public string StructuralValidationStatus { get; internal set; }
        public int? ValidationErrorCount { get; internal set; }
        public int? ValidationWarningCount { get; internal set; }
        public ReadOnlyCollection<StoredDesignSectionSummary> Sections { get; internal set; }
        public string StoredNetworkFingerprint { get; internal set; }
    }
    public sealed class StoredDesignSectionSummary
    {
        public string Name { get; internal set; }
        public int DefinedCriterionCount { get; internal set; }
        public int UndefinedCriterionCount { get; internal set; }
        public int ValidationIssueCount { get; internal set; }
    }
}
