using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    // Client-side orchestration for the single bounded GVF readback. It copies the declared
    // six-file family into a disposable directory, records both manifests, launches exactly one
    // owned RuntimeHost process and records the resulting observation. It performs no vendor call
    // itself and never touches the frozen customer source.
    internal static class P16AzadeganGvfReadbackProof
    {
        private const int TimeoutMilliseconds = 300000;

        internal static string DefaultRuntimeHostExecutable()
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(P16AzadeganGvfReadbackProof).Assembly.Location));
                 dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SewerPilot.sln")))
                    return Path.Combine(dir.FullName, "src", "SewerPilot.OpenFlows.RuntimeHost", "bin", "Release", "net48",
                        "SewerPilot.OpenFlows.RuntimeHost.exe");
            }
            return null;
        }

        internal static P16AzadeganProofOutcome Execute(string sourceModelPath, string selectedRuntime, string runtimeHostExecutable = null)
        {
            var outcome = new P16AzadeganProofOutcome { SourceModelPath = Path.GetFullPath(sourceModelPath) };
            selectedRuntime = Path.GetFullPath(selectedRuntime);
            WorkingCopyIntegrity working = null;
            try
            {
                string sourceDirectory = Path.GetDirectoryName(outcome.SourceModelPath);
                var family = P16AzadeganGvfReadbackHost.Family.Select(n => Path.Combine(sourceDirectory, n)).ToArray();
                foreach (string file in family)
                    if (!File.Exists(file)) throw new FileNotFoundException("Declared model family file is absent: " + file);
                outcome.SourceManifest = P16AzadeganReadOnlyResultProof.Manifest(family);

                working = WorkingCopyIntegrity.Create(outcome.SourceModelPath, family);
                outcome.WorkingCopyDirectory = working.DirectoryPath;
                outcome.WorkingModelPath = working.ModelPath;
                outcome.WorkingManifest = P16AzadeganReadOnlyResultProof.Manifest(
                    P16AzadeganGvfReadbackHost.Family.Select(n => Path.Combine(working.DirectoryPath, n)));
                outcome.InventoryDigest = P16AzadeganGvfReadbackHost.InventoryDigest(working.DirectoryPath);

                string copyId = Path.GetFileName(working.DirectoryPath);
                string executable = runtimeHostExecutable ?? DefaultRuntimeHostExecutable();
                if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
                    throw new FileNotFoundException("RuntimeHost executable is unavailable: " + (executable ?? "<unresolved>"));

                var coreFiles = OpenFlowsRuntimeCandidate.CoreAssemblyNames
                    .Select(name => Path.Combine(selectedRuntime, name + ".dll")).ToArray();
                var request = new HostRequest
                {
                    SchemaVersion = 1,
                    RunId = Guid.NewGuid().ToString("N"),
                    Operation = P16AzadeganGvfReadbackHost.Operation,
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
                    Diagnostics = P16AzadeganGvfReadbackHost.Context(copyId, outcome.InventoryDigest)
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
                    P16AzadeganGvfReadbackHost.LaunchSwitch + " \"" + requestPath + "\" \"" + resultPath + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executable)
                };
                // The API key is scrubbed in the parent so the child never inherits it; restored
                // immediately afterwards so the parent process is left unchanged.
                string atriaKey = Environment.GetEnvironmentVariable("ATRIA_API_KEY");
                if (atriaKey != null) Environment.SetEnvironmentVariable("ATRIA_API_KEY", null);
                System.Diagnostics.Process process;
                try { process = System.Diagnostics.Process.Start(start); }
                finally { if (atriaKey != null) Environment.SetEnvironmentVariable("ATRIA_API_KEY", atriaKey); }
                using (process)
                {
                    if (!process.WaitForExit(TimeoutMilliseconds))
                    {
                        process.Kill();
                        throw new TimeoutException("RuntimeHost execution exceeded its bound.");
                    }
                    outcome.RuntimeExitCode = process.ExitCode;
                }

                if (!File.Exists(resultPath))
                    throw new InvalidOperationException("RuntimeHost produced no result document (exit code " + outcome.RuntimeExitCode + ").");
                outcome.ResultJson = File.ReadAllText(resultPath);
                var result = RuntimeHostProtocol.ParseResult(resultPath, request);
                outcome.FailureClassification = result.FailureClassification;
                outcome.FailureMessageSafe = result.FailureMessageSafe;
                outcome.Passed = result.Success;
                string observation;
                if (result.Diagnostics.TryGetValue(P16AzadeganGvfReadbackHost.EvidenceKey, out observation))
                {
                    outcome.ObservationJson = observation;
                    outcome.Status = result.Diagnostics.ContainsKey("P16AzadeganGvfStatus")
                        ? result.Diagnostics["P16AzadeganGvfStatus"] : null;
                }
                else
                {
                    outcome.Status = "OBSERVATION_ABSENT";
                }
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
                    outcome.SourceAfterManifest = P16AzadeganReadOnlyResultProof.Manifest(P16AzadeganGvfReadbackHost.Family.Select(n =>
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
                        outcome.WorkingAfterManifest = P16AzadeganReadOnlyResultProof.Manifest(P16AzadeganGvfReadbackHost.Family.Select(n =>
                            Path.Combine(working.DirectoryPath, n)));
                    }
                    catch (Exception error) { outcome.Error = (outcome.Error ?? "") + "\nWorking re-hash failed: " + error; }
                }
            }
            return outcome;
        }
    }
}
