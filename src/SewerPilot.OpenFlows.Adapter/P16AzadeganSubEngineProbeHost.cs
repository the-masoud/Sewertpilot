using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SewerPilot.OpenFlows.Adapter
{
    // The supported result-store notification surface, recorded as verified signatures only.
    // None of these members is invoked by this operation.
    internal sealed class P16AzadeganNotificationSignature
    {
        public P16AzadeganNotificationSignature() { }
        public string DeclaringType { get; set; }
        public string Member { get; set; }
        public string Signature { get; set; }
        public string ArgumentSemantics { get; set; }
        public bool Invoked { get; set; }
    }

    // Read-only observation of the active sub-engine's result manager. It proves which engine
    // owns scenario 353's results and whether that engine reports results at all. It runs no
    // solver, saves nothing, creates no scenario or alternative, mutates no property, removes no
    // notification and invokes no notification accessor.
    internal sealed class P16AzadeganSubEngineProbeObservation
    {
        public P16AzadeganSubEngineProbeObservation() { }
        public string Status { get; set; } = "RESULT_CONTRACT_UNAVAILABLE";
        public int ScenarioId { get; set; }
        public int? ActiveScenarioId { get; set; }
        public int? PhysicalAlternativeId { get; set; }
        public int? CalculationOptionsId { get; set; }
        public string ProductEngineName { get; set; }
        public string ActiveEngineTypeNameForProduct { get; set; }
        public string ExpectedActiveEngineName { get; set; }
        public bool ActiveEngineMatchesExpected { get; set; }
        public bool ResultManagerAvailable { get; set; }
        public string ResultManagerRuntimeType { get; set; }
        public string ResultManagerEngineName { get; set; }
        public int? ResultManagerScenarioId { get; set; }
        public bool? ResultManagerWasActive { get; set; }
        public bool ResultManagerOpenedByProbe { get; set; }
        public bool? HasResults { get; set; }
        public bool ResultManagerClosed { get; set; }
        public string CloseError { get; set; }

        public bool NotificationProbeEnabled { get; set; }
        public string NotificationProbeDisabledReason { get; set; }
        public P16AzadeganNotificationSignature[] NotificationSignatures { get; set; } = new P16AzadeganNotificationSignature[0];
        public int NotificationApiInvocations { get; set; }
        public bool NotificationGateUnchanged { get; set; }
        public string NotificationGateEvidence { get; set; }

        public int ModelOpenInvocations { get; set; }
        public int SolverRunInvocations { get; set; }
        public int SaveInvocations { get; set; }
        public int ScenarioCreateInvocations { get; set; }
        public int AlternativeCreateInvocations { get; set; }
        public int MutationInvocations { get; set; }

        public string FailureOperation { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
    }

    internal static class P16AzadeganSubEngineProbeHost
    {
        internal const string Operation = "P16AzadeganSubEngineResultManagerProbe";
        internal const string LaunchSwitch = "--internal-p16-azadegan-subengine-probe";
        internal const string EvidenceKey = "P16AzadeganSubEngineProbeObservationJson";
        internal const string StatusKey = "P16AzadeganSubEngineProbeStatus";
        internal const string PassFlag = "P16AzadeganSubEngineProbePassed";
        internal const string PassStatus = "SUB_ENGINE_RESULT_MANAGER_OBSERVED";
        internal const string BlockedFailureCode = "P16_AZADEGAN_SUBENGINE_PROBE_BLOCKED";

        internal const string ProductEngine = "DelawareProductEngine";
        internal const string SubEngine = "GVFPressureEngine";
        internal const int ScenarioId = P16AzadeganReadOnlyResultHost.ScenarioId;
        internal const int PhysicalAlternativeId = P16AzadeganReadOnlyResultHost.PhysicalAlternativeId;
        internal const int CalculationOptionsId = P16AzadeganReadOnlyResultHost.CalculationOptionsId;

        internal const string ScenarioContextMismatch = "SCENARIO_CONTEXT_MISMATCH";
        internal const string ResultManagerUnavailable = "RESULT_MANAGER_UNAVAILABLE";
        internal const string ResultManagerContextMismatch = "RESULT_MANAGER_CONTEXT_MISMATCH";
        internal const string ActiveEngineMismatch = "ACTIVE_ENGINE_MISMATCH";
        internal const string ResultsMissing = "RESULTS_MISSING";

        internal const string GateEvidence =
            "HasSubUserNotifications=true still means the notification tree is incomplete and the " +
            "NOTIFICATION_TREE_INCOMPLETE gate is unchanged. No notification is drained, suppressed, " +
            "ignored or reinterpreted by this operation, and no notification accessor is invoked.";

        internal const string ArgumentSemanticsUnverified =
            "ARGUMENT_SEMANTICS_UNVERIFIED: the installed metadata declares " +
            "GetRunUserNotificationsSummary(int), GetTimeStepUserNotificationsSummary(int,int) and " +
            "GetUserNotifications(int,int,int) with no parameter names, so which integer means run index, " +
            "time-step index or result-connection index cannot be established from the installed surface.";

        internal const string ConnectionIdentityUnverified =
            "CONNECTION_IDENTITY_UNVERIFIED: no result-data-connection instance is established by this " +
            "probe, so the connection whose notifications would be read cannot be bound to scenario 353.";

        // Exact signatures read from installed metadata. Recorded, never invoked here.
        internal static P16AzadeganNotificationSignature[] NotificationSignatures()
        {
            return new[]
            {
                new P16AzadeganNotificationSignature {
                    DeclaringType = "Haestad.Domain.IResultDataConnection", Member = "GetRunUserNotificationsSummary",
                    Signature = "Haestad.Support.User.IUserNotification[] GetRunUserNotificationsSummary(int)",
                    ArgumentSemantics = "UNVERIFIED", Invoked = false },
                new P16AzadeganNotificationSignature {
                    DeclaringType = "Haestad.Domain.IResultDataConnection", Member = "GetTimeStepUserNotificationsSummary",
                    Signature = "Haestad.Support.User.IUserNotification[] GetTimeStepUserNotificationsSummary(int,int)",
                    ArgumentSemantics = "UNVERIFIED", Invoked = false },
                new P16AzadeganNotificationSignature {
                    DeclaringType = "Haestad.Domain.IResultDataConnection", Member = "GetUserNotifications",
                    Signature = "Haestad.Support.User.IUserNotification[] GetUserNotifications(int,int,int)",
                    ArgumentSemantics = "UNVERIFIED", Invoked = false }
            };
        }

        // The live notification probe stays disabled unless both prerequisites hold. Neither is
        // satisfiable from the installed surface today, so the reason is recorded rather than
        // worked around.
        internal static string NotificationProbeReadiness(bool connectionIdentityEstablished,
            bool argumentSemanticsEstablished)
        {
            if (!argumentSemanticsEstablished) return ArgumentSemanticsUnverified;
            if (!connectionIdentityEstablished) return ConnectionIdentityUnverified;
            return null;
        }

        internal static bool NotificationProbeEnabled(bool connectionIdentityEstablished,
            bool argumentSemanticsEstablished) =>
            NotificationProbeReadiness(connectionIdentityEstablished, argumentSemanticsEstablished) == null;

        // ------------------------------------------------------------ request validation

        internal static void ValidateRequest(HostRequest request)
        {
            if (request == null || request.Operation != Operation)
                throw new InvalidDataException("Azadegan sub-engine probe operation mismatch.");
            if (request.EngineeringPlan != null || request.WriteAuthorizationTokenId != null ||
                request.WriteAuthorizationScope != null || request.WriteAuthorizationTargetWorkingPath != null)
                throw new InvalidDataException("Azadegan sub-engine probe permits no engineering plan or write authority.");
            if (request.Diagnostics == null)
                throw new InvalidDataException("Azadegan sub-engine probe diagnostics are absent.");
            string copyId;
            if (!request.Diagnostics.TryGetValue("P16AzadeganCopyId", out copyId) ||
                !Regex.IsMatch(copyId ?? "", "^[a-f0-9]{32}$") || request.WorkingCopyId != copyId)
                throw new InvalidDataException("Azadegan sub-engine probe copy identity is not a controlled per-run identifier.");
            string modelPath = Path.Combine(P16AzadeganReadOnlyResultHost.WorkingRoot, copyId,
                P16AzadeganReadOnlyResultHost.ModelFileName);
            if (!string.Equals(Path.GetFullPath(request.WorkingModelPath ?? ""), Path.GetFullPath(modelPath),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Azadegan sub-engine probe must target its own disposable working copy only.");
            RuntimeHostProtocol.ControlledDirectory(Path.GetDirectoryName(modelPath), "ReadOnly");
            WorkingCopyIntegrity.RejectLinks(Path.GetDirectoryName(modelPath));
            WorkingCopyIntegrity.RejectLinks(modelPath);
            string digest;
            if (!request.Diagnostics.TryGetValue("P16AzadeganInventorySHA256", out digest) ||
                !Regex.IsMatch(digest ?? "", "^[A-F0-9]{64}$"))
                throw new InvalidDataException("Azadegan sub-engine probe inventory digest is malformed.");
            var expected = P16AzadeganReadOnlyResultHost.Context(copyId, digest);
            if (request.Diagnostics.Count != expected.Count ||
                expected.Any(pair => !request.Diagnostics.ContainsKey(pair.Key) || request.Diagnostics[pair.Key] != pair.Value))
                throw new InvalidDataException("Azadegan sub-engine probe requires the exact bounded scenario context.");
        }

        // ------------------------------------------------------------ receiving-side validation

        internal static void ValidateObservation(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new InvalidDataException("Sub-engine probe observation is absent.");
            P16AzadeganSubEngineProbeObservation o;
            try { o = RuntimeHostProtocol.Serializer().Deserialize<P16AzadeganSubEngineProbeObservation>(json); }
            catch (Exception error) { throw new InvalidDataException("Malformed sub-engine probe observation: " + error.GetType().Name); }
            if (o == null) throw new InvalidDataException("Sub-engine probe observation is empty.");
            if (o.ScenarioId != ScenarioId) throw new InvalidDataException("Sub-engine probe observed an unexpected scenario.");
            if (o.NotificationApiInvocations != 0)
                throw new InvalidDataException("Sub-engine probe must not invoke any notification accessor.");
            if (o.SolverRunInvocations != 0 || o.SaveInvocations != 0 || o.ScenarioCreateInvocations != 0 ||
                o.AlternativeCreateInvocations != 0 || o.MutationInvocations != 0)
                throw new InvalidDataException("Sub-engine probe must not solve, save, create or mutate.");
            if (o.NotificationSignatures == null || o.NotificationSignatures.Length != 3 ||
                o.NotificationSignatures.Any(s => s == null || s.Invoked))
                throw new InvalidDataException("Sub-engine probe must record all notification signatures uninvoked.");
            if (o.NotificationProbeEnabled && o.NotificationProbeDisabledReason != null)
                throw new InvalidDataException("Sub-engine probe notification readiness is self-contradictory.");
            if (!o.NotificationGateUnchanged)
                throw new InvalidDataException("Sub-engine probe may not change the notification completeness gate.");
        }

        // ------------------------------------------------------------ host-side execution

        internal static void ExecuteRead(object dataset, object project, HostRequest request, ModelReadEvidence evidence)
        {
            var o = new P16AzadeganSubEngineProbeObservation
            {
                ScenarioId = ScenarioId,
                ProductEngineName = ProductEngine,
                ExpectedActiveEngineName = SubEngine,
                NotificationSignatures = NotificationSignatures(),
                NotificationProbeEnabled = false,
                NotificationProbeDisabledReason = NotificationProbeReadiness(false, false),
                NotificationApiInvocations = 0,
                NotificationGateUnchanged = true,
                NotificationGateEvidence = GateEvidence,
                ModelOpenInvocations = 1, // The bounded lifecycle session, recorded explicitly.
                SolverRunInvocations = 0, SaveInvocations = 0,
                ScenarioCreateInvocations = 0, AlternativeCreateInvocations = 0, MutationInvocations = 0
            };
            object manager = null;
            Type managerType = null;
            try
            {
                var dataType = Contract("Haestad.Domain", "Haestad.Domain.IDomainDataSet");
                var scenarioType = Contract("Haestad.Domain", "Haestad.Domain.IScenario");
                var scenarioManagerType = Contract("Haestad.Domain", "Haestad.Domain.IScenarioManager");
                var elementsType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElementManager");
                var elementType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElement");
                managerType = Contract("Haestad.Domain", "Haestad.Domain.IResultManager");

                object scenarioManager = Get(dataset, dataType, "ScenarioManager", scenarioManagerType);
                o.ActiveScenarioId = (int)Get(scenarioManager, scenarioManagerType, "ActiveScenarioID", typeof(int));
                object scenario = Invoke(scenarioManager, elementsType, "Element", elementType, new[] { typeof(int) }, ScenarioId);
                if (!scenarioType.IsInstanceOfType(scenario))
                    throw new InvalidOperationException("Requested scenario 353 is not an IScenario.");

                o.PhysicalAlternativeId = (int)Invoke(scenario, scenarioType, "AlternativeID", typeof(int),
                    new[] { typeof(int) }, int.Parse(P16AzadeganReadOnlyResultHost.PhysicalAlternativeTypeId, CultureInfo.InvariantCulture));
                o.CalculationOptionsId = (int)Invoke(scenario, scenarioType, "CalculationOptionsID", typeof(int),
                    new[] { typeof(string) }, ProductEngine);
                if (o.PhysicalAlternativeId != PhysicalAlternativeId || o.CalculationOptionsId != CalculationOptionsId)
                {
                    o.Status = ScenarioContextMismatch;
                    return; // No assignment, copy or recalculation is attempted.
                }

                // The product facade's own answer about which sub-engine owns its results.
                o.ActiveEngineTypeNameForProduct = (string)Invoke(scenario, scenarioType,
                    "GetActiveNumericalEngineTypeName", typeof(string), new[] { typeof(string) }, ProductEngine);
                o.ActiveEngineMatchesExpected = o.ActiveEngineTypeNameForProduct == SubEngine;

                manager = Invoke(scenario, scenarioType, "ResultManager", managerType, new[] { typeof(string) }, SubEngine);
                o.ResultManagerAvailable = manager != null;
                if (manager == null) { o.Status = ResultManagerUnavailable; return; }
                o.ResultManagerRuntimeType = manager.GetType().FullName;

                o.ResultManagerEngineName = (string)Get(manager, managerType, "NumericalEngineTypeName", typeof(string));
                object resultScenario = Get(manager, managerType, "Scenario", scenarioType);
                o.ResultManagerScenarioId = ScenarioAlternativeCapabilityMapper.GetElementId(resultScenario);
                if (o.ResultManagerEngineName != SubEngine || o.ResultManagerScenarioId != ScenarioId)
                {
                    o.Status = ResultManagerContextMismatch;
                    return; // A result manager for another engine or scenario is never accepted.
                }
                if (!o.ActiveEngineMatchesExpected) { o.Status = ActiveEngineMismatch; return; }

                o.ResultManagerWasActive = (bool)Get(manager, managerType, "IsActive", typeof(bool));
                if (o.ResultManagerWasActive != true)
                {
                    Invoke(manager, managerType, "Open", typeof(void), Type.EmptyTypes);
                    o.ResultManagerOpenedByProbe = true;
                }
                o.HasResults = (bool)Get(manager, managerType, "HasResults", typeof(bool));
                o.Status = o.HasResults == true ? PassStatus : ResultsMissing;
            }
            catch (Exception error)
            {
                var inner = Unwrap(error);
                o.ErrorType = inner.GetType().FullName;
                o.ErrorMessage = inner.Message;
                o.FailureOperation = LastOperation;
                o.Status = BlockedFailureCode;
                RuntimeStageJournal.Record("SubEngineProbeFailure", "EXCEPTION", new { Operation = LastOperation }, inner.ToString());
            }
            finally
            {
                // Only a manager this probe opened is closed; pre-existing state is left untouched.
                if (manager != null && o.ResultManagerOpenedByProbe && managerType != null)
                {
                    try
                    {
                        Invoke(manager, managerType, "Close", typeof(void), Type.EmptyTypes);
                        o.ResultManagerClosed = true;
                    }
                    catch (Exception error) { o.CloseError = Unwrap(error).Message; }
                }
                else if (manager != null && !o.ResultManagerOpenedByProbe) o.ResultManagerClosed = true;

                evidence.Diagnostics[EvidenceKey] = RuntimeHostProtocol.Serializer().Serialize(o);
                evidence.Diagnostics[StatusKey] = o.Status;
                if (o.Status == PassStatus) evidence.Diagnostics[PassFlag] = "true";
            }
        }

        // ------------------------------------------------------------ vendor surface

        private static string _lastOperation;
        private static string LastOperation { get { return _lastOperation; } set { _lastOperation = value; } }

        private static Type Contract(string assembly, string name)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && a.GetName().Name == assembly).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Exact loaded contract assembly unavailable: " + assembly);
            return matches[0].GetType(name, true, false);
        }

        private static object Invoke(object target, Type contract, string name, Type returns, Type[] parameters,
            params object[] values)
        {
            LastOperation = contract.FullName + "." + name;
            if (target == null || !contract.IsInstanceOfType(target))
                throw new InvalidOperationException("Contract instance unavailable: " + LastOperation);
            var method = contract.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, parameters, null);
            if (method == null || method.ReturnType != returns || method.ContainsGenericParameters)
                throw new MissingMethodException("Exact signature unavailable: " + LastOperation);
            return RuntimeStageJournal.Call("P16AzSubEngine:" + LastOperation,
                () => method.Invoke(target, values),
                value => value is bool || value is int || value is string ? value : value == null ? "<null>" : value.GetType().FullName);
        }

        private static object Get(object target, Type contract, string name, Type returns) =>
            Invoke(target, contract, "get_" + name, returns, Type.EmptyTypes);

        private static Exception Unwrap(Exception error)
        {
            var invocation = error as TargetInvocationException;
            return invocation != null && invocation.InnerException != null ? invocation.InnerException : error;
        }

        // ------------------------------------------------------------ receiving-side result

        internal static void ValidateResult(HostResult result)
        {
            string json;
            if (!result.Diagnostics.TryGetValue(EvidenceKey, out json))
                throw new InvalidDataException("Sub-engine probe observation is missing.");
            ValidateObservation(json);
            string status;
            if (!result.Diagnostics.TryGetValue(StatusKey, out status))
                throw new InvalidDataException("Sub-engine probe status is missing.");
            if (!result.Diagnostics.TryGetValue(PassFlag, out string passed) || passed != "true")
                throw new InvalidDataException("Sub-engine probe result did not carry its pass flag.");
            if (status != PassStatus)
                throw new InvalidDataException("Sub-engine probe pass flag contradicts its status.");
        }
    }
}
