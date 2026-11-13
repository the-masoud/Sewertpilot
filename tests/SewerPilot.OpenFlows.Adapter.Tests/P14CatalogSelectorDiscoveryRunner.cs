using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    // One-shot, read-only native qualification of the catalog size selector.
    // No writes, no save, no solver, no retry.
    internal static class P14CatalogSelectorDiscoveryRunner
    {
        internal static int Run(string source, string evidenceDirectory)
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
                    "MODE=TEST_ONLY_CATALOG_SELECTOR_READONLY_PROBE_ZERO_WRITES_ZERO_SOLVE\r\nUTC=" +
                    DateTime.UtcNow.ToString("o"));
                var copy = WorkingCopyIntegrity.Create(source, files, "ReadOnly");
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
                    Operation = P14CatalogSelectorDiscoveryHost.Operation,
                    WorkingCopyId = copyId,
                    WorkingModelPath = copy.ModelPath,
                    RuntimeCandidateCount = 1,
                    DetectedArchitecture = "x64",
                    SelectedRuntime = runtime,
                    CoreFiles = core,
                    Diagnostics = new Dictionary<string, string>(StringComparer.Ordinal) {
                        ["P14CatalogProbeCopyId"] = copyId,
                        ["P14CatalogProbeScenario"] = "1",
                        ["P14CatalogProbeOptions"] = "29",
                        ["P14CatalogProbeEngine"] = "DelawareProductEngine",
                        ["P14CatalogProbeInputSHA256"] = digest
                    }
                };
                P14CatalogSelectorDiscoveryHost.ValidateRequest(request);
                string ipc = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "RuntimeHost", request.RunId);
                RuntimeHostProtocol.ControlledDirectory(ipc, "RuntimeHost");
                if (Directory.Exists(ipc)) throw new IOException("One-shot RuntimeHost IPC already exists.");
                Directory.CreateDirectory(ipc);
                string requestPath = Path.Combine(ipc, "request.json");
                string resultPath = Path.Combine(ipc, "result.json");
                RuntimeHostProtocol.AtomicWrite(requestPath, request);
                File.WriteAllText(Path.Combine(evidenceDirectory, "request.json"),
                    RuntimeHostProtocol.Serializer().Serialize(request));
                var start = new ProcessStartInfo(exe,
                    P14CatalogSelectorDiscoveryHost.LaunchSwitch + " \"" + requestPath + "\" \"" + resultPath + "\"") {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)
                };
                Console.WriteLine("OWNED_SOLVER_RUN=zero, SAVE_ATTEMPT=zero, WATCHDOG=90000ms");
                using (var process = Process.Start(start))
                {
                    if (process == null) throw new InvalidOperationException("RuntimeHost process was not created.");
                    int ownedId = process.Id;
                    if (!process.WaitForExit(90000))
                    {
                        OwnedProcessTerminator.TryTerminate(process, ownedId);
                        throw new TimeoutException("Catalog probe RuntimeHost exceeded its 90-second limit; no retry.");
                    }
                    Console.WriteLine("RUNTIME_EXIT=" + process.ExitCode);
                }
                string stages = Path.Combine(ipc, "stages.jsonl");
                if (File.Exists(stages)) File.Copy(stages, Path.Combine(evidenceDirectory, "stages.jsonl"));
                if (!File.Exists(resultPath)) throw new InvalidDataException("RuntimeHost did not return a result document.");
                File.Copy(resultPath, Path.Combine(evidenceDirectory, "result.json"));
                var raw = RuntimeHostProtocol.Serializer().Deserialize<HostResult>(File.ReadAllText(resultPath));
                Console.WriteLine("HOST_SUCCESS=" + raw.Success);
                Console.WriteLine("HOST_FAILURE=" + raw.FailureClassification);
                Console.WriteLine("SOLVER_CALLS=" + raw.SolverRunInvocationCount);
                Console.WriteLine("SAVE_CALLS=" + raw.SaveInvocationCount);
                Console.WriteLine("MUTATION_CALLS=" + raw.MutationInvocationCount);
                if (raw.Diagnostics.TryGetValue(P14CatalogSelectorDiscoveryHost.EvidenceKey, out string observation))
                {
                    File.WriteAllText(Path.Combine(evidenceDirectory, "catalog-selector-probe.json"), observation);
                    var seen = RuntimeHostProtocol.Serializer()
                        .Deserialize<P14CatalogSelectorDiscoveryHost.Observation>(observation);
                    Console.WriteLine("PROBE_STATUS=" + seen.Status);
                    Console.WriteLine("QUALIFICATION=" + seen.Qualification);
                    Console.WriteLine("QUALIFICATION_REASON=" + seen.QualificationReason);
                    Console.WriteLine("SIZE_SELECTOR_FIELD=" + seen.SizeSelectionField.Resolved + " " +
                        seen.SizeSelectionField.RuntimeType + " setter=" + seen.SizeSelectionField.LocalSetter);
                    Console.WriteLine("SIZE_SELECTOR_VALUE_CO40=" + seen.SizeSelectionField.ReadValue);
                    Console.WriteLine("SIZE_COLLECTION=" + seen.SizeCollectionStatus + " entries=" + seen.SizeEntries.Count);
                    Console.WriteLine("TWELVE_INCH_GUID=" + seen.TwelveInchGuid + " label=" + seen.TwelveInchLabel +
                        " id_ft=" + seen.TwelveInchInsideDiameterFt);
                    Console.WriteLine("LINKAGE_AGREE=" + seen.LinkageAgreementCount + " DISAGREE=" + seen.LinkageDisagreementCount);
                }
                return raw.Success ? 0 : 3;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("CATALOG_PROBE_BLOCKED=" + error.GetType().FullName + ": " + error.Message);
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
