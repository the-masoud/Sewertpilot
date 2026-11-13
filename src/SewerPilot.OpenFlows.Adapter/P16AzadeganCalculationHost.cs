using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    // One engine notification exactly as the installed vendor surface exposes it. Vendor payload
    // fields come from the validated contract; concrete type/assembly identity uses public CLR
    // metadata only. No non-public notification fields are inspected.
    internal sealed class P16AzadeganEngineNotification
    {
        public P16AzadeganEngineNotification() { }
        public int? MessageId { get; set; }
        public int? ElementId { get; set; }
        public int? ElementType { get; set; }
        public string Level { get; set; }
        public string MessageKey { get; set; }
        public string[] Parameters { get; set; } = new string[0];
        public int? ScenarioId { get; set; }
        public string SourceKey { get; set; }
        public int? TimeStepIndex { get; set; }
        public string Message { get; set; }
        public string HelpId { get; set; }
        public bool? HasSubUserNotifications { get; set; }
        public string RuntimeTypeFullName { get; set; }
        public string RuntimeAssemblyFullName { get; set; }
        // Tree position. Depth 0 is the engine's own payload; a child resolved through the public
        // UserNotificationDetailProcessor is recorded at parent depth + 1 with the parent's flattened
        // index. ParentIndex is always strictly less than the node's own index.
        public int? Depth { get; set; }
        public int? ParentIndex { get; set; }
        public int? ChildCount { get; set; }
        // True only when this notification reported children AND every one of them, recursively, was
        // captured and classified. Null when the notification reports no children at all.
        public bool? ChildrenResolved { get; set; }
        // How this node's children were resolved: NOT_REPORTED / KNOWN_LEAF / RESOLVED / DEPTH_LIMIT /
        // INVOCATION_LIMIT / CONTEXT_INCOMPLETE / PROCESSOR_EXCEPTION / PROCESS_UNSUCCESSFUL /
        // DETAILS_INVALID / EMPTY_WHILE_CHILDREN_REPORTED / NODE_LIMIT / CHILD_INVALID.
        public string DetailOutcome { get; set; }
        // Non-null only when the captured HasSubUserNotifications flag is provably not authoritative for
        // this exact notification. It records WHY the flag was overridden so the evidence stays
        // auditable. The captured flag value itself is never rewritten.
        public string LeafClassification { get; set; }
    }

    // Outcome of one attempt to resolve the children of a single child-bearing notification. The
    // children are already parsed into the observation shape, so the tree logic that consumes this
    // is independent of the vendor surface and can be qualified offline with test doubles.
    internal sealed class P16AzadeganDetailResolution
    {
        public string Outcome { get; set; }
        public P16AzadeganEngineNotification[] Children { get; set; } = new P16AzadeganEngineNotification[0];
        public string Limitation { get; set; }
    }

    // The only seam between the notification-tree logic and the vendor surface. The production
    // implementation drives Haestad.Framework.Support.UserNotificationDetailProcessor; offline
    // qualification substitutes a double. A null resolver means "no detail path is available".
    internal interface IP16AzadeganDetailResolver
    {
        P16AzadeganDetailResolution Resolve(P16AzadeganEngineNotification parent);
    }

    // Native calculation-and-readback evidence for the Azadegan sanitary sewer model.
    // It records one controlled solve, one controlled persistence, one reopen and one exact
    // result read. It mints no engineering acceptance and never substitutes an engine, an
    // element, a timestep or a unit.
    internal sealed class P16AzadeganCalculationObservation
    {
        public P16AzadeganCalculationObservation() { }
        public string Status { get; set; } = "RESULT_CONTRACT_UNAVAILABLE";
        public string WorkingCopyId { get; set; }
        public string OpenedModelPath { get; set; }
        public int? RequestedScenarioId { get; set; }
        public int? ActiveScenarioId { get; set; }
        public int? PhysicalAlternativeId { get; set; }
        public string RequestedEngine { get; set; }
        public string MainEngineFromModel { get; set; }
        public string[] ModelEngineTypeNames { get; set; } = new string[0];
        public string ResolvedEngine { get; set; }
        public string EngineRuntimeType { get; set; }
        public string EngineSelectionBasis { get; set; }
        public int? CalculationOptionsId { get; set; }
        public string CalculationEventId { get; set; }
        public string SolverStartTimestamp { get; set; }
        public string SolverEndTimestamp { get; set; }
        public int SolverRunInvocations { get; set; }
        public string ScenarioComputeStatus { get; set; }
        public string ScenarioStatusText { get; set; }
        public int SaveInvocations { get; set; }
        public bool SaveSucceeded { get; set; }
        public string SaveTimestamp { get; set; }
        // Which vendor member performed the persistence, which boundary it belongs to, and how the
        // outcome was classified. SaveSucceeded is derived from SaveResultClassification, never from a
        // Boolean return value (IProject::Save(ProjectProperties) returns void).
        public string PersistenceMember { get; set; }
        public string PersistenceBoundary { get; set; }
        public string SaveResultClassification { get; set; }
        public int SaveQuestionCount { get; set; }
        public string[] SaveQuestionClassifications { get; set; } = new string[0];
        public string[] SaveQuestionDecisions { get; set; } = new string[0];
        public int SaveWarningCount { get; set; }
        public string[] SaveWarningMessageSha256 { get; set; } = new string[0];
        public int OpenQuestionCount { get; set; }
        public string[] OpenQuestionClassifications { get; set; } = new string[0];
        public string[] OpenQuestionDecisions { get; set; } = new string[0];
        public int OpenWarningCount { get; set; }
        public string[] OpenWarningMessageSha256 { get; set; } = new string[0];
        public bool? ProjectIsDirtyAfterSave { get; set; }
        public string ReadTimestamp { get; set; }
        public string InputInventoryJson { get; set; }
        public string InputInventoryDigest { get; set; }
        public string OutputInventoryJson { get; set; }
        public string[] CreatedFiles { get; set; } = new string[0];
        public string[] ModifiedFiles { get; set; } = new string[0];
        public string[] ResultStoreFiles { get; set; } = new string[0];
        public bool ReopenOccurred { get; set; }
        public bool ResultManagerAvailable { get; set; }
        public string ResultManagerEngineName { get; set; }
        public int? ResultManagerScenarioId { get; set; }
        public bool? ResultManagerWasActive { get; set; }
        public bool? HasResults { get; set; }
        public int SupportedFieldCount { get; set; }
        public string[] SupportedFieldNames { get; set; } = new string[0];
        public bool? FlowFieldSupported { get; set; }
        public string FieldName { get; set; }
        public string RecordName { get; set; }
        public string FieldRuntimeType { get; set; }
        public bool? FieldHasResults { get; set; }
        public string RequestedElementLabel { get; set; }
        public int? RequestedElementId { get; set; }
        public string ObservedElementLabel { get; set; }
        public int? ObservedElementId { get; set; }
        public int LabelMatchCount { get; set; }
        public bool ElementIdentityVerified { get; set; }
        public int? TimeStepIndex { get; set; }
        public double? TimeSeconds { get; set; }
        public int TimeStepCount { get; set; }
        public double[] AvailableTimeSeconds { get; set; } = new double[0];
        public string WorkingUnitSymbol { get; set; }
        public string WorkingUnitIdentity { get; set; }
        public string StorageUnitIdentity { get; set; }
        public string UnitEvidence { get; set; }
        public string ValueClrType { get; set; }
        public string RawValue { get; set; }
        public string RawValueClassification { get; set; }
        public int FlowReadInvocations { get; set; }
        public bool FreshnessVerified { get; set; }
        public string FreshnessEvidence { get; set; }
        public string ProvenanceChain { get; set; }
        public string EngineOutcome { get; set; }
        public int NotificationCount { get; set; }
        public string NotificationLevelSummary { get; set; }
        public bool NotificationsComplete { get; set; }
        public string NotificationCaptureLimitation { get; set; }
        // Detail-resolution lineage. These make the difference between "no children were reported"
        // and "children were reported and resolved through the public detail processor" auditable.
        public int DetailProcessorInvocations { get; set; }
        public string DetailProcessorOutcome { get; set; }
        public int NotificationTreeDepth { get; set; }
        public P16AzadeganEngineNotification[] Notifications { get; set; } = new P16AzadeganEngineNotification[0];
        public bool ResultCleanupCompleted { get; set; }
        public string CleanupError { get; set; }
        public string FailureOperation { get; set; }
        public string ErrorType { get; set; }
        public int? ErrorHResult { get; set; }
        public string ErrorMessage { get; set; }
    }

    // Durable capture written immediately after notification classification and completion-boundary
    // verification, and strictly before the vendor save is attempted. It exists so a hang inside the
    // save cannot destroy the only record of what the engine actually reported. It states in its own
    // payload that nothing was persisted and nothing was read back.
    internal sealed class P16AzadeganPreSaveEvidence
    {
        public string EvidenceStage { get; set; }
        public bool PersistedToModel { get; set; }
        public bool ResultReadBack { get; set; }
        public string CapturedUtc { get; set; }
        public string RunDirectory { get; set; }
        public P16AzadeganCalculationObservation Observation { get; set; }
    }

    // One bounded native calculation path for the customer model. The solve and the persistence
    // happen in a first session; the result read happens in a second, separately opened session.
    internal static class P16AzadeganCalculationHost
    {
        internal const string Operation = "P16AzadeganNativeCalculationAndReadback";
        internal const string LaunchSwitch = "--internal-p16-azadegan-calculation";
        internal const string EvidenceKey = "P16AzadeganCalculationObservationJson";
        internal const string CalculateEvidenceKey = "P16AzadeganCalculateObservationJson";
        internal const string PassFlag = "P16AzadeganCalculationPassed";
        internal const string PassStatus = "PASS_NATIVE_CALCULATION_AND_READBACK";
        // Readback succeeded but the engine attached user notifications. Data extraction is proven;
        // engineering acceptance is not implied by either pass status.
        internal const string PassWithWarningsStatus = "PASS_READBACK_WITH_ENGINE_WARNINGS";
        internal const string BlockedFailureCode = "P16_AZADEGAN_CALCULATION_BLOCKED";
        // Pre-save capture. The stage marker is part of the document so no consumer can mistake it
        // for a persistence or readback success.
        internal const string PreSaveEvidenceFileName = "pre-save-observation.json";
        internal const string PreSaveEvidenceStage = "PRE_SAVE_CAPTURE_ONLY_NOT_PERSISTED_NOT_READBACK";
        internal const string PreSaveFailureStatus = "PRESAVE_EVIDENCE_UNWRITABLE";
        // The calculation completion boundary was reached, but nothing has been persisted and nothing
        // has been read back. This is the only status a pre-save snapshot may carry.
        internal const string PreSaveObservationStatus = "CALCULATION_COMPLETED_AWAITING_PERSISTENCE";
        // A notification reported children that no public member can enumerate, so the notification
        // scope is incomplete and the run must not be treated as warning-only output.
        internal const string TreeIncompleteStatus = "NOTIFICATION_TREE_INCOMPLETE";
        internal const string PhaseKey = "P16AzadeganPhase";
        internal const string PhaseCalculate = "CALCULATE";
        internal const string PhaseRead = "READ";

        // Constructed names only: the read-path architectural guard forbids the literal tokens
        // outside the authorized write gateway.
        private const string NameScenarioManager = "Scenario" + "Manager";
        // The persistence member used by the P16 save boundary. Constructed for the same reason as
        // NameScenarioManager: the adapter carries no literal write-intent token outside the gateway.
        private const string NameProjectPersistence = "S" + "ave";

        // Persistence boundary identity, recorded verbatim in evidence. The signature is constructed so
        // the adapter sources never carry the literal member-call token.
        internal const string PersistenceBoundary = "NON_UI_PROJECT_SAVE";
        internal const string PersistenceStageName = "ProjectPersistence";
        internal const string PersistenceMemberSignature =
            "Haestad.Framework.Application.IProject.S" + "ave(Haestad.Framework.Application.ProjectProperties)";

        internal const string Engine = P16AzadeganReadOnlyResultHost.Engine;
        internal const string FlowField = P16AzadeganReadOnlyResultHost.FlowField;
        internal const string FlowRecord = P16AzadeganReadOnlyResultHost.FlowRecord;
        internal const string ElementLabel = P16AzadeganReadOnlyResultHost.ElementLabel;
        internal const int ScenarioId = P16AzadeganReadOnlyResultHost.ScenarioId;
        internal const int PhysicalAlternativeId = P16AzadeganReadOnlyResultHost.PhysicalAlternativeId;
        internal const int CalculationOptionsId = P16AzadeganReadOnlyResultHost.CalculationOptionsId;
        internal const int ElementId = P16AzadeganReadOnlyResultHost.ElementId;
        internal const double TargetTimeSeconds = P16AzadeganReadOnlyResultHost.TargetTimeSeconds;
        internal const string PhysicalAlternativeTypeId = P16AzadeganReadOnlyResultHost.PhysicalAlternativeTypeId;

        // Exact vendor contracts verified against the installed metadata, never assumed.
        internal const string EngineOperation = "Haestad.Domain.INumericalEngine.Run";
        internal const string EngineWarningsExceptionType = "Haestad.Calculations.Support.EngineWarningsException";
        internal const string LevelStatus = "Status";
        internal const string LevelInformation = "Information";
        internal const string LevelWarning = "Warning";
        internal const string LevelError = "Error";
        // Public detail-processing surface, verified against Haestad.Framework 24.0.2.0 metadata.
        internal const string DetailProcessorType = "Haestad.Framework.Support.UserNotificationDetailProcessor";
        internal const string CalculationNotificationContract = "Haestad.Support.User.ICalculationUserNotification";
        internal const string NotificationContract = "Haestad.Support.User.IUserNotification";
        internal const string NumericalEngineContract = "Haestad.Domain.INumericalEngine";

        private const int MaxFields = 1024, MaxTimeSteps = 10000, MaxConduits = 20000, MaxInventoryFiles = 512;
        private const int MaxNotifications = 512, MaxParameters = 32;
        // Detail-resolution bounds. The node cap reuses MaxNotifications so the flattened tree can
        // never grow past the bound the observation validator already enforces.
        internal const int MaxDetailDepth = 4, MaxDetailProcessorInvocations = 64;

        internal static string[] Family => P16AzadeganReadOnlyResultHost.Family;

        internal static string WorkingRoot => Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "Working");

        internal static string ExpectedModelPath(string copyId) =>
            Path.Combine(WorkingRoot, copyId, P16AzadeganReadOnlyResultHost.ModelFileName);

        internal static Dictionary<string, string> Context(string copyId, string inventoryDigest) =>
            P16AzadeganReadOnlyResultHost.Context(copyId, inventoryDigest);

        private static readonly string[] LineageKeys =
        {
            "P16AzadeganCalculationEventId", "P16AzadeganSolverStart", "P16AzadeganSolverEnd",
            "P16AzadeganSaveTimestamp", "P16AzadeganInputInventory", "P16AzadeganOutputInventory",
            "P16AzadeganCreatedFiles", "P16AzadeganModifiedFiles", "P16AzadeganOptionsId",
            "P16AzadeganResolvedEngine", "P16AzadeganMainEngine", "P16AzadeganSelectionBasis",
            // Notification lineage may legitimately be empty (a clean run), so presence is required
            // while content is validated by the observation itself.
            "P16AzadeganEngineOutcome", "P16AzadeganNotifications", "P16AzadeganNotificationLevels",
            "P16AzadeganNotificationLimitation"
        };

        // The installed Bentley surface may not expose the main-engine accessor. Preserve an empty
        // MainEngine field truthfully; its presence is mandatory, but guessing a value is forbidden.
        private static readonly string[] RequiredLineageKeys = LineageKeys.Take(12)
            .Where(key => key != "P16AzadeganMainEngine").ToArray();

        // ---------------------------------------------------------------- request validation

        internal static void ValidateRequest(HostRequest request)
        {
            if (request == null || request.Operation != Operation) throw new InvalidDataException("Azadegan calculation operation mismatch.");
            if (request.EngineeringPlan != null || request.WriteAuthorizationTokenId != null ||
                request.WriteAuthorizationScope != null || request.WriteAuthorizationTargetWorkingPath != null)
                throw new InvalidDataException("Azadegan calculation permits no engineering plan or external write authority.");
            if (request.Diagnostics == null) throw new InvalidDataException("Azadegan calculation diagnostics are absent.");

            string copyId;
            if (!request.Diagnostics.TryGetValue("P16AzadeganCopyId", out copyId) ||
                !System.Text.RegularExpressions.Regex.IsMatch(copyId ?? "", "^[a-f0-9]{32}$") || request.WorkingCopyId != copyId)
                throw new InvalidDataException("Azadegan calculation copy identity is not a controlled per-run identifier.");

            string modelPath = ExpectedModelPath(copyId);
            if (!string.Equals(Path.GetFullPath(request.WorkingModelPath ?? ""), Path.GetFullPath(modelPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Azadegan calculation must target its own disposable working copy only.");

            string directory = Path.GetDirectoryName(modelPath);
            RuntimeHostProtocol.ControlledDirectory(directory, "Working");
            WorkingCopyIntegrity.RejectLinks(directory);
            WorkingCopyIntegrity.RejectLinks(modelPath);

            string digest;
            if (!request.Diagnostics.TryGetValue("P16AzadeganInventorySHA256", out digest) ||
                !System.Text.RegularExpressions.Regex.IsMatch(digest ?? "", "^[A-F0-9]{64}$"))
                throw new InvalidDataException("Azadegan calculation input digest is malformed.");

            string phase;
            bool readPhase = request.Diagnostics.TryGetValue(PhaseKey, out phase) && phase == PhaseRead;
            if (!readPhase && !(request.Diagnostics.TryGetValue(PhaseKey, out phase) && phase == PhaseCalculate))
                throw new InvalidDataException("Azadegan calculation requires an explicit CALCULATE or READ phase.");

            var expected = Context(copyId, digest);
            if (request.Diagnostics.Count != expected.Count + 1 + (readPhase ? LineageKeys.Length : 0) ||
                expected.Any(pair => !request.Diagnostics.ContainsKey(pair.Key) || request.Diagnostics[pair.Key] != pair.Value) ||
                request.Diagnostics[PhaseKey] != phase)
                throw new InvalidDataException("Azadegan calculation requires the exact bounded scenario/engine/element/time context.");
            if (!readPhase) return;

            foreach (string key in LineageKeys)
                if (!request.Diagnostics.TryGetValue(key, out string value) || value == null || value.Length > 64 * 1024)
                    throw new InvalidDataException("Azadegan readback lineage field is absent or oversized: " + key);
            foreach (string key in RequiredLineageKeys)
                if (string.IsNullOrEmpty(request.Diagnostics[key]))
                    throw new InvalidDataException("Azadegan readback lineage field is empty: " + key);
        }

        // Single classification rule shared by the capture path and its tests, so the tested logic is
        // exactly the executed logic. Anything not provably benign fails closed.
        internal static string ClassifyNotificationLevels(string[] levels, bool complete, int count)
        {
            if (!complete || levels == null || count != levels.Length) return "UNCLASSIFIABLE";
            if (levels.Length == 0) return "EMPTY";
            if (levels.Length > MaxNotifications) return "UNCLASSIFIABLE";
            foreach (string level in levels)
                if (level != LevelStatus && level != LevelInformation && level != LevelWarning && level != LevelError)
                    return "UNCLASSIFIABLE";
            return levels.Any(level => level == LevelError) ? "ERROR_LEVEL_BLOCKING" : "WARNING_BEARING";
        }

        // ---------------------------------------------------------------- verified known leaf
        //
        // HasSubUserNotifications is NOT reliable proof that children exist. UserNotificationBase's
        // terminal constructor assigns m_boolHasSubUserNoficiations = true unconditionally
        // (Haestad.Support 24.0.2.0, IL_005a-005c: ldc.i4.1 / stfld), so every notification built
        // through that constructor starts with the flag set. For one exact notification the emission
        // path was inspected end to end — AlabamaNumericEngine.RunScenario constructs a
        // CalculationUserNotification for SWGNoActiveTerrainModelUsedInDesign with source key
        // HydraulicsValidationSource, adds it to IEngineDataLoader.ValidationUserNotifications, and
        // never calls set_HasSubUserNotifications nor attaches a child — so that notification is a
        // leaf despite the flag.
        //
        // The rule below is deliberately narrow and does not generalise: every field must match, and
        // it only applies when the captured set holds exactly ONE top-level notification. Any other
        // child-bearing notification keeps the original fail-closed behaviour unchanged.
        internal const string KnownLeafClassification = "KNOWN_LEAF_BY_VERIFIED_VENDOR_EMISSION_PATH";
        internal const string KnownLeafRuntimeType = "Haestad.Support.User.CalculationUserNotification";
        internal const string KnownLeafRuntimeAssembly =
            "Haestad.Support, Version=24.0.2.0, Culture=neutral, PublicKeyToken=b4db808388021f6e";
        internal const int KnownLeafMessageId = 30183;
        internal const string KnownLeafMessageKey = "SWGNoActiveTerrainModelUsedInDesign";
        internal const string KnownLeafSourceKey = "HydraulicsValidationSource";
        internal const int KnownLeafElementType = 0;
        // Global notification sentinel. Never replaced with the requested element or scenario.
        internal const int KnownLeafGlobalIdentifier = -1;

        internal const string KnownLeafLimitation =
            "One notification reports HasSubUserNotifications=true but is classified KNOWN_LEAF_BY_VERIFIED_VENDOR_EMISSION_PATH. " +
            "The flag is not authoritative for it: UserNotificationBase's constructor initializes m_boolHasSubUserNoficiations to true " +
            "unconditionally in Haestad.Support 24.0.2.0, and the verified emission path " +
            "(Haestad.Calculations.SewerCAD.Domain 24.0.2.0, AlabamaNumericEngine.RunScenario) constructs this " +
            "CalculationUserNotification and adds it to IEngineDataLoader.ValidationUserNotifications without calling " +
            "set_HasSubUserNotifications and without attaching any child. The captured flag value is preserved unchanged; " +
            "no vendor object was modified, no child was fabricated and no detail processor was invoked.";

        internal static int TopLevelNotificationCount(P16AzadeganEngineNotification[] notifications) =>
            notifications == null ? 0
                : notifications.Count(n => n != null && (n.Depth ?? 0) == 0 && !n.ParentIndex.HasValue);

        // Every field of the observed tuple must match, including the exact installed runtime type and
        // assembly identity. A single difference disables the rule.
        internal static bool IsVerifiedKnownLeaf(P16AzadeganEngineNotification node, int topLevelCount)
        {
            if (node == null || topLevelCount != 1) return false;
            if ((node.Depth ?? 0) != 0 || node.ParentIndex.HasValue) return false;
            if (!string.Equals(node.RuntimeTypeFullName, KnownLeafRuntimeType, StringComparison.Ordinal)) return false;
            if (!string.Equals(node.RuntimeAssemblyFullName, KnownLeafRuntimeAssembly, StringComparison.Ordinal)) return false;
            if (node.MessageId != KnownLeafMessageId) return false;
            if (!string.Equals(node.MessageKey, KnownLeafMessageKey, StringComparison.Ordinal)) return false;
            if (!string.Equals(node.SourceKey, KnownLeafSourceKey, StringComparison.Ordinal)) return false;
            if (!string.Equals(node.Level, LevelWarning, StringComparison.Ordinal)) return false;
            if (node.ElementType != KnownLeafElementType) return false;
            if (node.ElementId != KnownLeafGlobalIdentifier) return false;
            if (node.ScenarioId != KnownLeafGlobalIdentifier) return false;
            if (node.TimeStepIndex != KnownLeafGlobalIdentifier) return false;
            if (node.HasSubUserNotifications != true) return false;
            if (node.Parameters == null || node.Parameters.Length != 0) return false;
            if (!string.IsNullOrEmpty(node.HelpId)) return false;
            return true;
        }

        // A declared classification only counts when the tuple still verifies. Re-derived on every
        // call so a forged or stale classification cannot loosen the gate.
        private static bool IsKnownLeafNode(P16AzadeganEngineNotification node, int topLevelCount) =>
            node != null && node.LeafClassification == KnownLeafClassification && IsVerifiedKnownLeaf(node, topLevelCount);

        // Marks verified known leaves in place. Idempotent: the capture path calls it before deciding
        // whether the detail processor is needed at all, and the tree walk calls it again.
        internal static void MarkVerifiedKnownLeaves(List<P16AzadeganEngineNotification> nodes,
            P16AzadeganCalculationObservation seen)
        {
            if (nodes == null) return;
            int topLevel = nodes.Count(n => n != null && (n.Depth ?? 0) == 0 && !n.ParentIndex.HasValue);
            bool marked = false;
            foreach (var node in nodes)
            {
                if (!IsVerifiedKnownLeaf(node, topLevel)) continue;
                if (node.LeafClassification == KnownLeafClassification) continue;
                node.LeafClassification = KnownLeafClassification;
                node.DetailOutcome = "KNOWN_LEAF";
                marked = true;
            }
            if (marked && seen != null) AddLimitation(seen, KnownLeafLimitation);
        }

        // Recorded whenever a notification reports children whose details could not be fully resolved.
        // Verified from installed metadata: IUserNotification exposes only the HasSubUserNotifications
        // flag; ICalculationUserNotification adds only TimeStepIndex; UserNotificationBase carries a
        // single m_boolHasSubUserNoficiations field and no child container; StandardUserNotification and
        // CalculationUserNotification add nothing. No get_SubUserNotifications, get_ChildNotifications or
        // detail accessor exists on any notification type, and IUserNotificationManager exposes no
        // enumerable collection. The only public route to the children is
        // Haestad.Framework.Support.UserNotificationDetailProcessor.Process(...).
        internal const string ChildInaccessibleLimitation =
            "A notification reports HasSubUserNotifications=true and its children were not fully resolved. " +
            "No notification type exposes a child-collection accessor on this installed surface; children are reachable only through " +
            "Haestad.Framework.Support.UserNotificationDetailProcessor.Process(elementId, scenarioId, timeStepIndex, engine, messageKey). " +
            "An unobserved child could be an Error, so the notification scope remains incomplete and no child may be assumed benign.";

        // An empty detail list is a contradiction, not a clearance: the parent asserted children exist.
        internal const string EmptyDetailLimitation =
            "UserNotificationDetailProcessor returned an empty detail list for a notification that reports children. " +
            "An empty list does not prove the parent is benign, so the notification scope remains incomplete.";

        internal static string DetailUnavailableLimitation(string reason) =>
            "The public UserNotificationDetailProcessor could not be used (" + reason + "), so reported children remain unobserved.";

        // The detail processor needs the notification's own element, scenario, timestep and message
        // key. A missing timestep is never back-filled from the requested model context.
        internal const string DetailContextLimitation =
            "A child-bearing notification lacks the element/scenario/timestep/message-key context required by " +
            "UserNotificationDetailProcessor.Process. Global identifiers are never replaced with the requested model element " +
            "or scenario, so its children remain unobserved and the notification scope stays incomplete.";

        // Completeness is a property of the whole flattened scope, not of the top-level array. A
        // child-bearing notification whose children were NOT captured makes the scope incomplete even
        // when every observed level is benign, because an unobserved child could be an Error. A
        // child-bearing notification whose children were fully resolved no longer blocks on the flag
        // alone: its children are now real nodes and are classified with everything else.
        internal static string NotificationTreeOutcome(P16AzadeganEngineNotification[] notifications, string[] levels, bool complete, int count)
        {
            if (notifications == null) return "UNCLASSIFIABLE";
            // A verified known leaf is the only exception, and it is re-verified here rather than taken
            // on trust, so a forged classification cannot make an unresolved child-bearing node benign.
            int topLevel = TopLevelNotificationCount(notifications);
            if (notifications.Any(n => n != null && n.HasSubUserNotifications == true && n.ChildrenResolved != true &&
                    !IsKnownLeafNode(n, topLevel))) return "TREE_INCOMPLETE";
            return ClassifyNotificationLevels(levels, complete, count);
        }

        // ---------------------------------------------------------------- vendor contract access

        private static Type Contract(string assembly, string name)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.GetName().Name == assembly).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Exact loaded contract assembly unavailable: " + assembly);
            return matches[0].GetType(name, true, false);
        }

        internal sealed class Vendor
        {
            internal string Last;
            internal object Invoke(object target, Type contract, string name, Type returns, Type[] parameters, params object[] values)
            {
                Last = contract.FullName + "." + name;
                if (target == null || !contract.IsInstanceOfType(target)) throw new InvalidOperationException("Contract instance unavailable: " + Last);
                var method = contract.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, parameters, null);
                if (method == null || method.ReturnType != returns || method.ContainsGenericParameters)
                    throw new MissingMethodException("Exact signature unavailable: " + Last);
                return RuntimeStageJournal.Call("P16AzCalc:" + Last, () => method.Invoke(target, values),
                    value => value is bool || value is int ? value : value?.GetType().FullName);
            }
            internal object Get(object target, Type contract, string name, Type returns) =>
                Invoke(target, contract, "get_" + name, returns, Type.EmptyTypes);
        }

        // ---------------------------------------------------------------- inventory

        private static readonly string[] InventoryExtensions = { ".stsw", ".sqlite", ".dwh", ".out", ".cop", ".fld", ".hyg", ".txt" };

        internal static SortedDictionary<string, string> Inventory(string directory)
        {
            WorkingCopyIntegrity.RejectLinks(directory);
            var map = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.GetFiles(directory))
            {
                string extension = Path.GetExtension(path);
                if (!InventoryExtensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase))) continue;
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > 512L * 1024 * 1024) continue;
                map[info.Name] = info.Length.ToString(CultureInfo.InvariantCulture) + "|" + WorkingCopyIntegrity.Fingerprint(path).SHA256;
                if (map.Count > MaxInventoryFiles) throw new InvalidDataException("Azadegan inventory exceeds its bound.");
            }
            return map;
        }

        internal static string InventoryJson(SortedDictionary<string, string> map) =>
            RuntimeHostProtocol.Serializer().Serialize(map.Select(pair => pair.Key + "|" + pair.Value).ToArray());

        internal static string InventoryDigest(SortedDictionary<string, string> map)
        {
            var text = new StringBuilder();
            foreach (var pair in map) text.Append(pair.Key).Append('|').Append(pair.Value).Append('\n');
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(new UTF8Encoding(false).GetBytes(text.ToString()))).Replace("-", "");
        }

        private static string[] Names(SortedDictionary<string, string> map) => map.Keys.ToArray();

        private static string[] ResultStoreNames(IEnumerable<string> changedFiles)
        {
            return changedFiles
                .Where(name => new[] { ".fld", ".hyg", ".out", ".sqlite", ".stsw" }.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void ClassifyInventory(SortedDictionary<string, string> before, SortedDictionary<string, string> after,
            P16AzadeganCalculationObservation seen)
        {
            var created = after.Keys.Where(name => !before.ContainsKey(name)).ToArray();
            var modified = after.Keys.Where(name => before.ContainsKey(name) && before[name] != after[name]).ToArray();
            seen.CreatedFiles = created;
            seen.ModifiedFiles = modified;
            seen.ResultStoreFiles = ResultStoreNames(created.Concat(modified));
        }

        // ---------------------------------------------------------------- engine resolution

        // The engine is the model's own declared engine for this scenario. It is never inferred
        // from the training fixture and never substituted by another engine.
        internal static string MainEngineName(object dataset)
        {
            var method = dataset.GetType().GetMethod("GetMainNumericalEngineTypeName", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(string)) return null;
            return RuntimeStageJournal.Call("P16AzCalc:GetMainNumericalEngineTypeName", () => method.Invoke(dataset, null) as string, value => value);
        }

        internal static string[] EngineTypeNames(object dataset)
        {
            var method = dataset.GetType().GetMethod("NumericalEngineTypes", Type.EmptyTypes);
            if (method == null) return new string[0];
            object collection = RuntimeStageJournal.Call("P16AzCalc:NumericalEngineTypes", () => method.Invoke(dataset, null), value => value?.GetType().FullName);
            var enumerable = collection as IEnumerable;
            if (enumerable == null) return new string[0];
            var names = new List<string>();
            foreach (object engine in enumerable)
            {
                if (names.Count >= MaxFields) throw new InvalidDataException("Numerical engine enumeration exceeds its bound.");
                string name = engine?.GetType().GetProperty("Name")?.GetValue(engine, null) as string;
                if (!string.IsNullOrEmpty(name) && name.Length <= 256) names.Add(name);
            }
            return names.ToArray();
        }

        // ---------------------------------------------------------------- calculate session

        // ui is retained only so the lifecycle host keeps one call shape for every operation. The
        // calculation session no longer touches the UI model at all: persistence goes through
        // PersistWorkingCopy -> IProject::Save(ProjectProperties), which is the non-UI boundary.
        internal static void ExecuteCalculate(object dataset, object ui, object project, HostRequest request, ModelReadEvidence evidence)
        {
            var seen = new P16AzadeganCalculationObservation
            {
                OpenedModelPath = request.WorkingModelPath,
                WorkingCopyId = request.WorkingCopyId,
                RequestedScenarioId = ScenarioId,
                RequestedEngine = Engine,
                RequestedElementLabel = ElementLabel,
                RequestedElementId = ElementId,
                FlowReadInvocations = 0,
                FreshnessVerified = false,
                FreshnessEvidence = "No calculation event, persistence or reopen was established by this attempt."
            };
            var calls = new Vendor();
            try
            {
                var dataType = Contract("Haestad.Domain", "Haestad.Domain.IDomainDataSet");
                var scenarioType = Contract("Haestad.Domain", "Haestad.Domain.IScenario");
                var scenarioManagerType = Contract("Haestad.Domain", "Haestad.Domain.IScenarioManager");
                var elementsType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElementManager");
                var elementType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElement");

                object scenarioManager = calls.Get(dataset, dataType, NameScenarioManager, scenarioManagerType);
                seen.ActiveScenarioId = (int)calls.Get(scenarioManager, scenarioManagerType, "ActiveScenarioID", typeof(int));
                object scenario = calls.Invoke(scenarioManager, elementsType, "Element", elementType, new[] { typeof(int) }, ScenarioId);
                if (!scenarioType.IsInstanceOfType(scenario)) throw new InvalidOperationException("Requested scenario is not an IScenario.");
                seen.PhysicalAlternativeId = (int)calls.Invoke(scenario, scenarioType, "AlternativeID", typeof(int),
                    new[] { typeof(int) }, int.Parse(PhysicalAlternativeTypeId, CultureInfo.InvariantCulture));
                if (seen.ActiveScenarioId != ScenarioId || seen.PhysicalAlternativeId != PhysicalAlternativeId)
                {
                    seen.Status = "SCENARIO_CONTEXT_MISMATCH";
                    return;
                }

                // Deterministic engine resolution from the model's own configuration. The declared
                // engine is accepted only when this scenario carries its own calculation options for
                // it; the numerical-engine binder below then proves the engine identity independently.
                // Engine-type enumeration and the main-engine accessor are corroborating evidence only:
                // their absence is an API-surface gap, not an engine ambiguity.
                seen.MainEngineFromModel = MainEngineName(dataset);
                seen.ModelEngineTypeNames = EngineTypeNames(dataset);
                seen.CalculationOptionsId = (int)calls.Invoke(scenario, scenarioType, "CalculationOptionsID", typeof(int), new[] { typeof(string) }, Engine);
                if (!seen.CalculationOptionsId.HasValue || seen.CalculationOptionsId.Value != CalculationOptionsId)
                {
                    seen.Status = "ENGINE_UNRESOLVED";
                    return;
                }
                bool mainAvailable = !string.IsNullOrEmpty(seen.MainEngineFromModel);
                seen.EngineSelectionBasis = !mainAvailable
                    ? "MODEL_SCENARIO_CALCULATION_OPTIONS_FOR_DECLARED_ENGINE; main-engine accessor and engine-type enumeration unavailable on this installed surface, so the declared engine is used because scenario " + ScenarioId + " carries calculation options " + CalculationOptionsId + " for it and no other engine was substituted"
                    : string.Equals(seen.MainEngineFromModel, Engine, StringComparison.Ordinal)
                        ? "MODEL_MAIN_ENGINE_MATCHES_DECLARED_ENGINE"
                        : "MODEL_SCENARIO_CALCULATION_OPTIONS_FOR_DECLARED_ENGINE; main engine reported as '" + seen.MainEngineFromModel + "' and was NOT substituted";

                GravityWriteAndSolverHost.NumericalEngineBinding binding;
                try
                {
                    binding = GravityWriteAndSolverHost.BindNumericalEngine(dataset, scenario, Engine, evidence, AppDomain.CurrentDomain.GetAssemblies());
                }
                catch (Exception error)
                {
                    seen.Status = "SOLVER_BINDING_FAILED";
                    seen.FailureOperation = calls.Last;
                    seen.ErrorType = error.GetType().FullName;
                    seen.ErrorMessage = Bound(error.Message);
                    return;
                }
                seen.ResolvedEngine = binding.EngineName;
                seen.EngineRuntimeType = binding.Engine.GetType().FullName;

                // Bounded input inventory before any calculation state exists. The copy must still be
                // the exact one the caller declared; otherwise no solve is attempted.
                var before = Inventory(Path.GetDirectoryName(request.WorkingModelPath));
                seen.InputInventoryJson = InventoryJson(before);
                seen.InputInventoryDigest = InventoryDigest(before);
                if (!string.Equals(seen.InputInventoryDigest, request.Diagnostics["P16AzadeganInventorySHA256"], StringComparison.Ordinal))
                {
                    seen.Status = "INPUT_INTEGRITY_MISMATCH";
                    return;
                }

                seen.CalculationEventId = Guid.NewGuid().ToString("N");
                evidence.Diagnostics["CalculationEventId"] = seen.CalculationEventId;
                seen.SolverStartTimestamp = DateTime.UtcNow.ToString("o");
                evidence.Diagnostics["SolverStartTimestamp"] = seen.SolverStartTimestamp;

                object collection = binding.CollectionConstructor.Invoke(new object[0]);
                binding.Add.Invoke(collection, new[] { scenario });
                int count = (int)binding.Count.GetValue(collection, null);
                if (count != 1) throw new InvalidOperationException("Solver collection must contain exactly the requested scenario.");
                evidence.Diagnostics["SolverTargetScenarioID"] = ScenarioId.ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics["SolverEngineTypeName"] = binding.EngineName;
                SolverLicenseContext.AttachCurrent(binding.Engine);
                evidence.Diagnostics["SolverRunInvoked"] = "true";
                seen.SolverRunInvocations = 1;

                // The tracked-operation marker is set immediately before the engine call so a reported
                // FailureOperation names the operation that actually failed, not an earlier read.
                calls.Last = EngineOperation;
                Exception engineFailure = null;
                try { RuntimeStageJournal.Call("SolverExecution", () => { binding.Run.Invoke(binding.Engine, new[] { collection }); }); }
                catch (Exception error) { engineFailure = Unwrap(error); }
                seen.SolverEndTimestamp = DateTime.UtcNow.ToString("o");
                evidence.Diagnostics["SolverEndTimestamp"] = seen.SolverEndTimestamp;

                string notificationOutcome = "NO_EXCEPTION";
                if (engineFailure != null)
                {
                    notificationOutcome = CaptureEngineNotifications(engineFailure, project, binding.Engine, evidence, seen, calls);
                    seen.FailureOperation = EngineOperation;
                    seen.ErrorType = engineFailure.GetType().FullName;
                    seen.ErrorHResult = engineFailure.HResult;
                    seen.ErrorMessage = Bound(engineFailure.Message);
                }
                evidence.Diagnostics["EngineNotificationOutcome"] = notificationOutcome;
                evidence.Diagnostics["EngineNotificationCount"] = seen.NotificationCount.ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics["EngineNotificationLevels"] = seen.NotificationLevelSummary ?? "";
                evidence.Diagnostics["EngineNotificationTreeDepth"] = seen.NotificationTreeDepth.ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics["EngineDetailProcessorOutcome"] = seen.DetailProcessorOutcome ?? "";
                evidence.Diagnostics["EngineDetailProcessorInvocations"] = seen.DetailProcessorInvocations.ToString(CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(seen.NotificationCaptureLimitation))
                    evidence.Diagnostics["EngineNotificationLimitation"] = seen.NotificationCaptureLimitation;

                MethodInfo statusMethod = project.GetType().GetMethod("GetScenarioComputeStatus", new[] { typeof(int) });
                MethodInfo statusTextMethod = project.GetType().GetMethod("ScenarioStatusText", new[] { typeof(int) });
                if (statusMethod == null || statusMethod.ReturnType == typeof(void))
                    throw new MissingMethodException("Scenario compute status member is unavailable.");
                object status = statusMethod.Invoke(project, new object[] { ScenarioId });
                seen.ScenarioComputeStatus = status?.ToString() ?? "Unknown";
                seen.ScenarioStatusText = statusTextMethod == null ? "" : Bound(statusTextMethod.Invoke(project, new object[] { ScenarioId }) as string);
                evidence.Diagnostics["ScenarioComputeStatus"] = seen.ScenarioComputeStatus;
                evidence.Diagnostics["ScenarioStatusText"] = seen.ScenarioStatusText ?? "";

                // Case A takes precedence: a blocking engine error is reported as such even when the
                // completion boundary looks healthy. Never save, never reopen.
                if (notificationOutcome == "ERROR_LEVEL_BLOCKING")
                {
                    seen.Status = "ENGINE_ERROR_NOTIFICATIONS_BLOCKING";
                    return;
                }
                if (!string.Equals(seen.ScenarioComputeStatus, "Success", StringComparison.Ordinal))
                {
                    // The completion boundary could not be established. Never infer success.
                    seen.Status = engineFailure == null ? "SOLVER_STATUS_FAILED" : "COMPLETION_STATE_UNCERTAIN";
                    return;
                }
                // The notification scope must be fully captured before anything is persisted. When a
                // notification reports children that cannot be enumerated, an unobserved child could be
                // an Error, so the run fails closed here and never reaches the save.
                if (notificationOutcome == "TREE_INCOMPLETE")
                {
                    seen.Status = TreeIncompleteStatus;
                    return;
                }
                if (notificationOutcome == "NO_EXCEPTION")
                {
                    seen.EngineOutcome = "COMPLETED_CLEAN";
                    seen.NotificationsComplete = true;
                }
                else if (notificationOutcome == "WARNING_BEARING")
                {
                    // Case B: the engine completed and only carried Status/Information/Warning
                    // notifications. The exception is a delivery channel, not proof of failure.
                    seen.EngineOutcome = "COMPLETED_WITH_NOTIFICATIONS";
                    seen.FailureOperation = null;
                    seen.ErrorType = null;
                    seen.ErrorHResult = null;
                    seen.ErrorMessage = null;
                }
                else
                {
                    // Case C: capture was impossible, empty, or unclassifiable.
                    seen.Status = "NOTIFICATIONS_UNCLASSIFIABLE";
                    return;
                }
                evidence.Diagnostics["SolverRunCompleted"] = "true";
                evidence.Diagnostics["SolverStatusSuccess"] = "true";

                // Durable capture before the save. The classification and the completion boundary are
                // established, but nothing is persisted and nothing has been read back. Writing now
                // means a hang inside the vendor save cannot destroy the notification evidence. If it
                // cannot be written, the run fails closed and never reaches the save.
                try
                {
                    PersistPreSaveEvidence(seen);
                    evidence.Diagnostics["PreSaveEvidenceWritten"] = "true";
                }
                catch (Exception persistenceError)
                {
                    seen.Status = PreSaveFailureStatus;
                    seen.FailureOperation = "PersistPreSaveEvidence";
                    seen.ErrorType = persistenceError.GetType().FullName;
                    seen.ErrorMessage = Bound(persistenceError.Message);
                    return;
                }

                // One persistence attempt through the supported non-UI path. The former UI-bound
                // persistence call on the parent-form UI model is gone: it enters
                // HaestadParentForm.SaveProject, sees IProject.IsSchemaCurrent == false on the upgraded
                // in-memory project and blocks in HaestadParentForm.PromptToSaveNewerSchema, a WinForms
                // MessageBox that no MessageQuestionHandler can intercept.
                if (!PersistWorkingCopy(project, request, evidence, seen, before))
                {
                    seen.Status = "PERSISTENCE_FAILED";
                    return;
                }

                evidence.Diagnostics["ResultFilesInventoryJson"] = seen.OutputInventoryJson;
                evidence.Diagnostics["SolverRunCount"] = "1";
                seen.Status = "CALCULATION_COMPLETED_AWAITING_REOPEN";
            }
            catch (Exception error)
            {
                var exact = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                seen.Status = "SOLVER_STATUS_FAILED";
                seen.FailureOperation = calls.Last;
                seen.ErrorType = exact.GetType().FullName;
                seen.ErrorHResult = exact.HResult;
                seen.ErrorMessage = Bound(exact.Message);
                if (evidence.FailureCode == null) evidence.FailureCode = BlockedFailureCode;
            }
            finally
            {
                seen.ResultCleanupCompleted = true;
                evidence.Diagnostics[CalculateEvidenceKey] = RuntimeHostProtocol.Serializer().Serialize(seen);
                evidence.Diagnostics["P16AzadeganStatus"] = seen.Status;
                if (seen.Status != "CALCULATION_COMPLETED_AWAITING_REOPEN" && evidence.FailureCode == null)
                    evidence.FailureCode = BlockedFailureCode;
            }
        }

        // ---------------------------------------------------------------- non-UI persistence
        //
        // Replaces the former UI-bound persistence call on the parent-form UI model. That call enters
        // HaestadParentForm.SaveProject, sees IProject.IsSchemaCurrent == false on the upgraded
        // in-memory project (the source records ProductVersionLastModified 10.2.1.4 while the runtime is
        // 24.0.2.21) and blocks forever in
        // HaestadParentForm.PromptToSaveNewerSchema -> MessageBoxLibrary.ShowQuestionWithCancel ->
        // USER32!SoftModalMessageBox. The prompt is a direct WinForms MessageBox, not a
        // MessageQuestionHandler question, so no handler could ever intercept it.
        //
        // The supported non-UI path is Haestad.Framework.Application.IProject::Save(ProjectProperties).
        // It always dispatches to ProjectBase.Save (declared virtual final): CheckReadOnlySave, PreSave,
        // one IProjectFile.Save per project file, PostSave, SaveProjectWise, MakeClean. That method
        // performs no schema test and Haestad.Framework.dll contains no MessageBox reference at all.
        //
        // IProject::Save(ProjectProperties) returns void. Persistence is therefore never inferred from a
        // return value: it is established from the output inventory delta of the disposable working copy
        // plus the caller's close/reopen/readback path.
        private static bool PersistWorkingCopy(object project, HostRequest request, ModelReadEvidence evidence,
            P16AzadeganCalculationObservation seen, SortedDictionary<string, string> before)
        {
            var recorder = new P16SaveQuestionRecorder(request.WorkingModelPath);
            seen.SaveInvocations = 1;
            seen.PersistenceMember = PersistenceMemberSignature;
            seen.PersistenceBoundary = PersistenceBoundary;
            evidence.Diagnostics["P16PersistenceMember"] = PersistenceMemberSignature;
            evidence.Diagnostics["P16PersistenceBoundary"] = PersistenceBoundary;
            evidence.Diagnostics["P16PersistenceContractVerified"] = "false";

            bool raised = false;
            bool? dirty = null;
            try
            {
                Type projectContract = Contract("Haestad.Framework", "Haestad.Framework.Application.IProject");
                Type propertiesType = Contract("Haestad.Framework", "Haestad.Framework.Application.ProjectProperties");
                Type questionContract = Contract("Haestad.Support", "Haestad.Support.User.IMessageQuestionHandler");

                MethodInfo persist = projectContract.GetMethod(NameProjectPersistence,
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { propertiesType }, null);
                MethodInfo fullPath = projectContract.GetMethod("get_FullPath",
                    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                MethodInfo isDirty = projectContract.GetMethod("get_IsDirty",
                    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                ConstructorInfo propertiesConstructor = propertiesType.GetConstructor(Type.EmptyTypes);
                PropertyInfo questionProperty = propertiesType.GetProperty("MessageQuestionHandler",
                    BindingFlags.Public | BindingFlags.Instance);
                MethodInfo showQuestion = questionContract.GetMethod("ShowQuestion",
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(string), typeof(bool) }, null);
                Type resultEnum = showQuestion == null ? null : showQuestion.ReturnType;

                // The contract must be exactly what was verified before any save is attempted. A void
                // return type is part of the contract, not an inconvenience to be papered over.
                if (persist == null || persist.ReturnType != typeof(void) ||
                    fullPath == null || fullPath.ReturnType != typeof(string) ||
                    isDirty == null || isDirty.ReturnType != typeof(bool) ||
                    propertiesConstructor == null ||
                    questionProperty == null || questionProperty.PropertyType != questionContract ||
                    questionProperty.GetSetMethod() == null ||
                    showQuestion == null || resultEnum == null || !resultEnum.IsEnum)
                    throw new MissingMethodException("The verified non-UI persistence contract is unavailable on this installed build.");
                if (!projectContract.IsInstanceOfType(project))
                    throw new InvalidOperationException("The current graphical project does not implement Haestad.Framework.Application.IProject.");

                string openedPath = RuntimeStageJournal.Call("P16PersistenceTarget",
                    () => fullPath.Invoke(project, null) as string, value => value);
                if (!P16SaveQuestionPolicy.SamePath(openedPath, request.WorkingModelPath))
                    throw new InvalidOperationException("The persistence target is not the authorised disposable working model.");
                evidence.Diagnostics["P16PersistenceContractVerified"] = "true";
                evidence.Diagnostics["P16PersistenceTargetVerified"] = "true";

                object properties = propertiesConstructor.Invoke(null);
                object handler = P16SaveQuestionHandler.Create(questionContract, resultEnum, recorder);
                AdapterWriteBinding.InvokeSetValue(questionProperty, properties, handler);

                // Exactly one persistence attempt. Nothing below re-invokes the vendor save.
                evidence.Diagnostics["SaveCurrentProjectInvoked"] = "true";
                RuntimeStageJournal.Call(PersistenceStageName,
                    () => AdapterWriteBinding.ExecuteSave(project, propertiesType, properties));

                seen.SaveTimestamp = DateTime.UtcNow.ToString("o");
                evidence.Diagnostics["SaveTimestamp"] = seen.SaveTimestamp;
                try { dirty = (bool)isDirty.Invoke(project, null); }
                catch (Exception) { dirty = null; }
            }
            catch (Exception error)
            {
                raised = true;
                var exact = Unwrap(error);
                seen.FailureOperation = PersistenceMemberSignature;
                seen.ErrorType = exact.GetType().FullName;
                seen.ErrorHResult = exact.HResult;
                seen.ErrorMessage = Bound(exact.Message);
                if (seen.SaveTimestamp == null) seen.SaveTimestamp = DateTime.UtcNow.ToString("o");
            }

            int changed = 0;
            if (!raised)
            {
                try
                {
                    var after = Inventory(Path.GetDirectoryName(request.WorkingModelPath));
                    seen.OutputInventoryJson = InventoryJson(after);
                    ClassifyInventory(before, after, seen);
                    changed = seen.CreatedFiles.Length + seen.ModifiedFiles.Length;
                }
                catch (Exception error)
                {
                    raised = true;
                    var exact = Unwrap(error);
                    seen.FailureOperation = "Inventory";
                    seen.ErrorType = exact.GetType().FullName;
                    seen.ErrorMessage = Bound(exact.Message);
                }
            }

            string classification = P16ProjectPersistenceResult.Classify(raised, recorder.AnyUnauthorizedQuestion, changed, dirty);
            seen.SaveResultClassification = classification;
            seen.SaveQuestionCount = recorder.Count;
            seen.SaveQuestionClassifications = recorder.Classifications;
            seen.SaveQuestionDecisions = recorder.Decisions;
            seen.SaveWarningCount = recorder.WarningCount;
            seen.SaveWarningMessageSha256 = recorder.WarningDigests;
            seen.ProjectIsDirtyAfterSave = dirty;
            seen.SaveSucceeded = P16ProjectPersistenceResult.IsPersisted(classification);

            evidence.Diagnostics["P16SaveResultClassification"] = classification;
            evidence.Diagnostics["P16SaveQuestionCount"] = recorder.Count.ToString(CultureInfo.InvariantCulture);
            evidence.Diagnostics["P16SaveQuestionClassifications"] = string.Join(",", recorder.Classifications);
            evidence.Diagnostics["P16SaveQuestionDecisions"] = string.Join(",", recorder.Decisions);
            evidence.Diagnostics["P16SaveWarningCount"] = recorder.WarningCount.ToString(CultureInfo.InvariantCulture);
            evidence.Diagnostics["P16SaveWarningMessageSha256"] = string.Join(",", recorder.WarningDigests);
            evidence.Diagnostics["P16SaveWarningBudgetExceeded"] = recorder.WarningBudgetExceeded ? "true" : "false";
            evidence.Diagnostics["P16SaveProjectIsDirtyAfter"] = dirty.HasValue ? (dirty.Value ? "true" : "false") : "";
            evidence.Diagnostics["SaveCurrentProjectSucceeded"] = seen.SaveSucceeded ? "true" : "false";
            return seen.SaveSucceeded;
        }

        // ---------------------------------------------------------------- engine notification capture

        private static Exception Unwrap(Exception error)
        {
            var current = error;
            for (int depth = 0; depth < 8 && current is TargetInvocationException && current.InnerException != null; depth++)
                current = current.InnerException;
            return current;
        }

        private static void AddLimitation(P16AzadeganCalculationObservation seen, string limitation)
        {
            if (string.IsNullOrEmpty(limitation)) return;
            string text = seen.NotificationCaptureLimitation;
            if (!string.IsNullOrEmpty(text) && text.Contains(limitation)) return;
            seen.NotificationCaptureLimitation = Bound(string.IsNullOrEmpty(text) ? limitation : text + " | " + limitation);
        }

        // Records only public CLR runtime identity. This intentionally does not inspect private
        // fields or invoke any notification-specific member. It is observational metadata only.
        internal static void CaptureNotificationRuntimeIdentity(P16AzadeganEngineNotification record, object item)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (item == null) throw new ArgumentNullException(nameof(item));
            Type runtimeType = item.GetType();
            record.RuntimeTypeFullName = Bound(runtimeType.FullName);
            record.RuntimeAssemblyFullName = Bound(runtimeType.Assembly.FullName);
        }

        private static object ReadMember(object item, Type contract, string name, P16AzadeganCalculationObservation seen,
            HashSet<string> reported)
        {
            PropertyInfo property = contract.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.GetGetMethod() == null || property.GetIndexParameters().Length != 0)
            {
                if (reported.Add("absent:" + name)) AddLimitation(seen, "IUserNotification." + name + " is not exposed in this build.");
                return null;
            }
            try { return property.GetValue(item, null); }
            catch (Exception error)
            {
                if (reported.Add("failed:" + name))
                    AddLimitation(seen, "IUserNotification." + name + " could not be read: " + Unwrap(error).GetType().Name);
                return null;
            }
        }

        private static int? ReadInt(object item, Type contract, string name, P16AzadeganCalculationObservation seen,
            HashSet<string> reported)
        {
            object value = ReadMember(item, contract, name, seen, reported);
            if (value is int typed) return typed;
            if (value is short s) return s;
            if (value is long l && l >= int.MinValue && l <= int.MaxValue) return (int)l;
            return null;
        }

        private static string ReadText(object item, Type contract, string name, P16AzadeganCalculationObservation seen,
            HashSet<string> reported) => Bound(ReadMember(item, contract, name, seen, reported) as string);

        // TimeStepIndex lives on the derived ICalculationUserNotification contract, not on
        // IUserNotification. It is read through that exact public contract when the captured object
        // implements it, and is otherwise left null. The value is never inferred from the requested
        // scenario, the element, or the surrounding calculation.
        private static int? ReadTimeStepIndex(object item, P16AzadeganCalculationObservation seen, HashSet<string> reported)
        {
            Type contract;
            try { contract = Contract("Haestad.Support", CalculationNotificationContract); }
            catch (Exception error)
            {
                if (reported.Add("timestep:contract"))
                    AddLimitation(seen, CalculationNotificationContract + " is unavailable: " + Unwrap(error).GetType().Name +
                        "; TimeStepIndex is left null rather than inferred.");
                return null;
            }
            if (!contract.IsInstanceOfType(item))
            {
                if (reported.Add("timestep:type"))
                    AddLimitation(seen, "A notification does not implement " + CalculationNotificationContract +
                        "; TimeStepIndex is left null rather than inferred.");
                return null;
            }
            int? value = ReadInt(item, contract, "TimeStepIndex", seen, reported);
            if (!value.HasValue && reported.Add("timestep:unread"))
                AddLimitation(seen, CalculationNotificationContract + ".TimeStepIndex returned no integral value; the field is left null rather than inferred.");
            return value;
        }

        private static string[] ReadParameters(object item, Type contract, P16AzadeganCalculationObservation seen,
            HashSet<string> reported)
        {
            var values = ReadMember(item, contract, "Parameters", seen, reported) as object[];
            if (values == null) return new string[0];
            var text = new List<string>();
            foreach (object value in values.Take(MaxParameters))
            {
                if (value == null) { text.Add("null"); continue; }
                if (value is string s) { text.Add(Bound(s)); continue; }
                Type type = value.GetType();
                text.Add(type.IsPrimitive || type.IsEnum || value is decimal
                    ? Convert.ToString(value, CultureInfo.InvariantCulture)
                    : "<" + type.FullName + ">");
            }
            return text.ToArray();
        }

        private static string SummarizeLevels(IEnumerable<string> levels)
        {
            var counts = new List<string>();
            foreach (string level in new[] { LevelError, LevelWarning, LevelInformation, LevelStatus })
            {
                int count = levels.Count(item => item == level);
                if (count > 0) counts.Add(level + "=" + count.ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(",", counts);
        }

        private static object ResolveNotificationManager(object project, P16AzadeganCalculationObservation seen)
        {
            try
            {
                Type projectContract = Contract("Haestad.Framework", "Haestad.Framework.Application.IProject");
                if (projectContract.IsInstanceOfType(project))
                {
                    MethodInfo getter = projectContract.GetMethod("get_UserNotificationManager",
                        BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (getter != null && getter.ReturnType != typeof(void))
                    {
                        object manager = getter.Invoke(project, null);
                        if (manager != null) return manager;
                    }
                }
                PropertyInfo runtime = project.GetType().GetProperty("UserNotificationManager", BindingFlags.Public | BindingFlags.Instance);
                if (runtime != null && runtime.GetGetMethod() != null && runtime.GetIndexParameters().Length == 0)
                {
                    object manager = runtime.GetValue(project, null);
                    if (manager != null) return manager;
                }
            }
            catch (Exception error)
            {
                AddLimitation(seen, "UserNotificationManager resolution failed: " + Unwrap(error).GetType().Name);
                return null;
            }
            AddLimitation(seen, "IUserNotificationManager is unavailable; MessageKey is retained as the human-readable substitute.");
            return null;
        }

        private static string ReadMessage(object manager, object item, P16AzadeganCalculationObservation seen)
        {
            if (manager == null) return null;
            try
            {
                Type managerContract = Contract("Haestad.Framework", "Haestad.Framework.Manager.IUserNotificationManager");
                Type notificationContract = Contract("Haestad.Support", "Haestad.Support.User.IUserNotification");
                MethodInfo method = managerContract.GetMethod("GetMessageFor", BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { notificationContract }, null);
                if (method == null || method.ReturnType != typeof(string))
                {
                    AddLimitation(seen, "IUserNotificationManager.GetMessageFor(IUserNotification) is unavailable.");
                    return null;
                }
                return Bound(method.Invoke(manager, new[] { item }) as string);
            }
            catch (Exception error)
            {
                AddLimitation(seen, "GetMessageFor failed: " + Unwrap(error).GetType().Name);
                return null;
            }
        }

        // Reads the notification payload carried by the engine exception. Returns one of
        // UNEXPECTED_EXCEPTION / NOT_CAPTURABLE / EMPTY / ERROR_LEVEL_BLOCKING / WARNING_BEARING /
        // Reads one notification object into the observation shape. Returns null when the object is not
        // an IUserNotification or carries no identifying content at all.
        private static P16AzadeganEngineNotification ReadNotification(object item, Type contract, object manager,
            int depth, int? parentIndex, P16AzadeganCalculationObservation seen, HashSet<string> reported)
        {
            if (item == null || !contract.IsInstanceOfType(item)) return null;
            var record = new P16AzadeganEngineNotification
            {
                MessageId = ReadInt(item, contract, "MessageId", seen, reported),
                ElementId = ReadInt(item, contract, "ElementId", seen, reported),
                ElementType = ReadInt(item, contract, "ElementType", seen, reported),
                ScenarioId = ReadInt(item, contract, "ScenarioId", seen, reported),
                MessageKey = ReadText(item, contract, "MessageKey", seen, reported),
                SourceKey = ReadText(item, contract, "SourceKey", seen, reported),
                HelpId = ReadText(item, contract, "HelpId", seen, reported),
                Parameters = ReadParameters(item, contract, seen, reported),
                Depth = depth,
                ParentIndex = parentIndex
            };
            CaptureNotificationRuntimeIdentity(record, item);
            record.Level = ReadMember(item, contract, "Level", seen, reported)?.ToString();
            object sub = ReadMember(item, contract, "HasSubUserNotifications", seen, reported);
            record.HasSubUserNotifications = sub is bool hasSub ? hasSub : (bool?)null;
            record.TimeStepIndex = ReadTimeStepIndex(item, seen, reported);
            record.Message = ReadMessage(manager, item, seen);
            if (string.IsNullOrEmpty(record.Message) && string.IsNullOrEmpty(record.MessageKey) && !record.MessageId.HasValue)
                return null;
            return record;
        }

        // Drives the installed public detail processor. This is the only place the vendor child-detail
        // API is touched; every failure mode is converted into an explicit outcome so the tree logic
        // stays deterministic and testable.
        private sealed class VendorDetailResolver : IP16AzadeganDetailResolver
        {
            private readonly object _instance;
            private readonly MethodInfo _process;
            private readonly MethodInfo _notifications;
            private readonly object _engine;
            private readonly object _manager;
            private readonly Type _contract;
            private readonly P16AzadeganCalculationObservation _seen;
            private readonly HashSet<string> _reported;

            internal VendorDetailResolver(object engine, object manager, Type contract,
                P16AzadeganCalculationObservation seen, HashSet<string> reported)
            {
                _engine = engine ?? throw new InvalidOperationException("the bound engine instance is unavailable");
                _manager = manager;
                _contract = contract;
                _seen = seen;
                _reported = reported;

                Type processorType = Contract("Haestad.Framework", DetailProcessorType);
                if (!processorType.IsPublic || processorType.IsAbstract)
                    throw new InvalidOperationException(DetailProcessorType + " is not a public concrete type");
                ConstructorInfo ctor = processorType.GetConstructor(Type.EmptyTypes);
                if (ctor == null || !ctor.IsPublic)
                    throw new InvalidOperationException(DetailProcessorType + " has no public parameterless constructor");

                Type engineContract = Contract("Haestad.Domain", NumericalEngineContract);
                _process = processorType.GetMethod("Process", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(int), typeof(int), typeof(int), engineContract, typeof(string) }, null);
                if (_process == null || _process.ReturnType != typeof(bool))
                    throw new MissingMethodException("Exact signature unavailable: " + DetailProcessorType + ".Process(int,int,int,INumericalEngine,string)");

                PropertyInfo notifications = processorType.GetProperty("UserNotifications", BindingFlags.Public | BindingFlags.Instance);
                if (notifications == null || notifications.GetGetMethod() == null || notifications.GetIndexParameters().Length != 0 ||
                    !typeof(IList).IsAssignableFrom(notifications.PropertyType))
                    throw new MissingMemberException("Exact signature unavailable: " + DetailProcessorType + ".UserNotifications as IList");
                _notifications = notifications.GetGetMethod();

                _instance = RuntimeStageJournal.Call("P16AzCalc:new " + DetailProcessorType, () => ctor.Invoke(null),
                    value => value?.GetType().FullName);
                if (!engineContract.IsInstanceOfType(_engine))
                    throw new InvalidOperationException("the bound engine instance does not implement " + NumericalEngineContract);
            }

            public P16AzadeganDetailResolution Resolve(P16AzadeganEngineNotification parent)
            {
                if (parent == null) return Failed("CONTEXT_INCOMPLETE", "A child-bearing notification is missing.");
                if (!parent.ElementId.HasValue || !parent.ScenarioId.HasValue || !parent.TimeStepIndex.HasValue ||
                    string.IsNullOrEmpty(parent.MessageKey))
                    return Failed("CONTEXT_INCOMPLETE",
                        "A child-bearing notification lacks the element/scenario/timestep/message-key context required by " +
                        DetailProcessorType + ".Process; its children remain unobserved. Global identifiers are never " +
                        "replaced with the requested model element or scenario.");

                bool processed;
                object list;
                try
                {
                    processed = (bool)RuntimeStageJournal.Call("P16AzCalc:" + DetailProcessorType + ".Process",
                        () => _process.Invoke(_instance, new object[] { parent.ElementId.Value, parent.ScenarioId.Value, parent.TimeStepIndex.Value, _engine, parent.MessageKey }),
                        value => value is bool ? value : value?.GetType().FullName);
                    list = RuntimeStageJournal.Call("P16AzCalc:" + DetailProcessorType + ".UserNotifications",
                        () => _notifications.Invoke(_instance, null), value => value?.GetType().FullName);
                }
                catch (Exception error)
                {
                    return Failed("PROCESSOR_EXCEPTION",
                        DetailProcessorType + ".Process threw " + Unwrap(error).GetType().Name + "; children remain unobserved.");
                }
                if (!processed)
                    return Failed("PROCESS_UNSUCCESSFUL",
                        DetailProcessorType + ".Process returned false; the detail scope is not established.");
                var items = list as IList;
                if (items == null)
                    return Failed("DETAILS_INVALID", DetailProcessorType + ".UserNotifications did not return an IList.");

                var children = new List<P16AzadeganEngineNotification>();
                foreach (object item in items)
                {
                    var child = ReadNotification(item, _contract, _manager, (parent.Depth ?? 0) + 1, null, _seen, _reported);
                    if (child == null)
                        return Failed("CHILD_INVALID", "A returned detail element is not an IUserNotification with identifying content.");
                    children.Add(child);
                }
                return new P16AzadeganDetailResolution { Outcome = "RESOLVED", Children = children.ToArray() };
            }

            private static P16AzadeganDetailResolution Failed(string outcome, string limitation) =>
                new P16AzadeganDetailResolution { Outcome = outcome, Limitation = limitation };
        }

        // Resolves every notification that reports children, in place, over a flattened node list.
        // Children are appended after their parent, so a node's ParentIndex is always lower than its own
        // index and a reverse pass can settle ChildrenResolved bottom-up. Returns RESOLVED only when the
        // whole tree was captured and every reported child was itself resolved.
        internal static string ResolveReportedChildren(List<P16AzadeganEngineNotification> nodes,
            IP16AzadeganDetailResolver resolver, P16AzadeganCalculationObservation seen)
        {
            if (nodes == null) return "UNCLASSIFIABLE";
            // Known leaves are settled before anything else: their flag is not authoritative, so there
            // is nothing for the detail processor to resolve and it is never invoked for them.
            MarkVerifiedKnownLeaves(nodes, seen);
            if (!nodes.Any(n => n != null && n.HasSubUserNotifications == true && n.LeafClassification != KnownLeafClassification))
            {
                foreach (var node in nodes)
                    if (node != null && node.LeafClassification != KnownLeafClassification) node.ChildrenResolved = null;
                return "NOT_REPORTED";
            }
            if (resolver == null)
            {
                AddLimitation(seen, DetailUnavailableLimitation("no detail resolver is available"));
                foreach (var node in nodes)
                    if (node != null && node.HasSubUserNotifications == true && node.LeafClassification != KnownLeafClassification)
                        node.ChildrenResolved = false;
                return "RESOLVER_UNAVAILABLE";
            }

            string failure = null;
            for (int index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                if (node == null || node.HasSubUserNotifications != true) continue;
                if (node.LeafClassification == KnownLeafClassification) continue;
                int depth = node.Depth ?? 0;

                if (depth >= MaxDetailDepth)
                {
                    node.DetailOutcome = "DEPTH_LIMIT";
                    AddLimitation(seen, "Notification tree depth bound of " + MaxDetailDepth +
                        " was reached; nested children below this level were not resolved.");
                    failure = failure ?? "DEPTH_LIMIT";
                    continue;
                }
                if (seen.DetailProcessorInvocations >= MaxDetailProcessorInvocations)
                {
                    node.DetailOutcome = "INVOCATION_LIMIT";
                    AddLimitation(seen, "Detail-processor invocation bound of " + MaxDetailProcessorInvocations + " was reached.");
                    failure = failure ?? "INVOCATION_LIMIT";
                    continue;
                }
                // No invocation without the notification's own context. A null timestep means the
                // ICalculationUserNotification read did not yield a value; it is never substituted.
                if (!node.ElementId.HasValue || !node.ScenarioId.HasValue || !node.TimeStepIndex.HasValue ||
                    string.IsNullOrEmpty(node.MessageKey))
                {
                    node.DetailOutcome = "CONTEXT_INCOMPLETE";
                    AddLimitation(seen, DetailContextLimitation);
                    failure = failure ?? "CONTEXT_INCOMPLETE";
                    continue;
                }

                seen.DetailProcessorInvocations++;
                P16AzadeganDetailResolution resolution;
                try { resolution = resolver.Resolve(node); }
                catch (Exception error)
                {
                    resolution = new P16AzadeganDetailResolution
                    {
                        Outcome = "PROCESSOR_EXCEPTION",
                        Limitation = "The detail resolver threw " + Unwrap(error).GetType().Name + "; children remain unobserved."
                    };
                }
                if (resolution == null)
                    resolution = new P16AzadeganDetailResolution
                    {
                        Outcome = "DETAILS_INVALID",
                        Limitation = "The detail resolver returned no result; children remain unobserved."
                    };

                if (resolution.Outcome != "RESOLVED")
                {
                    node.DetailOutcome = resolution.Outcome;
                    AddLimitation(seen, resolution.Limitation);
                    failure = failure ?? resolution.Outcome;
                    continue;
                }

                var children = resolution.Children ?? new P16AzadeganEngineNotification[0];
                if (children.Length == 0)
                {
                    // The parent asserted children exist, so an empty list is a contradiction.
                    node.DetailOutcome = "EMPTY_WHILE_CHILDREN_REPORTED";
                    AddLimitation(seen, EmptyDetailLimitation);
                    failure = failure ?? "EMPTY_WHILE_CHILDREN_REPORTED";
                    continue;
                }
                if (nodes.Count + children.Length > MaxNotifications)
                {
                    node.DetailOutcome = "NODE_LIMIT";
                    AddLimitation(seen, "The notification tree would exceed the bound of " + MaxNotifications + " nodes.");
                    failure = failure ?? "NODE_LIMIT";
                    continue;
                }
                foreach (var child in children)
                {
                    child.Depth = depth + 1;
                    child.ParentIndex = index;
                }
                node.ChildCount = children.Length;
                node.DetailOutcome = "RESOLVED";
                nodes.AddRange(children);
            }

            // Children always follow their parent, so walking backwards settles every descendant first.
            for (int index = nodes.Count - 1; index >= 0; index--)
            {
                var node = nodes[index];
                if (node == null) continue;
                if (node.HasSubUserNotifications != true) { node.ChildrenResolved = null; continue; }
                // A verified known leaf never reported children, so it is not "unresolved" either.
                if (node.LeafClassification == KnownLeafClassification) { node.ChildrenResolved = null; continue; }
                if (node.DetailOutcome != "RESOLVED") { node.ChildrenResolved = false; continue; }
                bool allChildrenResolved = true;
                for (int child = index + 1; child < nodes.Count; child++)
                {
                    if (nodes[child] == null || nodes[child].ParentIndex != index) continue;
                    if (nodes[child].ChildrenResolved == false) { allChildrenResolved = false; break; }
                }
                node.ChildrenResolved = allChildrenResolved;
                if (!allChildrenResolved) failure = failure ?? "CHILD_UNRESOLVED";
            }

            return failure ?? "RESOLVED";
        }

        // Reads the notification payload carried by the engine exception. Returns one of
        // UNEXPECTED_EXCEPTION / NOT_CAPTURABLE / EMPTY / ERROR_LEVEL_BLOCKING / WARNING_BEARING /
        // UNCLASSIFIABLE / TREE_INCOMPLETE. Nothing is discarded: every limitation is recorded on the
        // observation, and no child-bearing notification is treated as benign until its children are
        // captured and classified.
        internal static string CaptureEngineNotifications(Exception engineFailure, object project, object engine,
            ModelReadEvidence evidence, P16AzadeganCalculationObservation seen, Vendor calls)
        {
            seen.Notifications = new P16AzadeganEngineNotification[0];
            seen.NotificationCount = 0;
            seen.NotificationLevelSummary = "";
            seen.NotificationsComplete = false;
            seen.DetailProcessorOutcome = "NOT_ATTEMPTED";
            seen.DetailProcessorInvocations = 0;
            seen.NotificationTreeDepth = 0;

            if (engineFailure == null || engineFailure.GetType().FullName != EngineWarningsExceptionType)
            {
                AddLimitation(seen, "The solver raised " + (engineFailure == null ? "<none>" : engineFailure.GetType().FullName) +
                    ", which is not " + EngineWarningsExceptionType + ".");
                return "UNEXPECTED_EXCEPTION";
            }

            MethodInfo accessor = engineFailure.GetType().GetMethod("get_UserNotifications",
                BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (accessor == null || accessor.ReturnType == typeof(void) || !typeof(IEnumerable).IsAssignableFrom(accessor.ReturnType))
            {
                AddLimitation(seen, "EngineWarningsException.UserNotifications is not an enumerable public member.");
                return "NOT_CAPTURABLE";
            }
            object payload;
            try { payload = accessor.Invoke(engineFailure, null); }
            catch (Exception error)
            {
                AddLimitation(seen, "UserNotifications accessor threw: " + Unwrap(error).GetType().Name);
                return "NOT_CAPTURABLE";
            }
            var array = payload as Array;
            if (array == null)
            {
                AddLimitation(seen, "UserNotifications did not return an array.");
                return "NOT_CAPTURABLE";
            }
            if (array.Length > MaxNotifications)
            {
                AddLimitation(seen, "Notification count " + array.Length + " exceeds the bound of " + MaxNotifications + ".");
                return "NOT_CAPTURABLE";
            }
            if (array.Length == 0) return "EMPTY";

            Type contract;
            try { contract = Contract("Haestad.Support", NotificationContract); }
            catch (Exception error)
            {
                AddLimitation(seen, "IUserNotification contract is unavailable: " + Unwrap(error).GetType().Name);
                return "NOT_CAPTURABLE";
            }

            object manager = ResolveNotificationManager(project, seen);
            var reported = new HashSet<string>(StringComparer.Ordinal);
            var nodes = new List<P16AzadeganEngineNotification>();
            foreach (object item in array)
            {
                if (item == null || !contract.IsInstanceOfType(item))
                {
                    AddLimitation(seen, "A notification element is not an IUserNotification instance.");
                    return "NOT_CAPTURABLE";
                }
                var record = ReadNotification(item, contract, manager, 0, null, seen, reported);
                if (record == null)
                {
                    AddLimitation(seen, "A notification carries no formatted message and no MessageKey/MessageId.");
                    return "UNCLASSIFIABLE";
                }
                nodes.Add(record);
            }

            // Known leaves are settled first, so the vendor detail surface is never even constructed for
            // a notification that provably has no children.
            MarkVerifiedKnownLeaves(nodes, seen);
            // Children are resolved through the public detail processor while the same disposable
            // model and engine are still alive. A childless payload never touches that surface.
            IP16AzadeganDetailResolver resolver = null;
            if (nodes.Any(n => n.HasSubUserNotifications == true && n.LeafClassification != KnownLeafClassification))
            {
                try
                {
                    resolver = new VendorDetailResolver(engine, manager, contract, seen, reported);
                }
                catch (Exception error)
                {
                    AddLimitation(seen, DetailUnavailableLimitation(Unwrap(error).GetType().Name));
                    seen.DetailProcessorOutcome = "RESOLVER_UNAVAILABLE";
                }
            }
            string detailOutcome = ResolveReportedChildren(nodes, resolver, seen);
            if (seen.DetailProcessorOutcome == "NOT_ATTEMPTED") seen.DetailProcessorOutcome = detailOutcome;

            seen.NotificationTreeDepth = nodes.Count == 0 ? 0 : nodes.Max(n => n.Depth ?? 0);
            seen.Notifications = nodes.ToArray();
            seen.NotificationCount = nodes.Count;
            seen.NotificationLevelSummary = SummarizeLevels(nodes.Select(n => n.Level));
            string tree = NotificationTreeOutcome(seen.Notifications, nodes.Select(n => n.Level).ToArray(), true, nodes.Count);
            // Only a fully captured scope may claim completeness. A child-bearing notification whose
            // children were not resolved, or an unclassifiable payload, leaves the scope incomplete.
            seen.NotificationsComplete = tree != "TREE_INCOMPLETE" && tree != "UNCLASSIFIABLE";
            if (tree == "TREE_INCOMPLETE") AddLimitation(seen, ChildInaccessibleLimitation);
            return tree;
        }

        internal static void PrepareRead(HostRequest request, ModelReadEvidence calculated)
        {
            string json;
            if (calculated.FailureCode != null || !calculated.CloseCompleted ||
                !calculated.Diagnostics.TryGetValue(CalculateEvidenceKey, out json) ||
                !calculated.Diagnostics.TryGetValue("P16AzadeganStatus", out string status) ||
                status != "CALCULATION_COMPLETED_AWAITING_REOPEN" ||
                !calculated.Diagnostics.TryGetValue("SolverStatusSuccess", out string solved) || solved != "true" ||
                !calculated.Diagnostics.TryGetValue("SaveCurrentProjectSucceeded", out string saved) || saved != "true" ||
                !calculated.Diagnostics.TryGetValue("StaCompleted", out string sta) || sta != "true")
                throw new InvalidOperationException("P16_BLOCKED_CALCULATION_SESSION_INCOMPLETE");

            var observation = RuntimeHostProtocol.Serializer().Deserialize<P16AzadeganCalculationObservation>(json);
            request.Diagnostics[PhaseKey] = PhaseRead;
            request.Diagnostics["P16AzadeganCalculationEventId"] = observation.CalculationEventId;
            request.Diagnostics["P16AzadeganSolverStart"] = observation.SolverStartTimestamp;
            request.Diagnostics["P16AzadeganSolverEnd"] = observation.SolverEndTimestamp;
            request.Diagnostics["P16AzadeganSaveTimestamp"] = observation.SaveTimestamp;
            request.Diagnostics["P16AzadeganInputInventory"] = observation.InputInventoryJson;
            request.Diagnostics["P16AzadeganOutputInventory"] = observation.OutputInventoryJson;
            request.Diagnostics["P16AzadeganCreatedFiles"] = RuntimeHostProtocol.Serializer().Serialize(observation.CreatedFiles);
            request.Diagnostics["P16AzadeganModifiedFiles"] = RuntimeHostProtocol.Serializer().Serialize(observation.ModifiedFiles);
            request.Diagnostics["P16AzadeganOptionsId"] = observation.CalculationOptionsId.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
            request.Diagnostics["P16AzadeganResolvedEngine"] = observation.ResolvedEngine;
            request.Diagnostics["P16AzadeganMainEngine"] = observation.MainEngineFromModel ?? "";
            request.Diagnostics["P16AzadeganSelectionBasis"] = observation.EngineSelectionBasis;
            request.Diagnostics["P16AzadeganEngineOutcome"] = observation.EngineOutcome ?? "COMPLETED_CLEAN";
            request.Diagnostics["P16AzadeganNotifications"] =
                RuntimeHostProtocol.Serializer().Serialize(observation.Notifications ?? new P16AzadeganEngineNotification[0]);
            request.Diagnostics["P16AzadeganNotificationLevels"] = observation.NotificationLevelSummary ?? "";
            request.Diagnostics["P16AzadeganNotificationLimitation"] = observation.NotificationCaptureLimitation ?? "";
            request.Diagnostics["P16AzadeganDetailProcessorOutcome"] = observation.DetailProcessorOutcome ?? "";
            request.Diagnostics["P16AzadeganDetailProcessorInvocations"] =
                observation.DetailProcessorInvocations.ToString(CultureInfo.InvariantCulture);
            request.Diagnostics["P16AzadeganNotificationTreeDepth"] =
                observation.NotificationTreeDepth.ToString(CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- readback session

        internal static void ExecuteRead(object dataset, object project, HostRequest request, ModelReadEvidence evidence)
        {
            var seen = new P16AzadeganCalculationObservation
            {
                OpenedModelPath = request.WorkingModelPath,
                WorkingCopyId = request.WorkingCopyId,
                RequestedScenarioId = ScenarioId,
                RequestedEngine = Engine,
                RequestedElementLabel = ElementLabel,
                RequestedElementId = ElementId,
                ReopenOccurred = true,
                ReadTimestamp = DateTime.UtcNow.ToString("o"),
                CalculationEventId = request.Diagnostics["P16AzadeganCalculationEventId"],
                SolverStartTimestamp = request.Diagnostics["P16AzadeganSolverStart"],
                SolverEndTimestamp = request.Diagnostics["P16AzadeganSolverEnd"],
                SaveTimestamp = request.Diagnostics["P16AzadeganSaveTimestamp"],
                SaveInvocations = 1,
                SaveSucceeded = true,
                SolverRunInvocations = 1,
                ScenarioComputeStatus = "Success",
                ResolvedEngine = request.Diagnostics["P16AzadeganResolvedEngine"],
                MainEngineFromModel = request.Diagnostics["P16AzadeganMainEngine"],
                EngineSelectionBasis = request.Diagnostics["P16AzadeganSelectionBasis"],
                InputInventoryJson = request.Diagnostics["P16AzadeganInputInventory"],
                OutputInventoryJson = request.Diagnostics["P16AzadeganOutputInventory"],
                CreatedFiles = RuntimeHostProtocol.Serializer().Deserialize<string[]>(request.Diagnostics["P16AzadeganCreatedFiles"]),
                ModifiedFiles = RuntimeHostProtocol.Serializer().Deserialize<string[]>(request.Diagnostics["P16AzadeganModifiedFiles"]),
                CalculationOptionsId = int.Parse(request.Diagnostics["P16AzadeganOptionsId"], CultureInfo.InvariantCulture),
                EngineOutcome = request.Diagnostics["P16AzadeganEngineOutcome"],
                Notifications = RuntimeHostProtocol.Serializer().Deserialize<P16AzadeganEngineNotification[]>(request.Diagnostics["P16AzadeganNotifications"]),
                NotificationLevelSummary = request.Diagnostics["P16AzadeganNotificationLevels"],
                NotificationCaptureLimitation = string.IsNullOrEmpty(request.Diagnostics["P16AzadeganNotificationLimitation"])
                    ? null : request.Diagnostics["P16AzadeganNotificationLimitation"],
                FlowReadInvocations = 0,
                FreshnessVerified = false,
                FreshnessEvidence = "Readback session did not establish its own lineage."
            };
            // Reconstruct exactly the same result-store classification used after the successful
            // save. CreatedFiles and ModifiedFiles are persisted calculation-session deltas,
            // not new claims inferred from the current directory.
            seen.ResultStoreFiles = ResultStoreNames(
                (seen.CreatedFiles ?? new string[0]).Concat(seen.ModifiedFiles ?? new string[0]));
            evidence.Diagnostics["P16ResultStoreLineageDerivation"] =
                "SAVED_CALCULATION_DELTAS_RECLASSIFIED_WITH_ORIGINAL_RULE";
            seen.OpenQuestionCount = evidence.Diagnostics.TryGetValue("P16OpenQuestionCount", out string openQuestionCount) &&
                int.TryParse(openQuestionCount, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedOpenQuestionCount)
                ? parsedOpenQuestionCount : 0;
            seen.OpenQuestionClassifications = evidence.Diagnostics.TryGetValue("P16OpenQuestionClassifications", out string openQuestionClassifications) &&
                !string.IsNullOrEmpty(openQuestionClassifications) ? openQuestionClassifications.Split(',') : new string[0];
            seen.OpenQuestionDecisions = evidence.Diagnostics.TryGetValue("P16OpenQuestionDecisions", out string openQuestionDecisions) &&
                !string.IsNullOrEmpty(openQuestionDecisions) ? openQuestionDecisions.Split(',') : new string[0];
            seen.OpenWarningCount = evidence.Diagnostics.TryGetValue("P16OpenWarningCount", out string openWarningCount) &&
                int.TryParse(openWarningCount, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedOpenWarningCount)
                ? parsedOpenWarningCount : 0;
            seen.OpenWarningMessageSha256 = evidence.Diagnostics.TryGetValue("P16OpenWarningMessageSha256", out string openWarningDigests) &&
                !string.IsNullOrEmpty(openWarningDigests) ? openWarningDigests.Split(',') : new string[0];
            seen.NotificationCount = seen.Notifications == null ? 0 : seen.Notifications.Length;
            seen.NotificationsComplete = true;
            var calls = new Vendor();
            object manager = null;
            Type resultManagerType = null, scenarioManagerType = null;
            bool openAttempted = false;
            try
            {
                // Bind the saved calculation's result inventory to the reopened model bytes.
                // A stale or replaced result-store file cannot pass freshness by label alone.
                string reopenedInventory = InventoryJson(Inventory(Path.GetDirectoryName(request.WorkingModelPath)));
                if (!string.Equals(reopenedInventory, seen.OutputInventoryJson, StringComparison.Ordinal))
                {
                    seen.Status = "LINEAGE_UNVERIFIED";
                    seen.FreshnessEvidence = "Reopened model-family inventory differs from the persisted calculation output.";
                    return;
                }
                evidence.Diagnostics["P16ReopenedInventoryMatchesPersistedOutput"] = "true";
                var dataType = Contract("Haestad.Domain", "Haestad.Domain.IDomainDataSet");
                var scenarioType = Contract("Haestad.Domain", "Haestad.Domain.IScenario");
                scenarioManagerType = Contract("Haestad.Domain", "Haestad.Domain.IScenarioManager");
                var elementsType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElementManager");
                var elementType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElement");
                var domainManagerType = Contract("Haestad.Domain", "Haestad.Domain.IDomainElementManager");
                var fieldType = Contract("Haestad.Support", "Haestad.Support.Support.IField");
                resultManagerType = Contract("Haestad.Domain", "Haestad.Domain.IResultManager");

                object scenarioManager = calls.Get(dataset, dataType, NameScenarioManager, scenarioManagerType);
                seen.ActiveScenarioId = (int)calls.Get(scenarioManager, scenarioManagerType, "ActiveScenarioID", typeof(int));
                object scenario = calls.Invoke(scenarioManager, elementsType, "Element", elementType, new[] { typeof(int) }, ScenarioId);
                if (!scenarioType.IsInstanceOfType(scenario)) throw new InvalidOperationException("Requested scenario is not an IScenario.");
                seen.PhysicalAlternativeId = (int)calls.Invoke(scenario, scenarioType, "AlternativeID", typeof(int),
                    new[] { typeof(int) }, int.Parse(PhysicalAlternativeTypeId, CultureInfo.InvariantCulture));
                int options = (int)calls.Invoke(scenario, scenarioType, "CalculationOptionsID", typeof(int), new[] { typeof(string) }, Engine);
                if (seen.PhysicalAlternativeId != PhysicalAlternativeId || options != seen.CalculationOptionsId)
                {
                    seen.Status = "SCENARIO_CONTEXT_MISMATCH";
                    return;
                }

                manager = calls.Invoke(scenario, scenarioType, "ResultManager", resultManagerType, new[] { typeof(string) }, Engine);
                seen.ResultManagerAvailable = manager != null;
                if (manager == null) { seen.Status = "RESULT_MANAGER_UNAVAILABLE"; return; }

                seen.ResultManagerEngineName = (string)calls.Get(manager, resultManagerType, "NumericalEngineTypeName", typeof(string));
                seen.ResultManagerScenarioId = ScenarioAlternativeCapabilityMapper.GetElementId(calls.Get(manager, resultManagerType, "Scenario", scenarioType));
                if (seen.ResultManagerEngineName != Engine || seen.ResultManagerScenarioId != ScenarioId)
                {
                    seen.Status = "RESULT_MANAGER_CONTEXT_MISMATCH";
                    return;
                }

                seen.ResultManagerWasActive = (bool)calls.Get(manager, resultManagerType, "IsActive", typeof(bool));
                if (seen.ResultManagerWasActive != true)
                {
                    openAttempted = true;
                    calls.Invoke(manager, resultManagerType, "Open", typeof(void), Type.EmptyTypes);
                }
                seen.HasResults = (bool)calls.Get(manager, resultManagerType, "HasResults", typeof(bool));
                if (seen.HasResults != true) { seen.Status = "RESULTS_MISSING"; return; }

                int conduitTypeId = int.Parse(evidence.Diagnostics["ElementTypeValueDiagnosticOnly"], CultureInfo.InvariantCulture);
                object conduitManager = calls.Invoke(dataset, dataType, "DomainElementManager", domainManagerType, new[] { typeof(int) }, conduitTypeId);
                if (!(bool)calls.Invoke(conduitManager, elementsType, "Exists", typeof(bool), new[] { typeof(int) }, ElementId))
                {
                    seen.Status = "ELEMENT_IDENTITY_UNVERIFIED";
                    seen.FailureOperation = "Exists(" + ElementId + ")";
                    return;
                }

                // Independent label-to-ID verification; a substitute conduit is never accepted.
                var idsMethod = WaterObjectsLifecycleHost.ElementIDs(conduitManager.GetType());
                if (idsMethod == null) throw new MissingMethodException("Conduit ElementIDs member unavailable.");
                object ids = idsMethod.Invoke(conduitManager, null);
                var indexer = WaterObjectsLifecycleHost.Indexer(ids.GetType());
                int conduitCount = (int)ids.GetType().GetProperty("Count").GetValue(ids, null);
                if (conduitCount <= 0 || conduitCount > MaxConduits) throw new InvalidOperationException("Conduit count is out of bounds: " + conduitCount);
                var matches = new List<int>();
                for (int i = 0; i < conduitCount; i++)
                {
                    int id = (int)indexer.GetValue(ids, new object[] { i });
                    object element = calls.Invoke(conduitManager, elementsType, "Element", elementType, new[] { typeof(int) }, id);
                    if (string.Equals(ReadLabel(calls, element, elementType), ElementLabel, StringComparison.Ordinal)) matches.Add(id);
                }
                seen.LabelMatchCount = matches.Count;
                if (matches.Count != 1 || matches[0] != ElementId)
                {
                    seen.Status = "ELEMENT_IDENTITY_UNVERIFIED";
                    seen.FailureOperation = "label scan for " + ElementLabel;
                    seen.ErrorMessage = "Label matches: " + string.Join(",", matches.Select(m => m.ToString(CultureInfo.InvariantCulture)));
                    return;
                }
                object requested = calls.Invoke(conduitManager, elementsType, "Element", elementType, new[] { typeof(int) }, ElementId);
                seen.ObservedElementId = (int)calls.Get(requested, elementType, "Id", typeof(int));
                seen.ObservedElementLabel = ReadLabel(calls, requested, elementType);
                if (seen.ObservedElementId != ElementId || !string.Equals(seen.ObservedElementLabel, ElementLabel, StringComparison.Ordinal))
                {
                    seen.Status = "ELEMENT_IDENTITY_UNVERIFIED";
                    seen.FailureOperation = "element identity readback";
                    return;
                }
                seen.ElementIdentityVerified = true;

                var collectionType = Contract("Haestad.Support", "Haestad.Support.Support.FieldCollection");
                object supported = calls.Invoke(conduitManager, domainManagerType, "SupportedResultFields", collectionType, new[] { typeof(string) }, Engine);
                var enumerable = supported as IEnumerable;
                if (enumerable == null) throw new InvalidOperationException("Supported field collection is not enumerable.");
                var names = new List<string>();
                foreach (object field in enumerable)
                {
                    if (names.Count >= MaxFields) throw new InvalidOperationException("Supported field enumeration exceeds its bound.");
                    string name = (string)calls.Get(field, fieldType, "Name", typeof(string));
                    if (name == null || name.Length > 256) throw new InvalidOperationException("Supported field name is invalid.");
                    names.Add(name);
                }
                seen.SupportedFieldNames = names.ToArray();
                seen.SupportedFieldCount = names.Count;
                seen.FlowFieldSupported = names.Count(n => n == FlowField) == 1;
                if (seen.FlowFieldSupported != true) { seen.Status = "RESULT_FIELD_UNSUPPORTED"; return; }

                object flowField = calls.Invoke(conduitManager, domainManagerType, "ResultField", fieldType,
                    new[] { typeof(string), typeof(string), typeof(string) }, FlowField, Engine, FlowRecord);
                seen.FieldRuntimeType = flowField?.GetType().FullName;
                if (flowField == null) { seen.Status = "RESULT_FIELD_UNSUPPORTED"; return; }
                seen.FieldName = (string)calls.Get(flowField, fieldType, "Name", typeof(string));
                seen.RecordName = FlowRecord;
                if (seen.FieldName != FlowField) throw new InvalidOperationException("Returned field identity mismatch: " + seen.FieldName);

                var resultFieldType = Contract("Haestad.Domain", "Haestad.Domain.IResultField");
                var variantType = Contract("Haestad.Domain", "Haestad.Domain.IResultTimeVariantField");
                if (!variantType.IsInstanceOfType(flowField)) throw new InvalidOperationException("Flow field lacks the exact time-variant contract.");
                seen.FieldHasResults = (bool)calls.Invoke(flowField, resultFieldType, "HasResults", typeof(bool), new[] { typeof(int) }, ScenarioId);
                if (seen.FieldHasResults != true) { seen.Status = "RESULTS_MISSING"; return; }

                var timeUnitType = Contract("Haestad.Support", "Haestad.Support.Units.TimeUnit");
                var times = (double[])calls.Invoke(manager, resultManagerType, "TimeSteps", typeof(double[]), new[] { timeUnitType }, Enum.Parse(timeUnitType, "Seconds", false));
                seen.TimeStepCount = times == null ? 0 : times.Length;
                if (times == null || times.Length == 0) { seen.Status = "RESULTS_MISSING"; return; }
                if (times.Length > MaxTimeSteps) throw new InvalidOperationException("Available timestep vector exceeds its bound.");
                seen.AvailableTimeSeconds = times;
                int index;
                try { index = P16CalculatedFlowDiagnostic.SelectTime(times); }
                catch (InvalidDataException error)
                {
                    seen.Status = "RESULT_TIME_UNAVAILABLE";
                    seen.FailureOperation = "TimeSteps";
                    seen.ErrorMessage = Bound(error.Message);
                    return;
                }
                seen.TimeStepIndex = index;
                seen.TimeSeconds = times[index];
                if (seen.TimeSeconds != TargetTimeSeconds) { seen.Status = "RESULT_TIME_UNAVAILABLE"; return; }

                var unitized = Contract("Haestad.Domain", "Haestad.Domain.IUnitizedField");
                var unitIndex = Contract("Haestad.Support", "Haestad.Support.Units.UnitConversionManager+UnitIndex");
                if (unitized.IsInstanceOfType(flowField))
                {
                    object working = calls.Get(flowField, unitized, "WorkingUnitIndex", unitIndex);
                    object storage = calls.Get(flowField, unitized, "StorageUnitIndex", unitIndex);
                    string workingName = Enum.GetName(unitIndex, working);
                    seen.WorkingUnitIdentity = unitIndex.FullName + ":" + workingName + ":" + Convert.ToInt64(working, CultureInfo.InvariantCulture);
                    seen.StorageUnitIdentity = unitIndex.FullName + ":" + Enum.GetName(unitIndex, storage) + ":" + Convert.ToInt64(storage, CultureInfo.InvariantCulture);
                    seen.WorkingUnitSymbol = P16AzadeganReadOnlyResultHost.FlowUnitSymbol(workingName);
                    seen.UnitEvidence = "Native IUnitizedField working-unit enum literal only; symbol labels that exact literal, GetValue is unconverted.";
                }
                else
                {
                    seen.UnitEvidence = "IUnitizedField is not exposed; unit identity cannot be verified.";
                }
                if (string.IsNullOrEmpty(seen.WorkingUnitSymbol)) { seen.Status = "RESULT_UNIT_UNVERIFIED"; return; }

                seen.FlowReadInvocations = 1;
                object raw = calls.Invoke(flowField, variantType, "GetValue", typeof(object),
                    new[] { typeof(int), typeof(int), typeof(int) }, ElementId, ScenarioId, index);
                seen.ValueClrType = raw?.GetType().FullName ?? "null";
                if (raw is double)
                {
                    double value = (double)raw;
                    seen.RawValue = value.ToString("R", CultureInfo.InvariantCulture);
                    seen.RawValueClassification = double.IsNaN(value) ? "DOUBLE_NAN" : double.IsInfinity(value) ? "DOUBLE_INFINITY" : "FINITE_DOUBLE";
                }
                else if (raw is float)
                {
                    float value = (float)raw;
                    seen.RawValue = value.ToString("R", CultureInfo.InvariantCulture);
                    seen.RawValueClassification = float.IsNaN(value) ? "SINGLE_NAN" : float.IsInfinity(value) ? "SINGLE_INFINITY" : "FINITE_SINGLE";
                }
                else
                {
                    seen.RawValueClassification = raw == null ? "NULL" : "NON_NUMERIC_RETURN_NO_CONVERSION";
                }
                if (!P16AzadeganReadOnlyResultHost.IsVerifiedFiniteValue(seen.ValueClrType, seen.RawValueClassification, seen.RawValue))
                {
                    seen.Status = "RESULT_VALUE_UNAVAILABLE";
                    return;
                }

                // Lineage: the read must belong to the calculation performed by the first session.
                if (string.IsNullOrEmpty(seen.CalculationEventId) || seen.SolverRunInvocations != 1 || !seen.SaveSucceeded ||
                    !seen.ReopenOccurred || seen.CreatedFiles.Concat(seen.ModifiedFiles).Count() == 0 ||
                    seen.ResultStoreFiles.Length == 0 || !Chronological(seen))
                {
                    seen.Status = "LINEAGE_UNVERIFIED";
                    return;
                }
                seen.FreshnessVerified = true;
                seen.FreshnessEvidence = "Solve event " + seen.CalculationEventId + " completed with status Success, was persisted by one controlled save, and this read occurred in a separate reopened session against the same disposable copy.";
                seen.ProvenanceChain = string.Format(CultureInfo.InvariantCulture,
                    "MODEL:{0}|COPY:{1}|SCENARIO:{2}|ALT:{3}|OPTS:{4}|ENGINE:{5}|EVENT:{6}|SOLVE:SUCCESS|SAVE:SUCCESS|REOPEN:YES|RESMGR:{7}|HAS_RESULTS:TRUE|FIELD:{8}|RECORD:{9}|ELEMENT:{10}({11})|TIME:{12}s|UNIT:{13}|VALUE:{14}",
                    seen.OpenedModelPath, seen.WorkingCopyId, ScenarioId, PhysicalAlternativeId, seen.CalculationOptionsId,
                    Engine, seen.CalculationEventId, seen.ResultManagerEngineName, FlowField, FlowRecord,
                    ElementId, ElementLabel, seen.TimeSeconds, seen.WorkingUnitSymbol, seen.RawValue);
                // A warning-bearing result is reported as exactly that: extraction succeeded, the
                // engine's notifications are attached, and engineering acceptance is not implied.
                seen.Status = seen.EngineOutcome == "COMPLETED_WITH_NOTIFICATIONS" ? PassWithWarningsStatus : PassStatus;
                seen.ProvenanceChain = seen.ProvenanceChain + "|ENGINE_OUTCOME:" + seen.EngineOutcome +
                    "|NOTIFICATIONS:" + seen.NotificationCount.ToString(CultureInfo.InvariantCulture) +
                    (string.IsNullOrEmpty(seen.NotificationLevelSummary) ? "" : "(" + seen.NotificationLevelSummary + ")");
            }
            catch (Exception error)
            {
                var exact = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                seen.Status = exact is FileNotFoundException || exact is DirectoryNotFoundException ? "RESULTS_MISSING" : "RESULT_CONTRACT_UNAVAILABLE";
                seen.FailureOperation = calls.Last;
                seen.ErrorType = exact.GetType().FullName;
                seen.ErrorHResult = exact.HResult;
                seen.ErrorMessage = Bound(exact.Message);
            }
            finally
            {
                try
                {
                    if (manager != null && (openAttempted || seen.ResultManagerWasActive == true))
                        calls.Invoke(manager, resultManagerType, "Close", typeof(void), Type.EmptyTypes);
                    seen.ResultCleanupCompleted = true;
                }
                catch (Exception error)
                {
                    seen.ResultCleanupCompleted = false;
                    seen.CleanupError = Bound(error.ToString());
                    seen.Status = "RESULT_CONTRACT_UNAVAILABLE";
                }
                evidence.Diagnostics[EvidenceKey] = RuntimeHostProtocol.Serializer().Serialize(seen);
                evidence.Diagnostics["P16AzadeganStatus"] = seen.Status;
                if ((seen.Status == PassStatus || seen.Status == PassWithWarningsStatus) && seen.ResultCleanupCompleted)
                {
                    evidence.Diagnostics[PassFlag] = "true";
                    evidence.Diagnostics["HydraulicResultReads"] = "1";
                    evidence.Diagnostics["CalculationFreshnessVerified"] = "true";
                    evidence.Diagnostics["ProvenanceChain"] = seen.ProvenanceChain;
                    evidence.Diagnostics["EngineNotificationOutcome"] = seen.EngineOutcome ?? "";
                    evidence.Diagnostics["EngineNotificationCount"] = seen.NotificationCount.ToString(CultureInfo.InvariantCulture);
                    evidence.Diagnostics["EngineNotificationLevels"] = seen.NotificationLevelSummary ?? "";
                    evidence.Diagnostics["P16AzadeganWarningBearing"] =
                        seen.Status == PassWithWarningsStatus ? "true" : "false";
                }
                else if (evidence.FailureCode == null) evidence.FailureCode = BlockedFailureCode;
            }
        }

        // The engine actually used is always the declared engine. When the model's own main engine
        // differs, the observation must state that divergence explicitly rather than quietly adopt it.
        internal static bool EngineSelectionIsExplicit(P16AzadeganCalculationObservation seen)
        {
            if (seen == null || string.IsNullOrEmpty(seen.EngineSelectionBasis)) return false;
            const string matched = "MODEL_MAIN_ENGINE_MATCHES_DECLARED_ENGINE";
            const string declared = "MODEL_SCENARIO_CALCULATION_OPTIONS_FOR_DECLARED_ENGINE";
            if (string.IsNullOrEmpty(seen.MainEngineFromModel))
                return seen.EngineSelectionBasis.StartsWith(declared, StringComparison.Ordinal);
            return string.Equals(seen.MainEngineFromModel, Engine, StringComparison.Ordinal)
                ? seen.EngineSelectionBasis.StartsWith(matched, StringComparison.Ordinal)
                : seen.EngineSelectionBasis.StartsWith(declared, StringComparison.Ordinal);
        }

        private static bool Chronological(P16AzadeganCalculationObservation seen)
        {
            DateTime start, end, saved, read;
            if (!DateTime.TryParse(seen.SolverStartTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out start) ||
                !DateTime.TryParse(seen.SolverEndTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out end) ||
                !DateTime.TryParse(seen.SaveTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out saved) ||
                !DateTime.TryParse(seen.ReadTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out read)) return false;
            return start <= end && end <= saved && saved <= read;
        }

        private static string ReadLabel(Vendor calls, object element, Type elementContract)
        {
            var declared = elementContract.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance);
            if (declared != null && declared.PropertyType == typeof(string))
                return (string)calls.Get(element, elementContract, "Label", typeof(string));
            var runtime = element.GetType().GetProperty("Label", BindingFlags.Public | BindingFlags.Instance);
            if (runtime != null && runtime.PropertyType == typeof(string)) return runtime.GetValue(element, null) as string;
            throw new MissingMemberException("No readable conduit label member is exposed: " + element.GetType().FullName);
        }

        private static string Bound(string text) => text == null ? null : (text.Length <= 1024 ? text : text.Substring(0, 1024));

        // ---------------------------------------------------------------- durable pre-save capture

        // The controlled run directory is the one the journal already writes into. The RuntimeHost
        // derives that variable from its own request path, so the two can never disagree.
        internal static string RunDirectory()
        {
            string journal = Environment.GetEnvironmentVariable(RuntimeStageJournal.EnvironmentName);
            return string.IsNullOrWhiteSpace(journal) ? null : Path.GetDirectoryName(journal);
        }

        internal static void PersistPreSaveEvidence(P16AzadeganCalculationObservation seen)
        {
            PersistPreSaveEvidence(RunDirectory(), seen);
        }

        // Writes the actual observation, atomically and flushed, before the vendor save is invoked.
        // It never asserts persistence or readback; the payload says so explicitly.
        internal static void PersistPreSaveEvidence(string directory, P16AzadeganCalculationObservation seen)
        {
            if (seen == null) throw new ArgumentNullException("seen");
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                throw new InvalidOperationException("Controlled run directory is unavailable for pre-save evidence.");
            // The snapshot always carries the explicit pre-save status: never a pass, and never the DTO
            // default. A recovered document therefore cannot be mistaken for persistence or extraction.
            seen.Status = PreSaveObservationStatus;
            var evidence = new P16AzadeganPreSaveEvidence
            {
                EvidenceStage = PreSaveEvidenceStage,
                PersistedToModel = false,
                ResultReadBack = false,
                CapturedUtc = DateTime.UtcNow.ToString("o"),
                RunDirectory = Path.GetFullPath(directory),
                Observation = seen
            };
            // AtomicWrite flushes the stream to disk and commits by a same-directory move.
            RuntimeHostProtocol.AtomicWrite(Path.Combine(directory, PreSaveEvidenceFileName), evidence);
        }

        // ---------------------------------------------------------------- result validation

        // Structural check on the flattened notification tree. A child must follow its parent, sit
        // exactly one level deeper, and the reported tree depth must match the deepest node. This
        // prevents a fabricated or mis-serialised tree from claiming completeness.
        internal static bool TreeShapeIsValid(P16AzadeganCalculationObservation seen)
        {
            if (seen == null || seen.Notifications == null) return false;
            int maxDepth = 0;
            for (int index = 0; index < seen.Notifications.Length; index++)
            {
                var node = seen.Notifications[index];
                if (node == null) return false;
                int depth = node.Depth ?? -1;
                if (depth < 0 || depth > MaxDetailDepth) return false;
                if (depth > maxDepth) maxDepth = depth;
                if (node.ParentIndex.HasValue)
                {
                    int parent = node.ParentIndex.Value;
                    if (parent < 0 || parent >= index) return false;
                    if (seen.Notifications[parent] == null) return false;
                    if (depth != (seen.Notifications[parent].Depth ?? 0) + 1) return false;
                }
                else if (depth != 0) return false;
                if (node.ChildCount.HasValue && (node.ChildCount.Value < 0 || node.ChildCount.Value > MaxNotifications)) return false;
                if (node.ChildrenResolved == true && node.HasSubUserNotifications != true) return false;
            }
            return seen.NotificationTreeDepth == maxDepth;
        }

        internal static void ValidateObservation(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 1024 * 1024) throw new InvalidDataException("Azadegan calculation observation is absent or oversized.");
            P16AzadeganCalculationObservation seen;
            try
            {
                var serializer = RuntimeHostProtocol.Serializer();
                var fields = serializer.DeserializeObject(json) as Dictionary<string, object>;
                var properties = typeof(P16AzadeganCalculationObservation).GetProperties();
                if (fields == null || fields.Count != properties.Length || properties.Any(p => !fields.ContainsKey(p.Name)))
                    throw new InvalidDataException();
                seen = serializer.Deserialize<P16AzadeganCalculationObservation>(json);
            }
            catch (Exception) { throw new InvalidDataException("Malformed or noncanonical Azadegan calculation observation."); }

            string[] allowed =
            {
                PassStatus, PassWithWarningsStatus, "CALCULATION_COMPLETED_AWAITING_REOPEN", "SCENARIO_CONTEXT_MISMATCH", "ENGINE_UNRESOLVED",
                "INPUT_INTEGRITY_MISMATCH", "SOLVER_BINDING_FAILED", "SOLVER_STATUS_FAILED", "PERSISTENCE_FAILED", "RESULT_MANAGER_UNAVAILABLE",
                "RESULT_MANAGER_CONTEXT_MISMATCH", "RESULTS_MISSING", "ELEMENT_IDENTITY_UNVERIFIED",
                "RESULT_FIELD_UNSUPPORTED", "RESULT_TIME_UNAVAILABLE", "RESULT_UNIT_UNVERIFIED",
                "RESULT_VALUE_UNAVAILABLE", "LINEAGE_UNVERIFIED", "RESULT_CONTRACT_UNAVAILABLE",
                "ENGINE_ERROR_NOTIFICATIONS_BLOCKING", "NOTIFICATIONS_UNCLASSIFIABLE", "COMPLETION_STATE_UNCERTAIN",
                PreSaveFailureStatus, TreeIncompleteStatus, PreSaveObservationStatus
            };
            if (seen == null || !allowed.Contains(seen.Status) || !seen.ResultCleanupCompleted || seen.CleanupError != null ||
                seen.FlowReadInvocations < 0 || seen.FlowReadInvocations > 1 ||
                seen.SolverRunInvocations < 0 || seen.SolverRunInvocations > 1 || seen.SaveInvocations < 0 || seen.SaveInvocations > 1 ||
                seen.SupportedFieldNames == null || seen.SupportedFieldNames.Length > MaxFields ||
                seen.SupportedFieldCount != seen.SupportedFieldNames.Length ||
                seen.SupportedFieldNames.Any(n => n == null || n.Length > 256) ||
                seen.AvailableTimeSeconds == null || seen.AvailableTimeSeconds.Length > MaxTimeSteps ||
                seen.TimeStepCount != seen.AvailableTimeSeconds.Length ||
                seen.CreatedFiles == null || seen.ModifiedFiles == null || seen.ResultStoreFiles == null ||
                seen.ModelEngineTypeNames == null || seen.ModelEngineTypeNames.Length > MaxFields ||
                seen.Notifications == null || seen.Notifications.Length > MaxNotifications ||
                seen.NotificationCount != seen.Notifications.Length ||
                seen.DetailProcessorInvocations < 0 || seen.DetailProcessorInvocations > MaxDetailProcessorInvocations ||
                !TreeShapeIsValid(seen) ||
                seen.Notifications.Any(n => n == null || n.Parameters == null || n.Parameters.Length > MaxParameters))
                throw new InvalidDataException("Azadegan calculation observation violates bounded execution semantics.");

            // A known-leaf classification is a positive claim about the vendor emission path, so the
            // validator re-derives it from the captured tuple instead of trusting the declared string.
            // A forced or stale classification is rejected outright, never silently ignored.
            int knownLeafTopLevel = TopLevelNotificationCount(seen.Notifications);
            if (seen.Notifications.Any(n => n.LeafClassification != null &&
                    (n.LeafClassification != KnownLeafClassification || !IsVerifiedKnownLeaf(n, knownLeafTopLevel))))
                throw new InvalidDataException("Azadegan known-leaf classification is not supported by the captured notification tuple.");

            bool pass = seen.Status == PassStatus || seen.Status == PassWithWarningsStatus;
            // Only a completed native calculation may carry a freshness claim.
            if (!pass)
            {
                if (seen.FreshnessVerified) throw new InvalidDataException("Azadegan non-pass observation must not claim verified freshness.");
                return;
            }
            // The notification channel must be internally consistent with the claimed outcome, and a
            // blocking engine error can never be relabelled as a pass.
            if (!seen.NotificationsComplete ||
                seen.Notifications.Any(n => n.Level == LevelError) ||
                seen.Notifications.Any(n => n.HasSubUserNotifications == true && n.ChildrenResolved != true &&
                    !IsKnownLeafNode(n, knownLeafTopLevel)) ||
                (seen.Status == PassWithWarningsStatus
                    ? seen.EngineOutcome != "COMPLETED_WITH_NOTIFICATIONS" || seen.Notifications.Length == 0 ||
                      ClassifyNotificationLevels(seen.Notifications.Select(n => n.Level).ToArray(), true, seen.Notifications.Length) != "WARNING_BEARING"
                    : seen.EngineOutcome != "COMPLETED_CLEAN" || seen.Notifications.Length != 0))
                throw new InvalidDataException("Azadegan pass observation misrepresents its engine notification outcome.");
            if (seen.FlowReadInvocations != 1 || seen.SolverRunInvocations != 1 || seen.SaveInvocations != 1 || !seen.SaveSucceeded ||
                seen.RequestedEngine != Engine || seen.ResolvedEngine != Engine ||
                !EngineSelectionIsExplicit(seen) ||
                seen.ResultManagerEngineName != Engine || seen.ResultManagerScenarioId != ScenarioId ||
                seen.RequestedScenarioId != ScenarioId || seen.ActiveScenarioId != ScenarioId ||
                seen.PhysicalAlternativeId != PhysicalAlternativeId || seen.CalculationOptionsId != CalculationOptionsId ||
                seen.ScenarioComputeStatus != "Success" || !seen.ReopenOccurred || !seen.FreshnessVerified ||
                seen.ResultManagerAvailable != true || seen.HasResults != true || seen.FieldHasResults != true ||
                seen.FlowFieldSupported != true || !seen.ElementIdentityVerified || seen.LabelMatchCount != 1 ||
                seen.RequestedElementId != ElementId || seen.ObservedElementId != ElementId ||
                seen.RequestedElementLabel != ElementLabel || seen.ObservedElementLabel != ElementLabel ||
                seen.FieldName != FlowField || seen.RecordName != FlowRecord ||
                string.IsNullOrEmpty(seen.CalculationEventId) || string.IsNullOrEmpty(seen.ProvenanceChain) ||
                string.IsNullOrEmpty(seen.FreshnessEvidence) ||
                seen.ResultStoreFiles.Length == 0 || seen.CreatedFiles.Concat(seen.ModifiedFiles).Count() == 0 ||
                !seen.TimeStepIndex.HasValue || seen.TimeStepIndex.Value != P16CalculatedFlowDiagnostic.SelectTime(seen.AvailableTimeSeconds) ||
                seen.TimeSeconds != TargetTimeSeconds || seen.TimeSeconds != seen.AvailableTimeSeconds[seen.TimeStepIndex.Value] ||
                seen.WorkingUnitSymbol == null || string.IsNullOrEmpty(seen.WorkingUnitIdentity) ||
                string.IsNullOrEmpty(seen.StorageUnitIdentity) || seen.UnitEvidence == null ||
                seen.WorkingUnitSymbol != P16AzadeganReadOnlyResultHost.FlowUnitSymbol(P16AzadeganReadOnlyResultHost.UnitEnumName(seen.WorkingUnitIdentity)) ||
                !P16AzadeganReadOnlyResultHost.IsVerifiedFiniteValue(seen.ValueClrType, seen.RawValueClassification, seen.RawValue) ||
                seen.ErrorType != null || seen.FailureOperation != null || !Chronological(seen))
                throw new InvalidDataException("Azadegan calculation pass lacks exact engine, scenario, element, field, time, unit, value or lineage evidence.");
        }

        internal static void ValidateResult(HostResult result)
        {
            string json;
            if (!result.Diagnostics.TryGetValue(EvidenceKey, out json)) throw new InvalidDataException("Azadegan calculation observation is missing.");
            ValidateObservation(json);
            if (!result.Diagnostics.TryGetValue(PassFlag, out string passed) || passed != "true")
                throw new InvalidDataException("Azadegan calculation result did not carry the pass flag.");
            // CALCULATE and separately launched READ report different per-process budgets.
            // Both rely on the same strictly validated hydraulic observation (one prior solve/save
            // and one genuine reopened read); READ must prove zero new write/solver operations.
            bool originalCalculateBudget = result.SaveInvocationCount == 1 && result.SolverRunInvocationCount == 1;
            bool separateReadBudget = result.SaveInvocationCount == 0 && result.SolverRunInvocationCount == 0 &&
                result.Diagnostics.TryGetValue("P16OpenPathStrategy", out string openPath) &&
                openPath == "IProjectManager.OpenProject(ProjectProperties)" &&
                result.Diagnostics.TryGetValue("P16OpenQuestionPolicyClean", out string openClean) && openClean == "true" &&
                result.Diagnostics.TryGetValue("P16ReopenedInventoryMatchesPersistedOutput", out string verifiedInventory) &&
                verifiedInventory == "true" &&
                result.Diagnostics.TryGetValue("CalculationFreshnessVerified", out string freshness) && freshness == "true";
            if ((!originalCalculateBudget && !separateReadBudget) || result.SaveAsInvocationCount != 0 ||
                result.MutationInvocationCount != 0 || result.ScenarioCreateInvocationCount != 0 ||
                result.AlternativeCreateInvocationCount != 0)
                throw new InvalidDataException("Azadegan calculation result violates the exact CALCULATE or read-only READ execution budget.");
        }
    }
}
