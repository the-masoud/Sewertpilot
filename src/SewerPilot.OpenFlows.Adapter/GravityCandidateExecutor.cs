using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public static partial class GravityCandidateExecutor
    {
        public static void ValidateLineage(
            GravityDesignCandidate candidate,
            string expectedNetworkFingerprint,
            string expectedConstraintFingerprint)
        {
            if (candidate == null)
                throw new InvalidOperationException("P15_B_BLOCKED_CANDIDATE_LINEAGE: Candidate is null.");

            if (string.IsNullOrWhiteSpace(candidate.SourceNetworkFingerprint) ||
                !string.Equals(candidate.SourceNetworkFingerprint, expectedNetworkFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"P15_B_BLOCKED_CANDIDATE_LINEAGE: Candidate SourceNetworkFingerprint '{candidate.SourceNetworkFingerprint}' does not match expected '{expectedNetworkFingerprint}'.");
            }

            if (string.IsNullOrWhiteSpace(candidate.VerticalConstraintFingerprint) ||
                !string.Equals(candidate.VerticalConstraintFingerprint, expectedConstraintFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"P15_B_BLOCKED_CANDIDATE_LINEAGE: Candidate VerticalConstraintFingerprint '{candidate.VerticalConstraintFingerprint}' does not match expected '{expectedConstraintFingerprint}'.");
            }
        }

        public static void ValidateWorkingCopyContext(
            string baselinePath,
            string targetWorkingPath,
            WriteAuthorizationToken token)
        {
            if (string.IsNullOrWhiteSpace(baselinePath))
                throw new ArgumentNullException(nameof(baselinePath));
            if (string.IsNullOrWhiteSpace(targetWorkingPath))
                throw new ArgumentNullException(nameof(targetWorkingPath));

            string fullBaseline = Path.GetFullPath(baselinePath);
            string fullTarget = Path.GetFullPath(targetWorkingPath);

            if (string.Equals(fullBaseline, fullTarget, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("HARD_FAIL_PROTECTED_BASELINE_MODIFIED: Baseline path cannot be an execution target.");
            }

            if (token == null)
            {
                throw new InvalidOperationException("P15_B_BLOCKED_WRITE_AUTHORIZATION: Token is null.");
            }

            if (!string.Equals(Path.GetFullPath(token.TargetWorkingPath), fullTarget, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("P15_B_BLOCKED_WRITE_AUTHORIZATION: Token target path mismatch.");
            }

            if (!string.Equals(token.AuthorizedOperationScope, "GravityWriteAndSolver", StringComparison.Ordinal) &&
                !string.Equals(token.AuthorizedOperationScope, "ScenarioAlternativeIsolation", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("P15_B_BLOCKED_WRITE_AUTHORIZATION: Token scope mismatch.");
            }
        }

        public static void ValidateActiveWriteContext(
            int currentScenarioId,
            int isolatedScenarioId,
            int assignedPhysicalAltId,
            int isolatedPhysicalAltId,
            int fieldPhysicalAltId,
            int sourcePhysicalAltId)
        {
            if (currentScenarioId != isolatedScenarioId)
                throw new InvalidOperationException("P15_B_BLOCKED_ACTIVE_SCENARIO_TARGETING: Current scenario ID does not match isolated scenario ID.");

            if (assignedPhysicalAltId != isolatedPhysicalAltId)
                throw new InvalidOperationException("P15_B_BLOCKED_ACTIVE_SCENARIO_TARGETING: Assigned physical alternative does not match isolated physical alternative ID.");

            if (fieldPhysicalAltId != isolatedPhysicalAltId)
                throw new InvalidOperationException("P15_B_BLOCKED_ACTIVE_SCENARIO_TARGETING: Field physical alternative write context does not match isolated physical alternative ID.");

            if (isolatedPhysicalAltId == sourcePhysicalAltId)
                throw new InvalidOperationException("P15_B_BLOCKED_ACTIVE_SCENARIO_TARGETING: Isolated physical alternative ID equals source physical alternative ID.");
        }

        public static void ValidateCandidateEndpointDesigns(
            GravityDesignCandidate candidate,
            int expectedConduitId)
        {
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));

            if (candidate.EndpointDesigns.Count != 2)
            {
                throw new InvalidOperationException($"P15_B_BLOCKED_PROPERTY_WRITE: Expected exactly 2 endpoint designs, found {candidate.EndpointDesigns.Count}.");
            }

            var start = candidate.EndpointDesigns.FirstOrDefault(e => e.End == ConduitEndKind.Start);
            var stop = candidate.EndpointDesigns.FirstOrDefault(e => e.End == ConduitEndKind.Stop);

            if (start == null || stop == null)
            {
                throw new InvalidOperationException("P15_B_BLOCKED_PROPERTY_WRITE: Candidate must have both Start and Stop endpoint designs.");
            }

            if (start.ConduitId != expectedConduitId || stop.ConduitId != expectedConduitId)
            {
                throw new InvalidOperationException($"P15_B_BLOCKED_PROPERTY_WRITE: Candidate endpoints target unexpected conduit ID.");
            }

            if (!string.Equals(start.Unit?.UnitName, "feet", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(start.Unit?.Symbol, "ft", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("P15_B_BLOCKED_PROPERTY_WRITE: Start endpoint design unit must be Feet.");
            }

            if (!string.Equals(stop.Unit?.UnitName, "feet", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(stop.Unit?.Symbol, "ft", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("P15_B_BLOCKED_PROPERTY_WRITE: Stop endpoint design unit must be Feet.");
            }
        }
    }
}
