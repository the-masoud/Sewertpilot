using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    // Internal WinForms view; public adapter contracts remain free of UI/vendor types.
    internal sealed class ProjectWorkspaceForm : Form
    {
        internal readonly Button NewProject = ActionButton("New Project", 120);
        internal readonly Button OpenProject = ActionButton("Open Project", 120);
        internal readonly Button SaveProject = ActionButton("Save Project", 120);
        internal readonly Button SelectModel = ActionButton("Select SewerGEMS Model", 205);
        internal readonly Button LoadInputs = ActionButton("2. Load engineering inputs", 195);
        internal readonly Button Generate = ActionButton("3. Generate / preview", 175);
        internal readonly Button Confirm = ActionButton("4. Confirm", 120);
        internal readonly Button Execute = ActionButton("5. Execute", 120);
        internal readonly CheckBox Calculate = new CheckBox { Name="CalculateAfterChanges", AccessibleName="Calculate after persisted changes", Text="Calculate after changes", AutoSize=true };
        internal readonly Button OpenEvidence = ActionButton("Open evidence folder", 160);
        internal readonly Button ExportDiagnostics = ActionButton("Export Diagnostics", 150);
        internal readonly TextBox Output = ReadOnlyText("Engineering output", DockStyle.Fill);
        internal readonly EngineeringDesignPanel DesignWorkspace = new EngineeringDesignPanel();
        internal readonly StormFlowDesignPanel StormFlowWorkspace = new StormFlowDesignPanel();
        internal readonly HydraulicProfileViewerControl HydraulicProfileViewer = new HydraulicProfileViewerControl();
        internal readonly NetworkPlanCanvasControl PlanCanvas = new NetworkPlanCanvasControl();
        internal readonly NetworkElementInspectorControl ElementInspector = new NetworkElementInspectorControl();
        internal readonly EngineeringReportsPanel ReportsWorkspace = new EngineeringReportsPanel();
        internal readonly P16VerifiedReadbackPanel NativeResultsWorkspace = new P16VerifiedReadbackPanel();
        internal readonly VerifiedHydraulicResultsPanel VerifiedHydraulicResultsWorkspace = new VerifiedHydraulicResultsPanel();
        internal readonly GeneralModelCalculationPanel HydraulicCalculationWorkspace = new GeneralModelCalculationPanel();
        private readonly TextBox metadata = ReadOnlyText("Project metadata", DockStyle.Top);
        private readonly TextBox details = ReadOnlyText("Selected row details", DockStyle.Bottom);
        private readonly Label status = new Label { Name = "OperationStatus", AccessibleName = "Operation status", Dock = DockStyle.Top, Height = 28 };
        private readonly DataGridView conduits = Grid("Conduit table", NetworkExplorerProjection.ConduitHeaders);
        private readonly DataGridView structures = Grid("Structure table", NetworkExplorerProjection.StructureHeaders);
        private bool busy;
        private bool nativeReady;
        private bool calculationReady;
        private string boundStormDirectory;
        private readonly TabControl workspaceTabs;
        private readonly TabPage engineeringStatusTab;
        internal ProjectWorkspaceForm(GravityOperatingMode mode)
        {
            Text = "SewerPilot — " + mode; Width = 1180; Height = 850; StartPosition = FormStartPosition.CenterScreen;
            var projectActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38 };
            projectActions.Controls.AddRange(new Control[] { NewProject, OpenProject, SaveProject, SelectModel, ExportDiagnostics });
            var engineeringActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38 };
            Generate.Text="3. Preview selected design";Generate.AccessibleName=Generate.Text;
            engineeringActions.Controls.AddRange(new Control[] { LoadInputs, Generate, Calculate, Confirm, Execute, OpenEvidence });
            var tabs = new TabControl { Name = "WorkspaceTabs", AccessibleName = "Workspace tabs", Dock = DockStyle.Fill };

            // 2D Plan Canvas & Inspector Workspace Split Pane
            var planSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 750,
                BackColor = Color.FromArgb(35, 42, 56)
            };

            var planCanvasPanel = new Panel { Dock = DockStyle.Fill };
            var planToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, BackColor = Color.FromArgb(32, 36, 44) };
            var btnPlanFit = new Button { Text = "Fit Network", Width = 90, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(48, 54, 66) };
            var btnPlanZoomIn = new Button { Text = "Zoom +", Width = 65, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(48, 54, 66) };
            var btnPlanZoomOut = new Button { Text = "Zoom -", Width = 65, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(48, 54, 66) };
            var chkPlanViolations = new CheckBox { Text = "Show Violations", Checked = true, AutoSize = true, ForeColor = Color.Tomato, Padding = new Padding(4, 3, 0, 0) };
            var chkPlanLabels = new CheckBox { Text = "Show Labels", Checked = true, AutoSize = true, ForeColor = Color.WhiteSmoke, Padding = new Padding(4, 3, 0, 0) };
            var chkPlanArrows = new CheckBox { Text = "Show Flow Arrows", Checked = true, AutoSize = true, ForeColor = Color.LightCyan, Padding = new Padding(4, 3, 0, 0) };

            btnPlanFit.Click += (s, e) => PlanCanvas.ZoomToFit();
            btnPlanZoomIn.Click += (s, e) => PlanCanvas.Zoom(1.25f);
            btnPlanZoomOut.Click += (s, e) => PlanCanvas.Zoom(0.80f);
            chkPlanViolations.CheckedChanged += (s, e) => { PlanCanvas.ShowViolations = chkPlanViolations.Checked; PlanCanvas.Invalidate(); };
            chkPlanLabels.CheckedChanged += (s, e) => { PlanCanvas.ShowLabels = chkPlanLabels.Checked; PlanCanvas.Invalidate(); };
            chkPlanArrows.CheckedChanged += (s, e) => { PlanCanvas.ShowFlowArrows = chkPlanArrows.Checked; PlanCanvas.Invalidate(); };

            planToolbar.Controls.AddRange(new Control[] { btnPlanFit, btnPlanZoomIn, btnPlanZoomOut, chkPlanViolations, chkPlanLabels, chkPlanArrows });
            planCanvasPanel.Controls.Add(PlanCanvas);
            planCanvasPanel.Controls.Add(planToolbar);

            planSplit.Panel1.Controls.Add(planCanvasPanel);
            planSplit.Panel2.Controls.Add(ElementInspector);

            var planTab = new TabPage("Network Plan & Inspector");
            planTab.Controls.Add(planSplit);

            var conduitTab = new TabPage("Conduits"); conduitTab.Controls.Add(conduits);
            var structureTab = new TabPage("Structures"); structureTab.Controls.Add(structures);
            var engineeringTab = new TabPage("Engineering status"); engineeringTab.Controls.Add(Output);
            workspaceTabs=tabs;engineeringStatusTab=engineeringTab;
            var designTab = new TabPage("Design Workspace"); designTab.Controls.Add(DesignWorkspace);
            var stormTab = new TabPage("StormFlow Design Basis"); stormTab.Controls.Add(StormFlowWorkspace);
            var profileTab = new TabPage("StormFlow Profile & Audit"); profileTab.Controls.Add(HydraulicProfileViewer);
            var reportsTab = new TabPage("Engineering Reports"); reportsTab.Controls.Add(ReportsWorkspace);
            var nativeResultsTab = new TabPage("P16 Native Results"); nativeResultsTab.Controls.Add(NativeResultsWorkspace);
            var verifiedResultsTab = new TabPage("Verified Hydraulic Results"); verifiedResultsTab.Controls.Add(VerifiedHydraulicResultsWorkspace);
            var hydraulicCalculationTab = new TabPage("Calculate Existing Model"); hydraulicCalculationTab.Controls.Add(HydraulicCalculationWorkspace);
            tabs.TabPages.AddRange(new[] { planTab, conduitTab, structureTab, designTab, stormTab, profileTab, reportsTab, hydraulicCalculationTab, nativeResultsTab, verifiedResultsTab, engineeringTab });
            metadata.Height = 105; details.Height = 170;
            Controls.Add(tabs); Controls.Add(details);
            Controls.Add(new Label { Text = NetworkExplorerProjection.TopologyNotice, Dock = DockStyle.Top, Height = 25 });
            Controls.Add(status); Controls.Add(metadata); Controls.Add(engineeringActions); Controls.Add(projectActions);
            SaveProject.Enabled = SelectModel.Enabled = Confirm.Enabled = Execute.Enabled = OpenEvidence.Enabled = false;
            Output.Text = "Open model → prepare/review Design Workspace → P13/P14 and select → preview applied design → confirm → execute on a controlled copy. Optional JSON import remains available.\r\nMode: " + mode;
            metadata.Text = "Create or open a SewerPilot project. Save Project stores project.json and its audit; it never saves a Bentley model.";
            status.Text = "Ready — no project open.";

            // Bidirectional Selection Synchronization
            PlanCanvas.ElementSelected += (s, e) =>
            {
                if (e.ElementKind == PlanCanvasElementKind.Conduit)
                {
                    ElementInspector.InspectElement(PlanCanvasElementKind.Conduit, e.RawItem);
                    SelectGridRow(conduits, e.ElementId.ToString());
                    HydraulicProfileViewer.SelectConduit(e.Label);
                }
                else if (e.ElementKind == PlanCanvasElementKind.Structure)
                {
                    ElementInspector.InspectElement(PlanCanvasElementKind.Structure, e.RawItem);
                    SelectGridRow(structures, e.ElementId.ToString());
                }
                else
                {
                    ElementInspector.ClearInspection();
                }
            };

            ElementInspector.TraceUpstreamRequested += (s, id) => PlanCanvas.TraceUpstream(id, ElementInspector.CurrentKind == PlanCanvasElementKind.Conduit);
            ElementInspector.TraceDownstreamRequested += (s, id) => PlanCanvas.TraceDownstream(id, ElementInspector.CurrentKind == PlanCanvasElementKind.Conduit);
            ElementInspector.ClearTraceRequested += (s, e) => PlanCanvas.ClearTrace();
            ElementInspector.ViewInProfileRequested += (s, lbl) =>
            {
                HydraulicProfileViewer.SelectConduit(lbl);
                tabs.SelectedTab = profileTab;
            };

            HydraulicProfileViewer.ConduitSelected += (s, cid) =>
            {
                if (int.TryParse(cid, out int id))
                {
                    PlanCanvas.SelectConduit(id);
                }
                else
                {
                    var conduit = PlanCanvas.Conduits.Values.FirstOrDefault(c => string.Equals(c.Label, cid, StringComparison.OrdinalIgnoreCase));
                    if (conduit != null) PlanCanvas.SelectConduit(conduit.Id);
                }
            };

            conduits.SelectionChanged += (s, e) =>
            {
                DescribeSelection(conduits);
                if (conduits.CurrentRow?.Cells[0]?.Value != null && int.TryParse(conduits.CurrentRow.Cells[0].Value.ToString(), out int cid))
                {
                    if (PlanCanvas.SelectedKind != PlanCanvasElementKind.Conduit || PlanCanvas.SelectedId != cid)
                        PlanCanvas.SelectConduit(cid);
                }
            };

            structures.SelectionChanged += (s, e) =>
            {
                DescribeSelection(structures);
                if (structures.CurrentRow?.Cells[0]?.Value != null && int.TryParse(structures.CurrentRow.Cells[0].Value.ToString(), out int sid))
                {
                    if (PlanCanvas.SelectedKind != PlanCanvasElementKind.Structure || PlanCanvas.SelectedId != sid)
                        PlanCanvas.SelectStructure(sid);
                }
            };

            tabs.SelectedIndexChanged += (s, e) => {
                details.Visible = tabs.SelectedTab != planTab && tabs.SelectedTab != designTab && tabs.SelectedTab != stormTab && tabs.SelectedTab != profileTab && tabs.SelectedTab != reportsTab && tabs.SelectedTab != nativeResultsTab && tabs.SelectedTab != verifiedResultsTab;
                if (tabs.SelectedTab == conduitTab) DescribeSelection(conduits);
                else if (tabs.SelectedTab == structureTab) DescribeSelection(structures);
                else details.Text = "Engineering workflow retains its existing guarded inputs, lineage and confirmation contracts.";
            };
            FormClosing += (s, e) => { if (busy) { e.Cancel = true; status.Text = "The active operation is supervised and bounded. Wait for its cleanup before closing."; } };
        }

        private static void SelectGridRow(DataGridView grid, string id)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Cells.Count > 0 && row.Cells[0].Value != null &&
                    string.Equals(row.Cells[0].Value.ToString(), id, StringComparison.OrdinalIgnoreCase))
                {
                    row.Selected = true;
                    grid.CurrentCell = row.Cells[0];
                    break;
                }
            }
        }
        private static Button ActionButton(string name, int width) => new Button { Text = name, Name = name.Replace(" ", ""), AccessibleName = name, Width = width, Height = 28 };
        private static TextBox ReadOnlyText(string name, DockStyle dock) => new TextBox { Name = name.Replace(" ", ""), AccessibleName = name,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = dock };
        private static DataGridView Grid(string name, string[] headers)
        {
            var grid = new DataGridView { Name = name.Replace(" ", ""), AccessibleName = name, Dock = DockStyle.Fill, ReadOnly = true,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToOrderColumns = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false, AutoGenerateColumns = false };
            foreach (string header in headers) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = header.Replace(" ", ""), HeaderText = header, SortMode = DataGridViewColumnSortMode.NotSortable });
            return grid;
        }
        private void DescribeSelection(DataGridView grid)
        {
            var row = grid.CurrentRow?.Tag as NetworkExplorerRow;
            if (row != null) details.Text = row.Details;
        }
        internal void ShowProject(ProjectWorkspace workspace)
        {
            var state = workspace.State;
            metadata.Text = "Project: " + state.ProjectName + " | ID: " + state.ProjectId + " | Revision: " + state.StateRevision + (workspace.Dirty ? " | Unsaved metadata" : " | Saved") +
                "\r\nProject folder: " + workspace.DirectoryPath + "\r\nModel: " + (state.Model?.BaselineLocationMetadata ?? "Not selected") +
                "\r\nModel fingerprint: " + (state.Model?.ProtectedBaselineFingerprint ?? "Unavailable") +
                "\r\nScenario / Physical alternative: " + (state.ScenarioAlternativeState?.SelectedScenarioId?.ToString() ?? "Unavailable") + " / " +
                (state.ScenarioAlternativeState?.SelectedPhysicalAlternativeId?.ToString() ?? "Unavailable");
            SaveProject.Enabled = SelectModel.Enabled = !busy;
            if (!string.Equals(boundStormDirectory, workspace.DirectoryPath, StringComparison.OrdinalIgnoreCase))
            {
                boundStormDirectory = workspace.DirectoryPath;
                StormFlowWorkspace.Bind(workspace.DirectoryPath);
            }
        }
        internal void SetNativeReady(bool value)
        {
            nativeReady = value;
            NativeResultsWorkspace.CalculateNative.Enabled = value && !busy;
        }
        // Independent of the fixed-scenario P16 gate: any model the application could open can be
        // calculated, because the general path discovers the scenario from the model itself.
        internal void SetCalculationReady(bool value)
        {
            calculationReady = value;
            HydraulicCalculationWorkspace.SetModelAvailable(value);
            HydraulicCalculationWorkspace.CalculateExistingModel.Enabled = value && !busy;
        }
        internal void SetBusy(bool value)
        {
            busy = value;
            NativeResultsWorkspace.CalculateNative.Enabled = nativeReady && !busy;
            HydraulicCalculationWorkspace.CalculateExistingModel.Enabled = calculationReady && !busy;
            DesignWorkspace.SetBusy(value);
            StormFlowWorkspace.SetBusy(value);
            NewProject.Enabled = OpenProject.Enabled = SaveProject.Enabled = SelectModel.Enabled = LoadInputs.Enabled = Generate.Enabled = !value;
            Calculate.Enabled=!value;
            if (value) Confirm.Enabled = Execute.Enabled = false;
        }
        internal void SetStatus(string text) { status.Text = text; }
        internal void ShowEngineeringText(string text) { Output.Text=text;workspaceTabs.SelectedTab=engineeringStatusTab; }
        internal bool ShowVerifiedHydraulicOutcome(SingleOperatorOutcomeView view)
        {
            bool verified = VerifiedHydraulicResultsWorkspace.ShowOutcome(view);
            if (verified)
                workspaceTabs.SelectedTab = workspaceTabs.TabPages.Cast<TabPage>().Single(t => t.Text == "Verified Hydraulic Results");
            return verified;
        }
        internal void ClearNetwork()
        {
            conduits.Rows.Clear();
            structures.Rows.Clear();
            details.Clear();
            PlanCanvas.ClearNetwork();
            ElementInspector.ClearInspection();
        }
        internal void ShowNetwork(NetworkReadResult network)
        {
            ClearNetwork();
            foreach (var c in network.Conduits.OrderBy(c => c.Id)) Add(conduits, NetworkExplorerProjection.Conduit(c));
            foreach (var s in network.Structures.OrderBy(s => s.Id)) Add(structures, NetworkExplorerProjection.Structure(s, network));
            PlanCanvas.LoadNetwork(network);
            if (conduits.Rows.Count > 0) conduits.CurrentCell = conduits.Rows[0].Cells[0];
            DescribeSelection(conduits);
            status.Text = "Read succeeded — " + network.Conduits.Count + " conduits / " + network.Structures.Count + " structures. Tables are read-only; source model protected.";
        }
        private static void Add(DataGridView grid, NetworkExplorerRow value)
        { int index = grid.Rows.Add(value.Cells.Cast<object>().ToArray()); grid.Rows[index].Tag = value; }
        internal static bool GetNewProject(Form owner, out string name, out string directory)
        {
            using (var dialog = new Form { Text = "New SewerPilot Project", Width = 640, Height = 210, StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            using (var projectName = new TextBox { Name = "ProjectName", AccessibleName = "Project name", Left = 145, Top = 22, Width = 450 })
            using (var folder = new TextBox { Name = "ProjectDirectory", AccessibleName = "Project directory", Left = 145, Top = 65, Width = 450 })
            {
                var create = new Button { Text = "Create", Name = "CreateProject", AccessibleName = "Create project", Left = 415, Top = 112, Width = 85 };
                var cancel = new Button { Text = "Cancel", Left = 510, Top = 112, Width = 85, DialogResult = DialogResult.Cancel };
                create.Click += (s, e) => { if (string.IsNullOrWhiteSpace(projectName.Text) || string.IsNullOrWhiteSpace(folder.Text)) {
                    MessageBox.Show(dialog, "Supply a project name and a new or empty directory.", "Project information required"); return; }
                    dialog.DialogResult = DialogResult.OK; };
                dialog.Controls.AddRange(new Control[] { new Label { Text = "Project name", Left = 15, Top = 25, Width = 125 }, projectName,
                    new Label { Text = "Project directory", Left = 15, Top = 68, Width = 125 }, folder, create, cancel });
                dialog.AcceptButton = create; dialog.CancelButton = cancel;
                bool ok = dialog.ShowDialog(owner) == DialogResult.OK;
                name = projectName.Text; directory = folder.Text; return ok;
            }
        }
    }
}
