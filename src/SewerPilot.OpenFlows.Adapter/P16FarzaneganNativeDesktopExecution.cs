using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    // A qualified second-model Desktop path. The generic engineering Execute authorization
    // is deliberately not reused or bypassed by this explicitly confirmed native-only action.
    internal static class P16FarzaneganNativeDesktopExecution
    {
        internal const string QualifiedSourceSha256 = "CCF07288020064F25F9125224CF6CEF0FBD3085D87E85DE173FB3D2BBC652E5B";
        private static int active;
        internal static bool MatchesName(string path) =>
            !string.IsNullOrWhiteSpace(path) &&
            string.Equals(Path.GetFileName(path), P16FarzaneganNativePilotHost.ModelFileName, StringComparison.OrdinalIgnoreCase);

        internal static void ValidateSource(string path)
        {
            if (!MatchesName(path)) throw new InvalidDataException("This native route is qualified only for the tested Farzanegan model.");
            path = Path.GetFullPath(path);
            WorkingCopyIntegrity.RejectLinks(path);
            if (!File.Exists(path) || WorkingCopyIntegrity.Fingerprint(path).SHA256 != QualifiedSourceSha256)
                throw new InvalidDataException("Farzanegan source identity is not the qualified seven-file model.");
            string directory = Path.GetDirectoryName(path);
            foreach (string name in P16FarzaneganNativePilotHost.Family)
            {
                string file = Path.Combine(directory, name);
                WorkingCopyIntegrity.RejectLinks(file);
                if (!File.Exists(file)) throw new FileNotFoundException("Missing Farzanegan model companion: " + name);
            }
        }

        private static string Json(object obj) => RuntimeHostProtocol.Serializer().Serialize(obj);
        private static void Write(string folder, string name, string value) =>
            File.WriteAllText(Path.Combine(folder, name), value);

        private static HostResult RunHost(HostRequest request, string launchSwitch, string executable,
            string evidence, string phase, int timeoutMs)
        {
            string ipc = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "RuntimeHost", request.RunId);
            RuntimeHostProtocol.ControlledDirectory(ipc, "RuntimeHost");
            if (Directory.Exists(ipc)) throw new IOException("RuntimeHost IPC directory collision.");
            Directory.CreateDirectory(ipc);
            string requestPath = Path.Combine(ipc, "request.json");
            string resultPath = Path.Combine(ipc, "result.json");
            RuntimeHostProtocol.AtomicWrite(requestPath, request);
            Write(evidence, phase + "request.json", Json(request));
            var start = new ProcessStartInfo(executable,
                launchSwitch + " \"" + requestPath + "\" \"" + resultPath + "\"")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable) };
            int exit;
            using (var proc = Process.Start(start))
            {
                if (proc == null) throw new InvalidOperationException("Native RuntimeHost could not start.");
                int pid = proc.Id;
                if (!proc.WaitForExit(timeoutMs))
                {
                    OwnedProcessTerminator.TryTerminate(proc, pid);
                    throw new TimeoutException("The bounded Farzanegan " + phase + " host timed out; no retry.");
                }
                exit = proc.ExitCode;
            }
            string stages = Path.Combine(ipc, "stages.jsonl");
            if (File.Exists(stages)) File.Copy(stages, Path.Combine(evidence, phase + "stages.jsonl"));
            if (!File.Exists(resultPath)) throw new InvalidDataException("Native RuntimeHost result is missing.");
            Write(evidence, phase + "result.json", File.ReadAllText(resultPath));
            var result = RuntimeHostProtocol.ParseResult(resultPath, request);
            if (exit != 0 || !result.Success)
                throw new InvalidDataException("Native " + phase + " was not accepted (exit " + exit + ").");
            return result;
        }

        internal static P16NativeDesktopExecution.Receipt Execute(string source, Action<string> progress = null)
        {
            if (Interlocked.Exchange(ref active, 1) != 0)
                throw new InvalidOperationException("A Farzanegan native calculation is already active.");
            try
            {
                ValidateSource(source);
                source = Path.GetFullPath(source);
                string[] originals = P16FarzaneganNativePilotHost.Family.Select(
                    n => Path.Combine(Path.GetDirectoryName(source), n)).ToArray();
                string[] before = P16AzadeganCalculationProof.Manifest(originals).ToArray();
                string evidence = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SewerPilot", "Evidence", "Farzanegan", DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(evidence)) throw new IOException("Evidence directory collision.");
                Directory.CreateDirectory(evidence);
                Write(evidence, "source-family-before.txt", string.Join(Environment.NewLine, before));
                progress?.Invoke("Farzanegan: verifying runtime and preparing a disposable seven-file copy.");
                var working = WorkingCopyIntegrity.Create(source, originals, "Working");
                string workingId = Path.GetFileName(working.DirectoryPath);
                var options = new OpenFlowsDiscoveryOptions(Environment.GetEnvironmentVariable("SEWERPILOT_OPENFLOWS_HOME"));
                var selection = new OpenFlowsRuntimeSelector().Select(
                    new OpenFlowsRuntimeDiscovery(new WindowsRuntimeLocationSource(),
                        new InstalledRuntimeInspector()).Discover(options), options);
                if (selection.Status != OpenFlowsRuntimeSelectionStatus.Selected || selection.Candidate == null)
                    throw new InvalidOperationException("No unambiguous installed Bentley runtime.");
                string runtime = selection.Candidate.InstallRoot;
                string host = P16AzadeganCalculationProof.DefaultRuntimeHostExecutable();
                if (string.IsNullOrEmpty(host) || !File.Exists(host))
                    throw new FileNotFoundException("The compiled Native RuntimeHost is unavailable.");
                var core = OpenFlowsRuntimeCandidate.CoreAssemblyNames.Select(n => {
                    string p = Path.Combine(runtime, n + ".dll");
                    return new HostMetadata { SimpleName = n, Path = p,
                        SHA256 = WorkingCopyIntegrity.Fingerprint(p).SHA256 };
                }).ToArray();
                string inputDigest = P16AzadeganCalculationHost.InventoryDigest(
                    P16AzadeganCalculationHost.Inventory(working.DirectoryPath));
                var calculate = new HostRequest {
                    SchemaVersion = 1, RunId = Guid.NewGuid().ToString("N"),
                    Operation = P16FarzaneganNativePilotHost.Operation,
                    WorkingCopyId = workingId, WorkingModelPath = working.ModelPath,
                    RuntimeCandidateCount = 1, DetectedArchitecture = "x64", SelectedRuntime = runtime,
                    CoreFiles = core,
                    Diagnostics = new Dictionary<string, string>(StringComparer.Ordinal) {
                        ["FarzaneganPilotCopyId"] = workingId, ["FarzaneganPilotScenario"] = "1",
                        ["FarzaneganPilotOptions"] = "29", ["FarzaneganPilotEngine"] = P16FarzaneganNativePilotHost.Engine,
                        ["FarzaneganPilotInputSHA256"] = inputDigest
                    }
                };
                P16FarzaneganNativePilotHost.ValidateRequest(calculate);
                progress?.Invoke("Farzanegan: one native solver run and at most one save on the disposable copy.");
                var calcResult = RunHost(calculate, P16FarzaneganNativePilotHost.LaunchSwitch,
                    host, evidence, "calculate-", 90000);
                if (!calcResult.Diagnostics.TryGetValue(P16FarzaneganNativePilotHost.EvidenceKey, out string calcJson))
                    throw new InvalidDataException("Native calculation observation is absent.");
                Write(evidence, "calculation-observation.json", calcJson);
                var calc = RuntimeHostProtocol.Serializer().Deserialize<P16FarzaneganNativePilotHost.Observation>(calcJson);
                if (calc == null || !calc.SaveSucceeded || calc.SolverRunInvocations != 1 || calc.SaveInvocations != 1 ||
                    calc.ScenarioComputeStatus != "Success" || calc.SaveResultClassification != "PERSISTED" ||
                    (calc.Status != "CALCULATED_SAVED_AWAITING_REOPEN" &&
                     calc.Status != "CALCULATED_SAVED_WITH_WARNINGS_AWAITING_REOPEN") ||
                    string.IsNullOrEmpty(calc.CalculationEvent) ||
                    !P16SaveQuestionPolicy.SamePath(calc.WorkingModel, working.ModelPath))
                    throw new InvalidDataException("Farzanegan one-run persisted calculation proof was rejected.");
                string inventory = P16AzadeganCalculationHost.InventoryJson(
                    P16AzadeganCalculationHost.Inventory(working.DirectoryPath));
                if (inventory != calc.OutputInventory) throw new InvalidDataException("Persisted output inventory mismatch.");
                if (!before.SequenceEqual(P16AzadeganCalculationProof.Manifest(originals)))
                    throw new InvalidDataException("Protected Farzanegan source changed during calculation.");
                var savedFiles = P16FarzaneganNativePilotHost.Family.Select(
                    n => Path.Combine(working.DirectoryPath, n)).ToArray();
                progress?.Invoke("Farzanegan: independent zero-solver, zero-save reopen/readback.");
                var readCopy = WorkingCopyIntegrity.Create(working.ModelPath, savedFiles, "ReadOnly");
                var readInventory = P16AzadeganCalculationHost.Inventory(readCopy.DirectoryPath);
                if (P16AzadeganCalculationHost.InventoryJson(readInventory) != inventory)
                    throw new InvalidDataException("Separate read copy differs from saved calculation.");
                string readId = Path.GetFileName(readCopy.DirectoryPath);
                var read = new HostRequest {
                    SchemaVersion = 1, RunId = Guid.NewGuid().ToString("N"),
                    Operation = P16FarzaneganNativeReadHost.Operation,
                    WorkingModelPath = readCopy.ModelPath, WorkingCopyId = readId,
                    RuntimeCandidateCount = 1, DetectedArchitecture = "x64", SelectedRuntime = runtime,
                    CoreFiles = core,
                    Diagnostics = new Dictionary<string, string>(StringComparer.Ordinal) {
                        ["FarzaneganReadCopyId"] = readId, ["FarzaneganReadScenario"] = "1",
                        ["FarzaneganReadOptions"] = "29", ["FarzaneganReadEngine"] = P16FarzaneganNativePilotHost.Engine,
                        ["FarzaneganReadEvent"] = calc.CalculationEvent,
                        ["FarzaneganReadOutputInventory"] = inventory,
                        ["FarzaneganReadInputSHA256"] = P16AzadeganCalculationHost.InventoryDigest(readInventory)
                    }
                };
                P16FarzaneganNativeReadHost.ValidateRequest(read);
                Write(evidence, "read-preflight.txt", "CALC_EVENT=" + calc.CalculationEvent +
                    "\r\nSOLVER_CALLS_THIS_READ=0\r\nSAVE_CALLS_THIS_READ=0");
                var readResult = RunHost(read, P16FarzaneganNativeReadHost.LaunchSwitch,
                    host, evidence, "", 60000);
                if (!readResult.Diagnostics.TryGetValue(P16FarzaneganNativeReadHost.EvidenceKey, out string readJson))
                    throw new InvalidDataException("Native readback observation is absent.");
                Write(evidence, "read-observation.json", readJson);
                if (P16AzadeganCalculationHost.InventoryJson(
                        P16AzadeganCalculationHost.Inventory(working.DirectoryPath)) != inventory ||
                    !before.SequenceEqual(P16AzadeganCalculationProof.Manifest(originals)))
                    throw new InvalidDataException("Saved/source family changed during independent readback.");
                Write(evidence, "source-family-after.txt",
                    string.Join(Environment.NewLine, P16AzadeganCalculationProof.Manifest(originals)));
                var accepted = ReadReceipt(evidence);
                Write(evidence, "desktop-acceptance.txt", "STATUS=" + accepted.Status +
                    "\r\nFLOW=" + accepted.Flow + "\r\nCALCULATION_EVENT=" + accepted.EventId +
                    "\r\nSOURCE_FAMILY_UNCHANGED=true\r\nREAD_SOLVER_INVOCATIONS=0\r\nREAD_SAVE_INVOCATIONS=0");
                return new P16NativeDesktopExecution.Receipt {
                    EvidenceDirectory = evidence, WorkingModelPath = working.ModelPath, View = accepted
                };
            }
            finally { Interlocked.Exchange(ref active, 0); }
        }

        internal static P16VerifiedReadbackReceipt.View ReadReceipt(string directory)
        {
            directory = Path.GetFullPath(directory);
            WorkingCopyIntegrity.RejectLinks(directory);
            string[] names = { "calculate-request.json", "calculate-result.json", "calculation-observation.json",
                "request.json", "result.json", "read-observation.json" };
            foreach (string name in names)
            {
                string path = Path.Combine(directory, name);
                WorkingCopyIntegrity.RejectLinks(path);
                if (!File.Exists(path) || new FileInfo(path).Length > 8 * 1024 * 1024)
                    throw new InvalidDataException("Incomplete or oversized Farzanegan receipt: " + name);
            }
            var serializer = RuntimeHostProtocol.Serializer();
            var calcRequest = serializer.Deserialize<HostRequest>(File.ReadAllText(Path.Combine(directory, names[0])));
            var readRequest = serializer.Deserialize<HostRequest>(File.ReadAllText(Path.Combine(directory, names[3])));
            P16FarzaneganNativePilotHost.ValidateRequest(calcRequest);
            P16FarzaneganNativeReadHost.ValidateRequest(readRequest);
            var calculated = RuntimeHostProtocol.ParseResult(Path.Combine(directory, names[1]), calcRequest);
            var reopened = RuntimeHostProtocol.ParseResult(Path.Combine(directory, names[4]), readRequest);
            if (!calculated.Success || !reopened.Success)
                throw new InvalidDataException("Both native hosts must have accepted their results.");
            var calc = serializer.Deserialize<P16FarzaneganNativePilotHost.Observation>(
                File.ReadAllText(Path.Combine(directory, names[2])));
            var seen = serializer.Deserialize<P16FarzaneganNativeReadHost.Observation>(
                File.ReadAllText(Path.Combine(directory, names[5])));
            if (calc == null || seen == null || !calc.SaveSucceeded ||
                calc.SaveResultClassification != "PERSISTED" || calc.SolverRunInvocations != 1 ||
                calc.SaveInvocations != 1 || calc.ScenarioComputeStatus != "Success" ||
                (calc.Status != "CALCULATED_SAVED_AWAITING_REOPEN" &&
                 calc.Status != "CALCULATED_SAVED_WITH_WARNINGS_AWAITING_REOPEN") ||
                seen.Status != "FARZANEGAN_NATIVE_FLOW_READBACK_VERIFIED" ||
                !seen.HasResults || !seen.FieldHasResults || !seen.ResultCleanupCompleted ||
                seen.ResultReadInvocations != 1 || seen.ScenarioId != 1 || seen.OptionsId != 29 ||
                seen.Engine != P16FarzaneganNativePilotHost.Engine ||
                seen.Field != P16FarzaneganNativeReadHost.Field ||
                seen.Record != P16FarzaneganNativeReadHost.Record ||
                seen.ElementId != P16FarzaneganNativePilotHost.FirstConduitId ||
                string.IsNullOrWhiteSpace(seen.ElementLabel) || seen.TimeSeconds != 0 ||
                string.IsNullOrEmpty(calc.CalculationEvent) || calc.CalculationEvent != seen.CalculationEvent ||
                calc.CalculationEvent != readRequest.Diagnostics["FarzaneganReadEvent"] ||
                seen.InputInventory != calc.OutputInventory ||
                calc.OutputInventory != readRequest.Diagnostics["FarzaneganReadOutputInventory"] ||
                !P16SaveQuestionPolicy.SamePath(calc.WorkingModel, calcRequest.WorkingModelPath) ||
                !P16SaveQuestionPolicy.SamePath(seen.ModelPath, readRequest.WorkingModelPath) ||
                !string.Equals(seen.UnitSymbol, "ft3/s", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(seen.Provenance) ||
                !double.TryParse(seen.RawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double flow) ||
                double.IsNaN(flow) || double.IsInfinity(flow))
                throw new InvalidDataException("Farzanegan same-run native result identity/lineage failed.");
            string currentSaved = P16AzadeganCalculationHost.InventoryJson(
                P16AzadeganCalculationHost.Inventory(Path.GetDirectoryName(calc.WorkingModel)));
            string currentRead = P16AzadeganCalculationHost.InventoryJson(
                P16AzadeganCalculationHost.Inventory(Path.GetDirectoryName(seen.ModelPath)));
            if (currentSaved != calc.OutputInventory || currentRead != calc.OutputInventory ||
                calculated.Diagnostics[P16FarzaneganNativePilotHost.EvidenceKey] !=
                    File.ReadAllText(Path.Combine(directory, names[2])) ||
                reopened.Diagnostics[P16FarzaneganNativeReadHost.EvidenceKey] !=
                    File.ReadAllText(Path.Combine(directory, names[5])))
                throw new InvalidDataException("Receipt observation or persisted saved/read bytes changed.");
            var summary = new StringBuilder();
            summary.AppendLine("VERIFIED NATIVE FARZANEGAN READBACK | same-run disposable model");
            summary.AppendLine("Status: " + seen.Status);
            summary.AppendLine("Conduit: " + seen.ElementLabel + " (ID " + seen.ElementId + ")");
            summary.AppendLine("FLOW: " + seen.RawValue + " " + seen.UnitSymbol);
            summary.AppendLine("Time: " + seen.TimeSeconds.Value.ToString("R", CultureInfo.InvariantCulture) + " s");
            summary.AppendLine("Scenario: " + seen.ScenarioId + " | Options: " + seen.OptionsId);
            summary.AppendLine("Engine: " + seen.Engine);
            summary.AppendLine("Field / record: " + seen.Field + " / " + seen.Record);
            summary.AppendLine("Freshness: VERIFIED | Calculation event: " + seen.CalculationEvent);
            summary.AppendLine("Native read: 0 solve / 0 save | HasResults: " + seen.HasResults);
            summary.AppendLine("Persisted saved/read inventories: MATCHED");
            summary.AppendLine("Bentley notifications: " + (calc.Notifications == null ? "UNKNOWN" :
                calc.Notifications.Length.ToString(CultureInfo.InvariantCulture)) + " (not engineering approval)");
            foreach (var notice in calc.Notifications ?? new P16AzadeganEngineNotification[0])
                summary.AppendLine("  [" + notice.Level + " #" + notice.MessageId + "] " +
                    (string.IsNullOrEmpty(notice.Message) ? notice.MessageKey : notice.Message));
            summary.AppendLine("Working model: " + calc.WorkingModel);
            summary.AppendLine("PROVENANCE: " + seen.Provenance);
            return new P16VerifiedReadbackReceipt.View {
                Summary = summary.ToString(), Status = seen.Status,
                Flow = seen.RawValue + " " + seen.UnitSymbol, EventId = seen.CalculationEvent
            };
        }
    }
}
