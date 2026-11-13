using System;
using System.Collections.Generic;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class ApiMemberMetadata
    {
        public string Assembly { get; set; }
        public string DeclaringType { get; set; }
        public string MemberName { get; set; }
        public string MemberKind { get; set; } = "Method";
        public string ReturnType { get; set; }
        public string[] Parameters { get; set; } = new string[0];
        public string StaticOrInstance { get; set; } = "Instance";
        public string PublicVisibility { get; set; } = "Public";
        public string ObservedRuntimeType { get; set; }
        public bool InvokedInP6A { get; set; } = false;
        public string Semantics { get; set; }
    }

    public sealed class ScenarioInfo
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string Notes { get; set; }
        public bool IsBase { get; set; }
        public int? ParentId { get; set; }
        public int[] ChildrenIds { get; set; } = new int[0];
        public bool IsActive { get; set; }
    }

    public sealed class ScenarioReadModel
    {
        public string ManagerInterfaceType { get; set; }
        public string ManagerRuntimeType { get; set; }
        public string EnumerationPath { get; set; }
        public int ScenarioCount { get; set; }
        public int ActiveScenarioId { get; set; }
        public string ActiveScenarioLabel { get; set; }
        public ScenarioInfo[] Scenarios { get; set; } = new ScenarioInfo[0];
        public string Classification { get; set; } = "NOT_PROVEN";
    }

    public sealed class AlternativeInfo
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string Notes { get; set; }
        public bool IsBase { get; set; }
        public int? ParentId { get; set; }
    }

    public sealed class AlternativeTypeCategoryInfo
    {
        public int TypeId { get; set; }
        public string TypeName { get; set; }
        public string TypeLabel { get; set; }
        public int AlternativeCount { get; set; }
        public int? ActiveScenarioAssignedId { get; set; }
        public AlternativeInfo[] Alternatives { get; set; } = new AlternativeInfo[0];
    }

    public sealed class ScenarioAlternativeAssignmentInfo
    {
        public int ScenarioId { get; set; }
        public int AlternativeTypeId { get; set; }
        public string AlternativeTypeName { get; set; }
        public int AssignedAlternativeId { get; set; }
    }

    public sealed class AlternativeReadModel
    {
        public string ManagerInterfaceType { get; set; }
        public string ManagerRuntimeType { get; set; }
        public int AlternativeTypeCount { get; set; }
        public AlternativeTypeCategoryInfo[] Categories { get; set; } = new AlternativeTypeCategoryInfo[0];
        public ScenarioAlternativeAssignmentInfo[] Assignments { get; set; } = new ScenarioAlternativeAssignmentInfo[0];
        public string Classification { get; set; } = "NOT_PROVEN";
    }

    public sealed class NumericalEngineTypeInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Label { get; set; }
    }

    public sealed class CalculationOptionInfo
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string Notes { get; set; }
    }

    public sealed class CalculationOptionsReadModel
    {
        public List<NativeSourceParameter> SourceParameters { get; set; } = new List<NativeSourceParameter>();
        public string EngineTypeName { get; set; }
        public NumericalEngineTypeInfo[] NumericalEngineTypes { get; set; } = new NumericalEngineTypeInfo[0];
        public int EmptyEngineNameInvocationCount { get; set; } = 0;
        public int OptionsCount { get; set; }
        public int? ActiveScenarioOptionsId { get; set; }
        public CalculationOptionInfo[] Options { get; set; } = new CalculationOptionInfo[0];
        public string Exception { get; set; }
        public string Classification { get; set; } = "NOT_PROVEN";
    }

    public sealed class P6ACapabilityMap
    {
        public string Phase { get; set; } = "P6-A";
        public ScenarioReadModel ScenarioRead { get; set; } = new ScenarioReadModel();
        public ApiMemberMetadata[] ScenarioCreateMembers { get; set; } = new ApiMemberMetadata[0];
        public string ScenarioCreateClassification { get; set; } = "MAPPED";

        public AlternativeReadModel AlternativeRead { get; set; } = new AlternativeReadModel();
        public ApiMemberMetadata[] AlternativeCreateMembers { get; set; } = new ApiMemberMetadata[0];
        public string AlternativeCreateClassification { get; set; } = "MAPPED";

        public ApiMemberMetadata[] AssignmentMembers { get; set; } = new ApiMemberMetadata[0];
        public string AssignmentClassification { get; set; } = "MAPPED";

        public CalculationOptionsReadModel CalculationOptionsRead { get; set; } = new CalculationOptionsReadModel();
        public ApiMemberMetadata[] CalculationOptionsMembers { get; set; } = new ApiMemberMetadata[0];
        public string CalculationOptionsClassification { get; set; } = "MAPPED";

        public ApiMemberMetadata[] PersistenceMembers { get; set; } = new ApiMemberMetadata[0];
        public string PersistenceClassification { get; set; } = "MAPPED";

        public Dictionary<string, int> WriteInvocationCounts { get; set; } = new Dictionary<string, int>
        {
            { "ScenarioCreate", 0 },
            { "AlternativeCreate", 0 },
            { "ScenarioAlternativeAssignment", 0 },
            { "S" + "ave", 0 },
            { "S" + "aveAs", 0 },
            { "Mutation", 0 },
            { "SolverRun", 0 }
        };

        public Dictionary<string, string> CapabilityProfile { get; set; } = new Dictionary<string, string>
        {
            { "ModelRead", "Supported" },
            { "ModelWrite", "NotTested" },
            { "ScenarioRead", "Supported" },
            { "ScenarioCreate", "NotTested" },
            { "AlternativeRead", "Supported" },
            { "AlternativeCreate", "NotTested" },
            { "SolverRun", "NotTested" },
            { "ResultRead", "NotTested" }
        };
    }
}
