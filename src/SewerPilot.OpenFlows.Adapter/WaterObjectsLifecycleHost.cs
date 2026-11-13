using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    internal static class WaterObjectsLifecycleHost
    {
        // Ordinary read-only intake needs the same bounded, exact-copy question handling
        // as the qualified native workflows; the graphical wrapper can require an operator.
        internal static bool UsesControlledProjectOpen(HostRequest request) => request != null &&
            (request.Operation == RuntimeHostProtocol.Operation ||
             (request.Operation == P16AzadeganCalculationHost.Operation &&
              request.Diagnostics != null && request.Diagnostics.TryGetValue(P16AzadeganCalculationHost.PhaseKey, out string phase) &&
              phase == P16AzadeganCalculationHost.PhaseRead) ||
             // The general readback reopens a persisted model that the engine already wrote. It must
             // never be exposed to an interactive upgrade/save prompt, so it opens controlled too.
             // The terrain discovery pass is the same: a metadata read that must not prompt. The
             // rebinding and verification passes reopen that same persisted copy, and a prompt at
             // open time would either mutate it or hang the host, so they are controlled as well.
             (request.Operation == GeneralModelCalculationHost.Operation &&
              request.Diagnostics != null &&
              request.Diagnostics.TryGetValue(GeneralModelCalculationHost.PhaseKey, out string generalPhase) &&
              (generalPhase == GeneralModelCalculationHost.PhaseRead ||
               generalPhase == GeneralModelCalculationHost.PhaseDiscover ||
               generalPhase == GeneralModelCalculationHost.PhaseBind ||
               generalPhase == GeneralModelCalculationHost.PhaseVerify)) ||
             request.Operation == P16FarzaneganNativeReadHost.Operation ||
             request.Operation == P14CatalogSelectorDiscoveryHost.Operation ||
             request.Operation == P16FarzaneganNativePilotHost.Operation ||
             request.Operation == P14CatalogPointerPersistenceHost.PrepareOperation ||
             request.Operation == P14CatalogPointerPersistenceHost.ScopedRepairOperation ||
             request.Operation == P14CatalogPointerPersistenceHost.ScopedComputeOperation ||
             request.Operation == P14CatalogPointerPersistenceHost.AnalysisPrepareOperation ||
             request.Operation == P16AnalysisRemediationHost.PrepareOperation ||
             request.Operation == P16AnalysisRemediationHost.ComputeOperation);

        internal static void WithCleanup(Action read, Action cleanup)
        { try { read(); } finally { RuntimeStageJournal.Call("Cleanup", cleanup); } }

        // Resolution is deliberately limited to ElementIDs and Item[Int32].
        internal static IEnumerable<Type> Hierarchy(Type type)
        {
            var seen = new HashSet<Type>();
            for (Type current = type; current != null; current = current.BaseType)
                if (seen.Add(current)) yield return current;
            var pending = new Queue<Type>(type.GetInterfaces().OrderBy(item => item.FullName, StringComparer.Ordinal));
            while (pending.Count > 0)
            {
                Type current = pending.Dequeue();
                if (!seen.Add(current)) continue;
                yield return current;
                foreach (Type ancestor in current.GetInterfaces()) pending.Enqueue(ancestor);
            }
        }
        internal static MethodInfo ElementIDs(Type type)
        {
            var matches = Hierarchy(type).SelectMany(item => item.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(item => item.Name == "ElementIDs" && item.GetParameters().Length == 0).ToArray();
            if (matches.Length == 0 || matches.Any(item => item.ContainsGenericParameters || item.ReturnType != matches[0].ReturnType))
                throw new InvalidOperationException("ElementIDs callable contract absent or ambiguous.");
            return matches[0];
        }
        internal static PropertyInfo Indexer(Type type)
        {
            var property = Hierarchy(type).SelectMany(item => item.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .FirstOrDefault(item => item.Name == "Item" && item.GetGetMethod() != null &&
                    item.GetIndexParameters().Length == 1 && item.GetIndexParameters()[0].ParameterType == typeof(int));
            if (property == null) throw new InvalidOperationException("Item[Int32] getter absent.");
            return property;
        }
        internal static object EnumMember(Type type, string name) => Enum.Parse(type, name, false);
    }

    // Explicit WaterObjects licensing lifecycle; no preference resets, login or licensing-tool workflow.
    internal sealed class SolverLicenseContext : IDisposable
    {
        [ThreadStatic] internal static SolverLicenseContext Current;
        private readonly Type releaseType, licenseType, messageType;
        private readonly object productId, platform;
        private readonly string version;
        private readonly IntPtr owner;
        private readonly ModelReadEvidence evidence;
        private object release, license;
        private bool startAttempted, attached, disposed;

        internal SolverLicenseContext(Type releaseType, Type licenseType, Type messageType,
            object productId, object platform, string version, IntPtr owner, ModelReadEvidence evidence)
        {
            if (Current != null || productId?.ToString() != "Bentley_SewerGEMS" || platform?.ToString() != "Standalone" ||
                !Version.TryParse(version, out Version installed) || installed.Major <= 0)
                throw new InvalidOperationException("P15_BLOCKED_LICENSE_CONTEXT_UNPROVEN");
            this.releaseType = releaseType; this.licenseType = licenseType; this.messageType = messageType;
            this.productId = productId; this.platform = platform; this.version = version; this.owner = owner; this.evidence = evidence;
            Current = this;
        }

        private static MethodInfo Exact(Type type, string name, Type result, bool isStatic, params Type[] args)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance), null, args, null);
            if (method == null || method.ReturnType != result || method.IsStatic != isStatic)
                throw new InvalidOperationException("P15_BLOCKED_LICENSE_CONTEXT_UNPROVEN: " + name);
            return method;
        }
        private static object Call(object target, MethodInfo method, params object[] args) =>
            RuntimeStageJournal.Call("Licensing:" + method.Name, () => method.Invoke(target, args),
                value => value is bool || value?.GetType().IsEnum == true ? value.ToString() : value?.GetType().FullName);

        internal static void AttachCurrent(object engine)
        {
            if (Current != null) Current.StartAndAttach(engine);
            else if ((engine.GetType().FullName ?? "").StartsWith("Haestad.", StringComparison.Ordinal))
                throw new InvalidOperationException("P15_BLOCKED_LICENSE_CONTEXT_MISSING");
        }

        internal void StartAndAttach(object engine)
        {
            if (disposed || attached || license != null) throw new InvalidOperationException("P15_BLOCKED_LICENSE_REUSE");
            var constructor = releaseType.GetConstructor(new[] { productId.GetType(), typeof(string) });
            var create = Exact(licenseType, "Default", licenseType, true, releaseType, typeof(IntPtr), messageType);
            var initialize = Exact(licenseType, "Initialize", typeof(bool), false);
            var start = licenseType.GetMethod("StartDesktop", Type.EmptyTypes);
            var bind = Exact(engine.GetType(), "SetLicensingInfo", typeof(void), false, licenseType);
            Exact(licenseType, "StopDesktop", typeof(void), false);
            Exact(licenseType, "Dispose", typeof(void), false);
            Exact(releaseType, "Dispose", typeof(void), false);
            var initialized = licenseType.GetProperty("IsInitialized");
            var analysis = licenseType.GetProperty("IsAnalysisEnabled");
            var platformProperty = releaseType.GetProperty("PlatformType");
            if (constructor == null || start == null || start.IsStatic || !start.ReturnType.IsEnum ||
                initialized?.PropertyType != typeof(bool) || initialized.GetGetMethod() == null ||
                analysis?.PropertyType != typeof(bool) || analysis.GetGetMethod() == null ||
                platformProperty?.PropertyType != platform.GetType() || platformProperty.GetSetMethod() == null)
                throw new InvalidOperationException("P15_BLOCKED_LICENSE_CONTEXT_UNPROVEN");
            evidence.Diagnostics["LicenseProductName"] = productId.ToString();
            evidence.Diagnostics["LicenseProductID"] = Convert.ToInt32(productId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            evidence.Diagnostics["LicenseProductVersion"] = version;
            evidence.Diagnostics["LicenseFacadeIdentity"] = licenseType.Assembly.FullName;
            release = RuntimeStageJournal.Call("Licensing:ProductRelease", () => constructor.Invoke(new[] { productId, version }));
            platformProperty.GetSetMethod().Invoke(release, new object[] { platform });
            license = Call(null, create, release, owner, null);
            if (license == null) throw new InvalidOperationException("P15_BLOCKED_LICENSE_CREATION");
            evidence.Diagnostics["LicenseInitializeInvocations"] = "1";
            bool ok = (bool)Call(license, initialize);
            evidence.Diagnostics["LicenseInitialized"] = ok && (bool)initialized.GetValue(license, null) ? "true" : "false";
            if (evidence.Diagnostics["LicenseInitialized"] != "true") throw new InvalidOperationException("P15_BLOCKED_LICENSE_INITIALIZATION");
            startAttempted = true;
            evidence.Diagnostics["LicenseStartDesktopInvocations"] = "1";
            object runStatus = Call(license, start);
            evidence.Diagnostics["LicenseRunStatus"] = runStatus?.ToString() ?? "Unknown";
            evidence.Diagnostics["LicenseAnalysisEnabled"] = (bool)analysis.GetValue(license, null) ? "true" : "false";
            if (evidence.Diagnostics["LicenseRunStatus"] != "OK" || evidence.Diagnostics["LicenseAnalysisEnabled"] != "true")
                throw new InvalidOperationException("P15_BLOCKED_LICENSE_NOT_READY");
            Call(engine, bind, license);
            int propagatedCount = PropagateSubEngineLicenses(engine, license, licenseType);
            if (propagatedCount > 0)
                evidence.Diagnostics["SolverLicensePropagatedCount"] = propagatedCount.ToString();
            attached = true;
            evidence.Diagnostics["SolverLicenseAttached"] = "true";
            RuntimeStageJournal.Record("LICENSE_CONTEXT_VERIFIED", "EXIT", new { product = productId.ToString(), version, status = runStatus.ToString() });
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            bool IEqualityComparer<object>.Equals(object x, object y) => object.ReferenceEquals(x, y);
            int IEqualityComparer<object>.GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private static int PropagateSubEngineLicenses(object rootEngine, object license, Type licenseType)
        {
            if (rootEngine == null || license == null) return 0;
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { rootEngine };
            int propagatedCount = 0;

            void Propagate(object target, int depth)
            {
                if (target == null || depth > 8 || !visited.Add(target)) return;

                bool attachedToTarget = false;
                var method = target.GetType().GetMethod("SetLicensingInfo",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { licenseType },
                    null);
                if (method != null && method.ReturnType == typeof(void))
                {
                    try
                    {
                        method.Invoke(target, new[] { license });
                        attachedToTarget = true;
                    }
                    catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsLifecycleHost.cs: " + suppressedEx.Message); }
                }
                else
                {
                    foreach (var m in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (m.Name == "SetLicensingInfo" && m.ReturnType == typeof(void))
                        {
                            var parameters = m.GetParameters();
                            if (parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(license))
                            {
                                try
                                {
                                    m.Invoke(target, new[] { license });
                                    attachedToTarget = true;
                                    break;
                                }
                                catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsLifecycleHost.cs: " + suppressedEx.Message); }
                            }
                        }
                    }
                }

                var curr = target.GetType();
                while (curr != null && curr != typeof(object))
                {
                    var licField = curr.GetField("m_license", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (licField != null && (licField.FieldType.IsInstanceOfType(license) || licField.FieldType.IsAssignableFrom(licenseType)))
                    {
                        try
                        {
                            AdapterWriteBinding.SetFieldValue(licField, target, license);
                            attachedToTarget = true;
                        }
                        catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsLifecycleHost.cs: " + suppressedEx.Message); }
                    }
                    curr = curr.BaseType;
                }

                if (attachedToTarget) propagatedCount++;

                TraverseFields(target, depth);
            }

            void TraverseFields(object target, int depth)
            {
                var currentType = target.GetType();
                while (currentType != null && currentType != typeof(object))
                {
                    if (currentType.Namespace != null &&
                        (currentType.Namespace.StartsWith("System.", StringComparison.Ordinal) ||
                         currentType.Namespace == "System") &&
                        !typeof(System.Collections.IEnumerable).IsAssignableFrom(currentType))
                    {
                        break;
                    }

                    FieldInfo[] fields;
                    try
                    {
                        fields = currentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    }
                    catch
                    {
                        break;
                    }

                    foreach (var field in fields)
                    {
                        if (field.FieldType.IsPointer || field.FieldType.IsPrimitive || field.FieldType == typeof(string) || field.FieldType.IsValueType)
                            continue;

                        object val = null;
                        try
                        {
                            val = field.GetValue(target);
                        }
                        catch
                        {
                            continue;
                        }

                        if (val == null) continue;

                        if (val is System.Collections.IDictionary dict)
                        {
                            try
                            {
                                foreach (var dictValue in dict.Values)
                                {
                                    if (dictValue != null)
                                        Propagate(dictValue, depth + 1);
                                }
                            }
                            catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsLifecycleHost.cs: " + suppressedEx.Message); }
                        }
                        else if (val is System.Collections.IEnumerable enumerable && !(val is string))
                        {
                            try
                            {
                                foreach (var item in enumerable)
                                {
                                    if (item != null)
                                        Propagate(item, depth + 1);
                                }
                            }
                            catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsLifecycleHost.cs: " + suppressedEx.Message); }
                        }
                        else if (val.GetType().Namespace != null &&
                                 (val.GetType().Namespace.StartsWith("Haestad.", StringComparison.Ordinal) ||
                                  val.GetType().Namespace.StartsWith("Bentley.", StringComparison.Ordinal)))
                        {
                            Propagate(val, depth + 1);
                        }
                    }

                    currentType = currentType.BaseType;
                }
            }

            TraverseFields(rootEngine, 0);
            return propagatedCount;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var errors = new List<Exception>();
            Action<Action> cleanup = action => { try { action(); } catch (Exception error) { errors.Add(error); } };
            if (license != null && startAttempted) cleanup(() => {
                evidence.Diagnostics["LicenseStopDesktopInvocations"] = "1";
                Call(license, Exact(licenseType, "StopDesktop", typeof(void), false));
                evidence.Diagnostics["LicenseStopDesktopCompleted"] = "true";
            });
            if (license != null) cleanup(() => {
                evidence.Diagnostics["LicenseDisposeInvocations"] = "1";
                Call(license, Exact(licenseType, "Dispose", typeof(void), false));
                evidence.Diagnostics["LicenseDisposed"] = "true";
            });
            if (release != null) cleanup(() => {
                Call(release, Exact(releaseType, "Dispose", typeof(void), false));
                evidence.Diagnostics["LicenseProductReleaseDisposed"] = "true";
            });
            if (ReferenceEquals(Current, this)) Current = null;
            evidence.Diagnostics["LicenseCleanupCompleted"] = errors.Count == 0 ? "true" : "false";
            if (errors.Count != 0) throw new AggregateException("P15_BLOCKED_LICENSE_CLEANUP", errors);
        }
    }

    // Used only by the dedicated production RuntimeHost, in its default AppDomain.
    internal sealed class LifecycleWorker
    {
        public LifecycleWorker() { }
        private string root;
        private readonly ModelReadEvidence evidence = new ModelReadEvidence();
        private AssemblyProvenanceDiagnostics provenance;
        private string stage = "InitializationFailure";
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetDllDirectory(uint length, System.Text.StringBuilder buffer);

        public ModelReadEvidence Run(string selectedRoot, string workingPath, HostRequest request = null)
        {
            if (request?.Operation == GravityWriteAndSolverHost.ProductionOperation)
                GravityExecutionAuthorization.Require(request);
            root = Path.GetFullPath(selectedRoot);
            if (request?.CoreFiles != null)
            {
                evidence.Diagnostics["SolverTrustedRuntimeRoot"] = root;
                evidence.Diagnostics["SolverTrustedDomainSHA256"] = request.CoreFiles.Single(c => c.SimpleName == "Haestad.Domain").SHA256;
            }
            provenance = new AssemblyProvenanceDiagnostics(evidence);
            WorkingCopyIntegrity.RejectLinks(root);
            var thread = new Thread(() =>
            {
                try { RuntimeStageJournal.Call("STAHostThreadStarted", () => RunSTA(workingPath, request)); }
                catch (Exception error)
                { evidence.CloseCompleted = false; evidence.FailureCode = "CloseFailure"; evidence.Exception = (evidence.Exception ?? "") + "\n" + error; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            RuntimeStageJournal.Call("STAThreadStart", thread.Start);
            RuntimeStageJournal.Call("STAHostCompletion", () => thread.Join());
            evidence.Diagnostics["StaCompleted"] = "true";
            return evidence;
        }
        private Type VendorType(string assembly, string name)
        {
            string path = Path.Combine(root, assembly + ".dll");
            WorkingCopyIntegrity.RejectLinks(path);
            if (AssemblyName.GetAssemblyName(path).Name != assembly) throw new BadImageFormatException("Selected assembly identity mismatch.");
            var loaded = RuntimeStageJournal.Call("VendorAssemblyLoad:" + assembly, () => Assembly.LoadFrom(path), value => new { Type = name, File = WorkingCopyIntegrity.Fingerprint(value.Location) });
            if (!string.Equals(Path.GetFullPath(loaded.Location), path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Vendor binding escaped the selected runtime.");
            return RuntimeStageJournal.Call("VendorType:" + name, () => loaded.GetType(name, true, false));
        }
        private Assembly Resolve(object sender, ResolveEventArgs arguments)
        {
            var trace = provenance.BeginResolve(arguments);
            string candidate = null;
            Assembly returned = null;
            try
            {
            string name = new AssemblyName(arguments.Name).Name;
            if (name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return null;
            string path = Path.Combine(root, name + ".dll");
            candidate = path;
            if (!File.Exists(path)) return null;
            WorkingCopyIntegrity.RejectLinks(path);
            if (AssemblyName.GetAssemblyName(path).Name != name) throw new BadImageFormatException("Dependency identity mismatch.");
            returned = RuntimeStageJournal.Call("VendorDependencyLoad:" + name, () => Assembly.LoadFrom(path), value => new { File = WorkingCopyIntegrity.Fingerprint(value.Location) });
            return returned;
            }
            finally { provenance.EndResolve(trace, candidate, returned); }
        }
        private static object Invoke(object target, MethodInfo method, params object[] args)
        {
            if (method == null) throw new MissingMethodException("A proven lifecycle member is unavailable.");
            return RuntimeStageJournal.Call(method.DeclaringType.FullName + "." + method.Name, () => method.Invoke(target, args), value => value is bool || value is int ? value : value?.GetType().FullName);
        }
        private static MethodInfo Method(Type type, string name, params Type[] parameters) => type.GetMethod(name, parameters);
        private static object Construct(Type type, Type[] parameters, params object[] arguments)
        {
            var constructor = type.GetConstructor(parameters);
            if (constructor == null) throw new MissingMethodException(type.FullName, ".ctor");
            return RuntimeStageJournal.Call(type.FullName + ".ctor", () => constructor.Invoke(arguments), value => value?.GetType().FullName);
        }
        private static void RecordP16OpenQuestionDiagnostics(ModelReadEvidence evidence, P16SaveQuestionRecorder recorder)
        {
            evidence.Diagnostics["P16OpenQuestionCount"] = recorder.Count.ToString(CultureInfo.InvariantCulture);
            evidence.Diagnostics["P16OpenQuestionClassifications"] = string.Join(",", recorder.Classifications);
            evidence.Diagnostics["P16OpenQuestionDecisions"] = string.Join(",", recorder.Decisions);
            evidence.Diagnostics["P16OpenQuestionMessageSha256"] = string.Join(",", recorder.Records.Select(r => r.MessageSha256));
            evidence.Diagnostics["P16OpenQuestionCaptionSha256"] = string.Join(",", recorder.Records.Select(r => r.CaptionSha256));
            evidence.Diagnostics["P16OpenQuestionMessageLengths"] = string.Join(",", recorder.Records.Select(r => r.MessageLength.ToString(CultureInfo.InvariantCulture)));
            evidence.Diagnostics["P16OpenQuestionDefaultYes"] = string.Join(",", recorder.Records.Select(r => r.DefaultYesRequested ? "true" : "false"));
            evidence.Diagnostics["P16OpenWarningCount"] = recorder.WarningCount.ToString(CultureInfo.InvariantCulture);
            evidence.Diagnostics["P16OpenWarningMessageSha256"] = string.Join(",", recorder.WarningDigests);
            evidence.Diagnostics["P16OpenWarningBudgetExceeded"] = recorder.WarningBudgetExceeded ? "true" : "false";
            evidence.Diagnostics["P16OpenQuestionPolicyClean"] =
                (!recorder.AnyUnauthorizedQuestion && !recorder.BudgetExceeded && !recorder.WarningBudgetExceeded) ? "true" : "false";
        }

        private void RunSTA(string workingPath, HostRequest request)
        {
            object application = null, parent = null, ui = null, project = null;
            SolverLicenseContext solverLicense = null;
            Form owner = null;
            Type frameworkParent = null, properties = null, productUI = null;
            bool openAttempted = false;
            string priorDirectory = Environment.CurrentDirectory;
            var priorDllDirectory = new System.Text.StringBuilder(32768);
            GetDllDirectory((uint)priorDllDirectory.Capacity, priorDllDirectory);
            ResolveEventHandler resolver = Resolve;
            AppDomain.CurrentDomain.AssemblyResolve += resolver;
            try
            {
                Environment.CurrentDirectory = root;
                if (!SetDllDirectory(root)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                evidence.Diagnostics["STA"] = Thread.CurrentThread.GetApartmentState().ToString();
                var descriptionType = VendorType("Haestad.Framework", "Haestad.Framework.Application.IApplicationDescription");
                var applicationInterface = VendorType("Haestad.Framework", "Haestad.Framework.Application.IApplicationModel");
                var uiInterface = VendorType("Haestad.Framework", "Haestad.Framework.Application.IUserInterface");
                frameworkParent = VendorType("Haestad.Framework", "Haestad.Framework.Application.HaestadParentFormModel");
                properties = VendorType("Haestad.Framework", "Haestad.Framework.Application.ProjectProperties");
                var factoryType = VendorType("Haestad.StormSewerProduct", "Haestad.StormSewerProduct.Library.ProductizationLibrary");
                var applicationType = VendorType("Haestad.StormSewerProduct", "Haestad.StormSewerProduct.Application.StormSewerProductApplicationModel");
                var parentType = VendorType("Haestad.StormSewerProduct", "Haestad.StormSewerProduct.Application.StormSewerProductParentFormModel");
                productUI = VendorType("Haestad.StormSewerProduct", "Haestad.StormSewerProduct.Application.StormSewerProductParentFormUIModel");
                var uiType = VendorType("Haestad.SewerGEMS", "Haestad.Delaware.Application.DelawareParentFormUIModel");
                var platformType = VendorType("Haestad.LicensingFacade", "Haestad.LicensingFacade.LicensePlatformType");
                Version hostVersion = Assembly.GetExecutingAssembly().GetName().Version;
                evidence.Diagnostics["Factory"] = factoryType.FullName + ".NewSewerGEMSApplicationDescription";
                evidence.Diagnostics["HostAssemblyVersion"] = hostVersion.ToString();
                evidence.Diagnostics["PlatformVersion"] = hostVersion.ToString();
                evidence.Diagnostics["ApplicationModel"] = applicationType.FullName;
                evidence.Diagnostics["ParentFormModel"] = parentType.FullName;
                evidence.Diagnostics["UIModel"] = uiType.FullName;
                owner = RuntimeStageJournal.Call("HiddenOwnerCreated", () => new Form { ShowInTaskbar = false });
                IntPtr hiddenHandle = RuntimeStageJournal.Call("HiddenOwnerHandle", () => owner.Handle);
                object hiddenUI = HiddenOwner(uiInterface, owner);
                WaterObjectsLifecycleHost.WithCleanup(() =>
                {
                    object description = Invoke(null, Method(factoryType, "NewSewerGEMSApplicationDescription", typeof(Version), platformType, typeof(string)),
                        hostVersion, WaterObjectsLifecycleHost.EnumMember(platformType, "Standalone"), hostVersion.ToString());
                    evidence.Diagnostics["ApplicationDescriptionCreated"] = "true";
                    RuntimeStageJournal.Call("ApplicationDescriptionCreated", () => { });
                    application = Construct(applicationType, new[] { typeof(object[]), descriptionType }, new object[0], description);
                    evidence.Diagnostics["ApplicationModelCreated"] = "true";
                    RuntimeStageJournal.Call("ApplicationModelCreated", () => { });
                    parent = Construct(parentType, new[] { applicationInterface }, application);
                    ui = Construct(uiType, new[] { frameworkParent, typeof(bool) }, parent, true);
                    evidence.Diagnostics["ParentUiModelCreated"] = "true";
                    RuntimeStageJournal.Call("ParentUiModelCreated", () => { });
                    Invoke(ui, Method(productUI, "InitializeOnStartUp"));
                    Invoke(application, Method(applicationType, "SetCOMUserInterface", uiInterface), hiddenUI);
                    Invoke(parent, Method(parentType, "SetCOMUserInterface", uiInterface), hiddenUI);
                    Invoke(ui, Method(productUI, "SetCOMUserInterface", uiInterface), hiddenUI);
                    bool solverSession = request != null && (request.Operation == RuntimeHostProtocol.OpGravityWriteAndSolver ||
                        (request.Operation == GravityWriteAndSolverHost.ProductionOperation && request.EngineeringPlan.Calculate &&
                         request.Diagnostics.TryGetValue("GravityEngineeringPhase", out string engineeringPhase) && engineeringPhase == "REOPEN") ||
                        (request.Operation == P15TechnicalCapabilityHost.Operation &&
                         request.Diagnostics.TryGetValue("P15TechnicalPhase", out string licensePhase) && licensePhase == "REOPEN") ||
                        (request.Operation == P16HeadlessResultProofHost.Operation &&
                         (!request.Diagnostics.TryGetValue("P16ProofPhase", out string proofPhase) || proofPhase != "REOPEN")) ||
                        (request.Operation == P16AzadeganCalculationHost.Operation &&
                         request.Diagnostics.TryGetValue(P16AzadeganCalculationHost.PhaseKey, out string azadeganPhase) &&
                         azadeganPhase == P16AzadeganCalculationHost.PhaseCalculate) ||
                        // A general CALCULATE session runs the licensed engine exactly like the P16
                        // native path. Its READ phase must not acquire a solver licence context.
                        (request.Operation == GeneralModelCalculationHost.Operation &&
                         request.Diagnostics.TryGetValue(GeneralModelCalculationHost.PhaseKey, out string generalCalcPhase) &&
                         generalCalcPhase == GeneralModelCalculationHost.PhaseCalculate) ||
                        P14FarzaneganTestOnlyHost.IsPilot(request.Operation));
                    if (solverSession)
                    {
                        string installedExecutable = Path.Combine(root, "SewerGEMS.exe");
                        WorkingCopyIntegrity.RejectLinks(installedExecutable);
                        string installedVersion = AssemblyName.GetAssemblyName(installedExecutable).Version.ToString();
                        solverLicense = new SolverLicenseContext(
                            VendorType("Haestad.LicensingFacade", "Haestad.LicensingFacade.ProductRelease"),
                            VendorType("Haestad.LicensingFacade", "Haestad.LicensingFacade.License"),
                            VendorType("Haestad.Support", "Haestad.Support.User.IMessageHandler"),
                            WaterObjectsLifecycleHost.EnumMember(VendorType("Haestad.LicensingFacade", "Haestad.LicensingFacade.ProductId"), "Bentley_SewerGEMS"),
                            WaterObjectsLifecycleHost.EnumMember(platformType, "Standalone"), installedVersion, hiddenHandle, evidence);
                    }
                    evidence.LifecycleInitialized = true;
                    stage = "ModelOpenFailure"; openAttempted = true;
                    evidence.Diagnostics["OpenCalls"] = "1";
                    RuntimeStageJournal.Call("ModelOpenEntered", () => { });
                    bool p16ControlledReadOpen = WaterObjectsLifecycleHost.UsesControlledProjectOpen(request);
                    if (p16ControlledReadOpen)
                    {
                        // Open through the supported project-manager API so ProjectProperties can carry
                        // the bounded question handler. This also applies to the controlled Farzanegan
                        // solve: the graphical wrapper hard-wires WinFormMessageHandler and can block
                        // before solver entry. No question authorization or execution budget changes.
                        var openRecorder = new P16SaveQuestionRecorder(workingPath);
                        Type questionContract = VendorType("Haestad.Support", "Haestad.Support.User.IMessageQuestionHandler");
                        Type resultEnum = VendorType("Haestad.Support", "Haestad.Support.User.MessageHandlerResult");
                        object questionHandler = P16SaveQuestionHandler.Create(questionContract, resultEnum, openRecorder);
                        object openProperties = Invoke(null, Method(properties, "DefaultNominalProjectPath", typeof(string)), workingPath);
                        PropertyInfo handlerProperty = properties.GetProperty("MessageQuestionHandler");
                        if (handlerProperty == null) throw new MissingMemberException(properties.FullName, "MessageQuestionHandler");
                        AdapterWriteBinding.InvokeSetValue(handlerProperty, openProperties, questionHandler);
                        var projectApplicationModel = VendorType("Haestad.Framework", "Haestad.Framework.Application.IProjectApplicationModel");
                        var projectManagerType = VendorType("Haestad.Framework", "Haestad.Framework.Application.IProjectManager");
                        var projectType = VendorType("Haestad.Framework", "Haestad.Framework.Application.IProject");
                        if (!projectApplicationModel.IsInstanceOfType(application))
                            throw new InvalidOperationException("P16_OPEN_PROJECT_MANAGER_UNAVAILABLE: application does not implement IProjectApplicationModel.");
                        object projectManager = RuntimeStageJournal.Call("P16ProjectManagerResolution",
                            () => projectApplicationModel.GetProperty("ProjectManager").GetValue(application, null),
                            value => value?.GetType().FullName);
                        if (projectManager == null) throw new InvalidOperationException("P16_OPEN_PROJECT_MANAGER_UNAVAILABLE: ProjectManager is null.");
                        try
                        {
                            RuntimeStageJournal.Call("P16ProjectManagerOpenProject", () =>
                            {
                                Invoke(projectManager, Method(projectManagerType, "OpenProject", properties), openProperties);
                                return "RETURNED";
                            }, value => value);
                        }
                        finally { RecordP16OpenQuestionDiagnostics(evidence, openRecorder); }
                        if (openRecorder.AnyUnauthorizedQuestion || openRecorder.BudgetExceeded || openRecorder.WarningBudgetExceeded)
                            throw new InvalidOperationException("P16_READ_OPEN_UNAUTHORIZED_QUESTION_OR_BUDGET_EXCEEDED");
                        project = RuntimeStageJournal.Call("P16CurrentProjectAfterOpen",
                            () => projectManagerType.GetProperty("CurrentProject").GetValue(projectManager, null),
                            value => value?.GetType().FullName);
                        bool projectIsOpen = project != null && (bool)projectType.GetProperty("IsOpen").GetValue(project, null);
                        string projectPath = project == null ? null : (string)projectType.GetProperty("FullPath").GetValue(project, null);
                        evidence.Diagnostics["P16OpenPathStrategy"] = "IProjectManager.OpenProject(ProjectProperties)";
                        evidence.Diagnostics["P16OpenProjectPath"] = projectPath ?? "";
                        evidence.OpenSucceeded = projectIsOpen && P16SaveQuestionPolicy.SamePath(projectPath, workingPath);
                    }
                    else
                    {
                        evidence.OpenSucceeded = (bool)Invoke(ui, Method(uiType, "OpenFileWithSpecifiedExtension", typeof(string), typeof(bool)), workingPath, false);
                    }
                    RuntimeStageJournal.Call("ModelOpenCompleted", () => evidence.OpenSucceeded, value => value);
                    if (!evidence.OpenSucceeded) throw new InvalidOperationException(p16ControlledReadOpen
                        ? "IProjectManager.OpenProject did not establish the exact requested open project."
                        : "OpenFileWithSpecifiedExtension returned false.");
                    stage = "ModelReadFailure";
                    if (!p16ControlledReadOpen)
                    {
                        project = RuntimeStageJournal.Call("CurrentGraphicalProject", () => uiType.GetProperty("CurrentGraphicalProject").GetValue(ui, null));
                        evidence.CurrentGraphicalProjectAvailable = project != null;
                        if (project != null) RuntimeStageJournal.Call("CurrentGraphicalProjectEstablished", () => { });
                    }
                    else
                    {
                        // Prefer the graphical wrapper's current project when it exposes the same project;
                        // otherwise keep the exact IProjectManager.CurrentProject already verified above.
                        try
                        {
                            object graphicalProject = RuntimeStageJournal.Call("CurrentGraphicalProjectAfterDirectOpen",
                                () => uiType.GetProperty("CurrentGraphicalProject").GetValue(ui, null),
                                value => value?.GetType().FullName);
                            evidence.CurrentGraphicalProjectAvailable = graphicalProject != null;
                            if (graphicalProject != null) project = graphicalProject;
                        }
                        catch (Exception error)
                        {
                            evidence.Diagnostics["P16CurrentGraphicalProjectLookup"] = error.GetType().FullName;
                            evidence.CurrentGraphicalProjectAvailable = false;
                        }
                        if (evidence.CurrentGraphicalProjectAvailable)
                            RuntimeStageJournal.Call("CurrentGraphicalProjectEstablished", () => { });
                    }
                    var domainProject = VendorType("Haestad.Framework", "Haestad.Framework.Application.IDomainProject");
                    evidence.DomainProjectAvailable = RuntimeStageJournal.Call("IDomainProject", () => project != null && domainProject.IsInstanceOfType(project), value => value);
                    if (evidence.DomainProjectAvailable) RuntimeStageJournal.Call("DomainProjectEstablished", () => { });
                    if (!evidence.DomainProjectAvailable) throw new InvalidOperationException("IDomainProject not established.");
                    object dataset = RuntimeStageJournal.Call("DomainDataSet", () => domainProject.GetProperty("DomainDataSet").GetValue(project, null));
                    evidence.DomainDataSetAvailable = dataset != null;
                    if (dataset != null) RuntimeStageJournal.Call("DomainDataSetEstablished", () => { });
                    if (dataset == null) throw new InvalidOperationException("DomainDataSet is null.");

                    Action readConduitDatum = () =>
                    {
                        var elementType = VendorType("Haestad.Domain", "Haestad.Domain.DomainElementType");
                        object enumValue = RuntimeStageJournal.Call("ElementTypeResolution", () => WaterObjectsLifecycleHost.EnumMember(elementType, "ConduitElementManager"), value => new { Name = value.ToString(), DiagnosticValue = Convert.ToInt32(value) });
                        evidence.Diagnostics["ElementTypeName"] = "ConduitElementManager";
                        evidence.Diagnostics["ElementTypeValueDiagnosticOnly"] = Convert.ToInt32(enumValue).ToString();
                        var datasetType = VendorType("Haestad.Domain", "Haestad.Domain.IDomainDataSet");
                        object manager = RuntimeStageJournal.Call("ConduitManagerResolution", () => Invoke(dataset, Method(datasetType, "DomainElementManager", typeof(int)), Convert.ToInt32(enumValue)), value => value?.GetType().FullName);
                        if (manager == null) throw new InvalidOperationException("Conduit manager is null.");
                        evidence.Diagnostics["ManagerRuntimeType"] = manager.GetType().FullName;
                        var idsMethod = RuntimeStageJournal.Call("ElementIDsMemberResolution", () => WaterObjectsLifecycleHost.ElementIDs(manager.GetType()), value => new { DeclaringType = value.DeclaringType.FullName, ReturnType = value.ReturnType.FullName });
                        evidence.Diagnostics["ElementIDsDeclaringType"] = idsMethod.DeclaringType.FullName;
                        evidence.Diagnostics["ElementIDsReturnType"] = idsMethod.ReturnType.FullName;
                        evidence.Diagnostics["ElementIDsSignature"] = idsMethod.ToString();
                        object ids = Invoke(manager, idsMethod);
                        if (ids != null) RuntimeStageJournal.Call("ElementIdsCompleted", () => { });
                        if (ids == null) throw new InvalidOperationException("ElementIDs returned null.");
                        evidence.Diagnostics["Collection"] = ids.GetType().FullName;
                        var indexer = RuntimeStageJournal.Call("FirstElementIDAcquisition", () => WaterObjectsLifecycleHost.Indexer(ids.GetType()), value => new { Signature = value.ToString(), DeclaringType = value.DeclaringType.FullName });
                        evidence.Diagnostics["IndexerSignature"] = indexer.ToString();
                        evidence.Diagnostics["IndexerDeclaringType"] = indexer.DeclaringType.FullName;
                        object datum;
                        try { datum = RuntimeStageJournal.Call("Item[Int32]", () => indexer.GetValue(ids, new object[] { 0 }), value => value); }
                        catch (TargetInvocationException error) when (error.InnerException is IndexOutOfRangeException || error.InnerException is ArgumentOutOfRangeException)
                        { stage = "NoReadableConduitElement"; throw; }
                        if (!(datum is int)) throw new InvalidOperationException("First conduit ID is not Int32.");
                        evidence.FirstConduitElementID = (int)datum;
                        RuntimeStageJournal.Call("DatumRead", () => new { Name = "FirstConduitElementID", Value = (int)datum }, value => value);
                    };

                    if (request != null && (request.Operation == P16FlowDiagnostic.Operation || request.Operation == P16CalculatedFlowDiagnostic.Operation))
                    {
                        readConduitDatum();
                        stage = "P16_RESULT_DIAGNOSTIC_FAILURE";
                        P16FlowDiagnostic.Run(dataset, project, request, evidence, request.Operation == P16CalculatedFlowDiagnostic.Operation);
                    }
                    else if (request != null && request.Operation == P16HeadlessResultProofHost.Operation)
                    {
                        readConduitDatum();
                        stage = "P16_RESULT_PROOF_FAILURE";
                        if (request.Diagnostics.TryGetValue("P16ProofPhase", out string p16Phase) && p16Phase == "REOPEN")
                            P16HeadlessResultProofHost.ExecuteSecondSessionAndRead(dataset, ui, project, request, evidence);
                        else
                            P16HeadlessResultProofHost.ExecuteFirstSession(dataset, ui, project, request, evidence);
                    }
                    else if (request != null && request.Operation == P16SecondaryMetricsHost.Operation)
                    {
                        readConduitDatum();
                        stage = "P16_SECONDARY_METRICS_FAILURE";
                        P16SecondaryMetricsHost.ExecuteRead(dataset, ui, project, request, evidence);
                    }
                    else if (request != null && request.Operation == P16AzadeganReadOnlyResultHost.Operation)
                    {
                        readConduitDatum();
                        stage = P16AzadeganReadOnlyResultHost.BlockedFailureCode;
                        P16AzadeganReadOnlyResultHost.ExecuteRead(dataset, project, request, evidence);
                    }
                    else if (request != null && request.Operation == P16AzadeganGvfReadbackHost.Operation)
                    {
                        readConduitDatum();
                        stage = P16AzadeganGvfReadbackHost.BlockedFailureCode;
                        P16AzadeganGvfReadbackHost.ExecuteRead(dataset, project, request, evidence);
                    }
                    else if (request != null && request.Operation == P16AzadeganCalculationHost.Operation)
                    {
                        readConduitDatum();
                        stage = P16AzadeganCalculationHost.BlockedFailureCode;
                        if (request.Diagnostics.TryGetValue(P16AzadeganCalculationHost.PhaseKey, out string azadeganCalcPhase) &&
                            azadeganCalcPhase == P16AzadeganCalculationHost.PhaseRead)
                            P16AzadeganCalculationHost.ExecuteRead(dataset, project, request, evidence);
                        else
                            P16AzadeganCalculationHost.ExecuteCalculate(dataset, ui, project, request, evidence);
                    }
                    else if (request != null && request.Operation == GeneralModelCalculationHost.Operation)
                    {
                        readConduitDatum();
                        stage = GeneralModelCalculationHost.BlockedFailureCode;
                        // The launching service gives this process a private scratch root through
                        // TEMP/TMP. Record what it resolved to; the working-copy boundary itself is
                        // validated against the carried trusted root, never against this value.
                        evidence.Diagnostics[GeneralModelCalculationHost.HostTemporaryRootKey] = Path.GetTempPath();
                        if (request.Diagnostics.TryGetValue(GeneralModelCalculationHost.PhaseKey, out string generalPhase) &&
                            generalPhase == GeneralModelCalculationHost.PhaseRead)
                            GeneralModelCalculationHost.ExecuteRead(dataset, project, request, evidence);
                        else if (generalPhase == GeneralModelCalculationHost.PhaseDiscover)
                            GeneralModelCalculationHost.ExecuteDiscover(dataset, project, request, evidence);
                        else if (generalPhase == GeneralModelCalculationHost.PhaseBind)
                            GeneralModelCalculationHost.ExecuteBind(dataset, project, request, evidence);
                        else if (generalPhase == GeneralModelCalculationHost.PhaseVerify)
                            GeneralModelCalculationHost.ExecuteVerify(dataset, project, request, evidence);
                        else
                            GeneralModelCalculationHost.ExecuteCalculate(dataset, ui, project, request, evidence);
                    }
                    else if (request != null && request.Operation == P16FarzaneganNativeReadHost.Operation)
                    {
                        readConduitDatum();
                        stage = "FARZANEGAN_NATIVE_READBACK_BLOCKED";
                        P16FarzaneganNativeReadHost.Execute(dataset, project, request, evidence);
                    }
                    else if (request != null && request.Operation == P14CatalogSelectorDiscoveryHost.Operation)
                    {
                        readConduitDatum();
                        stage = "P14_CATALOG_SELECTOR_PROBE_BLOCKED";
                        P14CatalogSelectorDiscoveryHost.Execute(dataset, project, request, evidence);
                    }
                    else if (request != null && P14FarzaneganTestOnlyHost.IsPilot(request.Operation))
                    {
                        readConduitDatum();
                        stage = "FARZANEGAN_NATIVE_PILOT_BLOCKED";
                        P16FarzaneganNativePilotHost.Execute(dataset, project, request, evidence);
                    }
                    else if (request != null && request.Operation == P16AzadeganSubEngineProbeHost.Operation)
                    {
                        readConduitDatum();
                        stage = P16AzadeganSubEngineProbeHost.BlockedFailureCode;
                        P16AzadeganSubEngineProbeHost.ExecuteRead(dataset, project, request, evidence);
                    }
                    else if (request != null && request.Operation == P15TechnicalCapabilityHost.Operation)
                    {
                        readConduitDatum();
                        stage = "P15_TECH_BLOCKED_RUNTIME";
                        if (request.Diagnostics.TryGetValue("P15TechnicalPhase", out string phase) && phase == "REOPEN")
                            P15TechnicalCapabilityHost.ExecuteReopenAndSolver(dataset, ui, project, request, evidence);
                        else
                            P15TechnicalCapabilityHost.ExecuteWrite(dataset, ui, project, request, evidence);
                    }
                    else if (request != null && request.Operation == RuntimeHostProtocol.OpScenarioAlternativeIsolation)
                    {
                        stage = "ScenarioAlternativeIsolationFailure";
                        ScenarioAlternativeIsolationHost.ExecuteHost1Write(dataset, ui, project, request, evidence);
                    }
                    else if (request != null && request.Operation == RuntimeHostProtocol.OpVerifyScenarioAlternativeIsolation)
                    {
                        readConduitDatum();
                        stage = "VerifyScenarioAlternativeIsolationFailure";
                        ScenarioAlternativeIsolationHost.ExecuteHost2ReopenVerification(dataset, ui, project, request, evidence);
                    }
                    else if (request != null && request.Operation == GravityWriteAndSolverHost.ProductionOperation)
                    {
                        stage = "P15_BLOCKED_PRODUCTION_RUNTIME";
                        GravityWriteAndSolverHost.ExecuteProduction(dataset, ui, project, request, evidence);
                    }
                    else if (request != null && request.Operation == RuntimeHostProtocol.OpGravityWriteAndSolver)
                    {
                        readConduitDatum();
                        stage = "GravityWriteAndSolverFailure";
                        GravityWriteAndSolverHost.ExecuteHost1(dataset, ui, project, request, evidence);
                    }
                    else if (request != null && request.Operation == RuntimeHostProtocol.OpVerifyGravityWriteAndSolver)
                    {
                        readConduitDatum();
                        stage = "VerifyGravityWriteAndSolverFailure";
                        GravityWriteAndSolverHost.ExecuteHost2Reopen(dataset, ui, project, request, evidence);
                    }
                    else
                    {
                        readConduitDatum();
                        var p6aMap = RuntimeStageJournal.Call("P6ACapabilityMapping", () => ScenarioAlternativeCapabilityMapper.Map(dataset, project, ui, parent));
                        evidence.Diagnostics["P6ACapabilityMapJson"] = RuntimeHostProtocol.Serializer().Serialize(p6aMap);
                        var p12aResult = RuntimeStageJournal.Call("P12ANetworkReadMapping", () => GravityNetworkReadMapper.Map(dataset, project, ui, parent, p6aMap));
                        evidence.Diagnostics["P12ANetworkReadJson"] = RuntimeHostProtocol.Serializer().Serialize(p12aResult);
                        var p15aResult = RuntimeStageJournal.Call("P15AWriteAndSolverMapping", () => GravityWriteAndSolverCapabilityMapper.Map(dataset, project, ui, parent));
                        evidence.Diagnostics["P15ACapabilityMapJson"] = RuntimeHostProtocol.Serializer().Serialize(p15aResult);
                    }
                }, () =>
                {
                    RuntimeStageJournal.Call("VendorCleanupEntered", () => { });
                    var errors = new List<string>();
                    Action<Action> close = action => { try { action(); } catch (Exception error) { errors.Add(error.ToString()); } };
                    if (project == null && ui != null && openAttempted)
                        close(() => project = uiType.GetProperty("CurrentGraphicalProject").GetValue(ui, null));
                    if (project != null) close(() => Invoke(project, Method(VendorType("Haestad.Framework", "Haestad.Framework.Application.ProjectBase"), "MakeClean")));
                    if (parent != null && openAttempted) close(() => Invoke(parent, Method(frameworkParent, "CloseCurrentProject", properties), Construct(properties, Type.EmptyTypes)));
                    if (solverLicense != null) close(solverLicense.Dispose);
                    if (ui != null) close(() => Invoke(ui, Method(productUI, "CloseParentFormUIModel")));
                    foreach (object value in new[] { ui, parent, application })
                        if (value is IDisposable) close(() => RuntimeStageJournal.Call("Dispose:" + value.GetType().FullName, () => ((IDisposable)value).Dispose()));
                    evidence.CloseCompleted = errors.Count == 0;
                    if (evidence.CloseCompleted) RuntimeStageJournal.Call("VendorCleanupCompleted", () => { });
                    if (errors.Count != 0) { evidence.FailureCode = "CloseFailure"; evidence.Diagnostics["CloseExceptions"] = string.Join("\n", errors); }
                });
            }
            catch (Exception error)
            { if (evidence.FailureCode == null) evidence.FailureCode = stage; evidence.Exception = error.ToString(); }
            finally
            {
                try { if (owner != null) RuntimeStageJournal.Call("HiddenOwnerDisposal", () => owner.Dispose()); }
                catch (Exception error) { evidence.CloseCompleted = false; evidence.FailureCode = "CloseFailure"; evidence.Exception = (evidence.Exception ?? "") + "\n" + error; }
                try
                {
                    var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                    var companionRoots = AssemblyProvenanceDiagnostics.DiscoverCompanionRoots(loadedAssemblies);
                    provenance.RecordCompanionRoots(root, companionRoots);
                    provenance.Snapshot(loadedAssemblies, root, companionRoots);
                    foreach (var assembly in loadedAssemblies)
                    {
                        if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location)) continue;
                        string path = Path.GetFullPath(assembly.Location);
                        bool vendor = assembly.GetName().Name.StartsWith("Haestad.", StringComparison.Ordinal) || assembly.GetName().Name.StartsWith("Bentley.", StringComparison.Ordinal);
                        bool isUnderProduct = AssemblyProvenanceDiagnostics.ContainsPath(root, path);
                        bool isUnderCompanion = companionRoots.Any(cr => AssemblyProvenanceDiagnostics.ContainsPath(cr.Root, path));

                        if (isUnderProduct || isUnderCompanion)
                        {
                            if (vendor || isUnderProduct)
                                evidence.LoadedAssemblies.Add(WorkingCopyIntegrity.Fingerprint(path));
                        }
                        else if (vendor)
                        {
                            evidence.FailureCode = "VendorDependencyOutsideValidatedRoots";
                            evidence.Diagnostics["BindingFailure"] = "Vendor dependency outside validated product and companion roots.";
                            provenance.Reject(assembly, root, companionRoots, path, vendor);
                        }
                    }
                }
                finally
                {
                    AppDomain.CurrentDomain.AssemblyResolve -= resolver;
                    try
                    {
                        if (!SetDllDirectory(priorDllDirectory.Length == 0 ? null : priorDllDirectory.ToString()))
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    }
                    finally { Environment.CurrentDirectory = priorDirectory; }
                }
            }
        }

        private static object HiddenOwner(Type uiInterface, Form owner)
        {
            var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("SewerPilot.HiddenOwner"), AssemblyBuilderAccess.Run);
            var type = assembly.DefineDynamicModule("Owner").DefineType("SewerPilot.Internal.HiddenOwner", TypeAttributes.Public | TypeAttributes.Sealed);
            type.AddInterfaceImplementation(uiInterface);
            var field = type.DefineField("owner", typeof(object), FieldAttributes.Private);
            var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new[] { typeof(object) });
            var il = constructor.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
            foreach (string name in new[] { "get_OwnerWindow", "Open", "ModelessFormClosing" })
            {
                var contract = uiInterface.GetMethod(name);
                var implementation = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final,
                    contract.ReturnType, contract.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
                il = implementation.GetILGenerator();
                if (name == "get_OwnerWindow") { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Castclass, contract.ReturnType); }
                il.Emit(OpCodes.Ret); type.DefineMethodOverride(implementation, contract);
            }
            return Activator.CreateInstance(type.CreateType(), owner);
        }
    }
}
