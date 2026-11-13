using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    internal interface IRuntimeHostProcess : IDisposable
    {
        int Id { get; }
        int ExitCode { get; }
        bool WaitForExit(int milliseconds);
        void Kill();
    }
    internal interface IRuntimeHostLauncher
    { IRuntimeHostProcess Start(string executable, string request, string result, string journal); }
    internal sealed class RuntimeHostProcess : IRuntimeHostProcess
    {
        private readonly Process process;
        internal RuntimeHostProcess(Process process) { this.process = process; }
        public int Id => process.Id;
        public int ExitCode => process.ExitCode;
        public bool WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);
        public void Kill() { process.Kill(); } // Only the Process handle returned by Start.
        public void Dispose() { process.Dispose(); }
    }
    internal sealed class RuntimeHostLauncher : IRuntimeHostLauncher
    {
        public IRuntimeHostProcess Start(string executable, string request, string result, string journal)
        {
            var start = new ProcessStartInfo(executable, "\"" + request + "\" \"" + result + "\"") {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable) };
            // A workstation can define the same variable under more than one casing (for example
            // HTTPS_PROXY and https_proxy, which corporate proxy setups commonly do). Windows
            // treats those as a single variable, but the framework copies the inherited
            // environment into a case-insensitive collection and throws when it meets the second
            // casing, so the supervised host could never be started. Normalise our own process
            // environment for the duration of the launch and restore it afterwards.
            Action restore = SuppressDuplicateVariableCasing();
            try
            {
                start.EnvironmentVariables[RuntimeStageJournal.EnvironmentName] = journal;
                start.EnvironmentVariables.Remove("ATRIA_API_KEY");
                return new RuntimeHostProcess(Process.Start(start));
            }
            finally { restore(); }
        }

        // Removes the later casing of any variable defined more than once, returning an action
        // that restores exactly what was removed. The uppercase name is kept deterministically.
        // Internal so every host launch path (including the general calculation service) applies
        // exactly the same de-duplication rule.
        internal static Action SuppressDuplicateVariableCasing()
        {
            var variables = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
            var names = new System.Collections.Generic.List<string>();
            foreach (System.Collections.DictionaryEntry entry in variables)
                names.Add((string)entry.Key);
            names.Sort(StringComparer.Ordinal);

            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removed = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();
            foreach (string name in names)
            {
                if (seen.Add(name)) continue;
                object value = variables[name];
                removed.Add(new System.Collections.Generic.KeyValuePair<string, string>(name, value == null ? null : value.ToString()));
                Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
            }
            if (removed.Count == 0) return () => { };
            return () =>
            {
                foreach (var item in removed)
                    Environment.SetEnvironmentVariable(item.Key, item.Value, EnvironmentVariableTarget.Process);
            };
        }
    }
    internal sealed class HostReadOutcome
    {
        internal ModelReadEvidence Evidence = new ModelReadEvidence();
        internal OpenFlowsBindingResult Binding = new OpenFlowsBindingResult(false, new OpenFlowsAssemblyMetadata[0]);
    }
    internal sealed partial class RuntimeHostClient
    {
        private readonly IRuntimeHostLauncher launcher;
        internal RuntimeHostClient(IRuntimeHostLauncher launcher = null) { this.launcher = launcher ?? new RuntimeHostLauncher(); }
        internal static bool Supervise(IRuntimeHostProcess child, ModelReadEvidence evidence)
        {
            if (child.WaitForExit(120000)) return true;
            evidence.Diagnostics["RuntimeHostWatchdogTermination"] = "true";
            child.Kill();
            return child.WaitForExit(5000); // Bounded termination acknowledgement, no retry.
        }
        internal HostReadOutcome Read(HostRequest request)
        {
            RuntimeHostProtocol.ValidateRequest(request);
            string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "RuntimeHost", request.RunId);
            RuntimeHostProtocol.ControlledDirectory(directory, "RuntimeHost");
            Directory.CreateDirectory(directory);
            string requestPath = Path.Combine(directory, "request.json"), resultPath = Path.Combine(directory, "result.json"),
                journal = Path.Combine(directory, "stages.jsonl");
            RuntimeHostProtocol.AtomicWrite(requestPath, request);
            var outcome = new HostReadOutcome();
            var evidence = outcome.Evidence;
            string executable = Path.Combine(Path.GetDirectoryName(typeof(RuntimeHostClient).Assembly.Location), "SewerPilot.OpenFlows.RuntimeHost.exe");
            evidence.Diagnostics["RuntimeHostExecutable"] = executable;
            evidence.Diagnostics["RuntimeHostIpcDirectory"] = directory;
            evidence.Diagnostics["RuntimeHostWatchdogTermination"] = "false";
            evidence.Diagnostics["RuntimeHostResultValid"] = "false";
            evidence.Diagnostics["RuntimeHostResultCommitted"] = "false";
            evidence.Diagnostics["RuntimeHostProcessTerminated"] = "false";
            var timer = Stopwatch.StartNew();
            try
            {
                WorkingCopyIntegrity.RejectLinks(executable);
                if (!File.Exists(executable)) throw new FileNotFoundException("Production RuntimeHost executable is missing.");
                using (var child = launcher.Start(executable, requestPath, resultPath, journal))
                {
                    evidence.Diagnostics["RuntimeHostPID"] = child.Id.ToString();
                    evidence.Diagnostics["RuntimeHostStarted"] = "true";
                    evidence.Diagnostics["RuntimeHostStartedAtUtc"] = DateTime.UtcNow.ToString("o");
                    bool terminated = Supervise(child, evidence);
                    evidence.Diagnostics["RuntimeHostProcessTerminated"] = terminated ? "true" : "false";
                    if (!terminated) { evidence.FailureCode = "P4_R9_BLOCKED_PROCESS_TERMINATION"; return outcome; }
                    evidence.Diagnostics["RuntimeHostExitCode"] = child.ExitCode.ToString();
                    evidence.Diagnostics["RuntimeHostResultPresent"] = File.Exists(resultPath) ? "true" : "false";
                    CaptureLastStages(journal, evidence);
                    evidence.Diagnostics["RuntimeHostResultCommitted"] = CommittedMarker(journal, child.Id) ? "true" : "false";
                    if (evidence.Diagnostics["RuntimeHostWatchdogTermination"] == "true")
                    { evidence.FailureCode = TimeoutClassification(evidence); return outcome; }
                    HostResult result;
                    try { result = RuntimeHostProtocol.ParseResult(resultPath, request); }
                    catch (Exception error)
                    {
                        evidence.FailureCode = child.ExitCode != 0 ? "P4_R9_BLOCKED_VENDOR_RUNTIME" : "P4_R9_BLOCKED_RESULT_INVALID";
                        evidence.Exception = (evidence.Exception ?? "") + "\n" + error; return outcome;
                    }
                    if (!CommittedMarker(journal, child.Id)) throw new InvalidDataException("Durable ResultCommitted journal marker is missing.");
                    foreach (var item in result.Diagnostics)
                        if (!item.Key.StartsWith("RuntimeHost", StringComparison.Ordinal)) evidence.Diagnostics[item.Key] = item.Value;
                    evidence.Diagnostics["RuntimeHostResultValid"] = "true";
                    evidence.Diagnostics["RuntimeHostResultCommitted"] = "true";
                    evidence.Diagnostics["RuntimeHostStaCompleted"] = result.StaCompleted ? "true" : "false";
                    evidence.Diagnostics["RuntimeHostExitMode"] = result.RequestedExitMode;
                    evidence.RuntimeSelected = true;
                    evidence.AssembliesBound = result.RuntimeBinding;
                    evidence.LifecycleInitialized = result.LifecycleInitialized;
                    evidence.OpenSucceeded = result.ModelOpen;
                    evidence.CurrentGraphicalProjectAvailable = result.CurrentGraphicalProjectEstablished;
                    evidence.DomainProjectAvailable = result.DomainProjectEstablished;
                    evidence.DomainDataSetAvailable = result.DomainDataSetEstablished;
                    evidence.FirstConduitElementID = result.DatumValue;
                    evidence.CloseCompleted = result.VendorCleanupCompleted && result.StaCompleted;
                    evidence.LoadedAssemblies = result.LoadedAssemblies.Select(item => new FileFingerprint(item.Path, item.SHA256)).ToList();
                    if (result.RuntimeBinding)
                        outcome.Binding = new OpenFlowsBindingResult(true, result.BoundCoreFiles.Select(item => item.ToMetadata()));
                    if (!result.Success || child.ExitCode != 0)
                    { evidence.FailureCode = result.FailureClassification ?? "P4_R9_BLOCKED_VENDOR_RUNTIME"; evidence.Exception = result.FailureMessageSafe; }
                }
            }
            catch (Exception error)
            { evidence.FailureCode = evidence.Diagnostics.ContainsKey("RuntimeHostStarted") ? "P4_R9_BLOCKED_RESULT_INVALID" : "P4_R9_BLOCKED_RUNTIME_HOST_START"; evidence.Exception = error.ToString(); }
            finally { evidence.Diagnostics["RuntimeHostElapsedSeconds"] = timer.Elapsed.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture); }
            return outcome;
        }
        private static System.Collections.Generic.IEnumerable<System.Collections.Generic.Dictionary<string, object>> JournalEntries(string path)
        {
            if (!File.Exists(path)) yield break;
            foreach (string line in File.ReadLines(path))
            {
                System.Collections.Generic.Dictionary<string, object> entry;
                try { entry = RuntimeHostProtocol.Serializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(line); }
                catch (ArgumentException) { continue; } // A killed writer may leave an incomplete final line.
                if (entry != null) yield return entry;
            }
        }
        internal static bool CommittedMarker(string path, int pid) => JournalEntries(path).Any(entry => {
            return (string)entry["stage_name"] == "ResultCommitted" && (string)entry["state"] == "EXIT" && Convert.ToInt32(entry["process_id"]) == pid;
        });
        private static void CaptureLastStages(string path, ModelReadEvidence evidence)
        {
            if (!File.Exists(path)) return;
            foreach (var entry in JournalEntries(path))
            {
                if ((string)entry["state"] == "EXIT") evidence.Diagnostics["RuntimeHostLastCompletedStage"] = (string)entry["stage_name"];
                if ((string)entry["state"] == "ENTER") evidence.Diagnostics["RuntimeHostLastEnteredStage"] = (string)entry["stage_name"];
                if (entry.ContainsKey("exception") && entry["exception"] != null) evidence.Exception = (string)entry["exception"];
            }
        }
        private static string TimeoutClassification(ModelReadEvidence evidence)
        {
            string stage;
            if (!evidence.Diagnostics.TryGetValue("RuntimeHostLastEnteredStage", out stage)) return "P4_R9_BLOCKED_RUNTIME_HOST_START";
            if (stage.Contains("OpenFileWithSpecifiedExtension")) return "P4_R9_BLOCKED_MODEL_OPEN";
            if (stage.Contains("Close") || stage.Contains("Cleanup") || stage.Contains("Dispose")) return "P4_R9_BLOCKED_VENDOR_CLEANUP";
            if (stage.Contains("Result")) return "P4_R9_BLOCKED_RESULT_COMMIT";
            if (stage.Contains("Exit")) return "P4_R9_BLOCKED_PROCESS_TERMINATION";
            evidence.Diagnostics["RuntimeHostNonreturnBoundary"] = stage;
            return "P4_R9_BLOCKED_VENDOR_RUNTIME";
        }
    }
}
