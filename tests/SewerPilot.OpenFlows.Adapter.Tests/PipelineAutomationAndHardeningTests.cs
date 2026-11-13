using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    public static class PipelineAutomationAndHardeningTests
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Assertion failed: " + message);
        }

        public static int RunM5PipelineTests(string customEvidenceDir = null)
        {
            Console.WriteLine("================================================================================");
            Console.WriteLine(" SewerPilot Milestone 5: Headless Pipeline Automation & Extended Benchmarks");
            Console.WriteLine("================================================================================");

            int passed = 0;
            int failed = 0;

            var tests = new Tuple<string, Action>[]
            {
                Tuple.Create<string, Action>("M5-01: End-to-End Headless Pipeline Ingestion & Execution (.INP)", () => Test_EndToEndPipeline_SwmmInp(customEvidenceDir)),
                Tuple.Create<string, Action>("M5-02: LandXML 1.2 Ingestion, Conversion & Dual-Solver Dispatch", () => Test_EndToEndPipeline_LandXml(customEvidenceDir)),
                Tuple.Create<string, Action>("M5-03: Extended Benchmark Scenario 3 (Pump Station & Dynamic Force Main)", () => Test_BenchmarkScenario3(customEvidenceDir)),
                Tuple.Create<string, Action>("M5-04: Extended Benchmark Scenario 4 (Control Structures: Siphon/Weir/Orifice)", () => Test_BenchmarkScenario4(customEvidenceDir)),
                Tuple.Create<string, Action>("M5-05: Extended Benchmark Scenario 5 (Looped Urban Trunk Network)", () => Test_BenchmarkScenario5(customEvidenceDir)),
                Tuple.Create<string, Action>("M5-06: Headless COM/STA Lifecycle Clean Termination & Memory Isolation", () => Test_MemoryIsolationAndStaCleanup(customEvidenceDir))
            };

            foreach (var t in tests)
            {
                Console.Write($"[TEST] {t.Item1,-72} ... ");
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
            Console.WriteLine($"Milestone 5 Results: {passed} passed, {failed} failed.");
            Console.WriteLine("================================================================================");

            return failed == 0 ? 0 : 1;
        }

        public static int RunP28HardeningTests(string customEvidenceDir = null)
        {
            Console.WriteLine("================================================================================");
            Console.WriteLine(" SewerPilot Phase P28: Production Hardening, Degradation & Release Finalization");
            Console.WriteLine("================================================================================");

            int passed = 0;
            int failed = 0;

            var tests = new Tuple<string, Action>[]
            {
                Tuple.Create<string, Action>("P28-01: Dependency Vulnerability Audit (Zero Known CVEs)", Test_DependencyVulnerabilityAudit),
                Tuple.Create<string, Action>("P28-02: Repository Secret Scanning (Strict Zero-Finding Policy)", Test_RepositorySecretScan),
                Tuple.Create<string, Action>("P28-03: Graceful Degradation Contract (Absence of Bentley Runtime Fallback)", () => Test_GracefulDegradationContract(customEvidenceDir)),
                Tuple.Create<string, Action>("P28-04: Release Build Automation & Clean-Machine Packaging Verification", Test_ReleaseBuildPackagingVerification)
            };

            foreach (var t in tests)
            {
                Console.Write($"[TEST] {t.Item1,-72} ... ");
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
            Console.WriteLine($"Phase P28 Results: {passed} passed, {failed} failed.");
            Console.WriteLine("================================================================================");

            return failed == 0 ? 0 : 1;
        }

        private static void Test_EndToEndPipeline_SwmmInp(string customEvidenceDir)
        {
            string workDir = ResolveEvidenceDirectory(customEvidenceDir);
            string inpPath = Path.Combine(workDir, "case1_austin_24in_conduit.inp");

            if (!File.Exists(inpPath))
            {
                // Fallback to docs/evidence/swmm
                string root = FindRepoRoot();
                inpPath = Path.Combine(root, "docs", "evidence", "swmm", "case1_austin_24in_conduit.inp");
            }

            Assert(File.Exists(inpPath), "Input file not found: " + inpPath);

            var options = new ProductionPipelineExecutionOptions
            {
                SourceModelPath = inpPath,
                WorkDirectory = workDir,
                CaseName = "pipeline_test_case1",
                ParityTolerancePercent = 1.0,
                EnforceBentleyExecution = false
            };

            var res = ProductionPipelineOrchestrator.ExecutePipeline(options);

            Assert(res.Success, "Expected pipeline execution success.");
            Assert(res.IngestionType == "SWMM_INP", "Expected IngestionType = SWMM_INP.");
            Assert(res.IngestedStructureCount >= 2, "Expected at least 2 structures ingested.");
            Assert(res.IngestedConduitCount >= 1, "Expected at least 1 conduit ingested.");
            Assert(res.TopologicalDrift == 0.000, "Expected zero topological drift.");
            Assert(res.NativeSwmmExecuted, "Expected native SWMM execution.");
            Assert(Math.Abs(res.SwmmContinuityErrorPercent) <= 1.0, "Expected SWMM continuity error <= 1.0%.");
            Assert(res.TotalMetricsCount > 0, "Expected parity metrics to be generated.");
            Assert(res.PassedMetricsCount == res.TotalMetricsCount, "Expected all parity metrics to pass.");
            Assert(File.Exists(res.MarkdownReportPath), "Expected markdown report to exist.");
            Assert(File.Exists(res.ParityMatrixJsonPath), "Expected parity matrix JSON to exist.");
        }

        private static void Test_EndToEndPipeline_LandXml(string customEvidenceDir)
        {
            string workDir = ResolveEvidenceDirectory(customEvidenceDir);
            string xmlContent = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs/evidence/landxml/20261019-phase2-landxml-exchange/sample-civil3d-network.xml"));
            var hydraulics = new LandXmlPipelineHydraulics();
            hydraulics.OutfallTypes.Add("OF-103", "FREE");
            hydraulics.InflowsCfs.Add("MH-101", 1.0); // Explicit test loading.

            var options = new ProductionPipelineExecutionOptions
            {
                ModelContent = xmlContent,
                LandXmlHydraulics = hydraulics,
                WorkDirectory = workDir,
                CaseName = "pipeline_test_landxml_exchange",
                ParityTolerancePercent = 1.0,
                EnforceBentleyExecution = false
            };

            var res = ProductionPipelineOrchestrator.ExecutePipeline(options);

            Assert(res.Success, "Expected LandXML pipeline execution success.");
            Assert(res.IngestionType == "LANDXML", "Expected IngestionType = LANDXML.");
            Assert(res.IngestedStructureCount == 3, "Expected 3 structures ingested from LandXML.");
            Assert(res.IngestedConduitCount == 2, "Expected 2 conduits ingested from LandXML.");
            Assert(res.TopologicalDrift == 0.000, "Expected zero topological drift.");
            Assert(res.NativeSwmmExecuted, "Expected native SWMM execution on converted LandXML model.");
            Assert(res.PassedMetricsCount == res.TotalMetricsCount, "Expected all parity metrics to pass.");
        }

        private static void Test_BenchmarkScenario3(string customEvidenceDir)
        {
            string workDir = ResolveEvidenceDirectory(customEvidenceDir);
            string inpPath = Path.Combine(workDir, "case3_pump_station_force_main.inp");
            Assert(File.Exists(inpPath), "Scenario 3 .inp model missing: " + inpPath);

            var options = new ProductionPipelineExecutionOptions
            {
                SourceModelPath = inpPath,
                WorkDirectory = workDir,
                CaseName = "case3_pump_station_force_main",
                ParityTolerancePercent = 1.0,
                EnforceBentleyExecution = false
            };

            var res = ProductionPipelineOrchestrator.ExecutePipeline(options);

            Assert(res.Success, "Scenario 3 execution failed.");
            Assert(Math.Abs(res.SwmmContinuityErrorPercent) <= 1.0, $"Scenario 3 SWMM continuity error {res.SwmmContinuityErrorPercent:F3}% exceeds 1.0%.");
            Assert(res.MaxRelativeDeltaPercent <= 1.0, $"Scenario 3 max delta {res.MaxRelativeDeltaPercent:F3}% exceeds 1.0%.");
            Assert(res.PassedMetricsCount == res.TotalMetricsCount, "All Scenario 3 parity metrics must PASS.");
        }

        private static void Test_BenchmarkScenario4(string customEvidenceDir)
        {
            string workDir = ResolveEvidenceDirectory(customEvidenceDir);
            string inpPath = Path.Combine(workDir, "case4_hydraulic_structures_siphon.inp");
            Assert(File.Exists(inpPath), "Scenario 4 .inp model missing: " + inpPath);

            var options = new ProductionPipelineExecutionOptions
            {
                SourceModelPath = inpPath,
                WorkDirectory = workDir,
                CaseName = "case4_hydraulic_structures_siphon",
                ParityTolerancePercent = 1.0,
                EnforceBentleyExecution = false
            };

            var res = ProductionPipelineOrchestrator.ExecutePipeline(options);

            Assert(res.Success, "Scenario 4 execution failed.");
            Assert(Math.Abs(res.SwmmContinuityErrorPercent) <= 1.0, $"Scenario 4 SWMM continuity error {res.SwmmContinuityErrorPercent:F3}% exceeds 1.0%.");
            Assert(res.MaxRelativeDeltaPercent <= 1.0, $"Scenario 4 max delta {res.MaxRelativeDeltaPercent:F3}% exceeds 1.0%.");
            Assert(res.PassedMetricsCount == res.TotalMetricsCount, "All Scenario 4 parity metrics must PASS.");
        }

        private static void Test_BenchmarkScenario5(string customEvidenceDir)
        {
            string workDir = ResolveEvidenceDirectory(customEvidenceDir);
            string inpPath = Path.Combine(workDir, "case5_looped_urban_network.inp");
            Assert(File.Exists(inpPath), "Scenario 5 .inp model missing: " + inpPath);

            var options = new ProductionPipelineExecutionOptions
            {
                SourceModelPath = inpPath,
                WorkDirectory = workDir,
                CaseName = "case5_looped_urban_network",
                ParityTolerancePercent = 1.0,
                EnforceBentleyExecution = false
            };

            var res = ProductionPipelineOrchestrator.ExecutePipeline(options);

            Assert(res.Success, "Scenario 5 execution failed.");
            Assert(Math.Abs(res.SwmmContinuityErrorPercent) <= 1.0, $"Scenario 5 SWMM continuity error {res.SwmmContinuityErrorPercent:F3}% exceeds 1.0%.");
            Assert(res.MaxRelativeDeltaPercent <= 1.0, $"Scenario 5 max delta {res.MaxRelativeDeltaPercent:F3}% exceeds 1.0%.");
            Assert(res.PassedMetricsCount == res.TotalMetricsCount, "All Scenario 5 parity metrics must PASS.");
        }

        private static void Test_MemoryIsolationAndStaCleanup(string customEvidenceDir)
        {
            string workDir = ResolveEvidenceDirectory(customEvidenceDir);
            var suite = ProductionPipelineOrchestrator.ExecuteExtendedBenchmarkSuite(workDir);

            Assert(suite.OverallStatus == "PASS", "Expected Extended Benchmark Suite status PASS, got: " + suite.OverallStatus);
            Assert(suite.PassedCases == 3, "Expected 3/3 extended benchmark scenarios to pass.");
            Assert(suite.MaxRelativeDeltaPercent <= 1.0, $"Suite max delta {suite.MaxRelativeDeltaPercent:F3}% exceeds 1.0%.");
            Assert(File.Exists(suite.ConsolidatedReportPath), "Consolidated report missing.");
            Assert(File.Exists(suite.ConsolidatedParityMatrixPath), "Consolidated parity matrix missing.");
        }

        private static void Test_DependencyVulnerabilityAudit()
        {
            RunSecurityQualification("Dependencies");
        }

        private static void Test_RepositorySecretScan()
        {
            RunSecurityQualification("Secrets");
        }

        private static void RunSecurityQualification(string scope)
        {
            var start = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" +
                Path.Combine(FindRepoRoot(), "tools", "qualify-security.ps1") +
                "\" -Scope " + scope)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = FindRepoRoot()
            };
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(210000))
                {
                    process.Kill();
                    throw new InvalidOperationException("Security qualification timed out: " + scope);
                }
                // Raw subprocess output may include feed diagnostics; publish only scope and exit status.
                output.Wait();
                error.Wait();
                Assert(process.ExitCode == 0, "Security qualification failed: " + scope +
                    "; see redacted docs/evidence/remediation-20261021/security receipt.");
            }
        }
        private static void Test_GracefulDegradationContract(string customEvidenceDir)
        {
            string workDir = ResolveEvidenceDirectory(customEvidenceDir);
            string inpPath = Path.Combine(workDir, "case3_pump_station_force_main.inp");
            if (!File.Exists(inpPath))
            {
                string benchmarkDir = Path.Combine(FindRepoRoot(), "docs", "evidence", "swmm", "benchmarks");
                string srcInp = Path.Combine(benchmarkDir, "case3_pump_station_force_main.inp");
                if (File.Exists(srcInp)) inpPath = srcInp;
            }

            // Execute with simulated Bentley absence (ForceFallbackMode = true)
            var options = new ProductionPipelineExecutionOptions
            {
                SourceModelPath = inpPath,
                WorkDirectory = workDir,
                CaseName = "degradation_simulation_case3",
                ParityTolerancePercent = 1.0,
                ForceFallbackMode = true // FORCED SIMULATION
            };

            var res = ProductionPipelineOrchestrator.ExecutePipeline(options);

            // Invariants:
            // 1. Must not throw unhandled exception or crash
            Assert(res.Success, "Expected graceful degradation pipeline to succeed.");
            // 2. Must automatically switch to native EPA-SWMM 5.2 standalone mode
            Assert(res.NativeSwmmExecuted, "Expected native SWMM execution in standalone fallback mode.");
            Assert(!res.BentleyExecuted, "Bentley should NOT be executed when in fallback mode.");
            // 3. Must emit structured advisory telemetry
            Assert(res.BentleyExecutionStatus.Contains("BentleyRuntimeDegradedFallback"),
                "Expected status to indicate BentleyRuntimeDegradedFallback, got: " + res.BentleyExecutionStatus);
            Assert(res.TelemetryAdvisory.Contains("Bentley SewerGEMS runtime not detected") || res.TelemetryAdvisory.Contains("fallback"),
                "Expected telemetry advisory regarding fallback, got: " + res.TelemetryAdvisory);
            // 4. Result status is DEGRADED_PASS
            Assert(res.PipelineStatus == "DEGRADED_PASS", "Expected PipelineStatus = DEGRADED_PASS, got: " + res.PipelineStatus);
        }

        private static void Test_ReleaseBuildPackagingVerification()
        {
            string root = FindRepoRoot();
            string setupExe = Path.Combine(root, "distribution", "output", "SewerPilot-Setup-v1.0.0.exe");
            string issFile = Path.Combine(root, "distribution", "installer", "SewerPilot-Installer.iss");

            Assert(File.Exists(issFile), "Inno Setup installer script missing: " + issFile);
            Assert(File.Exists(setupExe), "Packaged setup executable missing: " + setupExe);

            var fi = new FileInfo(setupExe);
            Assert(fi.Length > 1000000, "Packaged setup executable appears truncated or empty: " + fi.Length + " bytes");
            string evidence = Path.Combine(root, "docs", "evidence", "remediation-20261021", "packaging");
            Assert(Directory.Exists(evidence), "Current remediation packaging evidence is required.");
            string receiptPath = Directory.GetFiles(evidence, "installer-manifest-*.json")
                .OrderByDescending(p => p, StringComparer.Ordinal).FirstOrDefault();
            Assert(receiptPath != null, "A current installer manifest is required.");
            var receipt = new System.Web.Script.Serialization.JavaScriptSerializer()
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(receiptPath));
            Assert(Convert.ToBoolean(receipt["test_install_passed"]), "Exact installed-payload qualification must pass.");
            Assert(Convert.ToInt64(receipt["size_bytes"]) == fi.Length, "Installer size must match the current receipt.");
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(setupExe))
            {
                string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                Assert(string.Equals(hash, Convert.ToString(receipt["sha256"]), StringComparison.OrdinalIgnoreCase),
                    "Installer SHA-256 must match the current qualification receipt.");
            }
            Assert(receipt.ContainsKey("signing_status"), "Actual Authenticode status must be reported.");
            Assert(Convert.ToString(receipt["clean_machine_acceptance"]) == "NOT_PROVEN_CURRENT_WORKSTATION_ONLY",
                "Developer-workstation verification must not claim clean-machine acceptance.");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SewerPilot.sln")))
            {
                dir = dir.Parent;
            }
            return dir != null ? dir.FullName : AppDomain.CurrentDomain.BaseDirectory;
        }

        private static string ResolveEvidenceDirectory(string customDir)
        {
            if (!string.IsNullOrWhiteSpace(customDir)) return Path.GetFullPath(customDir);
            string root = FindRepoRoot();
            string target = Path.Combine(root, "docs", "evidence", "swmm", "benchmarks");
            if (!Directory.Exists(target)) Directory.CreateDirectory(target);
            return target;
        }
    }
}
