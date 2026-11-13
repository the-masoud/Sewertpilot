using System;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

namespace SewerPilot.Civil3D.Plugin
{
    public class Civil3dIpcServer
    {
        public const string PipeName = "SewerPilot_Civil3D_IPC";

        public static void RunServer()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;
            ed?.WriteMessage(string.Format("\n[SewerPilot IPC] Server listening on \\\\.\\pipe\\{0}...\n", PipeName));

            using (var pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte))
            {
                pipeServer.WaitForConnection();
                ed?.WriteMessage("\n[SewerPilot IPC] Client connected!\n");

                using (var reader = new StreamReader(pipeServer))
                using (var writer = new StreamWriter(pipeServer) { AutoFlush = true })
                {
                    bool running = true;
                    while (running && pipeServer.IsConnected)
                    {
                        string line = reader.ReadLine();
                        if (line == null) break;

                        IpcResponse response = null;
                        try
                        {
                            IpcRequest request = JsonHelper.Deserialize<IpcRequest>(line);
                            if (request == null)
                            {
                                response = new IpcResponse { Success = false, ErrorMessage = "Invalid JSON request" };
                            }
                            else
                            {
                                switch (request.Command?.ToUpperInvariant())
                                {
                                    case "CONNECT":
                                    case "PING":
                                        response = HandleConnect(doc);
                                        break;
                                    case "READ_NETWORK":
                                        response = HandleReadNetwork(doc);
                                        break;
                                    case "MUTATE":
                                        response = HandleMutate(doc, request);
                                        break;
                                    case "READ_ELEMENT":
                                        response = HandleReadElement(doc, request);
                                        break;
                                    case "SHUTDOWN":
                                        response = new IpcResponse { Success = true, Pid = Process.GetCurrentProcess().Id };
                                        running = false;
                                        break;
                                    default:
                                        response = new IpcResponse { Success = false, ErrorMessage = "Unknown command: " + request.Command };
                                        break;
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            response = new IpcResponse { Success = false, ErrorMessage = ex.ToString() };
                        }

                        string jsonOut = JsonHelper.Serialize(response);
                        writer.WriteLine(jsonOut);

                        if (!running)
                        {
                            ed?.WriteMessage("\n[SewerPilot IPC] Shutdown requested by client.\n");
                            break;
                        }
                    }
                }
            }

            ed?.WriteMessage("\n[SewerPilot IPC] Server session ended.\n");
        }

        private static IpcResponse HandleConnect(Document doc)
        {
            return new IpcResponse
            {
                Success = true,
                Pid = Process.GetCurrentProcess().Id,
                Civil3DVersion = Application.Version.ToString(),
                ActiveDocument = doc?.Name ?? HostApplicationServices.WorkingDatabase.Filename
            };
        }

        private static IpcResponse HandleReadNetwork(Document doc)
        {
            var civDoc = CivilApplication.ActiveDocument;
            if (civDoc == null)
                return new IpcResponse { Success = false, ErrorMessage = "CivilDocument not available" };

            var networkDto = new Civil3dNetworkDto();
            ObjectIdCollection netIds = civDoc.GetPipeNetworkIds();
            if (netIds.Count == 0)
                return new IpcResponse { Success = false, ErrorMessage = "No pipe networks found in active drawing" };

            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId netId in netIds)
                {
                    Network net = tr.GetObject(netId, OpenMode.ForRead) as Network;
                    if (net == null) continue;

                    networkDto.NetworkName = net.Name;

                    // Read Structures
                    foreach (ObjectId structId in net.GetStructureIds())
                    {
                        Structure s = tr.GetObject(structId, OpenMode.ForRead) as Structure;
                        if (s == null) continue;

                        networkDto.Structures.Add(new Civil3dStructureDto
                        {
                            Handle = s.Handle.ToString(),
                            ObjectIdVal = s.ObjectId.Handle.Value,
                            Name = s.Name,
                            Description = s.Description,
                            X = s.Location.X,
                            Y = s.Location.Y,
                            Z = s.Location.Z,
                            RimElevation = s.RimElevation,
                            SumpElevation = s.SumpElevation,
                            SumpDepth = s.SumpDepth
                        });
                    }

                    // Read Pipes
                    foreach (ObjectId pipeId in net.GetPipeIds())
                    {
                        Pipe p = tr.GetObject(pipeId, OpenMode.ForRead) as Pipe;
                        if (p == null) continue;

                        string startName = null;
                        string startHandle = null;
                        if (!p.StartStructureId.IsNull)
                        {
                            Structure sStart = tr.GetObject(p.StartStructureId, OpenMode.ForRead) as Structure;
                            if (sStart != null)
                            {
                                startName = sStart.Name;
                                startHandle = sStart.Handle.ToString();
                            }
                        }

                        string endName = null;
                        string endHandle = null;
                        if (!p.EndStructureId.IsNull)
                        {
                            Structure sEnd = tr.GetObject(p.EndStructureId, OpenMode.ForRead) as Structure;
                            if (sEnd != null)
                            {
                                endName = sEnd.Name;
                                endHandle = sEnd.Handle.ToString();
                            }
                        }

                        networkDto.Pipes.Add(new Civil3dPipeDto
                        {
                            Handle = p.Handle.ToString(),
                            ObjectIdVal = p.ObjectId.Handle.Value,
                            Name = p.Name,
                            Description = p.Description,
                            StartStructureName = startName,
                            StartStructureHandle = startHandle,
                            EndStructureName = endName,
                            EndStructureHandle = endHandle,
                            InnerDiameterOrWidth = p.InnerDiameterOrWidth,
                            Length2D = p.Length2DCenterToCenter,
                            Slope = p.Slope,
                            StartX = p.StartPoint.X,
                            StartY = p.StartPoint.Y,
                            StartZ = p.StartPoint.Z,
                            EndX = p.EndPoint.X,
                            EndY = p.EndPoint.Y,
                            EndZ = p.EndPoint.Z,
                            FlowDirection = p.FlowDirection.ToString()
                        });
                    }
                    break; // Read the primary pipe network
                }

                tr.Commit();
            }

            return new IpcResponse
            {
                Success = true,
                Pid = Process.GetCurrentProcess().Id,
                Civil3DVersion = Application.Version.ToString(),
                ActiveDocument = doc.Name,
                NetworkData = networkDto
            };
        }

        private static IpcResponse HandleMutate(Document doc, IpcRequest request)
        {
            var civDoc = CivilApplication.ActiveDocument;
            if (civDoc == null)
                return new IpcResponse { Success = false, ErrorMessage = "CivilDocument not available" };

            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectIdCollection netIds = civDoc.GetPipeNetworkIds();
                foreach (ObjectId netId in netIds)
                {
                    Network net = tr.GetObject(netId, OpenMode.ForRead) as Network;
                    if (net == null) continue;

                    if (string.Equals(request.TargetType, "Structure", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (ObjectId structId in net.GetStructureIds())
                        {
                            Structure s = tr.GetObject(structId, OpenMode.ForRead) as Structure;
                            if (s != null && (string.Equals(s.Name, request.ElementName, StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(s.Handle.ToString(), request.ElementName, StringComparison.OrdinalIgnoreCase)))
                            {
                                s.UpgradeOpen();
                                var result = new Civil3dMutationResultDto
                                {
                                    TargetType = "Structure",
                                    ElementName = s.Name,
                                    PropertyName = request.PropertyName
                                };

                                if (string.Equals(request.PropertyName, "RimElevation", StringComparison.OrdinalIgnoreCase))
                                {
                                    result.OldValueDouble = s.RimElevation;
                                    try { s.AutomaticRimSurfaceAdjustment = false; } catch (System.Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Civil3dIpcServer.cs: " + suppressedEx.Message); }
                                    s.RimElevation = request.NewValueDouble.Value;
                                    result.NewValueDouble = s.RimElevation;
                                }
                                else if (string.Equals(request.PropertyName, "SumpElevation", StringComparison.OrdinalIgnoreCase))
                                {
                                    result.OldValueDouble = s.SumpElevation;
                                    s.SumpElevation = request.NewValueDouble.Value;
                                    result.NewValueDouble = s.SumpElevation;
                                }
                                else if (string.Equals(request.PropertyName, "Description", StringComparison.OrdinalIgnoreCase))
                                {
                                    result.OldValueString = s.Description;
                                    s.Description = request.NewValueString;
                                    result.NewValueString = s.Description;
                                }
                                else
                                {
                                    return new IpcResponse { Success = false, ErrorMessage = "Unsupported structure property: " + request.PropertyName };
                                }

                                tr.Commit();
                                result.TransactionCommitted = true;
                                return new IpcResponse { Success = true, MutationResult = result };
                            }
                        }
                    }
                    else if (string.Equals(request.TargetType, "Pipe", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (ObjectId pipeId in net.GetPipeIds())
                        {
                            Pipe p = tr.GetObject(pipeId, OpenMode.ForRead) as Pipe;
                            if (p != null && (string.Equals(p.Name, request.ElementName, StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(p.Handle.ToString(), request.ElementName, StringComparison.OrdinalIgnoreCase)))
                            {
                                p.UpgradeOpen();
                                var result = new Civil3dMutationResultDto
                                {
                                    TargetType = "Pipe",
                                    ElementName = p.Name,
                                    PropertyName = request.PropertyName
                                };

                                if (string.Equals(request.PropertyName, "StartPointZ", StringComparison.OrdinalIgnoreCase))
                                {
                                    result.OldValueDouble = p.StartPoint.Z;
                                    p.StartPoint = new Point3d(p.StartPoint.X, p.StartPoint.Y, request.NewValueDouble.Value);
                                    result.NewValueDouble = p.StartPoint.Z;
                                }
                                else if (string.Equals(request.PropertyName, "EndPointZ", StringComparison.OrdinalIgnoreCase))
                                {
                                    result.OldValueDouble = p.EndPoint.Z;
                                    p.EndPoint = new Point3d(p.EndPoint.X, p.EndPoint.Y, request.NewValueDouble.Value);
                                    result.NewValueDouble = p.EndPoint.Z;
                                }
                                else if (string.Equals(request.PropertyName, "Description", StringComparison.OrdinalIgnoreCase))
                                {
                                    result.OldValueString = p.Description;
                                    p.Description = request.NewValueString;
                                    result.NewValueString = p.Description;
                                }
                                else
                                {
                                    return new IpcResponse { Success = false, ErrorMessage = "Unsupported pipe property: " + request.PropertyName };
                                }

                                tr.Commit();
                                result.TransactionCommitted = true;
                                return new IpcResponse { Success = true, MutationResult = result };
                            }
                        }
                    }
                }
            }

            return new IpcResponse { Success = false, ErrorMessage = "Target element not found: " + request.ElementName };
        }

        private static IpcResponse HandleReadElement(Document doc, IpcRequest request)
        {
            var civDoc = CivilApplication.ActiveDocument;
            if (civDoc == null)
                return new IpcResponse { Success = false, ErrorMessage = "CivilDocument not available" };

            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectIdCollection netIds = civDoc.GetPipeNetworkIds();
                foreach (ObjectId netId in netIds)
                {
                    Network net = tr.GetObject(netId, OpenMode.ForRead) as Network;
                    if (net == null) continue;

                    if (string.Equals(request.TargetType, "Structure", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (ObjectId structId in net.GetStructureIds())
                        {
                            Structure s = tr.GetObject(structId, OpenMode.ForRead) as Structure;
                            if (s != null && (string.Equals(s.Name, request.ElementName, StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(s.Handle.ToString(), request.ElementName, StringComparison.OrdinalIgnoreCase)))
                            {
                                var res = new Civil3dReadElementResultDto
                                {
                                    TargetType = "Structure",
                                    ElementName = s.Name,
                                    PropertyName = request.PropertyName
                                };

                                if (string.Equals(request.PropertyName, "RimElevation", StringComparison.OrdinalIgnoreCase))
                                    res.ValueDouble = s.RimElevation;
                                else if (string.Equals(request.PropertyName, "SumpElevation", StringComparison.OrdinalIgnoreCase))
                                    res.ValueDouble = s.SumpElevation;
                                else if (string.Equals(request.PropertyName, "Description", StringComparison.OrdinalIgnoreCase))
                                    res.ValueString = s.Description;

                                tr.Commit();
                                return new IpcResponse { Success = true, ReadElementResult = res };
                            }
                        }
                    }
                    else if (string.Equals(request.TargetType, "Pipe", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (ObjectId pipeId in net.GetPipeIds())
                        {
                            Pipe p = tr.GetObject(pipeId, OpenMode.ForRead) as Pipe;
                            if (p != null && (string.Equals(p.Name, request.ElementName, StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(p.Handle.ToString(), request.ElementName, StringComparison.OrdinalIgnoreCase)))
                            {
                                var res = new Civil3dReadElementResultDto
                                {
                                    TargetType = "Pipe",
                                    ElementName = p.Name,
                                    PropertyName = request.PropertyName
                                };

                                if (string.Equals(request.PropertyName, "StartPointZ", StringComparison.OrdinalIgnoreCase))
                                    res.ValueDouble = p.StartPoint.Z;
                                else if (string.Equals(request.PropertyName, "EndPointZ", StringComparison.OrdinalIgnoreCase))
                                    res.ValueDouble = p.EndPoint.Z;
                                else if (string.Equals(request.PropertyName, "Description", StringComparison.OrdinalIgnoreCase))
                                    res.ValueString = p.Description;

                                tr.Commit();
                                return new IpcResponse { Success = true, ReadElementResult = res };
                            }
                        }
                    }
                }
            }

            return new IpcResponse { Success = false, ErrorMessage = "Target element not found: " + request.ElementName };
        }
    }
}
