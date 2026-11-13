using System;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.RuntimeHost
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == SwmmBentleyWorker.LifecycleProbeSwitch)
                return SwmmBentleyWorker.RunLifecycleProbe();
            if (args.Length == 2 && args[0] == SwmmBentleyWorker.LaunchSwitch)
            {
                int workerCode = SwmmBentleyWorker.Run(args[1]);
                Environment.Exit(workerCode);
                return workerCode;
            }
            // The output-reader operation is model-free by construction: it has its own one-shot
            // IPC envelope and never enters the model lifecycle below.
            if (args.Length == 3 && args[0] == P16AzadeganOutputReaderHost.LaunchSwitch)
                return P16AzadeganOutputReaderHost.Run(args[1], args[2]);
            // Run returns only after every required finally/using and the STA join.
            // No cleanup or persistence is deferred beyond this point.
            int code = Run(args);
            if (code == 0)
            {
                RuntimeStageJournal.Call("ControlledExitRequested", () => { });
                Environment.Exit(0);
            }
            return code;
        }
        private static int Run(string[] args)
        {
            bool internalP15 = args.Length == 3 && args[0] == P15TechnicalCapabilityHost.LaunchSwitch;
            bool internalP16 = args.Length == 3 && args[0] == P16FlowDiagnostic.LaunchSwitch;
            bool internalP16Calculated = args.Length == 3 && args[0] == P16CalculatedFlowDiagnostic.LaunchSwitch;
            bool internalP16Proof = args.Length == 3 && args[0] == P16HeadlessResultProofHost.LaunchSwitch;
            bool internalP16Secondary = args.Length == 3 && args[0] == P16SecondaryMetricsHost.LaunchSwitch;
            bool internalP16Azadegan = args.Length == 3 && args[0] == P16AzadeganReadOnlyResultHost.LaunchSwitch;
            bool internalP16GvfReadback = args.Length == 3 && args[0] == P16AzadeganGvfReadbackHost.LaunchSwitch;
            bool internalP16AzadeganCalc = args.Length == 3 && args[0] == P16AzadeganCalculationHost.LaunchSwitch;
            bool internalP16SubEngine = args.Length == 3 && args[0] == P16AzadeganSubEngineProbeHost.LaunchSwitch;
            bool internalFarzaneganPilot = args.Length == 3 && (args[0] == P16FarzaneganNativePilotHost.LaunchSwitch || args[0] == P14FarzaneganTestOnlyHost.LaunchSwitch || args[0] == FarzaneganHydraulicRemediationHost.LaunchSwitch);
            bool internalFarzaneganRead = args.Length == 3 && args[0] == P16FarzaneganNativeReadHost.LaunchSwitch;
            bool internalP14CatalogProbe = args.Length == 3 && args[0] == P14CatalogSelectorDiscoveryHost.LaunchSwitch;
            bool internalGeneralCalc = args.Length == 3 && args[0] == GeneralModelCalculationHost.LaunchSwitch;
            if (args.Length != 2 && !internalP15 && !internalP16 && !internalP16Calculated && !internalP16Proof && !internalP16Secondary && !internalP16Azadegan && !internalP16GvfReadback && !internalP16AzadeganCalc && !internalP16SubEngine && !internalFarzaneganPilot && !internalFarzaneganRead && !internalP14CatalogProbe && !internalGeneralCalc) return 2;
            if (internalP15 || internalP16 || internalP16Calculated || internalP16Proof || internalP16Secondary || internalP16Azadegan || internalP16GvfReadback || internalP16AzadeganCalc || internalP16SubEngine || internalFarzaneganPilot || internalFarzaneganRead || internalP14CatalogProbe || internalGeneralCalc) args = args.Skip(1).ToArray();
            try
            {
                string requestPath = Path.GetFullPath(args[0]), resultPath = Path.GetFullPath(args[1]);
                string directory = Path.GetDirectoryName(requestPath);
                // The control channel is SewerPilot's own, so it is measured against the application
                // anchor rather than this process's temporary environment, which the launching
                // service redirects to a private scratch root.
                RuntimeHostProtocol.AnchoredDirectory(directory, "RuntimeHost");
                if (Path.GetFileName(requestPath) != "request.json" || resultPath != Path.Combine(directory, "result.json") || File.Exists(resultPath))
                    throw new InvalidDataException("Unexpected one-shot IPC paths.");
                Environment.SetEnvironmentVariable(RuntimeStageJournal.EnvironmentName, Path.Combine(directory, "stages.jsonl"));
                RuntimeStageJournal.Call("HostStarted", () => { });
                if (internalP16Calculated && new FileInfo(requestPath).Length > 256 * 1024)
                    throw new InvalidDataException("Calculated P16 request exceeds its payload bound.");
                var request = internalP16Calculated ? P16CalculatedFlowDiagnostic.ParseRequest(File.ReadAllText(requestPath)) :
                    RuntimeHostProtocol.Serializer().Deserialize<HostRequest>(File.ReadAllText(requestPath));
                if (request != null && request.Operation == P16HeadlessResultProofHost.Operation)
                    internalP16Proof = true;
                if (request != null && request.Operation == P16SecondaryMetricsHost.Operation)
                    internalP16Secondary = true;
                // Operating mode is loaded from trusted application configuration, never from request flags.
                GravityExecutionAuthorization.InitializeRuntime(SewerPilotOperatingConfiguration.LoadApplicationConfiguration(), request, directory);
                RuntimeStageJournal.Call("RequestValidated", () => RuntimeHostProtocol.ValidateRequest(request, internalP15, null, internalP16 || internalP16Calculated, internalP16Proof, internalP16Secondary, internalP16Azadegan || internalP16GvfReadback, internalP16AzadeganCalc, internalP16SubEngine, internalFarzaneganPilot, internalFarzaneganRead, internalP14CatalogProbe, internalGeneralCalc));
                if (internalP15 && request.Operation != P15TechnicalCapabilityHost.Operation)
                    throw new InvalidDataException("Internal technical launch requires its dedicated operation.");
                if (internalP16 && request.Operation != P16FlowDiagnostic.Operation)
                    throw new InvalidDataException("Internal P16 launch requires its dedicated read-only diagnostic operation.");
                if (internalP16Calculated && request.Operation != P16CalculatedFlowDiagnostic.Operation)
                    throw new InvalidDataException("Calculated P16 launch requires its dedicated read-only operation.");
                if (internalP16Proof && request.Operation != P16HeadlessResultProofHost.Operation)
                    throw new InvalidDataException("Proof launch requires its dedicated operation.");
                if (internalP16Secondary && request.Operation != P16SecondaryMetricsHost.Operation)
                    throw new InvalidDataException("Secondary metrics launch requires its dedicated operation.");
                if (internalP16Azadegan && request.Operation != P16AzadeganReadOnlyResultHost.Operation)
                    throw new InvalidDataException("Azadegan launch requires its dedicated read-only operation.");
                if (internalP16GvfReadback && request.Operation != P16AzadeganGvfReadbackHost.Operation)
                    throw new InvalidDataException("Azadegan GVF readback launch requires its dedicated read-only operation.");
                if (internalP16AzadeganCalc && request.Operation != P16AzadeganCalculationHost.Operation)
                    throw new InvalidDataException("Azadegan calculation launch requires its dedicated operation.");
                if (internalP16SubEngine && request.Operation != P16AzadeganSubEngineProbeHost.Operation)
                    throw new InvalidDataException("Azadegan sub-engine launch requires its dedicated read-only operation.");
                if (internalFarzaneganPilot && !P14FarzaneganTestOnlyHost.IsPilot(request.Operation))
                    throw new InvalidDataException("Farzanegan live pilot launch requires its exact native operation.");
                if (internalFarzaneganRead && request.Operation != P16FarzaneganNativeReadHost.Operation)
                    throw new InvalidDataException("Farzanegan READ requires its exact read-only native operation.");
                if (internalP14CatalogProbe && request.Operation != P14CatalogSelectorDiscoveryHost.Operation)
                    throw new InvalidDataException("P14 catalog selector probe requires its exact read-only operation.");
                if (internalGeneralCalc && request.Operation != GeneralModelCalculationHost.Operation)
                    throw new InvalidDataException("General model calculation launch requires its dedicated operation.");
                if (Path.GetFileName(directory) != request.RunId || !Environment.Is64BitProcess || Environment.Version.Major != 4)
                    throw new InvalidDataException("Run identity or net48/x64 host eligibility mismatch.");
                RuntimeStageJournal.Call("RuntimeSelected", () => { });
                RuntimeStageJournal.Call("VendorBindingStarted", () => { });
                var binding = RuntimeStageJournal.Call("VendorBinding", () => new CoreBindingWorker().Bind(request.SelectedRuntime,
                    request.CoreFiles.Select(item => item.ToMetadata()).ToArray()), value => new { value.Success, value.Error });
                RuntimeStageJournal.Call("VendorBindingCompleted", () => { });
                ModelReadEvidence evidence;
                if (binding.Success)
                    evidence = new LifecycleWorker().Run(request.SelectedRuntime, request.WorkingModelPath, request);
                else evidence = new ModelReadEvidence { FailureCode = "P4_R9_BLOCKED_VENDOR_RUNTIME", Exception = binding.Error };
                bool production = request.Operation == GravityWriteAndSolverHost.ProductionOperation;
                bool azadeganCalculate = internalP16AzadeganCalc && request.Diagnostics.TryGetValue(P16AzadeganCalculationHost.PhaseKey, out string azadeganInitialPhase) &&
                    azadeganInitialPhase == P16AzadeganCalculationHost.PhaseCalculate;
                if ((internalP15 || production || internalP16Proof || azadeganCalculate) && evidence.FailureCode == null && evidence.CloseCompleted)
                {
                    var written = evidence;
                    try
                    {
                        if (production) GravityWriteAndSolverHost.PrepareProductionReopen(request, written);
                        else if (internalP16Proof) P16HeadlessResultProofHost.PrepareReopen(request, written);
                        else if (azadeganCalculate) P16AzadeganCalculationHost.PrepareRead(request, written);
                        else P15TechnicalCapabilityHost.PrepareReopen(request, written);
                        // A second fully initialized/closed model session, within this single x64 process invocation.
                        evidence = new LifecycleWorker().Run(request.SelectedRuntime, request.WorkingModelPath, request);
                        foreach (var pair in written.Diagnostics)
                            if (!evidence.Diagnostics.ContainsKey(pair.Key)) evidence.Diagnostics[pair.Key] = pair.Value;
                        evidence.Diagnostics["FirstSessionCloseCompleted"] = written.CloseCompleted ? "true" : "false";
                    }
                    catch (Exception error)
                    { evidence.FailureCode = production ? "P15_BLOCKED_PRODUCTION_REOPEN" : (internalP16Proof ? "P16_PROOF_BLOCKED_REOPEN" : (azadeganCalculate ? "P16_BLOCKED_AZADEGAN_READBACK" : "P15_TECH_BLOCKED_REOPEN")); evidence.Exception = error.ToString(); }
                }
                if (production && request.EngineeringPlan != null && request.EngineeringPlan.Calculate &&
                    evidence.FailureCode == null && evidence.CloseCompleted &&
                    evidence.Diagnostics.TryGetValue("SolverStatusSuccess", out string sss) && sss == "true")
                {
                    var solved = evidence;
                    try
                    {
                        GravityWriteAndSolverHost.PrepareProductionReadback(request, solved);
                        // A third fully initialized/closed model session for post-solve result readback
                        evidence = new LifecycleWorker().Run(request.SelectedRuntime, request.WorkingModelPath, request);
                        foreach (var pair in solved.Diagnostics)
                            if (!evidence.Diagnostics.ContainsKey(pair.Key)) evidence.Diagnostics[pair.Key] = pair.Value;
                        evidence.Diagnostics["SecondSessionCloseCompleted"] = solved.CloseCompleted ? "true" : "false";
                    }
                    catch (Exception error)
                    {
                        evidence.FailureCode = "P16_BLOCKED_PRODUCTION_READBACK";
                        evidence.Exception = error.ToString();
                    }
                }
                bool sta = evidence.Diagnostics.ContainsKey("StaCompleted") && evidence.Diagnostics["StaCompleted"] == "true";
                if (sta) RuntimeStageJournal.Call("StaCompleted", () => { });

                bool success;
                int saveCount = 0;
                int saveAsCount = 0;
                int mutationCount = 0;
                int scenarioCreateCount = 0;
                int altCreateCount = 0;
                int solverRunCount = 0;

                if (production)
                {
                    saveCount = (Flag(evidence, "SaveCurrentProjectInvoked") ? 1 : 0) +
                                (Flag(evidence, "PostSolveSaveCurrentProjectInvoked") ? 1 : 0);
                    scenarioCreateCount = Flag(evidence, "ScenarioCopyInvoked") ? 1 : 0;
                    altCreateCount = Flag(evidence, "AlternativeCopyInvoked") ? 1 : 0;
                    mutationCount = Count(evidence, "PropertySetCount");
                    solverRunCount = Flag(evidence, "SolverRunInvoked") ? 1 : 0;
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded && sta &&
                        GravityWriteAndSolverHost.ProductionSuccess(request, evidence);
                }
                else if (request.Operation == P16HeadlessResultProofHost.Operation)
                {
                    saveCount = Flag(evidence, "SaveCurrentProjectInvoked") ? 1 : 0;
                    solverRunCount = Flag(evidence, "SolverRunInvoked") ? 1 : 0;
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                        evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                        Flag(evidence, "FirstSessionCloseCompleted") &&
                        Flag(evidence, "SolverStatusSuccess") && solverRunCount == 1 &&
                        Flag(evidence, "SaveCurrentProjectSucceeded") && saveCount == 1 &&
                        Flag(evidence, "ResultManagerAvailable") &&
                        Flag(evidence, "HasResults") &&
                        Flag(evidence, "FlowFieldSupported") &&
                        Flag(evidence, "FlowFieldHasResults") &&
                        Flag(evidence, "FlowValueObserved");
                }
                else if (request.Operation == P16SecondaryMetricsHost.Operation)
                {
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                        evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                        evidence.SaveWriteInvocations.Count == 0 &&
                        Flag(evidence, "ResultManagerAvailable") &&
                        Flag(evidence, "HasResults") &&
                        Flag(evidence, "SecondaryMetricsCompleted");
                }
                else if (request.Operation == P16AzadeganReadOnlyResultHost.Operation)
                {
                    // Strictly read-only: every write/solve counter must stay at its zero default.
                    saveCount = 0; saveAsCount = 0; mutationCount = 0;
                    scenarioCreateCount = 0; altCreateCount = 0; solverRunCount = 0;
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                        evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                        evidence.SaveWriteInvocations.Count == 0 &&
                        evidence.FirstConduitElementID.HasValue &&
                        Flag(evidence, P16AzadeganReadOnlyResultHost.PassFlag);
                }
                else if (request.Operation == P16AzadeganGvfReadbackHost.Operation)
                {
                    // Strictly read-only persisted-result readback: every write/solve counter must
                    // stay at its zero default, and no result store may be created or saved.
                    saveCount = 0; saveAsCount = 0; mutationCount = 0;
                    scenarioCreateCount = 0; altCreateCount = 0; solverRunCount = 0;
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                        evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                        evidence.SaveWriteInvocations.Count == 0 &&
                        evidence.FirstConduitElementID.HasValue &&
                        Flag(evidence, P16AzadeganGvfReadbackHost.PassFlag);
                }
                else if (request.Operation == P16AzadeganCalculationHost.Operation)
                {
                    // CALCULATE owns one solve/save. A separate READ session must instead prove
                    // zero writes and zero solver invocations, with the earlier persisted solve
                    // fully validated through the readback observation and its inventory lineage.
                    bool azadeganReadOnlyPhase = request.Diagnostics.TryGetValue(P16AzadeganCalculationHost.PhaseKey, out string currentPhase) &&
                        currentPhase == P16AzadeganCalculationHost.PhaseRead;
                    saveAsCount = 0;
                    mutationCount = 0;
                    scenarioCreateCount = 0;
                    altCreateCount = 0;
                    if (azadeganReadOnlyPhase)
                    {
                        saveCount = 0;
                        solverRunCount = 0;
                        success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                            evidence.CurrentGraphicalProjectAvailable && evidence.DomainProjectAvailable && evidence.DomainDataSetAvailable &&
                            evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                            evidence.SaveWriteInvocations.Count == 0 && evidence.FirstConduitElementID.HasValue &&
                            Flag(evidence, "P16OpenQuestionPolicyClean") &&
                            Flag(evidence, "P16ReopenedInventoryMatchesPersistedOutput") &&
                            Flag(evidence, "CalculationFreshnessVerified") &&
                            Flag(evidence, P16AzadeganCalculationHost.PassFlag);
                    }
                    else
                    {
                        saveCount = Flag(evidence, "SaveCurrentProjectInvoked") ? 1 : 0;
                        solverRunCount = Flag(evidence, "SolverRunInvoked") ? 1 : 0;
                        success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                            evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                            Flag(evidence, "FirstSessionCloseCompleted") &&
                            Flag(evidence, "SolverStatusSuccess") && solverRunCount == 1 && saveCount == 1 &&
                            Flag(evidence, "SaveCurrentProjectSucceeded") &&
                            Flag(evidence, P16AzadeganCalculationHost.PassFlag);
                    }
                }
                else if (request.Operation == GeneralModelCalculationHost.Operation)
                {
                    // CALCULATE owns exactly one solve and one save on the disposable copy. READ must
                    // instead prove zero writes and zero solver invocations, with its result rows bound
                    // to the earlier persisted solve.
                    bool generalReadPhase = request.Diagnostics.TryGetValue(GeneralModelCalculationHost.PhaseKey, out string generalPhase) &&
                        generalPhase == GeneralModelCalculationHost.PhaseRead;
                    bool generalDiscoverPhase = request.Diagnostics.TryGetValue(GeneralModelCalculationHost.PhaseKey, out string generalDiscoverValue) &&
                        generalDiscoverValue == GeneralModelCalculationHost.PhaseDiscover;
                    bool generalBindPhase = request.Diagnostics.TryGetValue(GeneralModelCalculationHost.PhaseKey, out string generalBindValue) &&
                        generalBindValue == GeneralModelCalculationHost.PhaseBind;
                    bool generalVerifyPhase = request.Diagnostics.TryGetValue(GeneralModelCalculationHost.PhaseKey, out string generalVerifyValue) &&
                        generalVerifyValue == GeneralModelCalculationHost.PhaseVerify;
                    saveAsCount = 0;
                    mutationCount = 0;
                    scenarioCreateCount = 0;
                    altCreateCount = 0;
                    if (generalBindPhase)
                    {
                        // Rebinding writes the terrain reference fields and, on the same pass, the
                        // document-specification proxy of every two-dimensional data source. It must
                        // never solve, never create a scenario and never touch an engineering value.
                        // The allowlist, the element identities and the count are validated together,
                        // so widening one number cannot widen the contract.
                        bool referenceContract = true;
                        try { RuntimeHostProtocol.ValidateReferenceWrites(evidence.Diagnostics); }
                        catch (InvalidDataException) { referenceContract = false; }
                        saveCount = Flag(evidence, "SaveCurrentProjectInvoked") ? 1 : 0;
                        solverRunCount = Flag(evidence, "SolverRunInvoked") ? 1 : 0;
                        success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                            evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                            evidence.SaveWriteInvocations.Count <= 1 &&
                            Flag(evidence, "P16OpenQuestionPolicyClean") &&
                            Flag(evidence, "SaveCurrentProjectSucceeded") &&
                            solverRunCount == 0 && saveCount == 1 &&
                            referenceContract &&
                            Flag(evidence, GeneralModelCalculationHost.BindPassFlag);
                    }
                    else if (generalVerifyPhase)
                    {
                        // Independent persistence verification: reopen and read, nothing else.
                        saveCount = 0;
                        solverRunCount = 0;
                        success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                            evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                            evidence.SaveWriteInvocations.Count == 0 &&
                            Flag(evidence, "P16OpenQuestionPolicyClean") &&
                            Flag(evidence, GeneralModelCalculationHost.VerifyPassFlag);
                    }
                    else if (generalDiscoverPhase)
                    {
                        // Declared-input discovery: a metadata read with no solve, save or mutation.
                        saveCount = 0;
                        solverRunCount = 0;
                        success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                            evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                            evidence.SaveWriteInvocations.Count == 0 && evidence.FirstConduitElementID.HasValue &&
                            Flag(evidence, "P16OpenQuestionPolicyClean") &&
                            Flag(evidence, GeneralModelCalculationHost.DiscoverPassFlag);
                    }
                    else if (generalReadPhase)
                    {
                        saveCount = 0;
                        solverRunCount = 0;
                        success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                            evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                            evidence.SaveWriteInvocations.Count == 0 && evidence.FirstConduitElementID.HasValue &&
                            Flag(evidence, "P16OpenQuestionPolicyClean") &&
                            Flag(evidence, GeneralModelCalculationHost.ReadPassFlag);
                    }
                    else
                    {
                        saveCount = Flag(evidence, "SaveCurrentProjectInvoked") ? 1 : 0;
                        solverRunCount = Flag(evidence, "SolverRunInvoked") ? 1 : 0;
                        success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                            evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                            Flag(evidence, "SolverStatusSuccess") && solverRunCount == 1 && saveCount == 1 &&
                            Flag(evidence, "SaveCurrentProjectSucceeded") &&
                            Flag(evidence, GeneralModelCalculationHost.PassFlag);
                    }
                }
                else if (request.Operation == P16FarzaneganNativeReadHost.Operation)
                {
                    saveCount = 0; solverRunCount = 0;
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                        evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                        evidence.FirstConduitElementID == 52 && evidence.SaveWriteInvocations.Count == 0 &&
                        Flag(evidence, "P16OpenQuestionPolicyClean") &&
                        Flag(evidence, "FarzaneganPersistedInventoryMatches") &&
                        Flag(evidence, P16FarzaneganNativeReadHost.PassFlag);
                }
                else if (request.Operation == P14CatalogSelectorDiscoveryHost.Operation)
                {
                    // Strictly read-only qualification probe: zero writes, zero solve, zero save.
                    saveCount = 0; saveAsCount = 0; mutationCount = 0;
                    scenarioCreateCount = 0; altCreateCount = 0; solverRunCount = 0;
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                        evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                        evidence.SaveWriteInvocations.Count == 0 &&
                        evidence.FirstConduitElementID.HasValue &&
                        Flag(evidence, "P14CatalogSelectorProbeCompleted");
                }
                else if (P14FarzaneganTestOnlyHost.IsPilot(request.Operation))
                {
                    mutationCount = Count(evidence, "PropertySetCount");
                    saveCount = Flag(evidence, "SaveCurrentProjectInvoked") ? 1 : 0;
                    solverRunCount = Flag(evidence, "SolverRunInvoked") ? 1 : 0;
                    if (P14CatalogPointerPersistenceHost.IsOperation(request.Operation))
                    {
                        success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                            evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                            saveCount == 1 && solverRunCount == 0 && mutationCount == P14CatalogPointerPersistenceHost.ExpectedMutations(request.Operation) &&
                            Flag(evidence, "P14NativeWriteReadbackVerified") &&
                            Flag(evidence, "SaveCurrentProjectSucceeded") &&
                            Flag(evidence, P14CatalogPointerPersistenceHost.PassFlag) &&
                            Flag(evidence, P16FarzaneganNativePilotHost.PassFlag);
                    }
                    else
                    {
                        success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                            evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                            saveCount == 1 && solverRunCount == 1 &&
                            mutationCount == FarzaneganHydraulicRemediationHost.ExpectedWrites(request.Operation) &&
                            (FarzaneganHydraulicRemediationHost.ExpectedWrites(request.Operation) == 0 || Flag(evidence, "P14NativeWriteReadbackVerified")) &&
                            (request.Operation != FarzaneganHydraulicRemediationHost.Operation || Flag(evidence, "RemediationNativeReadbackVerified")) &&
                            Flag(evidence, "SaveCurrentProjectSucceeded") &&
                            Flag(evidence, P16FarzaneganNativePilotHost.PassFlag);
                    }
                }
                else if (request.Operation == P16AzadeganSubEngineProbeHost.Operation)
                {
                    // Strictly read-only sub-engine probe: every write/solve counter stays zero.
                    saveCount = 0; saveAsCount = 0; mutationCount = 0;
                    scenarioCreateCount = 0; altCreateCount = 0; solverRunCount = 0;
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                        evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                        evidence.SaveWriteInvocations.Count == 0 &&
                        evidence.FirstConduitElementID.HasValue &&
                        Flag(evidence, P16AzadeganSubEngineProbeHost.PassFlag);
                }
                else if (request.Operation == P15TechnicalCapabilityHost.Operation)
                {
                    saveCount = Flag(evidence, "SaveCurrentProjectInvoked") ? 1 : 0;
                    scenarioCreateCount = Flag(evidence, "ScenarioCopyInvoked") ? 1 : 0;
                    altCreateCount = Flag(evidence, "AlternativeCopyInvoked") ? 1 : 0;
                    mutationCount = Count(evidence, "PropertySetCount");
                    solverRunCount = Flag(evidence, "SolverRunInvoked") ? 1 : 0;
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                        evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                        Flag(evidence, "FirstSessionCloseCompleted") && Flag(evidence, "WriteCapabilityProven") &&
                        Flag(evidence, "ImmediateReadbackVerified") && Flag(evidence, "SaveCurrentProjectSucceeded") &&
                        P15TechnicalCapabilityHost.ValidWriteCounts(evidence.Diagnostics, mutationCount) &&
                        Flag(evidence, "SolverStatusSuccess") && solverRunCount == 1;
                }
                else if (request.Operation == RuntimeHostProtocol.OpScenarioAlternativeIsolation)
                {
                    saveCount = 1;
                    scenarioCreateCount = 1;
                    altCreateCount = 1;
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                        evidence.CurrentGraphicalProjectAvailable && evidence.DomainProjectAvailable && evidence.DomainDataSetAvailable &&
                        evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                        Flag(evidence, "SaveCurrentProjectInvoked") &&
                        Flag(evidence, "ScenarioCopied") &&
                        Flag(evidence, "AlternativeCopied") &&
                        Flag(evidence, "AlternativeAssigned") &&
                        Flag(evidence, "IsolationVerifiedBeforeSave");
                }
                else if (request.Operation == RuntimeHostProtocol.OpVerifyScenarioAlternativeIsolation)
                {
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                        evidence.CurrentGraphicalProjectAvailable && evidence.DomainProjectAvailable && evidence.DomainDataSetAvailable &&
                        evidence.FirstConduitElementID.HasValue && evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                        Flag(evidence, "ReopenVerificationSuccess") &&
                        evidence.SaveWriteInvocations.Count == 0;
                }
                else if (request.Operation == RuntimeHostProtocol.OpGravityWriteAndSolver)
                {
                    saveCount = Flag(evidence, "SaveCurrentProjectInvoked") ? 1 : 0;
                    scenarioCreateCount = Flag(evidence, "ScenarioCopyInvoked") ? 1 : 0;
                    altCreateCount = Flag(evidence, "AlternativeCopyInvoked") ? 1 : 0;
                    mutationCount = Count(evidence, "PropertySetCount");
                    solverRunCount = Flag(evidence, "SolverRunInvoked") ? 1 : 0;
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                        evidence.CurrentGraphicalProjectAvailable && evidence.DomainProjectAvailable && evidence.DomainDataSetAvailable &&
                        evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                        Flag(evidence, "SaveCurrentProjectInvoked") &&
                        Flag(evidence, "ScenarioCopied") &&
                        Flag(evidence, "AlternativeCopied") &&
                        Flag(evidence, "AlternativeAssigned") &&
                        Flag(evidence, "IsolationVerifiedBeforeSave") &&
                        Flag(evidence, "CandidateWritesApplied") &&
                        Flag(evidence, "LocalizationReadbackVerified") &&
                        Count(evidence, "LocalizationPropertySetCount") == 2 &&
                        Count(evidence, "EndpointInvertPropertySetCount") == 2 &&
                        mutationCount == 4 &&
                        Flag(evidence, "ImmediateReadbackVerified") &&
                        Flag(evidence, "SolverRunCompleted");
                }
                else if (request.Operation == RuntimeHostProtocol.OpVerifyGravityWriteAndSolver)
                {
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                        evidence.CurrentGraphicalProjectAvailable && evidence.DomainProjectAvailable && evidence.DomainDataSetAvailable &&
                        evidence.FirstConduitElementID.HasValue && evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                        Flag(evidence, "ReopenVerificationSuccess") &&
                        Flag(evidence, "LocalizationPersistenceVerified") &&
                        evidence.SaveWriteInvocations.Count == 0;
                }
                else
                {
                    success = binding.Success && evidence.LifecycleInitialized && evidence.OpenSucceeded &&
                        evidence.CurrentGraphicalProjectAvailable && evidence.DomainProjectAvailable && evidence.DomainDataSetAvailable &&
                        evidence.FirstConduitElementID.HasValue && evidence.CloseCompleted && sta && evidence.FailureCode == null &&
                        evidence.SaveWriteInvocations.Count == 0;
                }

                string failure = evidence.FailureCode;
                if (failure == "InitializationFailure") failure = "P4_R9_BLOCKED_VENDOR_RUNTIME";
                if (failure == "ModelOpenFailure") failure = "P4_R9_BLOCKED_MODEL_OPEN";
                if (failure == "CloseFailure") failure = "P4_R9_BLOCKED_VENDOR_CLEANUP";
                var companionRootsJson = Diagnostic(evidence, "CompanionRoots");
                var companionRoots = companionRootsJson != null
                    ? RuntimeHostProtocol.Serializer().Deserialize<HostCompanionRoot[]>(companionRootsJson)
                    : new HostCompanionRoot[0];
                var result = new HostResult {
                    SchemaVersion = 1, RunId = request.RunId, Success = success,
                    FailureClassification = success ? null : (failure ?? "P4_R9_BLOCKED_VENDOR_RUNTIME"), FailureMessageSafe = evidence.Exception ?? Diagnostic(evidence, "BindingFailure"),
                    RuntimeCandidateCount = request.RuntimeCandidateCount, SelectedRuntime = request.SelectedRuntime,
                    PrimaryProductRoot = request.SelectedRuntime, CompanionRoots = companionRoots,
                    DetectedProduct = request.DetectedProduct, DetectedVersion = request.DetectedVersion, DetectedBuild = request.DetectedBuild,
                    DetectedArchitecture = request.DetectedArchitecture, RuntimeBinding = binding.Success,
                    BoundCoreFiles = binding.Loaded.Select(HostMetadata.From).ToArray(),
                    ApplicationDescriptionCreated = Flag(evidence, "ApplicationDescriptionCreated"),
                    ApplicationModelCreated = Flag(evidence, "ApplicationModelCreated"), ParentUiModelCreated = Flag(evidence, "ParentUiModelCreated"),
                    LifecycleInitialized = evidence.LifecycleInitialized, ModelOpen = evidence.OpenSucceeded,
                    CurrentGraphicalProjectEstablished = evidence.CurrentGraphicalProjectAvailable,
                    DomainProjectEstablished = evidence.DomainProjectAvailable, DomainDataSetEstablished = evidence.DomainDataSetAvailable,
                    ElementType = Diagnostic(evidence, "ElementTypeName"), ManagerRuntimeType = Diagnostic(evidence, "ManagerRuntimeType"),
                    ElementIdsDeclaringType = Diagnostic(evidence, "ElementIDsDeclaringType"), ElementIdsRuntimeType = Diagnostic(evidence, "ElementIDsReturnType"),
                    CollectionRuntimeType = Diagnostic(evidence, "Collection"), IndexerDescription = Diagnostic(evidence, "IndexerSignature"),
                    DatumName = evidence.FirstConduitElementID.HasValue ? "FirstConduitElementID" : null, DatumValue = evidence.FirstConduitElementID,
                    SaveInvocationCount = saveCount, SaveAsInvocationCount = saveAsCount, MutationInvocationCount = mutationCount,
                    ScenarioCreateInvocationCount = scenarioCreateCount, AlternativeCreateInvocationCount = altCreateCount, SolverRunInvocationCount = solverRunCount,
                    VendorCleanupCompleted = evidence.CloseCompleted, StaCompleted = sta,
                    ResultCommitted = true, RequestedExitMode = success ? RuntimeHostProtocol.ExitMode : "Natural",
                    Diagnostics = evidence.Diagnostics,
                    LoadedAssemblies = evidence.LoadedAssemblies.Select(item => new HostFingerprint { Path = item.Path, SHA256 = item.SHA256 }).ToArray()
                };
                RuntimeStageJournal.Call("ResultWriteStarted", () => RuntimeHostProtocol.AtomicWrite(resultPath, result));
                RuntimeStageJournal.Call("ResultCommitted", () => { });
                return success ? 0 : 1;
            }
            catch (Exception error)
            {
                RuntimeStageJournal.Record("HostFailure", "EXCEPTION", null, error.ToString());
                return 1;
            }
        }
        private static string Diagnostic(ModelReadEvidence evidence, string key) => evidence.Diagnostics.ContainsKey(key) ? evidence.Diagnostics[key] : null;
        private static bool Flag(ModelReadEvidence evidence, string key) => Diagnostic(evidence, key) == "true";
        private static int Count(ModelReadEvidence evidence, string key) => int.TryParse(Diagnostic(evidence, key), out int value) ? value : 0;
    }
}
