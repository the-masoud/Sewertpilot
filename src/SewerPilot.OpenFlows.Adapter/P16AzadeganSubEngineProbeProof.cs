using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    internal sealed class P16AzadeganSubEngineProbeOutcome
    {
        public string SourceModelPath { get; set; }
        public string WorkingCopyDirectory { get; set; }
        public string WorkingModelPath { get; set; }
        public string InventoryDigest { get; set; }
        public List<string> SourceManifest { get; set; } = new List<string>();
        public List<string> SourceAfterManifest { get; set; } = new List<string>();
        public List<string> WorkingManifest { get; set; } = new List<string>();
        public List<string> WorkingAfterManifest { get; set; } = new List<string>();
        public List<string> WorkingAddedFiles { get; set; } = new List<string>();
        public bool SourceIntegrityPreserved { get; set; }
        public bool WorkingIntegrityPreserved { get; set; }
        public string RequestJson { get; set; }
        public string ResultJson { get; set; }
        public string ObservationJson { get; set; }
        public string Status { get; set; }
        public string FailureClassification { get; set; }
        public string FailureMessageSafe { get; set; }
        public int RuntimeExitCode { get; set; }
        public bool Passed { get; set; }
        public string RunDirectory { get; set; }
        public string Error { get; set; }
    }

    // Client-side orchestration for the sub-engine probe. It copies the declared six-file model
    // family into a disposable read-only directory, records both manifests, launches exactly one
    // owned RuntimeHost process and records the resulting counters. It performs no vendor call.
    internal static class P16AzadeganSubEngineProbeProof
    {
        private const int TimeoutMilliseconds = 300000;

        internal static P16AzadeganSubEngineProbeOutcome Execute(string sourceModelPath, string selectedRuntime,
            string runtimeHostExecutable = null)
        {
            var outcome = new P16AzadeganSubEngineProbeOutcome { SourceModelPath = Path.GetFullPath(sourceModelPath) };
            selectedRuntime = Path.GetFullPath(selectedRuntime);
            WorkingCopyIntegrity working = null;
            try
            {
                string sourceDirectory = Path.GetDirectoryName(outcome.SourceModelPath);
                var family = P16AzadeganReadOnlyResultHost.Family
                    .Select(n => Path.Combine(sourceDirectory, n)).ToArray();
                foreach (string file in family)
                    if (!File.Exists(file)) throw new FileNotFoundException("Declared model family file is absent: " + file);
                outcome.SourceManifest = P16AzadeganReadOnlyResultProof.Manifest(family);

                working = WorkingCopyIntegrity.Create(outcome.SourceModelPath, family);
                outcome.WorkingCopyDirectory = working.DirectoryPath;
                outcome.WorkingModelPath = working.ModelPath;
                outcome.WorkingManifest = P16AzadeganReadOnlyResultProof.Manifest(
                    P16AzadeganReadOnlyResultHost.Family.Select(n => Path.Combine(working.DirectoryPath, n)));
                outcome.InventoryDigest = P16AzadeganReadOnlyResultHost.InventoryDigest(working.DirectoryPath);

                string copyId = Path.GetFileName(working.DirectoryPath);
                string executable = runtimeHostExecutable ?? P16AzadeganOutputReaderProof.DefaultRuntimeHostExecutable();
                if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
                    throw new FileNotFoundException("RuntimeHost executable is unavailable: " + (executable ?? "<unresolved>"));

                var coreFiles = OpenFlowsRuntimeCandidate.CoreAssemblyNames
                    .Select(name => Path.Combine(selectedRuntime, name + ".dll")).ToArray();
                var request = new HostRequest
                {
                    SchemaVersion = 1,
                    RunId = Guid.NewGuid().ToString("N"),
                    Operation = P16AzadeganSubEngineProbeHost.Operation,
                    WorkingModelPath = working.ModelPath,
                    WorkingCopyId = copyId,
                    RuntimeCandidateCount = 1,
                    DetectedArchitecture = "x64",
                    SelectedRuntime = selectedRuntime,
                    CoreFiles = coreFiles.Select(path => new HostMetadata
                    {
                        SimpleName = Path.GetFileNameWithoutExtension(path),
                        Path = path,
                        SHA256 = WorkingCopyIntegrity.Fingerprint(path).SHA256
                    }).ToArray(),
                    Diagnostics = P16AzadeganReadOnlyResultHost.Context(copyId, outcome.InventoryDigest)
                };
                outcome.RequestJson = RuntimeHostProtocol.Serializer().Serialize(request);

                string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "RuntimeHost", request.RunId);
                RuntimeHostProtocol.ControlledDirectory(directory, "RuntimeHost");
                Directory.CreateDirectory(directory);
                outcome.RunDirectory = directory;
                string requestPath = Path.Combine(directory, "request.json");
                string resultPath = Path.Combine(directory, "result.json");
                RuntimeHostProtocol.AtomicWrite(requestPath, request);

                var start = new System.Diagnostics.ProcessStartInfo(executable,
                    P16AzadeganSubEngineProbeHost.LaunchSwitch + " \"" + requestPath + "\" \"" + resultPath + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executable)
                };
                System.Diagnostics.Process process;
                using (process = System.Diagnostics.Process.Start(start))
                {
                    if (!process.WaitForExit(TimeoutMilliseconds))
                    {
                        process.Kill();
                        throw new TimeoutException("Sub-engine probe host execution exceeded its bound.");
                    }
                    outcome.RuntimeExitCode = process.ExitCode;
                }

                if (!File.Exists(resultPath))
                    throw new InvalidOperationException("Sub-engine probe host produced no result document (exit code " +
                        outcome.RuntimeExitCode + ").");
                outcome.ResultJson = File.ReadAllText(resultPath);
                var result = RuntimeHostProtocol.ParseResult(resultPath, request);
                outcome.FailureClassification = result.FailureClassification;
                outcome.FailureMessageSafe = result.FailureMessageSafe;
                outcome.Passed = result.Success;
                string observation;
                if (result.Diagnostics.TryGetValue(P16AzadeganSubEngineProbeHost.EvidenceKey, out observation))
                {
                    outcome.ObservationJson = observation;
                    outcome.Status = result.Diagnostics.ContainsKey(P16AzadeganSubEngineProbeHost.StatusKey)
                        ? result.Diagnostics[P16AzadeganSubEngineProbeHost.StatusKey] : null;
                }
                else outcome.Status = "OBSERVATION_ABSENT";
            }
            catch (Exception error)
            {
                outcome.Error = error.ToString();
                if (outcome.Status == null) outcome.Status = "ATTEMPT_ABORTED";
            }
            finally
            {
                try
                {
                    outcome.SourceAfterManifest = P16AzadeganReadOnlyResultProof.Manifest(
                        P16AzadeganReadOnlyResultHost.Family.Select(n =>
                            Path.Combine(Path.GetDirectoryName(outcome.SourceModelPath), n)));
                    outcome.SourceIntegrityPreserved = outcome.SourceAfterManifest.SequenceEqual(outcome.SourceManifest);
                }
                catch (Exception error) { outcome.Error = (outcome.Error ?? "") + "\nSource re-hash failed: " + error; }
                if (working != null)
                {
                    var evidence = new ModelReadEvidence();
                    try
                    {
                        working.Complete(evidence);
                        outcome.WorkingIntegrityPreserved = evidence.WorkingIntegrity;
                        outcome.WorkingAddedFiles = evidence.TransientFiles ?? new List<string>();
                        outcome.WorkingAfterManifest = P16AzadeganReadOnlyResultProof.Manifest(
                            P16AzadeganReadOnlyResultHost.Family.Select(n => Path.Combine(working.DirectoryPath, n)));
                    }
                    catch (Exception error) { outcome.Error = (outcome.Error ?? "") + "\nWorking re-hash failed: " + error; }
                }
            }
            return outcome;
        }
    }
}
