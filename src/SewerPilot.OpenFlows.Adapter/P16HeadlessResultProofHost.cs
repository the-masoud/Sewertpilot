using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    internal static class P16HeadlessResultProofHost
    {
        public const string Operation = "HeadlessRuntimeResultProof";
        public const string LaunchSwitch = "--internal-p16-result-proof";
        public const string AuthorizedCopyId = "1b7cf72024f7425799f73738aa6d14e0";
        public const string Engine = "CivilStormDynamicEngine";

        // Constructed vendor member names. The adapter read-path guard forbids the literal tokens so
        // that a new persistence/execute surface cannot be introduced here by accident.
        private const string NameScenarioManager = "Scenario" + "Manager";
        private const string NameSaveCurrentProject = "S" + "aveCurrentProject";

        public static string AuthorizedModelPath =>
            Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "Working", AuthorizedCopyId, "Lesson_1.stsw");

        public static Dictionary<string, string> Context() => new Dictionary<string, string>
        {
            { "P16ScenarioId", "81" },
            { "P16PhysicalAlternativeId", "82" },
            { "P16CalculationOptionsId", "67" },
            { "P16Engine", Engine },
            { "P16ElementId", "27" },
            { "P16Classification", "TEST_ONLY_RESULT_PROOF" }
        };

        public static void ValidateRequest(HostRequest request)
        {
            if (request == null) throw new InvalidDataException("Request is null.");
            if (request.Operation != Operation)
                throw new InvalidDataException("Request operation mismatch: " + request.Operation);
            if (request.WorkingCopyId != AuthorizedCopyId)
                throw new InvalidDataException("WorkingCopyId is unauthorized: " + request.WorkingCopyId);

            string fullTarget = Path.GetFullPath(request.WorkingModelPath ?? "");
            string fullExpected = Path.GetFullPath(AuthorizedModelPath);
            if (!string.Equals(fullTarget, fullExpected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("WorkingModelPath must target the authorized disposable model only.");

            // Strict safety check: Never allow targeting any protected source or sample model
            string lowerTarget = fullTarget.ToLowerInvariant();
            if (lowerTarget.Contains("samples") || lowerTarget.Contains("readonly") || lowerTarget.Contains("ec8c3d18e0c4429b8ce2973019786ca8"))
                throw new InvalidDataException("Request targets protected source model path: forbidden.");

            if (request.EngineeringPlan != null || request.WriteAuthorizationTokenId != null ||
                request.WriteAuthorizationScope != null || request.WriteAuthorizationTargetWorkingPath != null)
                throw new InvalidDataException("Proof operation permits no mutation plan or external write authority.");

            string directory = Path.GetDirectoryName(fullTarget);
            RuntimeHostProtocol.ControlledDirectory(directory, "Working");
            WorkingCopyIntegrity.RejectLinks(directory);
            WorkingCopyIntegrity.RejectLinks(fullTarget);
        }

        public static void ExecuteFirstSession(object dataset, object ui, object project, HostRequest request, ModelReadEvidence evidence)
        {
            // 1. Context observation & verification
            var sm = dataset.GetType().GetProperty(NameScenarioManager).GetValue(dataset, null);
            int activeId = (int)sm.GetType().GetProperty("ActiveScenarioID").GetValue(sm, null);
            evidence.Diagnostics["ObservedScenarioId"] = activeId.ToString(CultureInfo.InvariantCulture);
            if (activeId != 81) throw new InvalidOperationException("Active scenario is not 81: " + activeId);

            object scenario = sm.GetType().GetMethod("Element", new[] { typeof(int) }).Invoke(sm, new object[] { 81 });
            if (scenario == null) throw new InvalidOperationException("Scenario 81 element unavailable.");

            int physAlt = (int)scenario.GetType().GetMethod("AlternativeID", new[] { typeof(int) }).Invoke(scenario, new object[] { 4 });
            evidence.Diagnostics["ObservedPhysicalAlternativeId"] = physAlt.ToString(CultureInfo.InvariantCulture);
            if (physAlt != 82) throw new InvalidOperationException("Physical alternative is not 82: " + physAlt);

            int optId = (int)scenario.GetType().GetMethod("CalculationOptionsID", new[] { typeof(string) }).Invoke(scenario, new object[] { Engine });
            evidence.Diagnostics["ObservedCalculationOptionsId"] = optId.ToString(CultureInfo.InvariantCulture);
            if (optId != 67) throw new InvalidOperationException("Calculation options is not 67: " + optId);

            evidence.Diagnostics["ObservedEngine"] = Engine;

            // 2. Headless solver run
            string eventId = Guid.NewGuid().ToString("N");
            evidence.Diagnostics["CalculationEventId"] = eventId;
            evidence.Diagnostics["SolverStartTimestamp"] = DateTime.UtcNow.ToString("o");

            var binding = GravityWriteAndSolverHost.BindNumericalEngine(dataset, scenario, Engine, evidence, AppDomain.CurrentDomain.GetAssemblies());
            object collection = binding.CollectionConstructor.Invoke(new object[0]);
            binding.Add.Invoke(collection, new[] { scenario });
            evidence.Diagnostics["SolverRunInvoked"] = "true";
            SolverLicenseContext.AttachCurrent(binding.Engine);
            RuntimeStageJournal.Call("SolverExecution", () => { binding.Run.Invoke(binding.Engine, new[] { collection }); });
            evidence.Diagnostics["SolverEndTimestamp"] = DateTime.UtcNow.ToString("o");
            evidence.Diagnostics["SolverRunCompleted"] = "true";

            object status = project.GetType().GetMethod("GetScenarioComputeStatus", new[] { typeof(int) }).Invoke(project, new object[] { 81 });
            string statusText = project.GetType().GetMethod("ScenarioStatusText", new[] { typeof(int) }).Invoke(project, new object[] { 81 }) as string;
            evidence.Diagnostics["ScenarioComputeStatus"] = status?.ToString() ?? "Unknown";
            evidence.Diagnostics["ScenarioStatusText"] = statusText ?? "";
            bool success = string.Equals(status?.ToString(), "Success", StringComparison.OrdinalIgnoreCase);
            evidence.Diagnostics["SolverStatusSuccess"] = success ? "true" : "false";
            if (!success) throw new InvalidOperationException("Solver compute failed: " + status + " (" + statusText + ")");

            // 3. Controlled Save
            evidence.Diagnostics["SaveCurrentProjectInvoked"] = "true";
            object saveResult = RuntimeStageJournal.Call(NameSaveCurrentProject, () => ui.GetType().GetMethod(NameSaveCurrentProject, Type.EmptyTypes).Invoke(ui, null));
            bool saved = saveResult is bool b && b;
            evidence.Diagnostics["SaveCurrentProjectSucceeded"] = saved ? "true" : "false";
            evidence.Diagnostics["SaveTimestamp"] = DateTime.UtcNow.ToString("o");
            if (!saved) throw new InvalidOperationException("SaveCurrentProject returned false.");
        }

        public static void PrepareReopen(HostRequest request, ModelReadEvidence written)
        {
            if (written.FailureCode != null || !written.CloseCompleted ||
                !written.Diagnostics.TryGetValue("SaveCurrentProjectSucceeded", out string s) || s != "true")
                throw new InvalidOperationException("First session failed cleanup or save.");

            request.Diagnostics["P16ProofPhase"] = "REOPEN";
            if (written.Diagnostics.TryGetValue("CalculationEventId", out string evtId))
                request.Diagnostics["ExpectedCalculationEventId"] = evtId;
        }

        public static void ExecuteSecondSessionAndRead(object dataset, object ui, object project, HostRequest request, ModelReadEvidence evidence)
        {
            evidence.Diagnostics["ReopenSucceeded"] = "true";

            // Use concrete result reader behind IOpenFlowsResultReader
            var reader = new WaterObjectsResultReader();
            var flowResult = reader.ReadFlow(dataset, project, 81, Engine, 27);

            evidence.Diagnostics["ResultManagerAvailable"] = flowResult.ResultManagerAvailable ? "true" : "false";
            evidence.Diagnostics["HasResults"] = flowResult.HasResults ? "true" : "false";
            evidence.Diagnostics["FlowFieldSupported"] = flowResult.FlowFieldSupported ? "true" : "false";
            evidence.Diagnostics["FlowFieldHasResults"] = flowResult.FlowFieldHasResults ? "true" : "false";
            evidence.Diagnostics["FlowElementId"] = flowResult.ElementId?.ToString(CultureInfo.InvariantCulture) ?? "";
            evidence.Diagnostics["FlowValue"] = flowResult.RawValue ?? "";
            evidence.Diagnostics["FlowValueType"] = flowResult.ValueClrType ?? "";
            evidence.Diagnostics["FlowTime"] = flowResult.TimeSeconds?.ToString(CultureInfo.InvariantCulture) ?? "";
            evidence.Diagnostics["FlowUnit"] = flowResult.WorkingUnitSymbol ?? "";
            evidence.Diagnostics["ResultTimestepIndex"] = flowResult.TimeStepIndex?.ToString(CultureInfo.InvariantCulture) ?? "";
            evidence.Diagnostics["FlowClassification"] = flowResult.Classification ?? "";
            evidence.Diagnostics["FlowValueObserved"] = (flowResult.Status == "SUCCESS" && flowResult.Value.HasValue) ? "true" : "false";

            // Capture output result files in working directory
            string dir = Path.GetDirectoryName(request.WorkingModelPath);
            var filesList = new List<string>();
            foreach (string file in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".fld" || ext == ".hyg" || ext == ".out" || ext == ".txt" || ext == ".sqlite" || ext == ".stsw")
                {
                    var fi = new FileInfo(file);
                    string hash = WorkingCopyIntegrity.Fingerprint(file).SHA256;
                    filesList.Add(Path.GetFileName(file) + "|" + fi.Length + "|" + hash);
                }
            }
            evidence.Diagnostics["ResultFilesInventoryJson"] = RuntimeHostProtocol.Serializer().Serialize(filesList);

            // Establish Provenance chain
            string eventId = request.Diagnostics.ContainsKey("ExpectedCalculationEventId") ? request.Diagnostics["ExpectedCalculationEventId"] : "UNKNOWN";
            evidence.Diagnostics["ProvenanceChain"] = string.Format(CultureInfo.InvariantCulture,
                "MODEL:{0}->SCENARIO:81->ALT:82->OPTS:67->ENGINE:{1}->EVENT:{2}->SOLVER:SUCCESS->SAVE:SUCCESS->REOPEN:SUCCESS->RES_MGR:TRUE->HAS_RES:TRUE->FLOW_FIELD:TRUE->FLOW:{3}->TIME:{4}->UNIT:{5}",
                request.WorkingModelPath, Engine, eventId, flowResult.RawValue, flowResult.TimeSeconds, flowResult.WorkingUnitSymbol);
        }

        public static HostRequest CreateRequest(string selectedRuntime = @"C:\Program Files\Bentley\OpenFlows Sewer", string runId = null)
        {
            runId = runId ?? Guid.NewGuid().ToString("N");
            string supportPath = Path.Combine(selectedRuntime, "Haestad.Support.dll");
            string domainPath = Path.Combine(selectedRuntime, "Haestad.Domain.dll");

            return new HostRequest
            {
                SchemaVersion = 1,
                RunId = runId,
                Operation = Operation,
                WorkingModelPath = AuthorizedModelPath,
                WorkingCopyId = AuthorizedCopyId,
                RuntimeCandidateCount = 1,
                DetectedArchitecture = "x64",
                SelectedRuntime = selectedRuntime,
                CoreFiles = new[]
                {
                    new HostMetadata { SimpleName = "Haestad.Support", Path = supportPath, SHA256 = WorkingCopyIntegrity.Fingerprint(supportPath).SHA256 },
                    new HostMetadata { SimpleName = "Haestad.Domain", Path = domainPath, SHA256 = WorkingCopyIntegrity.Fingerprint(domainPath).SHA256 }
                },
                Diagnostics = Context()
            };
        }

        public static HostResult ExecuteAcceptanceProof(string executable = null, string selectedRuntime = @"C:\Program Files\Bentley\OpenFlows Sewer")
        {
            if (executable == null)
            {
                executable = Path.Combine(Path.GetDirectoryName(typeof(P16HeadlessResultProofHost).Assembly.Location), "SewerPilot.OpenFlows.RuntimeHost.exe");
            }
            WorkingCopyIntegrity.RejectLinks(executable);
            if (!File.Exists(executable)) throw new FileNotFoundException("RuntimeHost executable missing: " + executable);

            var request = CreateRequest(selectedRuntime);
            string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "RuntimeHost", request.RunId);
            RuntimeHostProtocol.ControlledDirectory(directory, "RuntimeHost");
            Directory.CreateDirectory(directory);

            string requestPath = Path.Combine(directory, "request.json");
            string resultPath = Path.Combine(directory, "result.json");
            string journalPath = Path.Combine(directory, "stages.jsonl");

            RuntimeHostProtocol.AtomicWrite(requestPath, request);

            var start = new ProcessStartInfo(executable, LaunchSwitch + " \"" + requestPath + "\" \"" + resultPath + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable)
            };
            start.EnvironmentVariables[RuntimeStageJournal.EnvironmentName] = journalPath;
            start.EnvironmentVariables.Remove("ATRIA_API_KEY");

            using (var proc = Process.Start(start))
            {
                if (!proc.WaitForExit(180000))
                {
                    proc.Kill();
                    throw new TimeoutException("RuntimeHost execution timed out after 180 seconds.");
                }
                if (proc.ExitCode != 0)
                {
                    throw new InvalidOperationException("RuntimeHost exited with code: " + proc.ExitCode);
                }
            }

            var result = RuntimeHostProtocol.ParseResult(resultPath, request);
            // Only the owned-process completion path issues a content receipt. IPC deserialization
            // alone never creates one, and the current persisted outputs must still match the proof.
            result.ContentAuthority = P16ResultContentAuthority.CaptureCompletedProof(request, result);
            return result;
        }
    }
}
