using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static P16AzadeganCalculationObservation AzadeganCalculationPassObservation()
        {
            return new P16AzadeganCalculationObservation
            {
                Status = P16AzadeganCalculationHost.PassStatus,
                WorkingCopyId = "0123456789abcdef0123456789abcdef",
                OpenedModelPath = @"C:\temp\Working\0123456789abcdef0123456789abcdef\Azadegab ssw 26.10.25.stsw",
                RequestedScenarioId = 353,
                ActiveScenarioId = 353,
                PhysicalAlternativeId = 352,
                RequestedEngine = "DelawareProductEngine",
                MainEngineFromModel = "DelawareProductEngine",
                ModelEngineTypeNames = new[] { "DelawareProductEngine" },
                ResolvedEngine = "DelawareProductEngine",
                EngineRuntimeType = "Haestad.SewerGEMS.DelawareProductEngine",
                EngineSelectionBasis = "MODEL_MAIN_ENGINE_MATCHES_DECLARED_ENGINE",
                CalculationOptionsId = 29,
                CalculationEventId = "a1b2c3d4e5f60718293a4b5c6d7e8f90",
                SolverStartTimestamp = "2026-10-25T09:00:00.0000000Z",
                SolverEndTimestamp = "2026-10-25T09:00:12.0000000Z",
                SolverRunInvocations = 1,
                ScenarioComputeStatus = "Success",
                ScenarioStatusText = "Computing finished",
                SaveInvocations = 1,
                SaveSucceeded = true,
                SaveTimestamp = "2026-10-25T09:00:14.0000000Z",
                ReadTimestamp = "2026-10-25T09:00:40.0000000Z",
                InputInventoryJson = "[]",
                InputInventoryDigest = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789",
                OutputInventoryJson = "[]",
                CreatedFiles = new[] { "Azadegab ssw 26.10.25.stsw_1_353.fld" },
                ModifiedFiles = new[] { "Azadegab ssw 26.10.25.stsw.sqlite" },
                ResultStoreFiles = new[] { "Azadegab ssw 26.10.25.stsw.sqlite", "Azadegab ssw 26.10.25.stsw_1_353.fld" },
                ReopenOccurred = true,
                ResultManagerAvailable = true,
                ResultManagerEngineName = "DelawareProductEngine",
                ResultManagerScenarioId = 353,
                ResultManagerWasActive = false,
                HasResults = true,
                SupportedFieldCount = 2,
                SupportedFieldNames = new[] { "BasicFlowResults_Flow", "BasicFlowResults_Velocity" },
                FlowFieldSupported = true,
                FieldName = "BasicFlowResults_Flow",
                RecordName = "BasicFlowResults",
                FieldRuntimeType = "Haestad.Domain.ResultField",
                FieldHasResults = true,
                RequestedElementLabel = "SSW-001",
                RequestedElementId = 119,
                ObservedElementLabel = "SSW-001",
                ObservedElementId = 119,
                LabelMatchCount = 1,
                ElementIdentityVerified = true,
                TimeStepIndex = 0,
                TimeSeconds = 0d,
                TimeStepCount = 3,
                AvailableTimeSeconds = new[] { 0d, 60d, 120d },
                WorkingUnitSymbol = "L/s",
                WorkingUnitIdentity = "Haestad.Support.Units.UnitConversionManager+UnitIndex:LitersPerSecond:47",
                StorageUnitIdentity = "Haestad.Support.Units.UnitConversionManager+UnitIndex:LitersPerSecond:47",
                UnitEvidence = "Native IUnitizedField working-unit enum literal only.",
                ValueClrType = "System.Double",
                RawValue = "0.757082",
                RawValueClassification = "FINITE_DOUBLE",
                FlowReadInvocations = 1,
                FreshnessVerified = true,
                FreshnessEvidence = "Solve event a1b2c3d4 completed, was persisted by one controlled save, and was read in a separate reopened session.",
                ProvenanceChain = "MODEL:x|COPY:y|SCENARIO:353|ALT:352|OPTS:29|ENGINE:DelawareProductEngine|EVENT:a1b2c3d4|SOLVE:SUCCESS|SAVE:SUCCESS|REOPEN:YES",
                ResultCleanupCompleted = true,
                CleanupError = null,
                FailureOperation = null,
                ErrorType = null,
                ErrorHResult = null,
                ErrorMessage = null,
                EngineOutcome = "COMPLETED_CLEAN",
                NotificationCount = 0,
                NotificationLevelSummary = "",
                NotificationsComplete = true,
                NotificationCaptureLimitation = null,
                Notifications = new P16AzadeganEngineNotification[0]
            };
        }

        private static P16AzadeganEngineNotification AzadeganNotification(string level, int messageId, string messageKey, string message)
        {
            return new P16AzadeganEngineNotification
            {
                MessageId = messageId,
                ElementId = 119,
                ElementType = 12,
                Level = level,
                MessageKey = messageKey,
                Parameters = new[] { "SSW-001", "0.757" },
                ScenarioId = 353,
                SourceKey = "BasicFlowResults_Flow",
                TimeStepIndex = null,
                Message = message,
                HelpId = "HH1001",
                HasSubUserNotifications = false,
                Depth = 0,
                ParentIndex = null
            };
        }

        // A warning-bearing pass: extraction succeeded, notifications are attached, acceptance is not claimed.
        private static P16AzadeganCalculationObservation AzadeganCalculationWarningObservation()
        {
            var observation = AzadeganCalculationPassObservation();
            observation.Status = P16AzadeganCalculationHost.PassWithWarningsStatus;
            observation.EngineOutcome = "COMPLETED_WITH_NOTIFICATIONS";
            observation.Notifications = new[]
            {
                AzadeganNotification("Warning", 2101, "MSG_CONDUIT_SURCHARGED", "Conduit SSW-001 is surcharged."),
                AzadeganNotification("Information", 2102, "MSG_TIME_STEP_REDUCED", "Time step was reduced for stability.")
            };
            observation.NotificationCount = observation.Notifications.Length;
            observation.NotificationLevelSummary = "Warning=1,Information=1";
            observation.NotificationsComplete = true;
            observation.NotificationCaptureLimitation = "IUserNotification exposes no TimeStepIndex member in this build.";
            return observation;
        }

        private static string AzadeganCalculationJson(P16AzadeganCalculationObservation observation) =>
            RuntimeHostProtocol.Serializer().Serialize(observation);

        // The single classification rule the capture path executes: only provably benign levels pass.
        private static void P16AzadeganCalc_NotificationLevelsClassified()
        {
            Check(P16AzadeganCalculationHost.ClassifyNotificationLevels(new[] { "Warning" }, true, 1) == "WARNING_BEARING",
                "A lone warning is a warning-bearing outcome.");
            Check(P16AzadeganCalculationHost.ClassifyNotificationLevels(new[] { "Status", "Information", "Warning" }, true, 3) == "WARNING_BEARING",
                "Status/Information/Warning are warning-bearing, never blocking.");
            Check(P16AzadeganCalculationHost.ClassifyNotificationLevels(new[] { "Warning", "Error" }, true, 2) == "ERROR_LEVEL_BLOCKING",
                "Any error-level notification is blocking.");
            Check(P16AzadeganCalculationHost.ClassifyNotificationLevels(new[] { "Warning" }, true, 2) == "UNCLASSIFIABLE",
                "A count mismatch is unclassifiable.");
            Check(P16AzadeganCalculationHost.ClassifyNotificationLevels(new[] { "Warning" }, false, 1) == "UNCLASSIFIABLE",
                "An incomplete capture is unclassifiable.");
            Check(P16AzadeganCalculationHost.ClassifyNotificationLevels(new[] { "Critical" }, true, 1) == "UNCLASSIFIABLE",
                "An unknown level is unclassifiable.");
            Check(P16AzadeganCalculationHost.ClassifyNotificationLevels(new[] { "3" }, true, 1) == "UNCLASSIFIABLE",
                "A numeric level is unclassifiable, never guessed.");
            Check(P16AzadeganCalculationHost.ClassifyNotificationLevels(new string[0], true, 0) == "EMPTY",
                "An empty notification set is distinguished from a clean completion.");
            Check(P16AzadeganCalculationHost.ClassifyNotificationLevels(null, true, 0) == "UNCLASSIFIABLE",
                "A null notification set is unclassifiable.");
        }

        // A warning-bearing result is accepted, and its notifications survive serialization intact.
        private static void P16AzadeganCalc_WarningBearingObservationAccepted()
        {
            var observation = AzadeganCalculationWarningObservation();
            P16AzadeganCalculationHost.CaptureNotificationRuntimeIdentity(observation.Notifications[0], new object());
            string json = AzadeganCalculationJson(observation);
            P16AzadeganCalculationHost.ValidateObservation(json);
            var restored = RuntimeHostProtocol.Serializer().Deserialize<P16AzadeganCalculationObservation>(json);
            Check(restored.Status == P16AzadeganCalculationHost.PassWithWarningsStatus, "Warning-bearing pass status preserved.");
            Check(restored.Notifications[0].RuntimeTypeFullName == typeof(object).FullName &&
                restored.Notifications[0].RuntimeAssemblyFullName == typeof(object).Assembly.FullName,
                "Notification runtime type and assembly identity round-trip exactly.");
            Check(restored.EngineOutcome == "COMPLETED_WITH_NOTIFICATIONS", "Engine outcome preserved.");
            Check(restored.NotificationCount == 2 && restored.Notifications.Length == 2, "Both notifications preserved.");
            var first = restored.Notifications[0];
            Check(first.Level == "Warning" && first.MessageId == 2101 && first.ElementId == 119 && first.ElementType == 12 &&
                first.ScenarioId == 353 && first.MessageKey == "MSG_CONDUIT_SURCHARGED" && first.SourceKey == "BasicFlowResults_Flow" &&
                first.Message == "Conduit SSW-001 is surcharged." && first.HelpId == "HH1001" &&
                first.Parameters.Length == 2 && first.Parameters[0] == "SSW-001" &&
                first.TimeStepIndex == null && first.HasSubUserNotifications == false,
                "Every structured notification field round-trips exactly.");
            Check(restored.NotificationLevelSummary == "Warning=1,Information=1", "Level summary preserved.");
            Check(restored.NotificationCaptureLimitation != null, "Capture limitation preserved.");
        }

        // A blocking engine error can never be relabelled as a pass, and a pass can never hide one.
        private static void P16AzadeganCalc_NotificationIntegrityRejectsFalsePass()
        {
            var withError = AzadeganCalculationWarningObservation();
            withError.Notifications = new[] { AzadeganNotification("Error", 3001, "MSG_ENGINE_FAILED", "Engine failed.") };
            withError.NotificationCount = 1;
            withError.NotificationLevelSummary = "Error=1";
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(withError)));

            var hidden = AzadeganCalculationWarningObservation();
            hidden.Notifications = new[]
            {
                AzadeganNotification("Warning", 2101, "MSG_CONDUIT_SURCHARGED", "Surcharged."),
                AzadeganNotification("Error", 3001, "MSG_ENGINE_FAILED", "Engine failed.")
            };
            hidden.NotificationCount = 2;
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(hidden)));

            // Warning-bearing status with no notifications at all is not a pass.
            RejectAzadeganCalculation(o => { o.Status = P16AzadeganCalculationHost.PassWithWarningsStatus; });
            // A clean pass may not claim to have carried notifications.
            RejectAzadeganCalculation(o => o.EngineOutcome = "COMPLETED_WITH_NOTIFICATIONS");
            RejectAzadeganCalculation(o => o.NotificationsComplete = false);
            RejectAzadeganCalculation(o => o.NotificationCount = 1);
            RejectAzadeganCalculation(o => o.Notifications = new[] { AzadeganNotification("Warning", 1, "K", "M") });
            // A blocked outcome is preserved with its notifications rather than discarded.
            var blocked = AzadeganCalculationWarningObservation();
            blocked.Status = "ENGINE_ERROR_NOTIFICATIONS_BLOCKING";
            blocked.FreshnessVerified = false;
            blocked.RawValue = null;
            blocked.ValueClrType = null;
            blocked.FlowReadInvocations = 0;
            P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(blocked));
            Check(true, "Blocking engine notifications are preserved as evidence.");
        }

        // The failure stage must name the engine call, and the vendor contracts must be exact.
        private static void P16AzadeganCalc_EngineFailureStageIsTheSolver()
        {
            Check(P16AzadeganCalculationHost.EngineOperation == "Haestad.Domain.INumericalEngine.Run",
                "The tracked failure operation names the numerical engine call.");
            Check(P16AzadeganCalculationHost.EngineWarningsExceptionType == "Haestad.Calculations.Support.EngineWarningsException",
                "The notification-bearing exception is identified by its exact runtime type.");
            string source = File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), "src", "SewerPilot.OpenFlows.Adapter",
                "P16AzadeganCalculationHost.cs"));
            int marker = source.IndexOf("calls.Last = EngineOperation;", StringComparison.Ordinal);
            int invocation = source.IndexOf("\"SolverExecution\"", StringComparison.Ordinal);
            Check(marker > 0 && invocation > 0 && marker < invocation,
                "The failure-stage marker is set immediately before the solver invocation.");
            Check(source.IndexOf("catch (Exception error) { engineFailure = Unwrap(error); }", StringComparison.Ordinal) > 0,
                "TargetInvocationException is unwrapped to reach the vendor exception.");
            Check(source.IndexOf("CaptureNotificationRuntimeIdentity(record, item);", StringComparison.Ordinal) > 0 &&
                source.IndexOf("runtimeType.Assembly.FullName", StringComparison.Ordinal) > 0,
                "Live notification capture records the concrete runtime type and full assembly identity using public CLR metadata.");
            // No retry of the solver anywhere in the operation.
            Check(!source.Contains("for (int attempt") && !source.Contains("Retry(") && !source.Contains("retryCount"),
                "The solver is never retried.");
        }

        private static void P16AzadeganCalc_ExactPassObservationAccepted()
        {
            P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(AzadeganCalculationPassObservation()));
            Check(true, "Exact native calculation pass observation accepted.");

            // The installed surface may expose neither the main-engine accessor nor engine-type
            // enumeration. The declared engine remains explicit, never silently substituted.
            var noAccessor = AzadeganCalculationPassObservation();
            noAccessor.MainEngineFromModel = null;
            noAccessor.ModelEngineTypeNames = new string[0];
            noAccessor.EngineSelectionBasis = "MODEL_SCENARIO_CALCULATION_OPTIONS_FOR_DECLARED_ENGINE; main-engine accessor unavailable";
            P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(noAccessor));
            Check(true, "Declared-engine basis accepted when the main-engine accessor is unavailable.");
        }

        private static void RejectAzadeganCalculation(Action<P16AzadeganCalculationObservation> mutate)
        {
            var observation = AzadeganCalculationPassObservation();
            mutate(observation);
            string json = AzadeganCalculationJson(observation);
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(json));
        }

        // No substituted engine, scenario, options, element, field, timestep, unit, value or
        // provenance claim may be relabelled as a native calculation pass.
        private static void P16AzadeganCalc_ObservationRejectsSubstitutions()
        {
            RejectAzadeganCalculation(o => o.RequestedEngine = "CivilStormDynamicEngine");
            RejectAzadeganCalculation(o => o.ResolvedEngine = "CivilStormDynamicEngine");
            RejectAzadeganCalculation(o => o.ResultManagerEngineName = "CivilStormDynamicEngine");
            RejectAzadeganCalculation(o => o.MainEngineFromModel = "CivilStormDynamicEngine");
            RejectAzadeganCalculation(o => o.EngineSelectionBasis = "MODEL_SCENARIO_CALCULATION_OPTIONS_FOR_DECLARED_ENGINE");
            RejectAzadeganCalculation(o => o.EngineSelectionBasis = "");
            RejectAzadeganCalculation(o => o.CalculationOptionsId = 24);
            RejectAzadeganCalculation(o => o.RequestedScenarioId = 359);
            RejectAzadeganCalculation(o => o.ActiveScenarioId = 359);
            RejectAzadeganCalculation(o => o.ResultManagerScenarioId = 359);
            RejectAzadeganCalculation(o => o.PhysicalAlternativeId = 351);
            RejectAzadeganCalculation(o => o.RequestedElementId = 120);
            RejectAzadeganCalculation(o => o.ObservedElementId = 120);
            RejectAzadeganCalculation(o => o.ObservedElementLabel = "SSW-002");
            RejectAzadeganCalculation(o => o.LabelMatchCount = 2);
            RejectAzadeganCalculation(o => o.ElementIdentityVerified = false);
            RejectAzadeganCalculation(o => o.FieldName = "BasicFlowResults_Velocity");
            RejectAzadeganCalculation(o => o.RecordName = "BasicResults");
            RejectAzadeganCalculation(o => o.TimeStepIndex = 1);
            RejectAzadeganCalculation(o => o.TimeSeconds = 60d);
            RejectAzadeganCalculation(o => o.WorkingUnitSymbol = "ft3/s");
            RejectAzadeganCalculation(o => o.WorkingUnitIdentity = "Haestad.Support.Units.UnitConversionManager+UnitIndex:UnknownFlowUnit:999");
            RejectAzadeganCalculation(o => o.ValueClrType = "System.String");
            RejectAzadeganCalculation(o => o.RawValue = "NaN");
            RejectAzadeganCalculation(o => o.RawValueClassification = "NON_NUMERIC_RETURN_NO_CONVERSION");
            RejectAzadeganCalculation(o => o.FlowReadInvocations = 0);
            RejectAzadeganCalculation(o => o.FlowReadInvocations = 2);
            RejectAzadeganCalculation(o => o.SolverRunInvocations = 0);
            RejectAzadeganCalculation(o => o.SolverRunInvocations = 2);
            RejectAzadeganCalculation(o => o.SaveInvocations = 0);
            RejectAzadeganCalculation(o => o.SaveSucceeded = false);
            RejectAzadeganCalculation(o => o.ScenarioComputeStatus = "Failed");
            RejectAzadeganCalculation(o => o.ReopenOccurred = false);
            RejectAzadeganCalculation(o => o.FreshnessVerified = false);
            RejectAzadeganCalculation(o => o.ProvenanceChain = "");
            RejectAzadeganCalculation(o => o.CalculationEventId = "");
            // Either class of change is legitimate persistence evidence; the absence of both is not.
            RejectAzadeganCalculation(o => { o.CreatedFiles = new string[0]; o.ModifiedFiles = new string[0]; });
            RejectAzadeganCalculation(o => o.ResultStoreFiles = new string[0]);
            RejectAzadeganCalculation(o => o.ResultManagerAvailable = false);
            RejectAzadeganCalculation(o => o.HasResults = false);
            RejectAzadeganCalculation(o => o.FieldHasResults = false);
            RejectAzadeganCalculation(o => o.FlowFieldSupported = false);
            RejectAzadeganCalculation(o => o.ResultCleanupCompleted = false);
            RejectAzadeganCalculation(o => o.CleanupError = "close failed");
            RejectAzadeganCalculation(o => o.ErrorType = "System.Exception");
            RejectAzadeganCalculation(o => o.FailureOperation = "Haestad.Domain.IResultManager.Open");
            RejectAzadeganCalculation(o => o.Status = "RESULTS_MISSING");
            // Lineage must be chronological: a read that predates its own solve cannot be fresh.
            RejectAzadeganCalculation(o => o.SolverStartTimestamp = "2026-10-25T09:00:50.0000000Z");
            RejectAzadeganCalculation(o => o.SaveTimestamp = "2026-10-25T09:00:01.0000000Z");
        }

        // A missing-result observation may never carry a verified-freshness claim.
        private static void P16AzadeganCalc_NonPassCannotClaimFreshness()
        {
            var observation = AzadeganCalculationPassObservation();
            observation.Status = "RESULTS_MISSING";
            observation.HasResults = false;
            observation.RawValue = null;
            observation.ValueClrType = null;
            observation.FreshnessVerified = true;
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(observation)));

            var blocked = AzadeganCalculationPassObservation();
            blocked.Status = "SOLVER_STATUS_FAILED";
            blocked.ScenarioComputeStatus = "Failed";
            blocked.FreshnessVerified = false;
            blocked.RawValue = null;
            blocked.ValueClrType = null;
            blocked.FlowReadInvocations = 0;
            P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(blocked));
            Check(true, "Blocked calculation observation without a freshness claim is accepted as evidence.");
        }

        private static HostRequest AzadeganCalculationRequest(string copyId, string phase, bool withLineage)
        {
            var diagnostics = P16AzadeganCalculationHost.Context(copyId, new string('A', 64));
            diagnostics[P16AzadeganCalculationHost.PhaseKey] = phase;
            if (withLineage)
            {
                diagnostics["P16AzadeganCalculationEventId"] = new string('b', 32);
                diagnostics["P16AzadeganSolverStart"] = "2026-10-25T09:00:00.0000000Z";
                diagnostics["P16AzadeganSolverEnd"] = "2026-10-25T09:00:12.0000000Z";
                diagnostics["P16AzadeganSaveTimestamp"] = "2026-10-25T09:00:14.0000000Z";
                diagnostics["P16AzadeganInputInventory"] = "[]";
                diagnostics["P16AzadeganOutputInventory"] = "[]";
                diagnostics["P16AzadeganCreatedFiles"] = "[]";
                diagnostics["P16AzadeganModifiedFiles"] = "[]";
                diagnostics["P16AzadeganOptionsId"] = "29";
                diagnostics["P16AzadeganResolvedEngine"] = "DelawareProductEngine";
                diagnostics["P16AzadeganMainEngine"] = "DelawareProductEngine";
                diagnostics["P16AzadeganSelectionBasis"] = "MODEL_MAIN_ENGINE_MATCHES_DECLARED_ENGINE";
                // Notification lineage. These are fixture values chosen to satisfy the request
                // contract for a clean run; they are NOT observed runtime evidence. The production
                // PrepareRead writes the same four keys from the first session's observation, and
                // notification content may legitimately be empty, so presence is required while the
                // observation itself validates content.
                diagnostics["P16AzadeganEngineOutcome"] = "COMPLETED_CLEAN";
                diagnostics["P16AzadeganNotifications"] = "[]";
                diagnostics["P16AzadeganNotificationLevels"] = "";
                diagnostics["P16AzadeganNotificationLimitation"] = "";
            }
            return new HostRequest
            {
                SchemaVersion = 1,
                RunId = new string('c', 32),
                Operation = P16AzadeganCalculationHost.Operation,
                WorkingCopyId = copyId,
                WorkingModelPath = P16AzadeganCalculationHost.ExpectedModelPath(copyId),
                RuntimeCandidateCount = 1,
                DetectedArchitecture = "x64",
                SelectedRuntime = @"C:\Program Files\Bentley\OpenFlows Sewer",
                CoreFiles = new HostMetadata[0],
                Diagnostics = diagnostics
            };
        }

        private static void P16AzadeganCalc_RequestValidationBindsCopyPhaseAndLineage()
        {
            string copyId = "0123456789abcdef0123456789abcdef";
            P16AzadeganCalculationHost.ValidateRequest(AzadeganCalculationRequest(copyId, P16AzadeganCalculationHost.PhaseCalculate, false));
            P16AzadeganCalculationHost.ValidateRequest(AzadeganCalculationRequest(copyId, P16AzadeganCalculationHost.PhaseRead, true));
            Check(true, "Exact CALCULATE and READ requests accepted.");

            // The calculation may never be pointed at the source copy or at another run's copy.
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateRequest(new HostRequest
            {
                SchemaVersion = 1, RunId = new string('c', 32), Operation = P16AzadeganCalculationHost.Operation,
                WorkingCopyId = copyId, WorkingModelPath = @"C:\Users\M.Zeynali\Documents\SewerPilot\engineering\customer-data\Azadegan\Sanitary_Rev_B00_SourceCopy_20261024\New Model\Oct 26\Azadegab ssw 26.10.25.stsw",
                Diagnostics = AzadeganCalculationRequest(copyId, P16AzadeganCalculationHost.PhaseCalculate, false).Diagnostics
            }));
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateRequest(AzadeganCalculationRequest(copyId, "WRITE", false)));
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateRequest(AzadeganCalculationRequest("NOT-A-CONTROLLED-COPY-ID", P16AzadeganCalculationHost.PhaseCalculate, false)));

            // An engineering plan or any external write authority is rejected outright.
            var planned = AzadeganCalculationRequest(copyId, P16AzadeganCalculationHost.PhaseCalculate, false);
            planned.WriteAuthorizationTokenId = new string('d', 32);
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateRequest(planned));

            // Lineage keys are mandatory for the readback phase and forbidden for the calculate phase.
            var readWithoutLineage = AzadeganCalculationRequest(copyId, P16AzadeganCalculationHost.PhaseRead, false);
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateRequest(readWithoutLineage));
            var calculateWithLineage = AzadeganCalculationRequest(copyId, P16AzadeganCalculationHost.PhaseCalculate, true);
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateRequest(calculateWithLineage));

            // A single substituted context value invalidates the whole request.
            var substituted = AzadeganCalculationRequest(copyId, P16AzadeganCalculationHost.PhaseCalculate, false);
            substituted.Diagnostics["P16AzadeganEngine"] = "CivilStormDynamicEngine";
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateRequest(substituted));
            var substitutedOptions = AzadeganCalculationRequest(copyId, P16AzadeganCalculationHost.PhaseCalculate, false);
            substitutedOptions.Diagnostics["P16AzadeganCalculationOptionsId"] = "24";
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateRequest(substitutedOptions));
            var substitutedElement = AzadeganCalculationRequest(copyId, P16AzadeganCalculationHost.PhaseCalculate, false);
            substitutedElement.Diagnostics["P16AzadeganElementId"] = "120";
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateRequest(substitutedElement));
        }

        // The disposable inventory is content-bound and limited to the declared model family.
        private static void P16AzadeganCalc_InventoryIsContentBound()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "CalcInventoryTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "Azadegab ssw 26.10.25.stsw"), "model");
                File.WriteAllText(Path.Combine(directory, "Azadegab ssw 26.10.25.stsw.sqlite"), "state");
                File.WriteAllText(Path.Combine(directory, "unrelated.bin"), "ignored");
                var first = P16AzadeganCalculationHost.Inventory(directory);
                string digest = P16AzadeganCalculationHost.InventoryDigest(first);
                Check(first.Count == 2, "Inventory excludes files outside the declared model family.");
                Check(digest.Length == 64 && digest.All(c => "0123456789ABCDEF".IndexOf(c) >= 0), "Inventory digest is canonical SHA-256 hex.");
                Check(digest == P16AzadeganCalculationHost.InventoryDigest(P16AzadeganCalculationHost.Inventory(directory)), "Inventory digest is deterministic.");
                File.WriteAllText(Path.Combine(directory, "Azadegab ssw 26.10.25.stsw.sqlite"), "state-changed");
                Check(digest != P16AzadeganCalculationHost.InventoryDigest(P16AzadeganCalculationHost.Inventory(directory)), "Inventory digest is content-bound.");
            }
            finally { try { Directory.Delete(directory, true); } catch (IOException) { } }
        }

        // The new surface must expose no scenario/alternative creation, no property write and no
        // silent engine substitution.
        private static void P16AzadeganCalc_NoScenarioPropertyOrFallbackSurface()
        {
            string root = Path.Combine(ResolveRepositoryRoot(), "src", "SewerPilot.OpenFlows.Adapter");
            foreach (string file in new[] { "P16AzadeganCalculationHost.cs", "P16AzadeganCalculationProof.cs" })
            {
                string source = File.ReadAllText(Path.Combine(root, file));
                foreach (string forbidden in new[] { "\"SaveCurrentProject\"", ".SaveCurrentProject(", ".SetValue(", "CivilStormDynamicEngine",
                    "\"Copy\"", ".Copy(", "CreateScenario", "CreateAlternative", "\"SaveAs\"", ".SaveAs(" })
                    Check(!source.Contains(forbidden), file + " must not reference " + forbidden + ".");
            }
        }

        // ---------------------------------------------------------------- durable pre-save capture

        private static string AzadeganJournalLine(string stage, string state, string utc)
        {
            return "{\"stage_number\":1,\"stage_name\":\"" + stage + "\",\"utc\":\"" + utc + "\",\"state\":\"" + state + "\"}";
        }

        // The capture must survive a kill: it is written before the save and states that nothing was
        // persisted and nothing was read back.
        private static void P16AzadeganCalc_PreSaveEvidenceIsDurableAndNotAPass()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                P16AzadeganCalculationHost.PersistPreSaveEvidence(directory, AzadeganCalculationWarningObservation());

                string path = Path.Combine(directory, P16AzadeganCalculationHost.PreSaveEvidenceFileName);
                Check(File.Exists(path), "Pre-save evidence must exist in the controlled run directory.");
                Check(!File.Exists(path + ".tmp"), "Pre-save evidence must commit atomically with no temp residue.");

                string recovered = P16AzadeganCalculationProof.ReadPreSaveEvidence(directory);
                Check(!string.IsNullOrEmpty(recovered), "The parent must recover the pre-save evidence by path.");

                var evidence = RuntimeHostProtocol.Serializer().Deserialize<P16AzadeganPreSaveEvidence>(recovered);
                Check(evidence.EvidenceStage == P16AzadeganCalculationHost.PreSaveEvidenceStage,
                    "Pre-save evidence must carry its own non-persistence stage marker.");
                Check(!evidence.PersistedToModel, "Pre-save evidence must not claim the model was persisted.");
                Check(!evidence.ResultReadBack, "Pre-save evidence must not claim a readback.");
                Check(evidence.Observation != null, "Pre-save evidence must carry the actual observation.");
                Check(evidence.Observation.Notifications.Length == 2, "Both captured notifications must survive.");
                Check(evidence.Observation.Notifications[0].MessageKey == "MSG_CONDUIT_SURCHARGED",
                    "The first notification key must survive verbatim.");
                Check(evidence.Observation.Notifications[1].Message == "Time step was reduced for stability.",
                    "The formatted message must survive verbatim.");
                Check(evidence.Observation.NotificationLevelSummary == "Warning=1,Information=1",
                    "The level summary must survive verbatim.");
                Check(evidence.Observation.NotificationCaptureLimitation == "IUserNotification exposes no TimeStepIndex member in this build.",
                    "The capture limitation must survive verbatim.");
                Check(evidence.Observation.EngineOutcome == "COMPLETED_WITH_NOTIFICATIONS",
                    "The engine outcome must survive verbatim.");
                Check(evidence.Observation.ScenarioComputeStatus == "Success",
                    "The scenario completion boundary must survive verbatim.");
                Check(evidence.Observation.SaveTimestamp == "2026-10-25T09:00:14.0000000Z",
                    "Available timestamps must survive verbatim.");
                Check(evidence.Observation.Status == P16AzadeganCalculationHost.PreSaveObservationStatus,
                    "The pre-save snapshot must carry the explicit pre-save status, not the live object's status.");
            }
            finally { try { Directory.Delete(directory, true); } catch (IOException) { } }
        }

        // An unwritable capture must stop the run before the save, and its status must never read as a pass.
        private static void P16AzadeganCalc_PreSaveEvidenceFailsClosed()
        {
            string absent = Path.Combine(Path.GetTempPath(), "SewerPilot", "Tests", Guid.NewGuid().ToString("N"), "absent");
            Throws<InvalidOperationException>(() =>
                P16AzadeganCalculationHost.PersistPreSaveEvidence(absent, AzadeganCalculationWarningObservation()));
            Check(P16AzadeganCalculationProof.ReadPreSaveEvidence(absent) == null,
                "An absent run directory must yield no recovered evidence rather than a fabricated document.");
            Check(P16AzadeganCalculationProof.ReadPreSaveEvidence(null) == null,
                "A missing run directory must yield no evidence at all.");

            var blocked = AzadeganCalculationWarningObservation();
            blocked.Status = P16AzadeganCalculationHost.PreSaveFailureStatus;
            blocked.FreshnessVerified = false;
            P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(blocked));
            Check(blocked.Status != P16AzadeganCalculationHost.PassStatus &&
                  blocked.Status != P16AzadeganCalculationHost.PassWithWarningsStatus,
                "An unwritable pre-save capture must remain a blocked observation.");
        }

        // ---------------------------------------------------------------- save watchdog

        private static void P16AzadeganCalc_SaveWatchdogTimesOut()
        {
            string enter = AzadeganJournalLine(SaveStageWatchdog.SaveStageName, "ENTER", "2026-10-25T08:41:08.2543220Z");
            var watchdog = new SaveStageWatchdog(null);

            Check(watchdog.ObserveLine(enter, new DateTime(2026, 10, 25, 8, 41, 8, DateTimeKind.Utc)) == SaveStageVerdict.Continue,
                "Save ENTER starts the deadline without firing it.");
            Check(watchdog.SaveEntered, "The watchdog must record that the save was entered.");
            Check(watchdog.SaveEnteredUtc.HasValue && watchdog.SaveEnteredUtc.Value.ToString("o") == "2026-10-25T08:41:08.2543220Z",
                "The deadline must start at the journal's own timestamp, not at polling time.");

            var start = watchdog.SaveEnteredUtc.Value;
            Check(watchdog.Decide(start.AddMilliseconds(29999)) == SaveStageVerdict.Continue,
                "The deadline must not fire one millisecond early.");
            Check(watchdog.Decide(start.AddMilliseconds(30000)) == SaveStageVerdict.TimedOut,
                "The deadline must fire at exactly 30 s.");
            Check(watchdog.Detail == "SAVE_STAGE_TIMEOUT", "The watchdog must name the timeout it observed.");
            Check(watchdog.Decide(start.AddSeconds(600)) == SaveStageVerdict.TimedOut, "A timeout must stay a timeout.");
            Check(!watchdog.SaveCompleted, "A timeout must never be recorded as a completed save.");
        }

        private static void P16AzadeganCalc_SaveWatchdogStopsOnCompletion()
        {
            var enter = AzadeganJournalLine(SaveStageWatchdog.SaveStageName, "ENTER", "2026-10-25T08:41:08.2543220Z");
            var exit = AzadeganJournalLine(SaveStageWatchdog.SaveStageName, "EXIT", "2026-10-25T08:41:20.0000000Z");
            var failure = AzadeganJournalLine(SaveStageWatchdog.SaveStageName, "EXCEPTION", "2026-10-25T08:41:20.0000000Z");
            var at = new DateTime(2026, 10, 25, 8, 41, 8, DateTimeKind.Utc);

            var completed = new SaveStageWatchdog(null);
            completed.ObserveLine(enter, at);
            Check(completed.ObserveLine(exit, at.AddSeconds(1)) == SaveStageVerdict.SaveCompleted,
                "A save EXIT must complete the watchdog.");
            Check(completed.Decide(at.AddHours(1)) == SaveStageVerdict.SaveCompleted,
                "A completed save must never time out afterwards.");

            var failed = new SaveStageWatchdog(null);
            failed.ObserveLine(enter, at);
            Check(failed.ObserveLine(failure, at.AddSeconds(1)) == SaveStageVerdict.SaveCompleted,
                "A save EXCEPTION must also stop the watchdog; the host reports the failure, not the watchdog.");

            var idle = new SaveStageWatchdog(null);
            Check(idle.Decide(at.AddHours(1)) == SaveStageVerdict.Continue,
                "A watchdog that never saw the save stage must never fire.");
            Check(idle.Detail == "SAVE_STAGE_NOT_ENTERED", "An idle watchdog must say the save stage was never entered.");
        }

        // Termination must reach only the process this invocation started.
        private static void P16AzadeganCalc_WatchdogTerminatesOnlyOwnedProcess()
        {
            var info = new System.Diagnostics.ProcessStartInfo("ping.exe", "-n 60 127.0.0.1")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var child = System.Diagnostics.Process.Start(info))
            {
                try
                {
                    Check(!OwnedProcessTerminator.TryTerminate(child, child.Id + 1),
                        "A mismatched process id must never be terminated.");
                    Check(!child.HasExited, "The child must survive a refused termination.");
                    Check(!OwnedProcessTerminator.TryTerminate(null, child.Id), "A null handle must never be terminated.");
                    Check(!OwnedProcessTerminator.TryTerminate(child, 0), "A zero process id must never be terminated.");
                    Check(OwnedProcessTerminator.TryTerminate(child, child.Id),
                        "The owned process must be terminated once the save boundary stops responding.");
                    Check(child.HasExited, "The owned process must actually be gone.");
                }
                finally { try { if (!child.HasExited) child.Kill(); } catch (Exception) { } }
            }
        }

        // No outcome may read as a pass unless the host produced a pass status and its flag together.
        private static void P16AzadeganCalc_SaveTimeoutCannotPass()
        {
            var timedOut = new P16AzadeganCalculationOutcome { Status = "SAVE_STAGE_TIMEOUT", Passed = false };
            Check(!timedOut.IsPass, "A save-stage timeout is never a pass.");
            timedOut.Passed = true;
            Check(!timedOut.IsPass, "The raw pass flag must not promote a save-stage timeout.");
            Check(!new P16AzadeganCalculationOutcome { Status = "ATTEMPT_ABORTED", Passed = true }.IsPass,
                "An aborted attempt must never pass.");
            Check(!new P16AzadeganCalculationOutcome { Status = P16AzadeganCalculationHost.PassWithWarningsStatus, Passed = false }.IsPass,
                "A warning-bearing status still requires the host's pass flag.");
            Check(new P16AzadeganCalculationOutcome { Status = P16AzadeganCalculationHost.PassStatus, Passed = true }.IsPass,
                "A genuine pass must still pass.");
        }

        // ---------------------------------------------------------------- notification tree completeness

        // A tree with no child-bearing notification is fully captured and may be classified normally.
        private static void P16AzadeganCalc_CompleteTreeIsWarningBearing()
        {
            var notifications = new[] { AzadeganNotification("Warning", 2101, "K1", "M1") };
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(notifications, new[] { "Warning" }, true, 1) == "WARNING_BEARING",
                "A childless warning tree is warning-bearing.");
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(
                    new[] { AzadeganNotification("Status", 1, "K", "M"), AzadeganNotification("Information", 2, "K2", "M2") },
                    new[] { "Status", "Information" }, true, 2) == "WARNING_BEARING",
                "A childless Status/Information tree is warning-bearing, never blocking.");

            // EMPTY stays distinct from a clean run.
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(new P16AzadeganEngineNotification[0], new string[0], true, 0) == "EMPTY",
                "An empty payload must remain EMPTY.");
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(new P16AzadeganEngineNotification[0], new string[0], true, 0) != "COMPLETED_CLEAN",
                "EMPTY must never be promoted to a clean run.");
        }

        // A child-bearing notification can never be classified as warning-only output.
        private static void P16AzadeganCalc_ChildNotificationsInaccessibleFailsClosed()
        {
            var notification = AzadeganNotification("Warning", 30183, "SWGNoActiveTerrainModelUsedInDesign",
                "There is no active terrain model in the hydraulic model. No terrain model is used in design.");
            notification.HasSubUserNotifications = true;

            string outcome = P16AzadeganCalculationHost.NotificationTreeOutcome(new[] { notification }, new[] { "Warning" }, true, 1);
            Check(outcome == "TREE_INCOMPLETE", "A child-bearing notification makes the scope incomplete.");
            Check(outcome != "WARNING_BEARING", "An incomplete tree must never be classified warning-bearing.");

            // The host reports it as a non-pass status, and that status is admissible as evidence only.
            var blocked = AzadeganCalculationWarningObservation();
            blocked.Status = P16AzadeganCalculationHost.TreeIncompleteStatus;
            blocked.NotificationsComplete = false;
            blocked.FreshnessVerified = false;
            blocked.Notifications[0].HasSubUserNotifications = true;
            P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(blocked));
            Check(blocked.Status != P16AzadeganCalculationHost.PassStatus &&
                  blocked.Status != P16AzadeganCalculationHost.PassWithWarningsStatus,
                "An incomplete notification tree must never carry a pass status.");
        }

        // An unobserved child could be an Error, so benign top-level levels prove nothing.
        private static void P16AzadeganCalc_HiddenChildErrorCannotBeRuledOut()
        {
            var benign = AzadeganNotification("Status", 1, "K1", "M1");
            var parent = AzadeganNotification("Information", 2, "K2", "M2");
            parent.HasSubUserNotifications = true;
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(
                    new[] { benign, parent }, new[] { "Status", "Information" }, true, 2) == "TREE_INCOMPLETE",
                "Benign top-level levels must not imply that an unobserved child is benign.");
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(
                    new[] { benign, parent }, new[] { "Status", "Information" }, true, 2) != "ERROR_LEVEL_BLOCKING",
                "An incomplete tree is not evidence of an Error either; it is simply unclassified.");
        }

        // A single child-bearing notification must not be lost among many siblings, and the top-level
        // bound must still fail closed.
        private static void P16AzadeganCalc_NotificationTreeBoundsAndDuplicates()
        {
            var many = Enumerable.Range(1, 50)
                .Select(i => AzadeganNotification("Warning", i, "K" + i, "M" + i)).ToArray();
            many[49].HasSubUserNotifications = true;
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(many, many.Select(n => n.Level).ToArray(), true, many.Length) == "TREE_INCOMPLETE",
                "One child-bearing notification among many must still mark the scope incomplete.");

            var duplicates = new[]
            {
                AzadeganNotification("Warning", 7, "K", "M"),
                AzadeganNotification("Warning", 7, "K", "M")
            };
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(duplicates, new[] { "Warning", "Warning" }, true, 2) == "WARNING_BEARING",
                "Duplicated top-level notifications stay warning-bearing when no child is reported.");
            duplicates[1].HasSubUserNotifications = true;
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(duplicates, new[] { "Warning", "Warning" }, true, 2) == "TREE_INCOMPLETE",
                "A duplicate must not mask a child-bearing sibling.");

            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(null, new string[0], true, 0) == "UNCLASSIFIABLE",
                "A null notification payload must fail closed.");
            Check(P16AzadeganCalculationHost.ClassifyNotificationLevels(Enumerable.Repeat("Warning", 513).ToArray(), true, 513) == "UNCLASSIFIABLE",
                "A payload beyond the bound must fail closed.");
        }

        // The pre-save snapshot states what it is: not persisted, not read back, not a pass.
        private static void P16AzadeganCalc_PreSaveStatusCannotBeMistakenForPersistence()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                // The fixture is deliberately pass-shaped: it carries a pass status and a save count.
                // Snapshotting must normalise it so the recovered document cannot read as a pass.
                P16AzadeganCalculationHost.PersistPreSaveEvidence(directory, AzadeganCalculationWarningObservation());
                var evidence = RuntimeHostProtocol.Serializer().Deserialize<P16AzadeganPreSaveEvidence>(
                    P16AzadeganCalculationProof.ReadPreSaveEvidence(directory));

                Check(evidence.Observation.Status == P16AzadeganCalculationHost.PreSaveObservationStatus,
                    "A pass-shaped observation must be normalised to the pre-save status.");
                Check(evidence.Observation.Status != "RESULT_CONTRACT_UNAVAILABLE",
                    "The snapshot must never keep the observation DTO's default status.");
                Check(evidence.Observation.Status != P16AzadeganCalculationHost.PassStatus &&
                      evidence.Observation.Status != P16AzadeganCalculationHost.PassWithWarningsStatus,
                    "The snapshot status must not be a pass status.");
                Check(evidence.EvidenceStage == P16AzadeganCalculationHost.PreSaveEvidenceStage,
                    "The stage marker must survive the status correction.");
                Check(!evidence.PersistedToModel && !evidence.ResultReadBack,
                    "The snapshot must still deny persistence and readback.");
            }
            finally { try { Directory.Delete(directory, true); } catch (IOException) { } }
        }

        // A pass may never be built on an incomplete notification tree.
        private static void P16AzadeganCalc_PassCannotCarryIncompleteTree()
        {
            var withChild = AzadeganCalculationWarningObservation();
            withChild.Notifications[0].HasSubUserNotifications = true;
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(withChild)));

            var notComplete = AzadeganCalculationWarningObservation();
            notComplete.NotificationsComplete = false;
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(notComplete)));

            // The complete, childless tree remains a valid warning-bearing pass.
            P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(AzadeganCalculationWarningObservation()));
            Check(true, "A complete childless tree is still accepted as warning-bearing evidence.");
        }

        // ---------------------------------------------------------------- detail-processor tree resolution

        // A deterministic double for the vendor detail surface. Every branch of the tree logic is
        // reachable without touching Bentley assemblies; nothing here proves live vendor behaviour.
        private sealed class FakeDetailResolver : IP16AzadeganDetailResolver
        {
            private readonly Func<P16AzadeganEngineNotification, P16AzadeganDetailResolution> _resolve;
            internal int Calls { get; private set; }
            internal FakeDetailResolver(Func<P16AzadeganEngineNotification, P16AzadeganDetailResolution> resolve) { _resolve = resolve; }

            public P16AzadeganDetailResolution Resolve(P16AzadeganEngineNotification parent)
            {
                Calls++;
                return _resolve(parent);
            }
        }

        private static P16AzadeganEngineNotification AzadeganChildBearing(int messageId, string level, int? timeStepIndex)
        {
            var notification = AzadeganNotification(level, messageId, "SWGNoActiveTerrainModelUsedInDesign",
                "There is no active terrain model in the hydraulic model. No terrain model is used in design.");
            notification.HasSubUserNotifications = true;
            notification.ElementId = -1;
            notification.ScenarioId = -1;
            notification.TimeStepIndex = timeStepIndex;
            return notification;
        }

        private static P16AzadeganDetailResolution AzadeganDetails(params P16AzadeganEngineNotification[] children) =>
            new P16AzadeganDetailResolution { Outcome = "RESOLVED", Children = children };

        // A child-bearing notification without a timestep can never be resolved, and the processor is
        // never invoked with a substituted context.
        private static void P16AzadeganCalc_ChildBearingWithoutTimeStepStaysBlocked()
        {
            var parent = AzadeganChildBearing(30183, "Warning", null);
            var nodes = new List<P16AzadeganEngineNotification> { parent };
            var seen = new P16AzadeganCalculationObservation();
            var resolver = new FakeDetailResolver(_ => { throw new InvalidOperationException("must not be invoked"); });

            string outcome = P16AzadeganCalculationHost.ResolveReportedChildren(nodes, resolver, seen);
            Check(outcome == "CONTEXT_INCOMPLETE", "A missing timestep must block detail resolution.");
            Check(resolver.Calls == 0, "The processor must not be invoked without the notification's own timestep.");
            Check(parent.ChildrenResolved == false, "An unresolved parent must record ChildrenResolved=false.");
            Check(parent.DetailOutcome == "CONTEXT_INCOMPLETE", "The parent must record why it was not resolved.");
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(
                    nodes.ToArray(), nodes.Select(n => n.Level).ToArray(), true, nodes.Count) == "TREE_INCOMPLETE",
                "An unresolved child-bearing notification keeps the tree incomplete.");
        }

        // A fully resolved tree is classified on its real nodes, not on the parent's flag.
        private static void P16AzadeganCalc_DetailProcessorResolvesChildren()
        {
            var parent = AzadeganChildBearing(30183, "Warning", 0);
            var nodes = new List<P16AzadeganEngineNotification> { parent };
            var seen = new P16AzadeganCalculationObservation();
            P16AzadeganEngineNotification received = null;
            var resolver = new FakeDetailResolver(p =>
            {
                received = p;
                return AzadeganDetails(
                    AzadeganNotification("Warning", 30184, "SWG_TERRAIN_DETAIL", "Design used ground surface elevations."),
                    AzadeganNotification("Information", 30185, "SWG_TERRAIN_INFO", "Terrain model absent; default elevations applied."));
            });

            string outcome = P16AzadeganCalculationHost.ResolveReportedChildren(nodes, resolver, seen);
            Check(outcome == "RESOLVED", "A fully captured tree resolves.");
            Check(received == parent, "The resolver must receive the child-bearing notification itself.");
            Check(received.TimeStepIndex == 0 && received.ElementId == -1 && received.ScenarioId == -1,
                "The resolver must receive the notification's own identifiers, including -1 globals.");
            Check(resolver.Calls == 1, "One child-bearing notification means exactly one invocation.");
            Check(seen.DetailProcessorInvocations == 1, "The invocation must be counted on the observation.");
            Check(parent.ChildrenResolved == true, "A resolved parent must record ChildrenResolved=true.");
            Check(parent.ChildCount == 2, "The parent must record how many children were captured.");
            Check(nodes.Count == 3, "Children are appended to the flattened tree.");
            Check(nodes[1].ParentIndex == 0 && nodes[1].Depth == 1, "Children must point at their parent and sit one level deeper.");
            Check(seen.NotificationTreeDepth == 0, "Depth is recorded by the capture path, not by the resolver.");
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(
                    nodes.ToArray(), nodes.Select(n => n.Level).ToArray(), true, nodes.Count) == "WARNING_BEARING",
                "A resolved benign tree is warning-bearing.");
        }

        // Resolving the children does not soften the error gate: a hidden Error now becomes visible and blocks.
        private static void P16AzadeganCalc_ResolvedTreeStillBlocksOnChildError()
        {
            var parent = AzadeganChildBearing(30183, "Warning", 0);
            var nodes = new List<P16AzadeganEngineNotification> { parent };
            var seen = new P16AzadeganCalculationObservation();
            var resolver = new FakeDetailResolver(_ => AzadeganDetails(
                AzadeganNotification("Error", 30190, "SWG_TERRAIN_FAILED", "Terrain model could not be applied.")));

            Check(P16AzadeganCalculationHost.ResolveReportedChildren(nodes, resolver, seen) == "RESOLVED",
                "The tree itself resolves; the child carries the verdict.");
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(
                    nodes.ToArray(), nodes.Select(n => n.Level).ToArray(), true, nodes.Count) == "ERROR_LEVEL_BLOCKING",
                "A resolved Error child must block the operation.");
        }

        // Nested children resolve within the declared bounds and are flattened in parent-first order.
        private static void P16AzadeganCalc_NestedChildrenResolvedWithinBounds()
        {
            var parent = AzadeganChildBearing(30183, "Warning", 0);
            var nodes = new List<P16AzadeganEngineNotification> { parent };
            var seen = new P16AzadeganCalculationObservation();
            var resolver = new FakeDetailResolver(p => p.Depth == 0
                ? AzadeganDetails(AzadeganChildBearing(30184, "Warning", 0))
                : AzadeganDetails(AzadeganNotification("Information", 30185, "LEAF", "Leaf detail.")));

            string outcome = P16AzadeganCalculationHost.ResolveReportedChildren(nodes, resolver, seen);
            Check(outcome == "RESOLVED", "A two-level tree within bounds resolves.");
            Check(resolver.Calls == 2, "Each child-bearing node is resolved exactly once.");
            Check(nodes.Count == 3, "Grandchildren join the flattened tree.");
            Check(nodes[2].Depth == 2 && nodes[2].ParentIndex == 1, "Nested children carry depth and parent index.");
            Check(parent.ChildrenResolved == true && nodes[1].ChildrenResolved == true,
                "Resolution propagates through the whole subtree.");
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(
                    nodes.ToArray(), nodes.Select(n => n.Level).ToArray(), true, nodes.Count) == "WARNING_BEARING",
                "A fully resolved nested tree is warning-bearing.");
        }

        // An empty detail list contradicts the parent's own claim and must never clear it.
        private static void P16AzadeganCalc_EmptyDetailListFailsClosed()
        {
            var parent = AzadeganChildBearing(30183, "Warning", 0);
            var nodes = new List<P16AzadeganEngineNotification> { parent };
            var seen = new P16AzadeganCalculationObservation();
            var resolver = new FakeDetailResolver(_ => AzadeganDetails());

            Check(P16AzadeganCalculationHost.ResolveReportedChildren(nodes, resolver, seen) == "EMPTY_WHILE_CHILDREN_REPORTED",
                "An empty detail list must fail closed.");
            Check(parent.ChildrenResolved == false, "An empty detail list must not mark the parent resolved.");
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(
                    nodes.ToArray(), nodes.Select(n => n.Level).ToArray(), true, nodes.Count) == "TREE_INCOMPLETE",
                "An empty detail list leaves the scope incomplete.");
            Check((seen.NotificationCaptureLimitation ?? "").Contains("empty detail list"),
                "The empty-list contradiction must be recorded as a limitation.");
        }

        // Every processor failure mode is an explicit, blocking outcome.
        private static void P16AzadeganCalc_DetailProcessorFailureFailsClosed()
        {
            foreach (string outcome in new[] { "PROCESS_UNSUCCESSFUL", "PROCESSOR_EXCEPTION", "DETAILS_INVALID", "CHILD_INVALID" })
            {
                var parent = AzadeganChildBearing(30183, "Warning", 0);
                var nodes = new List<P16AzadeganEngineNotification> { parent };
                var seen = new P16AzadeganCalculationObservation();
                var resolver = new FakeDetailResolver(_ => new P16AzadeganDetailResolution
                {
                    Outcome = outcome,
                    Limitation = "synthetic " + outcome
                });

                Check(P16AzadeganCalculationHost.ResolveReportedChildren(nodes, resolver, seen) == outcome,
                    "Processor outcome " + outcome + " must propagate unchanged.");
                Check(parent.ChildrenResolved == false, "Processor outcome " + outcome + " must leave the parent unresolved.");
                Check(P16AzadeganCalculationHost.NotificationTreeOutcome(
                        nodes.ToArray(), nodes.Select(n => n.Level).ToArray(), true, nodes.Count) == "TREE_INCOMPLETE",
                    "Processor outcome " + outcome + " must keep the tree incomplete.");
            }

            // A resolver that throws, and one that returns nothing at all.
            var throwing = AzadeganChildBearing(30183, "Warning", 0);
            var throwingNodes = new List<P16AzadeganEngineNotification> { throwing };
            Check(P16AzadeganCalculationHost.ResolveReportedChildren(throwingNodes,
                    new FakeDetailResolver(_ => { throw new InvalidOperationException("synthetic"); }),
                    new P16AzadeganCalculationObservation()) == "PROCESSOR_EXCEPTION",
                "A throwing resolver must fail closed.");

            var missing = AzadeganChildBearing(30183, "Warning", 0);
            var missingNodes = new List<P16AzadeganEngineNotification> { missing };
            Check(P16AzadeganCalculationHost.ResolveReportedChildren(missingNodes,
                    new FakeDetailResolver(_ => null), new P16AzadeganCalculationObservation()) == "DETAILS_INVALID",
                "A null resolution must fail closed.");

            // No resolver at all is also a block, never an implicit clearance.
            var noResolver = AzadeganChildBearing(30183, "Warning", 0);
            var noResolverNodes = new List<P16AzadeganEngineNotification> { noResolver };
            Check(P16AzadeganCalculationHost.ResolveReportedChildren(noResolverNodes, null,
                    new P16AzadeganCalculationObservation()) == "RESOLVER_UNAVAILABLE",
                "An unavailable detail path must fail closed.");
            Check(noResolver.ChildrenResolved == false, "An unavailable detail path must leave the parent unresolved.");
        }

        // The recursion-depth and invocation bounds stop the walk instead of guessing.
        private static void P16AzadeganCalc_DetailBoundsEnforced()
        {
            // Depth bound: a node already at the limit may not recurse further.
            var deep = AzadeganChildBearing(30183, "Warning", 0);
            deep.Depth = P16AzadeganCalculationHost.MaxDetailDepth;
            var deepNodes = new List<P16AzadeganEngineNotification> { deep };
            var deepSeen = new P16AzadeganCalculationObservation();
            var deepResolver = new FakeDetailResolver(_ => { throw new InvalidOperationException("must not be invoked"); });
            Check(P16AzadeganCalculationHost.ResolveReportedChildren(deepNodes, deepResolver, deepSeen) == "DEPTH_LIMIT",
                "A node at the depth bound must not be resolved further.");
            Check(deepResolver.Calls == 0, "The depth bound must be checked before invocation.");

            // Invocation bound: more child-bearing notifications than the budget allows.
            var many = new List<P16AzadeganEngineNotification>();
            for (int i = 0; i < P16AzadeganCalculationHost.MaxDetailProcessorInvocations + 1; i++)
                many.Add(AzadeganChildBearing(30000 + i, "Warning", 0));
            var manySeen = new P16AzadeganCalculationObservation();
            var manyResolver = new FakeDetailResolver(_ => AzadeganDetails(
                AzadeganNotification("Information", 1, "LEAF", "Leaf detail.")));
            Check(P16AzadeganCalculationHost.ResolveReportedChildren(many, manyResolver, manySeen) == "INVOCATION_LIMIT",
                "Exceeding the invocation budget must fail closed.");
            Check(manyResolver.Calls == P16AzadeganCalculationHost.MaxDetailProcessorInvocations,
                "The invocation budget must be enforced exactly.");
            Check(many.Any(n => n.ChildrenResolved != true), "Nodes past the budget stay unresolved.");

            // Node bound: children that would push the flattened tree past the cap.
            var floodParent = AzadeganChildBearing(30183, "Warning", 0);
            var floodNodes = new List<P16AzadeganEngineNotification> { floodParent };
            var floodSeen = new P16AzadeganCalculationObservation();
            var flood = Enumerable.Range(0, 600)
                .Select(i => AzadeganNotification("Information", 40000 + i, "K" + i, "M" + i)).ToArray();
            Check(P16AzadeganCalculationHost.ResolveReportedChildren(floodNodes,
                    new FakeDetailResolver(_ => AzadeganDetails(flood)), floodSeen) == "NODE_LIMIT",
                "Exceeding the node bound must fail closed.");
            Check(floodParent.ChildrenResolved == false, "A node-bound overflow must leave the parent unresolved.");
        }

        // The observation validator must reject a pass that carries an unresolved child-bearing node,
        // and must accept one whose children were genuinely captured.
        private static void P16AzadeganCalc_PassCannotCarryUnresolvedChildren()
        {
            // Unresolved: the old blanket rule still applies.
            var unresolved = AzadeganCalculationWarningObservation();
            unresolved.Notifications[0].HasSubUserNotifications = true;
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(unresolved)));

            // Resolved: a parent plus its captured child is admissible as warning-bearing evidence.
            var resolved = AzadeganCalculationWarningObservation();
            var parent = AzadeganChildBearing(30183, "Warning", 0);
            parent.ChildrenResolved = true;
            parent.ChildCount = 1;
            var child = AzadeganNotification("Information", 30184, "SWG_TERRAIN_INFO", "Terrain model absent.");
            child.Depth = 1;
            child.ParentIndex = 0;
            resolved.Notifications = new[] { parent, child };
            resolved.NotificationCount = 2;
            resolved.NotificationLevelSummary = "Warning=1,Information=1";
            resolved.NotificationTreeDepth = 1;
            resolved.DetailProcessorOutcome = "RESOLVED";
            resolved.DetailProcessorInvocations = 1;
            P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(resolved));
            Check(true, "A fully resolved warning tree remains admissible as evidence.");

            // A resolved parent whose child is an Error must still be rejected as a pass.
            var withError = AzadeganCalculationWarningObservation();
            var errorParent = AzadeganChildBearing(30183, "Warning", 0);
            errorParent.ChildrenResolved = true;
            errorParent.ChildCount = 1;
            var errorChild = AzadeganNotification("Error", 30190, "SWG_TERRAIN_FAILED", "Terrain could not be applied.");
            errorChild.Depth = 1;
            errorChild.ParentIndex = 0;
            withError.Notifications = new[] { errorParent, errorChild };
            withError.NotificationCount = 2;
            withError.NotificationTreeDepth = 1;
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(withError)));
        }

        // The tree shape is validated, so a mis-serialised or fabricated tree cannot claim completeness.
        private static void P16AzadeganCalc_TreeShapeValidated()
        {
            var good = new P16AzadeganCalculationObservation
            {
                Notifications = new[] { AzadeganChildBearing(30183, "Warning", 0) },
                NotificationTreeDepth = 0
            };
            Check(P16AzadeganCalculationHost.TreeShapeIsValid(good), "A depth-0 root is a valid tree.");

            var forwardParent = new P16AzadeganCalculationObservation
            {
                Notifications = new[]
                {
                    AzadeganNotification("Warning", 1, "K", "M"),
                    AzadeganChildBearing(30183, "Warning", 0)
                },
                NotificationTreeDepth = 1
            };
            forwardParent.Notifications[0].Depth = 1;
            forwardParent.Notifications[0].ParentIndex = 1;
            Check(!P16AzadeganCalculationHost.TreeShapeIsValid(forwardParent),
                "A parent that follows its child is rejected.");

            var wrongDepth = new P16AzadeganCalculationObservation
            {
                Notifications = new[]
                {
                    AzadeganChildBearing(30183, "Warning", 0),
                    AzadeganNotification("Information", 2, "K2", "M2")
                },
                NotificationTreeDepth = 1
            };
            wrongDepth.Notifications[1].Depth = 2;
            wrongDepth.Notifications[1].ParentIndex = 0;
            Check(!P16AzadeganCalculationHost.TreeShapeIsValid(wrongDepth),
                "A child that skips a level is rejected.");

            var depthMismatch = new P16AzadeganCalculationObservation
            {
                Notifications = new[] { AzadeganChildBearing(30183, "Warning", 0) },
                NotificationTreeDepth = 3
            };
            Check(!P16AzadeganCalculationHost.TreeShapeIsValid(depthMismatch),
                "A declared tree depth that no node reaches is rejected.");

            var rootWithParent = new P16AzadeganCalculationObservation
            {
                Notifications = new[] { AzadeganChildBearing(30183, "Warning", 0) },
                NotificationTreeDepth = 0
            };
            rootWithParent.Notifications[0].ParentIndex = 0;
            Check(!P16AzadeganCalculationHost.TreeShapeIsValid(rootWithParent),
                "A node cannot be its own parent.");
        }

        // ---------------------------------------------------------------- verified known leaf

        // The exact notification the installed SewerCAD engine emits for
        // SWGNoActiveTerrainModelUsedInDesign. Every field is the observed live tuple; the
        // HasSubUserNotifications flag is true only because UserNotificationBase's constructor sets it.
        private static P16AzadeganEngineNotification AzadeganKnownLeafNotification()
        {
            return new P16AzadeganEngineNotification
            {
                MessageId = P16AzadeganCalculationHost.KnownLeafMessageId,
                ElementId = P16AzadeganCalculationHost.KnownLeafGlobalIdentifier,
                ElementType = P16AzadeganCalculationHost.KnownLeafElementType,
                Level = "Warning",
                MessageKey = P16AzadeganCalculationHost.KnownLeafMessageKey,
                Parameters = new string[0],
                ScenarioId = P16AzadeganCalculationHost.KnownLeafGlobalIdentifier,
                SourceKey = P16AzadeganCalculationHost.KnownLeafSourceKey,
                TimeStepIndex = P16AzadeganCalculationHost.KnownLeafGlobalIdentifier,
                Message = "There is no active terrain model in the hydraulic model. No terrain model is used in design.",
                HelpId = "",
                HasSubUserNotifications = true,
                RuntimeTypeFullName = P16AzadeganCalculationHost.KnownLeafRuntimeType,
                RuntimeAssemblyFullName = P16AzadeganCalculationHost.KnownLeafRuntimeAssembly,
                Depth = 0,
                ParentIndex = null
            };
        }

        // A pass-shaped observation whose whole notification scope is the one known leaf.
        private static P16AzadeganCalculationObservation AzadeganKnownLeafObservation()
        {
            var observation = AzadeganCalculationWarningObservation();
            observation.Notifications = new[] { AzadeganKnownLeafNotification() };
            observation.NotificationCount = 1;
            observation.NotificationLevelSummary = "Warning=1";
            observation.NotificationsComplete = true;
            observation.NotificationTreeDepth = 0;
            observation.DetailProcessorInvocations = 0;
            observation.DetailProcessorOutcome = "NOT_REPORTED";
            observation.NotificationCaptureLimitation = P16AzadeganCalculationHost.KnownLeafLimitation;
            return observation;
        }

        // The exact observed tuple is recognised, and the vendor detail surface is never touched.
        private static void P16AzadeganCalc_KnownLeafRecognised()
        {
            var node = AzadeganKnownLeafNotification();
            Check(P16AzadeganCalculationHost.IsVerifiedKnownLeaf(node, 1),
                "The exact observed notification tuple must be recognised as a verified known leaf.");

            var nodes = new List<P16AzadeganEngineNotification> { node };
            var seen = new P16AzadeganCalculationObservation();
            var resolver = new FakeDetailResolver(_ =>
            {
                throw new InvalidOperationException("the detail processor must never be invoked for a known leaf");
            });

            Check(P16AzadeganCalculationHost.ResolveReportedChildren(nodes, resolver, seen) == "NOT_REPORTED",
                "A known leaf reports nothing to resolve.");
            Check(node.LeafClassification == P16AzadeganCalculationHost.KnownLeafClassification,
                "The node must carry the explicit known-leaf classification.");
            Check(node.DetailOutcome == "KNOWN_LEAF", "The node must record how it was settled.");
            Check(node.ChildrenResolved == null, "A known leaf has no children, so ChildrenResolved stays null.");
            Check(seen.DetailProcessorInvocations == 0, "No detail-processor invocation may be claimed.");
            Check(node.HasSubUserNotifications == true, "The captured vendor flag must not be rewritten.");
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(nodes.ToArray(), new[] { "Warning" }, true, 1) == "WARNING_BEARING",
                "A lone verified known leaf is warning-bearing, not incomplete.");
            Check((seen.NotificationCaptureLimitation ?? "").Contains(P16AzadeganCalculationHost.KnownLeafClassification),
                "The evidence must explain why the vendor flag is not authoritative.");
        }

        // The original warning and its true flag survive into the serialized evidence.
        private static void P16AzadeganCalc_KnownLeafEvidencePreserved()
        {
            var observation = AzadeganKnownLeafObservation();
            observation.Notifications[0].LeafClassification = P16AzadeganCalculationHost.KnownLeafClassification;
            observation.Notifications[0].DetailOutcome = "KNOWN_LEAF";
            string json = AzadeganCalculationJson(observation);
            P16AzadeganCalculationHost.ValidateObservation(json);

            var restored = RuntimeHostProtocol.Serializer().Deserialize<P16AzadeganCalculationObservation>(json);
            var node = restored.Notifications[0];
            Check(node.HasSubUserNotifications == true,
                "The observation must retain HasSubUserNotifications=true exactly as the vendor reported it.");
            Check(node.LeafClassification == P16AzadeganCalculationHost.KnownLeafClassification,
                "The classification must round-trip.");
            Check(node.DetailOutcome == "KNOWN_LEAF", "The detail outcome must round-trip.");
            Check(node.MessageId == 30183 && node.MessageKey == "SWGNoActiveTerrainModelUsedInDesign" &&
                  node.SourceKey == "HydraulicsValidationSource" && node.Level == "Warning",
                "The warning itself must never be discarded or relabelled.");
            Check(node.ChildrenResolved == null, "The leaf must not claim resolved children it never had.");
            Check(restored.DetailProcessorInvocations == 0, "No child enumeration may be claimed.");
        }

        // Every critical field is part of the rule: one difference and the rule stops applying.
        private static void P16AzadeganCalc_KnownLeafRequiresExactTuple()
        {
            var mutations = new List<KeyValuePair<string, Action<P16AzadeganEngineNotification>>>
            {
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("RuntimeTypeFullName",
                    n => n.RuntimeTypeFullName = "Haestad.Support.User.StandardUserNotification"),
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("RuntimeAssemblyFullName",
                    n => n.RuntimeAssemblyFullName = "Haestad.Support, Version=24.0.1.0, Culture=neutral, PublicKeyToken=b4db808388021f6e"),
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("MessageId", n => n.MessageId = 30184),
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("MessageKey",
                    n => n.MessageKey = "SWGSomeOtherTerrainMessage"),
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("SourceKey",
                    n => n.SourceKey = "HydraulicsResultsSource"),
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("Level", n => n.Level = "Information"),
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("ElementType", n => n.ElementType = 12),
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("ElementId", n => n.ElementId = 119),
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("ScenarioId", n => n.ScenarioId = 353),
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("TimeStepIndex", n => n.TimeStepIndex = 0),
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("HasSubUserNotifications",
                    n => n.HasSubUserNotifications = false),
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("Parameters",
                    n => n.Parameters = new[] { "SSW-001" }),
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("HelpId", n => n.HelpId = "HH1001"),
                new KeyValuePair<string, Action<P16AzadeganEngineNotification>>("Depth", n => n.Depth = 1)
            };

            foreach (var mutation in mutations)
            {
                var node = AzadeganKnownLeafNotification();
                mutation.Value(node);
                Check(!P16AzadeganCalculationHost.IsVerifiedKnownLeaf(node, 1),
                    "Mutating " + mutation.Key + " must disable the known-leaf rule.");

                var nodes = new List<P16AzadeganEngineNotification> { node };
                P16AzadeganCalculationHost.MarkVerifiedKnownLeaves(nodes, new P16AzadeganCalculationObservation());
                Check(node.LeafClassification == null,
                    "Mutating " + mutation.Key + " must not yield a known-leaf classification.");
            }

            // The exact tuple still verifies, so the loop above is not vacuous.
            Check(P16AzadeganCalculationHost.IsVerifiedKnownLeaf(AzadeganKnownLeafNotification(), 1),
                "The unmutated tuple must still verify.");
        }

        // The rule is scoped to a single top-level notification and never generalises.
        private static void P16AzadeganCalc_KnownLeafRequiresSingleTopLevel()
        {
            var leaf = AzadeganKnownLeafNotification();
            Check(!P16AzadeganCalculationHost.IsVerifiedKnownLeaf(leaf, 2),
                "The rule must require exactly one top-level notification.");

            var sibling = AzadeganNotification("Information", 2102, "MSG_TIME_STEP_REDUCED", "Time step was reduced.");
            var both = new[] { leaf, sibling };
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(both, new[] { "Warning", "Information" }, true, 2) == "TREE_INCOMPLETE",
                "An additional top-level notification must disable the single-notification special case.");

            var nodes = new List<P16AzadeganEngineNotification> { leaf, sibling };
            P16AzadeganCalculationHost.MarkVerifiedKnownLeaves(nodes, new P16AzadeganCalculationObservation());
            Check(leaf.LeafClassification == null, "No classification may be minted when a sibling is present.");
        }

        // An unrelated child-bearing notification keeps the original fail-closed behaviour.
        private static void P16AzadeganCalc_UnrelatedChildBearingStillBlocks()
        {
            var unrelated = AzadeganChildBearing(30183, "Warning", 0);
            Check(!P16AzadeganCalculationHost.IsVerifiedKnownLeaf(unrelated, 1),
                "A child-bearing notification that does not match the tuple is not a known leaf.");

            var nodes = new List<P16AzadeganEngineNotification> { unrelated };
            var seen = new P16AzadeganCalculationObservation();
            var resolver = new FakeDetailResolver(_ => AzadeganDetails(
                AzadeganNotification("Information", 30185, "SWG_TERRAIN_INFO", "Terrain absent.")));
            Check(P16AzadeganCalculationHost.ResolveReportedChildren(nodes, resolver, seen) == "RESOLVED",
                "An unrelated child-bearing notification still goes through the supported detail path.");
            Check(resolver.Calls == 1, "The detail processor must still be invoked for it.");

            var unresolved = AzadeganChildBearing(30183, "Warning", null);
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(new[] { unresolved }, new[] { "Warning" }, true, 1) == "TREE_INCOMPLETE",
                "An unresolved child-bearing notification must still keep the tree incomplete.");
        }

        // Error and unknown levels are untouched by the leaf rule.
        private static void P16AzadeganCalc_KnownLeafDoesNotSoftenLevelGates()
        {
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(
                    new[] { AzadeganNotification("Error", 3001, "MSG_ENGINE_FAILED", "Engine failed.") },
                    new[] { "Error" }, true, 1) == "ERROR_LEVEL_BLOCKING",
                "An error-level notification must still block.");
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(
                    new[] { AzadeganNotification("Critical", 3002, "MSG_UNKNOWN", "Unknown severity.") },
                    new[] { "Critical" }, true, 1) == "UNCLASSIFIABLE",
                "An unknown level must still be unclassifiable.");

            // A look-alike at Error level is not a known leaf, and it blocks.
            var errorLeaf = AzadeganKnownLeafNotification();
            errorLeaf.Level = "Error";
            Check(!P16AzadeganCalculationHost.IsVerifiedKnownLeaf(errorLeaf, 1),
                "The known-leaf rule must not apply to an error-level notification.");
            Check(P16AzadeganCalculationHost.NotificationTreeOutcome(new[] { errorLeaf }, new[] { "Error" }, true, 1) == "TREE_INCOMPLETE",
                "An error-level look-alike must remain blocked.");

            var observation = AzadeganKnownLeafObservation();
            observation.Notifications = new[] { errorLeaf };
            observation.NotificationLevelSummary = "Error=1";
            observation.Notifications[0].LeafClassification = P16AzadeganCalculationHost.KnownLeafClassification;
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(observation)));
        }

        // A forged or inconsistent classification is rejected, not ignored.
        private static void P16AzadeganCalc_ValidatorRejectsForgedKnownLeaf()
        {
            // The tuple no longer matches, so the declared classification is a lie.
            var mutated = AzadeganKnownLeafObservation();
            mutated.Notifications[0].SourceKey = "HydraulicsResultsSource";
            mutated.Notifications[0].LeafClassification = P16AzadeganCalculationHost.KnownLeafClassification;
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(mutated)));

            // A made-up classification string is not a classification.
            var bogus = AzadeganKnownLeafObservation();
            bogus.Notifications[0].LeafClassification = "KNOWN_LEAF";
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(bogus)));

            // A classification minted on an ordinary warning notification is rejected.
            var ordinary = AzadeganCalculationWarningObservation();
            ordinary.Notifications[0].LeafClassification = P16AzadeganCalculationHost.KnownLeafClassification;
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(ordinary)));

            // The honest document still validates, so the check is not vacuous.
            var honest = AzadeganKnownLeafObservation();
            honest.Notifications[0].LeafClassification = P16AzadeganCalculationHost.KnownLeafClassification;
            honest.Notifications[0].DetailOutcome = "KNOWN_LEAF";
            P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(honest));
        }

        // Passing the notification gate fabricates nothing: every readback, unit, timestep,
        // persistence and freshness gate still applies.
        private static void P16AzadeganCalc_KnownLeafFabricatesNothing()
        {
            var observation = AzadeganKnownLeafObservation();
            observation.Notifications[0].LeafClassification = P16AzadeganCalculationHost.KnownLeafClassification;
            observation.Notifications[0].DetailOutcome = "KNOWN_LEAF";
            Check(observation.Notifications.Length == 1 && observation.Notifications[0].ChildCount == null,
                "A known leaf must not gain fabricated children.");

            // The leaf does not satisfy the readback gate on its own.
            var noRead = AzadeganKnownLeafObservation();
            noRead.Notifications[0].LeafClassification = P16AzadeganCalculationHost.KnownLeafClassification;
            noRead.FlowReadInvocations = 0;
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(noRead)));

            var noValue = AzadeganKnownLeafObservation();
            noValue.Notifications[0].LeafClassification = P16AzadeganCalculationHost.KnownLeafClassification;
            noValue.RawValue = null;
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(noValue)));

            var noUnit = AzadeganKnownLeafObservation();
            noUnit.Notifications[0].LeafClassification = P16AzadeganCalculationHost.KnownLeafClassification;
            noUnit.WorkingUnitIdentity = null;
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(noUnit)));

            var noFreshness = AzadeganKnownLeafObservation();
            noFreshness.Notifications[0].LeafClassification = P16AzadeganCalculationHost.KnownLeafClassification;
            noFreshness.FreshnessVerified = false;
            Throws<InvalidDataException>(() => P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(noFreshness)));

            // As pre-save evidence it is admissible, and it still claims no value.
            var preSave = AzadeganKnownLeafObservation();
            preSave.Notifications[0].LeafClassification = P16AzadeganCalculationHost.KnownLeafClassification;
            preSave.Notifications[0].DetailOutcome = "KNOWN_LEAF";
            preSave.Status = P16AzadeganCalculationHost.PreSaveObservationStatus;
            preSave.FlowReadInvocations = 0;
            preSave.RawValue = null;
            preSave.RawValueClassification = null;
            preSave.ValueClrType = null;
            preSave.TimeStepIndex = null;
            preSave.TimeSeconds = null;
            preSave.WorkingUnitSymbol = null;
            preSave.WorkingUnitIdentity = null;
            preSave.StorageUnitIdentity = null;
            preSave.UnitEvidence = null;
            preSave.FreshnessVerified = false;
            preSave.ProvenanceChain = null;
            preSave.SaveInvocations = 0;
            preSave.SaveSucceeded = false;
            preSave.ReopenOccurred = false;
            P16AzadeganCalculationHost.ValidateObservation(AzadeganCalculationJson(preSave));
            Check(preSave.RawValue == null && preSave.TimeStepIndex == null && preSave.WorkingUnitIdentity == null &&
                  !preSave.FreshnessVerified && preSave.SaveInvocations == 0 && !preSave.ReopenOccurred,
                "A known-leaf capture must carry no hydraulic value, timestep, unit, persistence or freshness claim.");
        }
    }
}
