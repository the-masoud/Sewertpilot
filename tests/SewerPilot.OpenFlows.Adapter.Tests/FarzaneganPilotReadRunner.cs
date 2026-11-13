using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static class FarzaneganPilotReadRunner
    {
        internal static int Run(string calculationEvidenceDir, string readEvidenceDir)
        {
            string savedModel = null;
            string[] originalFamily = null;
            string[] before = null;
            try
            {
                string calculationJson = File.ReadAllText(Path.Combine(calculationEvidenceDir, "calculation-observation.json"));
                var calc = RuntimeHostProtocol.Serializer().Deserialize<P16FarzaneganNativePilotHost.Observation>(calculationJson);
                if (calc == null ||
                    (calc.Status != "CALCULATED_SAVED_AWAITING_REOPEN" &&
                     calc.Status != "CALCULATED_SAVED_WITH_WARNINGS_AWAITING_REOPEN") ||
                    !calc.SaveSucceeded || calc.SolverRunInvocations != 1 || calc.SaveInvocations != 1 ||
                    calc.ScenarioComputeStatus != "Success" ||
                    !string.Equals(calc.Engine, P16FarzaneganNativePilotHost.Engine, StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(calc.CalculationEvent) ||
                    calc.ModifiedFiles.Length == 0 || string.IsNullOrEmpty(calc.OutputInventory))
                    throw new InvalidDataException("Farzanegan saved calculation does not carry one completed solver/save and complete lineage.");
                savedModel = Path.GetFullPath(calc.WorkingModel);
                WorkingCopyIntegrity.RejectLinks(savedModel);
                if (!File.Exists(savedModel)) throw new FileNotFoundException("Saved model is absent.");
                string originalCopy = Path.Combine(@"C:\Users\M.Zeynali\Documents\SewerPilot",
                    @"engineering\customer-data\Farzanegan\SourceCopy_20261027\Model-SewerGEM");
                originalFamily = P16FarzaneganNativePilotHost.Family.Select(n => Path.Combine(originalCopy,n)).ToArray();
                before = P16AzadeganCalculationProof.Manifest(originalFamily).ToArray();
                var savedFiles = P16FarzaneganNativePilotHost.Family.Select(n =>
                    Path.Combine(Path.GetDirectoryName(savedModel),n)).ToArray();
                if (savedFiles.Any(p=>!File.Exists(p))) throw new FileNotFoundException("Persisted model family incomplete.");
                string inventory = P16AzadeganCalculationHost.InventoryJson(
                    P16AzadeganCalculationHost.Inventory(Path.GetDirectoryName(savedModel)));
                if (inventory != calc.OutputInventory)
                    throw new InvalidDataException("Saved calculation files differ from recorded post-save inventory.");
                var firstRequest = RuntimeHostProtocol.Serializer().Deserialize<HostRequest>(
                    File.ReadAllText(Path.Combine(calculationEvidenceDir,"request.json")));
                if (!P14FarzaneganTestOnlyHost.IsPilot(firstRequest.Operation) ||
                    !P16SaveQuestionPolicy.SamePath(firstRequest.WorkingModelPath,savedModel))
                    throw new InvalidDataException("Saved model path is not identical to calculation request.");
                var readCopy = WorkingCopyIntegrity.Create(savedModel, savedFiles, "ReadOnly");
                var readInventory = P16AzadeganCalculationHost.Inventory(readCopy.DirectoryPath);
                if (P16AzadeganCalculationHost.InventoryJson(readInventory) != inventory)
                    throw new InvalidDataException("Read-only working model differs from the persisted calculation.");
                string id = Path.GetFileName(readCopy.DirectoryPath);
                var request = new HostRequest {
                    SchemaVersion=1,
                    RunId=Guid.NewGuid().ToString("N"),
                    Operation=P16FarzaneganNativeReadHost.Operation,
                    WorkingModelPath=readCopy.ModelPath,
                    WorkingCopyId=id,
                    RuntimeCandidateCount=firstRequest.RuntimeCandidateCount,
                    DetectedArchitecture=firstRequest.DetectedArchitecture,
                    SelectedRuntime=firstRequest.SelectedRuntime,
                    CoreFiles=firstRequest.CoreFiles,
                    Diagnostics=new Dictionary<string,string>(StringComparer.Ordinal) {
                        ["FarzaneganReadCopyId"]=id,
                        ["FarzaneganReadScenario"]="1",
                        ["FarzaneganReadOptions"]="29",
                        ["FarzaneganReadEngine"]=P16FarzaneganNativePilotHost.Engine,
                        ["FarzaneganReadEvent"]=calc.CalculationEvent,
                        ["FarzaneganReadOutputInventory"]=calc.OutputInventory,
                        ["FarzaneganReadInputSHA256"]=P16AzadeganCalculationHost.InventoryDigest(readInventory)
                    }
                };
                P16FarzaneganNativeReadHost.ValidateRequest(request);
                if (Directory.Exists(readEvidenceDir)) throw new InvalidOperationException("Read evidence directory already exists.");
                Directory.CreateDirectory(readEvidenceDir);
                File.WriteAllText(Path.Combine(readEvidenceDir, "read-preflight.txt"),
                    "CALC_EVENT="+calc.CalculationEvent+"\r\nCALC_STATUS="+calc.Status+
                    "\r\nSOLVER_CALLS_THIS_READ=0\r\nSAVE_CALLS_THIS_READ=0\r\nWATCHDOG_MS=60000");
                string runDir = Path.Combine(Path.GetTempPath(),"SewerPilot","OpenFlows","RuntimeHost",request.RunId);
                RuntimeHostProtocol.ControlledDirectory(runDir,"RuntimeHost");
                if (Directory.Exists(runDir)) throw new IOException("IPC collision.");
                Directory.CreateDirectory(runDir);
                string reqPath=Path.Combine(runDir,"request.json"),resultPath=Path.Combine(runDir,"result.json");
                RuntimeHostProtocol.AtomicWrite(reqPath,request);
                File.WriteAllText(Path.Combine(readEvidenceDir,"request.json"),RuntimeHostProtocol.Serializer().Serialize(request));
                string exe=P16AzadeganCalculationProof.DefaultRuntimeHostExecutable();
                if(string.IsNullOrEmpty(exe)||!File.Exists(exe))throw new FileNotFoundException("Release RuntimeHost missing.");
                var start=new ProcessStartInfo(exe,P16FarzaneganNativeReadHost.LaunchSwitch+
                    " \""+reqPath+"\" \""+resultPath+"\"") {
                    UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(exe)
                };
                using(var proc=Process.Start(start))
                {
                    if(proc==null)throw new InvalidOperationException("Farzanegan read host did not start.");
                    int pid=proc.Id;
                    if(!proc.WaitForExit(60000))
                    {
                        OwnedProcessTerminator.TryTerminate(proc,pid);
                        throw new TimeoutException("Farzanegan read host exceeded its 60 second bound. No retry.");
                    }
                    Console.WriteLine("READ_EXIT="+proc.ExitCode);
                }
                string journal=Path.Combine(runDir,"stages.jsonl");
                if(File.Exists(journal))File.Copy(journal,Path.Combine(readEvidenceDir,"stages.jsonl"));
                if(!File.Exists(resultPath))throw new InvalidDataException("READ result missing.");
                File.Copy(resultPath,Path.Combine(readEvidenceDir,"result.json"));
                var raw=RuntimeHostProtocol.Serializer().Deserialize<HostResult>(File.ReadAllText(resultPath));
                Console.WriteLine("READ_SUCCESS="+raw.Success);
                Console.WriteLine("READ_FAILURE="+raw.FailureClassification);
                Console.WriteLine("READ_SAVE_CALLS="+raw.SaveInvocationCount);
                Console.WriteLine("READ_SOLVER_CALLS="+raw.SolverRunInvocationCount);
                if(raw.Diagnostics.TryGetValue(P16FarzaneganNativeReadHost.EvidenceKey,out string observation))
                {
                    File.WriteAllText(Path.Combine(readEvidenceDir,"read-observation.json"),observation);
                    var value=RuntimeHostProtocol.Serializer().Deserialize<P16FarzaneganNativeReadHost.Observation>(observation);
                    Console.WriteLine("READ_STATUS="+value.Status);
                    Console.WriteLine("FIELD="+value.Field);
                    Console.WriteLine("ELEMENT="+value.ElementId+" ("+value.ElementLabel+")");
                    Console.WriteLine("SCENARIO="+value.ScenarioId);
                    Console.WriteLine("FLOW="+value.RawValue+" "+value.UnitSymbol);
                    Console.WriteLine("TIMESTEP_SECONDS="+value.TimeSeconds?.ToString("R",CultureInfo.InvariantCulture));
                    Console.WriteLine("HAS_RESULTS="+value.HasResults);
                    Console.WriteLine("LINEAGE="+value.CalculationEvent);
                }
                RuntimeHostProtocol.ParseResult(resultPath,request);
                string savedAfter=P16AzadeganCalculationHost.InventoryJson(
                    P16AzadeganCalculationHost.Inventory(Path.GetDirectoryName(savedModel)));
                if (savedAfter != inventory)
                    throw new InvalidDataException("The original persisted working copy changed during read-only reopen.");
                bool protectedFiles=before.SequenceEqual(P16AzadeganCalculationProof.Manifest(originalFamily));
                Console.WriteLine("PROTECTED_SEVEN_FILES_UNCHANGED="+protectedFiles);
                if(!protectedFiles)throw new InvalidDataException("Protected Farzanegan source copy changed.");
                return raw.Success?0:3;
            }
            catch(Exception error)
            {
                Console.Error.WriteLine("FARZANEGAN_READ_BLOCKED="+error.GetType().FullName+": "+error.Message);
                return 2;
            }
            finally
            {
                if(originalFamily!=null && before!=null)
                {
                    bool unchanged=before.SequenceEqual(P16AzadeganCalculationProof.Manifest(originalFamily));
                    Console.WriteLine("ORIGINAL_SEVEN_FILES_UNCHANGED="+unchanged);
                }
            }
        }
    }
}
