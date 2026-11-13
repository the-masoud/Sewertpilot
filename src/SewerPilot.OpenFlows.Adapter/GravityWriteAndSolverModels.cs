using System;
using System.Collections.Generic;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class ConduitInvertWriteTarget
    {
        public ConduitEndKind End { get; set; }
        public string TargetFieldName { get; set; }
        public string DeclaringType { get; set; }
        public string RuntimeType { get; set; }
        public string SetterMemberName { get; set; }
        public string[] SetterParameterTypes { get; set; }
        public string ValueTypeName { get; set; }
        public string ExpectedUnit { get; set; }
        public string TargetAlternativeType { get; set; }
        public int TargetAlternativeTypeId { get; set; }
        public bool IsWritable { get; set; }
        public string ValueWriteSemantics { get; set; }
        public string Provenance { get; set; }

        public ConduitInvertWriteTarget() { }

        public ConduitInvertWriteTarget(
            ConduitEndKind end,
            string targetFieldName,
            string declaringType,
            string runtimeType,
            string setterMemberName,
            string[] setterParameterTypes,
            string valueTypeName,
            string expectedUnit,
            string targetAlternativeType,
            int targetAlternativeTypeId,
            bool isWritable,
            string valueWriteSemantics,
            string provenance)
        {
            End = end;
            TargetFieldName = targetFieldName;
            DeclaringType = declaringType;
            RuntimeType = runtimeType;
            SetterMemberName = setterMemberName;
            SetterParameterTypes = setterParameterTypes ?? new string[0];
            ValueTypeName = valueTypeName;
            ExpectedUnit = expectedUnit;
            TargetAlternativeType = targetAlternativeType;
            TargetAlternativeTypeId = targetAlternativeTypeId;
            IsWritable = isWritable;
            ValueWriteSemantics = valueWriteSemantics;
            Provenance = provenance;
        }
    }

    public sealed class CandidateToFieldMapping
    {
        public ConduitInvertWriteTarget StartTarget { get; set; }
        public ConduitInvertWriteTarget StopTarget { get; set; }
        public string Units { get; set; }

        public CandidateToFieldMapping() { }

        public CandidateToFieldMapping(ConduitInvertWriteTarget startTarget, ConduitInvertWriteTarget stopTarget, string units)
        {
            StartTarget = startTarget;
            StopTarget = stopTarget;
            Units = units;
        }

        public string GetTargetField(ConduitEndKind end)
        {
            if (end == ConduitEndKind.Start) return StartTarget?.TargetFieldName ?? "Physical_UpstreamInvert";
            if (end == ConduitEndKind.Stop) return StopTarget?.TargetFieldName ?? "Physical_DownstreamInvert";
            throw new ArgumentOutOfRangeException(nameof(end));
        }
    }

    public sealed class PersistenceCapabilityDescriptor
    {
        public string Assembly { get; set; }
        public string DeclaringType { get; set; }
        public string RuntimeType { get; set; }
        public string MemberName { get; set; }
        public string MemberKind { get; set; }
        public string ReturnType { get; set; }
        public string[] ParameterTypes { get; set; }
        public string PersistenceScope { get; set; }
        public string Classification { get; set; }

        public PersistenceCapabilityDescriptor() { }

        public PersistenceCapabilityDescriptor(
            string assembly,
            string declaringType,
            string runtimeType,
            string memberName,
            string memberKind,
            string returnType,
            string[] parameterTypes,
            string persistenceScope,
            string classification)
        {
            Assembly = assembly;
            DeclaringType = declaringType;
            RuntimeType = runtimeType;
            MemberName = memberName;
            MemberKind = memberKind;
            ReturnType = returnType;
            ParameterTypes = parameterTypes ?? new string[0];
            PersistenceScope = persistenceScope;
            Classification = classification;
        }
    }

    public sealed class CalculationOptionsContextDescriptor
    {
        public int ActiveScenarioOptionsId { get; set; }
        public string EngineTypeName { get; set; }
        public int NumericalEngineTypeId { get; set; }
        public int ScenarioId { get; set; }
        public string ScenarioRelationship { get; set; }
        public string Classification { get; set; }

        public CalculationOptionsContextDescriptor() { }

        public CalculationOptionsContextDescriptor(
            int activeScenarioOptionsId,
            string engineTypeName,
            int numericalEngineTypeId,
            int scenarioId,
            string scenarioRelationship,
            string classification)
        {
            ActiveScenarioOptionsId = activeScenarioOptionsId;
            EngineTypeName = engineTypeName;
            NumericalEngineTypeId = numericalEngineTypeId;
            ScenarioId = scenarioId;
            ScenarioRelationship = scenarioRelationship;
            Classification = classification;
        }
    }

    public sealed class SolverCapabilityDescriptor
    {
        public string Assembly { get; set; }
        public string DeclaringType { get; set; }
        public string RuntimeType { get; set; }
        public string MemberName { get; set; }
        public string MemberKind { get; set; }
        public string ReturnType { get; set; }
        public string[] ParameterTypes { get; set; }
        public string ScenarioArgumentSemantics { get; set; }
        public bool IsSynchronous { get; set; }
        public string Classification { get; set; }

        public SolverCapabilityDescriptor() { }

        public SolverCapabilityDescriptor(
            string assembly,
            string declaringType,
            string runtimeType,
            string memberName,
            string memberKind,
            string returnType,
            string[] parameterTypes,
            string scenarioArgumentSemantics,
            bool isSynchronous,
            string classification)
        {
            Assembly = assembly;
            DeclaringType = declaringType;
            RuntimeType = runtimeType;
            MemberName = memberName;
            MemberKind = memberKind;
            ReturnType = returnType;
            ParameterTypes = parameterTypes ?? new string[0];
            ScenarioArgumentSemantics = scenarioArgumentSemantics;
            IsSynchronous = isSynchronous;
            Classification = classification;
        }
    }

    public sealed class SolverCompletionStatusDescriptor
    {
        public string StatusMechanism { get; set; }
        public string Assembly { get; set; }
        public string DeclaringType { get; set; }
        public string ReturnType { get; set; }
        public string SuccessValue { get; set; }
        public string WarningValue { get; set; }
        public string ErrorValue { get; set; }
        public string ComputeNeededValue { get; set; }
        public string StatusTextMethod { get; set; }
        public string Classification { get; set; }

        public SolverCompletionStatusDescriptor() { }

        public SolverCompletionStatusDescriptor(
            string statusMechanism,
            string assembly,
            string declaringType,
            string returnType,
            string successValue,
            string warningValue,
            string errorValue,
            string computeNeededValue,
            string statusTextMethod,
            string classification)
        {
            StatusMechanism = statusMechanism;
            Assembly = assembly;
            DeclaringType = declaringType;
            ReturnType = returnType;
            SuccessValue = successValue;
            WarningValue = warningValue;
            ErrorValue = errorValue;
            ComputeNeededValue = computeNeededValue;
            StatusTextMethod = statusTextMethod;
            Classification = classification;
        }
    }

    public sealed class P15BMutationPlan
    {
        public List<string> ExecutionSteps { get; set; } = new List<string>();
        public string AcceptancePolicy { get; set; }
        public bool SaveOrderedBeforeSolver { get; set; }
        public bool TargetsIsolatedPhysicalAlternative { get; set; }
        public bool ReopenVerificationPlanned { get; set; }
        public bool BaselineIntegrityPlanned { get; set; }

        public P15BMutationPlan()
        {
            ExecutionSteps.AddRange(new[]
            {
                "1. Create fresh P5 working copy",
                "2. Open model in working copy",
                "3. Create/copy isolated scenario",
                "4. Create/copy Physical alternative",
                "5. Assign Physical alternative to isolated scenario",
                "6. Apply candidate Start/Stop invert writes to active Physical alternative",
                "7. SaveCurrentProject to persist candidate values",
                "8. Select isolated scenario as active scenario",
                "9. Execute calculation for isolated scenario",
                "10. Capture calculation completion status",
                "11. Cleanup and close",
                "12. Reopen to verify persisted candidate values",
                "13. Verify baseline unchanged"
            });
            AcceptancePolicy = "Use one explicitly constructed TEST-ONLY candidate in a disposable working copy / isolated Physical alternative; never touches protected baseline.";
            SaveOrderedBeforeSolver = true;
            TargetsIsolatedPhysicalAlternative = true;
            ReopenVerificationPlanned = true;
            BaselineIntegrityPlanned = true;
        }
    }

    public sealed class P15ACapabilityMappingResult
    {
        public int SchemaVersion { get; set; } = 1;
        public ConduitInvertWriteTarget StartInvertTarget { get; set; }
        public ConduitInvertWriteTarget StopInvertTarget { get; set; }
        public CandidateToFieldMapping CandidateMapping { get; set; }
        public PersistenceCapabilityDescriptor Persistence { get; set; }
        public CalculationOptionsContextDescriptor CalculationOptions { get; set; }
        public SolverCapabilityDescriptor Solver { get; set; }
        public SolverCompletionStatusDescriptor CompletionStatus { get; set; }
        public P15BMutationPlan ExecutionPlan { get; set; }

        public int PropertySetCount { get; set; }
        public int SaveCount { get; set; }
        public int SaveAsCount { get; set; }
        public int ScenarioCreateCount { get; set; }
        public int AlternativeCreateCount { get; set; }
        public int SolverRunCount { get; set; }
        public bool Success { get; set; }

        public P15ACapabilityMappingResult()
        {
            ExecutionPlan = new P15BMutationPlan();
        }
    }

    public sealed class AppliedEndpointChange
    {
        public int ConduitId { get; set; }
        public string End { get; set; }
        public double OriginalValue { get; set; }
        public double AppliedValue { get; set; }
        public string Unit { get; set; }
        public string SourceField { get; set; }

        public AppliedEndpointChange() { }

        public AppliedEndpointChange(int conduitId, string end, double originalValue, double appliedValue, string unit, string sourceField)
        {
            ConduitId = conduitId;
            End = end;
            OriginalValue = originalValue;
            AppliedValue = appliedValue;
            Unit = unit;
            SourceField = sourceField;
        }
    }

    public sealed class GravitySolverExecutionResult
    {
        public string CandidateId { get; set; }
        public string CandidateFingerprint { get; set; }

        public int SourceScenarioId { get; set; }
        public int ExecutionScenarioId { get; set; }

        public int SourcePhysicalAlternativeId { get; set; }
        public int ExecutionPhysicalAlternativeId { get; set; }

        public List<AppliedEndpointChange> AppliedEndpointChanges { get; set; } = new List<AppliedEndpointChange>();

        public bool SaveCompleted { get; set; }
        public string SolverExecutionStatus { get; set; }
        public string ScenarioComputeStatus { get; set; }
        public string ScenarioStatusText { get; set; }

        public bool PersistenceVerified { get; set; }
        public bool SourceIsolationVerified { get; set; }

        public GravitySolverExecutionResult() { }
    }
}
