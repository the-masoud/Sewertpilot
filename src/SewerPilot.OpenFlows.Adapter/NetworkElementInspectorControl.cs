using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    // Production-quality, high-density Network Element Inspector.
    // Displays physical, hydraulic, compliance violations, and associated P25 decisions.
    // Offline / headless testable: does not perform direct model writes or unverified solver calls.
    internal sealed class NetworkElementInspectorControl : UserControl
    {
        private readonly Label titleBanner = new Label
        {
            Name = "InspectorTitleBanner",
            Dock = DockStyle.Top,
            Height = 32,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(32, 38, 50),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "  Element Inspector — No Element Selected"
        };

        private readonly Label statusBanner = new Label
        {
            Name = "InspectorStatusBanner",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.DarkGray,
            BackColor = Color.FromArgb(24, 28, 36),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "  Status: Select an element from the canvas or table."
        };

        private readonly FlowLayoutPanel actionToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = Color.FromArgb(28, 32, 42),
            Padding = new Padding(4, 3, 4, 3)
        };

        private readonly Button btnTraceUp = new Button
        {
            Text = "Trace Upstream",
            Name = "BtnTraceUp",
            Width = 105,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.LightCyan,
            BackColor = Color.FromArgb(40, 50, 68),
            Enabled = false
        };

        private readonly Button btnTraceDown = new Button
        {
            Text = "Trace Downstream",
            Name = "BtnTraceDown",
            Width = 120,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.LightCyan,
            BackColor = Color.FromArgb(40, 50, 68),
            Enabled = false
        };

        private readonly Button btnClearTrace = new Button
        {
            Text = "Clear Trace",
            Name = "BtnClearTrace",
            Width = 85,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.WhiteSmoke,
            BackColor = Color.FromArgb(48, 54, 66)
        };

        private readonly Button btnViewProfile = new Button
        {
            Text = "View in Profile",
            Name = "BtnViewProfile",
            Width = 105,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.Yellow,
            BackColor = Color.FromArgb(50, 58, 40),
            Enabled = false
        };

        private readonly DataGridView propertyGrid = new DataGridView
        {
            Name = "InspectorPropertyGrid",
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            BackgroundColor = Color.FromArgb(20, 24, 30),
            GridColor = Color.FromArgb(40, 46, 60),
            ForeColor = Color.FromArgb(220, 230, 245),
            BorderStyle = BorderStyle.None
        };

        private readonly TabControl bottomTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Name = "InspectorTabs"
        };

        private readonly ListBox violationsList = new ListBox
        {
            Name = "InspectorViolationsList",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(24, 28, 36),
            ForeColor = Color.FromArgb(255, 100, 100),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.5f)
        };

        private readonly ListBox decisionsList = new ListBox
        {
            Name = "InspectorDecisionsList",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(24, 28, 36),
            ForeColor = Color.FromArgb(140, 210, 255),
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 8.5f)
        };

        private PlanCanvasElementKind currentKind = PlanCanvasElementKind.None;
        private int currentId = 0;
        private string currentLabel = "";
        private List<EngineeringDecision> activeDecisions = new List<EngineeringDecision>();

        public event EventHandler<int> TraceUpstreamRequested;
        public event EventHandler<int> TraceDownstreamRequested;
        public event EventHandler ClearTraceRequested;
        public event EventHandler<string> ViewInProfileRequested;

        public PlanCanvasElementKind CurrentKind => currentKind;
        public int CurrentId => currentId;
        public string CurrentLabel => currentLabel;
        public int PropertyRowCount => propertyGrid.Rows.Count;
        public int ViolationItemCount => violationsList.Items.Count;
        public int DecisionItemCount => decisionsList.Items.Count;
        public string TitleText => titleBanner.Text;
        public string StatusText => statusBanner.Text;

        public NetworkElementInspectorControl()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(20, 24, 30);
            Dock = DockStyle.Fill;

            // Setup DataGridView Columns
            propertyGrid.DefaultCellStyle.BackColor = Color.FromArgb(24, 28, 36);
            propertyGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(45, 60, 85);
            propertyGrid.DefaultCellStyle.SelectionForeColor = Color.White;
            propertyGrid.DefaultCellStyle.Font = new Font("Segoe UI", 8.5f);
            propertyGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(32, 38, 50);
            propertyGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(180, 195, 220);
            propertyGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            propertyGrid.EnableHeadersVisualStyles = false;

            propertyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "Category", FillWeight = 25 });
            propertyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Property", HeaderText = "Property", FillWeight = 35 });
            propertyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", FillWeight = 30 });
            propertyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Unit", HeaderText = "Unit", FillWeight = 10 });

            // Setup Toolbar
            btnTraceUp.Click += (s, e) => { if (currentId != 0) TraceUpstreamRequested?.Invoke(this, currentId); };
            btnTraceDown.Click += (s, e) => { if (currentId != 0) TraceDownstreamRequested?.Invoke(this, currentId); };
            btnClearTrace.Click += (s, e) => ClearTraceRequested?.Invoke(this, EventArgs.Empty);
            btnViewProfile.Click += (s, e) => { if (!string.IsNullOrEmpty(currentLabel)) ViewInProfileRequested?.Invoke(this, currentLabel); };

            actionToolbar.Controls.AddRange(new Control[] { btnTraceUp, btnTraceDown, btnClearTrace, btnViewProfile });

            // Split Container for Properties (top) and Violations / Decisions (bottom)
            var split = new SplitContainer
            {
                Orientation = Orientation.Horizontal,
                Dock = DockStyle.Fill,
                SplitterDistance = 320,
                BackColor = Color.FromArgb(35, 42, 56)
            };
            split.Panel1.Controls.Add(propertyGrid);

            var tabViolations = new TabPage("Violations & Compliance");
            tabViolations.BackColor = Color.FromArgb(24, 28, 36);
            tabViolations.Controls.Add(violationsList);

            var tabDecisions = new TabPage("P25 Engineering Decisions");
            tabDecisions.BackColor = Color.FromArgb(24, 28, 36);
            tabDecisions.Controls.Add(decisionsList);

            bottomTabs.TabPages.AddRange(new[] { tabViolations, tabDecisions });
            split.Panel2.Controls.Add(bottomTabs);

            Controls.Add(split);
            Controls.Add(actionToolbar);
            Controls.Add(statusBanner);
            Controls.Add(titleBanner);
        }

        public void BindDecisions(IEnumerable<EngineeringDecision> decisions)
        {
            activeDecisions = decisions != null ? decisions.ToList() : new List<EngineeringDecision>();
        }

        public void ClearInspection()
        {
            currentKind = PlanCanvasElementKind.None;
            currentId = 0;
            currentLabel = "";
            titleBanner.Text = "  Element Inspector — No Element Selected";
            titleBanner.BackColor = Color.FromArgb(32, 38, 50);
            statusBanner.Text = "  Status: Select an element from the canvas or table.";
            statusBanner.ForeColor = Color.DarkGray;
            propertyGrid.Rows.Clear();
            violationsList.Items.Clear();
            decisionsList.Items.Clear();
            btnTraceUp.Enabled = btnTraceDown.Enabled = btnViewProfile.Enabled = false;
        }

        public void InspectElement(PlanCanvasElementKind kind, object item, IEnumerable<EngineeringDecision> decisions = null)
        {
            if (decisions != null) activeDecisions = decisions.ToList();

            if (kind == PlanCanvasElementKind.Conduit && item is PlanConduitItem conduit)
            {
                InspectConduit(conduit);
            }
            else if (kind == PlanCanvasElementKind.Structure && item is PlanStructureItem structure)
            {
                InspectStructure(structure);
            }
            else
            {
                ClearInspection();
            }
        }

        public void InspectConduit(PlanConduitItem c)
        {
            if (c == null) { ClearInspection(); return; }

            currentKind = PlanCanvasElementKind.Conduit;
            currentId = c.Id;
            currentLabel = c.Label;
            btnTraceUp.Enabled = btnTraceDown.Enabled = btnViewProfile.Enabled = true;

            titleBanner.Text = string.Format(CultureInfo.InvariantCulture, "  CONDUIT: {0} (ID #{1})", c.Label, c.Id);
            titleBanner.BackColor = Color.FromArgb(26, 45, 75);

            propertyGrid.Rows.Clear();

            // 1. Identification
            AddPropertyRow("Identification", "Element Kind", "Conduit", "");
            AddPropertyRow("Identification", "Element ID", c.Id.ToString(CultureInfo.InvariantCulture), "");
            AddPropertyRow("Identification", "Label", c.Label, "");
            AddPropertyRow("Identification", "Start Node (Upstream)", c.StartNodeId.ToString(CultureInfo.InvariantCulture), "");
            AddPropertyRow("Identification", "Stop Node (Downstream)", c.StopNodeId.ToString(CultureInfo.InvariantCulture), "");

            // 2. Physical / Geometric
            AddPropertyRow("Physical", "Diameter", string.Format(CultureInfo.InvariantCulture, "{0:F1}", c.DiameterInches), "in");
            AddPropertyRow("Physical", "Length", string.Format(CultureInfo.InvariantCulture, "{0:F2}", c.LengthFeet), "ft");
            AddPropertyRow("Physical", "Slope", string.Format(CultureInfo.InvariantCulture, "{0:F4} ({1:P2})", c.Slope, c.Slope), "ft/ft");
            AddPropertyRow("Physical", "Shape", c.Shape, "");
            AddPropertyRow("Physical", "Material", c.Material, "");
            AddPropertyRow("Physical", "Manning's n", string.Format(CultureInfo.InvariantCulture, "{0:F4}", c.ManningsN.GetValueOrDefault(0.013)), "");

            if (c.StartInvertFeet.HasValue)
                AddPropertyRow("Physical", "Upstream Invert", string.Format(CultureInfo.InvariantCulture, "{0:F2}", c.StartInvertFeet.Value), "ft");
            if (c.StopInvertFeet.HasValue)
                AddPropertyRow("Physical", "Downstream Invert", string.Format(CultureInfo.InvariantCulture, "{0:F2}", c.StopInvertFeet.Value), "ft");

            // 3. Hydraulic Performance
            if (c.FlowCfs.HasValue)
            {
                AddPropertyRow("Hydraulics", "Design Flow (Q)", string.Format(CultureInfo.InvariantCulture, "{0:F2}", c.FlowCfs.Value), "cfs");
                AddPropertyRow("Hydraulics", "Velocity (V)", string.Format(CultureInfo.InvariantCulture, "{0:F2}", c.VelocityFps.GetValueOrDefault(0.0)), "fps");
                AddPropertyRow("Hydraulics", "Head Loss", string.Format(CultureInfo.InvariantCulture, "{0:F4}", c.TotalHeadLossFeet.GetValueOrDefault(0.0)), "ft");
                AddPropertyRow("Hydraulics", "Upstream HGL", string.Format(CultureInfo.InvariantCulture, "{0:F2}", c.UpstreamHglFeet.GetValueOrDefault(0.0)), "ft");
                AddPropertyRow("Hydraulics", "Downstream HGL", string.Format(CultureInfo.InvariantCulture, "{0:F2}", c.DownstreamHglFeet.GetValueOrDefault(0.0)), "ft");
                AddPropertyRow("Hydraulics", "Surcharge State", c.IsSurcharged ? "SURCHARGED" : "Free Surface", "");
            }
            else
            {
                AddPropertyRow("Hydraulics", "Status", "No active verification results loaded", "");
            }

            // 4. Violations
            violationsList.Items.Clear();
            if (c.Violations != null && c.Violations.Count > 0)
            {
                foreach (var v in c.Violations) violationsList.Items.Add("• " + v);
                statusBanner.Text = string.Format(CultureInfo.InvariantCulture, "  VIOLATIONS DETECTED: {0} engineering rule exceedance(s)", c.Violations.Count);
                statusBanner.ForeColor = Color.FromArgb(255, 100, 100);
            }
            else
            {
                violationsList.Items.Add("✓ All hydraulic & geometric criteria compliant; zero violations.");
                statusBanner.Text = "  Status: FULLY COMPLIANT (Passes all engineering criteria)";
                statusBanner.ForeColor = Color.FromArgb(100, 230, 140);
            }

            // 5. P25 Decisions
            PopulateDecisions(c.Id.ToString(CultureInfo.InvariantCulture), c.Label);
        }

        public void InspectStructure(PlanStructureItem s)
        {
            if (s == null) { ClearInspection(); return; }

            currentKind = PlanCanvasElementKind.Structure;
            currentId = s.Id;
            currentLabel = s.Label;
            btnTraceUp.Enabled = btnTraceDown.Enabled = true;
            btnViewProfile.Enabled = false;

            titleBanner.Text = string.Format(CultureInfo.InvariantCulture, "  STRUCTURE: {0} (ID #{1})", s.Label, s.Id);
            titleBanner.BackColor = Color.FromArgb(35, 55, 60);

            propertyGrid.Rows.Clear();

            // 1. Identification
            AddPropertyRow("Identification", "Element Kind", "Structure", "");
            AddPropertyRow("Identification", "Element ID", s.Id.ToString(CultureInfo.InvariantCulture), "");
            AddPropertyRow("Identification", "Label", s.Label, "");
            AddPropertyRow("Identification", "Category", s.Category, "");
            AddPropertyRow("Identification", "Is Outfall", s.IsOutfall ? "Yes (Terminal Node)" : "No (Junction)", "");

            // 2. Physical / Geometric
            AddPropertyRow("Physical", "Location X", string.Format(CultureInfo.InvariantCulture, "{0:F2}", s.Location.X), "ft");
            AddPropertyRow("Physical", "Location Y", string.Format(CultureInfo.InvariantCulture, "{0:F2}", s.Location.Y), "ft");

            if (s.RimElevationFeet.HasValue)
                AddPropertyRow("Physical", "Rim Elevation", string.Format(CultureInfo.InvariantCulture, "{0:F2}", s.RimElevationFeet.Value), "ft");
            if (s.InvertElevationFeet.HasValue)
                AddPropertyRow("Physical", "Invert Elevation", string.Format(CultureInfo.InvariantCulture, "{0:F2}", s.InvertElevationFeet.Value), "ft");
            if (s.GroundElevationFeet.HasValue)
                AddPropertyRow("Physical", "Ground Elevation", string.Format(CultureInfo.InvariantCulture, "{0:F2}", s.GroundElevationFeet.Value), "ft");

            if (s.RimElevationFeet.HasValue && s.InvertElevationFeet.HasValue)
            {
                double depth = s.RimElevationFeet.Value - s.InvertElevationFeet.Value;
                AddPropertyRow("Physical", "Structure Depth", string.Format(CultureInfo.InvariantCulture, "{0:F2}", depth), "ft");
            }

            // 3. Hydraulic Performance
            if (s.HglFeet.HasValue)
            {
                AddPropertyRow("Hydraulics", "Calculated HGL", string.Format(CultureInfo.InvariantCulture, "{0:F2}", s.HglFeet.Value), "ft");
                AddPropertyRow("Hydraulics", "Freeboard Margin", string.Format(CultureInfo.InvariantCulture, "{0:F2}", s.FreeboardFeet.GetValueOrDefault(0.0)), "ft");
                AddPropertyRow("Hydraulics", "Surface Flooding", s.HglFeet.Value > s.RimElevationFeet.GetValueOrDefault(double.MaxValue) ? "FLOODED" : "Safe", "");
            }
            else
            {
                AddPropertyRow("Hydraulics", "Status", "No active verification results loaded", "");
            }

            // 4. Violations
            violationsList.Items.Clear();
            if (s.Violations != null && s.Violations.Count > 0)
            {
                foreach (var v in s.Violations) violationsList.Items.Add("• " + v);
                statusBanner.Text = string.Format(CultureInfo.InvariantCulture, "  VIOLATIONS DETECTED: {0} engineering rule exceedance(s)", s.Violations.Count);
                statusBanner.ForeColor = Color.FromArgb(255, 100, 100);
            }
            else
            {
                violationsList.Items.Add("✓ Structure elevations and freeboard meet all hydraulic standards.");
                statusBanner.Text = "  Status: FULLY COMPLIANT (Passes all engineering criteria)";
                statusBanner.ForeColor = Color.FromArgb(100, 230, 140);
            }

            // 5. P25 Decisions
            PopulateDecisions(s.Id.ToString(CultureInfo.InvariantCulture), s.Label);
        }

        private void PopulateDecisions(string idStr, string labelStr)
        {
            decisionsList.Items.Clear();
            var matches = activeDecisions.Where(d =>
                d.SubjectElements != null &&
                (d.SubjectElements.Contains(idStr) ||
                 (!string.IsNullOrEmpty(labelStr) && d.SubjectElements.Contains(labelStr)))).ToList();

            if (matches.Count > 0)
            {
                foreach (var dec in matches)
                {
                    decisionsList.Items.Add(string.Format("[{0}] {1} — Status: {2}", dec.DecisionId, dec.ProblemStatement, dec.Status));
                    decisionsList.Items.Add(string.Format("   Category: {0} | Module: {1}", dec.Category, dec.Module));
                    if (dec.Resolution != null)
                    {
                        decisionsList.Items.Add(string.Format("   Selected: {0} by {1} ({2})",
                            dec.Resolution.SelectedAlternativeId,
                            dec.Resolution.DecisionMaker?.Name ?? "Engineer",
                            dec.Resolution.DecisionMaker?.ProfessionalTitle ?? "PE"));
                        decisionsList.Items.Add(string.Format("   Rationale: {0}", dec.Resolution.EngineeringJustification));
                    }
                    decisionsList.Items.Add("");
                }
            }
            else
            {
                decisionsList.Items.Add("No formal P25 engineering decisions registered for this element.");
            }
        }

        private void AddPropertyRow(string category, string prop, string value, string unit)
        {
            propertyGrid.Rows.Add(category, prop, value, unit);
        }
    }
}
