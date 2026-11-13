using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SewerPilot.OpenFlows.Adapter;
using SewerPilot.OpenFlows.Compatibility;
using SewerPilot.OpenFlows.Contracts;

namespace SewerPilot.Desktop
{
    public static class Program
    {
        public const string ProductVersion = "1.1.0-BETA";
        public const string ProductName = "SewerPilot Engineering Beta";

        [STAThread]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ApplicationDiagnostics.Attach();

            if (args != null && args.Length > 0)
            {
                string first = args[0].ToLowerInvariant();
                if (first == "--help" || first == "-h" || first == "/?")
                {
                    ShowHelp();
                    return 0;
                }

                if (first == "--version" || first == "-v")
                {
                    ShowVersion();
                    return 0;
                }

                if (first == "--diagnose")
                {
                    string diag = RunPreflightDiagnostic();
                    Console.WriteLine(diag);
                    MessageBox.Show(diag, "SewerPilot — Runtime Diagnostic", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                if (first == "--export-diagnostics")
                {
                    return RunDiagnosticsExport(args.Length >= 2 ? args[1] : null);
                }

                if (first == "--profile")
                {
                    return RunStandaloneProfileViewer();
                }

                if (first == "--plan" || first == "--canvas")
                {
                    return RunStandalonePlanCanvasViewer();
                }

                if (first == "--open-project" && args.Length >= 2)
                {
                    string projectPath = args[1];
                    return RunWorkspaceWithProject(projectPath);
                }
            }

            return RunDefaultWorkspace();
        }

        private static int RunDefaultWorkspace()
        {
            try
            {
                return SingleOperatorDesktopWorkflow.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fatal error initializing SewerPilot Workspace:\n\n" + ex.Message,
                    "SewerPilot Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int RunWorkspaceWithProject(string projectPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectPath))
                    return SingleOperatorDesktopWorkflow.Run();

                string file = Directory.Exists(projectPath)
                    ? Path.Combine(projectPath, "project.json")
                    : projectPath;
                var options = SingleOperatorStartupOptions.ForProjectFile(file, false);

                return SingleOperatorDesktopWorkflow.Run(options);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error opening project:\n\n" + ex.Message,
                    "SewerPilot Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        // Command-line equivalent of the in-application Export Diagnostics action, used for
        // support when the workspace cannot be opened at all.
        private static int RunDiagnosticsExport(string destination)
        {
            try
            {
                string target = destination;
                if (string.IsNullOrWhiteSpace(target))
                {
                    string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (string.IsNullOrWhiteSpace(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    target = Path.Combine(folder, "SewerPilot-Diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
                }
                var result = DiagnosticsExportService.Export(target);
                string message = "Diagnostics exported to:\r\n" + result.ArchivePath + "\r\n\r\n" +
                                 "Entries: " + string.Join(", ", result.Entries) + "\r\n\r\n" +
                                 "Nothing was uploaded. Review the archive before sharing it.";
                Console.WriteLine(message);
                MessageBox.Show(message, "SewerPilot — diagnostics exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                ApplicationDiagnostics.RecordFailure("Diagnostics export", ex);
                Console.Error.WriteLine("Diagnostics export failed: " + ex.Message);
                MessageBox.Show("The diagnostics archive could not be created:\r\n\r\n" + ex.Message,
                    "SewerPilot — diagnostics export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int RunStandalonePlanCanvasViewer()
        {
            try
            {
                var form = new Form
                {
                    Text = "SewerPilot — Interactive 2D Plan Canvas & Inspector",
                    Width = 1200,
                    Height = 800,
                    StartPosition = FormStartPosition.CenterScreen
                };

                var split = new SplitContainer
                {
                    Dock = DockStyle.Fill,
                    Orientation = Orientation.Vertical,
                    SplitterDistance = 780,
                    BackColor = Color.FromArgb(35, 42, 56)
                };

                var canvas = new NetworkPlanCanvasControl { Dock = DockStyle.Fill };
                var inspector = new NetworkElementInspectorControl { Dock = DockStyle.Fill };

                var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, BackColor = Color.FromArgb(32, 36, 44) };
                var btnFit = new Button { Text = "Fit Network", Width = 90, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(48, 54, 66) };
                var btnZoomIn = new Button { Text = "Zoom +", Width = 65, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(48, 54, 66) };
                var btnZoomOut = new Button { Text = "Zoom -", Width = 65, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(48, 54, 66) };
                var chkViolations = new CheckBox { Text = "Show Violations", Checked = true, AutoSize = true, ForeColor = Color.Tomato, Padding = new Padding(4, 3, 0, 0) };
                var chkLabels = new CheckBox { Text = "Show Labels", Checked = true, AutoSize = true, ForeColor = Color.WhiteSmoke, Padding = new Padding(4, 3, 0, 0) };
                var chkArrows = new CheckBox { Text = "Show Flow Arrows", Checked = true, AutoSize = true, ForeColor = Color.LightCyan, Padding = new Padding(4, 3, 0, 0) };

                btnFit.Click += (s, e) => canvas.ZoomToFit();
                btnZoomIn.Click += (s, e) => canvas.Zoom(1.25f);
                btnZoomOut.Click += (s, e) => canvas.Zoom(0.80f);
                chkViolations.CheckedChanged += (s, e) => { canvas.ShowViolations = chkViolations.Checked; canvas.Invalidate(); };
                chkLabels.CheckedChanged += (s, e) => { canvas.ShowLabels = chkLabels.Checked; canvas.Invalidate(); };
                chkArrows.CheckedChanged += (s, e) => { canvas.ShowFlowArrows = chkArrows.Checked; canvas.Invalidate(); };

                toolbar.Controls.AddRange(new Control[] { btnFit, btnZoomIn, btnZoomOut, chkViolations, chkLabels, chkArrows });

                var canvasPanel = new Panel { Dock = DockStyle.Fill };
                canvasPanel.Controls.Add(canvas);
                canvasPanel.Controls.Add(toolbar);

                split.Panel1.Controls.Add(canvasPanel);
                split.Panel2.Controls.Add(inspector);

                canvas.ElementSelected += (s, e) =>
                {
                    if (e.ElementKind != PlanCanvasElementKind.None)
                        inspector.InspectElement(e.ElementKind, e.RawItem);
                    else
                        inspector.ClearInspection();
                };

                inspector.TraceUpstreamRequested += (s, id) => canvas.TraceUpstream(id, inspector.CurrentKind == PlanCanvasElementKind.Conduit);
                inspector.TraceDownstreamRequested += (s, id) => canvas.TraceDownstream(id, inspector.CurrentKind == PlanCanvasElementKind.Conduit);
                inspector.ClearTraceRequested += (s, e) => canvas.ClearTrace();

                form.Controls.Add(split);
                Application.Run(form);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error launching Plan Canvas Viewer:\n\n" + ex.Message,
                    "SewerPilot Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int RunStandaloneProfileViewer()
        {
            try
            {
                var form = new Form
                {
                    Text = "SewerPilot — Hydraulic Profile & Verification Viewer",
                    Width = 1100,
                    Height = 750,
                    StartPosition = FormStartPosition.CenterScreen
                };
                var viewer = new HydraulicProfileViewerControl { Dock = DockStyle.Fill };
                form.Controls.Add(viewer);
                Application.Run(form);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error launching Profile Viewer:\n\n" + ex.Message,
                    "SewerPilot Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        public static string RunPreflightDiagnostic()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=================================================");
            sb.AppendLine("  SewerPilot Workstation Preflight Diagnostic");
            sb.AppendLine("=================================================");
            sb.AppendLine("Product: " + ProductName + " v" + ProductVersion);
            sb.AppendLine("Operating System: " + Environment.OSVersion);
            sb.AppendLine("64-Bit OS: " + Environment.Is64BitOperatingSystem);
            sb.AppendLine("64-Bit Process: " + Environment.Is64BitProcess);
            sb.AppendLine("CLR Version: " + Environment.Version);
            sb.AppendLine();

            try
            {
                sb.AppendLine("[Bentley OpenFlows / SewerGEMS Detection]");
                var diag = new DiscoveryDiagnostics();
                var options = new OpenFlowsDiscoveryOptions();
                var locations = new WindowsRuntimeLocationSource().Read(options, diag);
                var inspector = new InstalledRuntimeInspector();
                var candidates = locations.SelectMany(loc => inspector.Inspect(loc, options, diag)).ToList();

                if (candidates != null && candidates.Count > 0)
                {
                    sb.AppendLine("Status: FOUND (" + candidates.Count + " candidate(s))");
                    foreach (var c in candidates)
                    {
                        sb.AppendLine("  - Install Root: " + c.InstallRoot);
                        sb.AppendLine("    Executable: " + c.Executable.Path + " (" + c.Executable.ProductVersion + ")");
                        sb.AppendLine("    Statically Usable: " + c.IsStaticallyUsable);
                    }
                }
                else
                {
                    sb.AppendLine("Status: NO BENTLEY OPENFLOWS INSTALLATION DETECTED");
                    sb.AppendLine("Note: SewerPilot StormFlow calculations, design workflows, and MCP server tools");
                    sb.AppendLine("      function completely in standalone mode without Bentley SewerGEMS.");
                    sb.AppendLine("      Bentley runtime is required only for direct WaterObjects solver executions.");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("Detection Error: " + ex.Message);
            }

            return sb.ToString();
        }

        private static void ShowHelp()
        {
            string help = ProductName + " v" + ProductVersion + "\n\n" +
                          "Usage: SewerPilot.exe [options]\n\n" +
                          "Options:\n" +
                          "  (no args)             Launch the SewerPilot Engineering Workspace\n" +
                          "  --open-project <path> Open workspace with specified project.json\n" +
                          "  --plan, --canvas      Launch standalone Interactive 2D Plan Canvas & Inspector\n" +
                          "  --profile             Launch standalone Hydraulic Profile Viewer\n" +
                          "  --diagnose            Run Bentley WaterObjects runtime preflight check\n" +
                          "  --export-diagnostics [path]  Save a redacted support archive (zip)\n" +
                          "  --version, -v         Display product version\n" +
                          "  --help, -h            Show this help dialog\n";
            MessageBox.Show(help, "SewerPilot Help", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void ShowVersion()
        {
            MessageBox.Show(ProductName + "\nVersion: " + ProductVersion + "\nBuild: " + BuildIdentity() +
                "\nTarget Framework: .NET Framework 4.8 (x64)\nChannel: Engineering Beta (limited functionality)",
                "About SewerPilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Full build identity including the embedded source revision, when present.
        private static string BuildIdentity()
        {
            try
            {
                var attribute = (System.Reflection.AssemblyInformationalVersionAttribute[])typeof(Program)
                    .Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
                return attribute != null && attribute.Length > 0 ? attribute[0].InformationalVersion : "unknown";
            }
            catch (Exception) { return "unknown"; }
        }
    }
}
