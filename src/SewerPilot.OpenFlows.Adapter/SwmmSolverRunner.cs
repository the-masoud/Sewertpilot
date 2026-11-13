using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class SwmmNodeResult
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public double AverageDepthFeet { get; set; }
        public double MaximumDepthFeet { get; set; } = double.NaN;
        public double MaximumHglFeet { get; set; } = double.NaN;
        public double MaximumTotalInflowCfs { get; set; }
    }

    public sealed class SwmmLinkResult
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public double MaximumFlowCfs { get; set; }
        public double MaximumVelocityFps { get; set; } = double.NaN;
        public double MaxOverFullFlowRatio { get; set; }
        public double MaxOverFullDepthRatio { get; set; }
    }

    public sealed class SwmmExecutionResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public int VersionCode { get; set; }
        public string VersionString { get; set; }
        public double ContinuityErrorPercent { get; set; } = double.NaN;
        public string DllPath { get; set; }
        public string InpPath { get; set; }
        public string RptPath { get; set; }
        public string OutPath { get; set; }
        public string ReportText { get; set; }
        public Dictionary<string, SwmmNodeResult> Nodes { get; } = new Dictionary<string, SwmmNodeResult>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SwmmLinkResult> Links { get; } = new Dictionary<string, SwmmLinkResult>(StringComparer.OrdinalIgnoreCase);
        public List<string> WarningsAndErrors { get; } = new List<string>();
    }

    public static class SwmmSolverRunner
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SwmmRunDelegate([MarshalAs(UnmanagedType.LPStr)] string f1, [MarshalAs(UnmanagedType.LPStr)] string f2, [MarshalAs(UnmanagedType.LPStr)] string f3);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SwmmGetVersionDelegate();

        private static readonly object _syncLock = new object();
        private static IntPtr _dllHandle = IntPtr.Zero;
        private static SwmmRunDelegate _runDelegate;
        private static SwmmGetVersionDelegate _getVersionDelegate;
        private static string _resolvedDllPath;

        public static string ResolveSwmmDllPath()
        {
            if (!string.IsNullOrEmpty(_resolvedDllPath) && File.Exists(_resolvedDllPath))
                return _resolvedDllPath;

            string envPath = Environment.GetEnvironmentVariable("SWMM5_DLL_PATH");
            if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
                return _resolvedDllPath = Path.GetFullPath(envPath);

            var candidates = new[]
            {
                @"C:\Program Files\Bentley\OpenFlows Sewer\swmm5.dll",
                @"C:\Program Files\EPA SWMM 5.2\swmm5.dll",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "swmm5.dll"),
                @"C:\Program Files (x86)\EPA SWMM 5.2\swmm5.dll"
            };

            foreach (var c in candidates)
            {
                if (File.Exists(c))
                    return _resolvedDllPath = c;
            }

            return null;
        }

        private static void EnsureInitialized()
        {
            lock (_syncLock)
            {
                if (_runDelegate != null) return;

                string dllPath = ResolveSwmmDllPath();
                if (string.IsNullOrEmpty(dllPath))
                    throw new FileNotFoundException("EPA-SWMM 5.2 native swmm5.dll could not be located in standard program directories or SWMM5_DLL_PATH.");

                _dllHandle = LoadLibrary(dllPath);
                if (_dllHandle == IntPtr.Zero)
                {
                    int lastErr = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"Failed to load native library '{dllPath}'. Win32 Error: {lastErr}");
                }

                IntPtr pRun = GetProcAddress(_dllHandle, "swmm_run");
                if (pRun == IntPtr.Zero)
                    throw new EntryPointNotFoundException($"Entry point 'swmm_run' not found in '{dllPath}'.");
                _runDelegate = (SwmmRunDelegate)Marshal.GetDelegateForFunctionPointer(pRun, typeof(SwmmRunDelegate));

                IntPtr pVer = GetProcAddress(_dllHandle, "swmm_getVersion");
                if (pVer != IntPtr.Zero)
                    _getVersionDelegate = (SwmmGetVersionDelegate)Marshal.GetDelegateForFunctionPointer(pVer, typeof(SwmmGetVersionDelegate));
            }
        }

        public static int GetSwmmVersion()
        {
            EnsureInitialized();
            return _getVersionDelegate != null ? _getVersionDelegate() : 52000;
        }

        public static SwmmExecutionResult Execute(string inpFilePath, string outputDir = null)
        {
            if (string.IsNullOrWhiteSpace(inpFilePath))
                throw new ArgumentNullException(nameof(inpFilePath));
            if (!File.Exists(inpFilePath))
                throw new FileNotFoundException("SWMM input file does not exist: " + inpFilePath, inpFilePath);

            EnsureInitialized();

            string baseDir = !string.IsNullOrWhiteSpace(outputDir)
                ? outputDir
                : Path.GetDirectoryName(Path.GetFullPath(inpFilePath));
            if (!Directory.Exists(baseDir))
                Directory.CreateDirectory(baseDir);

            string filePrefix = Path.GetFileNameWithoutExtension(inpFilePath);
            string rptPath = Path.Combine(baseDir, filePrefix + ".rpt");
            string outPath = Path.Combine(baseDir, filePrefix + ".out");

            if (File.Exists(rptPath)) File.Delete(rptPath);
            if (File.Exists(outPath)) File.Delete(outPath);

            int exitCode = _runDelegate(inpFilePath, rptPath, outPath);

            var result = new SwmmExecutionResult
            {
                ExitCode = exitCode,
                Success = (exitCode == 0),
                DllPath = _resolvedDllPath,
                InpPath = Path.GetFullPath(inpFilePath),
                RptPath = Path.GetFullPath(rptPath),
                OutPath = Path.GetFullPath(outPath),
                VersionCode = _getVersionDelegate != null ? _getVersionDelegate() : 0
            };

            if (File.Exists(rptPath))
            {
                string rptText = File.ReadAllText(rptPath, Encoding.UTF8);
                result.ReportText = rptText;
                ParseReportText(rptText, result);
            }
            else
            {
                result.WarningsAndErrors.Add("Report file was not generated by swmm_run.");
            }

            return result;
        }

        public static void ParseReportText(string reportText, SwmmExecutionResult result)
        {
            if (string.IsNullOrEmpty(reportText)) return;

            string[] lines = reportText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                // Version header
                if (line.StartsWith("EPA STORM WATER MANAGEMENT MODEL", StringComparison.OrdinalIgnoreCase))
                {
                    result.VersionString = line;
                }

                // Continuity error
                if (line.StartsWith("Continuity Error (%)", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(line, @"Continuity Error \(%\)\s*\.{3,}\s*([-\d\.]+)");
                    if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double err))
                    {
                        result.ContinuityErrorPercent = err;
                    }
                }

                // Error / warning lines
                if (line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) || line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
                {
                    result.WarningsAndErrors.Add(line);
                }

                // Node Depth Summary
                if (line.Equals("Node Depth Summary", StringComparison.OrdinalIgnoreCase) || line.Equals("******************", StringComparison.OrdinalIgnoreCase) && i + 1 < lines.Length && lines[i + 1].Trim().Equals("Node Depth Summary", StringComparison.OrdinalIgnoreCase))
                {
                    i = ParseNodeDepthSection(lines, i, result);
                }

                // Node Inflow Summary
                if (line.Equals("Node Inflow Summary", StringComparison.OrdinalIgnoreCase) || line.Equals("*******************", StringComparison.OrdinalIgnoreCase) && i + 1 < lines.Length && lines[i + 1].Trim().Equals("Node Inflow Summary", StringComparison.OrdinalIgnoreCase))
                {
                    i = ParseNodeInflowSection(lines, i, result);
                }

                // Link Flow Summary
                if (line.Equals("Link Flow Summary", StringComparison.OrdinalIgnoreCase) || line.Equals("*****************", StringComparison.OrdinalIgnoreCase) && i + 1 < lines.Length && lines[i + 1].Trim().Equals("Link Flow Summary", StringComparison.OrdinalIgnoreCase))
                {
                    i = ParseLinkFlowSection(lines, i, result);
                }
            }
        }

        private static readonly HashSet<string> ReservedHeaderTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Node", "Name", "Type", "Link", "Average", "Depth", "Maximum", "Time", "Occurrence", "Inflow", "Lateral", "Total", "Volume", "Days", "Hours", "Min", "Flow", "Velocity", "Full"
        };

        private static int ParseNodeDepthSection(string[] lines, int startIndex, SwmmExecutionResult result)
        {
            int idx = startIndex;
            // Advance until table header divider (e.g. "------")
            while (idx < lines.Length && !lines[idx].Contains("------"))
                idx++;
            idx++; // Move past divider line

            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("***") || line.StartsWith("Analysis begun"))
                    break;

                string[] tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 5)
                {
                    string nodeName = tokens[0];
                    if (ReservedHeaderTokens.Contains(nodeName)) { idx++; continue; }
                    string type = tokens[1];
                    if (!new[] { "JUNCTION", "OUTFALL", "STORAGE", "DIVIDER" }.Contains(type.ToUpperInvariant())) { idx++; continue; }
                    double avgDepth = ParseDouble(tokens[2]);
                    double maxDepth = ParseDouble(tokens[3]);
                    double maxHgl = ParseDouble(tokens[4]);

                    if (!result.Nodes.TryGetValue(nodeName, out var nodeRes))
                    {
                        nodeRes = new SwmmNodeResult { Name = nodeName, Type = type };
                        result.Nodes[nodeName] = nodeRes;
                    }

                    nodeRes.AverageDepthFeet = avgDepth;
                    nodeRes.MaximumDepthFeet = maxDepth;
                    nodeRes.MaximumHglFeet = maxHgl;
                }
                idx++;
            }
            return idx;
        }

        private static int ParseNodeInflowSection(string[] lines, int startIndex, SwmmExecutionResult result)
        {
            int idx = startIndex;
            while (idx < lines.Length && !lines[idx].Contains("------"))
                idx++;
            idx++;

            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("***") || line.StartsWith("Analysis begun"))
                    break;

                string[] tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 4)
                {
                    string nodeName = tokens[0];
                    if (ReservedHeaderTokens.Contains(nodeName)) { idx++; continue; }
                    string type = tokens[1];
                    if (!new[] { "JUNCTION", "OUTFALL", "STORAGE", "DIVIDER" }.Contains(type.ToUpperInvariant())) { idx++; continue; }
                    double totalInflow = ParseDouble(tokens[3]);

                    if (!result.Nodes.TryGetValue(nodeName, out var nodeRes))
                    {
                        nodeRes = new SwmmNodeResult { Name = nodeName, Type = type };
                        result.Nodes[nodeName] = nodeRes;
                    }
                    nodeRes.MaximumTotalInflowCfs = totalInflow;
                }
                idx++;
            }
            return idx;
        }

        private static int ParseLinkFlowSection(string[] lines, int startIndex, SwmmExecutionResult result)
        {
            int idx = startIndex;
            while (idx < lines.Length && !lines[idx].Contains("------"))
                idx++;
            idx++;

            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("***") || line.StartsWith("Analysis begun"))
                    break;

                string[] tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                // Expected format: Link Type MaxFlow OccurrenceDay OccurrenceTime MaxVelocity Max/FullFlow Max/FullDepth
                if (tokens.Length >= 5)
                {
                    string linkName = tokens[0];
                    if (ReservedHeaderTokens.Contains(linkName)) { idx++; continue; }
                    string type = tokens[1];
                    if (!new[] { "CONDUIT", "PUMP", "WEIR", "ORIFICE", "OUTLET" }.Contains(type.ToUpperInvariant())) { idx++; continue; }
                    double maxFlow = ParseDouble(tokens[2]);
                    // If occurrence is split (tokens[3] = day, tokens[4] = time), max velocity is tokens[5]
                    double maxVel = double.NaN;
                    double maxOverFullFlow = double.NaN;
                    double maxOverFullDepth = double.NaN;

                    if (tokens.Length >= 8)
                    {
                        maxVel = ParseDouble(tokens[5]);
                        maxOverFullFlow = ParseDouble(tokens[6]);
                        maxOverFullDepth = ParseDouble(tokens[7]);
                    }

                    var linkRes = new SwmmLinkResult
                    {
                        Name = linkName,
                        Type = type,
                        MaximumFlowCfs = maxFlow,
                        MaximumVelocityFps = maxVel,
                        MaxOverFullFlowRatio = maxOverFullFlow,
                        MaxOverFullDepthRatio = maxOverFullDepth
                    };
                    result.Links[linkName] = linkRes;
                }
                idx++;
            }
            return idx;
        }

        private static double ParseDouble(string str)
        {
            if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                return val;
            throw new InvalidDataException("SWMM_REPORT_INVALID_NUMBER: " + str);
        }
    }
}
