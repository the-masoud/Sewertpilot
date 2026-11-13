using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    // A controlled, single-model second-customer native solve; it must not reuse P16 Azadegan
    // scenario / element constants. No engineering values, topology or alternatives are altered.
    internal static class P16FarzaneganNativePilotHost
    {
        internal const string Operation = "P16FarzaneganNativePilot";
        internal const string LaunchSwitch = "--p16-farzanegan-native-pilot";
        internal const string ModelFileName = "FEP-AOC-030826.stsw";
        internal const string Engine = "DelawareProductEngine";
        internal const int ScenarioId = 1;
        internal const int OptionsId = 29;
        internal const int FirstConduitId = 52;
        internal const string EvidenceKey = "FarzaneganNativePilotObservationJson";
        internal const string PassFlag = "FarzaneganPilotPersisted";
        internal static readonly string[] Family = {
            ModelFileName, ModelFileName + ".dwh", ModelFileName + ".profiles.dwh",
            ModelFileName + ".scm", ModelFileName + ".sqlite",
            ModelFileName + "_1_1-stmc-gvf.out", "catchments.dxf"
        };

        internal sealed class Observation
        {
            public string Status { get; set; } = "NOT_STARTED";
            public string WorkingModel { get; set; }
            public string CalculationEvent { get; set; }
            public int ActiveScenarioId { get; set; }
            public int CalculationOptionsId { get; set; }
            public string Engine { get; set; }
            public int SolverRunInvocations { get; set; }
            public string SolverStartUtc { get; set; }
            public string SolverEndUtc { get; set; }
            public string ScenarioComputeStatus { get; set; }
            public string EngineExceptionType { get; set; }
            public string NotificationClassification { get; set; }
            public P16AzadeganEngineNotification[] Notifications { get; set; } = new P16AzadeganEngineNotification[0];
            public bool NotificationScopeComplete { get; set; }
            public int SaveInvocations { get; set; }
            public string SaveResultClassification { get; set; }
            public bool SaveSucceeded { get; set; }
            public string SaveUtc { get; set; }
            public string InputInventory { get; set; }
            public string OutputInventory { get; set; }
            public string[] ModifiedFiles { get; set; } = new string[0];
            public string[] CreatedFiles { get; set; } = new string[0];
            public string FailureOperation { get; set; }
            public string ErrorType { get; set; }
            public string ErrorMessage { get; set; }
        }

        private static Type Contract(string assemblyName, string typeName)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && a.GetName().Name == assemblyName).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Vendor contract assembly is missing or ambiguous: " + assemblyName);
            return matches[0].GetType(typeName, true, false);
        }

        internal static void ValidateRequest(HostRequest request)
        {
            // The catalog-size selector is qualified by the read-only P14 catalog probe; the
            // remediation route is therefore a genuine catalog selection, not a diameter trial.
            if (request == null || !P14FarzaneganTestOnlyHost.IsPilot(request.Operation) || request.SchemaVersion != 1 ||
                request.EngineeringPlan != null || request.WriteAuthorizationTokenId != null ||
                request.WriteAuthorizationScope != null || request.WriteAuthorizationTargetWorkingPath != null)
                throw new InvalidDataException("Farzanegan pilot is only a controlled single-scenario native calculation.");
            if (request.Diagnostics == null || request.Diagnostics.Count != 5 ||
                !request.Diagnostics.TryGetValue("FarzaneganPilotCopyId", out string copyId) ||
                !System.Text.RegularExpressions.Regex.IsMatch(copyId ?? "", "^[a-f0-9]{32}$") ||
                request.WorkingCopyId != copyId ||
                !request.Diagnostics.TryGetValue("FarzaneganPilotScenario", out string scenario) || scenario != "1" ||
                !request.Diagnostics.TryGetValue("FarzaneganPilotOptions", out string options) || options != "29" ||
                !request.Diagnostics.TryGetValue("FarzaneganPilotEngine", out string engine) || engine != Engine ||
                !request.Diagnostics.TryGetValue("FarzaneganPilotInputSHA256", out string digest) ||
                !System.Text.RegularExpressions.Regex.IsMatch(digest ?? "", "^[A-F0-9]{64}$"))
                throw new InvalidDataException("Farzanegan pilot requires an exact bounded source-model context.");
            if ((request.Operation == P14FarzaneganTestOnlyHost.Operation || request.Operation == FarzaneganHydraulicRemediationHost.Operation || request.Operation == P14CatalogPointerPersistenceHost.Operation || request.Operation==P14CatalogPointerPersistenceHost.PrepareOperation) &&
                digest != "F401E3DC32ABDEFB143D0DA0DD9A0931E04CBE3A4CD7C5C19B8C7A72944EA973")
                throw new InvalidDataException("P14 TEST_ONLY requires the qualified original seven-file fingerprint.");
            if(request.Operation==P14CatalogPointerPersistenceHost.LabelRepairOperation &&
                digest!=P14CatalogPointerPersistenceHost.LabelRepairInputDigest)
                throw new InvalidDataException("Label repair requires the exact independently verified saved candidate fingerprint.");
            if(request.Operation==P14CatalogPointerPersistenceHost.ScopedRepairOperation && digest!=P14CatalogPointerPersistenceHost.ScopedRepairInputDigest)
                throw new InvalidDataException("Scoped repair requires exact independently verified P14 candidate fingerprint.");
            if(request.Operation==P14CatalogPointerPersistenceHost.AnalysisPrepareOperation && digest!=P14CatalogPointerPersistenceHost.AnalysisInputDigest)
                throw new InvalidDataException("Analysis preparation requires exact repaired P14 candidate fingerprint.");
            if(request.Operation==P16AnalysisRemediationHost.PrepareOperation && digest!=P16AnalysisRemediationHost.InputDigest)
                throw new InvalidDataException("Remediation requires exact verified Analysis baseline fingerprint.");
            string path = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "Working", copyId, ModelFileName);
            if (!P16SaveQuestionPolicy.SamePath(path, request.WorkingModelPath))
                throw new InvalidDataException("Farzanegan pilot must operate only on its own disposable copy.");
            RuntimeHostProtocol.ControlledDirectory(Path.GetDirectoryName(path), "Working");
            WorkingCopyIntegrity.RejectLinks(path);
            string directory = Path.GetDirectoryName(path);
            if (Family.Any(name => !File.Exists(Path.Combine(directory, name))))
                throw new InvalidDataException("The copied seven-file Farzanegan family is incomplete.");
        }

        internal static void Execute(object dataset, object project, HostRequest request, ModelReadEvidence evidence)
        {
            var obs = new Observation { WorkingModel = request.WorkingModelPath, Engine = Engine };
            var calls = new P16AzadeganCalculationHost.Vendor();
            try
            {
                Type dataType = Contract("Haestad.Domain", "Haestad.Domain.IDomainDataSet");
                Type managerType = Contract("Haestad.Domain", "Haestad.Domain.IScenarioManager");
                Type elementManagerType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElementManager");
                Type elementType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElement");
                Type scenarioType = Contract("Haestad.Domain", "Haestad.Domain.IScenario");
                object scenarioManager = calls.Get(dataset, dataType, "ScenarioManager", managerType);
                obs.ActiveScenarioId = (int)calls.Get(scenarioManager, managerType, "ActiveScenarioID", typeof(int));
                if (obs.ActiveScenarioId != ScenarioId) { obs.Status = "ACTIVE_SCENARIO_MISMATCH"; return; }
                object scenario = calls.Invoke(scenarioManager, elementManagerType, "Element", elementType, new[] { typeof(int) }, ScenarioId);
                if (!scenarioType.IsInstanceOfType(scenario)) throw new InvalidDataException("Scenario 1 does not implement vendor IScenario.");
                obs.CalculationOptionsId = (int)calls.Invoke(scenario, scenarioType, "CalculationOptionsID", typeof(int),
                    new[] { typeof(string) }, Engine);
                if (obs.CalculationOptionsId != OptionsId)
                { obs.Status = "MODEL_ENGINE_OR_OPTIONS_MISMATCH"; return; }
                if (evidence.FirstConduitElementID != FirstConduitId)
                { obs.Status = "MODEL_FIRST_CONDUIT_ID_MISMATCH"; return; }

                string directory = Path.GetDirectoryName(request.WorkingModelPath);
                var before = P16AzadeganCalculationHost.Inventory(directory);
                obs.InputInventory = P16AzadeganCalculationHost.InventoryJson(before);
                string digest = P16AzadeganCalculationHost.InventoryDigest(before);
                if (digest != request.Diagnostics["FarzaneganPilotInputSHA256"])
                { obs.Status = "INPUT_HASH_MISMATCH"; return; }
                if (P14CatalogPointerPersistenceHost.IsOperation(request.Operation))
                {
                    P14CatalogPointerPersistenceHost.Execute(dataset, project, scenario, request, evidence);
                    obs.Status = request.Operation==P14CatalogPointerPersistenceHost.PrepareOperation ? "P14_FOUR_INVERTS_SAVED_AWAITING_INDEPENDENT_READBACK" : request.Operation==P14CatalogPointerPersistenceHost.LabelRepairOperation ? "NATIVE_LABEL_REPAIR_SAVED_AWAITING_INDEPENDENT_READBACK" : "CATALOG_POINTER_SAVED_AWAITING_INDEPENDENT_READBACK";
                    obs.SaveInvocations = 1;
                    obs.SaveResultClassification = "PERSISTED";
                    obs.SaveSucceeded = true;
                    obs.SaveUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    obs.SolverRunInvocations = 0;
                    obs.ScenarioComputeStatus = "NOT_RUN_BY_DESIGN";
                    evidence.Diagnostics[PassFlag] = "true";
                    return;
                }
                bool remediationCompute=request.Operation==P16AnalysisRemediationHost.ComputeOperation;
                if(remediationCompute) P16AnalysisRemediationHost.VerifyCompute(dataset,scenario,evidence,"BeforeBinding");
                bool scopedCompute=request.Operation==P14CatalogPointerPersistenceHost.ScopedComputeOperation;
                if(scopedCompute) P14CatalogPointerPersistenceHost.VerifyComputeState(dataset,scenario,evidence,"BeforeBinding");
                var binding = GravityWriteAndSolverHost.BindNumericalEngine(
                    dataset, scenario, Engine, evidence, AppDomain.CurrentDomain.GetAssemblies());
                if(scopedCompute) P14CatalogPointerPersistenceHost.VerifyComputeState(dataset,scenario,evidence,"AfterBinding");
                if(remediationCompute) P16AnalysisRemediationHost.VerifyCompute(dataset,scenario,evidence,"AfterBinding");
                if (binding.EngineName != Engine)
                { obs.Status = "SOLVER_ENGINE_MISMATCH"; return; }
                if (request.Operation == P14FarzaneganTestOnlyHost.Operation || request.Operation == FarzaneganHydraulicRemediationHost.Operation)
                    P14FarzaneganTestOnlyHost.Apply(dataset, project, request, evidence, scenario);
                if (request.Operation == FarzaneganHydraulicRemediationHost.Operation)
                    FarzaneganHydraulicRemediationHost.Apply(dataset, scenario, request, evidence);
                obs.CalculationEvent = Guid.NewGuid().ToString("N");
                obs.SolverStartUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                object collection = binding.CollectionConstructor.Invoke(new object[0]);
                binding.Add.Invoke(collection, new[] { scenario });
                if ((int)binding.Count.GetValue(collection, null) != 1)
                    throw new InvalidOperationException("Solver collection must contain exactly scenario 1.");
                SolverLicenseContext.AttachCurrent(binding.Engine);
                evidence.Diagnostics["SolverRunInvoked"] = "true";
                evidence.Diagnostics["SolverTargetScenarioID"] = "1";
                evidence.Diagnostics["SolverEngineTypeName"] = binding.EngineName;
                obs.SolverRunInvocations = 1;
                Exception engineError = null;
                try { RuntimeStageJournal.Call("FarzaneganSolverRun", () =>
                    binding.Run.Invoke(binding.Engine, new[] { collection })); }
                catch (Exception error)
                {
                    Exception current = error;
                    for(int i = 0; i < 8 && current is TargetInvocationException && current.InnerException != null; i++)
                        current = current.InnerException;
                    engineError = current;
                }
                obs.SolverEndUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                if (engineError != null)
                {
                    obs.EngineExceptionType = engineError.GetType().FullName;
                    var notificationEvidence = new P16AzadeganCalculationObservation();
                    obs.NotificationClassification = P16AzadeganCalculationHost.CaptureEngineNotifications(
                        engineError, project, binding.Engine, evidence, notificationEvidence, calls);
                    obs.Notifications = notificationEvidence.Notifications ?? new P16AzadeganEngineNotification[0];
                    obs.NotificationScopeComplete = notificationEvidence.NotificationsComplete;
                }
                else
                {
                    obs.NotificationClassification = "NO_EXCEPTION";
                    obs.NotificationScopeComplete = true;
                }
                MethodInfo statusGetter = project.GetType().GetMethod("GetScenarioComputeStatus", new[] { typeof(int) });
                if (statusGetter == null) throw new MissingMethodException("GetScenarioComputeStatus(int) is unavailable.");
                obs.ScenarioComputeStatus = Convert.ToString(statusGetter.Invoke(project, new object[] { ScenarioId }),
                    CultureInfo.InvariantCulture);
                if (obs.ScenarioComputeStatus != "Success")
                { obs.Status = "SOLVER_STATUS_NOT_SUCCESS"; return; }
                if (obs.NotificationClassification != "NO_EXCEPTION" &&
                    (obs.NotificationClassification != "WARNING_BEARING" || !obs.NotificationScopeComplete ||
                     obs.Notifications.Length == 0 ||
                     obs.Notifications.Any(n => n.Level == "Error" ||
                       (n.HasSubUserNotifications == true && n.ChildrenResolved != true &&
                        n.LeafClassification != P16AzadeganCalculationHost.KnownLeafClassification))))
                { obs.Status = "ENGINE_NOTIFICATIONS_NOT_ACCEPTED"; return; }

                if(scopedCompute) P14CatalogPointerPersistenceHost.VerifyComputeState(dataset,scenario,evidence,"AfterSolver");
                if(remediationCompute) P16AnalysisRemediationHost.VerifyCompute(dataset,scenario,evidence,"AfterSolver");
                // One headless save of only the disposable model. Unknown questions are cancelled;
                // a failed save never receives a success classification.
                Type projectContract = Contract("Haestad.Framework", "Haestad.Framework.Application.IProject");
                Type propsType = Contract("Haestad.Framework", "Haestad.Framework.Application.ProjectProperties");
                Type questionType = Contract("Haestad.Support", "Haestad.Support.User.IMessageQuestionHandler");
                Type enumType = Contract("Haestad.Support", "Haestad.Support.User.MessageHandlerResult");
                if (!projectContract.IsInstanceOfType(project) ||
                    !P16SaveQuestionPolicy.SamePath((string)projectContract.GetProperty("FullPath").GetValue(project, null),request.WorkingModelPath))
                    throw new InvalidOperationException("Save refused: current vendor IProject is not the controlled working model.");
                var questions = new P16SaveQuestionRecorder(request.WorkingModelPath);
                object props = Activator.CreateInstance(propsType);
                object handler = P16SaveQuestionHandler.Create(questionType, enumType, questions);
                AdapterWriteBinding.InvokeSetValue(propsType.GetProperty("MessageQuestionHandler"), props, handler);
                obs.SaveInvocations = 1;
                evidence.Diagnostics["SaveCurrentProjectInvoked"] = "true";
                RuntimeStageJournal.Call("FarzaneganProjectPersistence", () =>
                    AdapterWriteBinding.ExecuteSave(project, propsType, props));
                obs.SaveUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                bool dirty = (bool)projectContract.GetProperty("IsDirty").GetValue(project, null);
                var after = P16AzadeganCalculationHost.Inventory(directory);
                obs.OutputInventory = P16AzadeganCalculationHost.InventoryJson(after);
                obs.CreatedFiles = after.Keys.Where(k => !before.ContainsKey(k)).ToArray();
                obs.ModifiedFiles = after.Keys.Where(k => before.ContainsKey(k) && before[k] != after[k]).ToArray();
                obs.SaveResultClassification = P16ProjectPersistenceResult.Classify(
                    false, questions.AnyUnauthorizedQuestion || questions.BudgetExceeded ||
                    questions.WarningBudgetExceeded, obs.CreatedFiles.Length + obs.ModifiedFiles.Length, dirty);
                obs.SaveSucceeded = P16ProjectPersistenceResult.IsPersisted(obs.SaveResultClassification);
                if (!obs.SaveSucceeded) { obs.Status = "PERSISTENCE_NOT_VERIFIED"; return; }
                if(scopedCompute) P14CatalogPointerPersistenceHost.VerifyComputeState(dataset,scenario,evidence,"AfterSave");
                if(remediationCompute) P16AnalysisRemediationHost.VerifyCompute(dataset,scenario,evidence,"AfterSave");
                evidence.Diagnostics["SaveCurrentProjectSucceeded"] = "true";
                evidence.Diagnostics["SolverStatusSuccess"] = "true";
                obs.Status = obs.NotificationClassification == "NO_EXCEPTION"
                    ? "CALCULATED_SAVED_AWAITING_REOPEN" : "CALCULATED_SAVED_WITH_WARNINGS_AWAITING_REOPEN";
                evidence.Diagnostics[PassFlag] = "true";
            }
            catch (Exception error)
            {
                var exact = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                obs.Status = "FARZANEGAN_NATIVE_PILOT_BLOCKED";
                obs.FailureOperation = calls.Last;
                obs.ErrorType = exact.GetType().FullName;
                obs.ErrorMessage = (exact.Message ?? "").Substring(0, Math.Min(800, (exact.Message ?? "").Length));
            }
            finally
            {
                evidence.Diagnostics[EvidenceKey] = RuntimeHostProtocol.Serializer().Serialize(obs);
                evidence.Diagnostics["FarzaneganPilotStatus"] = obs.Status;
                if (obs.SaveSucceeded != true && evidence.FailureCode == null)
                    evidence.FailureCode = "FARZANEGAN_CALCULATE_NOT_ACCEPTED";
            }
        }
    }
}
