using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class GravityProductionExecutionOutcome
    {
        public string Status { get; internal set; }
        public string Blocker { get; internal set; }
        public bool NoChanges { get; internal set; }
        public int RuntimeInvocations { get; internal set; }
        public int? RuntimePid { get; internal set; }
        public double? RuntimeSeconds { get; internal set; }
        public string WorkingModelPath { get; internal set; }
        public string EvidenceDirectory { get; internal set; }
        public bool? BaselineIntegrityVerified { get; internal set; }
        public int FieldWrites { get; internal set; }
        public int Saves { get; internal set; }
        public int SolverRuns { get; internal set; }
        public string ComputeStatus { get; internal set; }
        public P16ResultContentAuthority ContentAuthority { get; internal set; }
        public HydraulicResultSet HydraulicResults { get; internal set; }
    }

    public static partial class GravityCandidateExecutor
    {
        // Compatibility entrypoint has no trusted composition. Supplied JSON and a P5 token cannot authorize it.
        public static GravityProductionExecutionOutcome ExecuteEngineeringPlan(GravityExecutionPlan plan,
            ProtectedBaseline baseline, OpenFlowsRuntimeCandidate runtime, int runtimeCandidateCount, string watchdogPythonPath)
        {
            return new GravityEngineeringExecutionService().ExecuteEngineeringPlan(plan, baseline, runtime,
                runtimeCandidateCount, watchdogPythonPath);
        }
    }

    internal sealed partial class RuntimeHostClient
    {
        internal GravityProductionExecutionOutcome ExecuteProduction(AuthorizedGravityExecutionPlan authorized, ProtectedBaseline baseline,
            OpenFlowsRuntimeCandidate runtime, int candidateCount, string pythonPath)
        {
            GravityExecutionPlanBuilder.Require(authorized != null, "TRUSTED_ENGINEERING_VERIFIER_UNAVAILABLE");
            var plan = authorized.Materialize(true);
            GravityEngineeringAuthorization.RequireRuntimeApproval(plan);
            return ExecuteProductionCore(() => authorized.Materialize(true), null, baseline, runtime, candidateCount, pythonPath);
        }
        internal GravityProductionExecutionOutcome ExecuteSingleOperator(SingleOperatorExecutionPermit permit, ProtectedBaseline baseline,
            OpenFlowsRuntimeCandidate runtime, int candidateCount, string pythonPath, Action<string> evidenceDirectoryReady = null)
        {
            GravityExecutionPlanBuilder.Require(permit != null && !permit.TestOnly, "OPERATOR_CONFIRMATION_REQUIRED");
            return ExecuteProductionCore(permit.Materialize, permit, baseline, runtime, candidateCount, pythonPath, evidenceDirectoryReady);
        }
        private GravityProductionExecutionOutcome ExecuteProductionCore(Func<GravityExecutionPlan> materialize, SingleOperatorExecutionPermit permit,
            ProtectedBaseline baseline, OpenFlowsRuntimeCandidate runtime, int candidateCount, string pythonPath, Action<string> evidenceDirectoryReady = null)
        {
            var plan = materialize();
            var outcome = new GravityProductionExecutionOutcome { Status = "BLOCKED_RUNTIME" };
            try
            {
                GravityExecutionPlanBuilder.Require(runtime != null && candidateCount > 0 && Path.IsPathRooted(pythonPath ?? "") &&
                    File.Exists(pythonPath), "WATCHDOG_CONFIGURATION");
                WorkingCopyIntegrity.RejectLinks(pythonPath);
                var working = ControlledWorkingCopy.Create(baseline); // Deliberately retained on every outcome.
                outcome.WorkingModelPath = working.PrimaryFilePath;
                var authorization = WriteAuthorizationBoundary.RequestAuthorization(baseline, working, GravityWriteAndSolverHost.ProductionOperation);
                GravityExecutionPlanBuilder.Require(authorization.IsAuthorized, "WORKING_COPY_AUTHORIZATION");
                var request = RuntimeHostProtocol.ScenarioIsolationRequest(runtime, candidateCount, working.PrimaryFilePath,
                    authorization.Token, baseline.Fingerprint.Value);
                request.Operation = GravityWriteAndSolverHost.ProductionOperation;
                request.EngineeringPlan = plan;
                request.Diagnostics["ProtectedBaselinePath"] = baseline.PrimaryFilePath;
                RuntimeHostProtocol.ValidateRequest(request, singleOperatorPermit: permit);
                string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "RuntimeHost", request.RunId);
                RuntimeHostProtocol.ControlledDirectory(directory, "RuntimeHost");
                Directory.CreateDirectory(directory);
                outcome.EvidenceDirectory = directory;
                evidenceDirectoryReady?.Invoke(directory); // Read-only UI observer; no change to runtime authorization or supervision.
                string requestPath = Path.Combine(directory, "request.json"), resultPath = Path.Combine(directory, "result.json");
                request.EngineeringPlan = materialize(); // Recheck authorization and submit only the frozen snapshot.
                permit?.WriteReceipt(directory, request);
                RuntimeHostProtocol.AtomicWrite(requestPath, request);
                string executable = Path.Combine(Path.GetDirectoryName(typeof(RuntimeHostClient).Assembly.Location), "SewerPilot.OpenFlows.RuntimeHost.exe");
                WorkingCopyIntegrity.RejectLinks(executable);
                GravityExecutionPlanBuilder.Require(File.Exists(executable), "RUNTIME_HOST_MISSING");
                RuntimeHostProtocol.AtomicWrite(Path.Combine(directory, "watchdog-config.json"), new {
                    executable, arguments = new[] { requestPath, resultPath } });
                string watchdog = Path.Combine(directory, "watchdog.py");
                using (var resource = typeof(RuntimeHostClient).Assembly.GetManifestResourceStream("SewerPilot.AcceptedRuntimeWatchdog.py"))
                using (var file = new FileStream(watchdog, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { if (resource == null) throw new InvalidDataException("Accepted watchdog resource missing."); resource.CopyTo(file); file.Flush(true); }
                var start = new ProcessStartInfo(pythonPath, "\"" + watchdog + "\"") {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                start.EnvironmentVariables.Remove("ATRIA_API_KEY");
                using (var supervisor = Process.Start(start))
                {
                    var stdout = supervisor.StandardOutput.ReadToEndAsync();
                    var stderr = supervisor.StandardError.ReadToEndAsync();
                    // The external accepted watchdog owns the 180-second runtime deadline and complete process tree.
                    // This bound covers watchdog startup/termination evidence; it never retries a runtime.
                    if (!supervisor.WaitForExit(195000))
                    {
                        supervisor.Kill();
                        bool terminated = supervisor.WaitForExit(5000);
                        throw new InvalidOperationException(terminated ? "P15_BLOCKED_WATCHDOG_SUPERVISOR_TIMEOUT" :
                            "P15_BLOCKED_WATCHDOG_COMPLETION_UNPROVEN");
                    }
                    RequireSupervisorOutput(new Task[] { stdout, stderr }, 5000);
                    File.WriteAllText(Path.Combine(directory, "controller-stdout.txt"), stdout.GetAwaiter().GetResult());
                    File.WriteAllText(Path.Combine(directory, "controller-stderr.txt"), stderr.GetAwaiter().GetResult());
                    GravityExecutionPlanBuilder.Require(supervisor.ExitCode == 0, "WATCHDOG_EXIT");
                }
                var execution = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(
                    File.ReadAllText(Path.Combine(directory, "execution.json")));
                string[] required = { "runtime_host_invocations", "pid", "elapsed_seconds", "remaining_runtime_processes",
                    "timeout_seconds", "timeout_triggered", "exit_code", "post_termination_check", "status" };
                GravityExecutionPlanBuilder.Require(required.All(execution.ContainsKey), "WATCHDOG_EVIDENCE_INCOMPLETE");
                outcome.RuntimeInvocations = Convert.ToInt32(execution["runtime_host_invocations"], CultureInfo.InvariantCulture);
                outcome.RuntimePid = Convert.ToInt32(execution["pid"], CultureInfo.InvariantCulture);
                outcome.RuntimeSeconds = Convert.ToDouble(execution["elapsed_seconds"], CultureInfo.InvariantCulture);
                RequireWatchdogTermination(execution);
                // Baseline assessment occurs only after the external supervisor and every owned runtime process exit.
                outcome.BaselineIntegrityVerified = baseline.VerifyCurrentIntegrity().IsMatch;
                RuntimeHostProtocol.AtomicWrite(Path.Combine(directory, "baseline-integrity.json"), new {
                    unchanged = outcome.BaselineIntegrityVerified, fingerprint_before = baseline.Fingerprint.Value,
                    entries_after = IntegritySnapshot.Capture(baseline.DirectoryPath, baseline.PrimaryFileName, baseline.FamilyRelativePaths).Entries.Values });
                GravityExecutionPlanBuilder.Require(outcome.BaselineIntegrityVerified == true, "PROTECTED_BASELINE_INTEGRITY");
                // A timed-out runtime may have no committed result. Preserve the actual watchdog cause first.
                RequireWatchdogNotTimedOut(execution);
                var result = RuntimeHostProtocol.ParseResult(resultPath, request);
                outcome.FieldWrites = result.MutationInvocationCount; outcome.Saves = result.SaveInvocationCount;
                outcome.SolverRuns = result.SolverRunInvocationCount;
                outcome.ComputeStatus = result.Diagnostics.TryGetValue("ScenarioComputeStatus", out string status) ? status : "NotRequested";
                if (!result.Success) throw new InvalidOperationException(result.FailureClassification + ": " + result.FailureMessageSafe);
                GravityExecutionPlanBuilder.Require(outcome.RuntimeInvocations == 1 && Convert.ToInt32(execution["timeout_seconds"]) == 180 &&
                    Convert.ToInt32(execution["exit_code"]) == 0 &&
                    (string)execution["status"] == "RUNTIME_COMPLETED" &&
                    CommittedMarker(Path.Combine(directory, "stages.jsonl"), outcome.RuntimePid.Value), "RUNTIME_EXECUTION");
                GravityWriteAndSolverHost.ValidateProductionResult(request, result);
                if (plan.Calculate && result.Diagnostics.TryGetValue("ProductionReadbackVerified", out string prv) && prv == "true")
                {
                    outcome.ContentAuthority = P16ResultContentAuthority.CaptureCompletedProductionResult(request, result);
                    if (result.Diagnostics.TryGetValue("HydraulicResultSetJson", out string json) && !string.IsNullOrEmpty(json))
                    {
                        outcome.HydraulicResults = outcome.ContentAuthority.CreateResultSet(
                            request.WorkingCopyId,
                            plan.NetworkFingerprint,
                            plan.PlanFingerprint);
                    }
                }
                outcome.Status = "PASS";
            }
            catch (Exception error)
            {
                outcome.Blocker = error.ToString();
                if (outcome.EvidenceDirectory != null)
                    try { RuntimeHostProtocol.AtomicWrite(Path.Combine(outcome.EvidenceDirectory, "production-failure.json"), new { outcome.Blocker }); }
                    catch (Exception persistenceError) { outcome.Blocker += "\nFailure evidence persistence: " + persistenceError; }
            }
            return outcome;
        }
        internal static void RequireSupervisorOutput(Task[] output, int milliseconds)
        {
            GravityExecutionPlanBuilder.Require(milliseconds >= 0 && milliseconds <= 5000 &&
                Task.WaitAll(output, milliseconds), "WATCHDOG_OUTPUT_INCOMPLETE");
        }
        internal static void RequireWatchdogTermination(Dictionary<string, object> execution)
        {
            GravityExecutionPlanBuilder.Require(execution.TryGetValue("remaining_runtime_processes", out object remaining) &&
                execution.TryGetValue("post_termination_check", out object rawTree) && rawTree is Dictionary<string, object> &&
                ((Dictionary<string, object>)rawTree).TryGetValue("active_processes", out object active) &&
                Convert.ToInt32(remaining) == 0 && Convert.ToInt32(active) == 0, "PROCESS_TERMINATION_UNPROVEN");
        }
        internal static void RequireWatchdogNotTimedOut(Dictionary<string, object> execution) =>
            GravityExecutionPlanBuilder.Require(execution.TryGetValue("timeout_triggered", out object flag) &&
                flag is bool timedOut && !timedOut, "RUNTIME_WATCHDOG_TIMEOUT");
    }
}
