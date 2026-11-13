using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    // Small WinForms interaction in the existing acceptance/application entrypoint; no new UI framework/project.
    public static class SingleOperatorDesktopWorkflow
    {
        public static int Run() => Run(SingleOperatorStartupOptions.Default);
        public static int Run(SingleOperatorStartupOptions startup)
        {
            if (startup == null) throw new ArgumentNullException(nameof(startup));
            int code = 0;
            var thread = new Thread(() => { try { Show(startup); } catch (Exception e) { MessageBox.Show(e.Message, "SewerPilot — blocked"); code = 1; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); return code;
        }
        private static void Show(SingleOperatorStartupOptions startup)
        {
            var config = SewerPilotOperatingConfiguration.LoadApplicationConfiguration();
            var service = new SingleOperatorExecutionService(config);
            ProtectedBaseline baseline = null; NetworkReadResult source = null;
            var session = new SingleOperatorDesignSession(new SingleOperatorDesignOperations(service));
            string existingEvidence = null;
            ProjectWorkspace workspace = null;
            using (var form = new ProjectWorkspaceForm(config.Mode))
            {
                var open = form.SelectModel; var load = form.LoadInputs; var review = form.Generate;
                var confirm = form.Confirm; var execute = form.Execute; var output = form.Output;
                Action<Exception> failed = error => { session.Invalidate(); confirm.Enabled = execute.Enabled = false;
                    ApplicationDiagnostics.RecordFailure("Engineering workflow operation", error);
                    var code = error is ProjectStorageException storage ? storage.Classification + ": " : "";
                    form.SetStatus("BLOCKED: " + code + error.Message); output.Text = "BLOCKED: " + code + error.Message + "\r\n" + output.Text; };
                Action<Action> guarded = work => { try { work(); } catch (Exception error) { failed(error); } };
                Action invalidate = () => { session.Invalidate(); confirm.Enabled = false; execute.Enabled = false; form.VerifiedHydraulicResultsWorkspace.ClearForModelChange(); };
                form.Calculate.CheckedChanged += (s,e) => invalidate();
                form.DesignWorkspace.Changed += () => { invalidate(); if(workspace!=null)form.ShowProject(workspace); };
                form.DesignWorkspace.Saved += () => { invalidate(); form.ShowProject(workspace); };
                // A successful StormFlow save advances the shared project revision; the active workspace is
                // synchronized with that accepted state so the next GravityFlow save is not rejected as stale.
                form.StormFlowWorkspace.Saved += () => { invalidate(); SynchronizeStormFlowSave(workspace, form.StormFlowWorkspace); form.ShowProject(workspace); };
                Action saveState = () => { if(form.DesignWorkspace.Dirty)form.DesignWorkspace.PersistEngineeringState();else workspace.SaveProjectState(); form.ShowProject(workspace); form.SetStatus("Project state saved — Bentley model saving was not invoked."); };
                Func<bool> canLeave = () => {
                    if (workspace == null || !workspace.Dirty) return true;
                    var choice = MessageBox.Show(form, "Save changed SewerPilot project metadata before leaving? This does not save the Bentley model.",
                        "Unsaved project metadata", MessageBoxButtons.YesNoCancel);
                    if (choice == DialogResult.Cancel) return false;
                    if (choice == DialogResult.No) return true;
                    try { saveState(); return true; } catch (Exception error) { failed(error); return false; }
                };
                Action changedProject = () => { invalidate(); baseline = null; source = null; form.SetNativeReady(false); form.SetCalculationReady(false); form.NativeResultsWorkspace.ClearForModelChange(); form.ClearNetwork(); form.ShowProject(workspace); form.DesignWorkspace.Bind(workspace);
                    output.Text = "Project opened. Model data has not been loaded; no engineering confirmation is retained."; };
                form.NewProject.Click += (s, e) => guarded(() => {
                    if (!canLeave() || !ProjectWorkspaceForm.GetNewProject(form, out string name, out string directory)) return;
                    workspace = ProjectWorkspace.Create(directory, name); changedProject(); form.SetStatus("Project created — choose a SewerGEMS model.");
                });
                form.SaveProject.Click += (s, e) => guarded(saveState);
                form.FormClosing += (s, e) => { if (!e.Cancel && !canLeave()) e.Cancel = true; };
                Func<ProjectWorkspace, string, Task> readModel = async (target, path) => {
                        invalidate(); source = null; baseline = null; form.SetNativeReady(false); form.SetCalculationReady(false); form.NativeResultsWorkspace.ClearForModelChange(); form.ClearNetwork();
                        form.SetBusy(true); form.SetStatus("Loading read-only model… supervised RuntimeHost active; duplicate loads disabled.");
                        try {
                            await Task.Run(() => target.ReadNetwork(path));
                            baseline = target.Baseline; source = target.Network;
                            form.ShowProject(workspace); form.ShowNetwork(source);
                            try
                            {
                                ValidateNativeSource(baseline.PrimaryFilePath);
                                form.SetNativeReady(true);
                            }
                            catch (InvalidDataException) { form.SetNativeReady(false); }
                            catch (FileNotFoundException) { form.SetNativeReady(false); }
                            // The general calculation path discovers the scenario from the model, so it
                            // is available for any model the application could open and read.
                            form.SetCalculationReady(true);
                            form.HydraulicCalculationWorkspace.SetModelAvailable(true);
                            form.DesignWorkspace.Bind(workspace);
                            output.Text = "Protected source: " + baseline.PrimaryFilePath + "\r\nModel fingerprint: " + baseline.Fingerprint.Value +
                                "\r\nSource scenario / Physical alternative: " + source.ScenarioContext.ActiveScenarioId + " / " + source.ScenarioContext.PhysicalAlternativeId;
                            DisplayReadiness(new GravityEngineeringInputs { ProjectId = workspace.State.ProjectId,
                                SourceRead = source, ModelFingerprint = baseline.Fingerprint.Value, DesignBasis = form.DesignWorkspace.Design?.Basis }, source, config, output, form.DesignWorkspace.Design);
                        }
                        catch (Exception error) { failed(error); }
                        finally { form.SetBusy(false); }
                };
                Func<SingleOperatorStartupOptions, Task> openProject = async options => {
                    invalidate(); form.SetBusy(true); form.SetStatus("Opening project state and verifying stored model identity…");
                    try {
                        await SingleOperatorProjectStartup.OpenAsync(options, (loaded, status) => {
                            workspace = loaded; changedProject(); form.SetStatus(status);
                        }, readModel);
                    }
                    catch (Exception error) { failed(error); }
                    finally { form.SetBusy(false); form.SaveProject.Enabled = open.Enabled = workspace != null; }
                };
                form.OpenProject.Click += async (s, e) => {
                    if (!canLeave()) return;
                    using (var dialog = new OpenFileDialog { Filter = "SewerPilot project|project.json", Title = "Open SewerPilot Project" })
                    {
                        if (dialog.ShowDialog(form) != DialogResult.OK) return;
                        try { await openProject(SingleOperatorStartupOptions.ForProjectFile(dialog.FileName, false)); }
                        catch (Exception error) { failed(error); }
                    }
                };
                open.Click += async (s, e) => {
                    using (var dialog = new OpenFileDialog { Filter = "SewerGEMS model|*.stsw", Title = "Open protected source model" })
                    {
                        if (dialog.ShowDialog(form) != DialogResult.OK) return;
                        await readModel(workspace, dialog.FileName);
                    }
                };
                load.Click += (s, e) => guarded(() => {
                    GravityExecutionPlanBuilder.Require(source != null, "OPEN_MODEL_FIRST");
                    using (var dialog = new OpenFileDialog { Filter = "Engineering inputs|*.json", Title = "Load explicit project Design Basis / P13 / P14 inputs" })
                    {
                        if (dialog.ShowDialog() != DialogResult.OK) return; invalidate(); WorkingCopyIntegrity.RejectLinks(dialog.FileName);
                        GravityExecutionPlanBuilder.Require(new FileInfo(dialog.FileName).Length <= 8 * 1024 * 1024, "ENGINEERING_INPUT_SIZE");
                        var imported = RuntimeHostProtocol.Serializer().Deserialize<GravityEngineeringInputs>(File.ReadAllText(dialog.FileName));
                        GravityExecutionPlanBuilder.Require(imported != null && (string.IsNullOrWhiteSpace(imported.ModelFingerprint) || imported.ModelFingerprint == baseline.Fingerprint.Value), "SOURCE_IDENTITY");
                        imported.SourceRead = source; imported.ModelFingerprint = baseline.Fingerprint.Value;
                        if(config.Mode==GravityOperatingMode.SingleOperator)
                            SingleOperatorDesignSession.ImportLegacy(workspace,form.DesignWorkspace.Design,imported,service);
                        else form.DesignWorkspace.Import(imported);
                        form.DesignWorkspace.ShowCurrentEngineeringState();
                        DisplayReadiness(form.DesignWorkspace.Design.Inputs,source,config,output,form.DesignWorkspace.Design);
                    }
                });
                review.Click += async (s,e) => {
                    invalidate();
                    try
                    {
                        GravityExecutionPlanBuilder.Require(config.Mode==GravityOperatingMode.SingleOperator,"SINGLE_OPERATOR_MODE_DISABLED");
                        var design=form.DesignWorkspace.Design;
                        GravityExecutionPlanBuilder.Require(design!=null,"OPEN_PROJECT_FIRST");
                        form.ShowEngineeringText(EngineeringDesignPanel.PresentReadiness(design.Readiness()));
                        form.DesignWorkspace.RequireAppliedInputs();
                        form.SetBusy(true);form.SetStatus("Validating applied engineering inputs and exact P13/P14 lineage; zero model mutations.");
                        bool calculate=form.Calculate.Checked;
                        var prepared=await Task.Run(()=>session.Prepare(workspace,design,calculate));
                        form.ShowEngineeringText(prepared.Text);form.SetStatus("Immutable preview prepared. Explicit one-use operator confirmation required.");
                    }
                    catch(Exception error){failed(error);}
                    finally {form.SetBusy(false);confirm.Enabled=session.HasPreview;execute.Enabled=false;form.SaveProject.Enabled=open.Enabled=workspace!=null;}
                };
                confirm.Click += (s,e) => guarded(()=> {
                    form.DesignWorkspace.RequireAppliedInputs();
                    execute.Enabled=session.ConfirmCurrent(workspace,form.DesignWorkspace.Design);
                    form.SetStatus(execute.Enabled?"Exact plan confirmed for one attempt; changes invalidate this confirmation.":"Confirmation cancelled. No runtime operations.");
                });
                execute.Click += async (s,e) => {
                    try
                    {
                        form.DesignWorkspace.RequireAppliedInputs();
                        var design=form.DesignWorkspace.Design;
                        var plan=session.CurrentPlan(workspace,design);
                        string pythonPath=null;
                        if(!plan.NoChanges)
                            using(var python=new OpenFileDialog {Filter="Python interpreter|python.exe",Title="Select installed Python for the accepted 180-second Job Object watchdog"})
                            {
                                if(python.ShowDialog(form)!=DialogResult.OK){session.Cancel();confirm.Enabled=execute.Enabled=false;form.SetStatus("Execution selection cancelled; no runtime operations.");return;}
                                pythonPath=python.FileName;
                            }
                        form.SetBusy(true);form.OpenEvidence.Enabled=false;
                        form.VerifiedHydraulicResultsWorkspace.ClearForModelChange();
                        var progress=new Progress<string>(text=>{if(session.Active)form.SetStatus(text);});
                        var view=await session.ExecuteAsync(workspace,design,pythonPath,progress);
                        form.ShowEngineeringText(view.Text);existingEvidence=view.ExistingEvidenceDirectory;
                        form.OpenEvidence.Enabled=existingEvidence!=null;
                        bool genuine = form.ShowVerifiedHydraulicOutcome(view);
                        form.SetStatus(view.Classification + (genuine ? " — genuine hydraulic results displayed." :
                            " — no trusted genuine result for this execution; inspect evidence."));
                    }
                    catch(Exception error){failed(error);}
                    finally {form.SetBusy(false);confirm.Enabled=execute.Enabled=false;form.SaveProject.Enabled=open.Enabled=workspace!=null;}
                };
                // The model must be opened through the existing protected-source workflow first.
                // This explicit P16 action does not bypass or impersonate the separate engineering
                // design preview/confirmation: it only runs the verified fixed-scenario native solver
                // on a fresh disposable model copy and presents the accepted result of that same run.
                form.NativeResultsWorkspace.CalculateNative.Click += async (s, e) =>
                {
                    if (baseline == null || source == null || workspace == null)
                    {
                        form.NativeResultsWorkspace.RejectNativeCalculation("Open a protected model in SewerPilot first.");
                        return;
                    }
                    string selectedSource = baseline.PrimaryFilePath;
                    try { ValidateNativeSource(selectedSource); }
                    catch (Exception error) { form.NativeResultsWorkspace.RejectNativeCalculation(error.Message); return; }
                    bool farzanegan = P16FarzaneganNativeDesktopExecution.MatchesName(selectedSource);
                    if (MessageBox.Show(form,
                        "Run one native Bentley calculation for the loaded " +
                        (farzanegan ? "Farzanegan Base scenario 1" : "Azadegan scenario 353") +
                        " on a disposable copy?\n" +
                        "The original model will not be modified. One solve and one save are followed by a separate read-only reopen.\n" +
                        "This is NOT design/engineering approval.",
                        "SewerPilot — P16 native calculation", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;

                    form.SetBusy(true);
                    form.NativeResultsWorkspace.BeginNativeCalculation();
                    try
                    {
                        var progress = new Progress<string>(message => form.SetStatus(message));
                        var result = await Task.Run(() => farzanegan
                            ? P16FarzaneganNativeDesktopExecution.Execute(selectedSource,
                                message => ((IProgress<string>)progress).Report(message))
                            : P16NativeDesktopExecution.Execute(selectedSource,
                                message => ((IProgress<string>)progress).Report(message)));
                        if (baseline == null || source == null ||
                            !string.Equals(baseline.PrimaryFilePath, selectedSource, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Selected model identity changed during native calculation.");
                        form.NativeResultsWorkspace.LoadReceipt(result.EvidenceDirectory);
                        existingEvidence = result.EvidenceDirectory;
                        form.OpenEvidence.Enabled = true;
                        form.SetStatus("P16 native calculation and same-run readback verified; Bentley warnings remain visible.");
                    }
                    catch (Exception error)
                    {
                        form.NativeResultsWorkspace.RejectNativeCalculation(error.Message);
                        form.SetStatus("P16 native calculation BLOCKED: " + error.Message);
                    }
                    finally
                    {
                        form.SetBusy(false);
                        if (baseline != null && source != null)
                        {
                            try
                            {
                                ValidateNativeSource(baseline.PrimaryFilePath);
                                form.SetNativeReady(true);
                            }
                            catch { form.SetNativeReady(false); }
                        }
                    }
                };
                // Calculates the selected model's own saved scenario with the installed Bentley engine.
                // No design candidate, engineering confirmation or authorization is involved: this
                // operation makes no engineering design change. The original model is only read; one
                // disposable copy is solved once, saved once, then independently reopened for results.
                form.HydraulicCalculationWorkspace.CalculateExistingModel.Click += async (s, e) =>
                {
                    if (baseline == null || source == null || workspace == null)
                    {
                        form.HydraulicCalculationWorkspace.RejectCalculation("Open a SewerGEMS model in SewerPilot first.");
                        return;
                    }
                    string selectedSource = baseline.PrimaryFilePath;
                    try { GeneralModelCalculationService.ValidateSource(selectedSource); }
                    catch (Exception error)
                    {
                        form.HydraulicCalculationWorkspace.RejectCalculation(error.Message);
                        ApplicationDiagnostics.RecordFailure("Calculate existing model", error);
                        return;
                    }
                    if (MessageBox.Show(form,
                        "Calculate the saved scenario of the selected model with the installed Bentley engine?\r\n\r\n" +
                        "• A disposable copy is created; your original model is not modified.\r\n" +
                        "• The model's own saved scenario and calculation options are used.\r\n" +
                        "• One solve and one save, then a separate read-only reopen.\r\n" +
                        "• This is not a design approval.\r\n\r\n" +
                        "Model: " + Path.GetFileName(selectedSource),
                        "SewerPilot — Calculate Existing Model", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;

                    form.SetBusy(true);
                    form.HydraulicCalculationWorkspace.BeginCalculation();
                    try
                    {
                        var progress = new Progress<string>(message => form.SetStatus(message));
                        var receipt = await Task.Run(() => GeneralModelCalculationService.Execute(selectedSource,
                            message => ((IProgress<string>)progress).Report(message)));
                        if (baseline == null || source == null ||
                            !string.Equals(baseline.PrimaryFilePath, selectedSource, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("The selected model changed during the calculation.");
                        form.HydraulicCalculationWorkspace.ShowReceipt(receipt);
                        existingEvidence = receipt.EvidenceDirectory;
                        form.OpenEvidence.Enabled = true;
                        form.SetStatus("Calculated: scenario " + receipt.ScenarioId + ", engine " + receipt.Engine +
                            " — genuine results read back from the same run.");
                    }
                    catch (Exception error)
                    {
                        form.HydraulicCalculationWorkspace.RejectCalculation(error.Message);
                        form.SetStatus("Calculation did not complete: " + error.Message);
                        ApplicationDiagnostics.RecordFailure("Calculate existing model", error);
                    }
                    finally
                    {
                        form.SetBusy(false);
                        form.SetCalculationReady(baseline != null && source != null);
                    }
                };
                form.OpenEvidence.Click+=(s,e)=>guarded(()=> {
                    GravityExecutionPlanBuilder.Require(Directory.Exists(existingEvidence),"EVIDENCE_DIRECTORY_MISSING");
                    WorkingCopyIntegrity.RejectLinks(existingEvidence);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(existingEvidence){UseShellExecute=true});
                });
                // Engineer-facing support action. Produces a local, redacted archive and never uploads it.
                form.ExportDiagnostics.Click += (s, e) => guarded(() =>
                {
                    string suggested = "SewerPilot-Diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip";
                    string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (string.IsNullOrWhiteSpace(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    using (var dialog = new SaveFileDialog
                    {
                        Filter = "Diagnostics archive (*.zip)|*.zip",
                        Title = "Save SewerPilot diagnostics archive",
                        FileName = suggested,
                        InitialDirectory = folder,
                        OverwritePrompt = true
                    })
                    {
                        if (dialog.ShowDialog(form) != DialogResult.OK) return;
                        var result = DiagnosticsExportService.Export(dialog.FileName);
                        form.SetStatus("Diagnostics exported: " + result.ArchivePath);
                        MessageBox.Show(form,
                            "A diagnostics archive was saved to:\r\n" + result.ArchivePath +
                            "\r\n\r\nIt contains " + result.Entries.Count + " files: application version, workstation details, " +
                            "runtime detection, recent log entries and the last failed operation.\r\n\r\n" +
                            "It contains no project or model content, and nothing was sent anywhere. " +
                            "Please review it before sharing it with the SewerPilot team.",
                            "SewerPilot — diagnostics exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + result.ArchivePath + "\"") { UseShellExecute = true }); }
                        catch (Exception) { /* Opening the folder is a convenience only. */ }
                    }
                });
                if (startup.ProjectJsonPath != null)
                    form.Shown += async (s, e) => await openProject(startup);
                form.ShowDialog();
            }
        }
        private static void ValidateNativeSource(string path)
        {
            if (P16FarzaneganNativeDesktopExecution.MatchesName(path))
                P16FarzaneganNativeDesktopExecution.ValidateSource(path);
            else P16NativeDesktopExecution.ValidateSource(path);
        }

        // Adopts the revision the application's own StormFlow save accepted into the shared project
        // workspace. The panel exposes that accepted state only after a save has succeeded, so a failed
        // save can never advance the active revision; an ambiguous reconciliation propagates and fails
        // closed with the unsaved workspace left intact.
        internal static void SynchronizeStormFlowSave(ProjectWorkspace workspace, StormFlowDesignPanel panel)
        {
            var accepted = panel?.LastSavedState;
            if (accepted != null && workspace != null) workspace.AcceptStormFlowSave(accepted);
        }
        private static string Describe(GravityEngineeringReadiness r) => "Project: " + (r.ProjectId ?? "MISSING") + "\r\nModel: " + r.ModelFingerprint +
            "\r\nMissing orientations: " + string.Join(", ", r.MissingFlowOrientationConduitIds) + "\r\nMissing outfall decisions: " + string.Join(", ", r.MissingOutfallDecisionNodeIds) +
            "\r\n" + string.Join("\r\n", r.BlockingReasons.Select(b => b.Code + ": " + b.Detail));
        private static void DisplayReadiness(GravityEngineeringInputs input, NetworkReadResult source, SewerPilotOperatingConfiguration config, TextBox output, EngineeringDesignWorkspace design = null)
        {
            var r = GravityEngineeringReadinessAssessment.Assess(new GravityEngineeringReadinessInput { EngineeringInputs = input,
                OperatingMode = config.Mode, SourceKind = GravityReadinessSourceKind.SuppliedP12Snapshot }, null);
            output.Text += "\r\n" + Describe(r);
            if (design != null)
            {
                var prepResult = design.TryPrepareAutomaticDesign();
                if (prepResult != null && prepResult.Components.Count > 0)
                {
                    output.Text += "\r\n--- AUTOMATIC ROUTING PREPARATION ---" +
                        "\r\nPolicy: " + prepResult.PolicyId + " v" + prepResult.PolicyVersion +
                        "\r\nComponents evaluated: " + prepResult.Components.Count +
                        "\r\nComponent status: " + string.Join("; ", prepResult.Components.SelectMany(c => c.UnresolvedReasons));
                }
                var overflow = design.EvaluateControlledOverflowProposal();
                if (overflow != null && overflow.IsSupported)
                {
                    output.Text += "\r\n--- CONTROLLED OVERFLOW PROPOSAL (AWAITING REVIEW) ---" +
                        "\r\nConduit " + overflow.OverflowConduitId + " (Stop Outfall " + overflow.OverflowOutfallId + ") start weir control structure " + overflow.ControlStructureId +
                        "\r\nCrest: " + overflow.CrestElevation + " ft | Top: " + overflow.TopElevation + " ft | Length: " + overflow.WeirLength + " ft | Coeff: " + overflow.WeirCoefficient +
                        "\r\nPrimary Treatment Route: Outfall " + overflow.PrimaryOutfallId +
                        "\r\nOverflow Relief Route: Outfall " + overflow.OverflowOutfallId +
                        "\r\nStatus: " + overflow.Status +
                        "\r\nAuthority: " + overflow.Detail;
                }
            }
        }
    }
}
