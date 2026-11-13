using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    // P16 vendor-neutral hydraulic result contract.
    // No vendor type, assembly member or result-field name is modelled here. A Bentley result-access
    // mapping is only added when accepted runtime evidence establishes the exact members; until then
    // ResultRead remains NotTested and only this neutral contract plus deterministic verification exist.
    public enum HydraulicMetricKind
    {
        Flow = 1,
        Capacity = 2,
        Velocity = 3,
        Depth = 4,
        DepthRatio = 5,
        Hgl = 6,
        Surcharge = 7,
        Cover = 8,
        Slope = 9
    }

    // Distinguishes solver-calculated results from measured/source geometry and from quantities that
    // SewerPilot derived deterministically from verified inputs.
    public enum HydraulicValueOrigin
    {
        BentleyCalculatedResult = 1,
        SourceGeometry = 2,
        VerifiedDerivedQuantity = 3
    }

    public enum HydraulicResultAvailability
    {
        Available = 1,
        NotAvailable = 2,
        NotApplicable = 3,
        Invalid = 4
    }

    public sealed class HydraulicResultProvenance
    {
        public string SourceDescription { get; set; }
        public string EvidenceReference { get; set; }
        public string CalculationContext { get; set; }
        public string Notes { get; set; }

        public HydraulicResultProvenance Clone()
        {
            return new HydraulicResultProvenance
            {
                SourceDescription = SourceDescription,
                EvidenceReference = EvidenceReference,
                CalculationContext = CalculationContext,
                Notes = Notes
            };
        }
    }

    public sealed class HydraulicMetricResult
    {
        public HydraulicMetricKind Metric { get; set; }
        public double? NumericValue { get; set; }
        public bool? BooleanValue { get; set; }
        public string Unit { get; set; }
        public HydraulicResultAvailability Availability { get; set; } = HydraulicResultAvailability.NotAvailable;
        public HydraulicValueOrigin Origin { get; set; } = HydraulicValueOrigin.BentleyCalculatedResult;
        // Only populated for VerifiedDerivedQuantity: the deterministic formula identity and the exact
        // verified inputs it consumed. Derived metrics never appear without both.
        public string Derivation { get; set; }
        public string DerivationInputs { get; set; }
        public HydraulicResultProvenance Provenance { get; set; }

        public bool HasUsableValue
        {
            get
            {
                return Availability == HydraulicResultAvailability.Available &&
                    ((NumericValue.HasValue && !double.IsNaN(NumericValue.Value) && !double.IsInfinity(NumericValue.Value)) ||
                     BooleanValue.HasValue);
            }
        }

        public static HydraulicMetricResult Missing(HydraulicMetricKind metric, string unit, string reason)
        {
            return new HydraulicMetricResult
            {
                Metric = metric,
                Unit = unit,
                Availability = HydraulicResultAvailability.NotAvailable,
                Origin = HydraulicValueOrigin.BentleyCalculatedResult,
                Provenance = new HydraulicResultProvenance { Notes = reason }
            };
        }

        public HydraulicMetricResult Clone()
        {
            return new HydraulicMetricResult
            {
                Metric = Metric,
                NumericValue = NumericValue,
                BooleanValue = BooleanValue,
                Unit = Unit,
                Availability = Availability,
                Origin = Origin,
                Derivation = Derivation,
                DerivationInputs = DerivationInputs,
                Provenance = Provenance?.Clone()
            };
        }
    }

    public sealed class HydraulicElementResult
    {
        public int ElementId { get; set; }
        // Neutral element category. No vendor type identity is carried.
        public string ElementKind { get; set; }
        public int ScenarioId { get; set; }
        public int PhysicalAlternativeId { get; set; }
        public string CalculationContext { get; set; }
        public double? ResultTimeSeconds { get; set; }
        public List<HydraulicMetricResult> Metrics { get; set; } = new List<HydraulicMetricResult>();
        public HydraulicResultProvenance Provenance { get; set; }

        public HydraulicMetricResult FindMetric(HydraulicMetricKind metric)
        {
            if (Metrics == null) return null;
            return Metrics.FirstOrDefault(m => m != null && m.Metric == metric);
        }

        public HydraulicElementResult Clone()
        {
            return new HydraulicElementResult
            {
                ElementId = ElementId,
                ElementKind = ElementKind,
                ScenarioId = ScenarioId,
                PhysicalAlternativeId = PhysicalAlternativeId,
                CalculationContext = CalculationContext,
                ResultTimeSeconds = ResultTimeSeconds,
                Metrics = Metrics != null ? Metrics.Select(m => m?.Clone()).Where(m => m != null).ToList() : new List<HydraulicMetricResult>(),
                Provenance = Provenance?.Clone()
            };
        }
    }

    public sealed class HydraulicResultSet
    {
        public int SchemaVersion { get; set; } = 1;
        public string ProjectId { get; set; }
        public string ModelFingerprint { get; set; }
        public string NetworkFingerprint { get; set; }
        public string CandidateFingerprint { get; set; }
        public int ScenarioId { get; set; }
        public int PhysicalAlternativeId { get; set; }
        public string CalculationContext { get; set; }
        public string CalculationRunId { get; set; }
        public double? ResultTimeSeconds { get; set; }
        // TEST_ONLY marks deterministic fixtures; it never authorizes engineering compliance.
        public bool IsTestOnly { get; set; }
        public List<HydraulicElementResult> Elements { get; set; } = new List<HydraulicElementResult>();
        public HydraulicResultProvenance Provenance { get; set; }

        public HydraulicElementResult FindElement(int elementId)
        {
            if (Elements == null) return null;
            return Elements.FirstOrDefault(e => e != null && e.ElementId == elementId);
        }

        // Deterministic content identity. Ordering, culture and machine/session noise do not participate.
        public static string ComputeFingerprint(HydraulicResultSet set)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            var sb = new StringBuilder();
            sb.Append("SCHEMA:").Append(set.SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append("\n");
            sb.Append("PROJECT:").Append(set.ProjectId ?? "").Append("\n");
            sb.Append("MODEL:").Append(set.ModelFingerprint ?? "").Append("\n");
            sb.Append("NETWORK:").Append(set.NetworkFingerprint ?? "").Append("\n");
            sb.Append("CANDIDATE:").Append(set.CandidateFingerprint ?? "").Append("\n");
            sb.Append("SCENARIO:").Append(set.ScenarioId.ToString(CultureInfo.InvariantCulture)).Append("\n");
            sb.Append("PHYSICAL:").Append(set.PhysicalAlternativeId.ToString(CultureInfo.InvariantCulture)).Append("\n");
            sb.Append("CONTEXT:").Append(set.CalculationContext ?? "").Append("\n");
            sb.Append("RUN:").Append(set.CalculationRunId ?? "").Append("\n");
            sb.Append("TESTONLY:").Append(set.IsTestOnly ? "true" : "false").Append("\n");

            var elements = (set.Elements ?? new List<HydraulicElementResult>())
                .Where(e => e != null)
                .OrderBy(e => e.ElementId)
                .ThenBy(e => e.ElementKind ?? "", StringComparer.Ordinal);
            foreach (var element in elements)
            {
                sb.Append("ELEMENT|").Append(element.ElementId.ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(element.ElementKind ?? "").Append("|")
                  .Append(element.ScenarioId.ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(element.PhysicalAlternativeId.ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(element.CalculationContext ?? "").Append("|")
                  .Append(element.ResultTimeSeconds.HasValue ? element.ResultTimeSeconds.Value.ToString("R", CultureInfo.InvariantCulture) : "")
                  .Append("\n");
                var metrics = (element.Metrics ?? new List<HydraulicMetricResult>())
                    .Where(m => m != null)
                    .OrderBy(m => (int)m.Metric);
                foreach (var metric in metrics)
                {
                    string valStr = metric.NumericValue.HasValue
                        ? metric.NumericValue.Value.ToString("R", CultureInfo.InvariantCulture)
                        : (metric.BooleanValue.HasValue ? (metric.BooleanValue.Value ? "true" : "false") : "");
                    sb.Append("  METRIC|").Append(((int)metric.Metric).ToString(CultureInfo.InvariantCulture)).Append("|")
                      .Append(((int)metric.Availability).ToString(CultureInfo.InvariantCulture)).Append("|")
                      .Append(((int)metric.Origin).ToString(CultureInfo.InvariantCulture)).Append("|")
                      .Append(valStr)
                      .Append("|").Append(metric.Unit ?? "").Append("|")
                      .Append(metric.Derivation ?? "").Append("|").Append(metric.DerivationInputs ?? "")
                      .Append("\n");
                }
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    // Explicit, engineer-supplied comparison threshold. SewerPilot never invents a criterion.
    public enum HydraulicCriterionOperator
    {
        Minimum = 1,
        Maximum = 2
    }

    public sealed class HydraulicVerificationCriterion
    {
        public string CriterionKey { get; set; }
        public HydraulicMetricKind Metric { get; set; }
        public HydraulicCriterionOperator Operator { get; set; }
        public double Threshold { get; set; }
        public string Unit { get; set; }
        // Null scope means every element that declares the metric. A positive value scopes one element.
        public int? AppliesToElementId { get; set; }
        public StandardProvenance Provenance { get; set; }

        public HydraulicVerificationCriterion Clone()
        {
            return new HydraulicVerificationCriterion
            {
                CriterionKey = CriterionKey,
                Metric = Metric,
                Operator = Operator,
                Threshold = Threshold,
                Unit = Unit,
                AppliesToElementId = AppliesToElementId,
                Provenance = Provenance != null ? new StandardProvenance
                {
                    SourceLayer = Provenance.SourceLayer,
                    StandardId = Provenance.StandardId,
                    StandardName = Provenance.StandardName,
                    StandardVersion = Provenance.StandardVersion,
                    ParameterKey = Provenance.ParameterKey,
                    DocumentReference = Provenance.DocumentReference,
                    SectionReference = Provenance.SectionReference,
                    Notes = Provenance.Notes
                } : null
            };
        }
    }

    public enum HydraulicVerificationClassification
    {
        Pass = 1,
        Violation = 2,
        NotAvailable = 3,
        NotApplicable = 4,
        InvalidInput = 5
    }

    public sealed class HydraulicVerificationFinding
    {
        public int ElementId { get; set; }
        public string ElementKind { get; set; }
        public HydraulicMetricKind Metric { get; set; }
        public HydraulicVerificationClassification Classification { get; set; }
        public double? ActualValue { get; set; }
        public string ActualUnit { get; set; }
        public HydraulicCriterionOperator? RequiredOperator { get; set; }
        public double? RequiredThreshold { get; set; }
        public string RequiredUnit { get; set; }
        public string CriterionKey { get; set; }
        public string Reason { get; set; }
        public StandardProvenance Provenance { get; set; }
    }

    public sealed class HydraulicVerificationReport
    {
        public int SchemaVersion { get; set; } = 1;
        public string ResultFingerprint { get; set; }
        public string CriteriaFingerprint { get; set; }
        // True when the underlying result set is a deterministic fixture and cannot establish compliance.
        public bool IsTestOnly { get; set; }
        public List<HydraulicVerificationFinding> Findings { get; set; } = new List<HydraulicVerificationFinding>();
        public int PassCount { get; set; }
        public int ViolationCount { get; set; }
        public int NotAvailableCount { get; set; }
        public int NotApplicableCount { get; set; }
        public int InvalidInputCount { get; set; }
        public string ReportFingerprint { get; set; }

        public bool HasViolations => ViolationCount > 0;

        public static string ComputeCriteriaFingerprint(IEnumerable<HydraulicVerificationCriterion> criteria)
        {
            var sb = new StringBuilder();
            var ordered = (criteria ?? Enumerable.Empty<HydraulicVerificationCriterion>())
                .Where(c => c != null)
                .OrderBy(c => c.CriterionKey ?? "", StringComparer.Ordinal)
                .ThenBy(c => (int)c.Metric)
                .ThenBy(c => c.AppliesToElementId ?? 0);
            foreach (var c in ordered)
            {
                sb.Append("C|").Append(c.CriterionKey ?? "").Append("|")
                  .Append(((int)c.Metric).ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(((int)c.Operator).ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(c.Threshold.ToString("R", CultureInfo.InvariantCulture)).Append("|")
                  .Append(c.Unit ?? "").Append("|")
                  .Append(c.AppliesToElementId.HasValue ? c.AppliesToElementId.Value.ToString(CultureInfo.InvariantCulture) : "")
                  .Append("|")
                  .Append(c.Provenance != null ? ((int)c.Provenance.SourceLayer).ToString(CultureInfo.InvariantCulture) + ":" + (c.Provenance.StandardId ?? "") + ":" + (c.Provenance.ParameterKey ?? "") : "NONE")
                  .Append("\n");
            }
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    // Deterministic derived-metric helpers. A derived value is produced only from verified inputs with
    // matching supported units; otherwise it is reported NotAvailable and never guessed.
    public static class HydraulicDerivedMetrics
    {
        public const string DepthRatioFormula = "d/D = depth / diameter";
        public const string SlopeFormula = "slope = (startInvert - stopInvert) / length";

        public static HydraulicMetricResult DepthRatio(double? depth, string depthUnit, double? diameter, string diameterUnit,
            HydraulicResultProvenance provenance)
        {
            if (!depth.HasValue || !diameter.HasValue || double.IsNaN(depth.Value) || double.IsInfinity(depth.Value) ||
                double.IsNaN(diameter.Value) || double.IsInfinity(diameter.Value) || diameter.Value <= 0)
                return HydraulicMetricResult.Missing(HydraulicMetricKind.DepthRatio, "dimensionless", "Verified depth and positive diameter are required.");
            if (!string.Equals(depthUnit, diameterUnit, StringComparison.OrdinalIgnoreCase))
                return HydraulicMetricResult.Missing(HydraulicMetricKind.DepthRatio, "dimensionless", "Depth and diameter units differ; no conversion is applied.");
            return new HydraulicMetricResult
            {
                Metric = HydraulicMetricKind.DepthRatio,
                NumericValue = depth.Value / diameter.Value,
                Unit = "dimensionless",
                Availability = HydraulicResultAvailability.Available,
                Origin = HydraulicValueOrigin.VerifiedDerivedQuantity,
                Derivation = DepthRatioFormula,
                DerivationInputs = "depth=" + depth.Value.ToString("R", CultureInfo.InvariantCulture) + " " + (depthUnit ?? "") +
                    "; diameter=" + diameter.Value.ToString("R", CultureInfo.InvariantCulture) + " " + (diameterUnit ?? ""),
                Provenance = provenance?.Clone()
            };
        }

        public static HydraulicMetricResult Slope(double? startInvert, double? stopInvert, double? length, string unit,
            HydraulicResultProvenance provenance)
        {
            if (!startInvert.HasValue || !stopInvert.HasValue || !length.HasValue ||
                double.IsNaN(startInvert.Value) || double.IsInfinity(startInvert.Value) ||
                double.IsNaN(stopInvert.Value) || double.IsInfinity(stopInvert.Value) ||
                double.IsNaN(length.Value) || double.IsInfinity(length.Value) || length.Value <= 0)
                return HydraulicMetricResult.Missing(HydraulicMetricKind.Slope, "dimensionless", "Verified inverts and positive length are required.");
            return new HydraulicMetricResult
            {
                Metric = HydraulicMetricKind.Slope,
                NumericValue = (startInvert.Value - stopInvert.Value) / length.Value,
                Unit = "dimensionless",
                Availability = HydraulicResultAvailability.Available,
                Origin = HydraulicValueOrigin.VerifiedDerivedQuantity,
                Derivation = SlopeFormula,
                DerivationInputs = "startInvert=" + startInvert.Value.ToString("R", CultureInfo.InvariantCulture) +
                    "; stopInvert=" + stopInvert.Value.ToString("R", CultureInfo.InvariantCulture) +
                    "; length=" + length.Value.ToString("R", CultureInfo.InvariantCulture) + " " + (unit ?? ""),
                Provenance = provenance?.Clone()
            };
        }
    }

    // Documents the exact result-access runtime proof still required before ResultRead can leave NotTested.
    public static class HydraulicResultReadBoundary
    {
        public const string CapabilityIdentifierName = "ResultRead";
        public const string CapabilityStateName = "NotTested";
        public const string MissingRuntimeProof =
            "No accepted runtime evidence establishes the vendor result-access members, result-field semantics, " +
            "element-result collection contract or scenario/alternative result association. A Bentley adapter is " +
            "deliberately absent until that proof exists.";
    }
}
