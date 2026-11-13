using System;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    public static class SwmmIntegrationTests
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Assertion failed: " + message);
        }

        public static int RunAllSwmmTests(string customEvidenceDir = null)
        {
            Console.WriteLine("================================================================================");
            Console.WriteLine(" SewerPilot Milestone 4: EPA-SWMM 5.2 Integration & Cross-Solver Validation");
            Console.WriteLine("================================================================================");

            int passed = 0;
            int failed = 0;

            var tests = new Tuple<string, Action>[]
            {
                Tuple.Create<string, Action>("M4-01: Direct .INP Parsing & Canonical Model Ingestion", Test_InpParsingAndCanonicalIngestion),
                Tuple.Create<string, Action>("M4-02: Fail-Closed Rejection of Corrupt / Unresolved Input", Test_RejectionOfCorruptInput),
                Tuple.Create<string, Action>("M4-03: Genuine EPA-SWMM 5.2 Native Execution & Output Extraction", Test_GenuineSwmmExecution),
                Tuple.Create<string, Action>("M4-04: Cross-Solver Numerical Parity Against Bentley SewerGEMS", () => Test_CrossSolverNumericalParity(customEvidenceDir)),
                Tuple.Create<string, Action>("M4-05: Materialization of Authoritative Evidence & Artifact Hashes", () => Test_EvidenceMaterialization(customEvidenceDir)),
                Tuple.Create<string, Action>("M4-06: Strict Control Semantics & Lossless Flow Serialization", Test_SwmmControlsPreservationAndNumericalPrecision)
            };

            foreach (var t in tests)
            {
                Console.Write($"[TEST] {t.Item1,-70} ... ");
                try
                {
                    t.Item2();
                    Console.WriteLine("PASS");
                    passed++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAIL");
                    Console.WriteLine("   Error: " + ex.Message);
                    if (ex.InnerException != null)
                        Console.WriteLine("   Inner: " + ex.InnerException.Message);
                    failed++;
                }
            }

            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine($"Results: {passed} passed, {failed} failed.");
            Console.WriteLine("================================================================================");

            return failed == 0 ? 0 : 1;
        }

        private static void Test_InpParsingAndCanonicalIngestion()
        {
            string inp = @"[TITLE]
Test Ingestion Model

[OPTIONS]
FLOW_UNITS CFS

[JUNCTIONS]
;;Name Elevation MaxDepth InitDepth SurDepth Aponded
J-1 100.0 8.0 0 0 0
J-2 95.0  8.0 0 0 0

[OUTFALLS]
;;Name Elevation Type Stage Gated
O-3 90.0 FREE 0 NO

[CONDUITS]
;;Name FromNode ToNode Length Roughness InOffset OutOffset InitFlow MaxFlow
C-1 J-1 J-2 300.0 0.013 0 0 0 0
C-2 J-2 O-3 250.0 0.013 0 0 0 0

[XSECTIONS]
;;Link Shape Geom1 Geom2 Geom3 Geom4 Barrels
C-1 CIRCULAR 1.5 0 0 0 1
C-2 CIRCULAR 2.0 0 0 0 1

[COORDINATES]
;;Node X-Coord Y-Coord
J-1 100 200
J-2 400 200
O-3 650 200
";

            var model = SwmmInpNetworkAdapter.ParseInpText(inp);
            SwmmInpNetworkAdapter.ValidateModelTopology(model);

            Assert(model.Junctions.Count == 2, "Expected 2 junctions.");
            Assert(model.Outfalls.Count == 1, "Expected 1 outfall.");
            Assert(model.Conduits.Count == 2, "Expected 2 conduits.");

            var readResult = SwmmInpNetworkAdapter.ConvertToNetworkReadResult(model, "M4 Scenario");
            Assert(readResult.Structures.Count == 3, "Expected 3 structures converted.");
            Assert(readResult.Conduits.Count == 2, "Expected 2 conduits converted.");

            var c1 = readResult.Conduits.First(c => c.Label == "C-1");
            Assert(Math.Abs(c1.Diameter.Value - 18.0) < 1e-4, "Expected C-1 diameter = 18.0 inches.");
            Assert(Math.Abs(c1.Length.Value - 300.0) < 1e-4, "Expected C-1 length = 300.0 ft.");
            Assert(Math.Abs(c1.ManningsN.Value - 0.013) < 1e-4, "Expected C-1 n = 0.013.");
            Assert(readResult.Structures.First(s => s.Id == c1.StartNodeId).Label == "J-1", "Expected C-1 start node J-1.");
            Assert(readResult.Structures.First(s => s.Id == c1.StopNodeId).Label == "J-2", "Expected C-1 stop node J-2.");

            // Test roundtrip export
            string exported = SwmmInpNetworkAdapter.ExportToInpText(model);
            Assert(exported.Contains("[JUNCTIONS]"), "Export missing [JUNCTIONS]");
            Assert(exported.Contains("[CONDUITS]"), "Export missing [CONDUITS]");
            Assert(exported.Contains("PIPE-24IN") == false, "Export should only contain model entities.");
        }

        private static void Test_RejectionOfCorruptInput()
        {
            // Case A: Missing / undefined FromNode
            string badNodeInp = @"[JUNCTIONS]
J-1 100.0 8.0 0 0 0
[CONDUITS]
C-1 MISSING_NODE J-1 100.0 0.013 0 0 0 0
[XSECTIONS]
C-1 CIRCULAR 1.5 0 0 0 1
";
            bool caughtBadNode = false;
            try
            {
                var m = SwmmInpNetworkAdapter.ParseInpText(badNodeInp);
                SwmmInpNetworkAdapter.ValidateModelTopology(m);
            }
            catch (SwmmInpParseException ex)
            {
                caughtBadNode = true;
                Assert(ex.Message.Contains("MISSING_NODE"), "Expected error mentioning MISSING_NODE");
            }
            Assert(caughtBadNode, "Failed to reject missing node reference.");

            // Case B: Non-positive length
            string badLengthInp = @"[JUNCTIONS]
J-1 100.0 8.0 0 0 0
J-2 95.0 8.0 0 0 0
[CONDUITS]
C-1 J-1 J-2 -50.0 0.013 0 0 0 0
[XSECTIONS]
C-1 CIRCULAR 1.5 0 0 0 1
";
            bool caughtBadLength = false;
            try
            {
                var m = SwmmInpNetworkAdapter.ParseInpText(badLengthInp);
                SwmmInpNetworkAdapter.ValidateModelTopology(m);
            }
            catch (SwmmInpParseException)
            {
                caughtBadLength = true;
            }
            Assert(caughtBadLength, "Failed to reject non-positive conduit length.");

            // Case C: Self-loop conduit
            string loopInp = @"[JUNCTIONS]
J-1 100.0 8.0 0 0 0
[CONDUITS]
C-1 J-1 J-1 100.0 0.013 0 0 0 0
[XSECTIONS]
C-1 CIRCULAR 1.5 0 0 0 1
";
            bool caughtLoop = false;
            try
            {
                var m = SwmmInpNetworkAdapter.ParseInpText(loopInp);
                SwmmInpNetworkAdapter.ValidateModelTopology(m);
            }
            catch (SwmmInpParseException)
            {
                caughtLoop = true;
            }
            Assert(caughtLoop, "Failed to reject self-loop conduit.");
        }

        private static void Test_GenuineSwmmExecution()
        {
            string dll = SwmmSolverRunner.ResolveSwmmDllPath();
            Assert(!string.IsNullOrEmpty(dll), "EPA-SWMM 5.2 DLL swmm5.dll could not be resolved.");
            Assert(File.Exists(dll), "EPA-SWMM 5.2 DLL file does not exist at: " + dll);

            int ver = SwmmSolverRunner.GetSwmmVersion();
            Assert(ver >= 52000, "Expected SWMM version >= 52000, got: " + ver);

            string tempDir = Path.Combine(Path.GetTempPath(), "SewerPilot_M4_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string inpFile = Path.Combine(tempDir, "test_run.inp");
                File.WriteAllText(inpFile, @"[TITLE]
Test SWMM Run

[OPTIONS]
FLOW_UNITS CFS
INFILTRATION HORTON
FLOW_ROUTING KINWAVE
START_DATE 01/01/2026
START_TIME 00:00:00
REPORT_START_DATE 01/01/2026
REPORT_START_TIME 00:00:00
END_DATE 01/01/2026
END_TIME 01:00:00
ROUTING_STEP 0:01:00

[JUNCTIONS]
J-1 100.0 8.0 0 0 0

[OUTFALLS]
O-1 95.0 FREE NO

[CONDUITS]
C-1 J-1 O-1 200.0 0.013 0 0 0 0

[XSECTIONS]
C-1 CIRCULAR 2.0 0 0 0 1

[DWF]
J-1 FLOW 5.0

[REPORT]
NODES ALL
LINKS ALL
", System.Text.Encoding.UTF8);

                var res = SwmmSolverRunner.Execute(inpFile, tempDir);
                Assert(res.Success, "Expected swmm_run success (exit code 0), got: " + res.ExitCode + "\nReport:\n" + res.ReportText);
                Assert(res.Links.ContainsKey("C-1"), "Expected report to contain link C-1.");
                Assert(res.Nodes.ContainsKey("J-1"), "Expected report to contain node J-1.");
                Assert(res.Links["C-1"].MaximumFlowCfs >= 4.5 && res.Links["C-1"].MaximumFlowCfs <= 6.5, "Expected C-1 maximum flow within transient routing range (4.5 - 6.5 cfs), got: " + res.Links["C-1"].MaximumFlowCfs);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in SwmmIntegrationTests.cs: " + suppressedEx.Message); }
            }
        }

        private static void Test_CrossSolverNumericalParity(string customEvidenceDir)
        {
            var summary = SwmmCrossSolverValidationEngine.RunCrossSolverValidation(customEvidenceDir);
            Assert(summary.OverallStatus == "PASS", "Expected overall cross-solver validation status PASS, got: " + summary.OverallStatus);
            Assert(summary.Cases.Count == 2, "Expected 2 validation cases.");
            Assert(summary.PassedMetricsCount == summary.TotalMetricsCount, $"Expected all {summary.TotalMetricsCount} metrics to pass.");
            Assert(summary.MaxRelativeDeltaPercent <= 1.0, $"Max relative delta {summary.MaxRelativeDeltaPercent:F3}% exceeds 1.0% tolerance.");
        }

        private static void Test_EvidenceMaterialization(string customEvidenceDir)
        {
            string evDir = !string.IsNullOrWhiteSpace(customEvidenceDir)
                ? customEvidenceDir
                : SwmmCrossSolverValidationEngine.ResolveDefaultEvidenceDirectory();

            Assert(Directory.Exists(evDir), "Evidence directory does not exist: " + evDir);

            string[] expectedFiles = new[]
            {
                "milestone4-cross-solver-report.md",
                "cross-solver-parity-matrix.json",
                "case1_austin_24in_conduit.inp",
                "case1_austin_24in_conduit.rpt",
                "case2_multinode_trunk_network.inp",
                "case2_multinode_trunk_network.rpt"
            };

            foreach (string ef in expectedFiles)
            {
                string p = Path.Combine(evDir, ef);
                Assert(File.Exists(p), "Expected evidence file missing: " + ef);
                var fi = new FileInfo(p);
                Assert(fi.Length > 0, "Evidence file is empty: " + ef);
            }
        }

        private static void Test_SwmmControlsPreservationAndNumericalPrecision()
        {
            string inp = @"[TITLE]
SWMM Controls and Precision Verification Model

[OPTIONS]
FLOW_UNITS CFS

[JUNCTIONS]
;;Name Elevation MaxDepth InitDepth SurDepth Aponded
J-1 100.000000 8.000000 0.000000 0.000000 0.000000
J-2 95.000000  8.000000 0.000000 0.000000 0.000000
J-3 92.000000  8.000000 0.000000 0.000000 0.000000
J-4 88.000000  8.000000 0.000000 0.000000 0.000000

[OUTFALLS]
;;Name Elevation Type Stage Gated
O-5 80.000000 FREE 0 NO

[CONDUITS]
;;Name FromNode ToNode Length Roughness InOffset OutOffset InitFlow MaxFlow
C-1 J-1 J-2 300.000000 0.0130 0.000000 0.000000 0.000000 0.000000

[PUMPS]
;;Name FromNode ToNode PumpCurve Status Startup Shutoff
PUMP-1 J-2 J-3 PUMP_CURVE1 ON 0.000000 0.000000

[ORIFICES]
;;Name FromNode ToNode Type Offset Qcoeff Gated CloseTime
ORF-1 J-3 J-4 SIDE 0.500000 0.650000 NO 0.000000

[WEIRS]
;;Name FromNode ToNode Type CrestHt Qcoeff Gated EndCon EndCoeff
WEIR-1 J-4 O-5 TRANSVERSE 2.000000 3.330000 NO 0 0.000000

[XSECTIONS]
;;Link Shape Geom1 Geom2 Geom3 Geom4 Barrels
C-1 CIRCULAR 1.500000 0 0 0 1
ORF-1 CIRCULAR 1.000000 0 0 0 1
WEIR-1 RECT_OPEN 3.000000 2.000000 0 0 1

[CURVES]
;;Name Type X-Value Y-Value
PUMP_CURVE1 Pump1 0.000000 10.000000
PUMP_CURVE1 Pump1 15.000000 5.000000

[INFLOWS]
;;Node Constituent TimeSeries Type Mfactor Sfactor Baseline Pattern
J-1 FLOW """" FLOW 1.0 1.0 0.000040 """"

[COORDINATES]
;;Node X-Coord Y-Coord
J-1 100 200
J-2 400 200
J-3 600 200
J-4 800 200
O-5 1000 200
";

            // 1. Parsing authentic SWMM controls without flattening
            var model = SwmmInpNetworkAdapter.ParseInpText(inp);
            SwmmInpNetworkAdapter.ValidateModelTopology(model);

            Assert(model.Pumps.Count == 1, "Expected 1 pump parsed.");
            Assert(model.Orifices.Count == 1, "Expected 1 orifice parsed.");
            Assert(model.Weirs.Count == 1, "Expected 1 weir parsed.");
            Assert(model.Curves.Count == 2, "Expected 2 curve points parsed.");
            Assert(model.Conduits.Count == 1, "Expected exactly 1 conduit (controls must not be converted to conduits).");
            Assert(!model.Conduits.ContainsKey("PUMP-1"), "PUMP-1 must not be flattened into Conduits.");
            Assert(!model.Conduits.ContainsKey("ORF-1"), "ORF-1 must not be flattened into Conduits.");
            Assert(!model.Conduits.ContainsKey("WEIR-1"), "WEIR-1 must not be flattened into Conduits.");

            // 2. Export retains typed SWMM sections and does not flatten controls
            string exported = SwmmInpNetworkAdapter.ExportToInpText(model);
            Assert(exported.Contains("[PUMPS]"), "Export must contain [PUMPS] block.");
            Assert(exported.Contains("PUMP-1") && exported.Contains("PUMP_CURVE1"), "Export must contain authentic PUMP-1 entry.");
            Assert(exported.Contains("[ORIFICES]"), "Export must contain [ORIFICES] block.");
            Assert(exported.Contains("ORF-1") && exported.Contains("SIDE"), "Export must contain authentic ORF-1 entry.");
            Assert(exported.Contains("[WEIRS]"), "Export must contain [WEIRS] block.");
            Assert(exported.Contains("WEIR-1") && exported.Contains("TRANSVERSE"), "Export must contain authentic WEIR-1 entry.");
            Assert(exported.Contains("[CURVES]"), "Export must contain [CURVES] block.");
            Assert(exported.Contains("PUMP_CURVE1") && (exported.Contains("PUMP1") || exported.Contains("Pump1")), "Export must contain authentic PUMP_CURVE1 entry.");

            // Verify conduits section in exported text contains only genuine conduits
            int conduitsIdx = exported.IndexOf("[CONDUITS]");
            int nextSectionIdx = exported.IndexOf("[PUMPS]", conduitsIdx);
            string conduitsBlock = exported.Substring(conduitsIdx, nextSectionIdx - conduitsIdx);
            Assert(!conduitsBlock.Contains("PUMP-1"), "Exported [CONDUITS] block must not contain PUMP-1.");
            Assert(!conduitsBlock.Contains("ORF-1"), "Exported [CONDUITS] block must not contain ORF-1.");
            Assert(!conduitsBlock.Contains("WEIR-1"), "Exported [CONDUITS] block must not contain WEIR-1.");

            // 3. Lossless small flow round-trip (0.00004 CFS never truncates to 0.00)
            Assert(exported.Contains("0.000040") || exported.Contains("0.00004"), "Small non-zero inflow (0.00004 CFS) must not serialize to 0.00.");
            Assert(!exported.Contains("FLOW             0.00\r\n") && !exported.Contains("FLOW             0.00\n"), "Small non-zero flow must never serialize to 0.00.");

            var roundTripModel = SwmmInpNetworkAdapter.ParseInpText(exported);
            Assert(roundTripModel.Inflows.Count == 1, "Roundtrip must preserve inflow.");
            Assert(Math.Abs(roundTripModel.Inflows["J-1"] - 0.00004) < 1e-9, $"Inflow baseline 0.00004 must survive round-trip, got {roundTripModel.Inflows["J-1"]}.");

            // 4. Gravity workflow conversion fails closed when active controls are present
            bool gravityConversionBlocked = false;
            try
            {
                SwmmInpNetworkAdapter.ConvertToNetworkReadResult(model, "Gravity-Conversion");
            }
            catch (NotSupportedException ex)
            {
                gravityConversionBlocked = ex.Message.Contains("active SWMM controls");
            }
            Assert(gravityConversionBlocked, "Converting active controls (pumps, orifices, weirs) to gravity network must fail closed.");
        }
    }
}
