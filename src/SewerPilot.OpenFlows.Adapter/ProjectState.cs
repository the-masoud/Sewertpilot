using System;
using System.Collections.Generic;
using System.Globalization;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum ProjectModule
    {
        Unselected = 0,
        StormFlow = 1,
        GravityFlow = 2
    }

    public sealed class ProjectState
    {
        public int SchemaVersion { get; set; } = 1;
        public string ProjectId { get; set; }
        public string ProjectName { get; set; }
        public int StateRevision { get; set; } = 1;
        public string SelectedModule { get; set; } = "Unselected";

        public ProjectModelMetadata Model { get; set; }
        public ProjectRuntimeMetadata Runtime { get; set; }
        public ProjectScenarioAlternativeState ScenarioAlternativeState { get; set; }

        public DesignBasisReference DesignBasisReference { get; set; }
        // StormFlow Design Basis reference. Additive and optional: schema-version-1 projects written before
        // StormFlow persistence existed simply omit it, and it is never required to be present. It is kept
        // separate from DesignBasisReference so GravityFlow and StormFlow coexist in one project without
        // overwriting each other's persisted Design Basis reference.
        public DesignBasisReference StormFlowDesignBasisReference { get; set; }
        public string CurrentDesignRunId { get; set; }
        public string EngineeringDecisionReferenceId { get; set; }
        public ProjectStandardsReference StandardsReference { get; set; }

        public string CreatedUtc { get; set; }
        public string UpdatedUtc { get; set; }

        public static ProjectModule ParseModule(string value)
        {
            if (string.Equals(value, "StormFlow", StringComparison.Ordinal))
                return ProjectModule.StormFlow;
            if (string.Equals(value, "GravityFlow", StringComparison.Ordinal))
                return ProjectModule.GravityFlow;
            if (string.Equals(value, "Unselected", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(value))
                return ProjectModule.Unselected;

            throw new ArgumentException("Unknown or invalid module value: " + value, nameof(value));
        }

        public static string FormatModule(ProjectModule module)
        {
            switch (module)
            {
                case ProjectModule.StormFlow: return "StormFlow";
                case ProjectModule.GravityFlow: return "GravityFlow";
                default: return "Unselected";
            }
        }
    }

    public sealed class ProjectModelMetadata
    {
        public string ProtectedBaselineFingerprint { get; set; }
        public string PrimaryModelDisplayName { get; set; }
        public string WorkingCopyId { get; set; }
        public string WorkingCopyFingerprint { get; set; }
        public string BaselineLocationMetadata { get; set; }
    }

    public sealed class ProjectRuntimeMetadata
    {
        public string Product { get; set; }
        public string Version { get; set; }
        public string Build { get; set; }
        public string Architecture { get; set; }
        public string RuntimeCandidateId { get; set; }
        public Dictionary<string, string> CapabilityStates { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public string PrimaryProductRoot { get; set; }
    }

    public sealed class ProjectScenarioAlternativeState
    {
        public int? SelectedScenarioId { get; set; }
        // Optional in schema 1: legacy project files deserialize this as null.
        public int? SelectedPhysicalAlternativeId { get; set; }
        public string ScenarioLabel { get; set; }
        public List<ProjectScenarioMapping> ScenarioMappings { get; set; } = new List<ProjectScenarioMapping>();
        public List<ProjectAlternativeMapping> AlternativeMappings { get; set; } = new List<ProjectAlternativeMapping>();
    }

    public sealed class ProjectScenarioMapping
    {
        public int ScenarioId { get; set; }
        public string ScenarioLabel { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class ProjectAlternativeMapping
    {
        public int AlternativeTypeId { get; set; }
        public string AlternativeTypeName { get; set; }
        public int AlternativeId { get; set; }
        public string AlternativeLabel { get; set; }
    }

    public sealed class DesignBasisReference
    {
        public string DesignBasisVersion { get; set; }
        public string ReferenceIdentifier { get; set; }
    }

    public sealed class ProjectStandardsReference
    {
        public string PublishedStandardId { get; set; }
        public string CompanyStandardId { get; set; }
        public string ProjectOverrideId { get; set; }
    }
}
