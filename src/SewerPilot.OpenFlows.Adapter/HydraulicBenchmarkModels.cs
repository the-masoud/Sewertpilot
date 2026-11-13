using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    /// <summary>
    /// Represents an individual evaluated hydraulic metric comparing Bentley OpenFlows solver reference
    /// against SewerPilot analytical calculation output with an enforced numeric parity tolerance threshold.
    /// </summary>
    public sealed class HydraulicParityMetric
    {
        public string MetricName { get; set; }
        public string ElementId { get; set; }
        public double BentleyReference { get; set; }
        public double SewerPilotValue { get; set; }
        public double AbsoluteDelta { get; set; }
        public double RelativePercentDelta { get; set; }
        public double ToleranceThresholdPercent { get; set; } = 1.0; // Default 1.0% parity threshold
        public double AbsoluteToleranceFloor { get; set; } = 0.01; // Floor threshold for near-zero quantities (e.g. 0.01 cfs or 0.02 ft)
        public string Unit { get; set; }
        public bool IsWithinTolerance { get; set; }
        public string Status => IsWithinTolerance ? "PASS" : "FAIL";

        public static HydraulicParityMetric Create(
            string metricName,
            string elementId,
            double bentleyReference,
            double sewerPilotValue,
            string unit,
            double tolerancePercent = 1.0,
            double absoluteFloor = 0.01)
        {
            if (double.IsNaN(bentleyReference) || double.IsInfinity(bentleyReference) ||
                double.IsNaN(sewerPilotValue) || double.IsInfinity(sewerPilotValue) ||
                double.IsNaN(tolerancePercent) || double.IsInfinity(tolerancePercent) || tolerancePercent <= 0.0)
            {
                throw new InvalidOperationException("PARITY_COMPARISON_FAILED: finite observations and a positive tolerance are required.");
            }

            double absDelta = Math.Abs(sewerPilotValue - bentleyReference);
            double relPercent = bentleyReference == 0.0
                ? (absDelta == 0.0 ? 0.0 : 100.0)
                : (absDelta / Math.Abs(bentleyReference)) * 100.0;

            // Retain the measured delta; absolute floors do not override the relative percentage gate
            bool compliant = relPercent <= tolerancePercent;

            return new HydraulicParityMetric
            {
                MetricName = metricName,
                ElementId = elementId,
                BentleyReference = Math.Round(bentleyReference, 4),
                SewerPilotValue = Math.Round(sewerPilotValue, 4),
                AbsoluteDelta = Math.Round(absDelta, 4),
                RelativePercentDelta = Math.Round(relPercent, 3),
                ToleranceThresholdPercent = tolerancePercent,
                AbsoluteToleranceFloor = absoluteFloor,
                Unit = unit,
                IsWithinTolerance = compliant
            };
        }
    }

    /// <summary>
    /// Represents a canonical hydraulic benchmark test case executing across an engineering reach or network.
    /// </summary>
    public sealed class HydraulicBenchmarkCase
    {
        public string CaseId { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string StandardsBasis { get; set; }
        public List<HydraulicParityMetric> Metrics { get; set; } = new List<HydraulicParityMetric>();

        public double MaxRelativeDeltaPercent =>
            Metrics.Count > 0 ? Metrics.Max(m => m.RelativePercentDelta) : 0.0;

        public double MaxAbsoluteDelta =>
            Metrics.Count > 0 ? Metrics.Max(m => m.AbsoluteDelta) : 0.0;

        public bool OverallParity =>
            Metrics.Count > 0 && Metrics.All(m => m.IsWithinTolerance);

        public string Verdict => OverallParity ? "PASS" : "FAIL";
    }

    /// <summary>
    /// Comprehensive summary matrix of the automated benchmark regression suite comparing
    /// SewerPilot calculated metrics against Bentley OpenFlows calculated outputs.
    /// </summary>
    public sealed class HydraulicBenchmarkSuiteSummary
    {
        public string SuiteId { get; set; } = "OPENFLOWS-BENCHMARK-SUITE-V1";
        public string SuiteName { get; set; } = "SewerPilot vs. Bentley OpenFlows Hydraulic Parity Benchmark Regression";
        public DateTime ExecutionTimestampUtc { get; set; } = DateTime.UtcNow;
        public string AuthoritativeEngineer { get; set; } = Environment.UserName;
        public string TargetToleranceDescription { get; set; } = "Relative Error <= 1.0% (Delta Q, Delta HGL, Delta V, Delta T <= 1%)";
        public List<HydraulicBenchmarkCase> Cases { get; set; } = new List<HydraulicBenchmarkCase>();

        public int TotalCases => Cases.Count;
        public int PassedCases => Cases.Count(c => c.OverallParity);
        public int FailedCases => Cases.Count(c => !c.OverallParity);

        public int TotalMetricsEvaluated => Cases.Sum(c => c.Metrics.Count);
        public int PassedMetricsCount => Cases.Sum(c => c.Metrics.Count(m => m.IsWithinTolerance));

        public double MaxObservedDeviationPercent =>
            Cases.Count > 0 && TotalMetricsEvaluated > 0
                ? Cases.SelectMany(c => c.Metrics).Max(m => m.RelativePercentDelta)
                : 0.0;

        public double MeanAbsoluteRelativeErrorPercent =>
            Cases.Count > 0 && TotalMetricsEvaluated > 0
                ? Cases.SelectMany(c => c.Metrics).Average(m => m.RelativePercentDelta)
                : 0.0;

        public double RootMeanSquareRelativeErrorPercent
        {
            get
            {
                if (Cases.Count == 0 || TotalMetricsEvaluated == 0) return 0.0;
                var allDeltas = Cases.SelectMany(c => c.Metrics).Select(m => m.RelativePercentDelta).ToList();
                double sumSquares = allDeltas.Sum(d => d * d);
                return Math.Sqrt(sumSquares / allDeltas.Count);
            }
        }

        public bool IsSuitePassing => TotalCases > 0 && FailedCases == 0 && MaxObservedDeviationPercent <= 1.0;
        public string SuiteVerdict => IsSuitePassing ? "PASS (100% PARITY)" : "FAILED";
    }
}
