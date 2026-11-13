using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    // A genuine hydraulic calculation receipt for the engineer's own selected model.
    internal sealed class GeneralCalculationReceipt
    {
        internal string EvidenceDirectory;
        internal string WorkingModelPath;
        internal int ScenarioId;
        internal int CalculationOptionsId;
        internal string Engine;
        internal string CalculationEventId;
        internal string ScenarioComputeStatus;
        internal int ConduitCount;
        internal int StructureCount;
        internal GeneralHydraulicResultRow[] Rows;
        internal string Summary;

        // The one acceptance decision, taken after the readback was verified. A warning-bearing run is
        // PARTIAL and keeps its rows; it is never promoted to PASS.
        internal string Verdict;
        internal string AcceptanceReason;
        internal string AcceptanceExplanation;
    }

    // One external input the model declares for itself, resolved to a file that actually exists on
    // this computer. The declared locations come from the model; only the resolution is ours.
    internal sealed class ResolvedTerrain
    {
        internal string SourcePath;
        internal string DeclaredRelativePath;
        internal string DeclaredAbsolutePath;
        internal string RelativeDestination;
        internal string BareDestination;
        internal string SHA256;
        internal long Bytes;
        internal string[] CopiedTo;
    }

    // One Shapefile family the model declares. A Shapefile is three sidecar files that only work
    // together, so the family travels as a unit and is copied once no matter how many records name it.
    internal sealed class ResolvedDataSource
    {
        internal string SourcePath;
        internal string DeclaredRelativePath;
        internal string DeclaredAbsolutePath;
        internal string RelativeDestination;
        internal int ReferenceCount;
        internal string[] SourcePaths;
        internal string[] SHA256;
        internal string[] DestinationPaths;
    }

    // The disposable copy plus everything discovered about the model's declared external inputs.
    internal sealed class TerrainPreparation
    {
        internal GeneralTerrainObservation Discovery;
        internal List<ResolvedTerrain> Terrains;
        internal List<ResolvedDataSource> DataSources;
        internal string[] Sources;
        internal WorkingCopyIntegrity Working;
        internal GeneralTerrainObservation WithDependencies;
        internal string WithDependenciesPath;
    }

    // The outcome of one rebinding attempt plus the independent reopen that follows it. Both halves
    // are kept because neither one alone is evidence: a setter that appears to work but does not
    // survive a new process is not a correction.
    internal sealed class TerrainRebindReceipt
    {
        internal string EvidenceDirectory;
        internal string WorkingModelPath;
        internal string WorkingCopyId;
        internal string TargetPath;
        internal string TargetRelativePath;
        internal string TargetSHA256;
        internal GeneralTerrainBindObservation Bind;
        internal GeneralTerrainVerifyObservation Verify;
        internal int SolverRunInvocations;
        internal string Verdict;
        internal string Summary;
    }

    // One integrated preparation of a disposable copy: the declared inputs are resolved and copied,
    // the terrain reference is corrected inside that exact copy, the correction is persisted once and
    // proven from a separate process, and only then is the input inventory measured. The digest is
    // therefore of the bytes a calculation would actually open.
    internal sealed class GeneralPreparation
    {
        internal TerrainPreparation Terrain;
        internal TerrainRebindReceipt Bind;
        internal string InputDigest;
        internal string WorkingModelPath;
        internal string WorkingCopyId;
    }

    // Client-side orchestration for one bounded calculation of an already-configured scenario.
    //
    // Three separate RuntimeHost processes are used: a read-only discovery pass that reports the
    // model's declared external inputs, the calculation that opens a disposable copy, runs the
    // installed Bentley engine once and saves once, and a readback that reopens the saved copy and
    // reads the results without a solver. The engineer's own model is only ever read, never written.
    internal static class GeneralModelCalculationService
    {
        private const int DiscoverTimeoutMilliseconds = 240000;
        private const int CalculateTimeoutMilliseconds = 900000;
        private const int ReadTimeoutMilliseconds = 180000;
        private const int BindTimeoutMilliseconds = 300000;
        private const int PollMilliseconds = 250;

        // A Shapefile is only usable as a family; the model declares just the .shp member.
        private const string ShapefileExtension = ".shp";
        private static readonly string[] ShapefileExtensions = { ".shp", ".shx", ".dbf" };

        private static int active;

        internal static bool IsBusy => Volatile.Read(ref active) != 0;

        // The complete input family of the model. Only files that exist are declared, and the model
        // itself is mandatory. Stale result stores are deliberately not carried over: the copy must
        // contain inputs only, so nothing read back afterwards can come from an earlier run.
        internal static string[] Family(string modelPath)
        {
            string model = Path.GetFullPath(modelPath);
            var files = new List<string> { model };
            foreach (string suffix in new[] { ".dwh", ".profiles.dwh", ".sqlite" })
                if (File.Exists(model + suffix)) files.Add(model + suffix);
            return files.ToArray();
        }

        internal static void ValidateSource(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("Select a SewerGEMS model first.");
            string model = Path.GetFullPath(path);
            WorkingCopyIntegrity.RejectLinks(model);
            if (!model.EndsWith(".stsw", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Only a .stsw SewerGEMS model can be calculated.");
            if (!File.Exists(model))
                throw new FileNotFoundException("The selected model is no longer present.");
            foreach (string file in Family(model))
            {
                WorkingCopyIntegrity.RejectLinks(file);
                if (!File.Exists(file))
                    throw new FileNotFoundException("A required model file is missing: " + Path.GetFileName(file));
            }
        }

        private static List<string> Manifest(IEnumerable<string> paths)
        {
            var lines = new List<string>();
            foreach (string path in paths.OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
            {
                var info = new FileInfo(path);
                lines.Add(info.Exists
                    ? Path.GetFileName(path) + "|" + info.Length.ToString(CultureInfo.InvariantCulture) + "|" +
                      WorkingCopyIntegrity.Fingerprint(path).SHA256
                    : Path.GetFileName(path) + "|ABSENT");
            }
            return lines;
        }

        private static string RuntimeRoot()
        {
            var options = new OpenFlowsDiscoveryOptions(Environment.GetEnvironmentVariable("SEWERPILOT_OPENFLOWS_HOME"));
            var candidates = new OpenFlowsRuntimeDiscovery(new WindowsRuntimeLocationSource(),
                new InstalledRuntimeInspector()).Discover(options);
            var selected = new OpenFlowsRuntimeSelector().Select(candidates, options);
            if (selected.Status != OpenFlowsRuntimeSelectionStatus.Selected || selected.Candidate == null)
                throw new InvalidOperationException(
                    "No unambiguous installed Bentley OpenFlows runtime was found: " + selected.Reason);
            return selected.Candidate.InstallRoot;
        }

        private static string RuntimeHostExecutable()
        {
            string sibling = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SewerPilot.OpenFlows.RuntimeHost.exe");
            if (File.Exists(sibling)) return sibling;
            string candidate = P16AzadeganCalculationProof.DefaultRuntimeHostExecutable();
            if (candidate != null && File.Exists(candidate)) return candidate;
            throw new FileNotFoundException("The native calculation host is unavailable in this installation.");
        }

        private static void Write(string directory, string name, string content)
        {
            if (content != null) File.WriteAllText(Path.Combine(directory, name), content, new UTF8Encoding(false));
        }

        internal static GeneralCalculationReceipt Execute(string selectedSourceModelPath, Action<string> progress = null)
        {
            if (Interlocked.Exchange(ref active, 1) != 0)
                throw new InvalidOperationException("A calculation is already running.");
            try
            {
                ValidateSource(selectedSourceModelPath);
                string model = Path.GetFullPath(selectedSourceModelPath);
                string[] family = Family(model);

                string evidence = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SewerPilot", "Evidence", "General",
                    DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + "-" +
                    Guid.NewGuid().ToString("N"));
                if (Directory.Exists(evidence)) throw new IOException("Evidence directory collision.");
                Directory.CreateDirectory(evidence);

                string runtime = RuntimeRoot();
                string host = RuntimeHostExecutable();
                var core = OpenFlowsRuntimeCandidate.CoreAssemblyNames.Select(name =>
                {
                    string path = Path.Combine(runtime, name + ".dll");
                    return new HostMetadata
                    {
                        SimpleName = name,
                        Path = path,
                        SHA256 = WorkingCopyIntegrity.Fingerprint(path).SHA256
                    };
                }).ToArray();

                // The model's own declared external inputs are read from a disposable copy before the
                // final working copy is built, so a model that needs a terrain file is copied complete
                // instead of being discovered incomplete by the engine mid-run. That copy is then
                // rebound and verified in place, and the digest used below is measured afterwards.
                GeneralPreparation preparation = PrepareCopy(model, family, runtime, host, core, evidence, progress);
                TerrainPreparation prepared = preparation.Terrain;
                GeneralTerrainObservation discovery = prepared.Discovery;
                List<ResolvedTerrain> terrains = prepared.Terrains;
                string[] terrainSources = prepared.Sources;
                var before = Manifest(family.Concat(terrainSources));
                Write(evidence, "source-family-before.txt", string.Join(Environment.NewLine, before));

                var working = prepared.Working;
                string copyId = Path.GetFileName(working.DirectoryPath);
                string inputDigest = preparation.InputDigest;

                var calculate = new HostRequest
                {
                    SchemaVersion = 1,
                    RunId = Guid.NewGuid().ToString("N"),
                    Operation = GeneralModelCalculationHost.Operation,
                    TrustedTempRoot = Path.GetTempPath(),
                    WorkingCopyId = copyId,
                    WorkingModelPath = working.ModelPath,
                    RuntimeCandidateCount = 1,
                    DetectedArchitecture = "x64",
                    SelectedRuntime = runtime,
                    CoreFiles = core,
                    Diagnostics = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [GeneralModelCalculationHost.CopyIdKey] = copyId,
                        [GeneralModelCalculationHost.InventoryKey] = inputDigest,
                        [GeneralModelCalculationHost.PhaseKey] = GeneralModelCalculationHost.PhaseCalculate
                    }
                };
                GeneralModelCalculationHost.ValidateRequest(calculate);
                progress?.Invoke("Running the installed Bentley engine once on the disposable copy.");
                var calcResult = RunHost(calculate, host, evidence, "calculate-", CalculateTimeoutMilliseconds, null);

                string calcJson = Evidence(calcResult, evidence, "calculation-observation.json");
                var calc = RuntimeHostProtocol.Serializer().Deserialize<GeneralCalculationObservation>(calcJson);
                if (calc == null)
                    throw new InvalidDataException("The native calculation returned no observation.");
                if (!string.Equals(calc.Status, GeneralModelCalculationHost.CompletedStatus, StringComparison.Ordinal))
                    throw new InvalidDataException(DescribeFailure("Calculation", calc.Status, calc.FailureMessage,
                        calc.FailureOperation, calc.ErrorType, calc.ErrorMessage));
                if (calc.SolverRunInvocations != 1 || calc.SaveInvocations != 1 ||
                    !string.Equals(calc.ScenarioComputeStatus, "Success", StringComparison.Ordinal) ||
                    !calc.SaveSucceeded ||
                    !string.Equals(calc.SaveResultClassification, "PERSISTED", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(calc.CalculationEventId) ||
                    calc.ScenarioId.GetValueOrDefault() <= 0 || calc.CalculationOptionsId.GetValueOrDefault() <= 0 ||
                    string.IsNullOrWhiteSpace(calc.Engine))
                    throw new InvalidDataException("The native solve did not establish a persisted calculation.");

                // The persisted working copy must match the calculation's own output inventory.
                var outputInventory = P16AzadeganCalculationHost.Inventory(working.DirectoryPath);
                string outputDigest = P16AzadeganCalculationHost.InventoryDigest(outputInventory);
                if (!string.Equals(P16AzadeganCalculationHost.InventoryJson(outputInventory), calc.OutputInventoryJson,
                        StringComparison.Ordinal))
                    throw new InvalidDataException("The saved working copy does not match the calculation record.");

                if (!before.SequenceEqual(Manifest(family.Concat(terrainSources))))
                    throw new InvalidDataException(
                        "The original model files changed during the calculation. Acceptance refused.");
                Write(evidence, "source-family-after-calculate.txt",
                    string.Join(Environment.NewLine, Manifest(family.Concat(terrainSources))));

                progress?.Invoke("Reopening the calculated copy to read the results without a solver.");
                var read = new HostRequest
                {
                    SchemaVersion = 1,
                    RunId = Guid.NewGuid().ToString("N"),
                    Operation = GeneralModelCalculationHost.Operation,
                    TrustedTempRoot = Path.GetTempPath(),
                    WorkingCopyId = copyId,
                    WorkingModelPath = working.ModelPath,
                    RuntimeCandidateCount = 1,
                    DetectedArchitecture = "x64",
                    SelectedRuntime = runtime,
                    CoreFiles = core,
                    Diagnostics = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [GeneralModelCalculationHost.CopyIdKey] = copyId,
                        [GeneralModelCalculationHost.InventoryKey] = outputDigest,
                        [GeneralModelCalculationHost.PhaseKey] = GeneralModelCalculationHost.PhaseRead,
                        [GeneralModelCalculationHost.ScenarioKey] = calc.ScenarioId.GetValueOrDefault().ToString(CultureInfo.InvariantCulture),
                        [GeneralModelCalculationHost.OptionsKey] = calc.CalculationOptionsId.GetValueOrDefault().ToString(CultureInfo.InvariantCulture),
                        [GeneralModelCalculationHost.EngineKey] = calc.Engine,
                        [GeneralModelCalculationHost.EventKey] = calc.CalculationEventId
                    }
                };
                GeneralModelCalculationHost.ValidateRequest(read);
                var readResult = RunHost(read, host, evidence, "read-", ReadTimeoutMilliseconds, null);

                string readJson = Evidence(readResult, evidence, "readback-observation.json");
                var seen = RuntimeHostProtocol.Serializer().Deserialize<GeneralReadbackObservation>(readJson);
                if (seen == null)
                    throw new InvalidDataException("The native readback returned no observation.");
                if (!string.Equals(seen.Status, GeneralModelCalculationHost.ReadVerifiedStatus, StringComparison.Ordinal))
                    throw new InvalidDataException(DescribeFailure("Result readback", seen.Status, seen.FailureMessage,
                        seen.FailureOperation, seen.ErrorType, seen.ErrorMessage));

                // Same-run attribution: identical calculation event, identical scenario/engine, and an
                // inventory that still matches the persisted solve.
                if (!string.Equals(seen.CalculationEventId, calc.CalculationEventId, StringComparison.Ordinal) ||
                    seen.ScenarioId != calc.ScenarioId || seen.CalculationOptionsId != calc.CalculationOptionsId ||
                    !string.Equals(seen.Engine, calc.Engine, StringComparison.Ordinal) ||
                    !string.Equals(seen.ReopenedInventoryDigest, outputDigest, StringComparison.Ordinal) ||
                    seen.Rows == null || seen.Rows.Length == 0 || seen.AvailableRowCount <= 0)
                    throw new InvalidDataException("The readback is not attributable to this calculation run.");
                if (!string.Equals(P16AzadeganCalculationHost.InventoryDigest(
                        P16AzadeganCalculationHost.Inventory(working.DirectoryPath)), outputDigest, StringComparison.Ordinal))
                    throw new InvalidDataException("The calculated working copy changed during readback.");
                if (!before.SequenceEqual(Manifest(family.Concat(terrainSources))))
                    throw new InvalidDataException(
                        "The original model files changed during readback. Acceptance refused.");
                Write(evidence, "source-family-after.txt",
                    string.Join(Environment.NewLine, Manifest(family.Concat(terrainSources))));

                // The single acceptance decision, taken only now that the readback is verified and
                // attributed. Everything above establishes the facts; this decides what they are worth.
                GeneralAcceptance acceptance = GeneralCalculationAcceptance.Classify(calc, seen);
                Write(evidence, "acceptance.json", AcceptanceJson(acceptance, calc));

                if (string.Equals(acceptance.Verdict, GeneralCalculationAcceptance.Blocked, StringComparison.Ordinal))
                    throw new InvalidDataException("Calculation acceptance is BLOCKED (" + acceptance.Reason +
                        "): " + acceptance.Explanation);

                var receipt = new GeneralCalculationReceipt
                {
                    EvidenceDirectory = evidence,
                    WorkingModelPath = working.ModelPath,
                    ScenarioId = seen.ScenarioId.GetValueOrDefault(),
                    CalculationOptionsId = seen.CalculationOptionsId.GetValueOrDefault(),
                    Engine = seen.Engine,
                    CalculationEventId = seen.CalculationEventId,
                    ScenarioComputeStatus = calc.ScenarioComputeStatus,
                    ConduitCount = seen.ConduitCount,
                    StructureCount = seen.StructureCount,
                    Rows = seen.Rows,
                    Verdict = acceptance.Verdict,
                    AcceptanceReason = acceptance.Reason,
                    AcceptanceExplanation = acceptance.Explanation,
                    Summary = BuildSummary(calc, seen, Path.GetFileName(model), acceptance)
                };
                Write(evidence, "desktop-acceptance.txt", receipt.Summary);
                ApplicationDiagnostics.RecordMessage("General calculation " + acceptance.Verdict.ToLowerInvariant() +
                    " for the selected model: scenario " + receipt.ScenarioId + ", engine " + receipt.Engine +
                    ", event " + receipt.CalculationEventId + " (" + acceptance.Reason + ")");
                return receipt;
            }
            finally { Interlocked.Exchange(ref active, 0); }
        }

        // Reads what the model declares about its own external inputs and builds the disposable copy
        // with those inputs already in place. Nothing here solves: the copy is only inspected.
        private static TerrainPreparation PrepareTerrain(string model, string[] family, string runtime,
            string host, HostMetadata[] core, string evidence, Action<string> progress)
        {
            progress?.Invoke("Reading the model's declared external inputs.");
            GeneralTerrainObservation discovery = DiscoverTerrain(model, family, runtime, host, core, evidence);
            List<ResolvedTerrain> terrains = ResolveTerrain(model, discovery);
            List<ResolvedDataSource> dataSources = ResolveDataSources(model, discovery);
            progress?.Invoke("Preparing a disposable copy of the selected model.");
            var working = WorkingCopyIntegrity.Create(model, family, "Working");
            CopyTerrain(working.DirectoryPath, terrains, evidence);
            CopyDataSources(working.DirectoryPath, dataSources, evidence);
            return new TerrainPreparation
            {
                Discovery = discovery,
                Terrains = terrains,
                DataSources = dataSources,
                // Every source file the copy depends on travels in the integrity manifest, so a change
                // to an engineer's Shapefile is caught exactly like a change to the model or terrain.
                Sources = terrains.Select(item => item.SourcePath)
                    .Concat(dataSources.SelectMany(item => item.SourcePaths)).ToArray(),
                Working = working
            };
        }

        // Stage-A entry point: runs the exact preparation a calculation uses — declared-input
        // discovery, disposable copy, terrain rebinding, independent verification — and stops before
        // the solver. It exists so the integrated sequence can be proven without a native solve.
        internal static GeneralPreparation PrepareForCalculation(string modelPath, string evidenceDirectory,
            Action<string> progress = null)
        {
            string model = Path.GetFullPath(modelPath);
            ValidateSource(model);
            string[] family = Family(model);
            string runtime = RuntimeRoot();
            string host = RuntimeHostExecutable();
            HostMetadata[] core = Core(runtime);
            if (!Directory.Exists(evidenceDirectory)) Directory.CreateDirectory(evidenceDirectory);

            GeneralPreparation preparation = PrepareCopy(model, family, runtime, host, core, evidenceDirectory, progress);
            Write(evidenceDirectory, "preparation-summary.txt",
                "MODEL=" + model + Environment.NewLine +
                "WORKING_MODEL=" + preparation.WorkingModelPath + Environment.NewLine +
                "WORKING_COPY_ID=" + preparation.WorkingCopyId + Environment.NewLine +
                "TERRAIN_DEPENDENCIES=" + preparation.Terrain.Terrains.Count.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                "TERRAIN_SOURCES=" + string.Join(" ; ", preparation.Terrain.Sources) + Environment.NewLine +
                "REBOUND=" + (preparation.Bind == null ? "false" : "true") + Environment.NewLine +
                "INPUT_DIGEST=" + preparation.InputDigest + Environment.NewLine +
                "SOLVER_INVOCATIONS=0");
            return preparation;
        }

        // Verification entry point: performs the discovery pass and the dependency copy without any
        // solve, so the corrected working copy can be inspected before the budgeted calculation.
        internal static TerrainPreparation InspectTerrain(string modelPath, string evidenceDirectory)
        {
            string model = Path.GetFullPath(modelPath);
            ValidateSource(model);
            string[] family = Family(model);
            string runtime = RuntimeRoot();
            string host = RuntimeHostExecutable();
            var core = OpenFlowsRuntimeCandidate.CoreAssemblyNames.Select(name =>
            {
                string path = Path.Combine(runtime, name + ".dll");
                return new HostMetadata
                {
                    SimpleName = name,
                    Path = path,
                    SHA256 = WorkingCopyIntegrity.Fingerprint(path).SHA256
                };
            }).ToArray();
            if (!Directory.Exists(evidenceDirectory)) Directory.CreateDirectory(evidenceDirectory);
            TerrainPreparation prepared = PrepareTerrain(model, family, runtime, host, core, evidenceDirectory, null);

            // Reopen a read-only copy that now contains the resolved dependency. Comparing what the
            // vendor reports for the terrain before and after shows whether the copied input is
            // actually picked up, without any solver being involved.
            var withTerrain = WorkingCopyIntegrity.Create(model, family, "ReadOnly");
            foreach (ResolvedTerrain terrain in prepared.Terrains)
                foreach (string destination in Destinations(terrain))
                    WorkingCopyIntegrity.CopyDependency(withTerrain.DirectoryPath, terrain.SourcePath, destination);
            prepared.WithDependenciesPath = withTerrain.DirectoryPath;
            prepared.WithDependencies = DiscoverOnCopy(withTerrain, runtime, host, core, evidenceDirectory,
                "discover-with-terrain-", "terrain-discovery-with-terrain.json");
            return prepared;
        }

        // Reads what the model declares about its own external inputs. This is a separate read-only
        // host run against a disposable copy of the core family: zero solves, zero saves.
        private static GeneralTerrainObservation DiscoverTerrain(string model, string[] family, string runtime,
            string host, HostMetadata[] core, string evidence)
        {
            var copy = WorkingCopyIntegrity.Create(model, family, "ReadOnly");
            return DiscoverOnCopy(copy, runtime, host, core, evidence, "discover-", "terrain-discovery.json");
        }

        // The same read-only pass against a copy the caller already prepared, so the effect of adding
        // a declared dependency can be observed by reopening the copy that now contains it.
        private static GeneralTerrainObservation DiscoverOnCopy(WorkingCopyIntegrity copy, string runtime,
            string host, HostMetadata[] core, string evidence, string prefix, string fileName)
        {
            string copyId = Path.GetFileName(copy.DirectoryPath);
            string digest = P16AzadeganCalculationHost.InventoryDigest(
                P16AzadeganCalculationHost.Inventory(copy.DirectoryPath));
            var request = new HostRequest
            {
                SchemaVersion = 1,
                RunId = Guid.NewGuid().ToString("N"),
                Operation = GeneralModelCalculationHost.Operation,
                TrustedTempRoot = Path.GetTempPath(),
                WorkingCopyId = copyId,
                WorkingModelPath = copy.ModelPath,
                RuntimeCandidateCount = 1,
                DetectedArchitecture = "x64",
                SelectedRuntime = runtime,
                CoreFiles = core,
                Diagnostics = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [GeneralModelCalculationHost.CopyIdKey] = copyId,
                    [GeneralModelCalculationHost.InventoryKey] = digest,
                    [GeneralModelCalculationHost.PhaseKey] = GeneralModelCalculationHost.PhaseDiscover
                }
            };
            GeneralModelCalculationHost.ValidateRequest(request);
            var result = RunHost(request, host, evidence, prefix, DiscoverTimeoutMilliseconds, null);
            string json = Evidence(result, evidence, fileName,
                GeneralModelCalculationHost.TerrainEvidenceKey);
            var observation = RuntimeHostProtocol.Serializer().Deserialize<GeneralTerrainObservation>(json);
            if (observation == null)
                throw new InvalidDataException("The declared-input discovery returned no observation.");
            if (!string.Equals(observation.Status, GeneralModelCalculationHost.DiscoverStatus, StringComparison.Ordinal))
                throw new InvalidDataException(DescribeFailure("Declared-input discovery", observation.Status,
                    observation.FailureMessage, null, observation.ErrorType, observation.ErrorMessage));
            return observation;
        }

        // ------------------------------------------------------------------ rebinding and verification

        // Rebinds the active terrain reference onto the verified copy inside the disposable working
        // copy, persists once, then proves the correction in a brand new process. No solver is
        // invoked anywhere on this path.
        internal static TerrainRebindReceipt RebindTerrain(string modelPath, string evidenceDirectory,
            Action<string> progress = null)
        {
            string model = Path.GetFullPath(modelPath);
            ValidateSource(model);
            string[] family = Family(model);
            string runtime = RuntimeRoot();
            string host = RuntimeHostExecutable();
            HostMetadata[] core = Core(runtime);
            if (!Directory.Exists(evidenceDirectory)) Directory.CreateDirectory(evidenceDirectory);

            progress?.Invoke("Reading the model's declared external inputs.");
            TerrainPreparation prepared = PrepareTerrain(model, family, runtime, host, core, evidenceDirectory, progress);
            return BindAndVerifyPreparedTerrain(prepared, runtime, host, core, evidenceDirectory, progress);
        }

        // The correction itself, applied to a copy the caller already built. Both the standalone
        // rebinding entry point and the calculation path call this, so the copy that is solved is
        // always the copy whose reference was verified. One bind, one save, one independent reopen;
        // zero solver invocations.
        private static TerrainRebindReceipt BindAndVerifyPreparedTerrain(TerrainPreparation prepared,
            string runtime, string host, HostMetadata[] core, string evidence, Action<string> progress)
        {
            if (prepared.Terrains.Count == 0)
                throw new InvalidDataException(
                    "The selected model declares no external terrain, so there is no reference to rebind.");
            if (prepared.Terrains.Count > 1)
                throw new InvalidDataException("The selected model declares " + prepared.Terrains.Count +
                    " external inputs; a single terrain reference cannot be chosen without guessing.");
            ResolvedTerrain terrain = prepared.Terrains[0];

            string working = prepared.Working.DirectoryPath;
            string target = TargetInside(working, terrain);
            string targetRelative = Relative(working, target);
            string targetHash = WorkingCopyIntegrity.Fingerprint(target).SHA256;
            if (!string.Equals(targetHash, terrain.SHA256, StringComparison.Ordinal))
                throw new InvalidDataException("The terrain inside the working copy does not match its verified source bytes.");

            var receipt = new TerrainRebindReceipt
            {
                EvidenceDirectory = Path.GetFullPath(evidence),
                WorkingModelPath = prepared.Working.ModelPath,
                WorkingCopyId = Path.GetFileName(working),
                TargetPath = target,
                TargetRelativePath = targetRelative,
                TargetSHA256 = targetHash
            };

            progress?.Invoke("Correcting the terrain reference inside the disposable copy.");
            string dataSources = DataSourceManifestText(prepared);
            HostResult bindResult = RunTerrainPhase(prepared.Working, runtime, host, core, evidence,
                GeneralModelCalculationHost.PhaseBind, target, dataSources, "bind-", "terrain-bind.json",
                BindTimeoutMilliseconds, GeneralModelCalculationHost.BindEvidenceKey);
            receipt.SolverRunInvocations += bindResult.SolverRunInvocationCount;
            receipt.Bind = RuntimeHostProtocol.Serializer().Deserialize<GeneralTerrainBindObservation>(
                Evidence(bindResult, evidence, "terrain-bind-observation.json", GeneralModelCalculationHost.BindEvidenceKey));
            if (receipt.Bind == null)
                throw new InvalidDataException("The terrain rebinding returned no observation.");
            if (!string.Equals(receipt.Bind.Status, GeneralModelCalculationHost.BindStatus, StringComparison.Ordinal))
            {
                receipt.Verdict = "BLOCKED_REBIND_FAILED";
                receipt.Summary = DescribeFailure("Terrain rebinding", receipt.Bind.Status,
                    receipt.Bind.FailureMessage, receipt.Bind.FailureOperation, receipt.Bind.ErrorType,
                    receipt.Bind.ErrorMessage);
                return receipt;
            }

            // The copy on disk changed when it was saved, so the verification pass opens the same
            // model through a brand new process with a freshly computed inventory digest.
            progress?.Invoke("Reopening the persisted copy in a separate process to verify the reference.");
            HostResult verifyResult = RunTerrainPhase(prepared.Working, runtime, host, core, evidence,
                GeneralModelCalculationHost.PhaseVerify, target, dataSources, "verify-", "terrain-verify.json",
                ReadTimeoutMilliseconds, GeneralModelCalculationHost.VerifyEvidenceKey);
            receipt.SolverRunInvocations += verifyResult.SolverRunInvocationCount;
            receipt.Verify = RuntimeHostProtocol.Serializer().Deserialize<GeneralTerrainVerifyObservation>(
                Evidence(verifyResult, evidence, "terrain-verify-observation.json", GeneralModelCalculationHost.VerifyEvidenceKey));
            if (receipt.Verify == null)
                throw new InvalidDataException("The terrain verification returned no observation.");

            bool persisted = string.Equals(receipt.Verify.Status, GeneralModelCalculationHost.VerifyStatus, StringComparison.Ordinal) &&
                string.Equals(receipt.Verify.TargetSHA256, terrain.SHA256, StringComparison.Ordinal) &&
                receipt.Verify.DataSourceReferencesVerified;
            receipt.Verdict = persisted
                ? "TERRAIN_REFERENCE_REBOUND_AND_VERIFIED"
                : "BLOCKED_REBIND_NOT_PERSISTED";
            receipt.Summary = persisted
                ? "The active terrain reference and the two-dimensional data-source references now point at " +
                  targetRelative + " and its Data Files family inside the disposable copy, and survived a " +
                  "separate reopen."
                : DescribeFailure("Terrain verification", receipt.Verify.Status, receipt.Verify.FailureMessage,
                    null, receipt.Verify.ErrorType, receipt.Verify.ErrorMessage);
            return receipt;
        }

        // The integrated preparation a calculation must pass before any solver exists: dependencies
        // resolved and copied, reference corrected and persisted, correction proven by a separate
        // process, and the input digest measured afterwards. A model that declares no external
        // terrain keeps its previous no-terrain behaviour and runs no rebinding at all.
        private static GeneralPreparation PrepareCopy(string model, string[] family, string runtime, string host,
            HostMetadata[] core, string evidence, Action<string> progress)
        {
            TerrainPreparation prepared = PrepareTerrain(model, family, runtime, host, core, evidence, progress);
            TerrainRebindReceipt bind = null;
            if (prepared.Terrains.Count > 0)
            {
                bind = BindAndVerifyPreparedTerrain(prepared, runtime, host, core, evidence, progress);
                RequireReboundTerrain(bind, prepared.Terrains[0], prepared.Working.DirectoryPath);
                Write(evidence, "terrain-rebind-receipt.txt", Receipt(bind));
            }

            // Measured only now, so the calculation is attributed to the rebound, persisted copy and
            // never to the pre-binding bytes.
            var inventory = P16AzadeganCalculationHost.Inventory(prepared.Working.DirectoryPath);
            string digest = P16AzadeganCalculationHost.InventoryDigest(inventory);
            Write(evidence, "input-inventory-after-preparation.txt",
                "DIGEST=" + digest + Environment.NewLine +
                "REBOUND=" + (bind == null ? "false" : "true") + Environment.NewLine +
                P16AzadeganCalculationHost.InventoryJson(inventory));

            return new GeneralPreparation
            {
                Terrain = prepared,
                Bind = bind,
                InputDigest = digest,
                WorkingModelPath = prepared.Working.ModelPath,
                WorkingCopyId = Path.GetFileName(prepared.Working.DirectoryPath)
            };
        }

        // Fails closed unless the copy that would be calculated holds a corrected, persisted and
        // independently verified reference to the terrain it actually contains.
        private static void RequireReboundTerrain(TerrainRebindReceipt receipt, ResolvedTerrain terrain,
            string workingDirectory)
        {
            if (receipt == null || receipt.Bind == null || receipt.Verify == null ||
                !string.Equals(receipt.Verdict, "TERRAIN_REFERENCE_REBOUND_AND_VERIFIED", StringComparison.Ordinal))
                throw new InvalidDataException(
                    "The calculation copy does not hold a rebound and verified terrain reference. " +
                    (receipt == null ? "No rebinding was performed." : receipt.Summary ?? receipt.Verdict));
            if (receipt.SolverRunInvocations != 0)
                throw new InvalidDataException(
                    "The terrain rebinding invoked the solver " + receipt.SolverRunInvocations +
                    " time(s); preparation must never solve.");
            if (!receipt.Bind.SaveSucceeded ||
                !string.Equals(receipt.Bind.SaveResultClassification, "PERSISTED", StringComparison.Ordinal) ||
                receipt.Bind.SaveQuestionCount != 0)
                throw new InvalidDataException(
                    "The terrain reference was corrected in memory but not persisted to the calculation copy.");
            if (!receipt.Verify.TargetExists || !receipt.Verify.AbsolutePathMatchesTarget ||
                !receipt.Verify.RelativePathResolves || receipt.Verify.AuthoringPathStillRequired)
                throw new InvalidDataException(
                    "The reopened calculation copy does not resolve its terrain from inside itself.");
            if (!string.Equals(receipt.Verify.TargetSHA256, terrain.SHA256, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "The terrain inside the calculation copy does not match the verified source bytes.");
            if (!string.IsNullOrWhiteSpace(receipt.Verify.ProxyPath) &&
                !SameFile(receipt.Verify.ProxyPath, receipt.TargetPath, workingDirectory))
                throw new InvalidDataException(
                    "The stored document proxy still names a location other than the verified terrain inside the copy.");
            if (!receipt.Verify.DataSourceReferencesVerified)
                throw new InvalidDataException(
                    "The reopened calculation copy does not resolve its two-dimensional data sources from inside itself.");
            foreach (GeneralDataSourceVerification source in
                receipt.Verify.DataSources ?? new GeneralDataSourceVerification[0])
            {
                if (!source.TargetExists || !source.FamilyVerified || source.ExternalPathRequired ||
                    !source.ProxyInsideCopy || !source.RelativePathResolves)
                    throw new InvalidDataException("The two-dimensional data source " + source.ElementId +
                        " does not resolve to a verified Shapefile family inside the calculation copy.");
            }
        }

        private static bool SameFile(string stored, string target, string root)
        {
            if (string.IsNullOrWhiteSpace(stored)) return false;
            string full;
            try { full = Path.IsPathRooted(stored) ? Path.GetFullPath(stored) : Path.GetFullPath(Path.Combine(root, stored)); }
            catch (Exception) { return false; }
            return string.Equals(full, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
        }

        private static string Receipt(TerrainRebindReceipt receipt)
        {
            var text = new StringBuilder();
            text.AppendLine("VERDICT=" + receipt.Verdict);
            text.AppendLine("WORKING_MODEL=" + receipt.WorkingModelPath);
            text.AppendLine("WORKING_COPY_ID=" + receipt.WorkingCopyId);
            text.AppendLine("TARGET=" + receipt.TargetPath);
            text.AppendLine("TARGET_RELATIVE=" + receipt.TargetRelativePath);
            text.AppendLine("TARGET_SHA256=" + receipt.TargetSHA256);
            text.AppendLine("BIND_STATUS=" + (receipt.Bind == null ? "(none)" : receipt.Bind.Status));
            text.AppendLine("BIND_SAVE=" + (receipt.Bind == null ? "(none)" :
                receipt.Bind.SaveResultClassification + "/questions=" +
                receipt.Bind.SaveQuestionCount.ToString(CultureInfo.InvariantCulture) + "/at=" + receipt.Bind.SaveTimestamp));
            text.AppendLine("BIND_FIELDS=" + (receipt.Bind == null || receipt.Bind.FieldDiagnostics == null
                ? "(none)" : string.Join(",", receipt.Bind.FieldDiagnostics)));
            text.AppendLine("BIND_PROXY=" + (receipt.Bind == null ? "(none)" :
                (receipt.Bind.ProxyPathBefore ?? "(none)") + " -> " + (receipt.Bind.ProxyPathAfter ?? "(none)")));
            text.AppendLine("VERIFY_STATUS=" + (receipt.Verify == null ? "(none)" : receipt.Verify.Status));
            text.AppendLine("VERIFY_ABSOLUTE_MATCH=" + (receipt.Verify != null && receipt.Verify.AbsolutePathMatchesTarget));
            text.AppendLine("VERIFY_RELATIVE_RESOLVES=" + (receipt.Verify != null && receipt.Verify.RelativePathResolves));
            text.AppendLine("VERIFY_AUTHORING_PATH_REQUIRED=" + (receipt.Verify != null && receipt.Verify.AuthoringPathStillRequired));
            text.AppendLine("VERIFY_PROXY=" + (receipt.Verify == null ? "(none)" : receipt.Verify.ProxyPath ?? "(none)"));
            text.AppendLine("DATA_SOURCE_ELEMENTS=" + (receipt.Bind == null ? "(none)" :
                receipt.Bind.DataSourceElementCount.ToString(CultureInfo.InvariantCulture)));
            if (receipt.Bind != null && receipt.Bind.DataSources != null)
            {
                foreach (GeneralDataSourceBinding source in receipt.Bind.DataSources)
                {
                    text.AppendLine("DATA_SOURCE_BIND=" + source.ElementId +
                        "|before=" + (source.ProxyPathBefore ?? "(none)") +
                        "|after=" + (source.ProxyPathAfter ?? "(none)") +
                        "|target=" + (source.TargetRelativePath ?? "(none)") +
                        "|proxyWritten=" + source.ProxyWritten +
                        "|relativeWritten=" + source.RelativePathWritten +
                        "|familyVerified=" + source.FamilyVerified);
                }
            }
            if (receipt.Verify != null && receipt.Verify.DataSources != null)
            {
                foreach (GeneralDataSourceVerification source in receipt.Verify.DataSources)
                {
                    text.AppendLine("DATA_SOURCE_VERIFY=" + source.ElementId +
                        "|proxy=" + (source.ProxyPath ?? "(none)") +
                        "|target=" + (source.TargetRelativePath ?? "(none)") +
                        "|inside=" + source.ProxyInsideCopy +
                        "|relativeResolves=" + source.RelativePathResolves +
                        "|familyVerified=" + source.FamilyVerified);
                }
            }
            text.AppendLine("SOLVER_INVOCATIONS=" + receipt.SolverRunInvocations.ToString(CultureInfo.InvariantCulture));
            text.Append(receipt.Summary);
            return text.ToString();
        }

        // The verified copy of each Shapefile family, in the form the binding and verification passes
        // read: the declared relative destination followed by the three member fingerprints. The host
        // never guesses a path or a hash; it checks the copy against what was verified here.
        private static string DataSourceManifestText(TerrainPreparation prepared)
        {
            if (prepared.DataSources == null || prepared.DataSources.Count == 0) return null;
            return string.Join("\n", prepared.DataSources.Select(item =>
                item.RelativeDestination + "|" + item.SHA256[0] + "|" + item.SHA256[1] + "|" + item.SHA256[2]));
        }

        // One host process for one terrain phase. A new run identity is always generated, so the
        // verification pass can never share a process with the pass that wrote the reference.
        private static HostResult RunTerrainPhase(WorkingCopyIntegrity copy, string runtime, string host,
            HostMetadata[] core, string evidence, string phase, string target, string dataSources,
            string prefix, string fileName, int timeout, string evidenceKey)
        {
            string copyId = Path.GetFileName(copy.DirectoryPath);
            string digest = P16AzadeganCalculationHost.InventoryDigest(
                P16AzadeganCalculationHost.Inventory(copy.DirectoryPath));
            var diagnostics = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [GeneralModelCalculationHost.CopyIdKey] = copyId,
                [GeneralModelCalculationHost.InventoryKey] = digest,
                [GeneralModelCalculationHost.PhaseKey] = phase,
                [GeneralModelCalculationHost.TargetKey] = target
            };
            // The binding and verification passes are told exactly which Shapefile families were
            // verified, so neither can bind to, or accept, a file it has not checked.
            if (!string.IsNullOrWhiteSpace(dataSources))
                diagnostics[GeneralModelCalculationHost.DataSourceManifestKey] = dataSources;
            var request = new HostRequest
            {
                SchemaVersion = 1,
                RunId = Guid.NewGuid().ToString("N"),
                Operation = GeneralModelCalculationHost.Operation,
                TrustedTempRoot = Path.GetTempPath(),
                WorkingCopyId = copyId,
                WorkingModelPath = copy.ModelPath,
                RuntimeCandidateCount = 1,
                DetectedArchitecture = "x64",
                SelectedRuntime = runtime,
                CoreFiles = core,
                Diagnostics = diagnostics
            };
            GeneralModelCalculationHost.ValidateRequest(request);
            return RunHost(request, host, evidence, prefix, timeout, null);
        }

        // The copy of the terrain the reference should name. The model's own declared relative
        // location is preferred because it preserves the layout the project already expects.
        internal static string TargetInside(string workingDirectory, ResolvedTerrain terrain)
        {
            foreach (string destination in Destinations(terrain))
            {
                string path = Path.GetFullPath(Path.Combine(workingDirectory, destination));
                if (File.Exists(path)) return path;
            }
            throw new InvalidDataException("The verified terrain was not found inside the working copy.");
        }

        private static HostMetadata[] Core(string runtime) =>
            OpenFlowsRuntimeCandidate.CoreAssemblyNames.Select(name =>
            {
                string path = Path.Combine(runtime, name + ".dll");
                return new HostMetadata
                {
                    SimpleName = name,
                    Path = path,
                    SHA256 = WorkingCopyIntegrity.Fingerprint(path).SHA256
                };
            }).ToArray();

        // Turns the model's declared references into files that actually exist here. Nothing is
        // assumed: a candidate must be a real file of the declared type, reachable without a
        // filesystem link, and the surviving candidates must all be the same bytes.
        internal static List<ResolvedTerrain> ResolveTerrain(string modelPath, GeneralTerrainObservation discovery)
        {
            var resolved = new List<ResolvedTerrain>();
            if (discovery == null || discovery.Dependencies == null) return resolved;
            string directory = Path.GetDirectoryName(Path.GetFullPath(modelPath));
            foreach (GeneralTerrainDependency dependency in discovery.Dependencies)
            {
                if (dependency == null || dependency.IsActive == false) continue;
                string declaredName = GeneralModelCalculationHost.FileName(dependency.DeclaredAbsolutePath) ??
                                      GeneralModelCalculationHost.FileName(dependency.DeclaredRelativePath);
                string declaredRelative = NormalizeDeclaredRelative(dependency.DeclaredRelativePath, dependency.ElementId);
                string declared = declaredName ?? declaredRelative;
                string extension = declared == null ? "" : Path.GetExtension(declared);
                if (extension.Length == 0)
                    throw new InvalidDataException(
                        "A declared external input has no recognisable file type (element " + dependency.ElementId + ").");

                var candidates = new List<string>();
                AddCandidate(candidates, dependency.DeclaredAbsolutePath, extension);
                if (declaredRelative != null) AddCandidate(candidates, Path.Combine(directory, declaredRelative), extension);
                if (declaredName != null) AddCandidate(candidates, Path.Combine(directory, declaredName), extension);
                if (candidates.Count == 0)
                    throw new InvalidDataException("A declared external input is not present on this computer, so the model " +
                        "cannot be calculated safely. Declared locations: " +
                        (dependency.DeclaredAbsolutePath ?? "(none)") + " ; " + (dependency.DeclaredRelativePath ?? "(none)") + ".");

                string[] fingerprints = candidates.Select(path => WorkingCopyIntegrity.Fingerprint(path).SHA256)
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (fingerprints.Length > 1)
                    throw new InvalidDataException("A declared external input matches more than one distinct file, so the " +
                        "correct one cannot be established: " + string.Join(" ; ", candidates) + ".");

                resolved.Add(new ResolvedTerrain
                {
                    SourcePath = candidates[0],
                    DeclaredAbsolutePath = dependency.DeclaredAbsolutePath,
                    DeclaredRelativePath = dependency.DeclaredRelativePath,
                    RelativeDestination = declaredRelative ?? declaredName,
                    BareDestination = declaredName ?? declaredRelative,
                    SHA256 = fingerprints[0],
                    Bytes = new FileInfo(candidates[0]).Length
                });
            }
            return resolved;
        }

        private static void AddCandidate(List<string> candidates, string path, string extension)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception) { return; }
            if (!string.Equals(Path.GetExtension(full), extension, StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(full)) return;
            WorkingCopyIntegrity.RejectLinks(full);
            if (candidates.Any(item => string.Equals(item, full, StringComparison.OrdinalIgnoreCase))) return;
            candidates.Add(full);
        }

        // A declared relative path is used verbatim only when it stays inside the model directory. A
        // rooted path or any '..' segment is refused rather than normalised into something else.
        private static string NormalizeDeclaredRelative(string declared, int elementId)
        {
            if (string.IsNullOrWhiteSpace(declared)) return null;
            string text = declared.Trim().Replace('/', '\\');
            if (Path.IsPathRooted(text) || text.IndexOf(':') >= 0)
                throw new InvalidDataException(
                    "A declared external input reference is not a relative path (element " + elementId + ").");
            string[] parts = text.Split('\\').Where(part => part.Length > 0 && part != ".").ToArray();
            if (parts.Length == 0 || parts.Any(part => part == ".."))
                throw new InvalidDataException(
                    "A declared external input reference may not leave the model directory (element " + elementId + ").");
            return string.Join("\\", parts);
        }

        private static void CopyTerrain(string workingDirectory, List<ResolvedTerrain> terrains, string evidence)
        {
            if (terrains.Count == 0) return;
            var lines = new List<string>();
            foreach (ResolvedTerrain terrain in terrains)
            {
                var copied = new List<string>();
                foreach (string destination in Destinations(terrain))
                    copied.Add(WorkingCopyIntegrity.CopyDependency(workingDirectory, terrain.SourcePath, destination));
                terrain.CopiedTo = copied.ToArray();
                lines.Add(Path.GetFileName(terrain.SourcePath) + "|" + terrain.Bytes + "|" + terrain.SHA256 + "|" +
                    string.Join(" ; ", copied.Select(path => Relative(workingDirectory, path))));
            }
            Write(evidence, "working-copy-terrain.txt", string.Join(Environment.NewLine, lines));
        }

        // A Shapefile is a family: geometry, its index and its attribute table only work together, so
        // the three files travel as a unit. The declared relative location is authoritative, and every
        // record that names the same file collapses into a single copy.
        internal static List<ResolvedDataSource> ResolveDataSources(string modelPath,
            GeneralTerrainObservation discovery)
        {
            var resolved = new List<ResolvedDataSource>();
            if (discovery == null || discovery.DataSources == null) return resolved;
            string directory = Path.GetDirectoryName(Path.GetFullPath(modelPath));
            foreach (GeneralTerrainDependency dependency in discovery.DataSources)
            {
                if (dependency == null) continue;
                string declaredName = GeneralModelCalculationHost.FileName(dependency.DeclaredAbsolutePath) ??
                                      GeneralModelCalculationHost.FileName(dependency.DeclaredRelativePath);
                if (declaredName == null) continue;
                if (!declaredName.EndsWith(ShapefileExtension, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A declared 2D data source is not a Shapefile (element " +
                        dependency.ElementId + ").");
                string relative = NormalizeDeclaredRelative(dependency.DeclaredRelativePath, dependency.ElementId) ??
                                  declaredName;
                string shapefile = LocateShapefile(directory, dependency, relative, declaredName);
                ResolvedDataSource existing = resolved.FirstOrDefault(item =>
                    string.Equals(item.SourcePath, shapefile, StringComparison.OrdinalIgnoreCase));
                if (existing != null) { existing.ReferenceCount++; continue; }
                resolved.Add(new ResolvedDataSource
                {
                    SourcePath = shapefile,
                    DeclaredRelativePath = relative,
                    DeclaredAbsolutePath = dependency.DeclaredAbsolutePath,
                    RelativeDestination = relative,
                    ReferenceCount = 1
                });
            }
            foreach (ResolvedDataSource source in resolved) CompleteDataSource(source);
            return resolved;
        }

        private static string LocateShapefile(string directory, GeneralTerrainDependency dependency,
            string relative, string declaredName)
        {
            var candidates = new List<string>();
            AddShapefileCandidate(candidates, dependency.DeclaredAbsolutePath);
            AddShapefileCandidate(candidates, Path.Combine(directory, relative));
            AddShapefileCandidate(candidates, Path.Combine(directory, declaredName));
            if (candidates.Count == 0)
                throw new InvalidDataException("A declared 2D data source is not present on this computer, so the " +
                    "model cannot be calculated safely. Declared location: " +
                    (dependency.DeclaredAbsolutePath ?? dependency.DeclaredRelativePath ?? "(none)") + ".");
            string[] fingerprints = candidates.Select(path => WorkingCopyIntegrity.Fingerprint(path).SHA256)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (fingerprints.Length > 1)
                throw new InvalidDataException("A declared 2D data source matches more than one distinct file, so " +
                    "the correct one cannot be established: " + string.Join(" ; ", candidates) + ".");
            return candidates[0];
        }

        private static void AddShapefileCandidate(List<string> candidates, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception) { return; }
            if (!string.Equals(Path.GetExtension(full), ShapefileExtension, StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(full)) return;
            WorkingCopyIntegrity.RejectLinks(full);
            if (candidates.Any(item => string.Equals(item, full, StringComparison.OrdinalIgnoreCase))) return;
            candidates.Add(full);
        }

        // The sidecar files are located from the Shapefile itself rather than from the declaration,
        // because the model names only the .shp. A family that is missing a member is refused.
        private static void CompleteDataSource(ResolvedDataSource source)
        {
            string stem = source.SourcePath.Substring(0, source.SourcePath.Length - ShapefileExtension.Length);
            var paths = new List<string>();
            var hashes = new List<string>();
            foreach (string extension in ShapefileExtensions)
            {
                string path = stem + extension;
                if (!File.Exists(path))
                    throw new InvalidDataException("A declared Shapefile is incomplete: the " + extension +
                        " file is missing next to " + Path.GetFileName(source.SourcePath) + ".");
                WorkingCopyIntegrity.RejectLinks(path);
                paths.Add(path);
                hashes.Add(WorkingCopyIntegrity.Fingerprint(path).SHA256);
            }
            source.SourcePaths = paths.ToArray();
            source.SHA256 = hashes.ToArray();
        }

        private static void CopyDataSources(string workingDirectory, List<ResolvedDataSource> sources,
            string evidence)
        {
            if (sources.Count == 0) return;
            var lines = new List<string>();
            foreach (ResolvedDataSource source in sources)
            {
                var copied = new List<string>();
                for (int index = 0; index < ShapefileExtensions.Length; index++)
                {
                    string destination = Path.ChangeExtension(source.RelativeDestination, ShapefileExtensions[index]);
                    string target = WorkingCopyIntegrity.CopyDependency(workingDirectory, source.SourcePaths[index],
                        destination);
                    if (!string.Equals(WorkingCopyIntegrity.Fingerprint(target).SHA256, source.SHA256[index],
                            StringComparison.Ordinal))
                        throw new InvalidDataException(
                            "A copied Shapefile does not match its verified source bytes: " +
                            Relative(workingDirectory, target));
                    copied.Add(target);
                }
                source.DestinationPaths = copied.ToArray();
                lines.Add(Path.GetFileName(source.SourcePath) + "|references=" +
                    source.ReferenceCount.ToString(CultureInfo.InvariantCulture) + "|" +
                    string.Join(" ; ", source.SHA256) + "|" +
                    string.Join(" ; ", copied.Select(path => Relative(workingDirectory, path))));
            }
            Write(evidence, "working-copy-data-sources.txt", string.Join(Environment.NewLine, lines));
        }

        // Where a verified dependency is placed inside a copy. The declared relative location is
        // authoritative: the model's own reference is preserved. The vendor also resolves a document
        // reference by file name in the model directory, and every shipped model of this family relies
        // on that fallback, so both locations are supplied from the same verified bytes.
        internal static List<string> Destinations(ResolvedTerrain terrain)
        {
            var destinations = new List<string>();
            if (!string.IsNullOrEmpty(terrain.RelativeDestination)) destinations.Add(terrain.RelativeDestination);
            if (!string.IsNullOrEmpty(terrain.BareDestination) &&
                !destinations.Any(item => string.Equals(item, terrain.BareDestination, StringComparison.OrdinalIgnoreCase)))
                destinations.Add(terrain.BareDestination);
            return destinations;
        }

        private static string Relative(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full.Substring(prefix.Length) : full;
        }

        private static string Evidence(HostResult result, string directory, string fileName) =>
            Evidence(result, directory, fileName, GeneralModelCalculationHost.EvidenceKey);

        private static string Evidence(HostResult result, string directory, string fileName, string key)
        {
            string json;
            if (!result.Diagnostics.TryGetValue(key, out json) || string.IsNullOrEmpty(json))
                throw new InvalidDataException("The native host returned no calculation observation.");
            Write(directory, fileName, json);
            return json;
        }

        private static string DescribeFailure(string stage, string status, string message, string operation,
            string errorType, string errorMessage)
        {
            var text = new StringBuilder();
            text.Append(stage).Append(" did not complete (").Append(status ?? "UNKNOWN").Append(").");
            if (!string.IsNullOrWhiteSpace(message)) text.Append(' ').Append(message);
            if (!string.IsNullOrWhiteSpace(operation)) text.Append(" [operation: ").Append(operation).Append(']');
            if (!string.IsNullOrWhiteSpace(errorType)) text.Append(" [").Append(errorType).Append(']');
            if (!string.IsNullOrWhiteSpace(errorMessage)) text.Append(' ').Append(errorMessage);
            return text.ToString();
        }

        // Machine-readable acceptance record, written beside the observations it was derived from so the
        // verdict can never be reconstructed differently by a later reader.
        private static string AcceptanceJson(GeneralAcceptance acceptance, GeneralCalculationObservation calc)
        {
            var record = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["verdict"] = acceptance.Verdict,
                ["reason"] = acceptance.Reason,
                ["explanation"] = acceptance.Explanation,
                ["warnings_unresolved"] = acceptance.WarningsUnresolved,
                ["warning_details_available"] = acceptance.WarningDetailsAvailable,
                ["warning_capture_status"] = calc.WarningCaptureStatus ?? "NOT_RECORDED",
                ["warning_exception_type"] = calc.WarningExceptionType ?? calc.ErrorType,
                ["warning_notification_count"] = calc.WarningNotificationCount,
                ["warning_capture_failure"] = calc.WarningCaptureFailure,
                ["engine_outcome"] = calc.EngineOutcome,
                ["warnings"] = calc.Warnings
            };
            return RuntimeHostProtocol.Serializer().Serialize(record);
        }

        private static string BuildSummary(GeneralCalculationObservation calc, GeneralReadbackObservation seen,
            string sourceName, GeneralAcceptance acceptance)
        {
            var text = new StringBuilder();
            text.AppendLine("BENTLEY HYDRAULIC CALCULATION — same-run native result");
            text.AppendLine("Model (working copy of the selected file): " + sourceName);
            text.AppendLine("Scenario: " + seen.ScenarioId + "  |  Calculation options: " + seen.CalculationOptionsId +
                "  |  Engine: " + seen.Engine);
            text.AppendLine("Engine resolution: " + (calc.EngineSelectionBasis ?? "NOT_RECORDED"));
            text.AppendLine("Solver status: " + calc.ScenarioComputeStatus +
                "  |  Solves: " + calc.SolverRunInvocations + "  |  Saves: " + calc.SaveInvocations);
            text.AppendLine("Calculation event: " + seen.CalculationEventId);
            text.AppendLine("Elements read: " + seen.ConduitCount + " conduits, " + seen.StructureCount + " structures");
            text.AppendLine("Result rows: " + seen.RowCount + " (" + seen.AvailableRowCount + " available)");
            text.AppendLine("ACCEPTANCE: " + acceptance.Verdict + "  |  " + acceptance.Reason);
            text.AppendLine("  " + acceptance.Explanation);
            // Notifications are printed whenever they were captured and they matter to the verdict: an
            // unresolved warning, or an accepted informational notice. An informational PASS must still
            // show what the vendor said, or the report would read as an exception-free run.
            bool informational = string.Equals(acceptance.Reason,
                GeneralCalculationAcceptance.InformationalNotificationsVerifiedReadback, StringComparison.Ordinal);
            if (acceptance.WarningsUnresolved || informational)
            {
                text.AppendLine("  Engine " + (informational ? "notifications" : "warnings") + ": " +
                    (calc.WarningNotificationCount.HasValue
                        ? calc.WarningNotificationCount.Value.ToString(CultureInfo.InvariantCulture) + " captured"
                        : "details " + (calc.WarningCaptureStatus ?? "NOT_RECORDED")));
                if (informational)
                    text.AppendLine("  Notification exception: " + (calc.WarningExceptionType ?? calc.ErrorType ??
                        "(not recorded)") + " (informational only, hydraulics preserved)");
                if (calc.Warnings != null)
                {
                    foreach (GeneralEngineWarning warning in calc.Warnings)
                    {
                        if (warning == null) continue;
                        text.AppendLine("    - " + (warning.Level ?? "?") + " " + (warning.MessageKey ?? "(no key)") +
                            (warning.ElementType == null ? "" : " on " + warning.ElementType) +
                            (warning.ElementId.HasValue ? " " + warning.ElementId.Value : "") +
                            (warning.Parameters == null || warning.Parameters.Length == 0
                                ? "" : " [" + string.Join(", ", warning.Parameters) + "]"));
                    }
                }
            }
            if (!string.IsNullOrEmpty(calc.TerrainRequirement))
                text.AppendLine("Declared external terrain: " + calc.TerrainRequirement +
                    (calc.ResolvedTerrainPaths == null || calc.ResolvedTerrainPaths.Length == 0
                        ? ""
                        : " (" + string.Join(", ", calc.ResolvedTerrainPaths.Select(Path.GetFileName)) + ")"));
            text.AppendLine();
            text.AppendLine("PROVENANCE: " + seen.Provenance);
            text.AppendLine();
            text.AppendLine("This result belongs to the calculation event above. It is not a design approval and it");
            text.AppendLine("does not extend to any other model, scenario or engine.");
            if (!string.IsNullOrEmpty(calc.NotificationCaptureLimitation))
                text.AppendLine("Limitation: " + calc.NotificationCaptureLimitation);
            return text.ToString();
        }

        // Launches one bounded host process. The overall deadline and the save-stage watchdog are the
        // same ones the proven native paths use; only the process this invocation started is killed.
        private static HostResult RunHost(HostRequest request, string executable, string evidence, string prefix,
            int timeoutMilliseconds, Action<string> progress)
        {
            // The trusted root is this parent's application temporary root. It is carried into the
            // child because the child's own TEMP/TMP are about to be redirected to a private scratch
            // root, and it must keep validating working copies against the application boundary
            // rather than against its redirected environment.
            request.TrustedTempRoot = Path.GetTempPath();
            // The control channel lives under the application anchor, not under this process's
            // temporary root, so it stays valid for a child whose TEMP/TMP were redirected.
            string ipc = Path.Combine(SewerPilotTemporary.Anchor(), SewerPilotTemporary.Folder,
                SewerPilotTemporary.Product, "RuntimeHost", request.RunId);
            RuntimeHostProtocol.AnchoredDirectory(ipc, "RuntimeHost");
            if (Directory.Exists(ipc)) throw new IOException("Runtime host IPC directory collision.");
            Directory.CreateDirectory(ipc);
            string requestPath = Path.Combine(ipc, "request.json");
            string resultPath = Path.Combine(ipc, "result.json");
            RuntimeHostProtocol.AtomicWrite(requestPath, request);
            Write(evidence, prefix + "request.json", RuntimeHostProtocol.Serializer().Serialize(request));

            // One private scratch root per host execution, keyed by the run identity alone so two
            // concurrent events can never share a tree and no model name participates.
            bool isolateEngineScratch = request.Operation == GeneralModelCalculationHost.Operation;
            string scratch = null;
            if (isolateEngineScratch)
            {
                scratch = SewerPilotTemporary.CreateEngineScratch(request.RunId, request.TrustedTempRoot);
                Write(evidence, prefix + "engine-scratch-root.txt", scratch + Environment.NewLine);
            }

            var start = new System.Diagnostics.ProcessStartInfo(executable,
                GeneralModelCalculationHost.LaunchSwitch + " \"" + requestPath + "\" \"" + resultPath + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable)
            };
            // Only the launched host sees the private scratch location. This process, the Windows
            // system environment and every other Bentley instance keep their own temporary settings.
            // The variables are written inside the duplicate-casing window below: enumerating this
            // process's environment fails outright on a workstation that carries two spellings of one
            // proxy variable, which is the same hazard the supervised client launch already guards.
            System.Diagnostics.Process process = null;
            try
            {
                // The same duplicate-variable-casing guard the supervised client launch uses, so a
                // proxy-configured workstation cannot prevent the host from starting.
                Action restore = RuntimeHostLauncher.SuppressDuplicateVariableCasing();
                try
                {
                    if (scratch != null)
                    {
                        start.EnvironmentVariables["TEMP"] = scratch;
                        start.EnvironmentVariables["TMP"] = scratch;
                    }
                    process = System.Diagnostics.Process.Start(start);
                }
                finally { restore(); }
                if (process == null) throw new InvalidOperationException("The native calculation host could not start.");

                var watchdog = new SaveStageWatchdog(Path.Combine(ipc, "stages.jsonl"));
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                bool saveTimedOut = false;
                using (process)
                {
                    int ownedProcessId = process.Id;
                    while (!process.WaitForExit(PollMilliseconds))
                    {
                        if (watchdog.Observe(DateTime.UtcNow) == SaveStageVerdict.TimedOut)
                        {
                            OwnedProcessTerminator.TryTerminate(process, ownedProcessId);
                            saveTimedOut = true;
                            break;
                        }
                        if (elapsed.ElapsedMilliseconds >= timeoutMilliseconds)
                        {
                            OwnedProcessTerminator.TryTerminate(process, ownedProcessId);
                            throw new TimeoutException("The native calculation exceeded its time limit and was stopped.");
                        }
                    }
                    if (saveTimedOut)
                        throw new TimeoutException(
                            "The Bentley save step stopped responding and was stopped. Nothing was persisted and no result is shown.");
                    string stages = Path.Combine(ipc, "stages.jsonl");
                    if (File.Exists(stages))
                        File.Copy(stages, Path.Combine(evidence, prefix + "runtime-stages.jsonl"), true);
                    if (!File.Exists(resultPath))
                        throw new InvalidDataException("The native host produced no result document.");
                    Write(evidence, prefix + "result.json", File.ReadAllText(resultPath));
                    var result = RuntimeHostProtocol.ParseResult(resultPath, request);
                    if (!result.Success)
                        throw new InvalidDataException(DescribeFailure("The native host",
                            result.FailureClassification ?? "HOST_NOT_ACCEPTED", result.FailureMessageSafe, null, null, null));
                    return result;
                }
            }
            finally
            {
                // The owned child has ended (or was terminated) before this runs. Record what the
                // vendor wrote into the private root, then remove only that one directory. No shared
                // or pre-existing temporary directory is ever touched.
                if (scratch != null)
                {
                    Write(evidence, prefix + "engine-scratch-inventory.txt",
                        string.Join(Environment.NewLine, SewerPilotTemporary.Inventory(scratch)) + Environment.NewLine);
                    SewerPilotTemporary.TryRemoveOwnedScratch(scratch, request.RunId, request.TrustedTempRoot);
                }
            }
        }
    }
}
