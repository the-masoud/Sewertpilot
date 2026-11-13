using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum EngineeringDecisionCategory
    {
        VerticalFeasibilityConflict = 1,
        MinimumCoverViolation = 2,
        VelocityLimitExceedance = 3,
        InletBypassPolicy = 4,
        OutfallBoundaryCondition = 5,
        CriteriaOverride = 6,
        DesignAlternativeSelection = 7,
        BottleneckCapacityRemediation = 8
    }

    public enum EngineeringDecisionStatus
    {
        Draft = 1,
        PendingReview = 2,
        ApprovedAndActive = 3,
        Superseded = 4,
        Revoked = 5
    }

    public enum EngineeringComplianceStatus
    {
        FullyCompliant = 1,
        CompliantWithVariance = 2,
        RequiresRegulatoryPermit = 3,
        NonCompliant = 4
    }

    public sealed class EngineeringDecisionAlternative
    {
        public string AlternativeId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public List<string> Pros { get; set; } = new List<string>();
        public List<string> Cons { get; set; } = new List<string>();
        public string PredictedHydraulicImpact { get; set; }
        public string PredictedCostImpact { get; set; }
        public bool IsSelected { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(AlternativeId))
                throw new EngineeringDecisionValidationException("AlternativeId cannot be null or whitespace.");
            if (string.IsNullOrWhiteSpace(Title))
                throw new EngineeringDecisionValidationException("Alternative Title cannot be null or whitespace.");
            if (string.IsNullOrWhiteSpace(Description))
                throw new EngineeringDecisionValidationException("Alternative Description cannot be null or whitespace.");
        }
    }

    public sealed class EngineeringDecisionMaker
    {
        public string Name { get; set; }
        public string ProfessionalTitle { get; set; }
        public string IdentityIdentifier { get; set; }
        public string ActionType { get; set; } = "ExplicitApproval";

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                throw new EngineeringDecisionValidationException("DecisionMaker Name cannot be null or whitespace.");
            if (string.IsNullOrWhiteSpace(ProfessionalTitle))
                throw new EngineeringDecisionValidationException("DecisionMaker ProfessionalTitle cannot be null or whitespace.");
            if (string.IsNullOrWhiteSpace(IdentityIdentifier))
                throw new EngineeringDecisionValidationException("DecisionMaker IdentityIdentifier cannot be null or whitespace.");
        }
    }

    public sealed class EngineeringDecisionImpact
    {
        public string HydraulicImpact { get; set; }
        public string PhysicalImpact { get; set; }
        public string DownstreamWorkflowImpact { get; set; }
        public EngineeringComplianceStatus ComplianceStatus { get; set; } = EngineeringComplianceStatus.CompliantWithVariance;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(HydraulicImpact))
                throw new EngineeringDecisionValidationException("Resulting HydraulicImpact cannot be null or whitespace.");
            if (!Enum.IsDefined(typeof(EngineeringComplianceStatus), ComplianceStatus))
                throw new EngineeringDecisionValidationException("Invalid ComplianceStatus.");
        }
    }

    public sealed class EngineeringDecisionResolution
    {
        public string SelectedAlternativeId { get; set; }
        public string EngineeringJustification { get; set; }
        public Dictionary<string, string> AppliedAdjustments { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public EngineeringDecisionMaker DecisionMaker { get; set; }
        public string TimestampUtc { get; set; }
        public EngineeringDecisionImpact ResultingImpact { get; set; }

        public void Validate(IEnumerable<EngineeringDecisionAlternative> alternatives)
        {
            if (string.IsNullOrWhiteSpace(SelectedAlternativeId))
                throw new EngineeringDecisionValidationException("SelectedAlternativeId cannot be null or whitespace.");
            if (string.IsNullOrWhiteSpace(EngineeringJustification))
                throw new EngineeringDecisionValidationException("EngineeringJustification cannot be null or whitespace.");
            if (DecisionMaker == null)
                throw new EngineeringDecisionValidationException("DecisionMaker cannot be null.");
            DecisionMaker.Validate();
            if (string.IsNullOrWhiteSpace(TimestampUtc))
                throw new EngineeringDecisionValidationException("TimestampUtc cannot be null or whitespace.");
            if (ResultingImpact == null)
                throw new EngineeringDecisionValidationException("ResultingImpact cannot be null.");
            ResultingImpact.Validate();

            if (alternatives == null || !alternatives.Any(a => string.Equals(a.AlternativeId, SelectedAlternativeId, StringComparison.OrdinalIgnoreCase)))
                throw new EngineeringDecisionValidationException("SelectedAlternativeId '" + SelectedAlternativeId + "' does not match any alternative considered.");
        }
    }

    public sealed class EngineeringDecision
    {
        public string DecisionId { get; set; }
        public string ProjectId { get; set; }
        public string Module { get; set; } // "GravityFlow", "StormFlow", or "CrossModule"
        public EngineeringDecisionCategory Category { get; set; }
        public List<string> SubjectElements { get; set; } = new List<string>();
        public string ProblemStatement { get; set; }
        public string Rationale { get; set; }
        public Dictionary<string, string> SupportingEvidence { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<EngineeringDecisionAlternative> Alternatives { get; set; } = new List<EngineeringDecisionAlternative>();
        public EngineeringDecisionResolution Resolution { get; set; }
        public EngineeringDecisionStatus Status { get; set; } = EngineeringDecisionStatus.Draft;
        public string CreatedUtc { get; set; }
        public string LineageFingerprint { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(DecisionId))
                throw new EngineeringDecisionValidationException("DecisionId cannot be null or whitespace.");
            if (string.IsNullOrWhiteSpace(ProjectId))
                throw new EngineeringDecisionValidationException("ProjectId cannot be null or whitespace.");
            if (string.IsNullOrWhiteSpace(Module) ||
                (!string.Equals(Module, "GravityFlow", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(Module, "StormFlow", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(Module, "CrossModule", StringComparison.OrdinalIgnoreCase)))
            {
                throw new EngineeringDecisionValidationException("Module must be 'GravityFlow', 'StormFlow', or 'CrossModule'.");
            }
            if (!Enum.IsDefined(typeof(EngineeringDecisionCategory), Category))
                throw new EngineeringDecisionValidationException("Invalid EngineeringDecisionCategory.");
            if (string.IsNullOrWhiteSpace(ProblemStatement))
                throw new EngineeringDecisionValidationException("ProblemStatement cannot be null or whitespace.");
            if (string.IsNullOrWhiteSpace(Rationale))
                throw new EngineeringDecisionValidationException("Rationale cannot be null or whitespace.");
            if (SupportingEvidence == null || SupportingEvidence.Count == 0)
                throw new EngineeringDecisionValidationException("SupportingEvidence must contain at least one piece of factual evidence.");
            if (Alternatives == null || Alternatives.Count < 2)
                throw new EngineeringDecisionValidationException("At least two distinct alternatives must be considered.");

            foreach (var alt in Alternatives)
            {
                if (alt == null)
                    throw new EngineeringDecisionValidationException("Alternative in list cannot be null.");
                alt.Validate();
            }

            if (Status == EngineeringDecisionStatus.ApprovedAndActive)
            {
                if (Resolution == null)
                    throw new EngineeringDecisionValidationException("Approved decision must have a valid Resolution.");
                Resolution.Validate(Alternatives);
            }
        }

        public string ComputeFingerprint()
        {
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                sb.Append(DecisionId).Append("|");
                sb.Append(ProjectId).Append("|");
                sb.Append(Module).Append("|");
                sb.Append((int)Category).Append("|");
                sb.Append(string.Join(",", SubjectElements.OrderBy(x => x, StringComparer.Ordinal))).Append("|");
                sb.Append(ProblemStatement).Append("|");
                sb.Append(Rationale).Append("|");
                foreach (var kvp in SupportingEvidence.OrderBy(x => x.Key, StringComparer.Ordinal))
                    sb.Append(kvp.Key).Append("=").Append(kvp.Value).Append(";");
                sb.Append("|");
                foreach (var alt in Alternatives.OrderBy(x => x.AlternativeId, StringComparer.Ordinal))
                    sb.Append(alt.AlternativeId).Append(":").Append(alt.Title).Append(";");
                sb.Append("|");
                if (Resolution != null)
                {
                    sb.Append(Resolution.SelectedAlternativeId).Append("|");
                    sb.Append(Resolution.DecisionMaker?.Name).Append("|");
                    sb.Append(Resolution.TimestampUtc).Append("|");
                }
                sb.Append((int)Status);

                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
            }
        }
    }

    public sealed class EngineeringDecisionRegister
    {
        public int SchemaVersion { get; set; } = 1;
        public string ProjectId { get; set; }
        public List<EngineeringDecision> Decisions { get; set; } = new List<EngineeringDecision>();

        public void AddOrUpdate(EngineeringDecision decision)
        {
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            decision.Validate();
            decision.LineageFingerprint = decision.ComputeFingerprint();

            int existingIndex = Decisions.FindIndex(d => string.Equals(d.DecisionId, decision.DecisionId, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
                Decisions[existingIndex] = decision;
            else
                Decisions.Add(decision);
        }

        public EngineeringDecision Get(string decisionId)
        {
            if (string.IsNullOrWhiteSpace(decisionId)) return null;
            return Decisions.FirstOrDefault(d => string.Equals(d.DecisionId, decisionId, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<EngineeringDecision> GetByModule(string module)
        {
            if (string.IsNullOrWhiteSpace(module)) return Enumerable.Empty<EngineeringDecision>();
            return Decisions.Where(d => string.Equals(d.Module, module, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(d.Module, "CrossModule", StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<EngineeringDecision> GetByElement(string elementId)
        {
            if (string.IsNullOrWhiteSpace(elementId)) return Enumerable.Empty<EngineeringDecision>();
            return Decisions.Where(d => d.SubjectElements != null &&
                                        d.SubjectElements.Any(e => string.Equals(e, elementId, StringComparison.OrdinalIgnoreCase)));
        }

        public IEnumerable<EngineeringDecision> GetActiveApproved()
        {
            return Decisions.Where(d => d.Status == EngineeringDecisionStatus.ApprovedAndActive);
        }

        public IEnumerable<EngineeringDecision> GetPendingReview()
        {
            return Decisions.Where(d => d.Status == EngineeringDecisionStatus.PendingReview || d.Status == EngineeringDecisionStatus.Draft);
        }

        public string ComputeRegisterFingerprint()
        {
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                sb.Append("Register:").Append(ProjectId).Append("|");
                foreach (var d in Decisions.OrderBy(x => x.DecisionId, StringComparer.Ordinal))
                    sb.Append(d.ComputeFingerprint()).Append(";");

                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
            }
        }

        public string ToJson()
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            return serializer.Serialize(this);
        }

        public static EngineeringDecisionRegister FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new EngineeringDecisionValidationException("EngineeringDecisionRegister JSON cannot be empty.");

            var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            var register = serializer.Deserialize<EngineeringDecisionRegister>(json);
            if (register == null)
                throw new EngineeringDecisionValidationException("Deserialized register is null.");
            if (register.SchemaVersion != 1)
                throw new EngineeringDecisionValidationException("Unsupported register schema version: " + register.SchemaVersion);

            foreach (var d in register.Decisions)
            {
                d.Validate();
            }
            return register;
        }

        public string ExportMarkdownRegister()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Engineering Decision Register");
            sb.AppendLine();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "**Project:** `{0}`  ", ProjectId ?? "N/A"));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "**Total Decisions Recorded:** `{0}`  ", Decisions.Count));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "**Active Approved Decisions:** `{0}`  ", Decisions.Count(d => d.Status == EngineeringDecisionStatus.ApprovedAndActive)));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "**Register Fingerprint:** `{0}`  ", ComputeRegisterFingerprint()));
            sb.AppendLine();
            sb.AppendLine("| Decision ID | Module | Category | Affected Elements | Status | Selected Choice | Decision-Maker | Impact Summary |");
            sb.AppendLine("|:---|:---|:---|:---|:---:|:---|:---|:---|");

            foreach (var d in Decisions.OrderBy(x => x.DecisionId, StringComparer.Ordinal))
            {
                string choice = d.Resolution?.SelectedAlternativeId ?? "PENDING";
                string maker = d.Resolution?.DecisionMaker != null ? d.Resolution.DecisionMaker.Name + " (" + d.Resolution.DecisionMaker.ProfessionalTitle + ")" : "UNASSIGNED";
                string elements = d.SubjectElements != null && d.SubjectElements.Count > 0 ? string.Join(", ", d.SubjectElements) : "Project-wide";
                string impact = d.Resolution?.ResultingImpact?.HydraulicImpact ?? "Pending resolution";

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| `{0}` | {1} | {2} | {3} | **{4}** | {5} | {6} | {7} |",
                    d.DecisionId,
                    d.Module,
                    d.Category,
                    elements,
                    d.Status,
                    choice,
                    maker,
                    impact));
            }

            sb.AppendLine();
            sb.AppendLine("## Decision Details & Technical Rationale");
            sb.AppendLine();

            foreach (var d in Decisions.OrderBy(x => x.DecisionId, StringComparer.Ordinal))
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "### Decision `{0}`: {1}", d.DecisionId, d.ProblemStatement));
                sb.AppendLine();
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "- **Module:** {0} | **Category:** {1} | **Status:** {2}", d.Module, d.Category, d.Status));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "- **Technical Rationale:** {0}", d.Rationale));
                sb.AppendLine("- **Supporting Evidence & Known Facts:**");
                foreach (var ev in d.SupportingEvidence)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - `{0}`: {1}", ev.Key, ev.Value));
                }
                sb.AppendLine("- **Alternatives Evaluated:**");
                foreach (var alt in d.Alternatives)
                {
                    string selTag = alt.IsSelected ? " **[SELECTED]**" : "";
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - `{0}`: **{1}**{2} — {3}", alt.AlternativeId, alt.Title, selTag, alt.Description));
                    if (!string.IsNullOrWhiteSpace(alt.PredictedHydraulicImpact))
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    - *Hydraulic Impact:* {0}", alt.PredictedHydraulicImpact));
                    if (!string.IsNullOrWhiteSpace(alt.PredictedCostImpact))
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    - *Cost Impact:* {0}", alt.PredictedCostImpact));
                }

                if (d.Resolution != null)
                {
                    sb.AppendLine("- **Resolution & Chosen Policy:**");
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - **Selected Alternative:** `{0}`", d.Resolution.SelectedAlternativeId));
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - **Engineering Justification:** {0}", d.Resolution.EngineeringJustification));
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - **Decision-Maker:** {0}, {1} (ID: `{2}`)",
                        d.Resolution.DecisionMaker?.Name,
                        d.Resolution.DecisionMaker?.ProfessionalTitle,
                        d.Resolution.DecisionMaker?.IdentityIdentifier));
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - **Resolution Date:** {0}", d.Resolution.TimestampUtc));
                    if (d.Resolution.ResultingImpact != null)
                    {
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - **Hydraulic Impact:** {0}", d.Resolution.ResultingImpact.HydraulicImpact));
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - **Physical Impact:** {0}", d.Resolution.ResultingImpact.PhysicalImpact));
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - **Workflow Impact:** {0}", d.Resolution.ResultingImpact.DownstreamWorkflowImpact));
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - **Compliance Verdict:** {0}", d.Resolution.ResultingImpact.ComplianceStatus));
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }

    public sealed class EngineeringDecisionValidationException : Exception
    {
        public EngineeringDecisionValidationException(string message) : base(message) { }
    }
}
