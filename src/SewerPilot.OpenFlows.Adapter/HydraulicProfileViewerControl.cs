using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    // Production UI control for multi-module hydraulic profile visualization and executive reporting.
    // Offline / headless testable: does not perform direct model writes or unverified solver calls.
    internal sealed class HydraulicProfileViewerControl : UserControl
    {
        internal readonly Button RunVerificationBtn = new Button { Text = "Run Hydraulic Verification", Name = "RunVerificationBtn", Width = 180, Height = 28 };
        internal readonly Button ExportReportBtn = new Button { Text = "Export Executive Report", Name = "ExportReportBtn", Width = 170, Height = 28 };
        internal readonly Button ExportCsvBtn = new Button { Text = "Export Profile CSV", Name = "ExportCsvBtn", Width = 140, Height = 28 };
        internal readonly Button ExportGraphicBtn = new Button { Text = "Export Profile PNG", Name = "ExportGraphicBtn", Width = 140, Height = 28 };

        internal readonly Label VerdictBanner = new Label
        {
            Name = "VerdictBanner",
            Text = "Status: Ready — No Verification Results Loaded",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft
        };

        internal readonly Label KpiHeadLoss = new Label { Name = "KpiHeadLoss", Text = "Head Loss: N/A", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        internal readonly Label KpiMaxHgl = new Label { Name = "KpiMaxHgl", Text = "Max HGL: N/A", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        internal readonly Label KpiFreeboard = new Label { Name = "KpiFreeboard", Text = "Freeboard: N/A", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        internal readonly Label KpiSurcharge = new Label { Name = "KpiSurcharge", Text = "Surcharge: N/A", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };

        internal readonly DataGridView ProfileGrid = new DataGridView
        {
            Name = "ProfileGrid",
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            AutoGenerateColumns = false
        };

        internal readonly ListBox RemediationList = new ListBox
        {
            Name = "RemediationList",
            Dock = DockStyle.Fill
        };

        internal readonly TextBox ReportPreviewBox = new TextBox
        {
            Name = "ReportPreviewBox",
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill
        };

        internal readonly HydraulicProfileCanvasControl ProfileCanvas = new HydraulicProfileCanvasControl
        {
            Dock = DockStyle.Fill,
            Name = "ProfileCanvas"
        };

        private StormExecutiveReportContext activeContext;

        internal int ProfileGridRowCount => ProfileGrid.Rows.Count;
        internal int RemediationItemCount => RemediationList.Items.Count;
        internal string VerdictText => VerdictBanner.Text;
        internal string ReportPreviewText => ReportPreviewBox.Text;

        internal bool HasGraphicalProfile => ProfileCanvas.HasProfileData;
        internal int ProfileSegmentCount => ProfileCanvas.Segments.Count;
        internal int ProfileNodeCount => ProfileCanvas.Nodes.Count;
        internal RectangleF ProfileWorldBounds => ProfileCanvas.WorldBounds;

        public event EventHandler<string> ConduitSelected;

        public void SelectConduit(string conduitId)
        {
            if (string.IsNullOrEmpty(conduitId)) return;
            ProfileCanvas.SelectedConduitId = conduitId;
            ProfileCanvas.Invalidate();

            foreach (DataGridViewRow row in ProfileGrid.Rows)
            {
                if (row.Cells["ConduitId"].Value != null &&
                    string.Equals(row.Cells["ConduitId"].Value.ToString(), conduitId, StringComparison.OrdinalIgnoreCase))
                {
                    row.Selected = true;
                    ProfileGrid.CurrentCell = row.Cells[0];
                    break;
                }
            }
        }

        internal Bitmap RenderOffscreen(int width, int height) => ProfileCanvas.RenderToBitmap(width, height);

        internal HydraulicProfileViewerControl()
        {
            Name = "HydraulicProfileViewer";
            AccessibleName = "Hydraulic Profile & Executive Reporting";
            Dock = DockStyle.Fill;

            // Initialize Grid Columns
            ProfileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ConduitId", HeaderText = "Conduit" });
            ProfileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StartNode", HeaderText = "Up Node" });
            ProfileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StopNode", HeaderText = "Down Node" });
            ProfileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Diameter", HeaderText = "Diam (in)" });
            ProfileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Flow", HeaderText = "Flow (cfs)" });
            ProfileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Velocity", HeaderText = "Vel (fps)" });
            ProfileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "HeadLoss", HeaderText = "Head Loss (ft)" });
            ProfileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UpHgl", HeaderText = "HGL Up (ft)" });
            ProfileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DownHgl", HeaderText = "HGL Down (ft)" });
            ProfileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UpCrown", HeaderText = "Crown Up (ft)" });
            ProfileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SurchargeState", HeaderText = "Surcharge State" });

            ProfileGrid.SelectionChanged += (s, e) =>
            {
                if (ProfileGrid.CurrentRow != null && ProfileGrid.CurrentRow.Cells["ConduitId"].Value != null)
                {
                    string cid = ProfileGrid.CurrentRow.Cells["ConduitId"].Value.ToString();
                    ProfileCanvas.SelectedConduitId = cid;
                    ProfileCanvas.Invalidate();
                    ConduitSelected?.Invoke(this, cid);
                }
            };

            // Actions bar
            var actionPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36 };
            actionPanel.Controls.AddRange(new Control[] { RunVerificationBtn, ExportReportBtn, ExportCsvBtn, ExportGraphicBtn });

            // KPI panel
            var kpiPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32 };
            kpiPanel.Controls.AddRange(new Control[] { KpiHeadLoss, new Label { Text = " | ", AutoSize = true },
                KpiMaxHgl, new Label { Text = " | ", AutoSize = true },
                KpiFreeboard, new Label { Text = " | ", AutoSize = true },
                KpiSurcharge });

            // Canvas Toolbar
            var canvasToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, BackColor = Color.FromArgb(32, 36, 44) };
            var resetBtn = new Button { Text = "Fit to Window", Width = 95, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(48, 54, 66) };
            var zoomInBtn = new Button { Text = "Zoom +", Width = 65, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(48, 54, 66) };
            var zoomOutBtn = new Button { Text = "Zoom -", Width = 65, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(48, 54, 66) };
            var chkEgl = new CheckBox { Text = "Show EGL", Checked = true, AutoSize = true, ForeColor = Color.LightCyan, Padding = new Padding(4, 3, 0, 0) };
            var chkGround = new CheckBox { Text = "Show Ground", Checked = true, AutoSize = true, ForeColor = Color.Wheat, Padding = new Padding(4, 3, 0, 0) };
            var chkFreeboard = new CheckBox { Text = "Show Freeboard Buffer", Checked = true, AutoSize = true, ForeColor = Color.Coral, Padding = new Padding(4, 3, 0, 0) };
            var chkLabels = new CheckBox { Text = "Show Annotations", Checked = true, AutoSize = true, ForeColor = Color.WhiteSmoke, Padding = new Padding(4, 3, 0, 0) };

            resetBtn.Click += (s, e) => ProfileCanvas.ResetView();
            zoomInBtn.Click += (s, e) => ProfileCanvas.Zoom(1.25f);
            zoomOutBtn.Click += (s, e) => ProfileCanvas.Zoom(0.80f);
            chkEgl.CheckedChanged += (s, e) => { ProfileCanvas.ShowEgl = chkEgl.Checked; ProfileCanvas.Invalidate(); };
            chkGround.CheckedChanged += (s, e) => { ProfileCanvas.ShowGround = chkGround.Checked; ProfileCanvas.Invalidate(); };
            chkFreeboard.CheckedChanged += (s, e) => { ProfileCanvas.ShowFreeboard = chkFreeboard.Checked; ProfileCanvas.Invalidate(); };
            chkLabels.CheckedChanged += (s, e) => { ProfileCanvas.ShowLabels = chkLabels.Checked; ProfileCanvas.Invalidate(); };

            canvasToolbar.Controls.AddRange(new Control[] { resetBtn, zoomInBtn, zoomOutBtn, chkEgl, chkGround, chkFreeboard, chkLabels });

            var canvasContainer = new Panel { Dock = DockStyle.Fill };
            canvasContainer.Controls.Add(ProfileCanvas);
            canvasContainer.Controls.Add(canvasToolbar);

            // Bottom tabs: Visual Profile Canvas, Table, Remediation, and Executive Report Preview
            var bottomTabs = new TabControl { Dock = DockStyle.Fill };
            var tabCanvas = new TabPage("Dynamic Hydraulic Profile (HGL/EGL)");
            tabCanvas.Controls.Add(canvasContainer);

            var tabGrid = new TabPage("Hydraulic Profile Table");
            tabGrid.Controls.Add(ProfileGrid);

            var tabRemediation = new TabPage("Bottlenecks & Remediation Proposals");
            tabRemediation.Controls.Add(RemediationList);

            var tabReport = new TabPage("Executive Audit Report Preview");
            tabReport.Controls.Add(ReportPreviewBox);

            bottomTabs.TabPages.AddRange(new[] { tabCanvas, tabGrid, tabRemediation, tabReport });

            Controls.Add(bottomTabs);
            Controls.Add(kpiPanel);
            Controls.Add(VerdictBanner);
            Controls.Add(actionPanel);

            ExportReportBtn.Click += (s, e) => ExportReport();
            ExportCsvBtn.Click += (s, e) => ExportCsv();
            ExportGraphicBtn.Click += (s, e) => ExportGraphic();
        }

        internal void LoadResults(StormExecutiveReportContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            context.Validate();
            activeContext = context;

            var ver = context.VerificationResult;

            // Verdict banner
            if (ver.HasFlooding)
            {
                VerdictBanner.Text = "VERDICT: FAILED — SURFACE FLOODING DETECTED";
                VerdictBanner.ForeColor = Color.Red;
            }
            else if (ver.HasFreeboardViolations)
            {
                VerdictBanner.Text = "VERDICT: CONDITIONAL — FREEBOARD BUFFER VIOLATION (< 1.0 ft)";
                VerdictBanner.ForeColor = Color.DarkOrange;
            }
            else if (ver.HasSurcharge)
            {
                VerdictBanner.Text = "VERDICT: PASS WITH SURCHARGE (PRESSURIZED PIPE FLOW)";
                VerdictBanner.ForeColor = Color.DarkGoldenrod;
            }
            else
            {
                VerdictBanner.Text = "VERDICT: ACCEPTED — SAFE OPEN-CHANNEL GRAVITY FLOW";
                VerdictBanner.ForeColor = Color.DarkGreen;
            }

            // KPIs
            KpiHeadLoss.Text = string.Format("System Head Loss: {0:F4} ft", ver.TotalHeadLossSystemFeet);
            KpiMaxHgl.Text = string.Format("Max HGL: {0:F2} ft", ver.MaxHglElevationFeet);
            KpiFreeboard.Text = string.Format("Freeboard: {0}", ver.HasFreeboardViolations ? "Violations Exist" : "All Compliant");
            KpiSurcharge.Text = string.Format("Surcharge: {0}", ver.HasSurcharge ? "Surcharged Reaches" : "Zero Surcharge");

            // Populate Grid
            ProfileGrid.Rows.Clear();
            foreach (var c in ver.ConduitStates)
            {
                ProfileGrid.Rows.Add(
                    c.ConduitId,
                    c.StartNodeId,
                    c.StopNodeId,
                    c.DiameterInches,
                    string.Format("{0:F2}", c.FlowCfs),
                    string.Format("{0:F2}", c.VelocityFps),
                    string.Format("{0:F4}", c.TotalHeadLossFeet),
                    string.Format("{0:F2}", c.UpstreamHglFeet),
                    string.Format("{0:F2}", c.DownstreamHglFeet),
                    string.Format("{0:F2}", c.UpstreamCrownFeet),
                    c.SurchargeState.ToString()
                );
            }

            // Populate Remediation proposals
            RemediationList.Items.Clear();
            if (ver.BottleneckProposals != null && ver.BottleneckProposals.Count > 0)
            {
                foreach (var prop in ver.BottleneckProposals)
                {
                    RemediationList.Items.Add(string.Format("[{0}] Upsize {1}\" -> {2}\" | Reduced Loss: -{3:F2} ft | Resolved Freeboard: {4:F2} ft",
                        prop.ConduitId, prop.CurrentDiameterInches, prop.ProposedDiameterInches, prop.HeadLossReductionFeet, prop.ResolvedUpstreamFreeboardFeet));
                }
            }
            else
            {
                RemediationList.Items.Add("No hydraulic bottlenecks detected; all conduits meet capacity requirements.");
            }

            // Render Markdown report preview
            ReportPreviewBox.Text = StormFlowExecutiveReportGenerator.GenerateMarkdownReport(context);

            // Populate graphical Profile Canvas
            ProfileCanvas.LoadData(context);
        }

        private void ExportReport()
        {
            if (activeContext == null)
            {
                MessageBox.Show(this, "No verification results loaded to export.", "Export Notice");
                return;
            }
            using (var sfd = new SaveFileDialog { Filter = "Markdown Report (*.md)|*.md", FileName = "StormFlow_Executive_Report.md" })
            {
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    File.WriteAllText(sfd.FileName, StormFlowExecutiveReportGenerator.GenerateMarkdownReport(activeContext), System.Text.Encoding.UTF8);
                    MessageBox.Show(this, "Executive report successfully exported to:\r\n" + sfd.FileName, "Export Succeeded");
                }
            }
        }

        private void ExportCsv()
        {
            if (activeContext == null)
            {
                MessageBox.Show(this, "No verification results loaded to export.", "Export Notice");
                return;
            }
            using (var sfd = new SaveFileDialog { Filter = "CSV Profiles (*.csv)|*.csv", FileName = "Conduit_Hydraulic_Profiles.csv" })
            {
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    File.WriteAllText(sfd.FileName, StormFlowExecutiveReportGenerator.GenerateConduitProfilesCsv(activeContext), System.Text.Encoding.UTF8);
                    MessageBox.Show(this, "Hydraulic profiles successfully exported to:\r\n" + sfd.FileName, "Export Succeeded");
                }
            }
        }

        private void ExportGraphic()
        {
            if (activeContext == null || !ProfileCanvas.HasProfileData)
            {
                MessageBox.Show(this, "No verification profile loaded to export graphic.", "Export Notice");
                return;
            }
            using (var sfd = new SaveFileDialog { Filter = "PNG Image (*.png)|*.png|Bitmap Image (*.bmp)|*.bmp", FileName = "StormFlow_Hydraulic_Profile.png" })
            {
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    using (var bmp = ProfileCanvas.RenderToBitmap(1600, 900))
                    {
                        var format = sfd.FileName.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ? ImageFormat.Bmp : ImageFormat.Png;
                        AdapterWriteBinding.EncodeImage(bmp, sfd.FileName, format);
                    }
                    MessageBox.Show(this, "Hydraulic profile graphic successfully exported to:\r\n" + sfd.FileName, "Export Succeeded");
                }
            }
        }
    }

    internal sealed class ProfileConduitSegment
    {
        public string ConduitId { get; set; }
        public int StartNodeId { get; set; }
        public int StopNodeId { get; set; }
        public string StartNodeLabel { get; set; }
        public string StopNodeLabel { get; set; }
        public double StartStationFeet { get; set; }
        public double StopStationFeet { get; set; }
        public double LengthFeet { get; set; }
        public int DiameterInches { get; set; }
        public double DiameterFeet => DiameterInches / 12.0;
        public double UpstreamInvertFeet { get; set; }
        public double DownstreamInvertFeet { get; set; }
        public double UpstreamCrownFeet { get; set; }
        public double DownstreamCrownFeet { get; set; }
        public double UpstreamHglFeet { get; set; }
        public double DownstreamHglFeet { get; set; }
        public double UpstreamEglFeet { get; set; }
        public double DownstreamEglFeet { get; set; }
        public double StartRimFeet { get; set; }
        public double StopRimFeet { get; set; }
        public double FlowCfs { get; set; }
        public double VelocityFps { get; set; }
        public double VelocityHeadFeet { get; set; }
        public double TotalHeadLossFeet { get; set; }
        public bool IsSurcharged { get; set; }
        public ConduitSurchargeState SurchargeState { get; set; }
    }

    internal sealed class ProfileNodeSegment
    {
        public int NodeId { get; set; }
        public string Label { get; set; }
        public double StationFeet { get; set; }
        public double RimElevationFeet { get; set; }
        public double InvertElevationFeet { get; set; }
        public double HglFeet { get; set; }
        public double FreeboardFeet { get; set; }
        public NodeFloodingState FloodingState { get; set; }
        public bool IsOutfall { get; set; }
    }

    internal sealed class HydraulicProfileCanvasControl : Control
    {
        internal readonly List<ProfileConduitSegment> Segments = new List<ProfileConduitSegment>();
        internal readonly List<ProfileNodeSegment> Nodes = new List<ProfileNodeSegment>();

        internal RectangleF WorldBounds { get; private set; }
        internal bool HasProfileData => Segments.Count > 0;

        internal bool ShowEgl { get; set; } = true;
        internal bool ShowGround { get; set; } = true;
        internal bool ShowFreeboard { get; set; } = true;
        internal bool ShowLabels { get; set; } = true;
        internal string SelectedConduitId { get; set; }

        private float zoomFactor = 1.0f;
        private PointF panOffset = PointF.Empty;
        private bool isPanning;
        private Point panStart;
        private Point lastMousePos = new Point(-1, -1);

        private ProfileConduitSegment hoveredSegment;
        private ProfileNodeSegment hoveredNode;

        internal HydraulicProfileCanvasControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(24, 28, 36);
            Cursor = Cursors.Cross;
        }

        internal void ResetView()
        {
            zoomFactor = 1.0f;
            panOffset = PointF.Empty;
            Invalidate();
        }

        internal void Zoom(float factor)
        {
            zoomFactor = Math.Max(0.2f, Math.Min(10.0f, zoomFactor * factor));
            Invalidate();
        }

        internal void LoadData(StormExecutiveReportContext context)
        {
            Segments.Clear();
            Nodes.Clear();

            if (context == null || context.VerificationResult == null)
            {
                WorldBounds = RectangleF.Empty;
                Invalidate();
                return;
            }

            var ver = context.VerificationResult;
            var nodeDict = ver.NodeStates.ToDictionary(n => n.NodeId);

            // Compute sequential stationing along conduits
            double currentStation = 0.0;
            var visitedNodes = new HashSet<int>();

            foreach (var c in ver.ConduitStates)
            {
                nodeDict.TryGetValue(c.StartNodeId, out var startNode);
                nodeDict.TryGetValue(c.StopNodeId, out var stopNode);

                if (startNode != null && !visitedNodes.Contains(startNode.NodeId))
                {
                    Nodes.Add(new ProfileNodeSegment
                    {
                        NodeId = startNode.NodeId,
                        Label = startNode.Label,
                        StationFeet = currentStation,
                        RimElevationFeet = startNode.RimElevationFeet,
                        InvertElevationFeet = startNode.InvertElevationFeet,
                        HglFeet = startNode.HglFeet,
                        FreeboardFeet = startNode.FreeboardFeet,
                        FloodingState = startNode.FloodingState,
                        IsOutfall = startNode.IsOutfall
                    });
                    visitedNodes.Add(startNode.NodeId);
                }

                double startStation = currentStation;
                double stopStation = currentStation + Math.Max(1.0, c.LengthFeet);
                currentStation = stopStation;

                if (stopNode != null && !visitedNodes.Contains(stopNode.NodeId))
                {
                    Nodes.Add(new ProfileNodeSegment
                    {
                        NodeId = stopNode.NodeId,
                        Label = stopNode.Label,
                        StationFeet = stopStation,
                        RimElevationFeet = stopNode.RimElevationFeet,
                        InvertElevationFeet = stopNode.InvertElevationFeet,
                        HglFeet = stopNode.HglFeet,
                        FreeboardFeet = stopNode.FreeboardFeet,
                        FloodingState = stopNode.FloodingState,
                        IsOutfall = stopNode.IsOutfall
                    });
                    visitedNodes.Add(stopNode.NodeId);
                }

                Segments.Add(new ProfileConduitSegment
                {
                    ConduitId = c.ConduitId,
                    StartNodeId = c.StartNodeId,
                    StopNodeId = c.StopNodeId,
                    StartNodeLabel = startNode != null ? startNode.Label : ("N" + c.StartNodeId),
                    StopNodeLabel = stopNode != null ? stopNode.Label : ("N" + c.StopNodeId),
                    StartStationFeet = startStation,
                    StopStationFeet = stopStation,
                    LengthFeet = c.LengthFeet,
                    DiameterInches = c.DiameterInches,
                    UpstreamInvertFeet = c.UpstreamInvertFeet,
                    DownstreamInvertFeet = c.DownstreamInvertFeet,
                    UpstreamCrownFeet = c.UpstreamCrownFeet,
                    DownstreamCrownFeet = c.DownstreamCrownFeet,
                    UpstreamHglFeet = c.UpstreamHglFeet,
                    DownstreamHglFeet = c.DownstreamHglFeet,
                    UpstreamEglFeet = c.UpstreamHglFeet + c.VelocityHeadFeet,
                    DownstreamEglFeet = c.DownstreamHglFeet + c.VelocityHeadFeet,
                    StartRimFeet = startNode != null ? startNode.RimElevationFeet : (c.UpstreamCrownFeet + 4.0),
                    StopRimFeet = stopNode != null ? stopNode.RimElevationFeet : (c.DownstreamCrownFeet + 4.0),
                    FlowCfs = c.FlowCfs,
                    VelocityFps = c.VelocityFps,
                    VelocityHeadFeet = c.VelocityHeadFeet,
                    TotalHeadLossFeet = c.TotalHeadLossFeet,
                    IsSurcharged = c.IsSurcharged,
                    SurchargeState = c.SurchargeState
                });
            }

            // Calculate bounding box in engineering world units
            if (Segments.Count > 0)
            {
                double minStation = 0.0;
                double maxStation = currentStation;
                double minElev = Segments.Min(s => Math.Min(s.UpstreamInvertFeet, s.DownstreamInvertFeet)) - 2.0;
                double maxElev = Math.Max(
                    Segments.Max(s => Math.Max(s.StartRimFeet, s.StopRimFeet)),
                    Segments.Max(s => Math.Max(s.UpstreamEglFeet, s.DownstreamEglFeet))
                ) + 3.0;

                WorldBounds = new RectangleF((float)minStation, (float)minElev, (float)(maxStation - minStation), (float)(maxElev - minElev));
            }
            else
            {
                WorldBounds = RectangleF.Empty;
            }

            ResetView();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
            {
                isPanning = true;
                panStart = e.Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            lastMousePos = e.Location;

            if (isPanning)
            {
                panOffset.X += (e.X - panStart.X);
                panOffset.Y += (e.Y - panStart.Y);
                panStart = e.Location;
                Invalidate();
                return;
            }

            // Perform Hit-testing
            hoveredSegment = null;
            hoveredNode = null;

            if (HasProfileData && ClientRectangle.Width > 120 && ClientRectangle.Height > 100)
            {
                foreach (var seg in Segments)
                {
                    var p1 = WorldToScreen(seg.StartStationFeet, (seg.UpstreamInvertFeet + seg.UpstreamCrownFeet) / 2.0, ClientRectangle);
                    var p2 = WorldToScreen(seg.StopStationFeet, (seg.DownstreamInvertFeet + seg.DownstreamCrownFeet) / 2.0, ClientRectangle);

                    if (e.X >= Math.Min(p1.X, p2.X) - 10 && e.X <= Math.Max(p1.X, p2.X) + 10 &&
                        e.Y >= Math.Min(p1.Y, p2.Y) - 25 && e.Y <= Math.Max(p1.Y, p2.Y) + 25)
                    {
                        hoveredSegment = seg;
                        break;
                    }
                }

                if (hoveredSegment == null)
                {
                    foreach (var node in Nodes)
                    {
                        var top = WorldToScreen(node.StationFeet, node.RimElevationFeet, ClientRectangle);
                        var bot = WorldToScreen(node.StationFeet, node.InvertElevationFeet, ClientRectangle);

                        if (Math.Abs(e.X - top.X) <= 12 && e.Y >= top.Y - 10 && e.Y <= bot.Y + 10)
                        {
                            hoveredNode = node;
                            break;
                        }
                    }
                }
            }

            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            isPanning = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float factor = e.Delta > 0 ? 1.15f : 0.85f;
            Zoom(factor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawProfile(e.Graphics, ClientRectangle);
        }

        internal Bitmap RenderToBitmap(int width, int height)
        {
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                DrawProfile(g, new Rectangle(0, 0, width, height));
            }
            return bmp;
        }

        private void DrawProfile(Graphics g, Rectangle bounds)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Deep engineering drafting slate background
            using (var bgBrush = new SolidBrush(Color.FromArgb(24, 28, 36)))
            {
                g.FillRectangle(bgBrush, bounds);
            }

            if (!HasProfileData)
            {
                using (var brush = new SolidBrush(Color.FromArgb(140, 150, 170)))
                using (var font = new Font("Segoe UI", 12f, FontStyle.Regular))
                {
                    var msg = "No Hydraulic Profile Loaded\r\nRun verification or execute pipeline to visualize dynamic HGL/EGL profiles.";
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(msg, font, brush, bounds, sf);
                }
                return;
            }

            // Margins for axes
            float marginL = 75f;
            float marginR = 40f;
            float marginT = 50f;
            float marginB = 55f;

            var plotRect = new RectangleF(marginL, marginT, bounds.Width - marginL - marginR, bounds.Height - marginT - marginB);
            if (plotRect.Width <= 10 || plotRect.Height <= 10) return;

            // Draw Coordinate Grid
            DrawGridAndAxes(g, bounds, plotRect);

            // Set clip to plot area for network components
            var oldClip = g.Clip;
            g.SetClip(plotRect);

            // 1. Draw Ground Surface & Hatch
            if (ShowGround)
            {
                DrawGroundSurface(g, bounds);
            }

            // 2. Draw Manhole Structure Vaults
            DrawManholes(g, bounds);

            // 3. Draw Conduit Barrels (Pipe Walls, Invert, Crown)
            DrawConduitBarrels(g, bounds);

            // 4. Draw Water Surfaces & Surcharge Zones
            DrawWaterAndSurcharge(g, bounds);

            // 5. Draw Hydraulic Grade Line (HGL)
            DrawHglLine(g, bounds);

            // 6. Draw Energy Grade Line (EGL)
            if (ShowEgl)
            {
                DrawEglLine(g, bounds);
            }

            // 7. Draw Structure & Conduit Annotations
            if (ShowLabels)
            {
                DrawAnnotations(g, bounds);
            }

            g.Clip = oldClip;

            // Draw Crosshair & Hover Tooltip
            DrawCrosshairAndTooltip(g, bounds, plotRect);

            // Draw Engineering Legend
            DrawLegend(g, bounds);
        }

        private PointF WorldToScreen(double station, double elevation, Rectangle bounds)
        {
            float marginL = 75f;
            float marginR = 40f;
            float marginT = 50f;
            float marginB = 55f;

            float plotW = Math.Max(10f, bounds.Width - marginL - marginR);
            float plotH = Math.Max(10f, bounds.Height - marginT - marginB);

            double xSpan = Math.Max(10.0, WorldBounds.Width);
            double ySpan = Math.Max(4.0, WorldBounds.Height);

            float normX = (float)((station - WorldBounds.Left) / xSpan);
            float normY = (float)((elevation - WorldBounds.Top) / ySpan);

            float screenX = marginL + (normX * plotW * zoomFactor) + panOffset.X;
            float screenY = (bounds.Height - marginB) - (normY * plotH * zoomFactor) + panOffset.Y;

            return new PointF(screenX, screenY);
        }

        private void ScreenToWorld(PointF pt, Rectangle bounds, out double station, out double elevation)
        {
            float marginL = 75f;
            float marginR = 40f;
            float marginT = 50f;
            float marginB = 55f;

            float plotW = Math.Max(10f, bounds.Width - marginL - marginR);
            float plotH = Math.Max(10f, bounds.Height - marginT - marginB);

            double xSpan = Math.Max(10.0, WorldBounds.Width);
            double ySpan = Math.Max(4.0, WorldBounds.Height);

            float normX = (pt.X - marginL - panOffset.X) / (plotW * zoomFactor);
            float normY = ((bounds.Height - marginB + panOffset.Y) - pt.Y) / (plotH * zoomFactor);

            station = WorldBounds.Left + (normX * xSpan);
            elevation = WorldBounds.Top + (normY * ySpan);
        }

        private void DrawGridAndAxes(Graphics g, Rectangle bounds, RectangleF plotRect)
        {
            using (var gridPen = new Pen(Color.FromArgb(40, 48, 62), 1f) { DashStyle = DashStyle.Dash })
            using (var axisPen = new Pen(Color.FromArgb(90, 105, 130), 1.5f))
            using (var textBrush = new SolidBrush(Color.FromArgb(170, 185, 205)))
            using (var font = new Font("Segoe UI", 8f))
            {
                // Plot boundary
                g.DrawRectangle(axisPen, plotRect.X, plotRect.Y, plotRect.Width, plotRect.Height);

                // Elevation Ticks (Vertical)
                double yMin = WorldBounds.Top;
                double yMax = WorldBounds.Bottom;
                double yStep = Math.Max(1.0, Math.Round((yMax - yMin) / 8.0));

                for (double y = Math.Floor(yMin); y <= yMax + 0.001; y += yStep)
                {
                    var p = WorldToScreen(WorldBounds.Left, y, bounds);
                    if (p.Y >= plotRect.Top && p.Y <= plotRect.Bottom)
                    {
                        g.DrawLine(gridPen, plotRect.Left, p.Y, plotRect.Right, p.Y);
                        string label = string.Format("{0:F1} ft", y);
                        g.DrawString(label, font, textBrush, plotRect.Left - 65, p.Y - 6);
                    }
                }

                // Station Ticks (Horizontal)
                double xMin = WorldBounds.Left;
                double xMax = WorldBounds.Right;
                double xStep = Math.Max(25.0, Math.Round((xMax - xMin) / 6.0 / 25.0) * 25.0);

                for (double x = xMin; x <= xMax + 0.001; x += xStep)
                {
                    var p = WorldToScreen(x, WorldBounds.Top, bounds);
                    if (p.X >= plotRect.Left && p.X <= plotRect.Right)
                    {
                        g.DrawLine(gridPen, p.X, plotRect.Top, p.X, plotRect.Bottom);
                        string label = string.Format("{0:F0} ft", x);
                        g.DrawString(label, font, textBrush, p.X - 18, plotRect.Bottom + 6);
                    }
                }

                // Axis Titles
                var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold);
                g.DrawString("Elevation (ft Datum)", titleFont, textBrush, 10, plotRect.Top - 25);
                g.DrawString("Stationing (ft along reach)", titleFont, textBrush, plotRect.Right - 150, plotRect.Bottom + 28);
                titleFont.Dispose();
            }
        }

        private void DrawGroundSurface(Graphics g, Rectangle bounds)
        {
            if (Nodes.Count < 2) return;

            var points = new List<PointF>();
            foreach (var n in Nodes)
            {
                points.Add(WorldToScreen(n.StationFeet, n.RimElevationFeet, bounds));
            }

            // Ground surface line
            using (var groundPen = new Pen(Color.FromArgb(184, 134, 11), 2.5f))
            {
                g.DrawLines(groundPen, points.ToArray());
            }

            // Fill ground hatch polygon
            var poly = new List<PointF>(points);
            var lastPt = points[points.Count - 1];
            var firstPt = points[0];
            var botRight = WorldToScreen(Nodes[Nodes.Count - 1].StationFeet, WorldBounds.Top, bounds);
            var botLeft = WorldToScreen(Nodes[0].StationFeet, WorldBounds.Top, bounds);

            poly.Add(botRight);
            poly.Add(botLeft);

            using (var groundBrush = new LinearGradientBrush(
                new PointF(0, points.Min(p => p.Y)),
                new PointF(0, botLeft.Y),
                Color.FromArgb(45, 184, 134, 11),
                Color.FromArgb(10, 184, 134, 11)))
            {
                g.FillPolygon(groundBrush, poly.ToArray());
            }
        }

        private void DrawManholes(Graphics g, Rectangle bounds)
        {
            using (var vaultBrush = new SolidBrush(Color.FromArgb(42, 48, 60)))
            using (var vaultPen = new Pen(Color.FromArgb(120, 135, 160), 1.5f))
            using (var rimPen = new Pen(Color.FromArgb(200, 215, 230), 3f))
            using (var fbPen = new Pen(Color.FromArgb(255, 140, 0), 1.5f) { DashStyle = DashStyle.Dash })
            {
                foreach (var n in Nodes)
                {
                    var rimPt = WorldToScreen(n.StationFeet, n.RimElevationFeet, bounds);
                    var invPt = WorldToScreen(n.StationFeet, n.InvertElevationFeet, bounds);

                    float width = 14f;
                    var vaultRect = new RectangleF(rimPt.X - width / 2f, rimPt.Y, width, Math.Max(2f, invPt.Y - rimPt.Y));

                    g.FillRectangle(vaultBrush, vaultRect);
                    g.DrawRectangle(vaultPen, vaultRect.X, vaultRect.Y, vaultRect.Width, vaultRect.Height);
                    g.DrawLine(rimPen, rimPt.X - width, rimPt.Y, rimPt.X + width, rimPt.Y);

                    // Draw 1.0 ft Freeboard Buffer line below rim
                    if (ShowFreeboard)
                    {
                        var fbPt = WorldToScreen(n.StationFeet, n.RimElevationFeet - 1.0, bounds);
                        g.DrawLine(fbPen, fbPt.X - width, fbPt.Y, fbPt.X + width, fbPt.Y);
                    }
                }
            }
        }

        private void DrawConduitBarrels(Graphics g, Rectangle bounds)
        {
            using (var pipeBrush = new SolidBrush(Color.FromArgb(32, 38, 48)))
            using (var wallPen = new Pen(Color.FromArgb(140, 150, 170), 2.0f))
            {
                foreach (var seg in Segments)
                {
                    var pCrownUp = WorldToScreen(seg.StartStationFeet, seg.UpstreamCrownFeet, bounds);
                    var pCrownDn = WorldToScreen(seg.StopStationFeet, seg.DownstreamCrownFeet, bounds);
                    var pInvDn = WorldToScreen(seg.StopStationFeet, seg.DownstreamInvertFeet, bounds);
                    var pInvUp = WorldToScreen(seg.StartStationFeet, seg.UpstreamInvertFeet, bounds);

                    PointF[] barrelPoly = { pCrownUp, pCrownDn, pInvDn, pInvUp };
                    g.FillPolygon(pipeBrush, barrelPoly);

                    if (!string.IsNullOrEmpty(SelectedConduitId) && string.Equals(seg.ConduitId, SelectedConduitId, StringComparison.OrdinalIgnoreCase))
                    {
                        using (var selPen = new Pen(Color.FromArgb(255, 215, 0), 3.0f))
                        {
                            g.DrawPolygon(selPen, barrelPoly);
                        }
                    }

                    // Crown line
                    g.DrawLine(wallPen, pCrownUp, pCrownDn);
                    // Invert line
                    g.DrawLine(wallPen, pInvUp, pInvDn);
                }
            }
        }

        private void DrawWaterAndSurcharge(Graphics g, Rectangle bounds)
        {
            using (var waterBrush = new SolidBrush(Color.FromArgb(90, 30, 144, 255)))
            using (var surchargeBrush = new SolidBrush(Color.FromArgb(140, 255, 140, 0)))
            {
                foreach (var seg in Segments)
                {
                    var pInvUp = WorldToScreen(seg.StartStationFeet, seg.UpstreamInvertFeet, bounds);
                    var pInvDn = WorldToScreen(seg.StopStationFeet, seg.DownstreamInvertFeet, bounds);
                    var pHglDn = WorldToScreen(seg.StopStationFeet, seg.DownstreamHglFeet, bounds);
                    var pHglUp = WorldToScreen(seg.StartStationFeet, seg.UpstreamHglFeet, bounds);

                    // Water polygon
                    PointF[] waterPoly = { pHglUp, pHglDn, pInvDn, pInvUp };
                    g.FillPolygon(waterBrush, waterPoly);

                    // If Surcharged: highlight zone between Crown and HGL in amber
                    if (seg.IsSurcharged)
                    {
                        var pCrownUp = WorldToScreen(seg.StartStationFeet, seg.UpstreamCrownFeet, bounds);
                        var pCrownDn = WorldToScreen(seg.StopStationFeet, seg.DownstreamCrownFeet, bounds);

                        PointF[] surchargePoly = { pHglUp, pHglDn, pCrownDn, pCrownUp };
                        g.FillPolygon(surchargeBrush, surchargePoly);
                    }
                }
            }
        }

        private void DrawHglLine(Graphics g, Rectangle bounds)
        {
            using (var hglPen = new Pen(Color.FromArgb(30, 144, 255), 3.0f))
            using (var markerBrush = new SolidBrush(Color.FromArgb(0, 191, 255)))
            {
                foreach (var seg in Segments)
                {
                    var pUp = WorldToScreen(seg.StartStationFeet, seg.UpstreamHglFeet, bounds);
                    var pDn = WorldToScreen(seg.StopStationFeet, seg.DownstreamHglFeet, bounds);

                    g.DrawLine(hglPen, pUp, pDn);

                    // Endpoint markers
                    g.FillEllipse(markerBrush, pUp.X - 3f, pUp.Y - 3f, 6f, 6f);
                    g.FillEllipse(markerBrush, pDn.X - 3f, pDn.Y - 3f, 6f, 6f);
                }
            }
        }

        private void DrawEglLine(Graphics g, Rectangle bounds)
        {
            using (var eglPen = new Pen(Color.FromArgb(0, 255, 255), 1.8f) { DashStyle = DashStyle.Dash })
            using (var markerBrush = new SolidBrush(Color.FromArgb(0, 255, 255)))
            {
                foreach (var seg in Segments)
                {
                    var pUp = WorldToScreen(seg.StartStationFeet, seg.UpstreamEglFeet, bounds);
                    var pDn = WorldToScreen(seg.StopStationFeet, seg.DownstreamEglFeet, bounds);

                    g.DrawLine(eglPen, pUp, pDn);

                    g.FillEllipse(markerBrush, pUp.X - 2.5f, pUp.Y - 2.5f, 5f, 5f);
                    g.FillEllipse(markerBrush, pDn.X - 2.5f, pDn.Y - 2.5f, 5f, 5f);
                }
            }
        }

        private void DrawAnnotations(Graphics g, Rectangle bounds)
        {
            using (var font = new Font("Segoe UI", 7.5f, FontStyle.Regular))
            using (var boldFont = new Font("Segoe UI", 8.0f, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.FromArgb(220, 230, 245)))
            using (var badgeBrush = new SolidBrush(Color.FromArgb(180, 255, 140, 0)))
            using (var floodBrush = new SolidBrush(Color.FromArgb(220, 220, 20, 20)))
            {
                // Structure annotations
                foreach (var n in Nodes)
                {
                    var rimPt = WorldToScreen(n.StationFeet, n.RimElevationFeet, bounds);
                    string label = string.Format("{0}\r\nRim {1:F2}'\r\nInv {2:F2}'", n.Label, n.RimElevationFeet, n.InvertElevationFeet);
                    g.DrawString(label, font, textBrush, rimPt.X - 25, rimPt.Y - 45);

                    // Freeboard Violation Badge
                    if (n.FreeboardFeet < 1.0)
                    {
                        string fbTxt = string.Format("[FB: {0:F2}']", n.FreeboardFeet);
                        g.DrawString(fbTxt, boldFont, badgeBrush, rimPt.X - 25, rimPt.Y - 58);
                    }

                    // Flooding badge
                    if (n.FloodingState == NodeFloodingState.SurfaceFlooding)
                    {
                        g.DrawString("[SURFACE FLOODING!]", boldFont, floodBrush, rimPt.X - 35, rimPt.Y - 72);
                    }
                }

                // Conduit annotations
                foreach (var seg in Segments)
                {
                    var midStation = (seg.StartStationFeet + seg.StopStationFeet) / 2.0;
                    var midInvert = (seg.UpstreamInvertFeet + seg.DownstreamInvertFeet) / 2.0;
                    var midPt = WorldToScreen(midStation, midInvert, bounds);

                    double slopePct = seg.LengthFeet > 0 ? ((seg.UpstreamInvertFeet - seg.DownstreamInvertFeet) / seg.LengthFeet) * 100.0 : 0.0;
                    string desc = string.Format("{0}: {1}\" RCP | L={2:F0}' | S={3:F2}%\r\nQ={4:F1} cfs, V={5:F1} fps",
                        seg.ConduitId, seg.DiameterInches, seg.LengthFeet, slopePct, seg.FlowCfs, seg.VelocityFps);

                    g.DrawString(desc, font, textBrush, midPt.X - 60, midPt.Y + 8);

                    // Surcharge Badge
                    if (seg.IsSurcharged)
                    {
                        var crownMid = (seg.UpstreamCrownFeet + seg.DownstreamCrownFeet) / 2.0;
                        var pCrown = WorldToScreen(midStation, crownMid, bounds);
                        g.DrawString("[SURCHARGED REACH]", boldFont, badgeBrush, pCrown.X - 45, pCrown.Y - 18);
                    }
                }
            }
        }

        private void DrawCrosshairAndTooltip(Graphics g, Rectangle bounds, RectangleF plotRect)
        {
            if (lastMousePos.X < plotRect.Left || lastMousePos.X > plotRect.Right ||
                lastMousePos.Y < plotRect.Top || lastMousePos.Y > plotRect.Bottom)
                return;

            ScreenToWorld(lastMousePos, bounds, out double station, out double elevation);

            using (var chPen = new Pen(Color.FromArgb(90, 180, 200, 220), 1f) { DashStyle = DashStyle.Dot })
            {
                g.DrawLine(chPen, plotRect.Left, lastMousePos.Y, plotRect.Right, lastMousePos.Y);
                g.DrawLine(chPen, lastMousePos.X, plotRect.Top, lastMousePos.X, plotRect.Bottom);
            }

            // Crosshair coordinates badge
            string coordText = string.Format("Sta: {0:F1} ft | Elev: {1:F2} ft", station, elevation);
            using (var font = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (var badgeBrush = new SolidBrush(Color.FromArgb(220, 28, 34, 46)))
            using (var borderPen = new Pen(Color.FromArgb(70, 85, 110)))
            using (var textBrush = new SolidBrush(Color.FromArgb(240, 245, 255)))
            {
                var size = g.MeasureString(coordText, font);
                float bx = Math.Min(plotRect.Right - size.Width - 10, Math.Max(plotRect.Left + 10, lastMousePos.X + 12));
                float by = Math.Min(plotRect.Bottom - size.Height - 10, Math.Max(plotRect.Top + 10, lastMousePos.Y - 25));

                g.FillRectangle(badgeBrush, bx, by, size.Width + 8, size.Height + 4);
                g.DrawRectangle(borderPen, bx, by, size.Width + 8, size.Height + 4);
                g.DrawString(coordText, font, textBrush, bx + 4, by + 2);
            }

            // Hover tooltip details for Conduit or Node
            if (hoveredSegment != null || hoveredNode != null)
            {
                var tipLines = new List<string>();
                if (hoveredSegment != null)
                {
                    tipLines.Add(string.Format("Conduit: {0} ({1}\" RCP, L={2:F0} ft)", hoveredSegment.ConduitId, hoveredSegment.DiameterInches, hoveredSegment.LengthFeet));
                    tipLines.Add(string.Format("Flow: {0:F2} cfs | Velocity: {1:F2} fps (hv={2:F3} ft)", hoveredSegment.FlowCfs, hoveredSegment.VelocityFps, hoveredSegment.VelocityHeadFeet));
                    tipLines.Add(string.Format("HGL: {0:F2} -> {1:F2} ft | Crown: {2:F2} -> {3:F2} ft", hoveredSegment.UpstreamHglFeet, hoveredSegment.DownstreamHglFeet, hoveredSegment.UpstreamCrownFeet, hoveredSegment.DownstreamCrownFeet));
                    tipLines.Add(string.Format("Head Loss: {0:F4} ft | State: {1}", hoveredSegment.TotalHeadLossFeet, hoveredSegment.SurchargeState));
                }
                else if (hoveredNode != null)
                {
                    tipLines.Add(string.Format("Structure: {0} (Station: {1:F0} ft)", hoveredNode.Label, hoveredNode.StationFeet));
                    tipLines.Add(string.Format("Rim: {0:F2} ft | Invert: {1:F2} ft", hoveredNode.RimElevationFeet, hoveredNode.InvertElevationFeet));
                    tipLines.Add(string.Format("HGL: {0:F2} ft | Safe Freeboard: {1:F2} ft", hoveredNode.HglFeet, hoveredNode.FreeboardFeet));
                    tipLines.Add(string.Format("Flooding State: {0}", hoveredNode.FloodingState));
                }

                using (var tipFont = new Font("Segoe UI", 8.0f))
                using (var tipBrush = new SolidBrush(Color.FromArgb(235, 20, 24, 32)))
                using (var tipPen = new Pen(Color.FromArgb(90, 140, 200), 1.5f))
                using (var textBrush = new SolidBrush(Color.FromArgb(220, 235, 255)))
                {
                    float maxW = tipLines.Max(l => g.MeasureString(l, tipFont).Width) + 16f;
                    float totH = (tipLines.Count * 17f) + 10f;

                    float tx = Math.Min(plotRect.Right - maxW - 10, Math.Max(plotRect.Left + 10, lastMousePos.X + 15));
                    float ty = Math.Min(plotRect.Bottom - totH - 10, Math.Max(plotRect.Top + 10, lastMousePos.Y + 15));

                    g.FillRectangle(tipBrush, tx, ty, maxW, totH);
                    g.DrawRectangle(tipPen, tx, ty, maxW, totH);

                    float cy = ty + 5f;
                    foreach (var line in tipLines)
                    {
                        g.DrawString(line, tipFont, textBrush, tx + 8f, cy);
                        cy += 17f;
                    }
                }
            }
        }

        private void DrawLegend(Graphics g, Rectangle bounds)
        {
            float legW = 160f;
            float legH = 135f;
            float legX = bounds.Width - legW - 45f;
            float legY = 55f;

            using (var legBg = new SolidBrush(Color.FromArgb(200, 26, 32, 42)))
            using (var legPen = new Pen(Color.FromArgb(60, 75, 95)))
            using (var font = new Font("Segoe UI", 7.5f))
            using (var boldFont = new Font("Segoe UI", 8.0f, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.FromArgb(220, 230, 245)))
            {
                g.FillRectangle(legBg, legX, legY, legW, legH);
                g.DrawRectangle(legPen, legX, legY, legW, legH);

                g.DrawString("HYDRAULIC LEGEND", boldFont, textBrush, legX + 10, legY + 8);

                float cy = legY + 28f;

                // Ground line
                using (var pen = new Pen(Color.FromArgb(184, 134, 11), 2.5f))
                {
                    g.DrawLine(pen, legX + 10, cy + 6, legX + 35, cy + 6);
                }
                g.DrawString("Ground Surface", font, textBrush, legX + 42, cy);
                cy += 17f;

                // Pipe Crown / Invert
                using (var pen = new Pen(Color.FromArgb(140, 150, 170), 2.0f))
                {
                    g.DrawLine(pen, legX + 10, cy + 6, legX + 35, cy + 6);
                }
                g.DrawString("Conduit Crown / Invert", font, textBrush, legX + 42, cy);
                cy += 17f;

                // HGL Line
                using (var pen = new Pen(Color.FromArgb(30, 144, 255), 2.5f))
                {
                    g.DrawLine(pen, legX + 10, cy + 6, legX + 35, cy + 6);
                }
                g.DrawString("Hydraulic Grade (HGL)", font, textBrush, legX + 42, cy);
                cy += 17f;

                // EGL Line
                using (var pen = new Pen(Color.FromArgb(0, 255, 255), 1.8f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(pen, legX + 10, cy + 6, legX + 35, cy + 6);
                }
                g.DrawString("Energy Grade (EGL)", font, textBrush, legX + 42, cy);
                cy += 17f;

                // Freeboard Buffer
                using (var pen = new Pen(Color.FromArgb(255, 140, 0), 1.5f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(pen, legX + 10, cy + 6, legX + 35, cy + 6);
                }
                g.DrawString("Freeboard Limit (1.0')", font, textBrush, legX + 42, cy);
                cy += 17f;

                // Surcharge Swatch
                using (var brush = new SolidBrush(Color.FromArgb(180, 255, 140, 0)))
                {
                    g.FillRectangle(brush, legX + 10, cy + 2, 25, 10);
                }
                g.DrawString("Surcharge Zone", font, textBrush, legX + 42, cy);
            }
        }
    }
}
