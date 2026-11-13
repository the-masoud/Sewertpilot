using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static class FarzaneganP14NativeLiveRunner
    {
        // One shot, no automated retry. No writes to original family, no external Bentley GUI.
        internal static int Run(string source, string evidenceDirectory, bool remediation = false, bool catalogPointerTrial = false, bool nativeLabelRepair = false, bool prepareOnly = false, bool scopedRepair = false, bool analysisPrepare = false, bool networkPrepare = false)
        {
            string[] files = null;
            string[] originalHashes = null;
            string working = null;
            try
            {
                source = Path.GetFullPath(source);
                if (!string.Equals(Path.GetFileName(source), P16FarzaneganNativePilotHost.ModelFileName,
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Farzanegan exact source filename required.");
                string directory = Path.GetDirectoryName(source);
                files = P16FarzaneganNativePilotHost.Family.Select(name => Path.Combine(directory, name)).ToArray();
                if (files.Any(file => !File.Exists(file)))
                    throw new FileNotFoundException("Farzanegan model family is incomplete.");
                originalHashes = P16AzadeganCalculationProof.Manifest(files).ToArray();
                if (Directory.Exists(evidenceDirectory) &&
                    (File.Exists(Path.Combine(evidenceDirectory, "live-attempt.txt")) ||
                     File.Exists(Path.Combine(evidenceDirectory, "request.json"))))
                    throw new InvalidOperationException("This evidence location already contains a live attempt.");
                Directory.CreateDirectory(evidenceDirectory);
                File.WriteAllLines(Path.Combine(evidenceDirectory, "source-before.txt"), originalHashes);
                File.WriteAllText(Path.Combine(evidenceDirectory, "live-attempt.txt"),
                    "MODE=" + (networkPrepare ? "TEST_ONLY_TEN_SECTION_CATALOGUE_REMEDIATION_30_WRITES_ONE_SAVE_ZERO_SOLVER" : analysisPrepare ? "TEST_ONLY_FIXED_CANDIDATE_ANALYSIS_ONE_OPTION_WRITE_ONE_SAVE_ZERO_SOLVER" : scopedRepair ? "TEST_ONLY_SCOPED_CO40_SELECTION_ONE_SAVE_ZERO_SOLVER" : prepareOnly ? "TEST_ONLY_FOUR_INVERTS_ONE_SAVE_ZERO_SOLVER" : nativeLabelRepair ? "TEST_ONLY_CO40_NATIVE_LABEL_REFRESH_ONE_WRITE_ONE_SAVE_ZERO_SOLVER" : catalogPointerTrial ? "TEST_ONLY_CO40_CATALOG_POINTER_TRIAL_TWO_WRITES_ONE_SAVE_ZERO_SOLVER" : "TEST_ONLY_P14_FOUR_NATIVE_WRITES_ONE_SOLVE_WITH_CONDITIONAL_SAVE") + "\r\nUTC=" + DateTime.UtcNow.ToString("o"));
                var copy = WorkingCopyIntegrity.Create(source, files, "Working");
                working = copy.DirectoryPath;
                string copyId = Path.GetFileName(copy.DirectoryPath);
                string digest = P16AzadeganCalculationHost.InventoryDigest(
                    P16AzadeganCalculationHost.Inventory(copy.DirectoryPath));
                var options = new OpenFlowsDiscoveryOptions(Environment.GetEnvironmentVariable("SEWERPILOT_OPENFLOWS_HOME"));
                var candidates = new OpenFlowsRuntimeDiscovery(new WindowsRuntimeLocationSource(),
                    new InstalledRuntimeInspector()).Discover(options);
                var selection = new OpenFlowsRuntimeSelector().Select(candidates, options);
                if (selection.Status != OpenFlowsRuntimeSelectionStatus.Selected || selection.Candidate == null)
                    throw new InvalidOperationException("Unambiguous Bentley installed runtime unavailable.");
                string runtime = selection.Candidate.InstallRoot;
                string exe = P16AzadeganCalculationProof.DefaultRuntimeHostExecutable();
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                    throw new FileNotFoundException("Compiled RuntimeHost missing.");
                var core = OpenFlowsRuntimeCandidate.CoreAssemblyNames.Select(name =>
                {
                    string path = Path.Combine(runtime, name + ".dll");
                    return new HostMetadata { SimpleName = name, Path = path,
                        SHA256 = WorkingCopyIntegrity.Fingerprint(path).SHA256 };
                }).ToArray();
                var request = new HostRequest {
                    SchemaVersion = 1,
                    RunId = Guid.NewGuid().ToString("N"),
                    Operation = networkPrepare ? P16AnalysisRemediationHost.PrepareOperation : analysisPrepare ? P14CatalogPointerPersistenceHost.AnalysisPrepareOperation : scopedRepair ? P14CatalogPointerPersistenceHost.ScopedRepairOperation : prepareOnly ? P14CatalogPointerPersistenceHost.PrepareOperation : nativeLabelRepair ? P14CatalogPointerPersistenceHost.LabelRepairOperation : catalogPointerTrial ? P14CatalogPointerPersistenceHost.Operation : (remediation ? FarzaneganHydraulicRemediationHost.Operation : P14FarzaneganTestOnlyHost.Operation),
                    WorkingCopyId = copyId,
                    WorkingModelPath = copy.ModelPath,
                    RuntimeCandidateCount = 1,
                    DetectedArchitecture = "x64",
                    SelectedRuntime = runtime,
                    CoreFiles = core,
                    Diagnostics = new Dictionary<string, string>(StringComparer.Ordinal) {
                        ["FarzaneganPilotCopyId"] = copyId,
                        ["FarzaneganPilotScenario"] = "1",
                        ["FarzaneganPilotOptions"] = "29",
                        ["FarzaneganPilotEngine"] = "DelawareProductEngine",
                        ["FarzaneganPilotInputSHA256"] = digest
                    }
                };
                P16FarzaneganNativePilotHost.ValidateRequest(request);
                string ipc = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "RuntimeHost", request.RunId);
                RuntimeHostProtocol.ControlledDirectory(ipc, "RuntimeHost");
                if (Directory.Exists(ipc)) throw new IOException("One-shot RuntimeHost IPC already exists.");
                Directory.CreateDirectory(ipc);
                string requestPath = Path.Combine(ipc, "request.json");
                string resultPath = Path.Combine(ipc, "result.json");
                RuntimeHostProtocol.AtomicWrite(requestPath, request);
                File.WriteAllText(Path.Combine(evidenceDirectory, "request.json"), RuntimeHostProtocol.Serializer().Serialize(request));
                var start = new ProcessStartInfo(exe,
                    (remediation ? FarzaneganHydraulicRemediationHost.LaunchSwitch : P14FarzaneganTestOnlyHost.LaunchSwitch) + " \"" + requestPath + "\" \"" + resultPath + "\"") {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)
                };
                Console.WriteLine(networkPrepare ? "OWNED_SOLVER_RUN=zero, SAVE=one, MUTATIONS=30, WATCHDOG=90000ms" : analysisPrepare ? "OWNED_SOLVER_RUN=zero, SAVE=one, OPTION_MUTATIONS=one, WATCHDOG=90000ms" : scopedRepair ? "OWNED_SOLVER_RUN=zero, SAVE=one, MUTATIONS=three, WATCHDOG=90000ms" : prepareOnly ? "OWNED_SOLVER_RUN=zero, SAVE_ATTEMPT=exactly_one, INVERT_MUTATIONS=exactly_four, WATCHDOG=90000ms" : nativeLabelRepair ? "OWNED_SOLVER_RUN=zero, SAVE_ATTEMPT=exactly_one, LABEL_MUTATIONS=exactly_one, WATCHDOG=90000ms" : catalogPointerTrial
                    ? "OWNED_SOLVER_RUN=zero, SAVE_ATTEMPT=exactly_one, MUTATIONS=exactly_two, WATCHDOG=90000ms"
                    : "OWNED_SOLVER_RUN=one, SAVE_ATTEMPT=zero_or_one, WATCHDOG=90000ms");
                using (var process = Process.Start(start))
                {
                    if (process == null) throw new InvalidOperationException("RuntimeHost process was not created.");
                    int ownedId = process.Id;
                    if (!process.WaitForExit(90000))
                    {
                        OwnedProcessTerminator.TryTerminate(process, ownedId);
                        throw new TimeoutException("Farzanegan pilot RuntimeHost exceeded its 90-second limit; no retry.");
                    }
                    Console.WriteLine("RUNTIME_EXIT=" + process.ExitCode);
                }
                string stages = Path.Combine(ipc, "stages.jsonl");
                if (File.Exists(stages)) File.Copy(stages, Path.Combine(evidenceDirectory, "stages.jsonl"));
                if (File.Exists(resultPath))
                {
                    File.Copy(resultPath, Path.Combine(evidenceDirectory, "result.json"));
                    var raw = RuntimeHostProtocol.Serializer().Deserialize<HostResult>(File.ReadAllText(resultPath));
                    Console.WriteLine("HOST_SUCCESS=" + raw.Success);
                    Console.WriteLine("HOST_FAILURE=" + raw.FailureClassification);
                    Console.WriteLine("SOLVER_CALLS=" + raw.SolverRunInvocationCount);
                    Console.WriteLine("SAVE_CALLS=" + raw.SaveInvocationCount);
                    if (raw.Diagnostics.TryGetValue(P16FarzaneganNativePilotHost.EvidenceKey, out string observation))
                    {
                        File.WriteAllText(Path.Combine(evidenceDirectory, "calculation-observation.json"), observation);
                        var seen = RuntimeHostProtocol.Serializer().Deserialize<P16FarzaneganNativePilotHost.Observation>(observation);
                        Console.WriteLine("CALCULATION_STATUS=" + seen.Status);
                        Console.WriteLine("SCENARIO_STATUS=" + seen.ScenarioComputeStatus);
                        Console.WriteLine("NOTIFICATIONS=" + seen.NotificationClassification + " count=" + seen.Notifications.Length);
                        Console.WriteLine("PERSISTED=" + seen.SaveSucceeded);
                        Console.WriteLine("EVENT=" + seen.CalculationEvent);
                    }
                    RuntimeHostProtocol.ParseResult(resultPath, request);
                    return raw.Success ? 0 : 3;
                }
                throw new InvalidDataException("RuntimeHost did not return a result document.");
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FARZANEGAN_LIVE_BLOCKED=" + error.GetType().FullName + ": " + error.Message);
                return 2;
            }
            finally
            {
                if (files != null && originalHashes != null)
                {
                    bool safe = originalHashes.SequenceEqual(P16AzadeganCalculationProof.Manifest(files));
                    Console.WriteLine("ORIGINAL_SEVEN_FILES_UNCHANGED=" + safe);
                    File.WriteAllText(Path.Combine(evidenceDirectory, "source-integrity.txt"),
                        "ORIGINAL_SEVEN_FILES_UNCHANGED=" + safe + "\r\nWORKING_DIRECTORY=" + working);
                }
            }
        }
    }
}
