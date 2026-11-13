using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    // A single desktop-triggered P16 CALCULATE -> persisted disposable copy -> separate READ -> UI receipt.
    // This is deliberately bounded to the one verified Azadegan model contract, not a generic SewerGEMS
    // calculation adapter; no engineering design approval is made.
    internal static class P16NativeDesktopExecution
    {
        internal sealed class Receipt
        {
            internal string EvidenceDirectory;
            internal string WorkingModelPath;
            internal P16VerifiedReadbackReceipt.View View;
        }

        private static int active;

        internal static void ValidateSource(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Open a protected Bentley model first.");
            var actual = Path.GetFullPath(path);
            WorkingCopyIntegrity.RejectLinks(actual);
            if (!string.Equals(Path.GetFileName(actual), P16AzadeganReadOnlyResultHost.ModelFileName,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Native P16 is currently qualified only for the verified Azadegan model family.");
            string directory = Path.GetDirectoryName(actual);
            foreach (string fileName in P16AzadeganCalculationHost.Family)
            {
                string full = Path.Combine(directory, fileName);
                WorkingCopyIntegrity.RejectLinks(full);
                if (!File.Exists(full)) throw new FileNotFoundException("Missing required model-family file: " + fileName);
            }
        }

        private static string RuntimeRoot()
        {
            var options = new OpenFlowsDiscoveryOptions(Environment.GetEnvironmentVariable("SEWERPILOT_OPENFLOWS_HOME"));
            var candidates = new OpenFlowsRuntimeDiscovery(new WindowsRuntimeLocationSource(),
                new InstalledRuntimeInspector(path => new MetadataWorker().Inspect(path))).Discover(options);
            var selected = new OpenFlowsRuntimeSelector().Select(candidates, options);
            if (selected.Status != OpenFlowsRuntimeSelectionStatus.Selected || selected.Candidate == null)
                throw new InvalidOperationException("No unambiguous installed Bentley runtime was selected: " + selected.Reason);
            return selected.Candidate.InstallRoot;
        }

        private static string RuntimeHostExe()
        {
            var sibling = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SewerPilot.OpenFlows.RuntimeHost.exe");
            if (File.Exists(sibling)) return sibling;
            var candidate = P16AzadeganCalculationProof.DefaultRuntimeHostExecutable();
            if (candidate == null || !File.Exists(candidate))
                throw new FileNotFoundException("Native RuntimeHost executable is unavailable.");
            return candidate;
        }

        private static void Write(string path, string content)
        {
            if (content != null) File.WriteAllText(path, content);
        }

        private static string Json(object value) => RuntimeHostProtocol.Serializer().Serialize(value);

        private static void AddLineage(HostRequest readRequest, P16AzadeganCalculationObservation calc)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["P16AzadeganCalculationEventId"] = calc.CalculationEventId,
                ["P16AzadeganSolverStart"] = calc.SolverStartTimestamp,
                ["P16AzadeganSolverEnd"] = calc.SolverEndTimestamp,
                ["P16AzadeganSaveTimestamp"] = calc.SaveTimestamp,
                ["P16AzadeganInputInventory"] = calc.InputInventoryJson,
                ["P16AzadeganOutputInventory"] = calc.OutputInventoryJson,
                ["P16AzadeganCreatedFiles"] = Json(calc.CreatedFiles ?? new string[0]),
                ["P16AzadeganModifiedFiles"] = Json(calc.ModifiedFiles ?? new string[0]),
                ["P16AzadeganOptionsId"] = calc.CalculationOptionsId?.ToString(CultureInfo.InvariantCulture),
                ["P16AzadeganResolvedEngine"] = calc.ResolvedEngine,
                ["P16AzadeganMainEngine"] = calc.MainEngineFromModel ?? "",
                ["P16AzadeganSelectionBasis"] = calc.EngineSelectionBasis,
                ["P16AzadeganEngineOutcome"] = calc.EngineOutcome,
                ["P16AzadeganNotifications"] = Json(calc.Notifications ?? new P16AzadeganEngineNotification[0]),
                ["P16AzadeganNotificationLevels"] = calc.NotificationLevelSummary ?? "",
                ["P16AzadeganNotificationLimitation"] = calc.NotificationCaptureLimitation ?? ""
            };
            foreach (var pair in values)
            {
                if (pair.Value == null || pair.Value.Length > 64 * 1024)
                    throw new InvalidDataException("P16 result lineage is incomplete or oversized: " + pair.Key);
                readRequest.Diagnostics.Add(pair.Key, pair.Value);
            }
        }

        private static HostResult ReadSavedOnce(HostRequest calculateRequest,
            P16AzadeganCalculationObservation calc, string evidenceDirectory,
            string hostExecutable, Action<string> update)
        {
            if (calculateRequest == null || calc == null)
                throw new InvalidDataException("P16 calculation request/observation missing.");
            var read = new HostRequest
            {
                SchemaVersion = calculateRequest.SchemaVersion,
                RunId = Guid.NewGuid().ToString("N"),
                Operation = calculateRequest.Operation,
                WorkingModelPath = calculateRequest.WorkingModelPath,
                WorkingCopyId = calculateRequest.WorkingCopyId,
                RuntimeCandidateCount = calculateRequest.RuntimeCandidateCount,
                DetectedArchitecture = calculateRequest.DetectedArchitecture,
                SelectedRuntime = calculateRequest.SelectedRuntime,
                CoreFiles = calculateRequest.CoreFiles,
                Diagnostics = new Dictionary<string, string>(calculateRequest.Diagnostics, StringComparer.Ordinal)
            };
            read.Diagnostics[P16AzadeganCalculationHost.PhaseKey] = P16AzadeganCalculationHost.PhaseRead;
            AddLineage(read, calc);
            P16AzadeganCalculationHost.ValidateRequest(read);
            string ipc = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "RuntimeHost", read.RunId);
            RuntimeHostProtocol.ControlledDirectory(ipc, "RuntimeHost");
            if (Directory.Exists(ipc)) throw new IOException("P16 READ IPC folder already exists.");
            Directory.CreateDirectory(ipc);
            string requestPath = Path.Combine(ipc, "request.json"), resultPath = Path.Combine(ipc, "result.json");
            RuntimeHostProtocol.AtomicWrite(requestPath, read);
            Write(Path.Combine(evidenceDirectory, "request.json"), Json(read));
            update?.Invoke("Reopening the freshly saved disposable model; no solver or save operation.");

            var start = new System.Diagnostics.ProcessStartInfo(hostExecutable,
                P16AzadeganCalculationHost.LaunchSwitch + " \"" + requestPath + "\" \"" + resultPath + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(hostExecutable)
            };
            using (var owned = System.Diagnostics.Process.Start(start))
            {
                if (owned == null) throw new InvalidOperationException("RuntimeHost READ process was not started.");
                int ownedId = owned.Id;
                if (!owned.WaitForExit(60000))
                {
                    OwnedProcessTerminator.TryTerminate(owned, ownedId);
                    throw new TimeoutException("The one permitted native READ RuntimeHost exceeded 60 seconds.");
                }
                if (owned.ExitCode != 0)
                    throw new InvalidDataException("P16 READ RuntimeHost ended with exit code " + owned.ExitCode);
            }

            if (!File.Exists(resultPath)) throw new InvalidDataException("Native READ did not return a result document.");
            Write(Path.Combine(evidenceDirectory, "result.json"), File.ReadAllText(resultPath));
            var journal = Path.Combine(ipc, "stages.jsonl");
            if (File.Exists(journal)) File.Copy(journal, Path.Combine(evidenceDirectory, "runtime-stages.jsonl"));
            var result = RuntimeHostProtocol.ParseResult(resultPath, read);
            if (!result.Success) throw new InvalidDataException("Native READ failed its runtime acceptance.");
            P16AzadeganCalculationHost.ValidateResult(result);
            return result;
        }

        internal static Receipt Execute(string selectedSourceModelPath, Action<string> update = null)
        {
            if (Interlocked.Exchange(ref active, 1) != 0)
                throw new InvalidOperationException("Another P16 native calculation is already active.");
            try
            {
                ValidateSource(selectedSourceModelPath);
                string model = Path.GetFullPath(selectedSourceModelPath);
                string directory = Path.GetDirectoryName(model);
                var family = P16AzadeganCalculationHost.Family.Select(f => Path.Combine(directory, f)).ToArray();
                var sourceBefore = P16AzadeganCalculationProof.Manifest(family);
                string evidence = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SewerPilot", "Evidence", "P16", DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(evidence)) throw new IOException("Evidence directory collision.");
                Directory.CreateDirectory(evidence);
                Write(Path.Combine(evidence, "source-family-before.txt"), string.Join(Environment.NewLine, sourceBefore));

                update?.Invoke("Calculating once with the installed Bentley engine, on a disposable model copy.");
                var attempt = P16AzadeganCalculationProof.Execute(model, RuntimeRoot(), RuntimeHostExe());
                Write(Path.Combine(evidence, "calculate-request.json"), attempt.RequestJson);
                Write(Path.Combine(evidence, "calculate-result.json"), attempt.ResultJson);
                Write(Path.Combine(evidence, "calculate-observation.json"), attempt.CalculateObservationJson);
                Write(Path.Combine(evidence, "pre-save-observation.json"), attempt.PreSaveObservationJson);
                if (attempt.RunDirectory != null)
                {
                    string journal = Path.Combine(attempt.RunDirectory, "stages.jsonl");
                    if (File.Exists(journal)) File.Copy(journal, Path.Combine(evidence, "calculate-runtime-stages.jsonl"));
                }
                var sourceAfter = P16AzadeganCalculationProof.Manifest(family);
                Write(Path.Combine(evidence, "source-family-after.txt"), string.Join(Environment.NewLine, sourceAfter));
                if (!attempt.SourceIntegrityPreserved || !sourceBefore.SequenceEqual(sourceAfter))
                    throw new InvalidDataException("Protected source-family integrity failed. Acceptance refused.");
                if (string.IsNullOrEmpty(attempt.CalculateObservationJson) ||
                    string.IsNullOrEmpty(attempt.RequestJson) || string.IsNullOrEmpty(attempt.WorkingModelPath))
                    throw new InvalidDataException("The native calculation did not produce a persisted CALCULATE handoff.");
                var serializer = RuntimeHostProtocol.Serializer();
                var calc = serializer.Deserialize<P16AzadeganCalculationObservation>(attempt.CalculateObservationJson);
                if (calc == null || calc.Status != "CALCULATION_COMPLETED_AWAITING_REOPEN" ||
                    calc.SolverRunInvocations != 1 || calc.SaveInvocations != 1 ||
                    calc.ScenarioComputeStatus != "Success" || !calc.SaveSucceeded ||
                    calc.SaveResultClassification != "PERSISTED" ||
                    string.IsNullOrWhiteSpace(calc.CalculationEventId))
                    throw new InvalidDataException("Actual Bentley calculate/save was not verified.");
                var expectedInventory = P16AzadeganCalculationHost.InventoryJson(
                    P16AzadeganCalculationHost.Inventory(attempt.WorkingCopyDirectory));
                if (!string.Equals(expectedInventory, calc.OutputInventoryJson, StringComparison.Ordinal))
                    throw new InvalidDataException("Freshly saved working-copy inventory does not match the calculation record.");
                var initialRequest = serializer.Deserialize<HostRequest>(attempt.RequestJson);
                ReadSavedOnce(initialRequest, calc, evidence, RuntimeHostExe(), update);
                var view = P16VerifiedReadbackReceipt.Read(evidence);
                if (!sourceBefore.SequenceEqual(P16AzadeganCalculationProof.Manifest(family)))
                    throw new InvalidDataException("The original model-family hashes changed during native readback.");
                Write(Path.Combine(evidence, "desktop-acceptance.txt"),
                    "STATUS=" + view.Status + Environment.NewLine + "FLOW=" + view.Flow + Environment.NewLine +
                    "CALCULATION_EVENT=" + view.EventId + Environment.NewLine +
                    "SOURCE_FAMILY_UNCHANGED=true" + Environment.NewLine +
                    "READ_SOLVER_INVOCATIONS=0" + Environment.NewLine + "READ_SAVE_INVOCATIONS=0");
                return new Receipt { EvidenceDirectory = evidence, WorkingModelPath = attempt.WorkingModelPath, View = view };
            }
            finally { Interlocked.Exchange(ref active, 0); }
        }
    }
}
