using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class Civil3dLiveOptions
    {
        public string Civil3dExecutablePath { get; set; } = @"C:\Program Files\Autodesk\AutoCAD 2021\accoreconsole.exe";
        public string PluginAssemblyPath { get; set; }
        public string SourceTemplateDwg { get; set; } = @"C:\Program Files\Autodesk\AutoCAD 2021\C3D\Help\Civil Tutorials\Drawings\Pipe Networks-3.dwg";
        public string ScratchDwgPath { get; set; }
        public string PipeName { get; set; } = "SewerPilot_Civil3D_IPC";
        public int ConnectionTimeoutSeconds { get; set; } = 30;
        public int ResponseTimeoutSeconds { get; set; } = 30;
        public string TargetElementName { get; set; } = "Structure - (1)";
        public string TargetProperty { get; set; } = "RimElevation";
        public double TargetMutatedValue { get; set; } = 655.500;
    }

    public sealed class Civil3dLiveProcessInfo
    {
        public int Pid { get; set; }
        public string Civil3DVersion { get; set; }
        public string ActiveDrawingName { get; set; }
    }

    public sealed class Civil3dLiveRoundTripResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public Civil3dLiveProcessInfo ProcessInfo { get; set; }
        public NetworkReadResult NetworkRead { get; set; }
        public GravityNetworkSnapshot Snapshot { get; set; }
        public string MutatedElementName { get; set; }
        public string MutatedProperty { get; set; }
        public double PreMutationValue { get; set; }
        public double TargetMutationValue { get; set; }
        public double PostMutationReReadValue { get; set; }
        public bool ParityVerified { get; set; }
        public string RawJsonReadPayload { get; set; }
        public string RawJsonMutationPayload { get; set; }
        public string RawJsonReReadPayload { get; set; }
        public TimeSpan Duration { get; set; }
        public string ScratchDwgPath { get; set; }
    }

    // Internal DTOs for IPC wire communication with the Civil 3D in-process plugin
    internal sealed class LiveIpcRequest
    {
        public string Command { get; set; }
        public string TargetType { get; set; }
        public string ElementName { get; set; }
        public string PropertyName { get; set; }
        public double? NewValueDouble { get; set; }
        public string NewValueString { get; set; }
    }

    internal sealed class LiveIpcResponse
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int Pid { get; set; }
        public string Civil3DVersion { get; set; }
        public string ActiveDocument { get; set; }
        public LiveNetworkDto NetworkData { get; set; }
        public LiveMutationResultDto MutationResult { get; set; }
        public LiveReadElementResultDto ReadElementResult { get; set; }
    }

    internal sealed class LiveNetworkDto
    {
        public string NetworkName { get; set; }
        public List<LivePipeDto> Pipes { get; set; } = new List<LivePipeDto>();
        public List<LiveStructureDto> Structures { get; set; } = new List<LiveStructureDto>();
    }

    internal sealed class LivePipeDto
    {
        public string Handle { get; set; }
        public long ObjectIdVal { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string StartStructureName { get; set; }
        public string StartStructureHandle { get; set; }
        public string EndStructureName { get; set; }
        public string EndStructureHandle { get; set; }
        public double InnerDiameterOrWidth { get; set; }
        public double Length2D { get; set; }
        public double Slope { get; set; }
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double StartZ { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
        public double EndZ { get; set; }
        public string FlowDirection { get; set; }
    }

    internal sealed class LiveStructureDto
    {
        public string Handle { get; set; }
        public long ObjectIdVal { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RimElevation { get; set; }
        public double SumpElevation { get; set; }
        public double SumpDepth { get; set; }
    }

    internal sealed class LiveMutationResultDto
    {
        public string TargetType { get; set; }
        public string ElementName { get; set; }
        public string PropertyName { get; set; }
        public double OldValueDouble { get; set; }
        public double NewValueDouble { get; set; }
        public string OldValueString { get; set; }
        public string NewValueString { get; set; }
        public bool TransactionCommitted { get; set; }
    }

    internal sealed class LiveReadElementResultDto
    {
        public string TargetType { get; set; }
        public string ElementName { get; set; }
        public string PropertyName { get; set; }
        public double ValueDouble { get; set; }
        public string ValueString { get; set; }
    }

    public sealed class Civil3dLiveClient : IDisposable
    {
        private readonly string _pipeName;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private NamedPipeClientStream _clientStream;
        private StreamReader _reader;
        private StreamWriter _writer;

        public const int MaximumResponseTimeoutMs = 30000;
        public int ResponseTimeoutMs { get; set; } = MaximumResponseTimeoutMs;
        public Process AssociatedProcess { get; set; }
        public int? AssociatedPid { get; set; }

        public Civil3dLiveClient(string pipeName = "SewerPilot_Civil3D_IPC", Process associatedProcess = null, int responseTimeoutMs = MaximumResponseTimeoutMs)
        {
            _pipeName = pipeName;
            AssociatedProcess = associatedProcess;
            ResponseTimeoutMs = Math.Min(Math.Max(responseTimeoutMs, 100), MaximumResponseTimeoutMs);
        }

        public void Connect(int timeoutMs = 30000)
        {
            _clientStream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            _clientStream.Connect(timeoutMs);
            _reader = new StreamReader(_clientStream, Encoding.UTF8);
            _writer = new StreamWriter(_clientStream, Encoding.UTF8) { AutoFlush = true };
        }

        internal LiveIpcResponse SendCommand(LiveIpcRequest request, int? timeoutMs = null)
        {
            if (_clientStream == null || !_clientStream.IsConnected)
                throw new InvalidOperationException("Named Pipe client is not connected to Civil 3D.");

            int effectiveTimeout = timeoutMs.HasValue
                ? Math.Min(Math.Max(timeoutMs.Value, 100), MaximumResponseTimeoutMs)
                : Math.Min(Math.Max(ResponseTimeoutMs, 100), MaximumResponseTimeoutMs);

            string json = _serializer.Serialize(request);
            _writer.WriteLine(json);

            var readTask = _reader.ReadLineAsync();
            if (!readTask.Wait(effectiveTimeout))
            {
                AbortConnection();
                TerminateAssociatedProcesses();
                throw new TimeoutException(
                    string.Format("Civil 3D IPC response timed out after {0} ms. Aborted connection and terminated orphaned processes.", effectiveTimeout));
            }

            string responseJson = readTask.Result;
            if (string.IsNullOrEmpty(responseJson))
                throw new IOException("Empty response received from Civil 3D IPC server.");

            var response = _serializer.Deserialize<LiveIpcResponse>(responseJson);
            if (response != null && response.Pid > 0)
            {
                AssociatedPid = response.Pid;
            }
            return response;
        }

        public void AbortConnection()
        {
            try { _clientStream?.Dispose(); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Civil3dLiveIntegration.cs: " + suppressedEx.Message); }
            try { _writer?.Dispose(); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Civil3dLiveIntegration.cs: " + suppressedEx.Message); }
            try { _reader?.Dispose(); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Civil3dLiveIntegration.cs: " + suppressedEx.Message); }
            _clientStream = null;
            _writer = null;
            _reader = null;
        }

        public void TerminateAssociatedProcesses()
        {
            if (AssociatedProcess != null)
            {
                try
                {
                    if (!AssociatedProcess.HasExited)
                    {
                        AssociatedProcess.Kill();
                    }
                }
                catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Civil3dLiveIntegration.cs: " + suppressedEx.Message); }
            }

            if (AssociatedPid.HasValue)
            {
                try
                {
                    var p = Process.GetProcessById(AssociatedPid.Value);
                    if (!p.HasExited)
                    {
                        p.Kill();
                    }
                }
                catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Civil3dLiveIntegration.cs: " + suppressedEx.Message); }
            }
        }

        public void Dispose()
        {
            AbortConnection();
        }
    }

    public static class Civil3dLiveWorkflow
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static Civil3dLiveRoundTripResult ExecuteLiveRoundTrip(Civil3dLiveOptions options = null)
        {
            var sw = Stopwatch.StartNew();
            options = options ?? new Civil3dLiveOptions();

            var result = new Civil3dLiveRoundTripResult
            {
                MutatedElementName = options.TargetElementName,
                MutatedProperty = options.TargetProperty,
                TargetMutationValue = options.TargetMutatedValue
            };

            // 1. Resolve paths
            if (!File.Exists(options.Civil3dExecutablePath))
            {
                result.Success = false;
                result.ErrorMessage = "Civil 3D executable not found at: " + options.Civil3dExecutablePath;
                return result;
            }

            if (string.IsNullOrEmpty(options.PluginAssemblyPath))
            {
                // Auto-detect plugin in output or repository
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string candidate1 = Path.Combine(baseDir, "SewerPilot.Civil3D.Plugin.dll");
                string cur = baseDir;
                string foundPlugin = null;
                if (File.Exists(candidate1)) foundPlugin = candidate1;
                else
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (string.IsNullOrEmpty(cur)) break;
                        string debugCandidate = Path.Combine(cur, @"src\SewerPilot.Civil3D.Plugin\bin\Debug\net48\SewerPilot.Civil3D.Plugin.dll");
                        string releaseCandidate = Path.Combine(cur, @"src\SewerPilot.Civil3D.Plugin\bin\Release\net48\SewerPilot.Civil3D.Plugin.dll");
                        if (File.Exists(debugCandidate)) { foundPlugin = debugCandidate; break; }
                        if (File.Exists(releaseCandidate)) { foundPlugin = releaseCandidate; break; }
                        var parent = Directory.GetParent(cur);
                        cur = parent != null ? parent.FullName : null;
                    }
                }

                if (!string.IsNullOrEmpty(foundPlugin)) options.PluginAssemblyPath = foundPlugin;
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "SewerPilot.Civil3D.Plugin.dll could not be located.";
                    return result;
                }
            }

            // 2. Prepare isolated scratch test drawing
            if (string.IsNullOrEmpty(options.ScratchDwgPath))
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "SewerPilot_Civil3D_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                options.ScratchDwgPath = Path.Combine(tempDir, "scratch_test_network.dwg");
            }
            result.ScratchDwgPath = options.ScratchDwgPath;

            if (!File.Exists(options.SourceTemplateDwg))
            {
                result.Success = false;
                result.ErrorMessage = "Source template drawing not found: " + options.SourceTemplateDwg;
                return result;
            }

            File.Copy(options.SourceTemplateDwg, options.ScratchDwgPath, true);

            // 3. Generate scratch script to load plugin and start IPC server
            string scriptPath = Path.Combine(Path.GetDirectoryName(options.ScratchDwgPath), "start_ipc.scr");
            string pluginLoadPath = options.PluginAssemblyPath.Replace('\\', '/');
            string scriptContent = string.Format("SECURELOAD 0\r\nNETLOAD \"{0}\"\r\nSEWERPILOT_IPC_START\r\nQUIT\r\n_Y\r\n", pluginLoadPath);
            File.WriteAllText(scriptPath, scriptContent);

            // 4. Launch Civil 3D accoreconsole process
            var psi = new ProcessStartInfo
            {
                FileName = options.Civil3dExecutablePath,
                Arguments = string.Format("/product \"C3D\" /l \"en-US\" /i \"{0}\" /s \"{1}\"", options.ScratchDwgPath, scriptPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Process c3dProcess = null;
            try
            {
                c3dProcess = Process.Start(psi);
                if (c3dProcess == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Failed to spawn Civil 3D accoreconsole process.";
                    return result;
                }

                // 5. Connect to live Named Pipe IPC
                using (var client = new Civil3dLiveClient(options.PipeName, c3dProcess, options.ResponseTimeoutSeconds * 1000))
                {
                    client.Connect(options.ConnectionTimeoutSeconds * 1000);

                    // A. Handshake & Process Info
                    var pingResp = client.SendCommand(new LiveIpcRequest { Command = "PING" });
                    if (!pingResp.Success)
                    {
                        result.Success = false;
                        result.ErrorMessage = "PING failed: " + pingResp.ErrorMessage;
                        return result;
                    }

                    result.ProcessInfo = new Civil3dLiveProcessInfo
                    {
                        Pid = pingResp.Pid,
                        Civil3DVersion = pingResp.Civil3DVersion,
                        ActiveDrawingName = pingResp.ActiveDocument
                    };

                    // B. Discover & Read Native Pipe Network
                    var readResp = client.SendCommand(new LiveIpcRequest { Command = "READ_NETWORK" });
                    if (!readResp.Success || readResp.NetworkData == null)
                    {
                        result.Success = false;
                        result.ErrorMessage = "READ_NETWORK failed: " + readResp.ErrorMessage;
                        return result;
                    }
                    result.RawJsonReadPayload = Serializer.Serialize(readResp.NetworkData);

                    // C. Map live extracted data to SewerPilot canonical models
                    var mapped = MapToSewerPilotModels(readResp.NetworkData, result.ProcessInfo);
                    result.NetworkRead = mapped.ReadResult;
                    result.Snapshot = mapped.Snapshot;

                    // Locate target element pre-mutation value
                    var targetStruct = readResp.NetworkData.Structures
                        .FirstOrDefault(s => string.Equals(s.Name, options.TargetElementName, StringComparison.OrdinalIgnoreCase));
                    if (targetStruct != null)
                    {
                        result.PreMutationValue = targetStruct.RimElevation;
                    }

                    // D. Apply Deterministic Mutation
                    var mutateResp = client.SendCommand(new LiveIpcRequest
                    {
                        Command = "MUTATE",
                        TargetType = "Structure",
                        ElementName = options.TargetElementName,
                        PropertyName = options.TargetProperty,
                        NewValueDouble = options.TargetMutatedValue
                    });
                    result.RawJsonMutationPayload = Serializer.Serialize(mutateResp);

                    if (!mutateResp.Success || mutateResp.MutationResult == null)
                    {
                        result.Success = false;
                        result.ErrorMessage = "MUTATE failed: " + mutateResp.ErrorMessage;
                        return result;
                    }

                    // E. Re-read mutated element from active Civil 3D database
                    var reReadResp = client.SendCommand(new LiveIpcRequest
                    {
                        Command = "READ_ELEMENT",
                        TargetType = "Structure",
                        ElementName = options.TargetElementName,
                        PropertyName = options.TargetProperty
                    });
                    result.RawJsonReReadPayload = Serializer.Serialize(reReadResp);

                    if (!reReadResp.Success || reReadResp.ReadElementResult == null)
                    {
                        result.Success = false;
                        result.ErrorMessage = "READ_ELEMENT failed: " + reReadResp.ErrorMessage;
                        return result;
                    }

                    result.PostMutationReReadValue = reReadResp.ReadElementResult.ValueDouble;

                    // Verify parity
                    result.ParityVerified = Math.Abs(result.PostMutationReReadValue - options.TargetMutatedValue) < 1e-4;
                    // Success requires BOTH authentic canonical snapshot ingestion and verified parity.
                    result.Success = result.ParityVerified && result.Snapshot != null;
                    if (!result.ParityVerified)
                    {
                        result.ErrorMessage = string.Format("Parity mismatch: expected {0:F4}, got {1:F4}",
                            options.TargetMutatedValue, result.PostMutationReReadValue);
                    }
                    else if (result.Snapshot == null)
                    {
                        result.ErrorMessage = "Civil 3D snapshot ingestion produced no canonical snapshot; success is prohibited.";
                    }

                    // F. Graceful shutdown
                    client.SendCommand(new LiveIpcRequest { Command = "SHUTDOWN" });
                }

                // Wait for process exit
                if (!c3dProcess.WaitForExit(10000))
                {
                    try { c3dProcess.Kill(); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Civil3dLiveIntegration.cs: " + suppressedEx.Message); }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = "Workflow exception: " + ex.Message;
                if (c3dProcess != null && !c3dProcess.HasExited)
                {
                    try { c3dProcess.Kill(); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Civil3dLiveIntegration.cs: " + suppressedEx.Message); }
                }
            }
            finally
            {
                sw.Stop();
                result.Duration = sw.Elapsed;
            }

            return result;
        }

        private static (NetworkReadResult ReadResult, GravityNetworkSnapshot Snapshot) MapToSewerPilotModels(
            LiveNetworkDto dto, Civil3dLiveProcessInfo procInfo)
        {
            var read = new NetworkReadResult
            {
                Phase = "P12-A",
                Status = "CIVIL3D_LIVE_EXTRACTED",
                ScenarioContext = new NetworkScenarioContext
                {
                    ActiveScenarioId = 1,
                    ActiveScenarioLabel = "Civil3D Live Active Drawing",
                    TopologyAlternativeId = 1,
                    PhysicalAlternativeId = 1,
                    ActiveTopologyAlternativeId = 1
                }
            };

            // Build node ID map
            var structureIdMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int nextNodeId = 1001;
            foreach (var s in dto.Structures)
            {
                int nodeId = nextNodeId++;
                structureIdMap[s.Name] = nodeId;
                if (!string.IsNullOrEmpty(s.Handle))
                    structureIdMap[s.Handle] = nodeId;

                read.Structures.Add(new StructureReadDto
                {
                    Id = nodeId,
                    Label = s.Name,
                    RuntimeCategory = "Civil3DManhole",
                    ElementTypeId = 52,
                    X = s.X,
                    Y = s.Y,
                    RimElevation = s.RimElevation,
                    GroundElevation = s.RimElevation,
                    InvertElevation = s.SumpElevation
                });
            }

            // Map conduits
            int nextConduitId = 2001;
            foreach (var p in dto.Pipes)
            {
                int conduitId = nextConduitId++;
                int startId = 0;
                int stopId = 0;

                if (!string.IsNullOrEmpty(p.StartStructureName))
                    structureIdMap.TryGetValue(p.StartStructureName, out startId);
                if (startId == 0 && !string.IsNullOrEmpty(p.StartStructureHandle))
                    structureIdMap.TryGetValue(p.StartStructureHandle, out startId);

                if (!string.IsNullOrEmpty(p.EndStructureName))
                    structureIdMap.TryGetValue(p.EndStructureName, out stopId);
                if (stopId == 0 && !string.IsNullOrEmpty(p.EndStructureHandle))
                    structureIdMap.TryGetValue(p.EndStructureHandle, out stopId);

                // If endpoints unlinked in drawing, create placeholder nodes
                if (startId == 0)
                {
                    startId = nextNodeId++;
                    read.Structures.Add(new StructureReadDto
                    {
                        Id = startId,
                        Label = p.Name + "_StartVirtual",
                        RuntimeCategory = "Civil3DManhole",
                        ElementTypeId = 52,
                        X = p.StartX,
                        Y = p.StartY,
                        RimElevation = null,
                        GroundElevation = null,
                        InvertElevation = p.StartZ
                    });
                }
                if (stopId == 0)
                {
                    stopId = nextNodeId++;
                    read.Structures.Add(new StructureReadDto
                    {
                        Id = stopId,
                        Label = p.Name + "_StopVirtual",
                        RuntimeCategory = "Civil3DManhole",
                        ElementTypeId = 52,
                        X = p.EndX,
                        Y = p.EndY,
                        RimElevation = null,
                        GroundElevation = null,
                        InvertElevation = p.EndZ
                    });
                }

                var conduitDto = new ConduitReadDto
                {
                    Id = conduitId,
                    Label = p.Name,
                    StartNodeId = startId,
                    StopNodeId = stopId,
                    Shape = null,
                    ConduitType = "Conduit",
                    Material = null,
                    ManningsN = null,
                    Diameter = p.InnerDiameterOrWidth > 0 ? (double?)p.InnerDiameterOrWidth : null,
                    Length = p.Length2D > 0 ? (double?)p.Length2D : null,
                    StartInvert = p.StartZ,
                    StopInvert = p.EndZ,
                    UpstreamInvert = p.StartZ,
                    DownstreamInvert = p.EndZ,
                    GeometryPoints = new List<Point2DDto>
                    {
                        new Point2DDto(p.StartX, p.StartY),
                        new Point2DDto(p.EndX, p.EndY)
                    }
                };

                string partCatalogDescription = p.Description;
                bool isCatalogResolved = !string.IsNullOrWhiteSpace(partCatalogDescription);
                if (string.IsNullOrWhiteSpace(partCatalogDescription) || !isCatalogResolved)
                {
                    conduitDto.PropertyClassifications["CatalogPart"] = "UNRESOLVED_CATALOG_PART";
                    conduitDto.Material = null;
                    conduitDto.ManningsN = null;
                }
                else
                {
                    conduitDto.PropertyClassifications["CatalogPart"] = partCatalogDescription;
                }

                read.Conduits.Add(conduitDto);
            }

            // Run validation
            read.TopologyCheck = NetworkTopologyValidator.Validate(read);

            // Ingest to canonical snapshot
            GravityNetworkSnapshot snapshot = null;
            try
            {
                var ingestionService = new GravityNetworkIngestionService();
                snapshot = ingestionService.Ingest(read, "civil3d_live_" + (procInfo?.Pid.ToString() ?? "0"));
            }
            catch (Exception ex)
            {
                // Fail closed: a null snapshot must not be indistinguishable from a successful
                // ingestion, and must never allow the round trip to report Success = true.
                throw new InvalidOperationException(
                    "Civil 3D canonical snapshot ingestion failed for the live drawing; live round-trip acceptance is prohibited.", ex);
            }

            return (read, snapshot);
        }
    }
}
