using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static void SaveTestOnlyBindingBasis(string directory)
        {
            ProjectStore.CreateProject(directory, "TEST_ONLY_STORM_BIND");
            var workspace = StormFlowDesignBasisWorkspace.Open(directory);
            workspace.NewDraft("TEST_ONLY_BIND_1");
            foreach (var section in new[] { StormFlowSectionIdentifier.Project, StormFlowSectionIdentifier.Rainfall })
            {
                string key = "TEST_ONLY_BIND_" + section;
                workspace.PutCriterion(section, new StormDesignCriterion
                {
                    Key = key, Section = section, Requirement = StormCriterionRequirement.Required,
                    Status = StormCriterionStatus.Defined, Value = StandardValue.FromQuantity(10.0, "yr"),
                    Provenance = new StandardProvenance
                    {
                        SourceLayer = StandardSourceLayer.ProjectOverride, ParameterKey = key,
                        DocumentReference = "TEST_ONLY_BIND_REGRESSION",
                        Notes = "UI binding fixture only; no engineering criterion or approval."
                    }
                });
            }
            workspace.Transition(DesignBasisReviewState.ReadyForReview, "TEST_ONLY binding regression; no approval.");
            workspace.PersistBasis();
        }

        // A fresh panel must render persisted criteria before any section-selection event occurs.
        private static void P18_Bind_PopulatesSavedSelectedSection()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SewerPilotStormBind", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                SaveTestOnlyBindingBasis(directory);
                var stored = ProjectStore.LoadProject(directory);
                string content = Path.Combine(directory, "stormflow", stored.StormFlowDesignBasisReference.ReferenceIdentifier + ".json");
                string[] paths = { Path.Combine(directory, "project.json"), Path.Combine(directory, "audit.jsonl"), content };
                var before = paths.Select(File.ReadAllBytes).ToArray();
                OnSta(() =>
                {
                    using (var panel = new StormFlowDesignPanel())
                    {
                        panel.Bind(directory);
                        var list = panel.Controls.Find("StormCriterionList", true).OfType<ListBox>().Single();
                        var selector = panel.Controls.Find("StormDesignBasisSection", true).OfType<ComboBox>().Single();
                        Check((string)selector.SelectedItem == "Project", "Fresh binding retains the default Project section.");
                        Check(list.Items.Count == 1 && ((StormDesignCriterion)list.Items[0]).Key == "TEST_ONLY_BIND_Project",
                            "Saved Project criterion is immediately visible without switching sections.");
                        var value = ((StormDesignCriterion)list.Items[0]).Value.QuantityValue;
                        Check(value.Value == 10.0 && value.Unit == "yr", "Displayed saved criterion retains the exact typed value and unit.");
                        Check(panel.Basis.CommonDesignBasis.ReviewState == DesignBasisReviewState.ReadyForReview && !panel.Dirty,
                            "Binding preserves persisted review state and does not dirty the project.");
                        Check(panel.EditingCriterion == null && list.SelectedIndex == -1, "Binding does not implicitly start an edit.");
                        Check(!panel.Controls.Find("NewStormCriterion", true).OfType<Button>().Single().Enabled &&
                            !panel.Controls.Find("ApplyStormDesignIdentity", true).OfType<Button>().Single().Enabled,
                            "Reviewed content remains locked after initial binding.");
                        Check(panel.Controls.Find("SaveStormEngineering", true).OfType<Button>().Single().Enabled &&
                            panel.Controls.Find("ReopenStormEngineering", true).OfType<Button>().Single().Enabled,
                            "Saved reviewed content remains available to save and reopen.");
                        panel.SelectSection(StormFlowSectionIdentifier.Rainfall);
                        panel.Bind(directory);
                        Check((string)selector.SelectedItem == "Rainfall" && list.Items.Count == 1 &&
                            ((StormDesignCriterion)list.Items[0]).Key == "TEST_ONLY_BIND_Rainfall",
                            "Rebinding populates the current section without resetting the selection.");
                    }
                });
                for (int i = 0; i < paths.Length; i++)
                    Check(before[i].SequenceEqual(File.ReadAllBytes(paths[i])), "Panel binding preserves stored file bytes: " + Path.GetFileName(paths[i]));
            }
            finally { TryDeleteDirectory(directory); }
        }

        // Changing projects or rejecting a broken project must never leave the previous criteria visible.
        private static void P18_Bind_ClearsPriorProjectOnEmptyOrFailedBinding()
        {
            string root = Path.Combine(Path.GetTempPath(), "SewerPilotStormBind", Guid.NewGuid().ToString("N"));
            string saved = Path.Combine(root, "saved"), empty = Path.Combine(root, "empty"), malformed = Path.Combine(root, "malformed");
            Directory.CreateDirectory(saved); Directory.CreateDirectory(empty); Directory.CreateDirectory(malformed);
            try
            {
                SaveTestOnlyBindingBasis(saved);
                ProjectStore.CreateProject(empty, "TEST_ONLY_EMPTY_BIND");
                string malformedPath = Path.Combine(malformed, "project.json");
                File.WriteAllText(malformedPath, "{TEST_ONLY malformed");
                OnSta(() =>
                {
                    using (var panel = new StormFlowDesignPanel())
                    {
                        var list = panel.Controls.Find("StormCriterionList", true).OfType<ListBox>().Single();
                        var grid = panel.Controls.Find("StormCriterionEditor", true).OfType<PropertyGrid>().Single();
                        panel.Bind(saved);
                        panel.EditCriterion("TEST_ONLY_BIND_Project");
                        Check(list.Items.Count == 1 && grid.SelectedObject != null, "Fixture has a populated prior project and editor.");
                        panel.Bind(empty);
                        Check(panel.Basis == null && list.Items.Count == 0 && grid.SelectedObject == null,
                            "A project without StormFlow content clears the previous list and editor.");
                        panel.Bind(saved);
                        panel.Bind(malformed);
                        Check(panel.Basis == null && list.Items.Count == 0 && panel.EditingCriterion == null,
                            "Failed project loading clears prior criteria rather than showing stale content.");
                        Check(panel.ValidationText.StartsWith("BLOCKED:", StringComparison.Ordinal), "Load failure remains actionable.");
                        Check(!panel.Controls.Find("SaveStormEngineering", true).OfType<Button>().Single().Enabled,
                            "Failed binding leaves save disabled.");
                        panel.Bind(saved);
                        panel.Bind(null);
                        Check(panel.Workspace == null && list.Items.Count == 0 && grid.SelectedObject == null,
                            "Unbinding clears the previous list and editor.");
                    }
                });
                Check(File.ReadAllText(malformedPath) == "{TEST_ONLY malformed", "Rejected input is not modified by binding.");
            }
            finally { TryDeleteDirectory(root); }
        }
    }
}
