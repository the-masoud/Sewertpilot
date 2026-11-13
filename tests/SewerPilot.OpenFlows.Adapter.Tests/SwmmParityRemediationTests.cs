using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static void SwmmParity_TimeoutTerminatesOwnedJobWithoutVendorExecution()
        {
            Check(SwmmBentleyWorker.VerifyTimeoutIsolation(), "The same worker timeout supervisor terminates its owned process job and confirms zero active processes without invoking Bentley.");
        }
        private static void SwmmParity_TrueRelativeDeltaAndFiniteGate()
        {
            var small = SwmmParityMetric.Evaluate("small", "fixture", 0.1, 0.105, "cfs", 1.0, 0.02);
            Check(small.Status == "FAIL" && Math.Abs(small.RelativePercentDelta - 5.0) < 1e-8,
                "A small absolute delta cannot mask five percent relative error.");
            var exact = SwmmParityMetric.Evaluate("zero", "fixture", 0, 0, "cfs");
            Check(exact.Status == "PASS" && exact.RelativePercentDelta == 0, "Observed zero equality is retained.");
            bool rejected = false;
            try { SwmmParityMetric.Evaluate("missing", "fixture", double.NaN, 1, "cfs"); }
            catch (InvalidDataException error) { rejected = error.Message.Contains("PARITY_COMPARISON_FAILED"); }
            Check(rejected, "An unavailable observation cannot become a passing zero.");
        }

        private static void SwmmParity_ImportedIdentityMapNoGuessedAliases()
        {
            var report = new BentleySwmmReportResult();
            report.Links["9127"] = new BentleyLinkSummary { Id = "9127", MaxFlowCfs = 12.37, MaxVelocityFps = 3.42 };
            report.Links["719_842"] = new BentleyLinkSummary { Id = "719_842", Type = "ORIFICE", MaxFlowCfs = 8.27 };
            report.Nodes["6402"] = new BentleyNodeSummary { Id = "6402", MaxDepthFeet = 2.1, MaxHglFeet = 104.2 };
            SwmmCrossSolverValidationEngine.ApplyElementLabelMap(report, new Dictionary<string, string>
            {
                ["9127"] = "custom-conduit", ["719_842"] = "custom-orifice", ["6402"] = "custom-junction"
            });
            Check(SwmmCrossSolverValidationEngine.RequireLink(report, "custom-conduit").MaxFlowCfs == 12.37, "Arbitrary persisted ID maps to original link label.");
            Check(SwmmCrossSolverValidationEngine.RequireLink(report, "custom-orifice").MaxFlowCfs == 8.27, "Explicit persisted composite control identity maps without an allocation guess.");
            Check(SwmmCrossSolverValidationEngine.RequireNode(report, "custom-junction").MaxHglFeet == 104.2, "Arbitrary persisted node identity maps to original label.");
            bool rejected = false;
            try { SwmmCrossSolverValidationEngine.RequireLink(report, "PIPE-24IN"); }
            catch (InvalidDataException error) { rejected = error.Message.Contains("PARITY_COMPARISON_FAILED"); }
            Check(rejected, "Missing reference never falls back to a benchmark constant.");
            bool ambiguous = false;
            try { SwmmCrossSolverValidationEngine.ApplyElementLabelMap(report, new Dictionary<string, string> { ["719_842"] = "custom-conduit" }); }
            catch (InvalidDataException) { ambiguous = true; }
            Check(ambiguous, "Conflicting original label aliases fail closed.");
        }

        private static void SwmmParity_ReportParsingMissingAndMalformedObservations()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SewerPilot-ParityFixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string reportPath = Path.Combine(directory, "fixture.rpt");
            File.WriteAllText(reportPath, "EPA STORM WATER MANAGEMENT MODEL - VERSION fixture\nContinuity Error (%) 0.003\nNode Depth Summary\n----------------\nNode header\n----------------\n6402 JUNCTION 1.0 2.1 104.2\n\nLink Flow Summary\n----------------\nLink header\n----------------\n9127 CONDUIT 12.37 0 00:01 3.42 0.2 0.3\n719_842 ORIFICE 8.27 0 00:01\n\n");
            var report = SwmmCrossSolverValidationEngine.ParseBentleySwmmReport(reportPath);
            Check(report.Nodes["6402"].MaxDepthFeet == 2.1 && report.Links["9127"].MaxVelocityFps == 3.42, "Report parser preserves measured finite values.");
            Check(report.Links["719_842"].MaxFlowCfs == 8.27 && double.IsNaN(report.Links["719_842"].MaxVelocityFps), "Control flow is observed, absent control velocity remains unavailable.");
            Check(!report.Links.ContainsKey("PIPE-24IN") && !report.Nodes.ContainsKey("J-1"), "Parser invents no scenario-specific labels.");
            File.WriteAllText(reportPath, File.ReadAllText(reportPath).Replace("12.37", "broken"));
            bool rejected = false;
            try { SwmmCrossSolverValidationEngine.ParseBentleySwmmReport(reportPath); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Malformed observed hydraulic values fail closed.");
            bool missing = false;
            try { SwmmCrossSolverValidationEngine.ReadElementLabelMapFromSqlite(Path.Combine(directory, "missing.sqlite")); }
            catch (InvalidDataException) { missing = true; }
            Check(missing && !File.Exists(Path.Combine(directory, "missing.sqlite")), "Missing identity database is never created by the reader.");
        }
    }
}
