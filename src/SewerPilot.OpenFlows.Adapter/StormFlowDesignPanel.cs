using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    // P18 minimal production UI integration for the independent StormFlow Design Basis.
    // Create, edit, validate, review, save and reopen are delegated to StormFlowDesignBasisWorkspace;
    // no engineering rule is re-implemented in an event handler. Offline only: no Bentley model access,
    // no solver, no authorization and no execution.
    internal sealed class StormFlowDesignPanel : UserControl
    {
        internal readonly TabControl Sections = new TabControl { Name = "StormFlowSections", AccessibleName = "StormFlow design basis sections", Dock = DockStyle.Fill };
        private readonly TextBox basisId = Input("StormDesignBasisId", 190);
        private readonly TextBox basisVersion = Input("StormDesignBasisVersion", 85);
        private readonly TextBox newVersion = Input("StormNewDraftVersion", 85);
        private readonly TextBox note = Input("StormReviewNote", 250);
        private readonly Label identity = new Label { Name = "StormDesignReviewIdentity", AutoSize = true };
        private readonly ComboBox section = Choice("StormDesignBasisSection", Enum.GetNames(typeof(StormFlowSectionIdentifier)), 170);
        private readonly ComboBox valueKind = Choice("StormCriterionValueKind", Enum.GetNames(typeof(StandardValueKind)), 100);
        private readonly ListBox criteria = new ListBox { Name = "StormCriterionList", AccessibleName = "StormFlow criteria", Dock = DockStyle.Fill, DisplayMember = "Key" };
        private readonly PropertyGrid criterion = new PropertyGrid { Name = "StormCriterionEditor", AccessibleName = "Typed StormFlow criterion editor", Dock = DockStyle.Fill, ToolbarVisible = false };
        private readonly TextBox validation = Output("StormDesignValidation");
        private readonly ComboBox transition = Choice("StormReviewTransition", Enum.GetNames(typeof(DesignBasisReviewState)), 140);
        private readonly Button applyCriterion = Button("ApplyStormCriterion", "Apply criterion");
        private readonly Button applyIdentity = Button("ApplyStormDesignIdentity", "Apply identity/version");
        private readonly Button create = Button("CreateStormDraft", "Create new draft/version");
        private readonly Button check = Button("ValidateStormDesignBasis", "Validate Design Basis");
        private readonly Button applyTransition = Button("ApplyStormReviewTransition", "Apply review transition");
        private readonly Button save = Button("SaveStormEngineering", "Save engineering");
        private readonly Button reload = Button("ReopenStormEngineering", "Reopen engineering state");
        private readonly Button add = Button("NewStormCriterion", "New criterion");
        private readonly Button makeValue = Button("CreateStormTypedValue", "Create typed value");
        private readonly Button discard = Button("DiscardStormCriterionEditor", "Discard editor changes");

        private StormFlowDesignBasisWorkspace workspace;
        private StormDesignCriterion editing;
        private string editingKey;
        private bool presenting;
        private bool pendingCriterion;

        internal StormFlowDesignBasisWorkspace Workspace => workspace;
        internal StormFlowDesignBasis Basis => workspace?.Basis;
        internal bool Dirty => workspace?.Dirty == true;
        internal StormDesignCriterion EditingCriterion => editing;
        internal string ValidationText => validation.Text;
        // The project state the application's own most recent StormFlow save accepted, or null when no save
        // has been accepted since the panel was bound (a reopen re-reads the store and clears it).
        internal ProjectState LastSavedState { get; private set; }
        internal event Action Changed;
        internal event Action Saved;

        internal StormFlowDesignPanel()
        {
            Name = "StormFlowDesignWorkspace";
            AccessibleName = "StormFlow Design Basis";
            Dock = DockStyle.Fill;
            foreach (var type in new[] { typeof(StandardValue), typeof(StandardQuantity), typeof(StandardProvenance) })
                TypeDescriptor.AddAttributes(type, new TypeConverterAttribute(typeof(ExpandableObjectConverter)));

            var page = new TabPage("StormFlow Design Basis") { Name = "StormFlowDesignBasis" };
            var left = new Panel { Dock = DockStyle.Left, Width = 190 };
            left.Controls.Add(criteria);
            left.Controls.Add(Bar(section));
            var editor = new Panel { Dock = DockStyle.Fill };
            editor.Controls.Add(criterion);
            editor.Controls.Add(Bar(add, valueKind, makeValue, applyCriterion));
            editor.Controls.Add(Bar(discard));
            var result = new Panel { Dock = DockStyle.Bottom, Height = 115 };
            result.Controls.Add(validation);
            page.Controls.Add(editor);
            page.Controls.Add(left);
            page.Controls.Add(result);
            page.Controls.Add(Bar(check, transition, note, applyTransition));
            page.Controls.Add(Bar(save, reload, identity));
            page.Controls.Add(Bar(new Label { Text = "ID", AutoSize = true }, basisId, new Label { Text = "Version", AutoSize = true }, basisVersion, applyIdentity));
            page.Controls.Add(Bar(new Label { Text = "New revision version", AutoSize = true }, newVersion, create));
            Sections.TabPages.Add(page);
            Controls.Add(Sections);
            Controls.Add(Bar(new Label { Text = "StormFlow is independent of GravityFlow. Rainfall, hydrology method and storm criteria are engineer-supplied; the software invents no value. Offline: no model access, solver or execution.", AutoSize = true }));

            add.Click += (s, e) => Guard(() => BeginNewCriterion(SelectedSection()));
            makeValue.Click += (s, e) => Guard(() => CreateTypedValue(SelectedValueKind()));
            applyCriterion.Click += (s, e) => Guard(ApplyCriterion);
            applyIdentity.Click += (s, e) => Guard(() => ApplyIdentity(basisId.Text, basisVersion.Text));
            create.Click += (s, e) => Guard(() => CreateDraft(newVersion.Text));
            check.Click += (s, e) => Guard(() => { validation.Text = ValidateBasis(); });
            applyTransition.Click += (s, e) => Guard(() => ApplyTransition(SelectedTransitionState(), note.Text));
            save.Click += (s, e) => Guard(Persist);
            reload.Click += (s, e) => Guard(Reopen);
            discard.Click += (s, e) =>
            {
                pendingCriterion = false; editing = null; editingKey = null; criterion.SelectedObject = null;
                validation.Text = "Unapplied editor changes discarded; saved engineering content retained.";
                RefreshView();
            };
            section.SelectedIndexChanged += (s, e) => { if (!presenting && !pendingCriterion) ShowCriteria(); };
            criteria.SelectedIndexChanged += (s, e) => { if (!presenting && !pendingCriterion) EditSelected(); };
            criterion.PropertyValueChanged += (s, e) => { pendingCriterion = true; RefreshView(); Changed?.Invoke(); };
            RefreshView();
        }

        // Binds the panel to an existing project directory. A load failure is reported as actionable
        // state and never mutates anything.
        internal void Bind(string projectDirectory)
        {
            workspace = null;
            editing = null; editingKey = null; pendingCriterion = false;
            LastSavedState = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(projectDirectory))
                    workspace = StormFlowDesignBasisWorkspace.Open(projectDirectory);
                validation.Text = workspace?.Basis == null
                    ? "No StormFlow Design Basis in this project yet. Create a new draft; no criterion is assumed."
                    : "Saved StormFlow Design Basis reopened; content identity verified.";
            }
            catch (Exception e)
            {
                validation.Text = "BLOCKED: " + e.GetBaseException().Message;
            }
            // Populate the selected section immediately, including clearing a previous project's list
            // when the new binding has no basis or could not be loaded.
            ShowCriteria();
            RefreshView();
        }

        internal void SetBusy(bool value)
        {
            Enabled = !value;
        }

        // ---- Operations (also the non-interactive test seam; all rules live in the workspace) ----

        internal void CreateDraft(string version)
        {
            RequireWorkspace();
            workspace.NewDraft(version);
            validation.Text = "StormFlow draft created. Rainfall, hydrology method and storm criteria remain unresolved until explicitly supplied.";
            ShowCriteria();
        }

        internal void ApplyIdentity(string id, string version)
        {
            RequireWorkspace();
            workspace.SetIdentity(id, version);
            validation.Text = "StormFlow Design Basis identity applied.";
        }

        internal void BeginNewCriterion(StormFlowSectionIdentifier sectionId)
        {
            RequireWorkspace();
            RequireDraft();
            Require(!pendingCriterion, "APPLY_CURRENT_DRAFT_EDITOR_FIRST");
            editing = new StormDesignCriterion { Section = sectionId, Provenance = new StandardProvenance() };
            editingKey = null;
            criterion.SelectedObject = editing;
            pendingCriterion = true;
            Changed?.Invoke();
        }

        internal void CreateTypedValue(StandardValueKind kind)
        {
            if (editing == null) return;
            editing.Value = new StandardValue { Kind = kind };
            // An editable but unresolved quantity. The numeric and unit fields start explicitly unresolved
            // (non-finite value, empty unit) so the editor can supply them and no engineering value is
            // invented on the operator's behalf. The validating StandardQuantity(double,string) constructor
            // is deliberately not used here: it rejects a non-finite value immediately, which would make the
            // editor unreachable. Strict validation is unchanged and still applies to the value when it is
            // applied and stored (StandardValue.Validate, and the validated copy taken when the criterion is
            // put). This mirrors the accepted GravityFlow typed-value editor exactly.
            if (editing.Value.Kind == StandardValueKind.Quantity)
                editing.Value.QuantityValue = new StandardQuantity { Value = double.NaN, Unit = "" };
            criterion.Refresh();
            pendingCriterion = true;
            Changed?.Invoke();
        }

        internal void ApplyCriterion()
        {
            RequireWorkspace();
            Require(editing != null, "NEW_CRITERION_FIRST");
            workspace.PutCriterion(editing.Section, editing, editingKey);
            pendingCriterion = false;
            validation.Text = "StormFlow criterion applied to the draft. Validation and review remain separate.";
            ShowCriteria();
        }

        // Programmatic selection seam: edits an existing criterion in the current section.
        internal void EditCriterion(string key)
        {
            RequireWorkspace();
            Require(!pendingCriterion, "APPLY_CURRENT_DRAFT_EDITOR_FIRST");
            var match = workspace.Basis?.GetSection(SelectedSection())?.Criteria?
                .FirstOrDefault(c => c != null && string.Equals(c.Key, key, StringComparison.Ordinal));
            Require(match != null, "CRITERION_NOT_FOUND");
            BeginEdit(match);
            presenting = true;
            try { criteria.SelectedItem = match; } finally { presenting = false; }
        }

        // Programmatic selection seam: switches the displayed StormFlow section.
        internal void SelectSection(StormFlowSectionIdentifier sectionId)
        {
            Require(Enum.IsDefined(typeof(StormFlowSectionIdentifier), sectionId), "UNKNOWN_SECTION");
            section.SelectedItem = sectionId.ToString();
            ShowCriteria();
        }

        internal string ValidateBasis()
        {
            RequireWorkspace();
            var result = workspace.ValidateBasis();
            return result.IsValid
                ? "StormFlow Design Basis validation passed; review state and production readiness remain separate."
                : string.Join(Environment.NewLine, result.Issues);
        }

        internal void ApplyTransition(DesignBasisReviewState state, string noteText)
        {
            RequireWorkspace();
            Require(!string.IsNullOrWhiteSpace(noteText), "REVIEW_NOTE_REQUIRED");
            workspace.Transition(state, noteText);
            validation.Text = "StormFlow review transition recorded against the current operator identity. No independent reviewer or execution authority is implied.";
        }

        internal void Persist()
        {
            RequireWorkspace();
            // The accepted state is captured only after the save succeeded, so a failed save can never be
            // mistaken for an accepted revision by the synchronization below.
            LastSavedState = workspace.PersistBasis();
            validation.Text = "StormFlow Design Basis saved to the project store. No Bentley model was written or saved.";
            Saved?.Invoke();
        }

        internal void Reopen()
        {
            RequireWorkspace();
            Require(!workspace.Dirty, "SAVE_EDITS_BEFORE_REOPEN");
            LastSavedState = null;
            Bind(workspace.ProjectDirectory);
            validation.Text = "Saved StormFlow Design Basis reopened; content identity verified. No executable plan or authorization was restored.";
            Saved?.Invoke();
        }

        // Deterministic, culture-invariant summary usable without a foreground window.
        internal static string PresentBasis(StormFlowDesignBasis basis)
        {
            if (basis == null) return "No StormFlow Design Basis.";
            var text = new StringBuilder();
            text.Append("Module: ").Append(basis.Module ?? "(none)").Append(Environment.NewLine);
            text.Append("Review: ").Append(basis.CommonDesignBasis?.ReviewState.ToString() ?? "(none)").Append(Environment.NewLine);
            foreach (var s in basis.EnumerateSectionsInCanonicalOrder())
            {
                var completeness = s.GetCompleteness();
                text.Append(s.SectionId.ToString())
                    .Append(": Defined=").Append(completeness.DefinedCriterionCount.ToString(CultureInfo.InvariantCulture))
                    .Append(", Undefined=").Append(completeness.UndefinedCriterionCount.ToString(CultureInfo.InvariantCulture))
                    .Append(Environment.NewLine);
            }
            return text.ToString();
        }

        // ---- View projection ----

        private void ShowCriteria()
        {
            var basis = workspace?.Basis;
            var list = basis?.GetSection(SelectedSection())?.Criteria ?? new List<StormDesignCriterion>();
            presenting = true;
            try
            {
                criteria.Items.Clear();
                foreach (var entry in list.Where(c => c != null)) criteria.Items.Add(entry);
            }
            finally { presenting = false; }
            editing = null; editingKey = null; pendingCriterion = false;
            criterion.SelectedObject = null;
        }

        private void EditSelected()
        {
            var selected = criteria.SelectedItem as StormDesignCriterion;
            if (selected != null) BeginEdit(selected);
        }

        private void BeginEdit(StormDesignCriterion selected)
        {
            editing = selected.Clone();
            editingKey = selected.Key;
            criterion.SelectedObject = editing;
            pendingCriterion = false;
        }

        private void RefreshView()
        {
            var basis = workspace?.Basis;
            bool has = basis != null;
            bool draft = has && basis.CommonDesignBasis?.ReviewState == DesignBasisReviewState.Draft;
            applyIdentity.Enabled = draft;
            add.Enabled = draft;
            makeValue.Enabled = draft && editing != null;
            applyCriterion.Enabled = draft && pendingCriterion;
            check.Enabled = has;
            applyTransition.Enabled = has;
            save.Enabled = has;
            reload.Enabled = has && !(workspace?.Dirty ?? false);
            presenting = true;
            try
            {
                basisId.Text = basis?.CommonDesignBasis?.DesignBasisId ?? "";
                basisVersion.Text = basis?.CommonDesignBasis?.DesignBasisVersion ?? "";
            }
            finally { presenting = false; }
            identity.Text = has
                ? "Module: " + basis.Module + " | Project: " + workspace.ProjectId +
                  " | Review: " + basis.CommonDesignBasis.ReviewState +
                  " | Version: " + (basis.CommonDesignBasis.DesignBasisVersion ?? "(none)") +
                  (workspace.Dirty ? " | Unsaved changes" : " | Saved")
                : "No StormFlow Design Basis bound. Create or open a project, then create a draft.";
        }

        private void Guard(Action action)
        {
            try { action(); }
            catch (Exception e) { validation.Text = "BLOCKED: " + e.GetBaseException().Message; }
            finally { RefreshView(); }
        }

        private void RequireWorkspace()
        {
            Require(workspace != null, "OPEN_PROJECT_FIRST");
        }

        private void RequireDraft()
        {
            Require(workspace?.Basis?.CommonDesignBasis?.ReviewState == DesignBasisReviewState.Draft,
                "DRAFT_REQUIRED: reviewed or review-pending content is locked.");
        }

        private static void Require(bool value, string detail)
        {
            if (!value) throw new InvalidOperationException("STORMS_PANEL_" + detail);
        }

        private StormFlowSectionIdentifier SelectedSection() =>
            (StormFlowSectionIdentifier)Enum.Parse(typeof(StormFlowSectionIdentifier), Convert.ToString(section.SelectedItem, CultureInfo.InvariantCulture));

        private StandardValueKind SelectedValueKind() =>
            (StandardValueKind)Enum.Parse(typeof(StandardValueKind), Convert.ToString(valueKind.SelectedItem, CultureInfo.InvariantCulture));

        private DesignBasisReviewState SelectedTransitionState() =>
            (DesignBasisReviewState)Enum.Parse(typeof(DesignBasisReviewState), Convert.ToString(transition.SelectedItem, CultureInfo.InvariantCulture));

        private static TextBox Input(string name, int width) => new TextBox { Name = name, AccessibleName = name, Width = width };
        private static TextBox Output(string name) => new TextBox { Name = name, AccessibleName = name, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
        private static Button Button(string name, string text) => new Button { Name = name, AccessibleName = text, Text = text, AutoSize = true, Height = 28 };
        private static ComboBox Choice(string name, string[] values, int width)
        {
            var combo = new ComboBox { Name = name, AccessibleName = name, DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
            combo.Items.AddRange(values);
            if (values.Length > 0) combo.SelectedIndex = 0;
            return combo;
        }
        private static FlowLayoutPanel Bar(params Control[] controls)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 37, AutoScroll = true };
            panel.Controls.AddRange(controls);
            return panel;
        }
    }
}
