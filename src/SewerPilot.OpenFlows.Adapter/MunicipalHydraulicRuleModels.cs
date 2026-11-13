using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter.HydraulicRules
{
    public enum RuleEvaluationStatus
    {
        Compliant = 1,
        Violation = 2,
        IncompleteData = 3,
        NotApplicable = 4,
        /// <summary>
        /// A numeric result exists but its physical basis is unproven (for example a velocity derived
        /// from an assumed full-flow wetted area). Compliance must not be asserted; an engineer must
        /// dispose of the item. Fail-closed equivalent of "indeterminate".
        /// </summary>
        RequiresEngineeringDisposition = 5
    }

    public enum RuleSeverity
    {
        Info = 1,
        Warning = 2,
        Error = 3
    }

    public sealed class HydraulicRuleEvaluationContext
    {
        public int ElementId { get; set; }
        public string ElementLabel { get; set; }
        public string ElementKind { get; set; } // "Conduit", "Manhole", "Outfall", "Junction"

        // Geometry properties
        public double? DiameterFeet { get; set; }
        public double? LengthFeet { get; set; }
        public double? LongitudinalSlope { get; set; }
        public double? ManningN { get; set; }
        public double? StartInvertFeet { get; set; }
        public double? StopInvertFeet { get; set; }
        public double? UpstreamGroundElevationFeet { get; set; }
        public double? DownstreamGroundElevationFeet { get; set; }

        // Hydraulic results
        public double? ActualVelocityFps { get; set; }
        public double? DesignDischargeCfs { get; set; }
        public double? UpstreamHglFeet { get; set; }
        public double? DownstreamHglFeet { get; set; }

        // Junction crown context (for junction / manhole crown alignment)
        public int? JunctionNodeId { get; set; }
        public string JunctionLabel { get; set; }
        public int? UpstreamConduitId { get; set; }
        public int? DownstreamConduitId { get; set; }
        public double? UpstreamExitCrownFeet { get; set; }
        public double? DownstreamEntranceCrownFeet { get; set; }
        public double? UpstreamExitInvertFeet { get; set; }
        public double? DownstreamEntranceInvertFeet { get; set; }
        public double? UpstreamDiameterFeet { get; set; }
        public double? DownstreamDiameterFeet { get; set; }

        public StandardProvenance Provenance { get; set; }
    }

    public sealed class RuleEvaluationResult
    {
        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public string MunicipalStandardReference { get; set; }
        public RuleSeverity Severity { get; set; }
        public RuleEvaluationStatus Status { get; set; }
        public int ElementId { get; set; }
        public string ElementLabel { get; set; }
        public string ElementKind { get; set; }
        public double? ObservedValue { get; set; }
        public string ObservedUnit { get; set; }
        public double? RequiredThreshold { get; set; }
        public string RequiredThresholdUnit { get; set; }
        public string ComparisonOperator { get; set; } // ">=", "<=", "Range", "Non-Negative"
        public string MathematicalDerivation { get; set; }
        public string FindingsMessage { get; set; }
        public StandardProvenance Provenance { get; set; }
    }

    public interface IHydraulicRule
    {
        string RuleId { get; }
        string RuleName { get; }
        string MunicipalStandardReference { get; }
        RuleSeverity Severity { get; }
        RuleEvaluationResult Evaluate(HydraulicRuleEvaluationContext context);
    }

    public sealed class MunicipalRuleEvaluationReport
    {
        public int SchemaVersion { get; set; } = 1;
        public string TimestampUtc { get; set; } = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        public string ProjectId { get; set; }
        public string NetworkFingerprint { get; set; }
        public string ResultSetFingerprint { get; set; }
        public string ReportFingerprint { get; set; }

        public int CompliantCount { get; set; }
        public int ViolationCount { get; set; }
        public int IncompleteDataCount { get; set; }
        public int NotApplicableCount { get; set; }
        public int RequiresEngineeringDispositionCount { get; set; }

        public List<RuleEvaluationResult> Results { get; set; } = new List<RuleEvaluationResult>();

        public void Summarize()
        {
            CompliantCount = 0;
            ViolationCount = 0;
            IncompleteDataCount = 0;
            NotApplicableCount = 0;
            RequiresEngineeringDispositionCount = 0;

            if (Results != null)
            {
                foreach (var r in Results)
                {
                    if (r == null) continue;
                    switch (r.Status)
                    {
                        case RuleEvaluationStatus.Compliant: CompliantCount++; break;
                        case RuleEvaluationStatus.Violation: ViolationCount++; break;
                        case RuleEvaluationStatus.IncompleteData: IncompleteDataCount++; break;
                        case RuleEvaluationStatus.NotApplicable: NotApplicableCount++; break;
                        case RuleEvaluationStatus.RequiresEngineeringDisposition: RequiresEngineeringDispositionCount++; break;
                        default:
                            // Unclassified statuses must never disappear from the tally.
                            throw new InvalidOperationException("Unhandled rule evaluation status: " + r.Status);
                    }
                }
            }

            ReportFingerprint = ComputeFingerprint(this);
        }

        public static string ComputeFingerprint(MunicipalRuleEvaluationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var sb = new StringBuilder();
            sb.Append("SCHEMA:").Append(report.SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append("\n");
            sb.Append("PROJECT:").Append(report.ProjectId ?? "").Append("\n");
            sb.Append("NETWORK:").Append(report.NetworkFingerprint ?? "").Append("\n");
            sb.Append("RESULTSET:").Append(report.ResultSetFingerprint ?? "").Append("\n");

            var ordered = (report.Results ?? new List<RuleEvaluationResult>())
                .Where(r => r != null)
                .OrderBy(r => r.ElementId)
                .ThenBy(r => r.RuleId ?? "", StringComparer.Ordinal);

            foreach (var r in ordered)
            {
                sb.Append("R|").Append(r.ElementId.ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(r.RuleId ?? "").Append("|")
                  .Append(((int)r.Status).ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(r.ObservedValue.HasValue ? r.ObservedValue.Value.ToString("R", CultureInfo.InvariantCulture) : "").Append("|")
                  .Append(r.RequiredThreshold.HasValue ? r.RequiredThreshold.Value.ToString("R", CultureInfo.InvariantCulture) : "").Append("|")
                  .Append(r.ComparisonOperator ?? "").Append("\n");
            }

            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
