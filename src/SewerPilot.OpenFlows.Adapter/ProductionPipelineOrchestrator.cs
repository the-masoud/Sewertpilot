using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class ProductionPipelineExecutionOptions
    {
        public string SourceModelPath { get; set; }
        public string ModelContent { get; set; }
        public string WorkDirectory { get; set; }
        public bool EnforceBentleyExecution { get; set; }
        public bool ForceFallbackMode { get; set; }
        public double ParityTolerancePercent { get; set; } = 1.0;
        public string CaseName { get; set; }
        public LandXmlPipelineHydraulics LandXmlHydraulics { get; set; }
    }

    // LandXML contains geometry, not an authorized hydraulic loading/boundary model.
    public sealed class LandXmlPipelineHydraulics
    {
        public Dictionary<string, double> InflowsCfs { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> OutfallTypes { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> FixedStagesFeet { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ProductionPipelineResult
    {
        public bool Success { get; set; }
        public string PipelineStatus { get; set; } = "UNKNOWN";
        public string IngestionType { get; set; }
        public int IngestedStructureCount { get; set; }
        public int IngestedConduitCount { get; set; }
        public double TopologicalDrift { get; set; }
        public bool BentleyTopologyMeasured { get; set; }
        public double? SourceConversionMaximumDeltaFeet { get; set; }

        public bool NativeSwmmExecuted { get; set; }
        public double SwmmContinuityErrorPercent { get; set; }
        public long SwmmElapsedMilliseconds { get; set; }
        public string SwmmVersion { get; set; }

        public bool BentleyExecuted { get; set; }
        public string BentleyExecutionStatus { get; set; }
        public double BentleyContinuityErrorPercent { get; set; }
        public long BentleyElapsedMilliseconds { get; set; }
        public string BentleyRuntimeAssemblyPath { get; set; }

        public string TelemetryAdvisory { get; set; }

        public int TotalMetricsCount { get; set; }
        public int PassedMetricsCount { get; set; }
        public double MaxRelativeDeltaPercent { get; set; }
        public double MeanRelativeDeltaPercent { get; set; }
        public List<SwmmParityMetric> ParityMetrics { get; } = new List<SwmmParityMetric>();

        public long MemoryBeforeBytes { get; set; }
        public long MemoryAfterBytes { get; set; }
        public long MemoryDeltaBytes => MemoryAfterBytes - MemoryBeforeBytes;

        public string MarkdownReportPath { get; set; }
        public string ParityMatrixJsonPath { get; set; }
        public Dictionary<string, string> ArtifactSha256Hashes { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> ExecutionLog { get; } = new List<string>();
    }

    public sealed class ExtendedBenchmarkSuiteResult
    {
        public string OverallStatus { get; set; }
        public DateTime ExecutionTimestampUtc { get; set; }
        public int TotalCases { get; set; }
        public int PassedCases { get; set; }
        public int TotalMetricsCount { get; set; }
        public int PassedMetricsCount { get; set; }
        public double MaxRelativeDeltaPercent { get; set; }
        public double MeanRelativeDeltaPercent { get; set; }
        public List<ProductionPipelineResult> CaseResults { get; } = new List<ProductionPipelineResult>();
        public Dictionary<string, string> ArtifactSha256Hashes { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string ConsolidatedReportPath { get; set; }
        public string ConsolidatedParityMatrixPath { get; set; }
    }

    public static class ProductionPipelineOrchestrator
    {
        public const double DefaultParityTolerancePercent = 1.0;

        public static string ResolveBentleyRuntimeRoot()
        {
            var options = new OpenFlowsDiscoveryOptions(Environment.GetEnvironmentVariable("SEWERPILOT_OPENFLOWS_INSTALL_ROOT"));
            var selection = new OpenFlowsRuntimeSelector().Select(new OpenFlowsRuntimeDiscovery().Discover(options), options);
            return selection.Candidate?.InstallRoot;
        }

        public static bool IsBentleyRuntimeAvailable()
        {
            try
            {
                if (!Environment.Is64BitOperatingSystem) return false;
                string root = ResolveBentleyRuntimeRoot();
                if (string.IsNullOrEmpty(root)) return false;
                string sewerGemsDll = Path.Combine(root, "Haestad.SewerGEMS.dll");
                string swmmDll = Path.Combine(root, "swmm5.dll");
                string licDll = Path.Combine(root, "Haestad.LicensingFacade.dll");

                return File.Exists(sewerGemsDll) && File.Exists(swmmDll) && File.Exists(licDll);
            }
            catch
            {
                return false;
            }
        }

        public static ProductionPipelineResult ExecutePipeline(ProductionPipelineExecutionOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var result = new ProductionPipelineResult();
            long memStart = GC.GetTotalMemory(true);
            result.MemoryBeforeBytes = memStart;

            string workDir = !string.IsNullOrWhiteSpace(options.WorkDirectory)
                ? Path.GetFullPath(options.WorkDirectory)
                : Path.Combine(Path.GetTempPath(), "SewerPilot_Pipeline_" + Guid.NewGuid().ToString("N"));

            if (!Directory.Exists(workDir)) Directory.CreateDirectory(workDir);

            string caseName = !string.IsNullOrWhiteSpace(options.CaseName)
                ? options.CaseName
                : (!string.IsNullOrWhiteSpace(options.SourceModelPath) ? Path.GetFileNameWithoutExtension(options.SourceModelPath) : "unnamed_scenario");
            if (caseName == "." || caseName == ".." || Path.GetFileName(caseName) != caseName || caseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("CaseName must be a single valid file name.");

            result.ExecutionLog.Add($"[{DateTime.UtcNow:O}] Pipeline started for case '{caseName}' in work dir '{workDir}'.");

            // =========================================================================
            // Stage 1: Model Ingestion (.inp / LandXML)
            // =========================================================================
            string rawContent = options.ModelContent;
            if (string.IsNullOrEmpty(rawContent) && !string.IsNullOrEmpty(options.SourceModelPath) && File.Exists(options.SourceModelPath))
            {
                rawContent = File.ReadAllText(options.SourceModelPath, Encoding.UTF8);
            }

            if (string.IsNullOrWhiteSpace(rawContent))
            {
                result.PipelineStatus = "FAIL";
                result.TelemetryAdvisory = "Model ingestion failed: empty or missing model source.";
                result.ExecutionLog.Add("FATAL: Source model content is empty.");
                return result;
            }

            string effectiveInpPath = null;
            bool isLandXml = rawContent.TrimStart().StartsWith("<", StringComparison.Ordinal);

            if (isLandXml)
            {
                result.IngestionType = "LANDXML";
                result.ExecutionLog.Add("Detected LandXML 1.2 source payload. Ingesting via LandXmlPipeNetworkAdapter...");

                var readResult = LandXmlPipeNetworkAdapter.ReadLandXml(rawContent, new LandXmlImportOptions { DefaultManningsN = double.NaN });

                result.IngestedStructureCount = readResult.Structures.Count;
                result.IngestedConduitCount = readResult.Conduits.Count;
                result.TopologicalDrift = 0.000;

                // Convert LandXml to SWMM Inp representation
                var swmmModel = ConvertNetworkReadResultToSwmmInp(readResult, caseName, options.LandXmlHydraulics);
                string exportedInp = SwmmInpNetworkAdapter.ExportToInpText(swmmModel);
                var roundTrip = SwmmInpNetworkAdapter.ParseInpText(exportedInp);
                result.TopologicalDrift = MeasureConversionDrift(swmmModel, roundTrip);
                result.SourceConversionMaximumDeltaFeet = result.TopologicalDrift;
                if (result.TopologicalDrift > 1e-6) throw new InvalidOperationException("LANDXML_CONVERSION_FIDELITY_FAILED");

                effectiveInpPath = Path.Combine(workDir, $"{caseName}_ingested_from_landxml.inp");
                File.WriteAllText(effectiveInpPath, exportedInp, Encoding.UTF8);
                result.ExecutionLog.Add($"LandXML converted to canonical SWMM INP: {result.IngestedStructureCount} structures, {result.IngestedConduitCount} conduits.");
            }
            else
            {
                result.IngestionType = "SWMM_INP";
                result.ExecutionLog.Add("Detected EPA-SWMM .INP payload. Ingesting and validating topology...");

                var swmmModel = SwmmInpNetworkAdapter.ParseInpText(rawContent);
                SwmmInpNetworkAdapter.ValidateModelTopology(swmmModel);

                result.IngestedStructureCount = swmmModel.Junctions.Count + swmmModel.Outfalls.Count;
                result.IngestedConduitCount = swmmModel.Conduits.Count;
                result.TopologicalDrift = 0.000;

                effectiveInpPath = Path.Combine(workDir, $"{caseName}.inp");
                File.WriteAllText(effectiveInpPath, rawContent, Encoding.UTF8);
                result.ExecutionLog.Add($"EPA-SWMM INP validated: {result.IngestedStructureCount} nodes, {result.IngestedConduitCount} links.");
            }

            // =========================================================================
            // Stage 2: Independent, sequential Dual-Solver Calculation
            // =========================================================================
            // 2A: Native EPA-SWMM 5.2 Solver
            result.ExecutionLog.Add("Executing Native EPA-SWMM 5.2 engine...");
            var sw = Stopwatch.StartNew();
            SwmmExecutionResult swmmExec;
            try { swmmExec = SwmmSolverRunner.Execute(effectiveInpPath, workDir); }
            catch (Exception ex)
            {
                result.PipelineStatus = ex is InvalidDataException ? "NATIVE_SWMM_RESULT_INVALID" : "NATIVE_SWMM_UNAVAILABLE";
                result.TelemetryAdvisory = "Native SWMM failure: " + ex.GetType().Name + ": " + ex.Message;
                MaterializeArtifactsAndHashes(workDir, caseName, result);
                return result;
            }
            sw.Stop();

            result.NativeSwmmExecuted = swmmExec.Success;
            result.SwmmContinuityErrorPercent = swmmExec.ContinuityErrorPercent;
            result.SwmmElapsedMilliseconds = sw.ElapsedMilliseconds;
            result.SwmmVersion = $"EPA-SWMM 5.2.4 (Build {SwmmSolverRunner.GetSwmmVersion()})";
            result.ExecutionLog.Add($"EPA-SWMM execution completed in {sw.ElapsedMilliseconds} ms. ExitCode={swmmExec.ExitCode}, ContinuityError={swmmExec.ContinuityErrorPercent:F3}%.");

            // 2B: Bentley SewerGEMS Solver (with Graceful Degradation Guard)
            bool bentleyAvailable = IsBentleyRuntimeAvailable();
            bool shouldRunBentley = bentleyAvailable && !options.ForceFallbackMode;

            BentleyLiveExecutionResult bentleyLive = null;
            if (shouldRunBentley)
            {
                result.ExecutionLog.Add("Bentley SewerGEMS runtime verified. Launching headless STA solver worker...");
                string sandboxDir = Path.Combine(Path.GetTempPath(), "SewerPilot_Live_SewerGEMS", caseName);
                try
                {
                    bentleyLive = SwmmCrossSolverValidationEngine.ExecuteLiveBentleySewerGems(effectiveInpPath, sandboxDir, caseName, workDir);
                    result.BentleyExecuted = bentleyLive.Success;
                    result.BentleyExecutionStatus = bentleyLive.ComputeStatus ?? "NormalExecution";
                    result.BentleyContinuityErrorPercent = bentleyLive.Report?.ContinuityErrorPercent ?? 0.0;
                    result.BentleyElapsedMilliseconds = bentleyLive.ElapsedMilliseconds;
                    result.BentleyRuntimeAssemblyPath = bentleyLive.RuntimeAssemblyPath;
                    result.TopologicalDrift = MeasureImportedTopologyDrift(effectiveInpPath, bentleyLive);
                    result.BentleyTopologyMeasured = true;
                    result.TelemetryAdvisory = "Bentley SewerGEMS / OpenFlows GVF-SWMM dual-solver execution successful.";
                    result.ExecutionLog.Add($"Bentley SewerGEMS calculation completed in {bentleyLive.ElapsedMilliseconds} ms. ContinuityError={result.BentleyContinuityErrorPercent:F3}%.");
                }
                catch (Exception bEx)
                {
                    result.BentleyExecuted = false;
                    result.BentleyExecutionStatus = "BentleyExecutionException: " + bEx.Message;
                    result.TelemetryAdvisory = "WARNING: Bentley solver execution threw exception. Falling back gracefully to EPA-SWMM standalone mode.";
                    result.ExecutionLog.Add("WARNING: Bentley solver exception: " + bEx.Message);
                }
            }
            else
            {
                result.BentleyExecuted = false;
                result.BentleyExecutionStatus = options.ForceFallbackMode
                    ? "BentleyRuntimeDegradedFallback (Forced Simulation)"
                    : "BentleyRuntimeDegradedFallback (Bentley Not Installed)";
                result.TelemetryAdvisory = "ADVISORY: Bentley SewerGEMS runtime not detected or operating in fallback mode. Switched to native EPA-SWMM 5.2 standalone solver.";
                result.ExecutionLog.Add("ADVISORY: " + result.BentleyExecutionStatus + " - Operating in EPA-SWMM standalone mode.");
            }

            // =========================================================================
            // Stage 3: Dynamic Hydrodynamic Extraction & Parity Evaluation
            // =========================================================================
            result.ExecutionLog.Add("Extracting hydrodynamic parameters and evaluating parity...");
            double tol = options.ParityTolerancePercent > 0 ? options.ParityTolerancePercent : DefaultParityTolerancePercent;

            bool comparisonFailed = options.EnforceBentleyExecution && (!result.BentleyExecuted || bentleyLive?.Report == null);
            if (result.BentleyExecuted && bentleyLive?.Report != null)
            {
                // Full dual-solver cross-comparison against live Bentley results
                try { EvaluateDualSolverParity(swmmExec, bentleyLive.Report, result, tol); }
                catch (Exception ex) when (ex is InvalidOperationException || ex is InvalidDataException)
                {
                    comparisonFailed = true;
                    result.TelemetryAdvisory = ex.Message;
                }
            }
            else
            {
                // Graceful degradation standalone evaluation: verifies SWMM continuity and stability invariants
                EvaluateStandaloneParity(swmmExec, result, tol);
            }

            // Summary stats
            result.TotalMetricsCount = result.ParityMetrics.Count;
            result.PassedMetricsCount = result.ParityMetrics.Count(m => m.Status == "PASS");
            result.MaxRelativeDeltaPercent = result.ParityMetrics.Count > 0 ? result.ParityMetrics.Max(m => m.RelativePercentDelta) : 0.0;
            result.MeanRelativeDeltaPercent = result.ParityMetrics.Count > 0 ? result.ParityMetrics.Average(m => m.RelativePercentDelta) : 0.0;

            bool allMetricsPass = (result.PassedMetricsCount == result.TotalMetricsCount);
            bool continuityPass = Math.Abs(result.SwmmContinuityErrorPercent) <= tol &&
                (!result.BentleyExecuted || (result.BentleyTopologyMeasured && result.TopologicalDrift == 0));
            if (result.BentleyExecuted)
            {
                continuityPass = continuityPass && (Math.Abs(result.BentleyContinuityErrorPercent) <= tol);
            }

            if (!comparisonFailed && result.TotalMetricsCount > 0 && allMetricsPass && continuityPass && result.NativeSwmmExecuted)
            {
                result.PipelineStatus = result.BentleyExecuted ? "PASS" : "DEGRADED_PASS";
                result.Success = true;
            }
            else
            {
                result.PipelineStatus = comparisonFailed ? "PARITY_COMPARISON_FAILED" : "FAIL";
                result.Success = false;
            }

            // =========================================================================
            // Stage 4: Clean STA Thread & COM Lifecycle Termination
            // =========================================================================
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long memEnd = GC.GetTotalMemory(true);
            result.MemoryAfterBytes = memEnd;
            result.ExecutionLog.Add($"Managed memory delta: {result.MemoryDeltaBytes / 1024} KB; this measurement alone is not a native leak test.");

            // =========================================================================
            // Stage 5: Parity Reporting & Artifact Materialization
            // =========================================================================
            MaterializeArtifactsAndHashes(workDir, caseName, result);

            return result;
        }

        public static ExtendedBenchmarkSuiteResult ExecuteExtendedBenchmarkSuite(string targetEvidenceDirectory = null)
        {
            var suite = new ExtendedBenchmarkSuiteResult
            {
                ExecutionTimestampUtc = DateTime.UtcNow
            };

            string workDir = !string.IsNullOrWhiteSpace(targetEvidenceDirectory)
                ? Path.GetFullPath(targetEvidenceDirectory)
                : ResolveDefaultBenchmarkEvidenceDirectory();

            if (!Directory.Exists(workDir)) Directory.CreateDirectory(workDir);

            // Extended Benchmark Definitions
            var scenarios = new[]
            {
                new { Id = "M5-BENCH-03-PUMP-STATION", Name = "case3_pump_station_force_main", Desc = "Scenario 3 (Pump Station & Dynamic Force Main)" },
                new { Id = "M5-BENCH-04-HYDRAULIC-STRUCTURES", Name = "case4_hydraulic_structures_siphon", Desc = "Scenario 4 (Complex Hydraulic Control Structures: Siphon, Weir, Orifice)" },
                new { Id = "M5-BENCH-05-LOOPED-NETWORK", Name = "case5_looped_urban_network", Desc = "Scenario 5 (Looped Urban Trunk Network with Backwater)" }
            };

            suite.TotalCases = scenarios.Length;

            foreach (var sc in scenarios)
            {
                string inpFile = Path.Combine(workDir, sc.Name + ".inp");
                if (!File.Exists(inpFile))
                {
                    // If not present in benchmark directory, materialize canonical text.
                    // Fail closed rather than emitting an empty deck that would run as a valid simulation.
                    string content = GetScenarioInpContent(sc.Name);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        throw new InvalidOperationException(
                            "Failed to materialize SWMM INP payload for model '" + sc.Name + "'. Writing empty simulation decks is prohibited.");
                    }
                    File.WriteAllText(inpFile, content, Encoding.UTF8);
                }

                var opt = new ProductionPipelineExecutionOptions
                {
                    SourceModelPath = inpFile,
                    WorkDirectory = workDir,
                    CaseName = sc.Name,
                    ParityTolerancePercent = 1.0,
                    EnforceBentleyExecution = true
                };

                var res = ExecutePipeline(opt);
                suite.CaseResults.Add(res);
                if (res.Success && res.BentleyExecuted && res.PipelineStatus == "PASS") suite.PassedCases++;
            }

            var allMetrics = suite.CaseResults.SelectMany(c => c.ParityMetrics).ToList();
            suite.TotalMetricsCount = allMetrics.Count;
            suite.PassedMetricsCount = allMetrics.Count(m => m.Status == "PASS");
            suite.MaxRelativeDeltaPercent = allMetrics.Count > 0 ? allMetrics.Max(m => m.RelativePercentDelta) : 0.0;
            suite.MeanRelativeDeltaPercent = allMetrics.Count > 0 ? allMetrics.Average(m => m.RelativePercentDelta) : 0.0;

            suite.OverallStatus = (suite.PassedCases == suite.TotalCases && suite.PassedMetricsCount == suite.TotalMetricsCount)
                ? "PASS"
                : "FAIL";

            ComputeSuiteFileHashes(workDir, suite);

            // Materialize Consolidated Benchmark Matrix and Markdown Report
            string mdReport = GenerateSuiteMarkdownReport(suite);

            string mdPath = Path.Combine(workDir, "extended-benchmark-regression-report.md");
            string jsonPath = Path.Combine(workDir, "extended-benchmark-parity-matrix.json");

            File.WriteAllText(mdPath, mdReport, Encoding.UTF8);
            suite.ConsolidatedReportPath = mdPath;
            suite.ConsolidatedParityMatrixPath = jsonPath;
            File.WriteAllText(jsonPath, new JavaScriptSerializer().Serialize(suite), Encoding.UTF8);

            return suite;
        }

        private static void EvaluateDualSolverParity(SwmmExecutionResult swmm, BentleySwmmReportResult bentley, ProductionPipelineResult result, double tol)
        {
            var source = SwmmInpNetworkAdapter.ReadInpFile(swmm.InpPath);
            if (!source.Conduits.Keys.All(swmm.Links.ContainsKey) ||
                !source.Junctions.Keys.Concat(source.Outfalls.Keys).All(swmm.Nodes.ContainsKey))
                throw new InvalidDataException("PARITY_COMPARISON_FAILED: Native report omits source elements.");
            // Evaluate all links
            foreach (var kvp in swmm.Links)
            {
                string linkName = kvp.Key;
                var sLink = kvp.Value;

                if (bentley.Links.TryGetValue(linkName, out var bLink))
                {
                    result.ParityMetrics.Add(SwmmParityMetric.Evaluate(
                        "ConduitPeakDischarge", linkName, bLink.MaxFlowCfs, sLink.MaximumFlowCfs, "cfs", tol, 0.02));
                    if (string.Equals(sLink.Type, "CONDUIT", StringComparison.OrdinalIgnoreCase))
                        result.ParityMetrics.Add(SwmmParityMetric.Evaluate(
                            "ConduitPeakVelocity", linkName, bLink.MaxVelocityFps, sLink.MaximumVelocityFps, "fps", tol, 0.02));
                }
                else
                {
                    throw new InvalidOperationException("PARITY_COMPARISON_FAILED: Missing Bentley link reference: " + linkName);
                }
            }

            // Evaluate all nodes
            foreach (var kvp in swmm.Nodes)
            {
                string nodeName = kvp.Key;
                var sNode = kvp.Value;

                if (bentley.Nodes.TryGetValue(nodeName, out var bNode))
                {
                    result.ParityMetrics.Add(SwmmParityMetric.Evaluate(
                        "JunctionMaxDepth", nodeName, bNode.MaxDepthFeet, sNode.MaximumDepthFeet, "ft", tol, 0.02));
                    result.ParityMetrics.Add(SwmmParityMetric.Evaluate(
                        "JunctionMaxHGL", nodeName, bNode.MaxHglFeet, sNode.MaximumHglFeet, "ft", tol, 0.05));
                }
                else
                {
                    throw new InvalidOperationException("PARITY_COMPARISON_FAILED: Missing Bentley node reference: " + nodeName);
                }
            }

            // Continuity error parity
            result.ParityMetrics.Add(SwmmParityMetric.Evaluate(
                "ContinuityErrorConvergence", "GLOBAL_SYSTEM", bentley.ContinuityErrorPercent, swmm.ContinuityErrorPercent, "%", tol, 0.10));
        }

        private static void EvaluateStandaloneParity(SwmmExecutionResult swmm, ProductionPipelineResult result, double tol)
        {
            // This is a native continuity gate, never cross-solver parity or a stability proof.
            result.ParityMetrics.Add(new SwmmParityMetric {
                MetricName = "StandaloneContinuityLimit", ElementId = "GLOBAL_SYSTEM", Unit = "%",
                SwmmCalculatedValue = swmm.ContinuityErrorPercent, AbsoluteDelta = Math.Abs(swmm.ContinuityErrorPercent),
                RelativePercentDelta = 0, ToleranceThresholdPercent = tol,
                Status = Finite(swmm.ContinuityErrorPercent) && Math.Abs(swmm.ContinuityErrorPercent) <= tol ? "PASS" : "FAIL" });
        }

        public static SwmmModel ConvertNetworkReadResultToSwmmInp(NetworkReadResult readResult, string scenarioTitle, LandXmlPipelineHydraulics hydraulics)
        {
            if (readResult == null) throw new ArgumentNullException(nameof(readResult));
            if (hydraulics == null || hydraulics.OutfallTypes.Count == 0 || hydraulics.InflowsCfs.Count == 0)
                throw new InvalidOperationException("LANDXML_ENGINEERING_INPUT_REQUIRED: Explicit outfall boundaries and inflows required.");
            var model = new SwmmModel { Title = scenarioTitle };
            model.Options["FLOW_UNITS"] = "CFS";
            model.Options["FLOW_ROUTING"] = "DYNWAVE";
            model.Options["INFILTRATION"] = "CURVE_NUMBER";
            model.Options["ALLOW_PONDING"] = "YES";
            model.Options["INERTIAL_DAMPING"] = "NONE";
            model.Options["VARIABLE_STEP"] = "0";
            model.Options["LENGTHENING_STEP"] = "0";
            model.Options["MIN_SURFAREA"] = "0";
            model.Options["NORMAL_FLOW_LIMITED"] = "BOTH";
            model.Options["FORCE_MAIN_EQUATION"] = "H-W";
            model.Options["MIN_SLOPE"] = "0";
            model.Options["SKIP_STEADY_STATE"] = "NO";
            model.Options["IGNORE_ROUTING"] = "NO";
            model.Options["IGNORE_QUALITY"] = "NO";
            model.Options["IGNORE_SNOWMELT"] = "NO";
            model.Options["IGNORE_GROUNDWATER"] = "NO";
            model.Options["MAX_TRIALS"] = "8";
            model.Options["HEAD_TOLERANCE"] = "0.005";
            model.Options["SYS_FLOW_TOL"] = "5";
            model.Options["LAT_FLOW_TOL"] = "5";
            model.Options["MINIMUM_STEP"] = "0.5";
            model.Options["THREADS"] = "4";
            model.Options["SURCHARGE_METHOD"] = "EXTRAN";
            model.Options["RULE_STEP"] = "00:00:00";

            foreach (var s in readResult.Structures)
            {
                double elev = LengthInFeet(s.InvertElevation, s.InvertElevationUnit, s.Label + " invert");
                double rim = LengthInFeet(s.GroundElevation ?? s.RimElevation, s.GroundElevation.HasValue ? s.GroundElevationUnit : s.RimElevationUnit, s.Label + " rim");
                double maxDepth = rim - elev;
                if (maxDepth <= 0) throw new InvalidOperationException("LANDXML_INVALID_NODE_DEPTH: " + s.Label);
                if (hydraulics.OutfallTypes.TryGetValue(s.Label, out string boundary))
                {
                    if (boundary != "FREE" && boundary != "FIXED") throw new InvalidOperationException("LANDXML_UNSUPPORTED_BOUNDARY: " + s.Label);
                    if (boundary == "FIXED" && !hydraulics.FixedStagesFeet.ContainsKey(s.Label)) throw new InvalidOperationException("LANDXML_FIXED_STAGE_REQUIRED");
                    model.Outfalls.Add(s.Label, new SwmmOutfallDto { Name = s.Label, Elevation = elev, OutfallType = boundary,
                        FixedStage = boundary == "FIXED" ? hydraulics.FixedStagesFeet[s.Label] : 0.0 });
                }
                else
                {
                    model.Junctions.Add(s.Label, new SwmmJunctionDto { Name = s.Label, Elevation = elev, MaxDepth = maxDepth });
                }
            }

            foreach (var c in readResult.Conduits)
            {
                string fromNode = readResult.Structures.Single(s => s.Id == c.StartNodeId).Label;
                string toNode = readResult.Structures.Single(s => s.Id == c.StopNodeId).Label;
                double len = LengthInFeet(c.Length, c.LengthUnit, c.Label + " length");
                double roughness = c.ManningsN ?? double.NaN;
                double diamFt = LengthInFeet(c.Diameter, c.DiameterUnit, c.Label + " diameter");
                if (len <= 0 || diamFt <= 0 || !Finite(roughness) || roughness <= 0 || c.Shape != "CircularPipe")
                    throw new InvalidOperationException("LANDXML_GEOMETRY_OR_ROUGHNESS_REQUIRED: " + c.Label);
                double fromElev = model.Junctions.ContainsKey(fromNode) ? model.Junctions[fromNode].Elevation : model.Outfalls[fromNode].Elevation;
                double toElev = model.Junctions.ContainsKey(toNode) ? model.Junctions[toNode].Elevation : model.Outfalls[toNode].Elevation;
                double startInvert = LengthInFeet(c.StartInvert, c.StartInvertUnit, c.Label + " start invert");
                double stopInvert = LengthInFeet(c.StopInvert, c.StopInvertUnit, c.Label + " stop invert");

                model.Conduits[c.Label] = new SwmmConduitDto
                {
                    Name = c.Label,
                    FromNode = fromNode,
                    ToNode = toNode,
                    Length = len,
                    Roughness = roughness,
                    InOffset = startInvert - fromElev,
                    OutOffset = stopInvert - toElev,
                    Geom1 = diamFt,
                    Shape = "CIRCULAR",
                    Barrels = 1
                };
            }
            foreach (var inflow in hydraulics.InflowsCfs)
            {
                if (!model.Junctions.ContainsKey(inflow.Key) || !Finite(inflow.Value) || inflow.Value < 0)
                    throw new InvalidOperationException("LANDXML_INVALID_INFLOW: " + inflow.Key);
                model.Inflows.Add(inflow.Key, inflow.Value);
            }
            if (hydraulics.OutfallTypes.Keys.Any(k => !model.Outfalls.ContainsKey(k))) throw new InvalidOperationException("LANDXML_UNKNOWN_OUTFALL");
            SwmmInpNetworkAdapter.ValidateModelTopology(model);
            return model;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static double MeasureImportedTopologyDrift(string sourcePath, BentleyLiveExecutionResult bentley)
        {
            var original = SwmmInpNetworkAdapter.ReadInpFile(sourcePath);
            var imported = SwmmInpNetworkAdapter.ReadInpFile(bentley.EngineInpPath);
            var labels = SwmmCrossSolverValidationEngine.ReadElementLabelMapFromSqlite(bentley.StswPath + ".sqlite");
            Func<string, string> label = id => labels.TryGetValue(id, out string value) ? value : id;
            var originalNodes = new HashSet<string>(original.Junctions.Keys.Concat(original.Outfalls.Keys), StringComparer.OrdinalIgnoreCase);
            var importedNodes = new HashSet<string>(imported.Junctions.Keys.Concat(imported.Outfalls.Keys).Select(label), StringComparer.OrdinalIgnoreCase);
            var originalLinks = new HashSet<string>(original.Conduits.Keys, StringComparer.OrdinalIgnoreCase);
            var importedLinks = new HashSet<string>(imported.Conduits.Keys.Select(label), StringComparer.OrdinalIgnoreCase);
            int difference = originalNodes.Except(importedNodes).Count() + importedNodes.Except(originalNodes).Count()
                + originalLinks.Except(importedLinks).Count() + importedLinks.Except(originalLinks).Count();
            foreach (var link in imported.Conduits.Values)
            {
                if (!original.Conduits.TryGetValue(label(link.Name), out var expected)) continue;
                if (!string.Equals(expected.FromNode, label(link.FromNode), StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(expected.ToNode, label(link.ToNode), StringComparison.OrdinalIgnoreCase)) difference++;
            }
            return difference;
        }

        private static double LengthInFeet(double? value, SourceUnitIdentity unit, string field)
        {
            if (!value.HasValue || !Finite(value.Value) || unit == null) throw new InvalidOperationException("LANDXML_QUANTITY_REQUIRED: " + field);
            if (EffectiveSectionHeightResolver.IsFeetUnit(unit)) return value.Value;
            if (unit.Symbol == "m") return value.Value / 0.3048;
            if (unit.Symbol == "in") return value.Value / 12.0;
            if (unit.Symbol == "mm") return value.Value / 304.8;
            throw new InvalidOperationException("LANDXML_UNSUPPORTED_UNIT: " + field);
        }

        public static double MeasureConversionDrift(SwmmModel source, SwmmModel converted)
        {
            if (!source.Junctions.Keys.OrderBy(k => k).SequenceEqual(converted.Junctions.Keys.OrderBy(k => k)) ||
                !source.Outfalls.Keys.OrderBy(k => k).SequenceEqual(converted.Outfalls.Keys.OrderBy(k => k)) ||
                !source.Conduits.Keys.OrderBy(k => k).SequenceEqual(converted.Conduits.Keys.OrderBy(k => k)) ||
                !source.Pumps.Keys.OrderBy(k => k).SequenceEqual(converted.Pumps.Keys.OrderBy(k => k)) ||
                !source.Orifices.Keys.OrderBy(k => k).SequenceEqual(converted.Orifices.Keys.OrderBy(k => k)) ||
                !source.Weirs.Keys.OrderBy(k => k).SequenceEqual(converted.Weirs.Keys.OrderBy(k => k))) return double.PositiveInfinity;
            double drift = 0;
            foreach (var pair in source.Conduits)
            {
                var b = converted.Conduits[pair.Key]; var a = pair.Value;
                if (a.FromNode != b.FromNode || a.ToNode != b.ToNode) return double.PositiveInfinity;
                foreach (double delta in new[] { a.Length-b.Length, a.Geom1-b.Geom1, a.Roughness-b.Roughness, a.InOffset-b.InOffset, a.OutOffset-b.OutOffset }) drift = Math.Max(drift, Math.Abs(delta));
            }
            foreach (var pair in source.Pumps)
            {
                var b = converted.Pumps[pair.Key]; var a = pair.Value;
                if (a.FromNode != b.FromNode || a.ToNode != b.ToNode) return double.PositiveInfinity;
                foreach (double delta in new[] { a.StartupDepth - b.StartupDepth, a.ShutoffDepth - b.ShutoffDepth }) drift = Math.Max(drift, Math.Abs(delta));
            }
            foreach (var pair in source.Orifices)
            {
                var b = converted.Orifices[pair.Key]; var a = pair.Value;
                if (a.FromNode != b.FromNode || a.ToNode != b.ToNode) return double.PositiveInfinity;
                foreach (double delta in new[] { a.Offset - b.Offset, a.DischargeCoeff - b.DischargeCoeff, a.Geom1 - b.Geom1, a.Geom2 - b.Geom2 }) drift = Math.Max(drift, Math.Abs(delta));
            }
            foreach (var pair in source.Weirs)
            {
                var b = converted.Weirs[pair.Key]; var a = pair.Value;
                if (a.FromNode != b.FromNode || a.ToNode != b.ToNode) return double.PositiveInfinity;
                foreach (double delta in new[] { a.CrestHeight - b.CrestHeight, a.DischargeCoeff - b.DischargeCoeff, a.Geom1 - b.Geom1, a.Geom2 - b.Geom2 }) drift = Math.Max(drift, Math.Abs(delta));
            }
            foreach (var pair in source.Junctions) { var b = converted.Junctions[pair.Key]; drift = Math.Max(drift, Math.Abs(pair.Value.Elevation-b.Elevation)); drift = Math.Max(drift, Math.Abs(pair.Value.MaxDepth-b.MaxDepth)); }
            foreach (var pair in source.Outfalls) { var b = converted.Outfalls[pair.Key]; if (pair.Value.OutfallType != b.OutfallType) return double.PositiveInfinity; drift = Math.Max(drift, Math.Abs(pair.Value.Elevation-b.Elevation)); drift = Math.Max(drift, Math.Abs(pair.Value.FixedStage-b.FixedStage)); }
            if (source.Inflows.Count != converted.Inflows.Count) return double.PositiveInfinity;
            foreach (var pair in source.Inflows) { if (!converted.Inflows.ContainsKey(pair.Key)) return double.PositiveInfinity; drift = Math.Max(drift, Math.Abs(pair.Value-converted.Inflows[pair.Key])); }
            return drift;
        }

        private static void MaterializeArtifactsAndHashes(string workDir, string caseName, ProductionPipelineResult result)
        {
            string mdPath = Path.Combine(workDir, $"{caseName}_pipeline_report.md");
            string jsonPath = Path.Combine(workDir, $"{caseName}_parity_matrix.json");

            var sb = new StringBuilder();
            sb.AppendLine($"# SewerPilot Pipeline Execution Report: {caseName}");
            sb.AppendLine();
            sb.AppendLine($"- **Status:** **{result.PipelineStatus}**");
            sb.AppendLine($"- **Ingestion Type:** {result.IngestionType}");
            sb.AppendLine($"- **Structures Ingested:** {result.IngestedStructureCount}");
            sb.AppendLine($"- **Conduits Ingested:** {result.IngestedConduitCount}");
            sb.AppendLine($"- **Bentley Topological Drift:** {(result.BentleyTopologyMeasured ? result.TopologicalDrift.ToString("F3", CultureInfo.InvariantCulture) : "NOT_MEASURED")}");
            sb.AppendLine($"- **EPA-SWMM Engine:** {result.SwmmVersion} (Continuity: {result.SwmmContinuityErrorPercent:F3}%)");
            sb.AppendLine($"- **Bentley Engine:** {result.BentleyExecutionStatus} (Continuity: {result.BentleyContinuityErrorPercent:F3}%)");
            sb.AppendLine($"- **Telemetry Advisory:** {result.TelemetryAdvisory}");
            sb.AppendLine($"- **Metrics Passed:** {result.PassedMetricsCount} / {result.TotalMetricsCount} (Max Delta: {result.MaxRelativeDeltaPercent:F3}%)");
            sb.AppendLine($"- **Memory Delta:** {result.MemoryDeltaBytes / 1024} KB");
            sb.AppendLine();
            sb.AppendLine("## Parity Metrics Matrix");
            sb.AppendLine("| Metric | Element | Bentley Ref | SWMM Calc | Unit | Abs Delta | Rel Delta (%) | Status |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var m in result.ParityMetrics)
            {
                sb.AppendLine($"| {m.MetricName} | {m.ElementId} | {m.BentleyReferenceValue:F3} | {m.SwmmCalculatedValue:F3} | {m.Unit} | {m.AbsoluteDelta:F4} | {m.RelativePercentDelta:F3}% | **{m.Status}** |");
            }

            result.MarkdownReportPath = mdPath;
            result.ParityMatrixJsonPath = jsonPath;
            result.ArtifactSha256Hashes.Clear();

            using (var sha = SHA256.Create())
            {
                foreach (string f in Directory.GetFiles(workDir, caseName + "*"))
                {
                    if (Path.GetExtension(f).Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(f).Equals(".md", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var stm = File.OpenRead(f))
                    {
                        byte[] hash = sha.ComputeHash(stm);
                        result.ArtifactSha256Hashes[Path.GetFileName(f)] = BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
                    }
                }
            }
            File.WriteAllText(mdPath, sb.ToString(), Encoding.UTF8);
            File.WriteAllText(jsonPath, new JavaScriptSerializer().Serialize(result), Encoding.UTF8);
        }

        private static string GenerateSuiteMarkdownReport(ExtendedBenchmarkSuiteResult suite)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Milestone 5: Extended Benchmark Regression Report (Dual-Solver Parity)");
            sb.AppendLine();
            sb.AppendLine($"**Execution Timestamp (UTC):** {suite.ExecutionTimestampUtc:O}  ");
            sb.AppendLine($"**Overall Disposition:** **{suite.OverallStatus}**  ");
            sb.AppendLine($"**Total Benchmark Scenarios:** {suite.TotalCases} ({suite.PassedCases} Passed)  ");
            sb.AppendLine($"**Total Hydraulic Parity Metrics:** {suite.TotalMetricsCount} ({suite.PassedMetricsCount} Passed)  ");
            sb.AppendLine($"**Maximum Relative Delta:** `{suite.MaxRelativeDeltaPercent:F4}%` (Gating Threshold: $\\le 1.0\\%$)  ");
            sb.AppendLine($"**Mean Relative Delta:** `{suite.MeanRelativeDeltaPercent:F4}%`  ");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 1. Extended Benchmark Scenarios Summary");
            sb.AppendLine();
            sb.AppendLine("| Case ID | Description | Structures | Conduits | SWMM Continuity | Bentley Continuity | Max Delta | Verdict |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var c in suite.CaseResults)
            {
                sb.AppendLine($"| `{Path.GetFileNameWithoutExtension(c.MarkdownReportPath).Replace("_pipeline_report", "")}` | {c.IngestionType} | {c.IngestedStructureCount} | {c.IngestedConduitCount} | `{c.SwmmContinuityErrorPercent:F3}%` | `{c.BentleyContinuityErrorPercent:F3}%` | `{c.MaxRelativeDeltaPercent:F3}%` | **{c.PipelineStatus}** |");
            }
            sb.AppendLine();
            sb.AppendLine("## 2. Comprehensive Metric Details");
            sb.AppendLine();
            sb.AppendLine("| Scenario | Parameter | Element | Bentley SewerGEMS | EPA-SWMM 5.2 | Delta (Abs) | Delta (%) | Tolerance | Verdict |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var c in suite.CaseResults)
            {
                string caseId = Path.GetFileNameWithoutExtension(c.MarkdownReportPath).Replace("_pipeline_report", "");
                foreach (var m in c.ParityMetrics)
                {
                    sb.AppendLine($"| `{caseId}` | {m.MetricName} | `{m.ElementId}` | `{m.BentleyReferenceValue:F3} {m.Unit}` | `{m.SwmmCalculatedValue:F3} {m.Unit}` | `{m.AbsoluteDelta:F4}` | `{m.RelativePercentDelta:F3}%` | `1.0%` | **{m.Status}** |");
                }
            }
            sb.AppendLine();
            sb.AppendLine("## 3. Cryptographic Artifact Hash Catalog");
            sb.AppendLine();
            sb.AppendLine("| Artifact File | SHA-256 Digest |");
            sb.AppendLine("|---|---|");
            foreach (var kvp in suite.ArtifactSha256Hashes.OrderBy(k => k.Key))
            {
                sb.AppendLine($"| `{kvp.Key}` | `{kvp.Value}` |");
            }

            return sb.ToString();
        }

        private static void ComputeSuiteFileHashes(string workDir, ExtendedBenchmarkSuiteResult suite)
        {
            suite.ArtifactSha256Hashes.Clear();
            if (!Directory.Exists(workDir)) return;

            using (var sha = SHA256.Create())
            {
                foreach (string f in Directory.GetFiles(workDir))
                {
                    string name = Path.GetFileName(f);
                    // A file cannot embed its own final digest. Consolidated outputs are sealed externally.
                    if (name.StartsWith("extended-benchmark-", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var stm = File.OpenRead(f))
                    {
                        byte[] hash = sha.ComputeHash(stm);
                        suite.ArtifactSha256Hashes[name] = BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
                    }
                }
            }
        }

        public static string ResolveDefaultBenchmarkEvidenceDirectory()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SewerPilot.sln")))
            {
                dir = dir.Parent;
            }
            string root = dir != null ? dir.FullName : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
            return Path.Combine(root, "docs", "evidence", "swmm", "benchmarks");
        }

        private static string GetScenarioInpContent(string scenarioName)
        {
            string dir = ResolveDefaultBenchmarkEvidenceDirectory();
            string path = Path.Combine(dir, scenarioName + ".inp");
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    "Failed to materialize SWMM INP payload for model '" + scenarioName + "': source deck not found at '" + path +
                    "'. Writing empty simulation decks is prohibited.");
            }
            string content = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException(
                    "Failed to materialize SWMM INP payload for model '" + scenarioName + "': source deck at '" + path +
                    "' is empty. Writing empty simulation decks is prohibited.");
            }
            return content;
        }
    }
}
