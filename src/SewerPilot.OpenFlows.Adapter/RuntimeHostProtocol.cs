using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    internal sealed class HostMetadata
    {
        public HostMetadata() { }
        public string Path { get; set; }
        public string SimpleName { get; set; }
        public string SHA256 { get; set; }
        public string AssemblyVersion { get; set; }
        public string FileVersion { get; set; }
        public string ProductVersion { get; set; }
        public string ImageRuntimeVersion { get; set; }
        public string TargetFramework { get; set; }
        public string Architecture { get; set; }
        public uint CorFlags { get; set; }
        internal OpenFlowsAssemblyMetadata ToMetadata() => new OpenFlowsAssemblyMetadata(Path, SimpleName, SHA256,
            AssemblyVersion, FileVersion, ProductVersion, ImageRuntimeVersion, TargetFramework, Architecture, CorFlags);
        internal static HostMetadata From(OpenFlowsAssemblyMetadata value) => new HostMetadata {
            Path = value.Path, SimpleName = value.SimpleName, SHA256 = value.SHA256, AssemblyVersion = value.AssemblyVersion,
            FileVersion = value.FileVersion, ProductVersion = value.ProductVersion, ImageRuntimeVersion = value.ImageRuntimeVersion,
            TargetFramework = value.TargetFramework, Architecture = value.Architecture, CorFlags = value.CorFlags };
    }
    internal sealed class HostFingerprint
    {
        public HostFingerprint() { }
        public string Path { get; set; }
        public string SHA256 { get; set; }
    }
    internal sealed class HostCompanionRoot
    {
        public HostCompanionRoot() { }
        public string Root { get; set; }
        public string RootKind { get; set; }
        public string DiscoverySource { get; set; }
        public string AnchorAssembly { get; set; }
        public string AnchorFullName { get; set; }
        public string AnchorLocation { get; set; }
        public bool Exists { get; set; }
        public string ValidationResult { get; set; }
        public string AnchorVersion { get; set; }
        public string AnchorPublicKeyToken { get; set; }
        public string AnchorFileVersion { get; set; }
        public string AnchorCompanyName { get; set; }
        public string AnchorProductName { get; set; }
    }
    internal sealed class HostRequest
    {
        public HostRequest() { }
        public int SchemaVersion { get; set; }
        public string RunId { get; set; }
        public string Operation { get; set; }
        public string WorkingModelPath { get; set; }
        public int RuntimeCandidateCount { get; set; }
        public string SelectedCandidate { get; set; }
        public string SelectedRuntime { get; set; }
        public string DetectedProduct { get; set; }
        public string DetectedVersion { get; set; }
        public string DetectedBuild { get; set; }
        public string DetectedArchitecture { get; set; }
        public HostMetadata[] CoreFiles { get; set; }
        public string WriteAuthorizationTokenId { get; set; }
        public string WriteAuthorizationScope { get; set; }
        public string WriteAuthorizationTargetWorkingPath { get; set; }
        public string WorkingCopyId { get; set; }
        // The application temporary root a controlled working copy is measured against. It is carried
        // explicitly because the launching service redirects this process's own TEMP/TMP to a private
        // engine scratch root. It is never trusted on its own: the host re-derives the anchor from the
        // user profile and refuses any value that is not canonically identical to it.
        public string TrustedTempRoot { get; set; }
        public string SourceBaselineFingerprint { get; set; }
        public GravityExecutionPlan EngineeringPlan { get; set; }
        public Dictionary<string, string> Diagnostics { get; set; } = new Dictionary<string, string>();
    }
    internal sealed class HostResult
    {
        public HostResult() { }
        // In-process completion receipt; never serialized or accepted from an IPC JSON property.
        internal P16ResultContentAuthority ContentAuthority { get; set; }
        public int SchemaVersion { get; set; }
        public string RunId { get; set; }
        public bool Success { get; set; }
        public string FailureClassification { get; set; }
        public string FailureMessageSafe { get; set; }
        public int RuntimeCandidateCount { get; set; }
        public string SelectedRuntime { get; set; }
        public string DetectedProduct { get; set; }
        public string DetectedVersion { get; set; }
        public string DetectedBuild { get; set; }
        public string DetectedArchitecture { get; set; }
        public bool RuntimeBinding { get; set; }
        public HostMetadata[] BoundCoreFiles { get; set; }
        public bool ApplicationDescriptionCreated { get; set; }
        public bool ApplicationModelCreated { get; set; }
        public bool ParentUiModelCreated { get; set; }
        public bool LifecycleInitialized { get; set; }
        public bool ModelOpen { get; set; }
        public bool CurrentGraphicalProjectEstablished { get; set; }
        public bool DomainProjectEstablished { get; set; }
        public bool DomainDataSetEstablished { get; set; }
        public string ElementType { get; set; }
        public string ManagerRuntimeType { get; set; }
        public string ElementIdsDeclaringType { get; set; }
        public string ElementIdsRuntimeType { get; set; }
        public string CollectionRuntimeType { get; set; }
        public string IndexerDescription { get; set; }
        public string DatumName { get; set; }
        public int? DatumValue { get; set; }
        public int SaveInvocationCount { get; set; }
        public int SaveAsInvocationCount { get; set; }
        public int MutationInvocationCount { get; set; }
        public int ScenarioCreateInvocationCount { get; set; }
        public int AlternativeCreateInvocationCount { get; set; }
        public int SolverRunInvocationCount { get; set; }
        public bool VendorCleanupCompleted { get; set; }
        public bool StaCompleted { get; set; }
        public bool ResultCommitted { get; set; }
        public string RequestedExitMode { get; set; }
        public Dictionary<string, string> Diagnostics { get; set; } = new Dictionary<string, string>();
        public HostFingerprint[] LoadedAssemblies { get; set; } = new HostFingerprint[0];
        public string PrimaryProductRoot { get; set; }
        public HostCompanionRoot[] CompanionRoots { get; set; } = new HostCompanionRoot[0];
    }

    internal static class RuntimeHostProtocol
    {
        internal const string Operation = "ReadFirstConduitElementId";
        internal const string OpScenarioAlternativeIsolation = "ScenarioAlternativeIsolation";
        internal const string OpVerifyScenarioAlternativeIsolation = "VerifyScenarioAlternativeIsolation";
        internal const string OpGravityWriteAndSolver = "GravityWriteAndSolver";
        internal const string OpVerifyGravityWriteAndSolver = "VerifyGravityWriteAndSolver";
        internal const string ExitMode = "ControlledEnvironmentExitAfterCleanup";
        internal static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        internal static void ControlledDirectory(string path, string category) =>
            ControlledDirectory(path, category, null);

        // The controlled boundary is measured against the trusted application root, never against this
        // process's own temporary environment, so redirecting the Bentley scratch location for one
        // child cannot widen or move it. The engine scratch category is refused outright: a scratch
        // tree is never a working-copy location and cannot stand in for one.
        internal static void ControlledDirectory(string path, string category, string trustedRoot) =>
            RequireUnderRoot(path, category, SewerPilotTemporary.TrustedRoot(trustedRoot));

        // SewerPilot's own control channel is not a working copy and has no carried root to consult:
        // it is always measured against the application anchor, so a redirected child still validates
        // it correctly and the scratch root can never stand in for it.
        internal static void AnchoredDirectory(string path, string category) =>
            RequireUnderRoot(path, category, SewerPilotTemporary.Anchor());

        // The reference-write contract. A correction may only touch fields in the published allowlist,
        // may write at most three of them on the terrain element and at most two per two-dimensional
        // data source, and must publish a total that agrees with its own per-element tallies. Nothing
        // here trusts the phase's arithmetic: the identities, the field names and the counts are
        // cross-checked against each other, so widening one number cannot widen the contract.
        internal static int ValidateReferenceWrites(Dictionary<string, string> diagnostics)
        {
            if (diagnostics == null)
                throw new InvalidDataException("Reference rebinding published no diagnostics at all.");
            string countText;
            if (!diagnostics.TryGetValue("GeneralModelTerrainBindWrites", out countText) ||
                !int.TryParse(countText, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int writeCount))
                throw new InvalidDataException("Reference rebinding published no reference-write count.");

            string fieldsText;
            diagnostics.TryGetValue(GeneralModelCalculationHost.ReferenceFieldsKey, out fieldsText);
            string[] fields = (fieldsText ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string field in fields)
            {
                if (!GeneralModelCalculationHost.ReferenceFieldAllowlist.Contains(field, StringComparer.Ordinal))
                    throw new InvalidDataException(
                        "Reference rebinding wrote a field outside the reference allowlist: " + field + ".");
            }

            string elementsText;
            diagnostics.TryGetValue(GeneralModelCalculationHost.ReferenceElementsKey, out elementsText);
            string[] elements = (elementsText ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            int terrain = 0, sources = 0, declared = 0;
            foreach (string element in elements)
            {
                string[] parts = element.Split(':');
                if (parts.Length != 3 || !int.TryParse(parts[2], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int writes) || writes <= 0)
                    throw new InvalidDataException(
                        "Reference rebinding published an unreadable element tally: " + element + ".");
                if (parts[0] == "Terrain")
                {
                    terrain++;
                    if (writes > 3)
                        throw new InvalidDataException("Reference rebinding wrote " + writes +
                            " terrain fields; at most three are permitted.");
                }
                else if (parts[0] == "TwoDDataSource")
                {
                    sources++;
                    if (writes > 2)
                        throw new InvalidDataException("Reference rebinding wrote " + writes +
                            " fields on one two-dimensional data source; at most two are permitted.");
                }
                else
                {
                    throw new InvalidDataException(
                        "Reference rebinding reported an element it is not allowed to write: " + parts[0] + ".");
                }
                declared += writes;
            }
            if (terrain > 1)
                throw new InvalidDataException("Reference rebinding reported more than one terrain element.");
            if (declared != writeCount)
                throw new InvalidDataException("Reference rebinding reported " + writeCount +
                    " writes but its element tallies sum to " + declared + ".");
            int budget = 3 + 2 * sources;
            if (writeCount < 1 || writeCount > budget)
                throw new InvalidDataException("Reference rebinding reported " + writeCount +
                    " writes, outside the permitted 1-" + budget + " range.");

            string discoveredText;
            if (diagnostics.TryGetValue("GeneralModelDataSourceElements", out discoveredText) &&
                int.TryParse(discoveredText, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int discovered) && sources > discovered)
                throw new InvalidDataException(
                    "Reference rebinding wrote more data sources than the model exposes.");
            return writeCount;
        }

        private static void RequireUnderRoot(string path, string category, string root)
        {
            if (string.IsNullOrWhiteSpace(category) || category == SewerPilotTemporary.EngineCategory)
                throw new InvalidDataException("An engine scratch root is never a controlled working directory.");
            WorkingCopyIntegrity.RejectLinks(path);
            string directory = System.IO.Path.GetFullPath(path).TrimEnd('\\');
            if (SewerPilotTemporary.IsEngineScratch(directory, root))
                throw new InvalidDataException("An engine scratch root is never a controlled working directory.");
            string expected = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, SewerPilotTemporary.Folder,
                SewerPilotTemporary.Product, category)).TrimEnd('\\');
            // Compared canonically: an 8.3 spelling and its long form name the same directory, and a
            // boundary check must not depend on which spelling the caller happened to use.
            if (!Regex.IsMatch(System.IO.Path.GetFileName(directory), "^[a-f0-9]{32}$") ||
                !string.Equals(SewerPilotTemporary.Canonical(System.IO.Path.GetDirectoryName(directory)),
                    SewerPilotTemporary.Canonical(expected), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Directory is outside the controlled per-run boundary.");
        }
        internal static void ValidateRequest(HostRequest request, bool internalP15Acceptance = false, SingleOperatorExecutionPermit singleOperatorPermit = null, bool internalP16Diagnostic = false, bool internalP16Proof = false, bool internalP16Secondary = false, bool internalP16Azadegan = false, bool internalP16AzadeganCalculation = false, bool internalP16SubEngineProbe = false, bool internalFarzaneganPilot = false, bool internalFarzaneganRead = false, bool internalP14CatalogProbe = false, bool internalGeneralModelCalculation = false)
        {
            if (request?.Operation == GravityWriteAndSolverHost.ProductionOperation)
                GravityExecutionAuthorization.Require(request, singleOperatorPermit);
            if (request == null || request.SchemaVersion != 1 ||
                (request.Operation != Operation && request.Operation != OpScenarioAlternativeIsolation && request.Operation != OpVerifyScenarioAlternativeIsolation &&
                 request.Operation != OpVerifyGravityWriteAndSolver && request.Operation != GravityWriteAndSolverHost.ProductionOperation &&
                 !(internalP15Acceptance && request.Operation == P15TechnicalCapabilityHost.Operation) &&
                 !(internalP16Diagnostic && (request.Operation == P16FlowDiagnostic.Operation || request.Operation == P16CalculatedFlowDiagnostic.Operation)) &&
                 !(internalP16Proof && request.Operation == P16HeadlessResultProofHost.Operation) &&
                 !(internalP16Secondary && request.Operation == P16SecondaryMetricsHost.Operation) &&
                 !(internalP16Azadegan && (request.Operation == P16AzadeganReadOnlyResultHost.Operation ||
                                           request.Operation == P16AzadeganGvfReadbackHost.Operation)) &&
                 !(internalP16AzadeganCalculation && request.Operation == P16AzadeganCalculationHost.Operation) &&
                 !(internalP16SubEngineProbe && request.Operation == P16AzadeganSubEngineProbeHost.Operation) &&
                 !(internalFarzaneganPilot && P14FarzaneganTestOnlyHost.IsPilot(request.Operation)) &&
                 !(internalFarzaneganRead && request.Operation == P16FarzaneganNativeReadHost.Operation) &&
                 !(internalGeneralModelCalculation && request.Operation == GeneralModelCalculationHost.Operation) &&
                 !(internalP14CatalogProbe && request.Operation == P14CatalogSelectorDiscoveryHost.Operation)) ||
                !Regex.IsMatch(request.RunId ?? "", "^[a-f0-9]{32}$") || request.RuntimeCandidateCount < 1 || request.DetectedArchitecture != "x64")
                throw new InvalidDataException("Unsupported or incomplete RuntimeHost request.");
            if (!System.IO.Path.IsPathRooted(request.SelectedRuntime ?? "") || !Directory.Exists(request.SelectedRuntime))
                throw new InvalidDataException("Selected runtime root is unavailable.");
            WorkingCopyIntegrity.RejectLinks(request.SelectedRuntime);
            if (!System.IO.Path.IsPathRooted(request.WorkingModelPath ?? "") || !File.Exists(request.WorkingModelPath) ||
                !request.WorkingModelPath.EndsWith(".stsw", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Controlled working model is unavailable.");
            // The terrain discovery pass reads declared metadata only and never persists, so it is
            // confined to the read-only category exactly like the model-read path. Rebinding and its
            // verification act on the persisted working copy, so they stay in the working category.
            bool generalDiscover = internalGeneralModelCalculation &&
                request.Operation == GeneralModelCalculationHost.Operation && request.Diagnostics != null &&
                request.Diagnostics.TryGetValue(GeneralModelCalculationHost.PhaseKey, out string categoryPhase) &&
                categoryPhase == GeneralModelCalculationHost.PhaseDiscover;
            string category = (request.Operation == OpScenarioAlternativeIsolation || request.Operation == OpVerifyScenarioAlternativeIsolation ||
                               request.Operation == OpGravityWriteAndSolver || request.Operation == OpVerifyGravityWriteAndSolver || request.Operation == GravityWriteAndSolverHost.ProductionOperation ||
                               (internalP15Acceptance && request.Operation == P15TechnicalCapabilityHost.Operation) ||
                               (internalP16Diagnostic && request.Operation == P16CalculatedFlowDiagnostic.Operation) ||
                               (internalP16Proof && request.Operation == P16HeadlessResultProofHost.Operation) ||
                               (internalP16Secondary && request.Operation == P16SecondaryMetricsHost.Operation) ||
                               (internalP16AzadeganCalculation && request.Operation == P16AzadeganCalculationHost.Operation) ||
                               (internalGeneralModelCalculation && request.Operation == GeneralModelCalculationHost.Operation && !generalDiscover) ||
                               (internalFarzaneganPilot && P14FarzaneganTestOnlyHost.IsPilot(request.Operation)))
                ? "Working" : "ReadOnly";
            ControlledDirectory(System.IO.Path.GetDirectoryName(request.WorkingModelPath), category, request.TrustedTempRoot);
            WorkingCopyIntegrity.RejectLinks(request.WorkingModelPath);
            if (request.CoreFiles == null || request.CoreFiles.Length != 2 ||
                !OpenFlowsRuntimeCandidate.CoreAssemblyNames.All(name => request.CoreFiles.Count(item => item.SimpleName == name) == 1))
                throw new InvalidDataException("Exactly the two P3 core identities are required.");
            foreach (var core in request.CoreFiles)
            {
                string expected = System.IO.Path.Combine(System.IO.Path.GetFullPath(request.SelectedRuntime), core.SimpleName + ".dll");
                if (!string.Equals(System.IO.Path.GetFullPath(core.Path), expected, StringComparison.OrdinalIgnoreCase) ||
                    !Regex.IsMatch(core.SHA256 ?? "", "^[A-F0-9]{64}$")) throw new InvalidDataException("Core inventory escaped the selected runtime.");
                WorkingCopyIntegrity.RejectLinks(core.Path);
            }
            if (request.Operation == P15TechnicalCapabilityHost.Operation)
                P15TechnicalCapabilityHost.ValidateRequest(request);
            if (request.Operation == GravityWriteAndSolverHost.ProductionOperation)
                GravityWriteAndSolverHost.ValidateProductionRequest(request, singleOperatorPermit);
            if (request.Operation == P16FlowDiagnostic.Operation)
                P16FlowDiagnostic.ValidateRequest(request);
            if (request.Operation == P16CalculatedFlowDiagnostic.Operation)
                P16CalculatedFlowDiagnostic.ValidateRequest(request);
            if (request.Operation == P16HeadlessResultProofHost.Operation)
                P16HeadlessResultProofHost.ValidateRequest(request);
            if (request.Operation == P16SecondaryMetricsHost.Operation)
                P16SecondaryMetricsHost.ValidateRequest(request);
            if (request.Operation == P16AzadeganReadOnlyResultHost.Operation)
                P16AzadeganReadOnlyResultHost.ValidateRequest(request);
            if (request.Operation == P16AzadeganGvfReadbackHost.Operation)
                P16AzadeganGvfReadbackHost.ValidateRequest(request);
            if (request.Operation == P16AzadeganCalculationHost.Operation)
                P16AzadeganCalculationHost.ValidateRequest(request);
            if (request.Operation == P16AzadeganSubEngineProbeHost.Operation)
                P16AzadeganSubEngineProbeHost.ValidateRequest(request);
            if (P14FarzaneganTestOnlyHost.IsPilot(request.Operation))
                P16FarzaneganNativePilotHost.ValidateRequest(request);
            if (request.Operation == P16FarzaneganNativeReadHost.Operation)
                P16FarzaneganNativeReadHost.ValidateRequest(request);
            if (request.Operation == P14CatalogSelectorDiscoveryHost.Operation)
                P14CatalogSelectorDiscoveryHost.ValidateRequest(request);
            if (request.Operation == GeneralModelCalculationHost.Operation)
                GeneralModelCalculationHost.ValidateRequest(request);
        }
        internal static void AtomicWrite(string path, object value)
        {
            string temporary = path + ".tmp";
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(file, new UTF8Encoding(false), 1024, true))
                { writer.Write(Serializer().Serialize(value)); writer.Flush(); }
                file.Flush(true);
            }
            File.Move(temporary, path); // Same-directory atomic commit; never overwrite.
        }
        internal static HostResult ParseResult(string path, HostRequest request)
        {
            if (!File.Exists(path)) throw new InvalidDataException("RuntimeHost result is missing.");
            WorkingCopyIntegrity.RejectLinks(path);
            if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("RuntimeHost result exceeds its bound.");
            HostResult result;
            try
            {
                string json = File.ReadAllText(path);
                var fields = Serializer().DeserializeObject(json) as Dictionary<string, object>;
                if (fields == null || typeof(HostResult).GetProperties().Any(property => !fields.ContainsKey(property.Name) ||
                    (property.PropertyType == typeof(bool) && !(fields[property.Name] is bool))))
                    throw new InvalidDataException("Partial or wrongly typed result.");
                result = Serializer().Deserialize<HostResult>(json);
            }
            catch (Exception error) { throw new InvalidDataException("Malformed RuntimeHost result: " + error.GetType().Name); }
            if (result == null || result.SchemaVersion != 1 || result.RunId != request.RunId ||
                result.SelectedRuntime != request.SelectedRuntime || result.RuntimeCandidateCount != request.RuntimeCandidateCount ||
                result.DetectedArchitecture != "x64" || !result.ResultCommitted || result.Diagnostics == null || result.LoadedAssemblies == null ||
                result.CompanionRoots == null)
                throw new InvalidDataException("Unsupported, mismatched or uncommitted RuntimeHost result.");
            if (result.Success && (!result.RuntimeBinding || !result.ApplicationDescriptionCreated || !result.ApplicationModelCreated ||
                !result.ParentUiModelCreated || !result.LifecycleInitialized || !result.ModelOpen || !result.CurrentGraphicalProjectEstablished ||
                !result.DomainProjectEstablished || !result.DomainDataSetEstablished ||
                !result.VendorCleanupCompleted || !result.StaCompleted ||
                result.RequestedExitMode != ExitMode || result.FailureClassification != null))
                throw new InvalidDataException("Success result lacks complete cleanup, STA, or lifecycle evidence.");

            if (request.Operation == OpScenarioAlternativeIsolation)
            {
                if (result.Success && (result.SaveInvocationCount != 1 || result.SaveAsInvocationCount != 0 ||
                    result.ScenarioCreateInvocationCount != 1 || result.AlternativeCreateInvocationCount != 1 ||
                    result.MutationInvocationCount != 0 || result.SolverRunInvocationCount != 0))
                    throw new InvalidDataException("ScenarioAlternativeIsolation result violates required exact write counts.");
            }
            else if (request.Operation == OpVerifyScenarioAlternativeIsolation)
            {
                if (result.Success && (result.DatumName != "FirstConduitElementID" || !result.DatumValue.HasValue ||
                    result.SaveInvocationCount != 0 || result.SaveAsInvocationCount != 0 ||
                    result.ScenarioCreateInvocationCount != 0 || result.AlternativeCreateInvocationCount != 0 ||
                    result.MutationInvocationCount != 0 || result.SolverRunInvocationCount != 0))
                    throw new InvalidDataException("VerifyScenarioAlternativeIsolation result violates zero-write or datum requirements.");
            }
            else if (request.Operation == P15TechnicalCapabilityHost.Operation)
            {
                if (result.Success && (result.SaveInvocationCount != 1 || result.SaveAsInvocationCount != 0 ||
                    result.ScenarioCreateInvocationCount != 1 || result.AlternativeCreateInvocationCount != 1 ||
                    !P15TechnicalCapabilityHost.ValidWriteCounts(result.Diagnostics, result.MutationInvocationCount) || result.SolverRunInvocationCount != 1 ||
                    !result.Diagnostics.TryGetValue("WriteCapabilityProven", out string writeProof) || writeProof != "true" ||
                    !result.Diagnostics.TryGetValue("SolverStatusSuccess", out string solverProof) || solverProof != "true"))
                    throw new InvalidDataException("Internal P15 technical result lacks exact write, persistence or solver proof.");
            }
            else if (request.Operation == GravityWriteAndSolverHost.ProductionOperation)
            {
                if (result.Success) GravityWriteAndSolverHost.ValidateProductionResult(request, result);
            }
            else if (request.Operation == P16HeadlessResultProofHost.Operation)
            {
                if (result.Success) P16ResultContentAuthority.ValidateCompletedProof(request, result);
            }
            else if (request.Operation == OpGravityWriteAndSolver)
            {
                if (result.Success && (result.SaveInvocationCount != 1 || result.SaveAsInvocationCount != 0 ||
                    result.ScenarioCreateInvocationCount != 1 || result.AlternativeCreateInvocationCount != 1 ||
                    result.MutationInvocationCount != 4 || result.SolverRunInvocationCount != 1 ||
                    !result.Diagnostics.TryGetValue("LocalizationPropertySetCount", out string localizationCount) || localizationCount != "2" ||
                    !result.Diagnostics.TryGetValue("EndpointInvertPropertySetCount", out string endpointCount) || endpointCount != "2" ||
                    !result.Diagnostics.TryGetValue("LocalizationReadbackVerified", out string localizationReadback) || localizationReadback != "true"))
                    throw new InvalidDataException("GravityWriteAndSolver result violates required exact write/solve counts.");
            }
            else if (request.Operation == OpVerifyGravityWriteAndSolver)
            {
                if (result.Success && (result.DatumName != "FirstConduitElementID" || !result.DatumValue.HasValue ||
                    result.SaveInvocationCount != 0 || result.SaveAsInvocationCount != 0 ||
                    result.ScenarioCreateInvocationCount != 0 || result.AlternativeCreateInvocationCount != 0 ||
                    result.MutationInvocationCount != 0 || result.SolverRunInvocationCount != 0))
                    throw new InvalidDataException("VerifyGravityWriteAndSolver result violates zero-write or datum requirements.");
            }
            else if (request.Operation == P16FarzaneganNativeReadHost.Operation)
            {
                if (result.Success && (result.SaveInvocationCount != 0 || result.SolverRunInvocationCount != 0 ||
                    result.SaveAsInvocationCount != 0 || result.MutationInvocationCount != 0 ||
                    result.ScenarioCreateInvocationCount != 0 || result.AlternativeCreateInvocationCount != 0 ||
                    !result.Diagnostics.TryGetValue(P16FarzaneganNativeReadHost.PassFlag, out string readPass) || readPass != "true" ||
                    !result.Diagnostics.TryGetValue("FarzaneganPersistedInventoryMatches", out string hashes) || hashes != "true" ||
                    !result.Diagnostics.TryGetValue("P16OpenQuestionPolicyClean", out string questions) || questions != "true"))
                    throw new InvalidDataException("Farzanegan independent read lacks zero-write and native hash evidence.");
            }
            else if (request.Operation == P14CatalogSelectorDiscoveryHost.Operation)
            {
                // Read-only qualification probe: the answer may be negative, but it may never be
                // produced by a write, a save or a solver invocation.
                if (result.Success && (result.SaveInvocationCount != 0 || result.SolverRunInvocationCount != 0 ||
                    result.SaveAsInvocationCount != 0 || result.MutationInvocationCount != 0 ||
                    result.ScenarioCreateInvocationCount != 0 || result.AlternativeCreateInvocationCount != 0 ||
                    !result.Diagnostics.TryGetValue("P14CatalogSelectorProbeCompleted", out string probeDone) || probeDone != "true"))
                    throw new InvalidDataException("Catalog selector probe violated its zero-write read-only contract.");
            }
            else if (P14FarzaneganTestOnlyHost.IsPilot(request.Operation))
            {
                if (P14CatalogPointerPersistenceHost.IsOperation(request.Operation))
                {
                    if (result.Success && (result.SaveInvocationCount != 1 || result.SolverRunInvocationCount != 0 ||
                        result.SaveAsInvocationCount != 0 || result.MutationInvocationCount != P14CatalogPointerPersistenceHost.ExpectedMutations(request.Operation) ||
                        result.ScenarioCreateInvocationCount != 0 || result.AlternativeCreateInvocationCount != 0 ||
                        !result.Diagnostics.TryGetValue(P14CatalogPointerPersistenceHost.PassFlag, out string trialPass) || trialPass != "true" ||
                        !result.Diagnostics.TryGetValue(P16FarzaneganNativePilotHost.PassFlag, out string pilotPass) || pilotPass != "true"))
                        throw new InvalidDataException("Catalog-pointer persistence trial violated its exact two-mutation, one-save, zero-solver contract.");
                }
                else if (result.Success && (result.SaveInvocationCount != 1 || result.SolverRunInvocationCount != 1 ||
                    result.SaveAsInvocationCount != 0 || result.MutationInvocationCount != FarzaneganHydraulicRemediationHost.ExpectedWrites(request.Operation) ||
                    result.ScenarioCreateInvocationCount != 0 || result.AlternativeCreateInvocationCount != 0 ||
                    !result.Diagnostics.TryGetValue(P16FarzaneganNativePilotHost.PassFlag, out string pass) || pass != "true"))
                    throw new InvalidDataException("Farzanegan native pilot claimed success without exact solve/save evidence.");
            }
            else if (request.Operation == P16AzadeganSubEngineProbeHost.Operation)
            {
                // Read-only sub-engine probe: no save, no scenario, no alternative, no mutation,
                // no solver run, and the lifecycle datum must still be present.
                if (result.Success && (result.DatumName != "FirstConduitElementID" || !result.DatumValue.HasValue ||
                    result.SaveInvocationCount != 0 || result.SaveAsInvocationCount != 0 ||
                    result.ScenarioCreateInvocationCount != 0 || result.AlternativeCreateInvocationCount != 0 ||
                    result.MutationInvocationCount != 0 || result.SolverRunInvocationCount != 0))
                    throw new InvalidDataException("Sub-engine probe result violates the zero-write or datum requirement.");
            }
            else if (request.Operation == GeneralModelCalculationHost.Operation)
            {
                // CALCULATE owns exactly one solve and one save, with no scenario/alternative creation
                // and no property mutation. READ must instead prove zero writes and zero solver calls.
                bool generalRead = request.Diagnostics != null &&
                    request.Diagnostics.TryGetValue(GeneralModelCalculationHost.PhaseKey, out string generalPhase) &&
                    generalPhase == GeneralModelCalculationHost.PhaseRead;
                bool generalDiscover = request.Diagnostics != null &&
                    request.Diagnostics.TryGetValue(GeneralModelCalculationHost.PhaseKey, out string generalDiscoverPhase) &&
                    generalDiscoverPhase == GeneralModelCalculationHost.PhaseDiscover;
                bool generalBind = request.Diagnostics != null &&
                    request.Diagnostics.TryGetValue(GeneralModelCalculationHost.PhaseKey, out string generalBindPhase) &&
                    generalBindPhase == GeneralModelCalculationHost.PhaseBind;
                bool generalVerify = request.Diagnostics != null &&
                    request.Diagnostics.TryGetValue(GeneralModelCalculationHost.PhaseKey, out string generalVerifyPhase) &&
                    generalVerifyPhase == GeneralModelCalculationHost.PhaseVerify;
                if (generalBind)
                {
                    // Rebinding owns exactly one save and a bounded set of reference-field writes on the
                    // terrain element and on each two-dimensional data source. The allowlist, the element
                    // identities and the count are validated separately so a specific contract breach
                    // reports its own reason instead of a generic one.
                    if (result.Success) ValidateReferenceWrites(result.Diagnostics);
                    if (result.Success && (result.DatumName != "FirstConduitElementID" || !result.DatumValue.HasValue ||
                        result.SaveInvocationCount != 1 || result.SolverRunInvocationCount != 0 ||
                        result.SaveAsInvocationCount != 0 || result.MutationInvocationCount != 0 ||
                        result.ScenarioCreateInvocationCount != 0 || result.AlternativeCreateInvocationCount != 0 ||
                        !result.Diagnostics.TryGetValue(GeneralModelCalculationHost.BindPassFlag, out string bindPass) || bindPass != "true"))
                        throw new InvalidDataException("General rebinding claimed success without exact save/write evidence.");
                }
                else if (generalVerify)
                {
                    // Persistence verification is a reopen-and-read: no save, no write, no solve.
                    if (result.Success && (result.DatumName != "FirstConduitElementID" || !result.DatumValue.HasValue ||
                        result.SaveInvocationCount != 0 || result.SaveAsInvocationCount != 0 ||
                        result.MutationInvocationCount != 0 || result.ScenarioCreateInvocationCount != 0 ||
                        result.AlternativeCreateInvocationCount != 0 || result.SolverRunInvocationCount != 0 ||
                        !result.Diagnostics.TryGetValue(GeneralModelCalculationHost.VerifyPassFlag, out string verifyPass) || verifyPass != "true"))
                        throw new InvalidDataException("General terrain verification violates its zero-write, zero-solver contract.");
                }
                else if (generalDiscover)
                {
                    // Declared-input discovery is a metadata read: no solve, no save, no mutation.
                    if (result.Success && (result.DatumName != "FirstConduitElementID" || !result.DatumValue.HasValue ||
                        result.SaveInvocationCount != 0 || result.SaveAsInvocationCount != 0 ||
                        result.MutationInvocationCount != 0 || result.ScenarioCreateInvocationCount != 0 ||
                        result.AlternativeCreateInvocationCount != 0 || result.SolverRunInvocationCount != 0 ||
                        !result.Diagnostics.TryGetValue(GeneralModelCalculationHost.DiscoverPassFlag, out string discoverPass) || discoverPass != "true"))
                        throw new InvalidDataException("General terrain discovery violates its zero-write, zero-solver contract.");
                }
                else if (generalRead)
                {
                    if (result.Success && (result.DatumName != "FirstConduitElementID" || !result.DatumValue.HasValue ||
                        result.SaveInvocationCount != 0 || result.SaveAsInvocationCount != 0 ||
                        result.MutationInvocationCount != 0 || result.ScenarioCreateInvocationCount != 0 ||
                        result.AlternativeCreateInvocationCount != 0 || result.SolverRunInvocationCount != 0 ||
                        !result.Diagnostics.TryGetValue(GeneralModelCalculationHost.ReadPassFlag, out string readPass) || readPass != "true"))
                        throw new InvalidDataException("General model readback violates its zero-write, zero-solver contract.");
                }
                else
                {
                    if (result.Success && (result.DatumName != "FirstConduitElementID" || !result.DatumValue.HasValue ||
                        result.SaveInvocationCount != 1 || result.SolverRunInvocationCount != 1 ||
                        result.SaveAsInvocationCount != 0 || result.MutationInvocationCount != 0 ||
                        result.ScenarioCreateInvocationCount != 0 || result.AlternativeCreateInvocationCount != 0 ||
                        !result.Diagnostics.TryGetValue(GeneralModelCalculationHost.PassFlag, out string calcPass) || calcPass != "true"))
                        throw new InvalidDataException("General model calculation claimed success without exact solve/save evidence.");
                }
            }
            else
            {
                if (result.Success && (result.DatumName != "FirstConduitElementID" || !result.DatumValue.HasValue ||
                    result.SaveInvocationCount != 0 || result.SaveAsInvocationCount != 0 || result.MutationInvocationCount != 0 ||
                    result.ScenarioCreateInvocationCount != 0 || result.AlternativeCreateInvocationCount != 0 || result.SolverRunInvocationCount != 0))
                    throw new InvalidDataException("Success result lacks read or zero-write evidence.");
            }

            if (request.Operation == P16FlowDiagnostic.Operation && result.Success)
                P16FlowDiagnostic.ValidateResult(result);
            if (request.Operation == P16CalculatedFlowDiagnostic.Operation && result.Success)
                P16CalculatedFlowDiagnostic.ValidateResult(result);
            if (request.Operation == P16SecondaryMetricsHost.Operation && result.Success)
            {
                if (result.DatumName != "FirstConduitElementID" || !result.DatumValue.HasValue ||
                    result.SaveInvocationCount != 0 || result.SaveAsInvocationCount != 0 || result.MutationInvocationCount != 0 ||
                    result.ScenarioCreateInvocationCount != 0 || result.AlternativeCreateInvocationCount != 0 || result.SolverRunInvocationCount != 0 ||
                    !result.Diagnostics.TryGetValue("SecondaryMetricsCompleted", out string secComp) || secComp != "true")
                    throw new InvalidDataException("P16 secondary metrics result violates zero-write or completion requirements.");
            }
            if (request.Operation == P16AzadeganReadOnlyResultHost.Operation && result.Success)
            {
                if (result.SaveInvocationCount != 0 || result.SaveAsInvocationCount != 0 || result.MutationInvocationCount != 0 ||
                    result.ScenarioCreateInvocationCount != 0 || result.AlternativeCreateInvocationCount != 0 || result.SolverRunInvocationCount != 0)
                    throw new InvalidDataException("Azadegan read-only result violated the zero write/solver/scenario contract.");
                P16AzadeganReadOnlyResultHost.ValidateResult(result);
            }
            if (request.Operation == P16AzadeganCalculationHost.Operation && result.Success)
                P16AzadeganCalculationHost.ValidateResult(result);
            if (request.Operation == P16AzadeganSubEngineProbeHost.Operation && result.Success)
                P16AzadeganSubEngineProbeHost.ValidateResult(result);
            if (result.RuntimeBinding && (result.BoundCoreFiles == null || result.BoundCoreFiles.Length != 2 ||
                !request.CoreFiles.All(expected => result.BoundCoreFiles.Count(actual => actual.SimpleName == expected.SimpleName &&
                    actual.Path == expected.Path && actual.SHA256 == expected.SHA256) == 1)))
                throw new InvalidDataException("Returned core binding does not match the selected inventory.");
            return result;
        }
        internal static HostRequest Request(OpenFlowsRuntimeCandidate candidate, int count, string model) => new HostRequest {
            SchemaVersion = 1, RunId = Guid.NewGuid().ToString("N"), Operation = Operation, WorkingModelPath = model,
            RuntimeCandidateCount = count, SelectedCandidate = candidate.CandidateId, SelectedRuntime = candidate.InstallRoot,
            DetectedProduct = candidate.Executable.ProductName, DetectedVersion = candidate.Executable.ProductVersion,
            DetectedBuild = candidate.Executable.FileVersion, DetectedArchitecture = candidate.Executable.Architecture,
            CoreFiles = candidate.Assemblies.Select(HostMetadata.From).ToArray() };

        internal static HostRequest ScenarioIsolationRequest(OpenFlowsRuntimeCandidate candidate, int count, string model, WriteAuthorizationToken token, string baselineFingerprint) => new HostRequest {
            SchemaVersion = 1, RunId = Guid.NewGuid().ToString("N"), Operation = OpScenarioAlternativeIsolation, WorkingModelPath = model,
            RuntimeCandidateCount = count, SelectedCandidate = candidate.CandidateId, SelectedRuntime = candidate.InstallRoot,
            DetectedProduct = candidate.Executable.ProductName, DetectedVersion = candidate.Executable.ProductVersion,
            DetectedBuild = candidate.Executable.FileVersion, DetectedArchitecture = candidate.Executable.Architecture,
            CoreFiles = candidate.Assemblies.Select(HostMetadata.From).ToArray(),
            WriteAuthorizationTokenId = token.TokenId,
            WriteAuthorizationScope = token.AuthorizedOperationScope,
            WriteAuthorizationTargetWorkingPath = token.TargetWorkingPath,
            WorkingCopyId = token.WorkingCopyId,
            SourceBaselineFingerprint = baselineFingerprint
        };

        internal static HostRequest ReopenVerificationRequest(OpenFlowsRuntimeCandidate candidate, int count, string model, Dictionary<string, string> diagnostics) => new HostRequest {
            SchemaVersion = 1, RunId = Guid.NewGuid().ToString("N"), Operation = OpVerifyScenarioAlternativeIsolation, WorkingModelPath = model,
            RuntimeCandidateCount = count, SelectedCandidate = candidate.CandidateId, SelectedRuntime = candidate.InstallRoot,
            DetectedProduct = candidate.Executable.ProductName, DetectedVersion = candidate.Executable.ProductVersion,
            DetectedBuild = candidate.Executable.FileVersion, DetectedArchitecture = candidate.Executable.Architecture,
            CoreFiles = candidate.Assemblies.Select(HostMetadata.From).ToArray(),
            Diagnostics = diagnostics
        };

        internal static HostRequest GravityWriteAndSolverRequest(OpenFlowsRuntimeCandidate candidate, int count, string model, WriteAuthorizationToken token, string baselineFingerprint) => new HostRequest {
            SchemaVersion = 1, RunId = Guid.NewGuid().ToString("N"), Operation = OpGravityWriteAndSolver, WorkingModelPath = model,
            RuntimeCandidateCount = count, SelectedCandidate = candidate.CandidateId, SelectedRuntime = candidate.InstallRoot,
            DetectedProduct = candidate.Executable.ProductName, DetectedVersion = candidate.Executable.ProductVersion,
            DetectedBuild = candidate.Executable.FileVersion, DetectedArchitecture = candidate.Executable.Architecture,
            CoreFiles = candidate.Assemblies.Select(HostMetadata.From).ToArray(),
            WriteAuthorizationTokenId = token.TokenId,
            WriteAuthorizationScope = token.AuthorizedOperationScope,
            WriteAuthorizationTargetWorkingPath = token.TargetWorkingPath,
            WorkingCopyId = token.WorkingCopyId,
            SourceBaselineFingerprint = baselineFingerprint
        };

        internal static HostRequest GravityWriteAndSolverReopenRequest(OpenFlowsRuntimeCandidate candidate, int count, string model, Dictionary<string, string> diagnostics) => new HostRequest {
            SchemaVersion = 1, RunId = Guid.NewGuid().ToString("N"), Operation = OpVerifyGravityWriteAndSolver, WorkingModelPath = model,
            RuntimeCandidateCount = count, SelectedCandidate = candidate.CandidateId, SelectedRuntime = candidate.InstallRoot,
            DetectedProduct = candidate.Executable.ProductName, DetectedVersion = candidate.Executable.ProductVersion,
            DetectedBuild = candidate.Executable.FileVersion, DetectedArchitecture = candidate.Executable.Architecture,
            CoreFiles = candidate.Assemblies.Select(HostMetadata.From).ToArray(),
            Diagnostics = diagnostics
        };
    }
}
