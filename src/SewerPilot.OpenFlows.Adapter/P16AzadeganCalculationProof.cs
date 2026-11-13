using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    internal sealed class P16AzadeganCalculationOutcome
    {
        public string SourceModelPath { get; set; }
        public string WorkingCopyDirectory { get; set; }
        public string WorkingModelPath { get; set; }
        public string DeclaredInventoryDigest { get; set; }
        public List<string> SourceManifestBefore { get; set; } = new List<string>();
        public List<string> SourceManifestAfter { get; set; } = new List<string>();
        public List<string> WorkingManifestBefore { get; set; } = new List<string>();
        public List<string> WorkingManifestAfter { get; set; } = new List<string>();
        public List<string> WorkingCreatedFiles { get; set; } = new List<string>();
        public List<string> WorkingModifiedFiles { get; set; } = new List<string>();
        public string RequestJson { get; set; }
        public string ResultJson { get; set; }
        public string ObservationJson { get; set; }
        public string CalculateObservationJson { get; set; }
        public string Status { get; set; }
        public string FailureClassification { get; set; }
        public string FailureMessageSafe { get; set; }
        public int RuntimeExitCode { get; set; }
        public bool Passed { get; set; }
        public bool SourceIntegrityPreserved { get; set; }
        public string RunDirectory { get; set; }
        public string Error { get; set; }
        public string PreSaveObservationJson { get; set; }
        public string WatchdogDetail { get; set; }
        public int SaveInvocationsAttempted { get; set; }
        public int SaveInvocationsCompleted { get; set; }

        // A pass requires the status and the flag together. A save-stage timeout can never be
        // promoted to a pass by setting the raw flag, and nothing here implies readback.
        public bool IsPass
        {
            get
            {
                return Passed &&
                    (string.Equals(Status, P16AzadeganCalculationHost.PassStatus, StringComparison.Ordinal) ||
                     string.Equals(Status, P16AzadeganCalculationHost.PassWithWarningsStatus, StringComparison.Ordinal));
            }
        }
    }

    internal enum SaveStageVerdict { Continue, SaveCompleted, TimedOut }

    // Watches the run journal for the vendor save boundary. The deadline starts when the save stage
    // records ENTER and is cancelled only by that same stage recording EXIT or EXCEPTION. It performs
    // no vendor call, holds no model reference and never infers completion from anything else.
    internal sealed class SaveStageWatchdog
    {
        internal const int DefaultDeadlineMilliseconds = 30000;
        // The journal stage the persistence boundary records. Mission 18 replaced the UI-bound
        // SaveCurrentProject call with the non-UI IProject::Save(ProjectProperties) path, so the stage
        // name moved with it. The deadline semantics are unchanged: it starts on that stage's ENTER and
        // is cancelled only by the same stage's EXIT or EXCEPTION.
        internal const string SaveStageName = P16AzadeganCalculationHost.PersistenceStageName;

        private readonly string _journalPath;
        private readonly int _deadlineMilliseconds;
        private long _offset;
        private DateTime? _enteredUtc;
        private bool _completed;
        private string _detail = "SAVE_STAGE_NOT_ENTERED";

        internal SaveStageWatchdog(string journalPath, int deadlineMilliseconds = DefaultDeadlineMilliseconds)
        {
            _journalPath = journalPath;
            _deadlineMilliseconds = deadlineMilliseconds <= 0 ? DefaultDeadlineMilliseconds : deadlineMilliseconds;
        }

        internal bool SaveEntered { get { return _enteredUtc.HasValue; } }
        internal bool SaveCompleted { get { return _completed; } }
        internal string Detail { get { return _detail; } }
        internal DateTime? SaveEnteredUtc { get { return _enteredUtc; } }

        // Pure decision over one journal line, so the policy is testable without a live process.
        internal SaveStageVerdict ObserveLine(string line, DateTime nowUtc)
        {
            if (!string.IsNullOrEmpty(line) && line.IndexOf(SaveStageName, StringComparison.Ordinal) >= 0)
            {
                if (line.IndexOf("\"state\":\"EXIT\"", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("\"state\":\"EXCEPTION\"", StringComparison.Ordinal) >= 0)
                {
                    _completed = true;
                    _detail = "SAVE_STAGE_COMPLETED";
                }
                else if (!_enteredUtc.HasValue && line.IndexOf("\"state\":\"ENTER\"", StringComparison.Ordinal) >= 0)
                {
                    _enteredUtc = ParseUtc(line) ?? nowUtc;
                    _detail = "SAVE_STAGE_ENTERED";
                }
            }
            return Decide(nowUtc);
        }

        internal SaveStageVerdict Decide(DateTime nowUtc)
        {
            if (_completed) return SaveStageVerdict.SaveCompleted;
            if (!_enteredUtc.HasValue) return SaveStageVerdict.Continue;
            if ((nowUtc - _enteredUtc.Value).TotalMilliseconds >= _deadlineMilliseconds)
            {
                _detail = "SAVE_STAGE_TIMEOUT";
                return SaveStageVerdict.TimedOut;
            }
            return SaveStageVerdict.Continue;
        }

        internal SaveStageVerdict Observe(DateTime nowUtc)
        {
            foreach (string line in ReadNewLines()) ObserveLine(line, nowUtc);
            return Decide(nowUtc);
        }

        // Incremental, tolerant of a concurrently written journal: only complete lines advance the
        // offset, so a half-written entry is re-read rather than misparsed.
        private IEnumerable<string> ReadNewLines()
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(_journalPath) || !File.Exists(_journalPath)) return lines;
            long start = _offset;
            try
            {
                using (var stream = new FileStream(_journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (stream.Length <= start) return lines;
                    stream.Seek(start, SeekOrigin.Begin);
                    int length = (int)Math.Min(stream.Length - start, 4 * 1024 * 1024);
                    var chunk = new byte[length];
                    int read = stream.Read(chunk, 0, length);
                    string text = new UTF8Encoding(false).GetString(chunk, 0, read);
                    int lastNewline = text.LastIndexOf('\n');
                    if (lastNewline < 0) return lines;
                    _offset = start + new UTF8Encoding(false).GetByteCount(text.Substring(0, lastNewline + 1));
                    foreach (string line in text.Substring(0, lastNewline).Split('\n'))
                        if (line.Trim().Length > 0) lines.Add(line);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return lines;
        }

        private static DateTime? ParseUtc(string line)
        {
            const string marker = "\"utc\":\"";
            int start = line.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;
            int end = line.IndexOf('"', start);
            if (end <= start) return null;
            DateTime value;
            return DateTime.TryParse(line.Substring(start, end - start), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value)
                ? value.ToUniversalTime()
                : (DateTime?)null;
        }
    }

    // Terminates only the RuntimeHost process this invocation started. It enumerates nothing and
    // refuses any handle whose identity does not match the id captured at launch.
    internal static class OwnedProcessTerminator
    {
        internal static bool TryTerminate(System.Diagnostics.Process owned, int expectedProcessId)
        {
            if (owned == null || expectedProcessId <= 0) return false;
            try
            {
                if (owned.Id != expectedProcessId) return false;
                if (owned.HasExited) return false;
                owned.Kill();
                owned.WaitForExit(10000);
                return owned.HasExited;
            }
            catch (Exception) { return false; }
        }
    }

    // Client-side orchestration for one bounded native calculation attempt. It copies the declared
    // family into a calculation-scoped disposable directory, records both manifests, launches exactly
    // one owned RuntimeHost process and records the resulting counters. It performs no vendor call.
    internal static class P16AzadeganCalculationProof
    {
        private const int TimeoutMilliseconds = 600000;
        private const int PollMilliseconds = 250;

        internal static List<string> Manifest(IEnumerable<string> paths)
        {
            var lines = new List<string>();
            foreach (string path in paths.OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
            {
                var info = new FileInfo(path);
                if (!info.Exists) { lines.Add(Path.GetFileName(path) + "|ABSENT"); continue; }
                lines.Add(Path.GetFileName(path) + "|" + info.Length.ToString(CultureInfo.InvariantCulture) + "|" +
                    WorkingCopyIntegrity.Fingerprint(path).SHA256);
            }
            return lines;
        }

        internal static string DefaultRuntimeHostExecutable()
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(P16AzadeganCalculationProof).Assembly.Location));
                 dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SewerPilot.sln")))
                    return Path.Combine(dir.FullName, "src", "SewerPilot.OpenFlows.RuntimeHost", "bin", "Release", "net48",
                        "SewerPilot.OpenFlows.RuntimeHost.exe");
            }
            return null;
        }

        // Recovers the child's pre-save capture even when the child was terminated mid-save. The file
        // remains in the controlled run directory either way; this only lifts it into the outcome.
        internal static string ReadPreSaveEvidence(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return null;
            string path = Path.Combine(directory, P16AzadeganCalculationHost.PreSaveEvidenceFileName);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 8 * 1024 * 1024)
                throw new InvalidDataException("Pre-save evidence exceeds its bound.");
            return File.ReadAllText(path);
        }

        internal static P16AzadeganCalculationOutcome Execute(string sourceModelPath, string selectedRuntime, string runtimeHostExecutable = null)
        {
            var outcome = new P16AzadeganCalculationOutcome { SourceModelPath = Path.GetFullPath(sourceModelPath) };
            selectedRuntime = Path.GetFullPath(selectedRuntime);
            try
            {
                string sourceDirectory = Path.GetDirectoryName(outcome.SourceModelPath);
                var family = P16AzadeganCalculationHost.Family.Select(n => Path.Combine(sourceDirectory, n)).ToArray();
                foreach (string file in family)
                    if (!File.Exists(file)) throw new FileNotFoundException("Declared model family file is absent: " + file);
                outcome.SourceManifestBefore = Manifest(family);

                var working = WorkingCopyIntegrity.Create(outcome.SourceModelPath, family, "Working");
                outcome.WorkingCopyDirectory = working.DirectoryPath;
                outcome.WorkingModelPath = working.ModelPath;
                string copyId = Path.GetFileName(working.DirectoryPath);

                var inputInventory = P16AzadeganCalculationHost.Inventory(working.DirectoryPath);
                outcome.DeclaredInventoryDigest = P16AzadeganCalculationHost.InventoryDigest(inputInventory);
                outcome.WorkingManifestBefore = Manifest(P16AzadeganCalculationHost.Family.Select(n => Path.Combine(working.DirectoryPath, n)));

                string executable = runtimeHostExecutable ?? DefaultRuntimeHostExecutable();
                if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
                    throw new FileNotFoundException("RuntimeHost executable is unavailable: " + (executable ?? "<unresolved>"));

                var coreFiles = OpenFlowsRuntimeCandidate.CoreAssemblyNames
                    .Select(name => Path.Combine(selectedRuntime, name + ".dll")).ToArray();
                var diagnostics = P16AzadeganCalculationHost.Context(copyId, outcome.DeclaredInventoryDigest);
                diagnostics[P16AzadeganCalculationHost.PhaseKey] = P16AzadeganCalculationHost.PhaseCalculate;
                var request = new HostRequest
                {
                    SchemaVersion = 1,
                    RunId = Guid.NewGuid().ToString("N"),
                    Operation = P16AzadeganCalculationHost.Operation,
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
                    Diagnostics = diagnostics
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
                    P16AzadeganCalculationHost.LaunchSwitch + " \"" + requestPath + "\" \"" + resultPath + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executable)
                };
                string atriaKey = Environment.GetEnvironmentVariable("ATRIA_API_KEY");
                if (atriaKey != null) Environment.SetEnvironmentVariable("ATRIA_API_KEY", null);
                System.Diagnostics.Process process;
                try { process = System.Diagnostics.Process.Start(start); }
                finally { if (atriaKey != null) Environment.SetEnvironmentVariable("ATRIA_API_KEY", atriaKey); }
                using (process)
                {
                    int ownedProcessId = process.Id;
                    var watchdog = new SaveStageWatchdog(Path.Combine(directory, "stages.jsonl"));
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    bool saveTimedOut = false;
                    while (!process.WaitForExit(PollMilliseconds))
                    {
                        if (watchdog.Observe(DateTime.UtcNow) == SaveStageVerdict.TimedOut)
                        {
                            // Only this invocation's own process is terminated, and only because the
                            // save boundary stopped responding.
                            OwnedProcessTerminator.TryTerminate(process, ownedProcessId);
                            saveTimedOut = true;
                            break;
                        }
                        if (elapsed.ElapsedMilliseconds >= TimeoutMilliseconds)
                        {
                            OwnedProcessTerminator.TryTerminate(process, ownedProcessId);
                            throw new TimeoutException("RuntimeHost calculation exceeded its bound.");
                        }
                    }
                    if (!saveTimedOut) outcome.RuntimeExitCode = process.ExitCode;
                    outcome.WatchdogDetail = watchdog.Detail;
                    if (saveTimedOut)
                    {
                        // Nothing was persisted, so nothing may be closed, reopened or read back.
                        outcome.Status = "SAVE_STAGE_TIMEOUT";
                        outcome.FailureClassification = "SAVE_STAGE_TIMEOUT";
                        outcome.FailureMessageSafe = "The vendor save stage recorded neither EXIT nor EXCEPTION within " +
                            SaveStageWatchdog.DefaultDeadlineMilliseconds.ToString(CultureInfo.InvariantCulture) +
                            " ms; the owned RuntimeHost process was terminated. One save was attempted and none completed; " +
                            "no close, reopen or readback was attempted.";
                        outcome.SaveInvocationsAttempted = 1;
                        outcome.SaveInvocationsCompleted = 0;
                        outcome.Passed = false;
                        return outcome;
                    }
                }

                if (!File.Exists(resultPath))
                    throw new InvalidOperationException("RuntimeHost produced no result document (exit code " + outcome.RuntimeExitCode + ").");
                outcome.ResultJson = File.ReadAllText(resultPath);
                var result = RuntimeHostProtocol.ParseResult(resultPath, request);
                outcome.FailureClassification = result.FailureClassification;
                outcome.FailureMessageSafe = result.FailureMessageSafe;
                outcome.Passed = result.Success;
                string observation, calculateObservation;
                if (result.Diagnostics.TryGetValue(P16AzadeganCalculationHost.EvidenceKey, out observation))
                {
                    outcome.ObservationJson = observation;
                    outcome.Status = result.Diagnostics.ContainsKey("P16AzadeganStatus") ? result.Diagnostics["P16AzadeganStatus"] : null;
                }
                else
                {
                    outcome.Status = "OBSERVATION_ABSENT";
                }
                if (result.Diagnostics.TryGetValue(P16AzadeganCalculationHost.CalculateEvidenceKey, out calculateObservation))
                    outcome.CalculateObservationJson = calculateObservation;

                var after = P16AzadeganCalculationHost.Inventory(working.DirectoryPath);
                outcome.WorkingCreatedFiles = after.Keys.Where(name => !inputInventory.ContainsKey(name)).ToList();
                outcome.WorkingModifiedFiles = after.Keys.Where(name => inputInventory.ContainsKey(name) && inputInventory[name] != after[name]).ToList();
            }
            catch (Exception error)
            {
                outcome.Error = error.ToString();
                if (outcome.Status == null) outcome.Status = "ATTEMPT_ABORTED";
            }
            finally
            {
                try { outcome.PreSaveObservationJson = ReadPreSaveEvidence(outcome.RunDirectory); }
                catch (Exception error) { outcome.Error = (outcome.Error ?? "") + "\nPre-save evidence recovery failed: " + error; }
                try
                {
                    outcome.SourceManifestAfter = Manifest(P16AzadeganCalculationHost.Family.Select(n =>
                        Path.Combine(Path.GetDirectoryName(outcome.SourceModelPath), n)));
                    outcome.SourceIntegrityPreserved = outcome.SourceManifestAfter.SequenceEqual(outcome.SourceManifestBefore);
                }
                catch (Exception error) { outcome.Error = (outcome.Error ?? "") + "\nSource re-hash failed: " + error; }
                try
                {
                    if (outcome.WorkingCopyDirectory != null)
                        outcome.WorkingManifestAfter = Manifest(P16AzadeganCalculationHost.Family.Select(n =>
                            Path.Combine(outcome.WorkingCopyDirectory, n)));
                }
                catch (Exception error) { outcome.Error = (outcome.Error ?? "") + "\nWorking re-hash failed: " + error; }
            }
            return outcome;
        }
    }
}
