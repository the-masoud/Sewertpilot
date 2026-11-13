using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        // Isolated TEST_ONLY panel session. No production startup, model reader, runtime, approval or execution service.
        private static int RunP14AutomaticProposalGuiHost()
        {
            int exitCode = 0;
            var thread = new Thread(() =>
            {
                string evidence = Environment.GetEnvironmentVariable("SEWERPILOT_P14_GUI_EVIDENCE_DIR") ??
                    Path.Combine(Path.GetTempPath(), "SewerPilot-P14Auto02GuiR1-" + Guid.NewGuid().ToString("N"));
                try
                {
                    evidence = Path.GetFullPath(evidence);
                    var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!evidence.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("TEST_ONLY_TEMP_DIRECTORY_REQUIRED");
                    Directory.CreateDirectory(evidence);
                    Action<object> record = value =>
                    {
                        string json = GravityExecutionPlanBuilder.Json(value);
                        File.AppendAllText(Path.Combine(evidence, "host-observations.jsonl"), json + Environment.NewLine);
                        Console.WriteLine(json); Console.Out.Flush();
                    };
                    var adapter = typeof(EngineeringDesignPanel).Assembly;
                    string adjacent = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SewerPilot.OpenFlows.Adapter.dll");
                    if (!string.Equals(Path.GetFullPath(adapter.Location), Path.GetFullPath(adjacent), StringComparison.OrdinalIgnoreCase) ||
                        GuiHostFileHash(adapter.Location) != Environment.GetEnvironmentVariable("SEWERPILOT_P14_GUI_ADAPTER_HASH"))
                        throw new InvalidOperationException("CURRENT_TEST_BUILD_ADAPTER_IDENTITY_REQUIRED");
                    record(new { Stage = "DEPENDENCY_VERIFIED", Utc = DateTime.UtcNow.ToString("O"), Pid = Process.GetCurrentProcess().Id,
                        Executable = Process.GetCurrentProcess().MainModule.FileName, ExecutableHash = GuiHostFileHash(Process.GetCurrentProcess().MainModule.FileName),
                        Adapter = adapter.Location, AdapterIdentity = adapter.FullName, AdapterHash = GuiHostFileHash(adapter.Location) });

                    // Keep fixture storage short and independent of the evidence directory.
                    string fixtureRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SPG-" + Guid.NewGuid().ToString("N").Substring(0, 12)));
                    if (Directory.Exists(fixtureRoot) || File.Exists(fixtureRoot)) throw new InvalidOperationException("TEST_ONLY_FIXTURE_ROOT_ALREADY_EXISTS");
                    string stagingName = "ui02-" + new string('a', 64) + ".json." + new string('b', 32) + ".staging";
                    var stagingPaths = new[] { "A", "B" }.Select(state => new {
                        State = state, ProjectPath = Path.GetFullPath(Path.Combine(fixtureRoot, state, "p")),
                        StagingPath = Path.GetFullPath(Path.Combine(fixtureRoot, state, "p", "engineering", stagingName)) }).ToArray();
                    record(new { Stage = "STAGING_PATH_PREFLIGHT", FixtureRoot = fixtureRoot,
                        Paths = stagingPaths.Select(path => new { path.State, path.ProjectPath, path.StagingPath, Length = path.StagingPath.Length }).ToArray() });
                    if (stagingPaths.Any(path => path.StagingPath.Length >= 230)) throw new InvalidOperationException("TEMP_PATH_TOO_LONG");
                    Directory.CreateDirectory(fixtureRoot);
                    // Exactly two fresh preparations, validated before constructing or showing any controls.
                    var projectA = PrepareP14GuiProject(fixtureRoot, "A", record);
                    var projectB = PrepareP14GuiProject(fixtureRoot, "B", record);
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    using (var form = new Form { Name = "P14AutomaticProposalGuiHost", Text = "SewerPilot P14-AUTO-02 — TEST_ONLY isolated panel",
                        Width = 1180, Height = 820, StartPosition = FormStartPosition.CenterScreen })
                    using (var panel = new EngineeringDesignPanel())
                    using (var budget = new System.Windows.Forms.Timer { Interval = 1200000 })
                    {
                        var switchState = new Button { Name = "SwitchToStateB", AccessibleName = "Switch to independent TEST_ONLY State B",
                            Text = "Switch to State B", AutoSize = true };
                        var label = new Label { AutoSize = true, Text = "TEST_ONLY State A — synthetic placeholders; no Bentley, approval or execution" };
                        var banner = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
                        banner.Controls.Add(switchState); banner.Controls.Add(label);
                        form.Controls.Add(panel); form.Controls.Add(banner);
                        var preview = (Button)panel.Controls.Find("PreviewAutomaticProposal", true).Single();
                        var stage = (Button)panel.Controls.Find("StageAutomaticProposal", true).Single();
                        var assess = (Button)panel.Controls.Find("AssessP13", true).Single();
                        var select = (Button)panel.Controls.Find("SelectCandidate", true).Single();
                        var details = (TextBox)panel.Controls.Find("CandidateDetails", true).Single();
                        var feasibility = (TextBox)panel.Controls.Find("FeasibilityResult", true).Single();
                        var candidateList = (ListBox)panel.Controls.Find("CandidateList", true).Single();
                        string state = "A"; var project = projectA; int guiButtonActions = 0;
                        Action<string> observe = action =>
                        {
                            var design = panel.Design;
                            var candidate = design?.Candidates?.Candidates.SingleOrDefault();
                            string proposalFingerprint = details.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                                .FirstOrDefault(line => line.StartsWith("Proposal fingerprint: ", StringComparison.Ordinal));
                            record(new { Stage = "DOMAIN_OBSERVATION", Action = action, State = state, Utc = DateTime.UtcNow.ToString("O"),
                                GuiButtonActions = guiButtonActions, ProjectId = project.State.ProjectId, ProjectPath = project.DirectoryPath,
                                ModelFingerprint = design?.Inputs.ModelFingerprint, NetworkFingerprint = design?.NetworkFingerprint,
                                ConstraintFingerprint = design?.Constraints?.VerticalConstraintFingerprint, SourceChanged = design?.SourceChanged,
                                HasLiveNetwork = design?.HasLiveNetwork, ReviewState = design?.Basis?.CommonDesignBasis.ReviewState.ToString(),
                                FeasibilityStatus = design?.Feasibility?.Status.ToString(), CandidatesCount = design?.Candidates?.Candidates.Count ?? 0,
                                CandidateId = candidate?.CandidateId, CandidateFingerprint = candidate?.CandidateFingerprint,
                                CandidateNetworkFingerprint = candidate?.SourceNetworkFingerprint, CandidateConstraintFingerprint = candidate?.VerticalConstraintFingerprint,
                                CandidateJson = design?.Inputs.CandidateJson,
                                CandidateJsonMatchesStaged = candidate != null && design.Inputs.CandidateJson == GravityExecutionPlanBuilder.Json(candidate),
                                ApprovalPresent = design?.Inputs.Approval != null, StageEnabled = stage.Enabled,
                                CandidateListCount = candidateList.Items.Count, HighlightedCandidateId = (candidateList.SelectedItem as GravityDesignCandidate)?.CandidateId,
                                ProposalFingerprintLine = proposalFingerprint, CandidateDetails = details.Text, FeasibilityDetails = feasibility.Text,
                                ProjectDirty = project.Dirty,
                                EngineeringState = design == null ? null : GravityExecutionPlanBuilder.Json(new {
                                    design.Inputs, design.Recipes, design.RecipeConstraintFingerprint, design.Feasibility, design.Candidates, design.Dirty, design.SourceChanged }) });
                        };
                        // Passive observers run after the existing handlers. They never trigger an action or mutate domain state.
                        foreach (var button in new[] { assess, preview, stage, select })
                        {
                            var observed = button;
                            observed.Click += (sender, args) => { guiButtonActions++; observe(observed.Name); };
                        }
                        switchState.Click += (sender, args) =>
                        {
                            if (state != "A") return;
                            guiButtonActions++; state = "B"; project = projectB; panel.Bind(projectB);
                            label.Text = "TEST_ONLY State B — independent synthetic project; one reassessment invalidates preview";
                            switchState.Enabled = false; observe("SwitchToStateB");
                        };
                        panel.Bind(projectA);
                        form.Shown += (sender, args) => { observe("HOST_SHOWN"); budget.Start(); };
                        budget.Tick += (sender, args) => { budget.Stop(); observe("GUI_TIME_LIMIT"); form.Close(); };
                        form.FormClosed += (sender, args) => { budget.Stop(); observe("HOST_CLOSED"); };
                        record(new { Stage = "PANEL_CONTROLS_VERIFIED", PreviewControl = preview.Name, StageControl = stage.Name });
                        Application.Run(form);
                    }
                }
                catch (Exception error)
                {
                    exitCode = 1;
                    var diagnostic = GravityExecutionPlanBuilder.Json(new { Stage = "HOST_BLOCKED", Utc = DateTime.UtcNow.ToString("O"), Exception = error.ToString() });
                    Console.Error.WriteLine(diagnostic); Console.Error.Flush();
                    if (Directory.Exists(evidence)) File.WriteAllText(Path.Combine(evidence, "host-blocked.json"), diagnostic);
                }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); return exitCode;
        }

        private static string GuiHostFileHash(string path)
        {
            using (var hash = SHA256.Create()) using (var file = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "");
        }

        private static ProjectWorkspace PrepareP14GuiProject(string fixtureRoot, string state, Action<object> record)
        {
            string root = Path.Combine(fixtureRoot, state); Directory.CreateDirectory(root);
            string placeholder = Path.Combine(root, "TEST_ONLY_NOT_A_BENTLEY_MODEL.stsw");
            File.WriteAllText(placeholder, "TEST_ONLY_NOT_A_BENTLEY_MODEL State=" + state + " Identity=" + Guid.NewGuid().ToString("N"));
            var raw = CreateValidReadResultWithEndpoints(); raw.ScenarioContext.ActiveScenarioLabel = "TEST_ONLY_AUTO";
            foreach (var conduit in raw.Conduits)
            {
                conduit.Length = 100; conduit.LengthUnit = CreateFeetValue(0).Unit;
                conduit.Rise = 1.5; conduit.RiseUnit = CreateFeetValue(0).Unit;
            }
            foreach (var node in raw.Structures.Where(node => node.GroundElevation.HasValue))
            { node.GroundElevationUnit = CreateFeetValue(0).Unit; node.PropertyClassifications["GroundElevation"] = "PROVEN"; }
            var baseline = ProtectedBaseline.Register(placeholder);
            var project = ProjectWorkspace.Create(Path.Combine(root, "p"), "TEST_ONLY_P14_GUI_" + state);
            project.AttachModel(baseline, raw); project.SaveProjectState();
            var design = EngineeringDesignStorage.OpenWorkspace(project);
            design.NewDraft("TEST_ONLY_AUTO03_1"); design.SetIdentity("TEST_ONLY_AUTO03_BASIS", "TEST_ONLY_AUTO03_1");
            design.PrepareAutomaticDesign(GravityAutomaticPreparationService.PolicyId, GravityAutomaticPreparationService.PolicyVersion,
                design.CurrentAutomaticProposalContext(GravityAutomaticPreparationService.PolicyId, GravityAutomaticPreparationService.PolicyVersion));
            foreach (var conduit in project.Network.Conduits)
            {
                design.PutConstraint(new VerticalSlopeConstraint(conduit.Id, .01, .04, Ui02Provenance("TEST_ONLY.slope." + conduit.Id)));
                foreach (var end in new[] { ConduitEndKind.Start, ConduitEndKind.Stop })
                {
                    var endpoint = design.End(conduit.Id, end);
                    design.PutConstraint(new VerticalCoverConstraint(endpoint, CreateFeetValue(100), CreateFeetValue(2),
                        Ui02Provenance("TEST_ONLY.cover." + conduit.Id + "." + end)));
                }
            }
            design.PutConstraint(new OutfallVerticalConstraint(103, Ui02Provenance("TEST_ONLY.outfall"), CreateFeetValue(90)));
            EngineeringDesignStorage.PersistEngineeringState(project, design);
            var restored = EngineeringDesignStorage.OpenWorkspace(project);
            if (restored.SourceChanged || !restored.HasLiveNetwork || restored.Basis == null || restored.Constraints == null ||
                restored.Inputs.CandidateJson != null || restored.Inputs.Approval != null || restored.Candidates != null || restored.Feasibility != null ||
                restored.Recipes.Count != 0 || restored.Inputs.GenerationRequestJson != null ||
                restored.Basis.CommonDesignBasis.ReviewState != DesignBasisReviewState.Draft)
                throw new InvalidOperationException("TEST_ONLY_FIXTURE_BINDING_PRECONDITIONS_FAILED: " + state);
            record(new { Stage = "FIXTURE_VALIDATED", State = state, ProjectId = project.State.ProjectId, ProjectPath = project.DirectoryPath,
                Placeholder = placeholder, PlaceholderHash = GuiHostFileHash(placeholder), BaselineFingerprint = baseline.Fingerprint.Value,
                NetworkFingerprint = restored.NetworkFingerprint, ConstraintFingerprint = restored.Constraints.VerticalConstraintFingerprint,
                SourceChanged = restored.SourceChanged, HasLiveNetwork = restored.HasLiveNetwork, ReviewState = restored.Basis.CommonDesignBasis.ReviewState.ToString(),
                FeasibilityPresent = restored.Feasibility != null, CandidatesPresent = restored.Candidates != null,
                CandidateJsonPresent = restored.Inputs.CandidateJson != null, ApprovalPresent = restored.Inputs.Approval != null });
            return project;
        }
    }
}
