using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class EngineeringDecisionWorkflowEngine
    {
        public EngineeringDecision CreateGenericDecision(
            string decisionId,
            string projectId,
            string module,
            EngineeringDecisionCategory category,
            IEnumerable<string> subjectElements,
            string problemStatement,
            string rationale,
            IDictionary<string, string> supportingEvidence,
            IEnumerable<EngineeringDecisionAlternative> alternatives)
        {
            if (string.IsNullOrWhiteSpace(decisionId))
                throw new ArgumentException("Decision ID cannot be null or empty.", nameof(decisionId));
            if (string.IsNullOrWhiteSpace(projectId))
                throw new ArgumentException("Project ID cannot be null or empty.", nameof(projectId));

            var decision = new EngineeringDecision
            {
                DecisionId = decisionId,
                ProjectId = projectId,
                Module = module,
                Category = category,
                SubjectElements = subjectElements != null ? subjectElements.ToList() : new List<string>(),
                ProblemStatement = problemStatement ?? string.Empty,
                Rationale = rationale ?? string.Empty,
                SupportingEvidence = supportingEvidence != null ? new Dictionary<string, string>(supportingEvidence, StringComparer.Ordinal) : new Dictionary<string, string>(StringComparer.Ordinal),
                Alternatives = alternatives != null ? alternatives.ToList() : new List<EngineeringDecisionAlternative>(),
                Status = EngineeringDecisionStatus.PendingReview,
                CreatedUtc = DateTimeOffset.UtcNow.ToString("o")
            };

            decision.Validate();
            decision.LineageFingerprint = decision.ComputeFingerprint();
            return decision;
        }

        public EngineeringDecision CreateFromGravityConflict(
            string decisionId,
            string projectId,
            VerticalConstraintConflict conflict,
            string rationale,
            IEnumerable<EngineeringDecisionAlternative> alternatives)
        {
            if (conflict == null) throw new ArgumentNullException(nameof(conflict));

            var evidence = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "ConflictCode", conflict.ConflictCode },
                { "Explanation", conflict.Explanation ?? "Vertical constraint conflict" }
            };

            if (conflict.ConstraintIds != null && conflict.ConstraintIds.Count > 0)
                evidence["ConstraintIds"] = string.Join(", ", conflict.ConstraintIds);

            if (conflict.KnownValues != null)
            {
                foreach (var kvp in conflict.KnownValues)
                {
                    evidence["KnownValue_" + kvp.Key] = kvp.Value.ToString("F3", CultureInfo.InvariantCulture);
                }
            }

            var elements = conflict.AffectedElements != null ? conflict.AffectedElements.ToList() : new List<string>();

            return CreateGenericDecision(
                decisionId,
                projectId,
                "GravityFlow",
                EngineeringDecisionCategory.VerticalFeasibilityConflict,
                elements,
                "Vertical Constraint Conflict: " + conflict.ConflictCode,
                rationale,
                evidence,
                alternatives);
        }

        public EngineeringDecision CreateFromStormBottleneck(
            string decisionId,
            string projectId,
            BottleneckRemediationProposal proposal,
            double rimElevation,
            double currentHgl,
            IEnumerable<EngineeringDecisionAlternative> alternatives)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));

            double freeboardDeficit = rimElevation - currentHgl;
            var evidence = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "ConduitId", proposal.ConduitId ?? "Unknown" },
                { "CurrentDiameterInches", proposal.CurrentDiameterInches.ToString("D", CultureInfo.InvariantCulture) },
                { "ProposedDiameterInches", proposal.ProposedDiameterInches.ToString("D", CultureInfo.InvariantCulture) },
                { "RimElevationFeet", rimElevation.ToString("F2", CultureInfo.InvariantCulture) },
                { "CurrentHglFeet", currentHgl.ToString("F2", CultureInfo.InvariantCulture) },
                { "CurrentFreeboardFeet", freeboardDeficit.ToString("F2", CultureInfo.InvariantCulture) },
                { "HeadLossReductionFeet", proposal.HeadLossReductionFeet.ToString("F3", CultureInfo.InvariantCulture) },
                { "ResolvedUpstreamFreeboardFeet", proposal.ResolvedUpstreamFreeboardFeet.ToString("F2", CultureInfo.InvariantCulture) },
                { "RemediationDescription", proposal.RemediationDescription ?? string.Empty }
            };

            var elements = new List<string>();
            if (!string.IsNullOrWhiteSpace(proposal.ConduitId)) elements.Add(proposal.ConduitId);

            return CreateGenericDecision(
                decisionId,
                projectId,
                "StormFlow",
                EngineeringDecisionCategory.BottleneckCapacityRemediation,
                elements,
                "Storm Network Surcharge & Bottleneck Remediation for " + proposal.ConduitId,
                proposal.RemediationDescription ?? "Capacity remediation required to relieve surcharge and restore safe freeboard.",
                evidence,
                alternatives);
        }

        public void ResolveDecision(
            EngineeringDecision decision,
            string chosenAlternativeId,
            string justification,
            EngineeringDecisionMaker decisionMaker,
            EngineeringDecisionImpact impact,
            IDictionary<string, string> parameterAdjustments = null)
        {
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            if (decisionMaker == null) throw new ArgumentNullException(nameof(decisionMaker));
            if (impact == null) throw new ArgumentNullException(nameof(impact));

            decisionMaker.Validate();
            impact.Validate();

            var matchingAlternative = decision.Alternatives.FirstOrDefault(a => string.Equals(a.AlternativeId, chosenAlternativeId, StringComparison.OrdinalIgnoreCase));
            if (matchingAlternative == null)
            {
                throw new EngineeringDecisionValidationException(
                    "Selected alternative '" + chosenAlternativeId + "' is not present in the evaluated alternatives for decision " + decision.DecisionId);
            }

            foreach (var alt in decision.Alternatives)
            {
                alt.IsSelected = string.Equals(alt.AlternativeId, chosenAlternativeId, StringComparison.OrdinalIgnoreCase);
            }

            var resolution = new EngineeringDecisionResolution
            {
                SelectedAlternativeId = matchingAlternative.AlternativeId,
                EngineeringJustification = justification,
                DecisionMaker = decisionMaker,
                TimestampUtc = DateTimeOffset.UtcNow.ToString("o"),
                ResultingImpact = impact,
                AppliedAdjustments = parameterAdjustments != null ? new Dictionary<string, string>(parameterAdjustments, StringComparer.Ordinal) : new Dictionary<string, string>(StringComparer.Ordinal)
            };

            resolution.Validate(decision.Alternatives);

            decision.Resolution = resolution;
            decision.Status = EngineeringDecisionStatus.ApprovedAndActive;
            decision.LineageFingerprint = decision.ComputeFingerprint();
        }

        public void RevokeDecision(EngineeringDecision decision, string reason, EngineeringDecisionMaker decisionMaker)
        {
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            if (decisionMaker == null) throw new ArgumentNullException(nameof(decisionMaker));
            if (string.IsNullOrWhiteSpace(reason))
                throw new EngineeringDecisionValidationException("Revocation reason must be provided.");

            decisionMaker.Validate();
            decision.Status = EngineeringDecisionStatus.Revoked;
            decision.SupportingEvidence["RevocationReason"] = reason;
            decision.SupportingEvidence["RevokedBy"] = decisionMaker.Name + " (" + decisionMaker.IdentityIdentifier + ")";
            decision.SupportingEvidence["RevokedUtc"] = DateTimeOffset.UtcNow.ToString("o");
            decision.LineageFingerprint = decision.ComputeFingerprint();
        }

        public void SupersedeDecision(EngineeringDecision oldDecision, EngineeringDecision newDecision, string reason)
        {
            if (oldDecision == null) throw new ArgumentNullException(nameof(oldDecision));
            if (newDecision == null) throw new ArgumentNullException(nameof(newDecision));
            if (string.IsNullOrWhiteSpace(reason))
                throw new EngineeringDecisionValidationException("Supersession reason must be provided.");

            oldDecision.Status = EngineeringDecisionStatus.Superseded;
            oldDecision.SupportingEvidence["SupersededByDecisionId"] = newDecision.DecisionId;
            oldDecision.SupportingEvidence["SupersededReason"] = reason;
            oldDecision.LineageFingerprint = oldDecision.ComputeFingerprint();

            newDecision.SupportingEvidence["SupersedesDecisionId"] = oldDecision.DecisionId;
            newDecision.LineageFingerprint = newDecision.ComputeFingerprint();
        }
    }
}
