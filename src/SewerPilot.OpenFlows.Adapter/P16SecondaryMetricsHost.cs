using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    internal static class P16SecondaryMetricsHost
    {
        public const string Operation = "HeadlessSecondaryMetricsAcceptance";
        public const string LaunchSwitch = "--internal-p16-secondary-metrics";
        public const string AuthorizedCopyId = "1b7cf72024f7425799f73738aa6d14e0";
        public const string Engine = "CivilStormDynamicEngine";
        public const string CalculationEventId = "93d8ddb063d34917bea7ddd27a2ad5ca";
        private const string NameScenarioManager = "Scenario" + "Manager";

        public static string AuthorizedModelPath =>
            Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "Working", AuthorizedCopyId, "Lesson_1.stsw");

        public static Dictionary<string, string> Context() => new Dictionary<string, string>
        {
            { "P16ScenarioId", "81" },
            { "P16PhysicalAlternativeId", "82" },
            { "P16CalculationOptionsId", "67" },
            { "P16Engine", Engine },
            { "P16ElementId", "27" },
            { "P16Classification", "TEST_ONLY_SECONDARY_METRICS" }
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

            string lowerTarget = fullTarget.ToLowerInvariant();
            if (lowerTarget.Contains("samples") || lowerTarget.Contains("readonly") || lowerTarget.Contains("ec8c3d18e0c4429b8ce2973019786ca8"))
                throw new InvalidDataException("Request targets protected source model path: forbidden.");

            if (request.EngineeringPlan != null || request.WriteAuthorizationTokenId != null ||
                request.WriteAuthorizationScope != null || request.WriteAuthorizationTargetWorkingPath != null)
                throw new InvalidDataException("Secondary metrics operation permits no mutation plan or external write authority.");

            string directory = Path.GetDirectoryName(fullTarget);
            RuntimeHostProtocol.ControlledDirectory(directory, "Working");
            WorkingCopyIntegrity.RejectLinks(directory);
            WorkingCopyIntegrity.RejectLinks(fullTarget);
        }

        public static void ExecuteRead(object dataset, object ui, object project, HostRequest request, ModelReadEvidence evidence)
        {
            // Context observation & verification
            var sm = dataset.GetType().GetProperty(NameScenarioManager).GetValue(dataset, null);
            int activeId = (int)sm.GetType().GetProperty("ActiveScenarioID").GetValue(sm, null);
            evidence.Diagnostics["ObservedScenarioId"] = activeId.ToString(CultureInfo.InvariantCulture);
            if (activeId != 81)
            {
                evidence.Diagnostics["SecondaryMetricsFirstBlocker"] = "ACTIVE_SCENARIO_NOT_81";
                throw new InvalidOperationException("Active scenario is not 81: " + activeId);
            }

            object scenario = sm.GetType().GetMethod("Element", new[] { typeof(int) }).Invoke(sm, new object[] { 81 });
            if (scenario == null)
            {
                evidence.Diagnostics["SecondaryMetricsFirstBlocker"] = "SCENARIO_81_UNAVAILABLE";
                throw new InvalidOperationException("Scenario 81 element unavailable.");
            }

            int physAlt = (int)scenario.GetType().GetMethod("AlternativeID", new[] { typeof(int) }).Invoke(scenario, new object[] { 4 });
            evidence.Diagnostics["ObservedPhysicalAlternativeId"] = physAlt.ToString(CultureInfo.InvariantCulture);
            if (physAlt != 82)
            {
                evidence.Diagnostics["SecondaryMetricsFirstBlocker"] = "PHYSICAL_ALTERNATIVE_NOT_82";
                throw new InvalidOperationException("Physical alternative is not 82: " + physAlt);
            }

            int optId = (int)scenario.GetType().GetMethod("CalculationOptionsID", new[] { typeof(string) }).Invoke(scenario, new object[] { Engine });
            evidence.Diagnostics["ObservedCalculationOptionsId"] = optId.ToString(CultureInfo.InvariantCulture);
            if (optId != 67)
            {
                evidence.Diagnostics["SecondaryMetricsFirstBlocker"] = "CALCULATION_OPTIONS_NOT_67";
                throw new InvalidOperationException("Calculation options is not 67: " + optId);
            }

            evidence.Diagnostics["ObservedEngine"] = Engine;

            var reader = new WaterObjectsResultReader();

            // Check ResultManager and HasResults on default engine
            var domainAsm = Assembly.Load("Haestad.Domain");
            Type scenarioType = domainAsm.GetType("Haestad.Domain.IScenario", false);
            Type resultManagerType = domainAsm.GetType("Haestad.Domain.IResultManager", false);
            object manager = scenario.GetType().GetMethod("ResultManager", new[] { typeof(string) }).Invoke(scenario, new object[] { Engine });
            bool rmAvail = manager != null;
            evidence.Diagnostics["ResultManagerAvailable"] = rmAvail ? "true" : "false";
            if (!rmAvail)
            {
                evidence.Diagnostics["SecondaryMetricsFirstBlocker"] = "RESULT_MANAGER_UNAVAILABLE";
                throw new InvalidOperationException("ResultManager unavailable for " + Engine);
            }

            object activeObj = resultManagerType?.GetProperty("IsActive")?.GetValue(manager, null) ?? manager.GetType().GetProperty("IsActive")?.GetValue(manager, null);
            if (!(activeObj is bool isActive && isActive))
            {
                (resultManagerType?.GetMethod("Open", Type.EmptyTypes) ?? manager.GetType().GetMethod("Open", Type.EmptyTypes))?.Invoke(manager, null);
            }

            object hasResObj = resultManagerType?.GetProperty("HasResults")?.GetValue(manager, null) ?? manager.GetType().GetProperty("HasResults")?.GetValue(manager, null);
            bool hasResults = hasResObj is bool hr && hr;
            evidence.Diagnostics["HasResults"] = hasResults ? "true" : "false";
            if (!hasResults)
            {
                evidence.Diagnostics["SecondaryMetricsFirstBlocker"] = "HAS_RESULTS_FALSE";
                throw new InvalidOperationException("HasResults is false on ResultManager");
            }

            // Close manager so that individual reader calls manage their lifecycle cleanly
            try { (resultManagerType?.GetMethod("Close", Type.EmptyTypes) ?? manager.GetType().GetMethod("Close", Type.EmptyTypes))?.Invoke(manager, null); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in P16SecondaryMetricsHost.cs: " + suppressedEx.Message); }

            // Metric Acceptance in Exact Order:
            // 1. Velocity
            var vel = reader.ReadVelocity(dataset, project, 81, Engine, 27);
            RecordMetricDiagnostics(evidence, "Velocity", vel);
            if (vel.Status != "SUCCESS")
            {
                evidence.Diagnostics["SecondaryMetricsFirstBlocker"] = "VELOCITY_" + vel.Status;
                return;
            }

            // 2. Depth
            var depth = reader.ReadDepth(dataset, project, 81, Engine, 27);
            RecordMetricDiagnostics(evidence, "Depth", depth);
            if (depth.Status != "SUCCESS")
            {
                evidence.Diagnostics["SecondaryMetricsFirstBlocker"] = "DEPTH_" + depth.Status;
                return;
            }

            // 3. Capacity
            var cap = reader.ReadCapacity(dataset, project, 81, Engine, 27);
            RecordMetricDiagnostics(evidence, "Capacity", cap);
            if (cap.Status != "SUCCESS")
            {
                evidence.Diagnostics["SecondaryMetricsFirstBlocker"] = "CAPACITY_" + cap.Status;
                return;
            }

            // 4. HGL
            var hgl = reader.ReadHgl(dataset, project, 81, Engine, 27);
            RecordMetricDiagnostics(evidence, "Hgl", hgl);
            if (hgl.Status != "SUCCESS")
            {
                evidence.Diagnostics["SecondaryMetricsFirstBlocker"] = "HGL_" + hgl.Status;
                return;
            }

            // 5. Surcharge
            var sur = reader.ReadSurcharge(dataset, project, 81, Engine, 27);
            RecordMetricDiagnostics(evidence, "Surcharge", sur);
            if (sur.Status != "SUCCESS")
            {
                evidence.Diagnostics["SecondaryMetricsFirstBlocker"] = "SURCHARGE_" + sur.Status;
                return;
            }

            evidence.Diagnostics["SecondaryMetricsCompleted"] = "true";
            evidence.Diagnostics["CalculationEventId"] = CalculationEventId;

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

            evidence.Diagnostics["ProvenanceChain"] = string.Format(CultureInfo.InvariantCulture,
                "MODEL:{0}->SCENARIO:81->ALT:82->OPTS:67->ENGINE:{1}->EVENT:{2}->RES_MGR:TRUE->HAS_RES:TRUE->VEL:{3}->DEP:{4}->CAP:{5}->HGL:{6}->SUR:{7}",
                request.WorkingModelPath, Engine, CalculationEventId, vel.RawValue, depth.RawValue, cap.RawValue, hgl.RawValue, sur.RawValue);
        }

        private static void RecordMetricDiagnostics(ModelReadEvidence evidence, string prefix, WaterObjectsMetricResult metric)
        {
            evidence.Diagnostics[prefix + "Field"] = metric.FieldName ?? "";
            evidence.Diagnostics[prefix + "Record"] = metric.RecordName ?? "";
            evidence.Diagnostics[prefix + "Engine"] = metric.EngineName ?? "";
            evidence.Diagnostics[prefix + "FieldSupported"] = metric.FieldSupported ? "true" : "false";
            evidence.Diagnostics[prefix + "FieldHasResults"] = metric.FieldHasResults ? "true" : "false";
            evidence.Diagnostics[prefix + "ElementId"] = metric.ElementId?.ToString(CultureInfo.InvariantCulture) ?? "";
            evidence.Diagnostics[prefix + "Value"] = metric.RawValue ?? "";
            evidence.Diagnostics[prefix + "ValueType"] = metric.ValueClrType ?? "";
            evidence.Diagnostics[prefix + "Time"] = metric.TimeSeconds?.ToString(CultureInfo.InvariantCulture) ?? "";
            evidence.Diagnostics[prefix + "Unit"] = metric.WorkingUnitSymbol ?? "";
            evidence.Diagnostics[prefix + "TimestepIndex"] = metric.TimeStepIndex?.ToString(CultureInfo.InvariantCulture) ?? "";
            evidence.Diagnostics[prefix + "Semantics"] = metric.Semantics ?? "";
            if (!string.IsNullOrEmpty(metric.DatumSemantics))
                evidence.Diagnostics[prefix + "DatumSemantics"] = metric.DatumSemantics;
            evidence.Diagnostics[prefix + "Classification"] = metric.Classification ?? "";
            evidence.Diagnostics[prefix + "Status"] = metric.Status ?? "";
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

        public static HostResult ExecuteAcceptance(string executable = null, string selectedRuntime = @"C:\Program Files\Bentley\OpenFlows Sewer")
        {
            if (executable == null)
            {
                executable = Path.Combine(Path.GetDirectoryName(typeof(P16SecondaryMetricsHost).Assembly.Location), "SewerPilot.OpenFlows.RuntimeHost.exe");
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

            return RuntimeHostProtocol.ParseResult(resultPath, request);
        }
    }
}
