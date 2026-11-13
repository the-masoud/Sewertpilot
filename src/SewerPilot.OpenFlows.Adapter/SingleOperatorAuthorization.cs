using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum GravityOperatingMode { Enterprise = 0, SingleOperator = 1 }
    public sealed class SewerPilotOperatingConfiguration
    {
        public SewerPilotOperatingConfiguration(GravityOperatingMode mode)
        { GravityExecutionPlanBuilder.Require(Enum.IsDefined(typeof(GravityOperatingMode), mode), "OPERATING_MODE_INVALID"); Mode = mode; }
        public GravityOperatingMode Mode { get; }
        // Trusted application installation/user configuration, never a request, DTO, environment fallback or verifier verdict.
        public static SewerPilotOperatingConfiguration LoadApplicationConfiguration()
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(SewerPilotOperatingConfiguration).Assembly.Location), "SewerPilot.OperatingMode.json");
            if (!File.Exists(path)) return new SewerPilotOperatingConfiguration(GravityOperatingMode.SingleOperator);
            WorkingCopyIntegrity.RejectLinks(path);
            GravityExecutionPlanBuilder.Require(new FileInfo(path).Length <= 4096, "OPERATING_MODE_CONFIGURATION");
            var fields = RuntimeHostProtocol.Serializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(path));
            GravityExecutionPlanBuilder.Require(fields != null && fields.Count == 1 && fields.TryGetValue("mode", out object raw) && raw is string &&
                ((string)raw == "SingleOperator" || (string)raw == "Enterprise"), "OPERATING_MODE_CONFIGURATION");
            return new SewerPilotOperatingConfiguration((GravityOperatingMode)Enum.Parse(typeof(GravityOperatingMode), (string)fields["mode"]));
        }
    }
    public sealed class SingleOperatorPreview
    {
        internal SingleOperatorPreview(GravityExecutionPlan plan, string model, bool testOnly)
        {
            FrozenPlan = GravityExecutionPlanBuilder.Json(plan); ModelPath = Path.GetFullPath(model);
            PlanFingerprint = plan.PlanFingerprint; TestOnly = testOnly;
            Text = "SewerPilot SingleOperator — proposed engineering execution\r\n" +
                "Project: " + plan.Inputs.ProjectId + "\r\nProtected source model: " + ModelPath +
                "\r\nSource model fingerprint: " + plan.Inputs.ModelFingerprint +
                "\r\nSource scenario / Physical alternative: " + plan.SourceScenarioId + " / " + plan.SourcePhysicalAlternativeId +
                "\r\nCandidate: " + plan.CandidateId + " / " + plan.CandidateFingerprint +
                "\r\nPlan: " + plan.PlanFingerprint + "\r\nSolver requested: " + plan.Calculate +
                "\r\nField writes: " + plan.TotalFieldWriteCount + " (endpoints " + plan.EndpointWriteCount + ", localization " + plan.LocalizationWriteCount + ")\r\n" +
                (plan.NoChanges ? "No changes: zero writes, saves, solver runs or runtime launches.\r\n" : "Execute on a NEW controlled copy with isolated scenario and Physical alternative.\r\n") +
                string.Join("\r\n", plan.Endpoints.Select(e => string.Format(CultureInfo.InvariantCulture,
                    "Conduit {0} {1} / node {2}: {3:R} -> {4:R} {5}; inherited={6}; localize={7}; change={8}",
                    e.ConduitId, e.End, e.NodeId, e.Original, e.Proposed, e.Unit.Symbol, e.OriginalInherited, e.Localize, e.Changed)));
        }
        internal string FrozenPlan { get; }
        internal bool TestOnly { get; }
        public string ModelPath { get; }
        public string PlanFingerprint { get; }
        public string Text { get; }
        internal GravityExecutionPlan Materialize()
        { var p = RuntimeHostProtocol.Serializer().Deserialize<GravityExecutionPlan>(FrozenPlan); GravityExecutionPlanBuilder.ValidateSingleOperator(p); return p; }
    }
    public sealed class SingleOperatorConfirmation
    {
        private int used;
        private readonly SingleOperatorPreview preview;
        internal SingleOperatorConfirmation(SingleOperatorPreview preview) { this.preview = preview; ConfirmedUtc = DateTimeOffset.UtcNow.ToString("o");
            using (var identity = WindowsIdentity.GetCurrent()) { OperatorSid = identity.User.Value; OperatorName = identity.Name; } }
        public string PlanFingerprint => preview.PlanFingerprint;
        public string OperatorSid { get; }
        public string OperatorName { get; }
        public string ConfirmedUtc { get; }
        internal SingleOperatorExecutionPermit Consume(GravityExecutionPlan supplied, string model)
        {
            GravityExecutionPlanBuilder.Require(Interlocked.Exchange(ref used, 1) == 0, "OPERATOR_CONFIRMATION_ALREADY_USED");
            GravityExecutionPlanBuilder.ValidateSingleOperator(supplied);
            GravityExecutionPlanBuilder.Require(GravityExecutionPlanBuilder.Json(supplied) == preview.FrozenPlan &&
                Path.GetFullPath(model) == preview.ModelPath && WindowsIdentity.GetCurrent().User.Value == OperatorSid, "OPERATOR_CONFIRMATION_PLAN_MISMATCH");
            return new SingleOperatorExecutionPermit(preview, this);
        }
    }
    internal sealed class SingleOperatorExecutionPermit
    {
        private readonly SingleOperatorPreview preview;
        private readonly SingleOperatorConfirmation confirmation;
        internal SingleOperatorExecutionPermit(SingleOperatorPreview preview, SingleOperatorConfirmation confirmation) { this.preview = preview; this.confirmation = confirmation; }
        internal bool TestOnly => preview.TestOnly;
        internal GravityExecutionPlan Materialize() => preview.Materialize();
        internal void RequirePlan(GravityExecutionPlan plan) => GravityExecutionPlanBuilder.Require(GravityExecutionPlanBuilder.Json(plan) == preview.FrozenPlan, "OPERATOR_CONFIRMATION_PLAN_MISMATCH");
        internal void WriteReceipt(string directory, HostRequest request)
        {
            GravityExecutionPlanBuilder.Require(!TestOnly, "TEST_ONLY_NON_ENGINEERING"); RequirePlan(request.EngineeringPlan);
            RuntimeHostProtocol.AtomicWrite(Path.Combine(directory, "operator-confirmation.json"), new SingleOperatorReceipt {
                RunId = request.RunId, PlanJson = preview.FrozenPlan, ModelPath = preview.ModelPath, WorkingPath = request.WorkingModelPath,
                OperatorSid = confirmation.OperatorSid, OperatorName = confirmation.OperatorName, ConfirmedUtc = confirmation.ConfirmedUtc });
            var security = new FileSecurity(); var sid = new SecurityIdentifier(confirmation.OperatorSid);
            security.SetOwner(sid); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
            File.SetAccessControl(Path.Combine(directory, "operator-confirmation.json"), security);
        }
    }
    internal sealed class SingleOperatorReceipt
    {
        public string RunId { get; set; }
        public string PlanJson { get; set; }
        public string ModelPath { get; set; }
        public string WorkingPath { get; set; }
        public string OperatorSid { get; set; }
        public string OperatorName { get; set; }
        public string ConfirmedUtc { get; set; }
    }
    internal static class GravityExecutionAuthorization
    {
        private static SingleOperatorReceipt runtimeConfirmation;
        internal static void InitializeRuntime(SewerPilotOperatingConfiguration config, HostRequest request, string directory)
        {
            if (request?.Operation != GravityWriteAndSolverHost.ProductionOperation) return;
            bool single = request.EngineeringPlan?.SchemaVersion == 2;
            GravityExecutionPlanBuilder.Require(config != null && (config.Mode == GravityOperatingMode.SingleOperator) == single, "OPERATING_MODE_PLAN_MISMATCH");
            if (!single) return; // Enterprise provider checks stay mandatory; no fallback.
            GravityExecutionPlanBuilder.ValidateSingleOperator(request.EngineeringPlan);
            string path = Path.Combine(directory, "operator-confirmation.json"); WorkingCopyIntegrity.RejectLinks(path);
            GravityExecutionPlanBuilder.Require(File.Exists(path) && new FileInfo(path).Length <= 8 * 1024 * 1024, "OPERATOR_CONFIRMATION_REQUIRED");
            var acl = File.GetAccessControl(path); string sid = WindowsIdentity.GetCurrent().User.Value;
            GravityExecutionPlanBuilder.Require(acl.AreAccessRulesProtected && acl.GetOwner(typeof(SecurityIdentifier)).Value == sid &&
                acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().All(r => r.IdentityReference.Value == sid && r.AccessControlType == AccessControlType.Allow), "OPERATOR_CONFIRMATION_FILE_PERMISSION");
            var receipt = RuntimeHostProtocol.Serializer().Deserialize<SingleOperatorReceipt>(File.ReadAllText(path));
            GravityExecutionPlanBuilder.Require(receipt != null && receipt.RunId == request.RunId && receipt.OperatorSid == sid &&
                receipt.PlanJson == GravityExecutionPlanBuilder.Json(request.EngineeringPlan) && receipt.WorkingPath == request.WorkingModelPath &&
                receipt.ModelPath == request.Diagnostics["ProtectedBaselinePath"] &&
                DateTimeOffset.TryParse(receipt.ConfirmedUtc, out DateTimeOffset confirmed) && confirmed <= DateTimeOffset.UtcNow &&
                confirmed > DateTimeOffset.UtcNow.AddHours(-1), "OPERATOR_CONFIRMATION_PLAN_MISMATCH");
            using (var consumed = new FileStream(Path.Combine(directory, "operator-confirmation-used"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { consumed.WriteByte(1); consumed.Flush(true); } // Atomic one-use claim; failure never retries.
            runtimeConfirmation = receipt;
        }
        internal static void Require(HostRequest request, SingleOperatorExecutionPermit permit = null)
        {
            if (request.EngineeringPlan?.SchemaVersion != 2) { GravityEngineeringAuthorization.RequireRuntimeApproval(request.EngineeringPlan); return; }
            GravityExecutionPlanBuilder.ValidateSingleOperator(request.EngineeringPlan);
            if (permit != null) { GravityExecutionPlanBuilder.Require(!permit.TestOnly, "TEST_ONLY_NON_ENGINEERING"); permit.RequirePlan(request.EngineeringPlan); return; }
            GravityExecutionPlanBuilder.Require(runtimeConfirmation != null && runtimeConfirmation.RunId == request.RunId &&
                runtimeConfirmation.PlanJson == GravityExecutionPlanBuilder.Json(request.EngineeringPlan) &&
                runtimeConfirmation.WorkingPath == request.WorkingModelPath, "OPERATOR_CONFIRMATION_REQUIRED");
        }
    }
    public sealed class SingleOperatorExecutionService
    {
        private readonly SewerPilotOperatingConfiguration config;
        public SingleOperatorExecutionService(SewerPilotOperatingConfiguration configuration) { config = configuration ?? throw new ArgumentNullException(nameof(configuration)); }
        private void RequireMode() => GravityExecutionPlanBuilder.Require(config.Mode == GravityOperatingMode.SingleOperator, "SINGLE_OPERATOR_MODE_DISABLED");
        public GravityEngineeringIntakeResult ReviewInputs(GravityEngineeringIntakeRequest request)
        {
            RequireMode(); GravityExecutionPlanBuilder.Require(request?.Context != null, "EXTERNAL_ENGINEERING_INPUT");
            var context = request.Context;
            return GravityEngineeringIntake.Review(new GravityEngineeringIntakeRequest {
                Constraints = request.Constraints, CandidateRequest = request.CandidateRequest, SelectedRecipeId = request.SelectedRecipeId,
                Context = new GravityEngineeringReadinessInput { EngineeringInputs = context.EngineeringInputs,
                    OperatingMode = GravityOperatingMode.SingleOperator, Calculate = context.Calculate, SourceKind = context.SourceKind,
                    ModelName = context.ModelName, HistoricalNetworkFingerprint = context.HistoricalNetworkFingerprint, SourceEvidenceReference = context.SourceEvidenceReference } }, null);
        }
        public SingleOperatorPreview PreparePreview(GravityEngineeringIntakeRequest request, string modelPath)
        {
            var reviewed = ReviewInputs(request);
            GravityExecutionPlanBuilder.Require(reviewed.EngineeringContentValidated && reviewed.Rejection == null, "EXTERNAL_ENGINEERING_INPUT");
            GravityExecutionPlanBuilder.Require(request.Context.SourceKind == GravityReadinessSourceKind.SuppliedP12Snapshot &&
                reviewed.Readiness.BlockingReasons.All(b => b.Code == "OPERATOR_CONFIRMATION_REQUIRED"), "SOURCE_AUTHORITY_UNAVAILABLE");
            var plan = GravityExecutionPlanBuilder.BuildSingleOperator(reviewed.PreparedInputs, request.Context.Calculate);
            GravityExecutionPlanBuilder.Require(!plan.Inputs.ProjectId.Contains("TEST_ONLY_NON_ENGINEERING"), "TEST_ONLY_NON_ENGINEERING");
            return new SingleOperatorPreview(plan, modelPath, false);
        }
        public SingleOperatorConfirmation Confirm(SingleOperatorPreview preview)
        {
            RequireMode(); GravityExecutionPlanBuilder.Require(preview != null && !preview.TestOnly, "OPERATOR_CONFIRMATION_REQUIRED");
            preview.Materialize();
            using (var form = new Form { Text = "SewerPilot — review exact plan", Width = 960, Height = 650, StartPosition = FormStartPosition.CenterScreen })
            using (var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Text = preview.Text })
            using (var confirm = new Button { Text = "Confirm one execution attempt", Dock = DockStyle.Bottom, Height = 45 })
            using (var cancel = new Button { Text = "Cancel", Dock = DockStyle.Bottom, Height = 35, DialogResult = DialogResult.Cancel })
            {
                confirm.Click += (sender, args) => { form.DialogResult = DialogResult.OK; form.Close(); };
                form.Controls.Add(text); form.Controls.Add(cancel); form.Controls.Add(confirm); form.CancelButton = cancel;
                return form.ShowDialog() == DialogResult.OK ? new SingleOperatorConfirmation(preview) : null;
            }
        }
        public GravityProductionExecutionOutcome Execute(SingleOperatorConfirmation confirmation, GravityExecutionPlan plan, ProtectedBaseline baseline,
            OpenFlowsRuntimeCandidate runtime, int count, string pythonPath)
        { return ExecuteWithEvidenceObserver(confirmation, plan, baseline, runtime, count, pythonPath, null); }
        internal GravityProductionExecutionOutcome ExecuteWithEvidenceObserver(SingleOperatorConfirmation confirmation, GravityExecutionPlan plan, ProtectedBaseline baseline,
            OpenFlowsRuntimeCandidate runtime, int count, string pythonPath, Action<string> evidenceDirectoryReady)
        {
            RequireMode(); GravityExecutionPlanBuilder.Require(confirmation != null, "OPERATOR_CONFIRMATION_REQUIRED");
            var permit = confirmation.Consume(plan, baseline?.PrimaryFilePath ?? "");
            return ExecuteConfirmed(permit, () => baseline != null && baseline.VerifyCurrentIntegrity().IsMatch &&
                baseline.Fingerprint.Value == plan.Inputs.ModelFingerprint,
                () => new RuntimeHostClient().ExecuteSingleOperator(permit, baseline, runtime, count, pythonPath, evidenceDirectoryReady));
        }
        internal static GravityProductionExecutionOutcome ExecuteConfirmed(SingleOperatorExecutionPermit permit, Func<bool> sourceMatches,
            Func<GravityProductionExecutionOutcome> execute)
        {
            var plan = permit.Materialize();
            GravityExecutionPlanBuilder.Require(!permit.TestOnly || plan.NoChanges, "TEST_ONLY_NON_ENGINEERING");
            GravityExecutionPlanBuilder.Require(sourceMatches(), "BASELINE_SOURCE_IDENTITY");
            if (plan.NoChanges) return new GravityProductionExecutionOutcome { Status = "NO_CHANGES_NOT_EXECUTED", NoChanges = true, BaselineIntegrityVerified = true, RuntimeInvocations = 0 };
            return execute();
        }
        public GravityExecutionPlan GetPreviewPlan(SingleOperatorPreview preview) { RequireMode(); return preview.Materialize(); }
    }
}
