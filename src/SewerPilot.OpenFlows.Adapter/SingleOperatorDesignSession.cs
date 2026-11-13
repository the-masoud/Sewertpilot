using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    // Internal application seam. Tests use only TEST_ONLY previews; the installed implementation always uses the existing service.
    internal interface ISingleOperatorDesignOperations
    {
        GravityEngineeringIntakeResult Review(GravityEngineeringIntakeRequest request);
        SingleOperatorPreview Preview(GravityEngineeringIntakeRequest request, string path);
        SingleOperatorConfirmation Confirm(SingleOperatorPreview preview);
        GravityProductionExecutionOutcome Execute(SingleOperatorConfirmation confirmation, GravityExecutionPlan plan,
            ProtectedBaseline baseline, string python, Action<string> evidenceReady);
    }
    internal sealed class SingleOperatorDesignOperations : ISingleOperatorDesignOperations
    {
        private readonly SingleOperatorExecutionService service;
        internal SingleOperatorDesignOperations(SingleOperatorExecutionService service) { this.service = service; }
        public GravityEngineeringIntakeResult Review(GravityEngineeringIntakeRequest request) => service.ReviewInputs(request);
        public SingleOperatorPreview Preview(GravityEngineeringIntakeRequest request, string path) => service.PreparePreview(request, path);
        public SingleOperatorConfirmation Confirm(SingleOperatorPreview preview) => service.Confirm(preview);
        public GravityProductionExecutionOutcome Execute(SingleOperatorConfirmation confirmation, GravityExecutionPlan plan,
            ProtectedBaseline baseline, string python, Action<string> evidenceReady)
        {
            OpenFlowsRuntimeCandidate runtime = null; int count = 0;
            if (!plan.NoChanges)
            {
                var options = new OpenFlowsDiscoveryOptions(Environment.GetEnvironmentVariable("SEWERPILOT_OPENFLOWS_HOME"));
                var discovery = new OpenFlowsRuntimeDiscovery(new WindowsRuntimeLocationSource(),
                    new InstalledRuntimeInspector(path => new MetadataWorker().Inspect(path))).Discover(options);
                var selected = new OpenFlowsRuntimeSelector().Select(discovery, options);
                GravityExecutionPlanBuilder.Require(selected.Status == OpenFlowsRuntimeSelectionStatus.Selected, "RUNTIME_SELECTION");
                runtime = selected.Candidate; count = discovery.Candidates.Count;
            }
            return service.ExecuteWithEvidenceObserver(confirmation, plan, baseline, runtime, count, python, evidenceReady);
        }
    }
    internal sealed class SingleOperatorDesignSession
    {
        private readonly object gate = new object();
        private readonly ISingleOperatorDesignOperations operations;
        private SingleOperatorPreview preview;
        private SingleOperatorConfirmation confirmation;
        private string contextJson;
        private bool active, calculate;
        internal SingleOperatorDesignSession(ISingleOperatorDesignOperations operations) { this.operations = operations ?? throw new ArgumentNullException(nameof(operations)); }
        internal bool Active { get { lock(gate) return active; } }
        internal bool HasPreview { get { lock(gate) return preview != null; } }
        internal bool HasConfirmation { get { lock(gate) return confirmation != null; } }
        internal void Invalidate()
        { lock(gate) { preview = null; confirmation = null; contextJson = null; } }
        internal void Cancel() { lock(gate) { Require(!active,"EXECUTION_ALREADY_ACTIVE"); Invalidate(); } }
        private static void Require(bool value,string code) => GravityExecutionPlanBuilder.Require(value,code);
        internal static GravityEngineeringIntakeRequest Capture(ProjectWorkspace project, EngineeringDesignWorkspace design, bool calculate)
        {
            Require(project != null && design != null,"OPEN_PROJECT_FIRST");
            Require(!project.Reading && project.Network != null && project.Baseline != null && design.HasLiveNetwork && !design.SourceChanged,"LIVE_P12_AUTHORITY_REQUIRED");
            var input = RuntimeHostProtocol.Serializer().Deserialize<GravityEngineeringInputs>(GravityExecutionPlanBuilder.Json(design.Inputs));
            Require(input.ProjectId == project.State.ProjectId && input.DesignBasis?.CommonDesignBasis?.ProjectId == project.State.ProjectId,"PROJECT_IDENTITY");
            Require(string.Equals(project.State.Model?.ProtectedBaselineFingerprint,project.Baseline.Fingerprint.Value,StringComparison.OrdinalIgnoreCase) &&
                input.ModelFingerprint == project.Baseline.Fingerprint.Value &&
                string.Equals(Path.GetFullPath(project.State.Model?.BaselineLocationMetadata ?? ""),project.Baseline.PrimaryFilePath,StringComparison.OrdinalIgnoreCase),"SOURCE_IDENTITY");
            Require(project.Baseline.VerifyCurrentIntegrity().IsMatch,"BASELINE_SOURCE_IDENTITY");
            Require(GravityExecutionPlanBuilder.Json(input.SourceRead) == GravityExecutionPlanBuilder.Json(project.Network),"LIVE_P12_SNAPSHOT_MISMATCH");
            var network = new GravityNetworkIngestionService().Ingest(project.Network,input.ModelFingerprint,schemaVersion:2);
            Require(network.NetworkFingerprint == design.NetworkFingerprint,"SOURCE_NETWORK_FINGERPRINT");
            Require(project.State.ScenarioAlternativeState?.SelectedScenarioId == network.ScenarioContext.ScenarioId &&
                project.State.ScenarioAlternativeState?.SelectedPhysicalAlternativeId == network.ScenarioContext.PhysicalAlternativeId,"PROJECT_SOURCE_CONTEXT");
            Require(!string.IsNullOrWhiteSpace(input.FeasibilityJson) && !string.IsNullOrWhiteSpace(input.GenerationRequestJson) &&
                !string.IsNullOrWhiteSpace(input.CandidateJson),"P13_P14_ASSESS_AND_SELECT_FIRST");
            string selectedRecipe = SelectedRecipe(input.CandidateJson);
            Require(design.Recipes.Count == 0 || design.RecipeConstraintFingerprint == design.Constraints?.VerticalConstraintFingerprint,"RECIPE_STALE");
            var generation = GravityEngineeringWire.Read<GravityCandidateGenerationRequest>(RuntimeHostProtocol.Serializer().DeserializeObject(input.GenerationRequestJson));
            Require(GravityExecutionPlanBuilder.Json(generation.Recipes) == GravityExecutionPlanBuilder.Json(design.Recipes),"RECIPE_STATE_MISMATCH");
            return new GravityEngineeringIntakeRequest { SelectedRecipeId = selectedRecipe, Context = new GravityEngineeringReadinessInput {
                EngineeringInputs = input, SourceKind = GravityReadinessSourceKind.SuppliedP12Snapshot,
                OperatingMode = GravityOperatingMode.SingleOperator, Calculate = calculate } };
        }
        internal static void ImportLegacy(ProjectWorkspace project,EngineeringDesignWorkspace design,GravityEngineeringInputs supplied,
            SingleOperatorExecutionService service)
        {
            Require(project?.Network!=null&&project.Baseline!=null&&design!=null,"LIVE_P12_AUTHORITY_REQUIRED");
            Require(supplied?.ProjectId==project.State.ProjectId&&supplied.ModelFingerprint==project.Baseline.Fingerprint.Value&&
                GravityExecutionPlanBuilder.Json(supplied.SourceRead)==GravityExecutionPlanBuilder.Json(project.Network),"IMPORT_IDENTITY_MISMATCH");
            Require(!string.IsNullOrWhiteSpace(supplied.CandidateJson),"P14_SELECTION_REQUIRED");
            string selectedJson=GravityExecutionPlanBuilder.Json(RuntimeHostProtocol.Serializer().DeserializeObject(supplied.CandidateJson));
            var reviewed=service.ReviewInputs(new GravityEngineeringIntakeRequest {SelectedRecipeId=SelectedRecipe(supplied.CandidateJson),Context=new GravityEngineeringReadinessInput {
                EngineeringInputs=supplied,OperatingMode=GravityOperatingMode.SingleOperator,SourceKind=GravityReadinessSourceKind.SuppliedP12Snapshot }});
            Require(reviewed.EngineeringContentValidated&&reviewed.Rejection==null,"EXTERNAL_ENGINEERING_INPUT");
            design.Import(reviewed.PreparedInputs);design.Assess();var generated=design.GenerateCandidates();
            var exact=generated.Candidates.SingleOrDefault(c=>GravityExecutionPlanBuilder.Json(c)==selectedJson);
            Require(exact!=null,"CANDIDATE_RECONSTRUCTION");design.SelectCandidate(exact.CandidateId);
        }
        private static string SelectedRecipe(string candidateJson)
        {
            // Candidate objects are regenerated by intake, not reconstructed through the constraint/recipe wire helper.
            var fields=RuntimeHostProtocol.Serializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(candidateJson);
            object recipe=null;
            Require(fields!=null&&fields.TryGetValue("RecipeId",out recipe)&&recipe is string&&!string.IsNullOrWhiteSpace((string)recipe),"P14_SELECTION_REQUIRED");
            return (string)recipe;
        }
        private static string Context(ProjectWorkspace project, EngineeringDesignWorkspace design, bool calculate)
        {
            var request = Capture(project,design,calculate);
            return GravityExecutionPlanBuilder.Json(new { request.Context.EngineeringInputs, request.SelectedRecipeId,
                calculate, project.State.StateRevision, project.DirectoryPath, project.Baseline.PrimaryFilePath,
                recipes = GravityExecutionPlanBuilder.Json(design.Recipes), design.RecipeConstraintFingerprint });
        }
        internal GravityEngineeringIntakeResult Review(ProjectWorkspace project, EngineeringDesignWorkspace design)
            => operations.Review(Capture(project,design,false));
        internal SingleOperatorPreview Prepare(ProjectWorkspace project, EngineeringDesignWorkspace design, bool solver)
        {
            lock(gate)
            {
                Require(!active,"EXECUTION_ALREADY_ACTIVE"); Invalidate();
                var request = Capture(project,design,solver); var before = Context(project,design,solver);
                var next = operations.Preview(request,project.Baseline.PrimaryFilePath);
                Require(before == Context(project,design,solver),"ENGINEERING_CONTEXT_CHANGED");
                preview = next; contextJson = before; calculate = solver; return next;
            }
        }
        private void Current(ProjectWorkspace project, EngineeringDesignWorkspace design)
        {
            Require(preview != null && contextJson != null,"PREVIEW_REQUIRED");
            try { Require(contextJson == Context(project,design,calculate),"PREVIEW_STALE"); }
            catch { Invalidate(); throw; }
        }
        internal bool ConfirmCurrent(ProjectWorkspace project, EngineeringDesignWorkspace design)
        {
            lock(gate)
            {
                Require(!active,"EXECUTION_ALREADY_ACTIVE"); Current(project,design); confirmation = null;
                var accepted = operations.Confirm(preview); Current(project,design);
                confirmation = accepted; return confirmation != null;
            }
        }
        internal GravityExecutionPlan CurrentPlan(ProjectWorkspace project, EngineeringDesignWorkspace design)
        { lock(gate) { Current(project,design); return preview.Materialize(); } }
        internal Task<SingleOperatorOutcomeView> ExecuteAsync(ProjectWorkspace project, EngineeringDesignWorkspace design,
            string python, IProgress<string> progress = null)
        {
            SingleOperatorPreview frozen; SingleOperatorConfirmation oneUse; string expected; bool solver;
            lock(gate)
            {
                Require(!active,"EXECUTION_ALREADY_ACTIVE"); Current(project,design);
                Require(confirmation != null,"OPERATOR_CONFIRMATION_REQUIRED");
                frozen=preview; oneUse=confirmation; expected=contextJson; solver=calculate;
                confirmation=null; active=true;
            }
            return Task.Run(() => {
                using(var checkpoints=new SingleOperatorCheckpointObserver(progress))
                {
                    try
                    {
                        Require(expected == Context(project,design,solver),"PREVIEW_STALE");
                        progress?.Report("Dispatching the exact frozen plan; execution and cleanup are supervised.");
                        var outcome=operations.Execute(oneUse,frozen.Materialize(),project.Baseline,python,checkpoints.Attach);
                        checkpoints.Capture(); return SingleOperatorOutcomeView.Load(outcome,frozen);
                    }
                    catch(Exception error) { return SingleOperatorOutcomeView.Create(new GravityProductionExecutionOutcome {
                        Status="BLOCKED_APPLICATION", Blocker=error.ToString() },frozen); }
                    finally { lock(gate) { active=false; Invalidate(); } }
                }
            });
        }
    }
}
