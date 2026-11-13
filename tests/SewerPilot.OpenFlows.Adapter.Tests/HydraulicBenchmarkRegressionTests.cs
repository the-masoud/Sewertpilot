using System;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static void Benchmark_SuiteExecution_AllCasesPassNumericParity()
        {
            var summary = HydraulicBenchmarkRegressionHarness.RunFullBenchmarkSuite(1.0);

            Check(summary != null, "Benchmark suite summary is not null.");
            Check(summary.TotalCases == 5, $"Expected 5 benchmark cases, got {summary.TotalCases}.");
            Check(summary.PassedCases == 5, $"Expected all 5 cases to pass, got {summary.PassedCases} passed.");
            Check(summary.FailedCases == 0, $"Expected 0 failed cases, got {summary.FailedCases}.");
            Check(summary.TotalMetricsEvaluated >= 20, $"Expected at least 20 evaluated metrics, got {summary.TotalMetricsEvaluated}.");
            Check(summary.PassedMetricsCount == summary.TotalMetricsEvaluated, "Every single metric is within tolerance.");
            Check(summary.MaxObservedDeviationPercent <= 1.0, $"Max deviation ({summary.MaxObservedDeviationPercent:F3}%) must be <= 1.0%.");
            Check(summary.MeanAbsoluteRelativeErrorPercent < 0.50, $"Mean absolute relative error ({summary.MeanAbsoluteRelativeErrorPercent:F3}%) is well within 0.5%.");
            Check(summary.IsSuitePassing, "IsSuitePassing must be true.");
            Check(summary.SuiteVerdict.Contains("PASS"), "SuiteVerdict contains PASS.");
        }

        private static void Benchmark_Case1_ManningGravityFlow_ParityChecks()
        {
            var summary = HydraulicBenchmarkRegressionHarness.RunFullBenchmarkSuite(1.0);
            var case1 = summary.Cases.FirstOrDefault(c => c.CaseId == "BENCH-01-CIRCULAR-MANNING");

            Check(case1 != null, "Case 1 must be present.");
            Check(case1.OverallParity, "Case 1 must achieve overall parity.");
            Check(case1.Metrics.Count == 4, $"Case 1 must evaluate 4 metrics, got {case1.Metrics.Count}.");

            var qFull = case1.Metrics.First(m => m.MetricName == "FullFlowCapacity");
            Check(qFull.IsWithinTolerance, "FullFlowCapacity must be within tolerance.");
            Check(qFull.RelativePercentDelta <= 0.05, $"FullFlowCapacity delta ({qFull.RelativePercentDelta}%) <= 0.05%.");

            var vFull = case1.Metrics.First(m => m.MetricName == "FullFlowVelocity");
            Check(vFull.IsWithinTolerance, "FullFlowVelocity must be within tolerance.");

            var yn = case1.Metrics.First(m => m.MetricName == "PartialNormalDepth");
            Check(yn.IsWithinTolerance, "PartialNormalDepth must be within tolerance.");
            Check(yn.RelativePercentDelta <= 0.10, $"PartialNormalDepth delta ({yn.RelativePercentDelta}%) <= 0.10%.");
        }

        private static void Benchmark_Case2_CascadingBackwaterSweep_ParityChecks()
        {
            var summary = HydraulicBenchmarkRegressionHarness.RunFullBenchmarkSuite(1.0);
            var case2 = summary.Cases.FirstOrDefault(c => c.CaseId == "BENCH-02-CASCADING-BACKWATER");

            Check(case2 != null, "Case 2 must be present.");
            Check(case2.OverallParity, "Case 2 must achieve overall parity.");
            Check(case2.Metrics.Count == 6, $"Case 2 must evaluate 6 metrics, got {case2.Metrics.Count}.");

            var ofHgl = case2.Metrics.First(m => m.MetricName == "OutfallTailwaterHGL");
            Check(ofHgl.IsWithinTolerance && ofHgl.AbsoluteDelta == 0.0, "Outfall tailwater boundary must be exact.");

            var mh2Hgl = case2.Metrics.First(m => m.MetricName == "JunctionNode2HGL");
            Check(mh2Hgl.IsWithinTolerance, "MH-2 HGL must be within tolerance.");
            Check(mh2Hgl.AbsoluteDelta <= 0.02, $"MH-2 HGL absolute delta ({mh2Hgl.AbsoluteDelta} ft) <= 0.02 ft.");

            var mh2Egl = case2.Metrics.First(m => m.MetricName == "JunctionNode2EGL");
            Check(mh2Egl.IsWithinTolerance, "MH-2 EGL must be within tolerance.");

            var hf = case2.Metrics.First(m => m.MetricName == "ConduitFrictionLoss_C23");
            Check(hf.IsWithinTolerance, "Conduit friction loss must be within tolerance.");
        }

        private static void Benchmark_Case3_RationalHydrology_ParityChecks()
        {
            var summary = HydraulicBenchmarkRegressionHarness.RunFullBenchmarkSuite(1.0);
            var case3 = summary.Cases.FirstOrDefault(c => c.CaseId == "BENCH-03-RATIONAL-HYDROLOGY");

            Check(case3 != null, "Case 3 must be present.");
            Check(case3.OverallParity, "Case 3 must achieve overall parity.");
            Check(case3.Metrics.Count == 3, $"Case 3 must evaluate 3 metrics, got {case3.Metrics.Count}.");

            var intensity = case3.Metrics.First(m => m.MetricName == "RainfallIntensity");
            Check(intensity.IsWithinTolerance, "RainfallIntensity must be within tolerance.");

            var qPeak = case3.Metrics.First(m => m.MetricName == "PeakRunoffDischarge");
            Check(qPeak.IsWithinTolerance, "PeakRunoffDischarge must be within tolerance.");
            Check(qPeak.RelativePercentDelta <= 0.05, $"Peak discharge delta ({qPeak.RelativePercentDelta}%) <= 0.05%.");
        }

        private static void Benchmark_Case4_GutterAndInletInterception_ParityChecks()
        {
            var summary = HydraulicBenchmarkRegressionHarness.RunFullBenchmarkSuite(1.0);
            var case4 = summary.Cases.FirstOrDefault(c => c.CaseId == "BENCH-04-GUTTER-INLET-HEC22");

            Check(case4 != null, "Case 4 must be present.");
            Check(case4.OverallParity, "Case 4 must achieve overall parity.");
            Check(case4.Metrics.Count == 5, $"Case 4 must evaluate 5 metrics, got {case4.Metrics.Count}.");

            var spread = case4.Metrics.First(m => m.MetricName == "GutterSpread");
            Check(spread.IsWithinTolerance, "GutterSpread must be within tolerance.");
            Check(spread.RelativePercentDelta <= 0.05, $"GutterSpread delta ({spread.RelativePercentDelta}%) <= 0.05%.");

            var eff = case4.Metrics.First(m => m.MetricName == "CaptureEfficiency");
            Check(eff.IsWithinTolerance, "CaptureEfficiency must be within tolerance.");

            var qi = case4.Metrics.First(m => m.MetricName == "InterceptedFlow");
            Check(qi.IsWithinTolerance, "InterceptedFlow must be within tolerance.");

            var qb = case4.Metrics.First(m => m.MetricName == "BypassFlow");
            Check(qb.IsWithinTolerance, "BypassFlow must be within tolerance.");
        }

        private static void Benchmark_Case5_SurchargeHydraulics_ParityChecks()
        {
            var summary = HydraulicBenchmarkRegressionHarness.RunFullBenchmarkSuite(1.0);
            var case5 = summary.Cases.FirstOrDefault(c => c.CaseId == "BENCH-05-SURCHARGE-HYDRAULICS");

            Check(case5 != null, "Case 5 must be present.");
            Check(case5.OverallParity, "Case 5 must achieve overall parity.");
            Check(case5.Metrics.Count == 5, $"Case 5 must evaluate 5 metrics, got {case5.Metrics.Count}.");

            var upHgl = case5.Metrics.First(m => m.MetricName == "UpstreamSurchargedHGL");
            Check(upHgl.IsWithinTolerance, "Upstream surcharged HGL must be within tolerance.");
            Check(upHgl.AbsoluteDelta <= 0.02, $"Surcharged HGL delta ({upHgl.AbsoluteDelta} ft) <= 0.02 ft.");

            var fb = case5.Metrics.First(m => m.MetricName == "NodeRemainingFreeboard");
            Check(fb.IsWithinTolerance, "Node remaining freeboard must be within tolerance.");
        }

        private static void Benchmark_FailClosed_ExceededToleranceDetected()
        {
            // Inject a test metric that exceeds the 1.0% parity threshold
            var failingMetric = HydraulicParityMetric.Create(
                "ArtificialDivergenceMetric",
                "TEST-PIPE",
                10.00,
                10.25, // 2.5% divergence (> 1.0% limit)
                "cfs",
                1.0,
                0.001);

            Check(!failingMetric.IsWithinTolerance, "Failing metric must not be within tolerance.");
            Check(failingMetric.Status == "FAIL", "Status must be FAIL.");
            Check(failingMetric.RelativePercentDelta == 2.5, "RelativePercentDelta must be exactly 2.5%.");

            var mockCase = new HydraulicBenchmarkCase
            {
                CaseId = "MOCK-CASE",
                Title = "Mock Test Case"
            };
            mockCase.Metrics.Add(failingMetric);

            Check(!mockCase.OverallParity, "Mock case must fail parity.");
            Check(mockCase.Verdict == "FAIL", "Verdict must be FAIL.");
        }

        private static void Benchmark_MaterializeAuthoritativeEvidence()
        {
            string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Evidence", "benchmarks", "20261019-openflows-parity");
            string reportPath = HydraulicBenchmarkRegressionHarness.MaterializeEvidence(outDir);

            Check(File.Exists(reportPath), $"Report file must exist at {reportPath}.");
            string md = File.ReadAllText(reportPath);
            Check(md.Contains("# SewerPilot vs. Bentley OpenFlows — Hydraulic Parity Benchmark Report"), "Markdown report header verified.");
            Check(md.Contains("PASS (100% PARITY)"), "Markdown report contains PASS (100% PARITY).");
            Check(md.Contains("BENCH-01-CIRCULAR-MANNING"), "Markdown report contains Case 1.");
            Check(md.Contains("BENCH-02-CASCADING-BACKWATER"), "Markdown report contains Case 2.");
            Check(md.Contains("BENCH-03-RATIONAL-HYDROLOGY"), "Markdown report contains Case 3.");
            Check(md.Contains("BENCH-04-GUTTER-INLET-HEC22"), "Markdown report contains Case 4.");
            Check(md.Contains("BENCH-05-SURCHARGE-HYDRAULICS"), "Markdown report contains Case 5.");

            string jsonPath = Path.Combine(outDir, "benchmark-parity-matrix.json");
            Check(File.Exists(jsonPath), $"JSON file must exist at {jsonPath}.");
            string json = File.ReadAllText(jsonPath);
            Check(json.Contains("OPENFLOWS-BENCHMARK-SUITE-V1"), "JSON matrix contains suite identifier.");
            Check(json.Contains("\"IsSuitePassing\":true"), "JSON matrix confirms IsSuitePassing=true.");

            // Materialize also to canonical repository docs path
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SewerPilot.sln")))
            {
                dir = dir.Parent;
            }
            string repoDocsDir = Path.Combine(dir != null ? dir.FullName : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..")), "docs", "evidence", "benchmarks", "20261019-openflows-parity");
            if (!Directory.Exists(repoDocsDir)) Directory.CreateDirectory(repoDocsDir);
            File.WriteAllText(Path.Combine(repoDocsDir, "benchmark-regression-report.md"), md);
            File.WriteAllText(Path.Combine(repoDocsDir, "benchmark-parity-matrix.json"), json);
            Check(File.Exists(Path.Combine(repoDocsDir, "benchmark-regression-report.md")), "Repo docs markdown report verified.");
            Check(File.Exists(Path.Combine(repoDocsDir, "benchmark-parity-matrix.json")), "Repo docs JSON matrix verified.");
        }
    }
}
