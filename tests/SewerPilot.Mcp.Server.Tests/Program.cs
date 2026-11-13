using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SewerPilot.Mcp.Server;

namespace SewerPilot.Mcp.Server.Tests
{
    internal static partial class Program
    {
        private static readonly List<string> checks = new List<string>();
        private static string activeCheck = "startup";
        private static readonly List<object> nativeAccounting = new List<object>();
        private static readonly List<object> processEvidence = new List<object>();
        private static IReadOnlyDictionary<string, int> nativeLayouts;
        private static int? expectedServerPid;
        private static string ownedProcessCleanup = "NOT_ATTEMPTED";
        private const string ProjectId = "12345678-abcd-1234-abcd-123456789012";
        private const string Sentinel = "TEST_ONLY_PRIVATE_PASSWORD_SENTINEL";
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        private static async Task<int> Main(string[] args)
        {
            bool diagnosticOnly = args.Length == 3 && args[2] == "--diagnose-accounting";
            if (args.Length != 2 && !diagnosticOnly) { Console.Error.WriteLine("Usage: test-harness <server-exe> <result-json> [--diagnose-accounting]"); return 2; }
            string root = Path.Combine(Path.GetTempPath(), "SewerPilot-MCP01-TEST_ONLY-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string fixture = Path.Combine(root, "project");
                Directory.CreateDirectory(fixture);
                WriteValid(fixture);
                Check(IntPtr.Size == 8, "HARNESS_X64");
                activeCheck = "NATIVE_STRUCT_LAYOUTS";
                nativeLayouts = OwnedProcessFence.ValidateNativeLayouts();
                Check(true, "NATIVE_STRUCT_LAYOUTS");
                await RunIntegration(Path.GetFullPath(args[0]), fixture, root, diagnosticOnly);
                if (diagnosticOnly)
                {
                    File.WriteAllText(args[1], JsonSerializer.Serialize(new {
                        status = "DIAGNOSTIC_COMPLETE", acceptance = "NOT_RUN", checks, nativeLayouts, nativeAccounting,
                        expectedServerPid, ownedProcessCleanup, fixture = root, nativeErrors = new object[0]
                    }, Json), new UTF8Encoding(false));
                    Console.WriteLine("MCP01_ACCOUNTING_DIAGNOSTIC_COMPLETE; ACCEPTANCE_NOT_RUN");
                    return 0;
                }
                foreach (var pair in new[] {
                    Tuple.Create((string)null, "PROJECT_DIR_REQUIRED"),
                    Tuple.Create("relative-project", "PROJECT_PATH_INVALID"),
                    Tuple.Create(@"\\server\project", "PROJECT_PATH_INVALID"),
                    Tuple.Create(@"\\?\C:\project", "PROJECT_PATH_INVALID"),
                    Tuple.Create(Path.GetPathRoot(fixture), "PROJECT_PATH_INVALID"),
                    Tuple.Create(fixture + @"\..\project", "PROJECT_PATH_INVALID"),
                    Tuple.Create(fixture + ":stream", "PROJECT_PATH_INVALID"),
                    Tuple.Create(fixture + " ", "PROJECT_PATH_INVALID"),
                    Tuple.Create(fixture + @"\*", "PROJECT_PATH_INVALID"),
                    Tuple.Create(Path.Combine(root, "missing"), "PROJECT_INPUT_MISSING")
                })
                    ExpectQueryError(pair.Item1, pair.Item2);

                string junction = Path.Combine(root, "junction");
                CreateJunction(junction, fixture);
                ExpectQueryError(junction, "PROJECT_REPARSE_POINT_REJECTED");
                string nested = Path.Combine(fixture, "nested");
                Directory.CreateDirectory(nested);
                ExpectQueryError(Path.Combine(junction, "nested"), "PROJECT_REPARSE_POINT_REJECTED");
                Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("Haestad", StringComparison.OrdinalIgnoreCase)),
                    "NO_VENDOR_ASSEMBLY_LOADED_IN_QUERY_HARNESS");
                File.WriteAllText(args[1], JsonSerializer.Serialize(new { status = "PASS", checks, nativeAccounting, processEvidence, ownedProcessCleanup, fixture = root, officialClient = "ModelContextProtocol 2.2.0" }, Json), new UTF8Encoding(false));
                Console.WriteLine("MCP01_FOCUSED_CHECKS: " + checks.Count + " PASS");
                return 0;
            }
            catch (Exception error)
            {
                var native = error as NativeJobException;
                var evidence = error as ProcessEvidenceException;
                if (native != null) activeCheck = "NATIVE_" + native.Operation;
                if (evidence != null) activeCheck = evidence.Classification;
                File.WriteAllText(args[1], JsonSerializer.Serialize(new {
                    status = "FAIL", diagnosticOnly, checks, nativeLayouts, nativeAccounting, processEvidence, expectedServerPid, failedGate = activeCheck,
                    exceptionType = error.GetType().Name, fixture = root,
                    nativeOperation = native?.Operation, nativeErrorCode = native?.ErrorCode,
                    nativeErrorHex = native == null ? null : "0x" + native.ErrorCode.ToString("X8"),
                    failureClassification = evidence?.Classification ?? native?.Classification,
                    evidenceOperation = evidence?.Operation,
                    ownedProcessCleanup = error.Data["ownedProcessCleanup"] ?? ownedProcessCleanup
                }, Json), new UTF8Encoding(false));
                Console.Error.WriteLine("MCP01_FOCUSED_FAILURE: " + activeCheck + " (" + error.GetType().Name + ")");
                if (native != null) Console.Error.WriteLine(native.Operation + ": " + native.ErrorCode + " / " + native.Classification);
                if (evidence != null) Console.Error.WriteLine(evidence.Operation + ": " + evidence.Classification);
                return 1;
            }
        }

        private static Process StartOwnedServer(ProcessStartInfo start)
        {
            var process = Process.Start(start);
            expectedServerPid = process.Id;
            return process;
        }

        private static async Task RunIntegration(string sourceExe, string fixture, string root, bool diagnosticOnly)
        {
            // Execute only the copied distribution; remove cache/PATH/home/credential dependencies.
            string bundle = Path.Combine(root, "offline-bundle");
            Directory.CreateDirectory(bundle);
            foreach (string source in Directory.GetFiles(Path.GetDirectoryName(sourceExe)))
                if (new[] { ".dll", ".exe", ".config" }.Contains(Path.GetExtension(source), StringComparer.OrdinalIgnoreCase))
                    File.Copy(source, Path.Combine(bundle, Path.GetFileName(source)));
            string serverExe = Path.Combine(bundle, Path.GetFileName(sourceExe));
            Check(File.Exists(serverExe) && !File.Exists(Path.Combine(bundle, "SewerPilot.OpenFlows.RuntimeHost.exe")), "OFFLINE_BUNDLE_NO_RUNTIMEHOST");

            var start = new ProcessStartInfo(serverExe)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = bundle
            };
            start.EnvironmentVariables.Clear();
            start.EnvironmentVariables["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot");
            start.EnvironmentVariables["TEMP"] = root;
            start.EnvironmentVariables["TMP"] = root;
            start.EnvironmentVariables["PATH"] = Path.Combine(root, "no-tools-or-package-cache");
            start.EnvironmentVariables["SEWERPILOT_PROJECT_DIR"] = fixture;
            start.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            activeCheck = "NATIVE_PROCESS_ACCOUNTING_SETUP";
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)))
            using (var process = StartOwnedServer(start))
            using (var processFence = new OwnedProcessFence(process, serverExe))
            {
                Task<string> diagnostics = process.StandardError.ReadToEndAsync();
                var output = new RecordingStream(process.StandardOutput.BaseStream);
                McpClient client = null;
                try
                {
                    activeCheck = "NATIVE_ACCOUNTING_START_SNAPSHOT";
                    JobSnapshot startSnapshot = processFence.CaptureSnapshot(process.Id);
                    nativeAccounting.Add(new { stage = "START", snapshot = startSnapshot });
                    if (diagnosticOnly)
                    {
                        // Three scheduled observations in one invocation, not retries; no SDK or acceptance calls.
                        await Task.Delay(2000);
                        activeCheck = "NATIVE_ACCOUNTING_BEFORE_CLEANUP_SNAPSHOT";
                        nativeAccounting.Add(new { stage = "BEFORE_CLEANUP", snapshot = processFence.CaptureSnapshot(process.Id) });
                        activeCheck = "OWNED_DIAGNOSTIC_PROCESS_CLEANUP";
                        if (!process.HasExited) process.Kill();
                        if (!process.WaitForExit(5000)) throw new InvalidOperationException("Owned server cleanup timeout");
                        ownedProcessCleanup = "PASS";
                        activeCheck = "NATIVE_ACCOUNTING_AFTER_CLEANUP_SNAPSHOT";
                        nativeAccounting.Add(new { stage = "AFTER_CLEANUP", snapshot = processFence.CaptureSnapshot(process.Id) });
                        File.WriteAllText(Path.Combine(root, "diagnostic-stderr.txt"), await diagnostics, new UTF8Encoding(false));
                        return;
                    }
                    Check(startSnapshot.limitFlags == "0x00002008" && startSnapshot.activeProcessLimit == 2 && !startSnapshot.breakawayAllowed, "JOB_OBJECT_SECURITY_LIMITS_START");
                    activeCheck = "PROCESS_ALLOWLIST_START";
                    processFence.VerifyProcessAllowlist(startSnapshot, true, record => processEvidence.Add(record));
                    Check(true, "PROCESS_ALLOWLIST_START");
                    activeCheck = "MCP_INITIALIZE";
                    client = await McpClient.CreateAsync(new StreamClientTransport(process.StandardInput.BaseStream, output),
                        cancellationToken: timeout.Token);
                    Check(client.ServerInfo.Name == "SewerPilot.Mcp.Server", "MCP_INITIALIZE");
                    var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
                    Check(tools.Count == 9 &&
                        tools.Count(t => t.Name == ProjectTools.ToolName) == 1 &&
                        tools.Count(t => t.Name == DesignBasisTools.ToolName) == 1 &&
                        tools.Count(t => t.Name == StormFlowCalculationTools.RationalToolName) == 1 &&
                        tools.Count(t => t.Name == StormFlowCalculationTools.InletToolName) == 1 &&
                        tools.Count(t => t.Name == StormFlowCalculationTools.ConduitSizingToolName) == 1 &&
                        tools.Count(t => t.Name == StormFlowCalculationTools.HydraulicVerificationToolName) == 1 &&
                        tools.Count(t => t.Name == StormFlowCalculationTools.EndToEndPipelineToolName) == 1 &&
                        tools.Count(t => t.Name == GravityCandidatePreviewTools.FeasibilityToolName) == 1 &&
                        tools.Count(t => t.Name == GravityCandidatePreviewTools.CandidatePreviewToolName) == 1, "TOOLS_LIST_ALL_NINE");
                    foreach (var t in tools)
                    {
                        Check(t.ProtocolTool.Annotations.ReadOnlyHint == true && t.ProtocolTool.Annotations.DestructiveHint == false,
                            "TOOL_READ_ONLY_HINT_" + t.Name);
                    }
                    var metadataTool = tools.Single(t => t.Name == ProjectTools.ToolName);
                    var summaryTool = tools.Single(t => t.Name == DesignBasisTools.ToolName);
                    Check(metadataTool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Count() == 0 &&
                        !metadataTool.ProtocolTool.InputSchema.GetProperty("additionalProperties").GetBoolean(), "TOOL_ZERO_ARGUMENT_SCHEMA");
                    Check(metadataTool.ProtocolTool.Annotations.ReadOnlyHint == true && metadataTool.ProtocolTool.Annotations.DestructiveHint == false,
                        "TOOL_READ_ONLY_HINT");
                    Check(summaryTool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Count() == 1 &&
                        summaryTool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("module").GetProperty("enum").EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] { "GravityFlow", "StormFlow" }) &&
                        summaryTool.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Single().GetString() == "module" &&
                        !summaryTool.ProtocolTool.InputSchema.GetProperty("additionalProperties").GetBoolean() &&
                        summaryTool.ProtocolTool.Annotations.ReadOnlyHint == true && summaryTool.ProtocolTool.Annotations.DestructiveHint == false, "DESIGN_BASIS_TOOL_SCHEMA_AND_HINTS");
                    Dictionary<string, string> original = HashInputs(fixture);
                    activeCheck = "PROJECT_INFO_CALL";
                    CallToolResult good = await client.CallToolAsync(ProjectTools.ToolName, cancellationToken: timeout.Token);
                    using (JsonDocument body = JsonDocument.Parse(Text(good)))
                    {
                        JsonElement p = body.RootElement;
                        Check(good.IsError != true && p.GetProperty("kind").GetString() == "STORED_PROJECT_METADATA", "PROJECT_INFO_CALL");
                        Check(p.GetProperty("projectId").GetString() == ProjectId && p.GetProperty("projectName").GetString() == "TEST_ONLY_MCP01",
                            "PROJECT_IDENTITY");
                        Check(p.GetProperty("schemaVersion").GetInt32() == 1 && p.GetProperty("stateRevision").GetInt32() == 7 &&
                            p.GetProperty("selectedModule").GetString() == "StormFlow", "PROJECT_SCHEMA_REVISION_MODULE");
                        Check(p.GetProperty("storedModelFingerprint").GetString() == new string('a', 64) &&
                            p.GetProperty("gravityFlowDesignBasisReference").GetProperty("referenceIdentifier").GetString() == "TEST_ONLY_GF" &&
                            p.GetProperty("stormFlowDesignBasisReference").GetProperty("referenceIdentifier").GetString() == "TEST_ONLY_SF",
                            "STORED_FINGERPRINT_AND_DUAL_REFERENCES");
                        Check(p.EnumerateObject().Count() == 9 && !Text(good).Contains(Sentinel) && !Text(good).Contains(fixture), "MINIMAL_METADATA_NO_PATHS_OR_AUDIT");
                    }
                    Unchanged(original, fixture, "VALID_PROJECT_AND_AUDIT_UNCHANGED");
                    await ExpectClientError(client, fixture, "MCP_ARGUMENTS_NOT_ALLOWED",
                        new Dictionary<string, object> { ["projectPath"] = Sentinel }, timeout.Token);
                    CallToolResult unavailable = await client.CallToolAsync("sewerpilot_save_project", cancellationToken: timeout.Token);
                    Check(unavailable.IsError == true && Classification(unavailable) == "TOOL_NOT_AVAILABLE", "UNREGISTERED_WRITE_TOOL_REJECTED");

                    File.WriteAllText(Path.Combine(fixture, "project.json"), "{" + Sentinel);
                    await ExpectClientError(client, fixture, "MALFORMED_PROJECT_STATE", null, timeout.Token);
                    WriteValid(fixture, schema: 99);
                    await ExpectClientError(client, fixture, "UNSUPPORTED_PROJECT_SCHEMA", null, timeout.Token);
                    WriteValid(fixture);
                    File.WriteAllText(Path.Combine(fixture, "audit.jsonl"), "{" + Sentinel);
                    await ExpectClientError(client, fixture, "MALFORMED_AUDIT_LOG", null, timeout.Token);
                    WriteValid(fixture, auditProjectId: "87654321");
                    await ExpectClientError(client, fixture, "AUDIT_PROJECT_ID_MISMATCH", null, timeout.Token);
                    WriteValid(fixture, secret: true);
                    await ExpectClientError(client, fixture, "SECURITY_VIOLATION", null, timeout.Token);
                    WriteValid(fixture);
                    SetOversized(Path.Combine(fixture, "project.json"));
                    await ExpectClientError(client, fixture, "PROJECT_INPUT_TOO_LARGE", null, timeout.Token);
                    WriteValid(fixture);
                    SetOversized(Path.Combine(fixture, "audit.jsonl"));
                    await ExpectClientError(client, fixture, "PROJECT_INPUT_TOO_LARGE", null, timeout.Token);
                    WriteValid(fixture);
                    activeCheck = "PROJECT_INPUT_BUSY";
                    var beforeBusy = HashInputs(fixture);
                    using (var writerLease = new FileStream(Path.Combine(fixture, "project.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                    {
                        CallToolResult busy = await client.CallToolAsync(ProjectTools.ToolName, cancellationToken: timeout.Token);
                        Check(busy.IsError == true && Classification(busy) == "PROJECT_INPUT_BUSY", "REJECT_PROJECT_INPUT_BUSY");
                        Check(!Text(busy).Contains(Sentinel) && !Text(busy).Contains(fixture) && !Text(busy).Contains("Exception"), "SANITIZE_PROJECT_INPUT_BUSY");
                    }
                    Unchanged(beforeBusy, fixture, "READ_ONLY_PROJECT_INPUT_BUSY");
                    WriteValid(fixture, noAudit: true);
                    CallToolResult legacy = await client.CallToolAsync(ProjectTools.ToolName, cancellationToken: timeout.Token);
                    Check(legacy.IsError != true, "LEGACY_OPTIONAL_AUDIT");
                    Check(!File.Exists(Path.Combine(fixture, "audit.jsonl")), "ABSENT_AUDIT_NOT_CREATED");
                    WriteValid(fixture);
                    await RunDesignBasisChecks(client, fixture, root, timeout.Token);
                    await RunStormFlowCalculationChecks(client, timeout.Token);
                    await RunGravityCandidatePreviewChecks(client, fixture, root, timeout.Token);
                    activeCheck = "NATIVE_PROCESS_IDENTITY_BEFORE_SHUTDOWN";
                    JobSnapshot beforeShutdown = processFence.CaptureSnapshot(process.Id);
                    nativeAccounting.Add(new { stage = "BEFORE_SHUTDOWN", snapshot = beforeShutdown });
                    processFence.VerifyProcessAllowlist(beforeShutdown, true, record => processEvidence.Add(record));
                    Check(true, "PROCESS_ALLOWLIST_BEFORE_SHUTDOWN");
                    activeCheck = "CLEAN_SERVER_SHUTDOWN";
                    // Official SDK stream transport leaves caller-owned streams open; close our pipe to deliver EOF.
                    process.StandardInput.Close();
                    await client.DisposeAsync();
                    client = null;
                    if (!process.WaitForExit(8000)) throw new InvalidOperationException("Server did not exit on client disconnect");
                    Check(process.ExitCode == 0, "CLEAN_SERVER_SHUTDOWN");
                    await Task.Delay(2000, timeout.Token);
                    JobSnapshot endSnapshot = processFence.CaptureSnapshot(process.Id);
                    nativeAccounting.Add(new { stage = "EXIT", snapshot = endSnapshot });
                    Check(endSnapshot.limitFlags == "0x00002008" && endSnapshot.activeProcessLimit == 2 && !endSnapshot.breakawayAllowed, "JOB_OBJECT_SECURITY_LIMITS_END");
                    activeCheck = "PROCESS_ALLOWLIST_COMPLETE_RUN";
                    processFence.VerifyProcessAllowlist(endSnapshot, false, record => processEvidence.Add(record));
                    Check(true, "PROCESS_ALLOWLIST_COMPLETE_RUN");
                    string stderr = await diagnostics;
                    Check(!stderr.Contains(Sentinel) && !stderr.Contains(fixture) && !stderr.Contains(" at SewerPilot."), "STDERR_SANITIZED");
                    string stdout = output.Captured;
                    string[] lines = stdout.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                        using (JsonDocument message = JsonDocument.Parse(line))
                            Check(message.RootElement.GetProperty("jsonrpc").GetString() == "2.0", "STDOUT_JSON_RPC_ONLY");
                    Check(lines.Length >= 10, "PROTOCOL_RESPONSES_RECORDED");
                    Check(output.MaximumCaptureBytes <= 4 * 1024 * 1024, "BOUNDED_PROTOCOL_CAPTURE");
                    File.WriteAllText(Path.Combine(root, "stdout.jsonl"), stdout, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(root, "stderr.txt"), stderr, new UTF8Encoding(false));
                    Check(true, "OFFLINE_RUNTIME_COPIED_BUNDLE_NO_HOME_OR_PATH");
                    File.WriteAllText(Path.Combine(root, "bundle-manifest.json"), JsonSerializer.Serialize(
                        Directory.GetFiles(bundle).Select(f => new { name = Path.GetFileName(f), sha256 = Hash(f) }).ToArray(), Json));
                }
                finally
                {
                    if (client != null) await client.DisposeAsync();
                    if (!process.HasExited) process.Kill(); // Exact owned test process only, on test failure.
                    if (!process.WaitForExit(5000)) throw new InvalidOperationException("Owned server cleanup timeout");
                    ownedProcessCleanup = "PASS";
                }
            }
        }

        private static async Task ExpectClientError(McpClient client, string fixture, string expected,
            IReadOnlyDictionary<string, object> arguments, CancellationToken token)
        {
            activeCheck = expected;
            var before = HashInputs(fixture);
            CallToolResult result = await client.CallToolAsync(ProjectTools.ToolName, arguments, cancellationToken: token);
            Check(result.IsError == true && Classification(result) == expected, "REJECT_" + expected);
            Check(!Text(result).Contains(Sentinel) && !Text(result).Contains(fixture) && !Text(result).Contains("Exception"), "SANITIZE_" + expected);
            Unchanged(before, fixture, "READ_ONLY_" + expected);
        }

        private static void ExpectQueryError(string path, string expected)
        {
            activeCheck = expected;
            try { new ReadOnlyProjectQuery(path).Read(); throw new InvalidOperationException("Expected rejection"); }
            catch (MetadataReadException error) { Check(error.Classification == expected, "PATH_REJECT_" + expected); }
        }

        private static string Text(CallToolResult value) => value.Content.OfType<TextContentBlock>().Single().Text;
        private static string Classification(CallToolResult value)
        {
            using (JsonDocument body = JsonDocument.Parse(Text(value))) return body.RootElement.GetProperty("classification").GetString();
        }
        private static void Check(bool valid, string name)
        {
            activeCheck = name;
            if (!valid) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        private static Dictionary<string, string> HashInputs(string fixture) => Directory.GetFiles(fixture).ToDictionary(Path.GetFileName, Hash);
        private static void Unchanged(Dictionary<string, string> before, string fixture, string name)
        {
            var after = HashInputs(fixture);
            Check(before.Count == after.Count && before.All(p => after.ContainsKey(p.Key) && after[p.Key] == p.Value), name);
        }
        private static string Hash(string path)
        {
            using (var input = File.OpenRead(path))
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        private static void SetOversized(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) file.SetLength(ReadOnlyProjectQuery.MaximumInputBytes + 1);
        }
        private static void WriteValid(string directory, int schema = 1, string auditProjectId = ProjectId, bool secret = false, bool noAudit = false)
        {
            var state = new
            {
                SchemaVersion = schema, ProjectId, ProjectName = "TEST_ONLY_MCP01", StateRevision = 7, SelectedModule = "StormFlow",
                Model = new { ProtectedBaselineFingerprint = new string('a', 64), BaselineLocationMetadata = Sentinel },
                Runtime = new { CapabilityStates = new Dictionary<string, string> { [secret ? "password" : "TEST_ONLY"] = secret ? Sentinel : "NotTested" } },
                DesignBasisReference = new { ReferenceIdentifier = "TEST_ONLY_GF", DesignBasisVersion = "GF-1" },
                StormFlowDesignBasisReference = new { ReferenceIdentifier = "TEST_ONLY_SF", DesignBasisVersion = "SF-1" }
            };
            File.WriteAllText(Path.Combine(directory, "project.json"), JsonSerializer.Serialize(state), new UTF8Encoding(false));
            string audit = Path.Combine(directory, "audit.jsonl");
            if (noAudit) { if (File.Exists(audit)) File.Delete(audit); return; } // Owned test fixture only.
            File.WriteAllText(audit, JsonSerializer.Serialize(new
            {
                AuditRecordId = "TEST_ONLY", Sequence = 1, ProjectId = auditProjectId, ActionType = "ProjectCreated",
                Payload = new { irrelevantPrivateValue = Sentinel }
            }) + "\n", new UTF8Encoding(false));
        }

        private static void CreateJunction(string link, string target)
        {
            // Native Windows fixture creation, headless and outside the repository; never product GUI.
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                "/d /c mklink /J \"" + link + "\" \"" + target + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(start))
            {
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                if (!process.WaitForExit(5000) || process.ExitCode != 0) throw new InvalidOperationException("Fixture junction unavailable");
            }
            Check((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "REAL_REPARSE_FIXTURE");
        }

        private static void RemoveJunction(string link)
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                "/d /c rmdir \"" + link + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(start))
            {
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit(5000);
            }
        }

        private sealed class RecordingStream : Stream
        {
            private readonly Stream inner;
            private readonly MemoryStream capture = new MemoryStream();
            public int MaximumCaptureBytes { get; private set; }
            public RecordingStream(Stream inner) { this.inner = inner; }
            public string Captured => Encoding.UTF8.GetString(capture.ToArray());
            private int Record(byte[] buffer, int offset, int count)
            {
                if (capture.Length + count > 4 * 1024 * 1024) throw new IOException("Protocol capture limit");
                capture.Write(buffer, offset, count);
                MaximumCaptureBytes = (int)capture.Length;
                return count;
            }
            public override int Read(byte[] buffer, int offset, int count) => Record(buffer, offset, inner.Read(buffer, offset, count));
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
                => Record(buffer, offset, await inner.ReadAsync(buffer, offset, count, token));
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        }
    }
}
