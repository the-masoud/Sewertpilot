using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    // Client-side record of one bounded output-reader attempt. It holds the source and copy
    // manifests, the exact launch that was used and the resulting observation, so a live mission
    // can be audited without re-reading the vendor store.
    internal sealed class P16AzadeganOutputReaderOutcome
    {
        public string SourceArtifactPath { get; set; }
        public string SourceArtifactSHA256Before { get; set; }
        public string SourceArtifactSHA256After { get; set; }
        public bool SourceArtifactUnchanged { get; set; }
        public string CopyId { get; set; }
        public string CopyDirectory { get; set; }
        public string CopyArtifactPath { get; set; }
        public string CopyArtifactSHA256Before { get; set; }
        public string CopyArtifactSHA256After { get; set; }
        public bool CopyArtifactUnchanged { get; set; }
        public string[] CopyDirectoryEntries { get; set; } = new string[0];
        public string ReaderRoot { get; set; }
        public string[] ReaderAssemblyNames { get; set; } = new string[0];
        public string[] ReaderAssemblySHA256 { get; set; } = new string[0];
        // The corrected contract, recorded client-side so an audit does not have to re-read the
        // vendor store to know which reader and which field were addressed.
        public string ReaderTypeName { get; set; }
        public string RequestedFieldName { get; set; }
        public int ExpectedFieldOrdinal { get; set; }
        public string RuntimeHostExecutable { get; set; }
        public string LaunchArguments { get; set; }
        public string RunDirectory { get; set; }
        public string RequestJson { get; set; }
        public string ResultJson { get; set; }
        public string ObservationJson { get; set; }
        public string Status { get; set; }
        public string FailureClassification { get; set; }
        public string FailureMessageSafe { get; set; }
        public int RuntimeExitCode { get; set; }
        public bool MechanicsCompleted { get; set; }
        public bool Passed { get; set; }
        public string Error { get; set; }
    }

    // Copies only the vendor output store into its own disposable directory, records the before
    // and after identities of both the source and the copy, and launches exactly one owned
    // RuntimeHost process. It performs no vendor call itself and opens no model.
    internal static class P16AzadeganOutputReaderProof
    {
        private const int TimeoutMilliseconds = 120000;
        internal const string Category = "OutputReader";

        internal static string WorkingRoot => Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", Category);

        internal static string DefaultRuntimeHostExecutable()
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(P16AzadeganOutputReaderProof).Assembly.Location));
                 dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SewerPilot.sln")))
                    return Path.Combine(dir.FullName, "src", "SewerPilot.OpenFlows.RuntimeHost", "bin", "Release", "net48",
                        "SewerPilot.OpenFlows.RuntimeHost.exe");
            }
            return null;
        }

        internal static P16AzadeganOutputReaderOutcome Execute(string sourceArtifactPath, string readerRoot,
            string runtimeHostExecutable = null)
        {
            var outcome = new P16AzadeganOutputReaderOutcome
            {
                SourceArtifactPath = Path.GetFullPath(sourceArtifactPath),
                ReaderRoot = Path.GetFullPath(readerRoot),
                ReaderAssemblyNames = P16AzadeganOutputReaderHost.ReaderAssemblyNames,
                ReaderTypeName = P16AzadeganOutputReaderHost.ReaderTypeName,
                RequestedFieldName = P16AzadeganOutputReaderHost.RequestedFieldName,
                ExpectedFieldOrdinal = P16AzadeganOutputReaderHost.ExpectedFieldOrdinal
            };
            try
            {
                if (!File.Exists(outcome.SourceArtifactPath))
                    throw new FileNotFoundException("Source output store is absent: " + outcome.SourceArtifactPath);
                if (!string.Equals(Path.GetFileName(outcome.SourceArtifactPath),
                        P16AzadeganOutputReaderHost.ArtifactFileName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Source file is not the declared output store.");
                outcome.SourceArtifactSHA256Before = WorkingCopyIntegrity.Fingerprint(outcome.SourceArtifactPath).SHA256;
                outcome.ReaderAssemblySHA256 = P16AzadeganOutputReaderHost.ReaderAssemblyNames
                    .Select(name => WorkingCopyIntegrity.Fingerprint(Path.Combine(outcome.ReaderRoot, name + ".dll")).SHA256)
                    .ToArray();

                // Only the output store is copied. No model, no companion and no working family.
                string copyId = Guid.NewGuid().ToString("N");
                string directory = Path.Combine(WorkingRoot, copyId);
                RuntimeHostProtocol.ControlledDirectory(directory, Category);
                Directory.CreateDirectory(directory);
                outcome.CopyId = copyId;
                outcome.CopyDirectory = directory;
                outcome.CopyArtifactPath = Path.Combine(directory, P16AzadeganOutputReaderHost.ArtifactFileName);
                File.Copy(outcome.SourceArtifactPath, outcome.CopyArtifactPath);
                outcome.CopyArtifactSHA256Before = WorkingCopyIntegrity.Fingerprint(outcome.CopyArtifactPath).SHA256;
                if (outcome.CopyArtifactSHA256Before != outcome.SourceArtifactSHA256Before)
                    throw new InvalidDataException("Copied output store does not match its source identity.");
                outcome.CopyDirectoryEntries = Directory.GetFiles(directory)
                    .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                if (outcome.CopyDirectoryEntries.Length != 1)
                    throw new InvalidDataException("Disposable directory must contain only the output store.");

                string executable = runtimeHostExecutable ?? DefaultRuntimeHostExecutable();
                outcome.RuntimeHostExecutable = executable;
                if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
                    throw new FileNotFoundException("RuntimeHost executable is unavailable: " + (executable ?? "<unresolved>"));

                var request = new P16AzadeganOutputReaderRequest
                {
                    SchemaVersion = 1,
                    RunId = Guid.NewGuid().ToString("N"),
                    Operation = P16AzadeganOutputReaderHost.Operation,
                    ReaderRoot = outcome.ReaderRoot,
                    ArtifactPath = outcome.CopyArtifactPath,
                    SourceArtifactPath = outcome.SourceArtifactPath,
                    SourceArtifactSHA256 = outcome.SourceArtifactSHA256Before,
                    CopyId = copyId,
                    ReaderAssemblies = P16AzadeganOutputReaderHost.ReaderAssemblyNames.Select((name, index) => new HostMetadata
                    {
                        SimpleName = name,
                        Path = Path.Combine(outcome.ReaderRoot, name + ".dll"),
                        SHA256 = outcome.ReaderAssemblySHA256[index]
                    }).ToArray(),
                    Diagnostics = new Dictionary<string, string>
                    {
                        { "P16AzadeganScenarioId", P16AzadeganOutputReaderHost.ScenarioId.ToString(CultureInfo.InvariantCulture) },
                        { "P16AzadeganElementId", P16AzadeganOutputReaderHost.ElementId.ToString(CultureInfo.InvariantCulture) }
                    }
                };
                outcome.RequestJson = RuntimeHostProtocol.Serializer().Serialize(request);

                string runDirectory = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "RuntimeHost", request.RunId);
                RuntimeHostProtocol.ControlledDirectory(runDirectory, "RuntimeHost");
                Directory.CreateDirectory(runDirectory);
                outcome.RunDirectory = runDirectory;
                string requestPath = Path.Combine(runDirectory, "request.json");
                string resultPath = Path.Combine(runDirectory, "result.json");
                RuntimeHostProtocol.AtomicWrite(requestPath, request);

                outcome.LaunchArguments = P16AzadeganOutputReaderHost.LaunchSwitch +
                    " \"" + requestPath + "\" \"" + resultPath + "\"";
                var start = new System.Diagnostics.ProcessStartInfo(executable, outcome.LaunchArguments)
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
                        throw new TimeoutException("Output-reader host execution exceeded its bound.");
                    }
                    outcome.RuntimeExitCode = process.ExitCode;
                }

                if (!File.Exists(resultPath))
                    throw new InvalidOperationException("Output-reader host produced no result document (exit code " +
                        outcome.RuntimeExitCode + ").");
                outcome.ResultJson = File.ReadAllText(resultPath);
                P16AzadeganOutputReaderHost.ParseResult(resultPath, request);
                var result = RuntimeHostProtocol.Serializer()
                    .Deserialize<P16AzadeganOutputReaderResult>(outcome.ResultJson);
                outcome.Status = result.Status;
                outcome.FailureClassification = result.FailureClassification;
                outcome.FailureMessageSafe = result.FailureMessageSafe;
                outcome.ObservationJson = result.ObservationJson;
                outcome.MechanicsCompleted = result.Success;
                outcome.Passed = result.Status == P16AzadeganOutputReaderHost.PassStatus;
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
                    outcome.SourceArtifactSHA256After = File.Exists(outcome.SourceArtifactPath)
                        ? WorkingCopyIntegrity.Fingerprint(outcome.SourceArtifactPath).SHA256 : null;
                    outcome.SourceArtifactUnchanged = outcome.SourceArtifactSHA256Before != null &&
                        outcome.SourceArtifactSHA256Before == outcome.SourceArtifactSHA256After;
                }
                catch (Exception error) { outcome.Error = (outcome.Error ?? "") + "\nSource re-hash failed: " + error; }
                try
                {
                    if (outcome.CopyArtifactPath != null && File.Exists(outcome.CopyArtifactPath))
                        outcome.CopyArtifactSHA256After = WorkingCopyIntegrity.Fingerprint(outcome.CopyArtifactPath).SHA256;
                    outcome.CopyArtifactUnchanged = outcome.CopyArtifactSHA256Before != null &&
                        outcome.CopyArtifactSHA256Before == outcome.CopyArtifactSHA256After;
                }
                catch (Exception error) { outcome.Error = (outcome.Error ?? "") + "\nCopy re-hash failed: " + error; }
            }
            return outcome;
        }
    }
}
