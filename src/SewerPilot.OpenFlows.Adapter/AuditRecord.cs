using System;
using System.Collections.Generic;

namespace SewerPilot.OpenFlows.Adapter
{
    public static class AuditActionType
    {
        public const string ProjectCreated = "ProjectCreated";
        public const string ProjectOpened = "ProjectOpened";
        public const string ProjectStateUpdated = "ProjectStateUpdated";
        public const string ModelSelected = "ModelSelected";
        public const string ScenarioMappingUpdated = "ScenarioMappingUpdated";
        public const string DesignRunRegistered = "DesignRunRegistered";
        public const string EngineeringDecisionRegistered = "EngineeringDecisionRegistered";
        public const string EngineeringDecisionApproved = "EngineeringDecisionApproved";
        public const string EngineeringDecisionRevoked = "EngineeringDecisionRevoked";

        private static readonly HashSet<string> ValidActions = new HashSet<string>(StringComparer.Ordinal)
        {
            ProjectCreated,
            ProjectOpened,
            ProjectStateUpdated,
            ModelSelected,
            ScenarioMappingUpdated,
            DesignRunRegistered,
            EngineeringDecisionRegistered,
            EngineeringDecisionApproved,
            EngineeringDecisionRevoked
        };

        public static bool IsValid(string action)
        {
            return action != null && ValidActions.Contains(action);
        }
    }

    public sealed class AuditRecord
    {
        public string AuditRecordId { get; set; }
        public long Sequence { get; set; }
        public string TimestampUtc { get; set; }
        public string ProjectId { get; set; }
        public string ActionType { get; set; }
        public string Module { get; set; }
        public string ModelFingerprint { get; set; }
        public string WorkingCopyId { get; set; }
        public int? ScenarioId { get; set; }
        public string DesignRunId { get; set; }
        public string CorrelationId { get; set; }
        public Dictionary<string, string> Payload { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
