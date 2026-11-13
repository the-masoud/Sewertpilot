using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    // Bounded verification of the general hydraulic execution path:
    //  1. one genuine native calculation of a non-allowlisted model's own saved scenario,
    //  2. one independent reopen/readback with element ids, units and run provenance,
    //  3. original-model byte integrity before and after,
    //  4. the safety boundaries around the new operation,
    //  5. proof that the desktop action is wired to the same service.
    internal static class GeneralHydraulicExecutionTests
    {
        internal static int Run(string sourceModelPath, string evidenceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceModelPath) || !File.Exists(sourceModelPath))
                throw new FileNotFoundException("Test source model was not found.", sourceModelPath);
            if (string.IsNullOrWhiteSpace(evidenceDirectory))
                throw new ArgumentException("An evidence directory is required.", nameof(evidenceDirectory));
            Directory.CreateDirectory(evidenceDirectory);

            string model = Path.GetFullPath(sourceModelPath);
            string[] family = GeneralModelCalculationService.Family(model);
            var before = Fingerprints(family);

            Console.WriteLine("GENERAL_MODEL=" + model);
            Console.WriteLine("FAMILY_FILES=" + family.Length);

            var receipt = GeneralModelCalculationService.Execute(model, message => Console.WriteLine("PROGRESS: " + message));

            // ---- independent readback integrity: the service already reopened and read back; here we
            // re-read the produced observation documents and re-verify the same-run identity ourselves.
            var calculation = ReadObservation<GeneralCalculationObservation>(receipt.EvidenceDirectory, "calculation-observation.json");
            var readback = ReadObservation<GeneralReadbackObservation>(receipt.EvidenceDirectory, "readback-observation.json");

            var problems = new List<string>();
            if (calculation == null) problems.Add("calculation observation missing");
            if (readback == null) problems.Add("readback observation missing");
            if (problems.Count > 0) throw new InvalidDataException(string.Join("; ", problems));

            if (calculation.SolverRunInvocations != 1) problems.Add("solver invocations != 1");
            if (calculation.SaveInvocations != 1) problems.Add("save invocations != 1");
            if (!string.Equals(calculation.ScenarioComputeStatus, "Success", StringComparison.Ordinal))
                problems.Add("scenario compute status not Success");
            if (!calculation.SaveSucceeded) problems.Add("save not persisted");
            if (!string.Equals(readback.CalculationEventId, calculation.CalculationEventId, StringComparison.Ordinal))
                problems.Add("readback event differs from the calculation event");
            if (readback.ScenarioId != calculation.ScenarioId) problems.Add("scenario identity differs");
            if (readback.CalculationOptionsId != calculation.CalculationOptionsId) problems.Add("options identity differs");
            if (!string.Equals(readback.Engine, calculation.Engine, StringComparison.Ordinal)) problems.Add("engine identity differs");
            // ---- inventory identity, verified independently instead of asserted against itself.
            // Recompute the digest of the working copy the calculation actually persisted, then require
            // the independently reopened digest AND the recorded output inventory to agree with it.
            // A model the engine really solved cannot leave its output inventory equal to its input.
            string workingDirectory = Path.GetDirectoryName(receipt.WorkingModelPath);
            var persistedInventory = P16AzadeganCalculationHost.Inventory(workingDirectory);
            string persistedDigest = P16AzadeganCalculationHost.InventoryDigest(persistedInventory);
            if (!GeneralModelCalculationHost.InventoryMatches(persistedDigest, readback.ReopenedInventoryDigest))
                problems.Add("reopened inventory digest does not match the persisted working copy");
            if (!string.Equals(P16AzadeganCalculationHost.InventoryJson(persistedInventory),
                    calculation.OutputInventoryJson, StringComparison.Ordinal))
                problems.Add("calculation output inventory does not match the persisted working copy");
            if (GeneralModelCalculationHost.InventoryMatches(persistedDigest, calculation.InputInventoryDigest))
                problems.Add("output inventory is identical to the input inventory");

            // ---- genuine values: at least one conduit metric and one structure metric, with ids/units.
            var rows = readback.Rows ?? new GeneralHydraulicResultRow[0];
            // Prefer a calculated flow, then velocity, then depth, so the recorded sample is the most
            // directly interpretable hydraulic quantity rather than an elevation.
            var conduitValue = First(rows, "Flow") ?? First(rows, "Velocity") ?? First(rows, "Depth") ?? First(rows, null);
            var structureValue = rows.FirstOrDefault(r => r.Category == "Structure" && r.Status == "AVAILABLE");
            if (conduitValue == null) problems.Add("no available conduit hydraulic value");
            if (structureValue == null) problems.Add("no available structure hydraulic value");
            if (conduitValue != null && string.IsNullOrEmpty(conduitValue.Unit))
                problems.Add("conduit value has no unit");
            if (conduitValue != null && string.IsNullOrEmpty(conduitValue.ElementLabel))
                problems.Add("conduit value has no source label");
            // A solved network must show at least one flowing conduit. An all-zero result set would be
            // indistinguishable from an uncalculated or default-filled read, so it is not accepted as
            // evidence that the engine actually solved this model.
            if (!rows.Any(r => r.Category == "Conduit" && r.Metric == "Flow" && r.Value.HasValue && r.Value.Value > 0d))
                problems.Add("no conduit carries a positive calculated flow");
            if (!rows.Any(r => r.Category == "Conduit" && r.Metric == "Capacity" && r.Value.HasValue && r.Value.Value > 0d))
                problems.Add("no conduit carries a positive calculated capacity");

            // ---- original model must be byte-identical.
            var after = Fingerprints(family);
            bool unchanged = before.SequenceEqual(after);
            if (!unchanged) problems.Add("original model family changed");

            // ---- the disposable working copy is the only thing the engine wrote.
            bool workingOutsideSource = !string.Equals(Path.GetFullPath(workingDirectory),
                Path.GetFullPath(Path.GetDirectoryName(model)), StringComparison.OrdinalIgnoreCase);
            if (!workingOutsideSource) problems.Add("working copy is not isolated from the source directory");

            // ---- the single acceptance decision, taken here rather than inferred from the absence of
            // problems. A warning-bearing run is PARTIAL and keeps its rows; it is never printed as PASS.
            GeneralAcceptance acceptance = GeneralCalculationAcceptance.Classify(calculation, readback);
            string verdict = problems.Count > 0
                ? GeneralCalculationAcceptance.Blocked
                : acceptance.Verdict;

            WriteEvidence(evidenceDirectory, receipt, calculation, readback, before, after, unchanged,
                conduitValue, structureValue, problems, acceptance, verdict);
            WriteConsole(calculation, readback, conduitValue, structureValue, problems, acceptance, verdict);

            if (problems.Count > 0)
                throw new InvalidDataException("General hydraulic execution verification failed: " +
                    string.Join("; ", problems));

            if (string.Equals(verdict, GeneralCalculationAcceptance.Blocked, StringComparison.Ordinal))
                throw new InvalidDataException("General hydraulic execution acceptance is BLOCKED (" +
                    acceptance.Reason + "): " + acceptance.Explanation);

            Console.WriteLine("GENERAL_HYDRAULIC_EXECUTION=" + verdict);
            // PASS returns 0. PARTIAL is a real, recorded outcome - not a success, and not a crash - so
            // it returns 2 and can never be mistaken for a clean run by a caller that only tests for zero.
            return string.Equals(verdict, GeneralCalculationAcceptance.Pass, StringComparison.Ordinal) ? 0 : 2;
        }

        // ------------------------------------------------------------------ terrain dependency

        // Pre-solve verification of the declared external inputs: the discovery pass, the resolved
        // source file, and the layout of the disposable copy the engine will actually be given.
        // No solver is invoked here.
        internal static int InspectTerrain(string sourceModelPath, string evidenceDirectory)
        {
            var report = new List<string>();
            var prepared = GeneralModelCalculationService.InspectTerrain(sourceModelPath, evidenceDirectory);
            var discovery = prepared.Discovery;
            report.Add("DISCOVERY_STATUS=" + discovery.Status);
            report.Add("TERRAIN_MANAGER=" + (discovery.TerrainManagerResolved ? discovery.TerrainManagerName : "NONE"));
            report.Add("TERRAIN_ELEMENTS=" + discovery.TerrainElementCount);
            report.Add("TERRAIN_MANAGER_NAMES=" + string.Join(",", discovery.AvailableManagerNames ?? new string[0]));
            foreach (GeneralTerrainDependency dependency in discovery.Dependencies ?? new GeneralTerrainDependency[0])
            {
                report.Add("DEPENDENCY element=" + dependency.ElementId + " active=" + dependency.IsActive +
                    " checked=" + dependency.IsChecked + " sourceType=" + dependency.SourceType);
                report.Add("  declaredAbsolute=" + (dependency.DeclaredAbsolutePath ?? "(none)"));
                report.Add("  declaredRelative=" + (dependency.DeclaredRelativePath ?? "(none)"));
            }
            foreach (ResolvedTerrain terrain in prepared.Terrains)
            {
                report.Add("RESOLVED_SOURCE=" + terrain.SourcePath);
                report.Add("  bytes=" + terrain.Bytes + " sha256=" + terrain.SHA256);
                report.Add("  copiedTo=" + string.Join(" ; ", terrain.CopiedTo));
                foreach (string destination in terrain.CopiedTo)
                {
                    var info = new FileInfo(destination);
                    report.Add("  ON_DISK=" + info.FullName + " bytes=" + info.Length + " sha256=" +
                        WorkingCopyIntegrity.Fingerprint(destination).SHA256);
                }
            }
            report.Add("WORKING_COPY=" + prepared.Working.DirectoryPath);
            foreach (string path in Directory.GetFileSystemEntries(prepared.Working.DirectoryPath))
                report.Add("  ENTRY=" + Path.GetFileName(path));

            // Does the vendor pick up the dependency once it is present beside the model?
            report.Add("TERRAIN_BEARING_COPY=" + prepared.WithDependenciesPath);
            foreach (GeneralTerrainDependency dependency in prepared.WithDependencies.Dependencies ?? new GeneralTerrainDependency[0])
            {
                report.Add("AFTER_DEPENDENCY element=" + dependency.ElementId + " active=" + dependency.IsActive +
                    " checked=" + dependency.IsChecked + " sourceType=" + dependency.SourceType);
                report.Add("  declaredAbsolute=" + (dependency.DeclaredAbsolutePath ?? "(none)"));
                report.Add("  declaredRelative=" + (dependency.DeclaredRelativePath ?? "(none)"));
            }
            foreach (string line in prepared.WithDependencies.ElementPropertyDump ?? new string[0])
                if ((line.StartsWith("FIELD:", StringComparison.Ordinal) &&
                     line.IndexOf("FilePath", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    line.StartsWith("FIELD:DigitalTerrainModel_ModelString", StringComparison.Ordinal))
                    report.Add("  AFTER_" + line);
            string dump = Path.Combine(evidenceDirectory, "terrain-element-properties.txt");
            File.WriteAllText(dump, string.Join(Environment.NewLine, discovery.ElementPropertyDump ?? new string[0]));
            report.Add("ELEMENT_PROPERTY_DUMP=" + dump);
            File.WriteAllText(Path.Combine(evidenceDirectory, "terrain-inspection.txt"),
                string.Join(Environment.NewLine, report));
            foreach (string line in report) Console.WriteLine(line);
            Console.WriteLine("GENERAL_TERRAIN_INSPECTION=PASS");
            return 0;
        }

        // ------------------------------------------------------------------ reference rebinding

        // Corrects the active terrain reference onto the verified copy inside the disposable working
        // copy, persists once, and then reopens that copy in a separate process to prove the
        // correction survived. No solver is invoked on this path.
        internal static int Rebind(string sourceModelPath, string evidenceDirectory)
        {
            var report = new List<string>();
            TerrainRebindReceipt receipt = GeneralModelCalculationService.RebindTerrain(
                sourceModelPath, evidenceDirectory, line => Console.WriteLine("STAGE: " + line));
            report.Add("EVIDENCE_DIRECTORY=" + receipt.EvidenceDirectory);
            report.Add("WORKING_MODEL=" + receipt.WorkingModelPath);
            report.Add("WORKING_COPY_ID=" + receipt.WorkingCopyId);
            report.Add("TARGET=" + receipt.TargetPath);
            report.Add("TARGET_RELATIVE=" + receipt.TargetRelativePath);
            report.Add("TARGET_SHA256=" + receipt.TargetSHA256);

            GeneralTerrainBindObservation bind = receipt.Bind;
            report.Add("BIND_STATUS=" + (bind == null ? "(none)" : bind.Status));
            if (bind != null)
            {
                report.Add("BIND_ELEMENT=" + bind.ElementId + " activeElements=" + bind.TerrainElementCount);
                report.Add("BIND_FIELDS=" + string.Join(",", bind.FieldDiagnostics ?? new string[0]));
                report.Add("BIND_BEFORE_ABSOLUTE=" + (bind.Before == null ? "(none)" : bind.Before.DeclaredAbsolutePath ?? "(none)"));
                report.Add("BIND_BEFORE_RELATIVE=" + (bind.Before == null ? "(none)" : bind.Before.DeclaredRelativePath ?? "(none)"));
                report.Add("BIND_BEFORE_PROXY=" + (bind.Before == null ? "(none)" : bind.Before.FileProxy ?? "(none)"));
                report.Add("BIND_AFTER_ABSOLUTE=" + (bind.After == null ? "(none)" : bind.After.DeclaredAbsolutePath ?? "(none)"));
                report.Add("BIND_AFTER_RELATIVE=" + (bind.After == null ? "(none)" : bind.After.DeclaredRelativePath ?? "(none)"));
                report.Add("BIND_AFTER_PROXY=" + (bind.After == null ? "(none)" : bind.After.FileProxy ?? "(none)"));
                report.Add("BIND_PROXY_TYPE=" + (bind.ProxyTypeName ?? "(none)"));
                report.Add("BIND_PROXY_INDEPENDENT=" + bind.ProxyIndependent);
                report.Add("BIND_PROXY_BEFORE=" + (bind.ProxyPathBefore ?? "(none)"));
                report.Add("BIND_PROXY_AFTER=" + (bind.ProxyPathAfter ?? "(none)"));
                report.Add("BIND_PROXY_FIELD_WRITTEN=" + bind.ProxyFieldWritten);
                report.Add("BIND_PROXY_NOTES=" + string.Join(",", bind.ProxyDiagnostics ?? new string[0]));
                report.Add("BIND_SAVE=" + bind.SaveSucceeded + " classification=" + bind.SaveResultClassification +
                    " questions=" + bind.SaveQuestionCount + " at=" + (bind.SaveTimestamp ?? "(none)"));
                report.Add("BIND_CREATED=" + string.Join(",", bind.CreatedFiles ?? new string[0]));
                report.Add("BIND_MODIFIED=" + string.Join(",", bind.ModifiedFiles ?? new string[0]));
            }

            GeneralTerrainVerifyObservation verify = receipt.Verify;
            report.Add("VERIFY_STATUS=" + (verify == null ? "(none)" : verify.Status));
            if (verify != null)
            {
                report.Add("VERIFY_ELEMENT=" + verify.ElementId + " activeElements=" + verify.TerrainElementCount);
                report.Add("VERIFY_ABSOLUTE=" + (verify.Reference == null ? "(none)" : verify.Reference.DeclaredAbsolutePath ?? "(none)"));
                report.Add("VERIFY_RELATIVE=" + (verify.Reference == null ? "(none)" : verify.Reference.DeclaredRelativePath ?? "(none)"));
                report.Add("VERIFY_PROXY=" + (verify.ProxyPath ?? "(none)"));
                report.Add("VERIFY_TARGET_EXISTS=" + verify.TargetExists);
                report.Add("VERIFY_TARGET_SHA256=" + (verify.TargetSHA256 ?? "(none)"));
                report.Add("VERIFY_ABSOLUTE_MATCHES=" + verify.AbsolutePathMatchesTarget);
                report.Add("VERIFY_RELATIVE_RESOLVES=" + verify.RelativePathResolves);
                report.Add("VERIFY_AUTHORING_STILL_REQUIRED=" + verify.AuthoringPathStillRequired);
            }
            report.Add("VERDICT=" + receipt.Verdict);
            report.Add("SUMMARY=" + receipt.Summary);

            File.WriteAllText(Path.Combine(evidenceDirectory, "terrain-rebind.txt"),
                string.Join(Environment.NewLine, report));
            foreach (string line in report) Console.WriteLine(line);
            if (!string.Equals(receipt.Verdict, "TERRAIN_REFERENCE_REBOUND_AND_VERIFIED", StringComparison.Ordinal))
            {
                Console.WriteLine("GENERAL_TERRAIN_REBIND=BLOCKED");
                return 1;
            }
            Console.WriteLine("GENERAL_TERRAIN_REBIND=PASS");
            return 0;
        }

        // ------------------------------------------------------------------ integrated preparation

        // Proves the sequence a real calculation uses — discovery, disposable copy, terrain
        // rebinding, independent reopen — without invoking the solver at all. Every assertion here is
        // measured from the produced observations, not from the service's own verdict alone.
        internal static int RunPreparation(string sourceModelPath, string evidenceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceModelPath) || !File.Exists(sourceModelPath))
                throw new FileNotFoundException("Test source model was not found.", sourceModelPath);
            if (string.IsNullOrWhiteSpace(evidenceDirectory))
                throw new ArgumentException("An evidence directory is required.", nameof(evidenceDirectory));
            Directory.CreateDirectory(evidenceDirectory);

            string model = Path.GetFullPath(sourceModelPath);
            string[] family = GeneralModelCalculationService.Family(model);
            var before = Fingerprints(family);

            GeneralPreparation preparation = GeneralModelCalculationService.PrepareForCalculation(
                model, evidenceDirectory, line => Console.WriteLine("STAGE: " + line));

            var report = new List<string>();
            var problems = new List<string>();
            string working = preparation.WorkingModelPath;
            string copyRoot = Path.GetDirectoryName(working);

            report.Add("MODEL=" + model);
            report.Add("WORKING_MODEL=" + working);
            report.Add("WORKING_COPY_ID=" + preparation.WorkingCopyId);
            report.Add("TERRAIN_DEPENDENCIES=" + preparation.Terrain.Terrains.Count);
            report.Add("TERRAIN_SOURCES=" + string.Join(" ; ", preparation.Terrain.Sources));
            report.Add("INPUT_DIGEST=" + preparation.InputDigest);

            // ---- the copy is a controlled working copy, not the engineer's directory.
            string expectedRoot = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "Working");
            if (!copyRoot.StartsWith(Path.GetFullPath(expectedRoot), StringComparison.OrdinalIgnoreCase))
                problems.Add("the prepared copy is outside the trusted working root: " + copyRoot);
            if (!string.Equals(Path.GetFileName(copyRoot), preparation.WorkingCopyId, StringComparison.OrdinalIgnoreCase))
                problems.Add("the working copy identity does not match its directory");

            // ---- exactly one declared terrain dependency, and the copy matches its source bytes.
            if (preparation.Terrain.Terrains.Count != 1)
                problems.Add("expected one terrain dependency, saw " + preparation.Terrain.Terrains.Count);
            ResolvedTerrain terrain = preparation.Terrain.Terrains.Count == 1
                ? preparation.Terrain.Terrains[0] : null;
            if (terrain != null)
            {
                report.Add("TERRAIN_SOURCE_SHA256=" + terrain.SHA256);
                if (!string.Equals(WorkingCopyIntegrity.Fingerprint(terrain.SourcePath).SHA256, terrain.SHA256,
                        StringComparison.Ordinal))
                    problems.Add("the terrain source no longer matches the resolved bytes");
            }

            // ---- the declared Shapefile family travelled with the copy, once, byte for byte.
            List<ResolvedDataSource> sources = preparation.Terrain.DataSources;
            report.Add("DATA_SOURCE_FAMILIES=" + sources.Count);
            if (sources.Count != 1)
                problems.Add("expected one Shapefile family, saw " + sources.Count);
            else
            {
                ResolvedDataSource source = sources[0];
                report.Add("DATA_SOURCE_NAME=" + Path.GetFileName(source.SourcePath));
                report.Add("DATA_SOURCE_REFERENCES=" + source.ReferenceCount);
                report.Add("DATA_SOURCE_DESTINATION=" + source.RelativeDestination);
                if (source.ReferenceCount != 2)
                    problems.Add("the two records naming the same Shapefile were not collapsed to one copy");
                string[] expected =
                {
                    "7dc6eec390a081efb18c044a33f7a18914bf60032378f782b4df5c5e65b2a651",
                    "18ab9e29e66a5351556ec6e4e194af15e3ffb319c5b657c920d93ddc2a9f0dbc",
                    "636b0b0103ea1698bdaeb1a783ce509dcdb8bc292ca373e2e08137fe365a0582"
                };
                for (int index = 0; index < source.SourcePaths.Length; index++)
                {
                    string copied = source.DestinationPaths[index];
                    string name = Path.GetFileName(copied);
                    string destinationHash = WorkingCopyIntegrity.Fingerprint(copied).SHA256;
                    report.Add("DATA_SOURCE_" + Path.GetExtension(copied).TrimStart('.').ToUpperInvariant() +
                        "_SOURCE=" + source.SHA256[index]);
                    report.Add("DATA_SOURCE_" + Path.GetExtension(copied).TrimStart('.').ToUpperInvariant() +
                        "_DEST=" + destinationHash);
                    report.Add("DATA_SOURCE_" + Path.GetExtension(copied).TrimStart('.').ToUpperInvariant() +
                        "_DESTINATION=" + copied);
                    if (!File.Exists(copied)) problems.Add("the copied Shapefile member is absent: " + name);
                    if (!string.Equals(destinationHash, source.SHA256[index], StringComparison.Ordinal))
                        problems.Add("the copied Shapefile member differs from its source: " + name);
                    if (!string.Equals(destinationHash, expected[index], StringComparison.OrdinalIgnoreCase))
                        problems.Add("the copied Shapefile member is not the verified lesson file: " + name);
                }
                string familyDirectory = Path.GetDirectoryName(source.DestinationPaths[0]);
                if (!string.Equals(Path.GetFileName(familyDirectory), "Data Files", StringComparison.OrdinalIgnoreCase))
                    problems.Add("the Shapefile family was not placed in Data Files: " + familyDirectory);
            }

            // ---- binding happened on that copy, saved once, and verified from another process.
            TerrainRebindReceipt receipt = preparation.Bind;
            if (receipt == null)
            {
                problems.Add("no terrain rebinding was performed");
            }
            else
            {
                report.Add("BIND_VERDICT=" + receipt.Verdict);
                report.Add("BIND_STATUS=" + (receipt.Bind == null ? "(none)" : receipt.Bind.Status));
                report.Add("BIND_SAVE=" + (receipt.Bind == null ? "(none)" :
                    receipt.Bind.SaveSucceeded + "/" + receipt.Bind.SaveResultClassification +
                    "/questions=" + receipt.Bind.SaveQuestionCount));
                report.Add("BIND_PROXY=" + (receipt.Bind == null ? "(none)" :
                    (receipt.Bind.ProxyPathBefore ?? "(none)") + " -> " + (receipt.Bind.ProxyPathAfter ?? "(none)")));
                report.Add("VERIFY_STATUS=" + (receipt.Verify == null ? "(none)" : receipt.Verify.Status));
                report.Add("VERIFY_ABSOLUTE=" + (receipt.Verify == null ? "(none)" :
                    receipt.Verify.Reference == null ? "(none)" : receipt.Verify.Reference.DeclaredAbsolutePath));
                report.Add("VERIFY_RELATIVE=" + (receipt.Verify == null ? "(none)" :
                    receipt.Verify.Reference == null ? "(none)" : receipt.Verify.Reference.DeclaredRelativePath));
                report.Add("VERIFY_PROXY=" + (receipt.Verify == null ? "(none)" : receipt.Verify.ProxyPath ?? "(none)"));
                report.Add("VERIFY_TARGET_SHA256=" + (receipt.Verify == null ? "(none)" : receipt.Verify.TargetSHA256));
                report.Add("SOLVER_INVOCATIONS=" + receipt.SolverRunInvocations);

                if (!string.Equals(receipt.Verdict, "TERRAIN_REFERENCE_REBOUND_AND_VERIFIED", StringComparison.Ordinal))
                    problems.Add("rebinding verdict is " + receipt.Verdict);
                if (receipt.Bind == null ||
                    !string.Equals(receipt.Bind.Status, GeneralModelCalculationHost.BindStatus, StringComparison.Ordinal))
                    problems.Add("binding did not report a persisted correction");
                if (receipt.Bind != null && (!receipt.Bind.SaveSucceeded ||
                    !string.Equals(receipt.Bind.SaveResultClassification, "PERSISTED", StringComparison.Ordinal) ||
                    receipt.Bind.SaveQuestionCount != 0))
                    problems.Add("binding was not persisted with a clean save");
                if (receipt.Verify == null ||
                    !string.Equals(receipt.Verify.Status, GeneralModelCalculationHost.VerifyStatus, StringComparison.Ordinal))
                    problems.Add("the independent reopen did not verify the reference");
                if (receipt.Verify != null)
                {
                    if (!receipt.Verify.AbsolutePathMatchesTarget) problems.Add("the absolute reference does not match the copy");
                    if (!receipt.Verify.RelativePathResolves) problems.Add("the relative reference does not resolve");
                    if (receipt.Verify.AuthoringPathStillRequired) problems.Add("the authoring location is still required");
                    if (!receipt.Verify.TargetExists) problems.Add("the referenced terrain does not exist");
                    if (terrain != null && !string.Equals(receipt.Verify.TargetSHA256, terrain.SHA256, StringComparison.Ordinal))
                        problems.Add("the referenced terrain hash differs from the verified source");
                    if (!string.IsNullOrWhiteSpace(receipt.Verify.ProxyPath) &&
                        !string.Equals(Path.GetFullPath(receipt.Verify.ProxyPath), Path.GetFullPath(receipt.TargetPath),
                            StringComparison.OrdinalIgnoreCase))
                        problems.Add("the stored proxy still names another location");
                    // Binding must have targeted a file inside the very copy that would be calculated.
                    string copyPrefix = Path.GetFullPath(copyRoot).TrimEnd(Path.DirectorySeparatorChar) +
                                        Path.DirectorySeparatorChar;
                    if (!Path.GetFullPath(receipt.TargetPath).StartsWith(copyPrefix, StringComparison.OrdinalIgnoreCase))
                        problems.Add("the rebound target is not inside the calculation copy: " + receipt.TargetPath);
                    if (!string.Equals(receipt.WorkingCopyId, preparation.WorkingCopyId, StringComparison.OrdinalIgnoreCase))
                        problems.Add("the rebound copy identity differs from the prepared copy");
                }
                if (receipt.SolverRunInvocations != 0)
                    problems.Add("preparation invoked the solver " + receipt.SolverRunInvocations + " time(s)");
            }

            // ---- the digest is of the rebound copy, and a CALCULATE request would be accepted.
            string recomputed = P16AzadeganCalculationHost.InventoryDigest(
                P16AzadeganCalculationHost.Inventory(copyRoot));
            report.Add("RECOMPUTED_DIGEST=" + recomputed);
            if (!string.Equals(recomputed, preparation.InputDigest, StringComparison.Ordinal))
                problems.Add("the recorded input digest does not match the persisted copy");
            try
            {
                GeneralModelCalculationHost.ValidateRequest(new HostRequest
                {
                    SchemaVersion = 1,
                    RunId = Guid.NewGuid().ToString("N"),
                    Operation = GeneralModelCalculationHost.Operation,
                    TrustedTempRoot = Path.GetTempPath(),
                    WorkingCopyId = preparation.WorkingCopyId,
                    WorkingModelPath = working,
                    RuntimeCandidateCount = 1,
                    DetectedArchitecture = "x64",
                    Diagnostics = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [GeneralModelCalculationHost.CopyIdKey] = preparation.WorkingCopyId,
                        [GeneralModelCalculationHost.InventoryKey] = preparation.InputDigest,
                        [GeneralModelCalculationHost.PhaseKey] = GeneralModelCalculationHost.PhaseCalculate
                    }
                });
                report.Add("CALCULATE_REQUEST_ACCEPTED=True");
            }
            catch (Exception error)
            {
                report.Add("CALCULATE_REQUEST_ACCEPTED=False: " + error.Message);
                problems.Add("a CALCULATE request with the post-bind digest was rejected: " + error.Message);
            }

            // ---- the engineer's own files are untouched.
            var after = Fingerprints(family);
            bool familyUnchanged = before.SequenceEqual(after);
            report.Add("SOURCE_FAMILY_UNCHANGED=" + familyUnchanged);
            if (!familyUnchanged) problems.Add("the original model family changed during preparation");

            report.Add("PROBLEMS=" + problems.Count);
            foreach (string problem in problems) report.Add("PROBLEM: " + problem);
            File.WriteAllText(Path.Combine(evidenceDirectory, "preparation-verification.txt"),
                string.Join(Environment.NewLine, report));
            foreach (string line in report) Console.WriteLine(line);
            Console.WriteLine("GENERAL_CALCULATION_PREPARATION=" + (problems.Count == 0 ? "PASS" : "FAIL"));
            return problems.Count == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ safety boundaries

        internal static int RunSafety()
        {
            var problems = new List<string>();
            string working = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "Working",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(working);
            string model = Path.Combine(working, "Any Model.stsw");
            File.WriteAllText(model, "not a real model");
            string digest = new string('A', 64);
            try
            {
                // A well-formed CALCULATE request is accepted.
                var good = Request(model, working, digest, GeneralModelCalculationHost.PhaseCalculate);
                Expect(problems, "well-formed CALCULATE request is accepted", () =>
                    GeneralModelCalculationHost.ValidateRequest(good), false);

                // Wrong operation.
                var wrong = Request(model, working, digest, GeneralModelCalculationHost.PhaseCalculate);
                wrong.Operation = "SomethingElse";
                Expect(problems, "foreign operation is rejected", () =>
                    GeneralModelCalculationHost.ValidateRequest(wrong), true);

                // An engineering plan or external write authority must never be accepted here.
                var planned = Request(model, working, digest, GeneralModelCalculationHost.PhaseCalculate);
                planned.EngineeringPlan = new GravityExecutionPlan();
                Expect(problems, "engineering plan is rejected", () =>
                    GeneralModelCalculationHost.ValidateRequest(planned), true);

                var scoped = Request(model, working, digest, GeneralModelCalculationHost.PhaseCalculate);
                scoped.WriteAuthorizationScope = "SOME_SCOPE";
                Expect(problems, "external write authority is rejected", () =>
                    GeneralModelCalculationHost.ValidateRequest(scoped), true);

                // A model outside the controlled per-run boundary must be refused.
                string outside = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "ReadOnly",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(outside);
                string outsideModel = Path.Combine(outside, "Any Model.stsw");
                File.WriteAllText(outsideModel, "x");
                var misplaced = Request(outsideModel, outside, digest, GeneralModelCalculationHost.PhaseCalculate);
                Expect(problems, "model outside the Working boundary is rejected", () =>
                    GeneralModelCalculationHost.ValidateRequest(misplaced), true);

                // A copy identity that does not match the directory must be refused.
                var mismatched = Request(model, working, digest, GeneralModelCalculationHost.PhaseCalculate);
                mismatched.WorkingCopyId = new string('b', 32);
                Expect(problems, "mismatched copy identity is rejected", () =>
                    GeneralModelCalculationHost.ValidateRequest(mismatched), true);

                // ---- trusted working boundary versus the private engine scratch root.
                // The host process is launched with TEMP/TMP redirected to a private scratch root, so
                // the boundary is carried explicitly and measured against the application anchor.
                string anchor = SewerPilotTemporary.Anchor();
                if (!string.Equals(SewerPilotTemporary.Canonical(Path.GetTempPath()), anchor, StringComparison.OrdinalIgnoreCase))
                    problems.Add("trusted boundary: the application anchor is not this process's temporary root");
                if (SewerPilotTemporary.Canonical(@"C:\Windows\Temp") == anchor)
                    problems.Add("trusted boundary: an unrelated system directory matches the anchor");

                var untrusted = Request(model, working, digest, GeneralModelCalculationHost.PhaseCalculate);
                untrusted.TrustedTempRoot = null;
                Expect(problems, "a request without a trusted working root is rejected", () =>
                    GeneralModelCalculationHost.ValidateRequest(untrusted), true);

                var forged = Request(model, working, digest, GeneralModelCalculationHost.PhaseCalculate);
                forged.TrustedTempRoot = @"C:\Windows\Temp";
                Expect(problems, "a forged trusted working root is rejected", () =>
                    GeneralModelCalculationHost.ValidateRequest(forged), true);

                var relativeRoot = Request(model, working, digest, GeneralModelCalculationHost.PhaseCalculate);
                relativeRoot.TrustedTempRoot = @"..\Temp";
                Expect(problems, "a relative trusted working root is rejected", () =>
                    GeneralModelCalculationHost.ValidateRequest(relativeRoot), true);

                var absentRoot = Request(model, working, digest, GeneralModelCalculationHost.PhaseCalculate);
                absentRoot.TrustedTempRoot = Path.Combine(Path.GetTempPath(), "SewerPilot",
                    "Absent" + Guid.NewGuid().ToString("N"));
                Expect(problems, "an absent trusted working root is rejected", () =>
                    GeneralModelCalculationHost.ValidateRequest(absentRoot), true);

                // A valid copy under the genuine root is still accepted with the root carried, so the
                // separation did not narrow the legitimate boundary.
                var carried = Request(model, working, digest, GeneralModelCalculationHost.PhaseCalculate);
                carried.TrustedTempRoot = anchor;
                Expect(problems, "a genuine working copy is accepted with the anchor carried", () =>
                    GeneralModelCalculationHost.ValidateRequest(carried), false);

                // One private scratch root per execution, never shared and never reused.
                string scratch = SewerPilotTemporary.CreateEngineScratch(Guid.NewGuid().ToString("N"), Path.GetTempPath());
                string scratchId = Path.GetFileName(scratch);
                try
                {
                    Expect(problems, "the engine scratch category is never a controlled directory", () =>
                        RuntimeHostProtocol.ControlledDirectory(scratch, SewerPilotTemporary.EngineCategory,
                            Path.GetTempPath()), true);
                    Expect(problems, "the engine scratch root is not a RuntimeHost directory", () =>
                        RuntimeHostProtocol.ControlledDirectory(scratch, "RuntimeHost", Path.GetTempPath()), true);

                    // A model inside the scratch root cannot masquerade as a controlled working copy.
                    string masquerade = Path.Combine(scratch, new string('a', 32));
                    Directory.CreateDirectory(masquerade);
                    string masqueradeModel = Path.Combine(masquerade, "Any Model.stsw");
                    File.WriteAllText(masqueradeModel, "x");
                    Expect(problems, "a working copy inside the engine scratch root is rejected", () =>
                        GeneralModelCalculationHost.ValidateRequest(
                            Request(masqueradeModel, masquerade, digest, GeneralModelCalculationHost.PhaseCalculate)), true);

                    string second = SewerPilotTemporary.CreateEngineScratch(Guid.NewGuid().ToString("N"), Path.GetTempPath());
                    if (string.Equals(SewerPilotTemporary.Canonical(scratch),
                        SewerPilotTemporary.Canonical(second), StringComparison.OrdinalIgnoreCase))
                        problems.Add("engine scratch roots are shared between executions");
                    if (!SewerPilotTemporary.TryRemoveOwnedScratch(second, Path.GetFileName(second), Path.GetTempPath()))
                        problems.Add("an owned scratch root could not be removed");
                    if (Directory.Exists(second))
                        problems.Add("an owned scratch root survived its guarded removal");

                    Expect(problems, "an existing engine scratch root is a collision, not a reuse", () =>
                        SewerPilotTemporary.CreateEngineScratch(scratchId, Path.GetTempPath()), true);

                    // Removal is confined to the exact owned directory.
                    if (SewerPilotTemporary.TryRemoveOwnedScratch(Path.GetTempPath(), scratchId, Path.GetTempPath()))
                        problems.Add("a shared temporary root was accepted for removal");
                    if (SewerPilotTemporary.TryRemoveOwnedScratch(scratch, new string('f', 32), Path.GetTempPath()))
                        problems.Add("a mismatched run identity was accepted for removal");

                    // A directory link is never a controlled location.
                    string link = Path.Combine(Path.GetTempPath(), "SewerPilot", "Link" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        var made = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
                            "/c mklink /J \"" + link + "\" \"" + working + "\"")
                        { UseShellExecute = false, CreateNoWindow = true });
                        if (made != null) made.WaitForExit(15000);
                        if (Directory.Exists(link))
                        {
                            Expect(problems, "a directory link is rejected as a controlled boundary", () =>
                                RuntimeHostProtocol.ControlledDirectory(Path.Combine(link, scratchId), "Working",
                                    Path.GetTempPath()), true);
                        }
                    }
                    catch (Exception) { }
                    finally { try { if (Directory.Exists(link)) Directory.Delete(link, false); } catch (Exception) { } }
                }
                finally { try { Directory.Delete(scratch, true); } catch (Exception) { } }

                // A READ request without the calculation event it claims to read must be refused.
                var unbound = Request(model, working, digest, GeneralModelCalculationHost.PhaseRead);
                unbound.Diagnostics.Remove(GeneralModelCalculationHost.EventKey);
                Expect(problems, "READ without a calculation event is rejected", () =>
                    GeneralModelCalculationHost.ValidateRequest(unbound), true);

                var malformedEvent = Request(model, working, digest, GeneralModelCalculationHost.PhaseRead);
                malformedEvent.Diagnostics[GeneralModelCalculationHost.EventKey] = "not-an-event";
                Expect(problems, "READ with a malformed event is rejected", () =>
                    GeneralModelCalculationHost.ValidateRequest(malformedEvent), true);

                var bound = Request(model, working, digest, GeneralModelCalculationHost.PhaseRead);
                Expect(problems, "well-formed READ request is accepted", () =>
                    GeneralModelCalculationHost.ValidateRequest(bound), false);

                // Source validation: only an existing .stsw with its family is accepted.
                string text = Path.Combine(working, "notes.txt");
                File.WriteAllText(text, "x");
                Expect(problems, "non-.stsw source is rejected", () =>
                    GeneralModelCalculationService.ValidateSource(text), true);
                Expect(problems, "missing source is rejected", () =>
                    GeneralModelCalculationService.ValidateSource(Path.Combine(working, "absent.stsw")), true);

                // GHC-01 negative test: a deliberately mismatched inventory digest must never be
                // accepted as the same run. This exercises the exact guard the host applies, so a
                // mismatch proven here is the mismatch that raises REOPENED_INVENTORY_MISMATCH there.
                string established = new string('c', 64);
                string mutated = new string('d', 64);
                if (!GeneralModelCalculationHost.InventoryMatches(established, established))
                    problems.Add("GHC-01: identical inventory digests are not recognised as a match");
                if (GeneralModelCalculationHost.InventoryMatches(established, mutated))
                    problems.Add("GHC-01: a deliberately mismatched inventory digest is accepted");
                if (GeneralModelCalculationHost.InventoryMatches(established, null))
                    problems.Add("GHC-01: an absent reopened digest is accepted");
                if (GeneralModelCalculationHost.InventoryMatches(null, null))
                    problems.Add("GHC-01: two absent digests are accepted as a match");

                // GHC-02: one availability rule for every metric.
                // A Boolean is an available NON-NUMERIC result, a real zero stays zero, and an
                // unavailable metric can never be counted as available.
                Check(problems, "GHC-02 Boolean False", "False", "AVAILABLE", null);
                Check(problems, "GHC-02 Boolean True", "True", "AVAILABLE", null);
                Check(problems, "GHC-02 numeric zero", "0", "AVAILABLE", 0d);
                Check(problems, "GHC-02 numeric value", "0.011140046641230583", "AVAILABLE", 0.011140046641230583);
                Check(problems, "GHC-02 empty value", "", "NOT_AVAILABLE", null);
                Check(problems, "GHC-02 N/A text", "N/A", "NOT_AVAILABLE", null);
                Check(problems, "GHC-02 error text", "Error", "NOT_AVAILABLE", null);

                // A DISCOVER request carries no scenario, engine or event because it never solves, and
                // it is only ever legal on a read-only copy.
                string readOnly = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "ReadOnly",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(readOnly);
                string readOnlyModel = Path.Combine(readOnly, "Any Model.stsw");
                File.WriteAllText(readOnlyModel, "x");
                try
                {
                    var discover = Request(readOnlyModel, readOnly, digest, GeneralModelCalculationHost.PhaseDiscover);
                    Expect(problems, "well-formed DISCOVER request is accepted", () =>
                        GeneralModelCalculationHost.ValidateRequest(discover), false);
                    var discoverInWorking = Request(model, working, digest, GeneralModelCalculationHost.PhaseDiscover);
                    Expect(problems, "DISCOVER on a solving copy is rejected", () =>
                        GeneralModelCalculationHost.ValidateRequest(discoverInWorking), true);
                }
                finally { try { Directory.Delete(readOnly, true); } catch (Exception) { } }

                // ---- declared external inputs. A model that names a terrain file must have it
                // resolved from its own declaration, never guessed, and never outside the copy.
                string terrain = Path.Combine(Path.GetTempPath(), "SewerPilot", "TerrainTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(terrain, "Data Files"));
                try
                {
                    string declared = Path.Combine(terrain, "Data Files", "Surface.tin");
                    File.WriteAllText(declared, "TKPG-terrain");
                    string modelPath = Path.Combine(terrain, "Model.stsw");
                    File.WriteAllText(modelPath, "x");
                    var discovery = new GeneralTerrainObservation
                    {
                        Status = GeneralModelCalculationHost.DiscoverStatus,
                        Dependencies = new[]
                        {
                            new GeneralTerrainDependency
                            {
                                ElementId = 45,
                                IsActive = true,
                                DeclaredRelativePath = ".\\Data Files\\Surface.tin",
                                DeclaredAbsolutePath = "Z:\\Absent\\Surface.tin"
                            }
                        }
                    };

                    var resolved = GeneralModelCalculationService.ResolveTerrain(modelPath, discovery);
                    if (resolved.Count != 1)
                        problems.Add("terrain: a declared relative reference was not resolved");
                    else if (!string.Equals(resolved[0].SourcePath, Path.GetFullPath(declared), StringComparison.OrdinalIgnoreCase))
                        problems.Add("terrain: the wrong file was resolved");

                    if (GeneralModelCalculationService.ResolveTerrain(modelPath, new GeneralTerrainObservation()).Count != 0)
                        problems.Add("terrain: a model with no declared inputs produced a dependency");

                    var absent = new GeneralTerrainObservation
                    {
                        Dependencies = new[]
                        {
                            new GeneralTerrainDependency
                            {
                                ElementId = 1, IsActive = true,
                                DeclaredRelativePath = ".\\Data Files\\Absent.tin",
                                DeclaredAbsolutePath = "Z:\\Absent\\Absent.tin"
                            }
                        }
                    };
                    Expect(problems, "terrain: an unsatisfiable declaration is rejected", () =>
                        GeneralModelCalculationService.ResolveTerrain(modelPath, absent), true);

                    var escape = new GeneralTerrainObservation
                    {
                        Dependencies = new[]
                        {
                            new GeneralTerrainDependency
                            {
                                ElementId = 1, IsActive = true, DeclaredRelativePath = "..\\..\\Surface.tin"
                            }
                        }
                    };
                    Expect(problems, "terrain: a traversal reference is rejected", () =>
                        GeneralModelCalculationService.ResolveTerrain(modelPath, escape), true);

                    // Two distinct files matching one declaration cannot be disambiguated.
                    string shadow = Path.Combine(terrain, "Surface.tin");
                    File.WriteAllText(shadow, "different bytes");
                    Expect(problems, "terrain: ambiguous candidates are rejected", () =>
                        GeneralModelCalculationService.ResolveTerrain(modelPath, discovery), true);
                    File.Delete(shadow);

                    string copyRoot = Path.Combine(Path.GetTempPath(), "SewerPilot", "TerrainCopy",
                        Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(copyRoot);
                    try
                    {
                        Expect(problems, "terrain copy: a traversal destination is rejected", () =>
                            WorkingCopyIntegrity.CopyDependency(copyRoot, declared, "..\\escape.tin"), true);
                        Expect(problems, "terrain copy: a rooted destination is rejected", () =>
                            WorkingCopyIntegrity.CopyDependency(copyRoot, declared, "C:\\escape.tin"), true);
                        string placed = WorkingCopyIntegrity.CopyDependency(copyRoot, declared, "Data Files\\Surface.tin");
                        if (!File.Exists(placed))
                            problems.Add("terrain copy: the declared relative location was not created");
                        else if (!string.Equals(WorkingCopyIntegrity.Fingerprint(placed).SHA256,
                                     WorkingCopyIntegrity.Fingerprint(declared).SHA256, StringComparison.Ordinal))
                            problems.Add("terrain copy: the copied bytes differ from the verified source");
                        Expect(problems, "terrain copy: an existing destination is not overwritten", () =>
                            WorkingCopyIntegrity.CopyDependency(copyRoot, declared, "Data Files\\Surface.tin"), true);
                    }
                    finally { try { Directory.Delete(copyRoot, true); } catch (Exception) { } }
                }
                finally { try { Directory.Delete(terrain, true); } catch (Exception) { } }

                // ---- terrain reference rebinding. A correction may only ever name a real file that
                // already sits inside this disposable copy; an authoring path, an external source or
                // an absent file is never a legal write target.
                string rebindRoot = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "Working",
                    Guid.NewGuid().ToString("N"));
                string authoring = Path.Combine(Path.GetTempPath(), "SewerPilot", "TerrainTests",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(rebindRoot, "Data Files"));
                Directory.CreateDirectory(Path.Combine(authoring, "Data Files"));
                try
                {
                    string copyModel = Path.Combine(rebindRoot, "Any Model.stsw");
                    File.WriteAllText(copyModel, "x");
                    string copyTerrain = Path.Combine(rebindRoot, "Data Files", "Surface.tin");
                    File.WriteAllText(copyTerrain, "TKPG-terrain");
                    string externalTerrain = Path.Combine(authoring, "Data Files", "Surface.tin");
                    File.WriteAllText(externalTerrain, "TKPG-terrain");

                    var bindable = Request(copyModel, rebindRoot, digest, GeneralModelCalculationHost.PhaseBind, copyTerrain);
                    Expect(problems, "well-formed BIND_TERRAIN request is accepted", () =>
                        GeneralModelCalculationHost.ValidateRequest(bindable), false);

                    var verifiable = Request(copyModel, rebindRoot, digest, GeneralModelCalculationHost.PhaseVerify, copyTerrain);
                    Expect(problems, "well-formed VERIFY_TERRAIN request is accepted", () =>
                        GeneralModelCalculationHost.ValidateRequest(verifiable), false);

                    Expect(problems, "BIND_TERRAIN without a target is rejected", () =>
                        GeneralModelCalculationHost.ValidateRequest(
                            Request(copyModel, rebindRoot, digest, GeneralModelCalculationHost.PhaseBind)), true);

                    // The authoring machine's own terrain is an external source path: it is readable
                    // evidence, never a write target.
                    Expect(problems, "BIND_TERRAIN onto an external source path is rejected", () =>
                        GeneralModelCalculationHost.ValidateRequest(Request(copyModel, rebindRoot, digest,
                            GeneralModelCalculationHost.PhaseBind, externalTerrain)), true);

                    // A sibling copy under the same category is still outside this run's boundary.
                    string sibling = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "Working",
                        Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(sibling);
                    try
                    {
                        string siblingTerrain = Path.Combine(sibling, "Surface.tin");
                        File.WriteAllText(siblingTerrain, "TKPG-terrain");
                        Expect(problems, "BIND_TERRAIN onto a sibling copy is rejected", () =>
                            GeneralModelCalculationHost.ValidateRequest(Request(copyModel, rebindRoot, digest,
                                GeneralModelCalculationHost.PhaseBind, siblingTerrain)), true);
                        Expect(problems, "VERIFY_TERRAIN onto a sibling copy is rejected", () =>
                            GeneralModelCalculationHost.ValidateRequest(Request(copyModel, rebindRoot, digest,
                                GeneralModelCalculationHost.PhaseVerify, siblingTerrain)), true);
                    }
                    finally { try { Directory.Delete(sibling, true); } catch (Exception) { } }

                    Expect(problems, "BIND_TERRAIN onto an absent file is rejected", () =>
                        GeneralModelCalculationHost.ValidateRequest(Request(copyModel, rebindRoot, digest,
                            GeneralModelCalculationHost.PhaseBind, Path.Combine(rebindRoot, "Data Files", "Absent.tin"))), true);

                    // A directory is not a terrain.
                    Expect(problems, "BIND_TERRAIN onto a directory is rejected", () =>
                        GeneralModelCalculationHost.ValidateRequest(Request(copyModel, rebindRoot, digest,
                            GeneralModelCalculationHost.PhaseBind, Path.Combine(rebindRoot, "Data Files"))), true);

                    // The relative reference that is written must be expressed relative to the copy,
                    // and the proxy check must judge a relative proxy against the copy, not against
                    // whatever directory the process happens to be in.
                    string inside = GeneralModelCalculationHost.RelativeInside(rebindRoot, copyTerrain);
                    if (!string.Equals(inside, Path.Combine("Data Files", "Surface.tin"), StringComparison.OrdinalIgnoreCase))
                        problems.Add("rebind: the relative reference was not expressed relative to the copy");
                    if (GeneralModelCalculationHost.RelativeInside(rebindRoot, externalTerrain) != null)
                        problems.Add("rebind: an external path was reported as inside the copy");
                    if (GeneralModelCalculationHost.ResolveAgainst(rebindRoot, "Data Files\\Surface.tin") !=
                        Path.GetFullPath(copyTerrain))
                        problems.Add("rebind: a relative proxy path was not anchored at the copy");
                    string proxy = "&lt;DocumentSpecificationLocalFile Path=\"" +
                        externalTerrain.Replace("&", "&amp;") + "\" /&gt;";
                    if (GeneralModelCalculationHost.ProxyPath(proxy) != externalTerrain)
                        problems.Add("rebind: the proxy path was not read back from the proxy string");
                    if (GeneralModelCalculationHost.ProxyPath("no path here") != null)
                        problems.Add("rebind: a proxy without a path produced a path");
                }
                finally
                {
                    try { Directory.Delete(rebindRoot, true); } catch (Exception) { }
                    try { Directory.Delete(authoring, true); } catch (Exception) { }
                }

                // An unsupported model is refused before any native launch. Deliberately corrupting a
                // file that still ends in .stsw is not attempted here: the vendor open path may present
                // a modal dialog for such a file, which no bounded test should depend on. Unsupported
                // and unresolvable states are instead rejected by the host classifications
                // (ENGINE_UNRESOLVED / ENGINE_AMBIGUOUS / HAS_RESULTS_FALSE / REOPENED_*_MISMATCH),
                // none of which the service ever promotes to a result.
            }
            finally
            {
                try { Directory.Delete(working, true); } catch (Exception) { }
                try { Directory.Delete(Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "ReadOnly"), true); }
                catch (Exception) { }
            }

            // ---- desktop wiring: the action exists, is gated on a loaded model, and the workflow
            // references the same service the headless test exercises.
            CheckWiring(problems);

            // ---- the reference-write contract that guards the combined terrain and 2D correction.
            CheckReferenceWriteContract(problems);

            // ---- the acceptance classifier and the engine-warning capture mapping. Both are offline:
            // no exception object and no solver are involved.
            CheckAcceptanceClassifier(problems);
            CheckWarningCapture(problems);
            CheckGridApplicability(problems);

            foreach (string problem in problems) Console.WriteLine("SAFETY_FAILURE: " + problem);
            if (problems.Count > 0)
                throw new InvalidDataException("General hydraulic safety boundaries failed: " + problems.Count + " problem(s).");
            Console.WriteLine("GENERAL_HYDRAULIC_SAFETY=PASS");
            return 0;
        }

        public sealed class GridCollectionProbe
        {
            public int Count { get; set; }
            public object[] Values;
            public object this[int index] => Values[index];
        }
        public sealed class GridManagerProbe
        {
            public object Collection;
            public object ElementIDs() => Collection;
        }
        public sealed class ThrowingGridManagerProbe
        {
            public object ElementIDs() { throw new InvalidOperationException("Synthetic inventory failure"); }
        }
        private static void CheckGridApplicability(List<string> problems)
        {
            int before = problems.Count;
            Expect(problems, "complete empty grid inventory permits a no-grid model", () =>
            {
                int[] ids = GeneralModelCalculationHost.ConfiguredGridIds(new GridManagerProbe {
                    Collection = new GridCollectionProbe { Count = 0, Values = new object[0] } });
                if (GeneralModelCalculationHost.GridPreparationRequired(0, ids))
                    throw new InvalidDataException("An empty model should not require a grid.");
            }, false);
            Expect(problems, "a configured active grid still requires generation", () =>
            {
                int[] ids = GeneralModelCalculationHost.ConfiguredGridIds(new GridManagerProbe {
                    Collection = new GridCollectionProbe { Count = 2, Values = new object[] { 47, 46 } } });
                if (!ids.SequenceEqual(new[] { 46, 47 }) ||
                    !GeneralModelCalculationHost.GridPreparationRequired(46, ids))
                    throw new InvalidDataException("Configured 2D grid bypassed generation.");
            }, false);
            Expect(problems, "configured grid with no active identity blocks", () =>
                GeneralModelCalculationHost.GridPreparationRequired(0, new[] { 46 }), true);
            Expect(problems, "active identity absent from inventory blocks", () =>
                GeneralModelCalculationHost.GridPreparationRequired(47, new[] { 46 }), true);
            Expect(problems, "active identity with empty inventory blocks", () =>
                GeneralModelCalculationHost.GridPreparationRequired(46, new int[0]), true);
            Expect(problems, "unknown active identity blocks", () =>
                GeneralModelCalculationHost.GridPreparationRequired(null, new int[0]), true);
            Expect(problems, "negative active identity blocks", () =>
                GeneralModelCalculationHost.GridPreparationRequired(-1, new int[0]), true);
            Expect(problems, "unknown inventory blocks", () =>
                GeneralModelCalculationHost.GridPreparationRequired(0, null), true);
            Expect(problems, "duplicate configured identities block", () =>
                GeneralModelCalculationHost.GridPreparationRequired(46, new[] { 46, 46 }), true);
            Expect(problems, "nonpositive configured identity blocks", () =>
                GeneralModelCalculationHost.GridPreparationRequired(0, new[] { 0 }), true);
            Expect(problems, "missing manager is not an empty inventory", () =>
                GeneralModelCalculationHost.ConfiguredGridIds(null), true);
            Expect(problems, "null collection is not an empty inventory", () =>
                GeneralModelCalculationHost.ConfiguredGridIds(new GridManagerProbe()), true);
            Expect(problems, "failed enumeration is not an empty inventory", () =>
                GeneralModelCalculationHost.ConfiguredGridIds(new ThrowingGridManagerProbe()), true);
            foreach (var collection in new[] {
                new GridCollectionProbe { Count = -1, Values = new object[0] },
                new GridCollectionProbe { Count = GeneralModelCalculationHost.MaxConduits + 1, Values = new object[0] },
                new GridCollectionProbe { Count = 1, Values = new object[] { null } },
                new GridCollectionProbe { Count = 1, Values = new object[] { "46" } },
                new GridCollectionProbe { Count = 1, Values = new object[] { 0 } },
                new GridCollectionProbe { Count = 2, Values = new object[] { 46, 46 } },
                new GridCollectionProbe { Count = 2, Values = new object[] { 46 } } })
                Expect(problems, "incomplete or malformed inventory blocks", () =>
                    GeneralModelCalculationHost.ConfiguredGridIds(new GridManagerProbe { Collection = collection }), true);
            if (problems.Count == before) Console.WriteLine("GRID_APPLICABILITY_SAFETY=PASS; cases=20");
        }

        // The reference-write contract is the only thing standing between a reference correction and an
        // arbitrary field write. It is exercised on both sides here: the shapes it must accept, and the
        // shapes it must refuse - including a count widened with no extra element to justify it.
        private static void CheckReferenceWriteContract(List<string> problems)
        {
            Func<string, string, string, Dictionary<string, string>> evidence =
                (fields, elements, discovered) =>
                {
                    int total = 0;
                    foreach (string element in elements.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        total += int.Parse(element.Split(':')[2]);
                    return new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["GeneralModelTerrainBindWrites"] = total.ToString(),
                        [GeneralModelCalculationHost.ReferenceFieldsKey] = fields,
                        [GeneralModelCalculationHost.ReferenceElementsKey] = elements,
                        ["GeneralModelDataSourceElements"] = discovered
                    };
                };

            const string terrainFields = "DigitalTerrainModel_FilePath;" +
                "DigitalTerrainModel_RelativeFilePath;DigitalTerrainModel_FileProxy";
            const string sourceField = "TwoDDataSource_FileProxy";

            Expect(problems, "a terrain-plus-two-data-source correction is accepted", () =>
                RuntimeHostProtocol.ValidateReferenceWrites(evidence(terrainFields + ";" + sourceField,
                    "Terrain:12:3;TwoDDataSource:48:1;TwoDDataSource:54:1", "2")), false);

            Expect(problems, "a correction that writes no field is rejected", () =>
                RuntimeHostProtocol.ValidateReferenceWrites(evidence("", "", "0")), true);

            Expect(problems, "a field outside the reference allowlist is rejected", () =>
                RuntimeHostProtocol.ValidateReferenceWrites(evidence("DigitalTerrainModel_IsActive",
                    "Terrain:12:1", "0")), true);

            Expect(problems, "an engineering field is rejected as a reference write", () =>
                RuntimeHostProtocol.ValidateReferenceWrites(evidence("Conduit_DesignFlow",
                    "Terrain:12:1", "0")), true);

            Expect(problems, "more than three terrain writes are rejected", () =>
                RuntimeHostProtocol.ValidateReferenceWrites(evidence(terrainFields,
                    "Terrain:12:4", "0")), true);

            Expect(problems, "more than two writes on one data source are rejected", () =>
                RuntimeHostProtocol.ValidateReferenceWrites(evidence(
                    sourceField + ";TwoDDataSource_FileRelativePath;DigitalTerrainModel_FilePath",
                    "Terrain:12:1;TwoDDataSource:48:3", "1")), true);

            Expect(problems, "a count that disagrees with its element tallies is rejected", () =>
                {
                    Dictionary<string, string> forged = evidence(terrainFields, "Terrain:12:3", "0");
                    forged["GeneralModelTerrainBindWrites"] = "7";
                    RuntimeHostProtocol.ValidateReferenceWrites(forged);
                }, true);

            Expect(problems, "an element that may not be written is rejected", () =>
                RuntimeHostProtocol.ValidateReferenceWrites(evidence("DigitalTerrainModel_FilePath",
                    "Conduit:52:1", "0")), true);

            Expect(problems, "more data-source writes than the model exposes are rejected", () =>
                RuntimeHostProtocol.ValidateReferenceWrites(evidence(sourceField,
                    "TwoDDataSource:48:1;TwoDDataSource:54:1", "1")), true);

            Expect(problems, "a single data-source proxy write is accepted", () =>
                RuntimeHostProtocol.ValidateReferenceWrites(evidence(sourceField,
                    "TwoDDataSource:48:1", "1")), false);

            Expect(problems, "a missing write count is rejected", () =>
                RuntimeHostProtocol.ValidateReferenceWrites(
                    new Dictionary<string, string>(StringComparer.Ordinal)), true);
        }

        // Focused offline dispatch for the notification-severity rule: only the warning-capture mapping
        // and the acceptance classifier. No host is launched, no model is copied, no solver is reachable
        // and no temporary directory is created or deleted.
        internal static int RunWarningAcceptanceSafety()
        {
            var problems = new List<string>();
            CheckAcceptanceClassifier(problems);
            CheckWarningCapture(problems);

            foreach (string problem in problems) Console.WriteLine("WARNING_ACCEPTANCE_FAILURE: " + problem);
            if (problems.Count > 0)
                throw new InvalidDataException(
                    "General warning acceptance boundaries failed: " + problems.Count + " problem(s).");
            Console.WriteLine("GENERAL_WARNING_ACCEPTANCE_SAFETY=PASS");
            return 0;
        }

        // Replays the acceptance decision over observations that were already saved. This path reads
        // two files and runs a pure function: it launches no host, opens no model and cannot reach a
        // solver, which is why it is safe to run against historical evidence.
        internal static int ReplayAcceptance(string evidenceDirectory)
        {
            if (string.IsNullOrWhiteSpace(evidenceDirectory) || !Directory.Exists(evidenceDirectory))
                throw new DirectoryNotFoundException("Evidence directory was not found: " + evidenceDirectory);
            string calculationPath = Path.Combine(evidenceDirectory, "calculation-observation.json");
            string readbackPath = Path.Combine(evidenceDirectory, "readback-observation.json");
            if (!File.Exists(calculationPath)) throw new FileNotFoundException("No calculation observation.", calculationPath);
            if (!File.Exists(readbackPath)) throw new FileNotFoundException("No readback observation.", readbackPath);

            var serializer = RuntimeHostProtocol.Serializer();
            var calculation = serializer.Deserialize<GeneralCalculationObservation>(Read(calculationPath));
            var readback = serializer.Deserialize<GeneralReadbackObservation>(Read(readbackPath));
            GeneralAcceptance acceptance = GeneralCalculationAcceptance.Classify(calculation, readback);

            Console.WriteLine("REPLAY_ENGINE_OUTCOME=" + (calculation.EngineOutcome ?? "(none)"));
            Console.WriteLine("REPLAY_ERROR_TYPE=" + (calculation.ErrorType ?? "(none)"));
            Console.WriteLine("REPLAY_WARNING_TYPE=" + (calculation.WarningExceptionType ?? "(none)"));
            Console.WriteLine("REPLAY_WARNING_CAPTURE=" + (calculation.WarningCaptureStatus ?? "NOT_RECORDED"));
            Console.WriteLine("REPLAY_WARNING_COUNT=" + (calculation.WarningNotificationCount.HasValue
                ? calculation.WarningNotificationCount.Value.ToString(CultureInfo.InvariantCulture) : "null"));
            Console.WriteLine("REPLAY_SOLVER_INVOCATIONS=" + calculation.SolverRunInvocations);
            Console.WriteLine("REPLAY_SAVE=" + (calculation.SaveResultClassification ?? "(none)"));
            Console.WriteLine("REPLAY_AVAILABLE_ROWS=" + readback.AvailableRowCount);
            Console.WriteLine("REPLAY_VERDICT=" + acceptance.Verdict);
            Console.WriteLine("REPLAY_REASON=" + acceptance.Reason);
            Console.WriteLine("REPLAY_WARNINGS_UNRESOLVED=" + acceptance.WarningsUnresolved);
            Console.WriteLine("GENERAL_ACCEPTANCE_REPLAY=" + acceptance.Verdict);
            return string.Equals(acceptance.Verdict, GeneralCalculationAcceptance.Pass, StringComparison.Ordinal) ? 0 : 2;
        }

        private static string Read(string path) => File.ReadAllText(path).TrimStart('\uFEFF');

        // ------------------------------------------------------------------ acceptance and warnings

        private static GeneralCalculationObservation CleanCalculation() => new GeneralCalculationObservation
        {
            Status = GeneralModelCalculationHost.CompletedStatus,
            SolverRunInvocations = 1,
            SaveInvocations = 1,
            SaveSucceeded = true,
            SaveResultClassification = "PERSISTED",
            ScenarioComputeStatus = "Success",
            EngineOutcome = "COMPLETED_CLEAN",
            ScenarioId = 1,
            CalculationOptionsId = 31,
            Engine = "DelawareProductEngine",
            CalculationEventId = "event-1",
            WarningCaptureStatus = GeneralModelCalculationHost.WarningCaptureNotApplicable
        };

        private static GeneralReadbackObservation ValidReadback(int rows = 81)
        {
            var produced = new List<GeneralHydraulicResultRow>();
            for (int index = 0; index < rows; index++)
            {
                produced.Add(new GeneralHydraulicResultRow
                {
                    ElementId = 80 + index,
                    ElementLabel = "CO-" + (index + 1),
                    Category = "Conduit",
                    Metric = "Flow",
                    Value = 0.001,
                    Unit = "ft3/s",
                    Status = "AVAILABLE"
                });
            }
            return new GeneralReadbackObservation
            {
                Status = GeneralModelCalculationHost.ReadVerifiedStatus,
                HasResults = true,
                RowCount = rows,
                AvailableRowCount = rows,
                ScenarioId = 1,
                CalculationOptionsId = 31,
                Engine = "DelawareProductEngine",
                CalculationEventId = "event-1",
                Rows = produced.ToArray()
            };
        }

        private static void ExpectVerdict(List<string> problems, string description,
            GeneralAcceptance acceptance, string expected)
        {
            if (!string.Equals(acceptance.Verdict, expected, StringComparison.Ordinal))
                problems.Add(description + " (expected " + expected + ", got " + acceptance.Verdict +
                    "/" + acceptance.Reason + ")");
        }

        // A verified, persisted, read-back calculation that exited through the vendor's notification
        // exception, carrying exactly the severities asked for. Everything except the notification
        // evidence is identical to CleanCalculation, so a verdict change can only come from that.
        private static GeneralCalculationObservation NotificationCalculation(params string[] levels)
        {
            var produced = new List<GeneralEngineWarning>();
            for (int index = 0; index < levels.Length; index++)
            {
                produced.Add(new GeneralEngineWarning
                {
                    MessageKey = "EngineNotification." + (index + 1).ToString(CultureInfo.InvariantCulture),
                    Level = levels[index],
                    MessageId = (800080 + index).ToString(CultureInfo.InvariantCulture),
                    ElementId = 80 + index,
                    ElementType = "Conduit",
                    ScenarioId = 1
                });
            }
            GeneralCalculationObservation observation = CleanCalculation();
            observation.EngineOutcome = "COMPLETED_WITH_EXCEPTION_REPORTED";
            observation.ErrorType = GeneralModelCalculationHost.EngineWarningsExceptionType;
            observation.WarningExceptionType = GeneralModelCalculationHost.EngineWarningsExceptionType;
            observation.WarningCaptureStatus = GeneralModelCalculationHost.WarningCaptureCaptured;
            observation.WarningNotificationCount = levels.Length;
            observation.Warnings = produced.ToArray();
            return observation;
        }

        // The five required acceptance outcomes, plus the guards that must not be bypassed on the way.
        private static void CheckAcceptanceClassifier(List<string> problems)
        {
            ExpectVerdict(problems, "clean engine with a verified readback is PASS",
                GeneralCalculationAcceptance.Classify(CleanCalculation(), ValidReadback()),
                GeneralCalculationAcceptance.Pass);

            // 2. A warning exception carrying real warnings and real rows is PARTIAL - never PASS. The
            // fixture declares two notifications and now actually holds two, so the count and the array
            // agree and only the severity can decide the verdict.
            GeneralCalculationObservation warnings = NotificationCalculation("Warning", "Warning");
            GeneralAcceptance warningAcceptance = GeneralCalculationAcceptance.Classify(warnings, ValidReadback());
            ExpectVerdict(problems, "an engine warning exception with valid rows is PARTIAL",
                warningAcceptance, GeneralCalculationAcceptance.Partial);
            if (!string.Equals(warningAcceptance.Reason, GeneralCalculationAcceptance.WarningsUnresolved,
                    StringComparison.Ordinal))
                problems.Add("engine warnings did not report ENGINE_WARNINGS_UNRESOLVED");
            if (!warningAcceptance.WarningsUnresolved)
                problems.Add("the warning acceptance does not flag unresolved warnings");
            if (string.Equals(warningAcceptance.Verdict, GeneralCalculationAcceptance.Pass, StringComparison.Ordinal))
                problems.Add("engine warnings were promoted to PASS");

            // 2a. A single, completely captured Information notice is the one warning-bearing outcome
            // that may be accepted - and it must say so with its own reason, not the clean-run reason.
            GeneralCalculationObservation informational = NotificationCalculation("Information");
            GeneralAcceptance informationalAcceptance = GeneralCalculationAcceptance.Classify(informational,
                ValidReadback());
            ExpectVerdict(problems, "one complete Information notification is PASS", informationalAcceptance,
                GeneralCalculationAcceptance.Pass);
            if (!string.Equals(informationalAcceptance.Reason,
                    GeneralCalculationAcceptance.InformationalNotificationsVerifiedReadback, StringComparison.Ordinal))
                problems.Add("an informational notification was not reported with its own reason (" +
                    informationalAcceptance.Reason + ")");
            if (informationalAcceptance.WarningsUnresolved)
                problems.Add("an informational notification was still reported as unresolved");
            if (!informationalAcceptance.WarningDetailsAvailable)
                problems.Add("an informational notification was reported without available details");
            if (string.Equals(informationalAcceptance.Reason, "CLEAN_ENGINE_VERIFIED_READBACK",
                    StringComparison.Ordinal))
                problems.Add("an informational notification was reported as an exception-free run");
            if (informationalAcceptance.Explanation == null ||
                informationalAcceptance.Explanation.IndexOf("informational", StringComparison.OrdinalIgnoreCase) < 0)
                problems.Add("the informational acceptance does not state that the exception was informational");

            // 2b. Several complete Information notices are accepted the same way.
            ExpectVerdict(problems, "multiple complete Information notifications are PASS",
                GeneralCalculationAcceptance.Classify(
                    NotificationCalculation("Information", "Information", "Information"), ValidReadback()),
                GeneralCalculationAcceptance.Pass);

            // 2c. The severity token is matched exactly but without regard to case or padding.
            ExpectVerdict(problems, "an Information severity with whitespace and mixed case is PASS",
                GeneralCalculationAcceptance.Classify(NotificationCalculation("  information  "), ValidReadback()),
                GeneralCalculationAcceptance.Pass);

            // 2d. Mixed severities keep the conservative verdict.
            ExpectVerdict(problems, "Information plus Warning is PARTIAL",
                GeneralCalculationAcceptance.Classify(
                    NotificationCalculation("Information", "Warning"), ValidReadback()),
                GeneralCalculationAcceptance.Partial);

            // 2e. An Error-level notification inside a warning exception is never accepted.
            ExpectVerdict(problems, "an Error severity notification is never PASS",
                GeneralCalculationAcceptance.Classify(NotificationCalculation("Error"), ValidReadback()),
                GeneralCalculationAcceptance.Partial);

            // 2f. A missing or unrecognised severity is not Information by inference.
            ExpectVerdict(problems, "a missing severity is PARTIAL",
                GeneralCalculationAcceptance.Classify(
                    NotificationCalculation(new string[] { null }), ValidReadback()),
                GeneralCalculationAcceptance.Partial);
            ExpectVerdict(problems, "an unrecognised severity token is PARTIAL",
                GeneralCalculationAcceptance.Classify(NotificationCalculation("Info"), ValidReadback()),
                GeneralCalculationAcceptance.Partial);

            // 2g. Incomplete capture of any shape stays PARTIAL and never reads as clean.
            GeneralCalculationObservation missingArray = NotificationCalculation("Information");
            missingArray.Warnings = null;
            ExpectVerdict(problems, "a missing notification array is PARTIAL",
                GeneralCalculationAcceptance.Classify(missingArray, ValidReadback()),
                GeneralCalculationAcceptance.Partial);

            GeneralCalculationObservation nullEntry = NotificationCalculation("Information");
            nullEntry.Warnings = new GeneralEngineWarning[] { null };
            ExpectVerdict(problems, "a null notification entry is never PASS",
                GeneralCalculationAcceptance.Classify(nullEntry, ValidReadback()),
                GeneralCalculationAcceptance.Partial);

            GeneralCalculationObservation noCount = NotificationCalculation("Information");
            noCount.WarningNotificationCount = null;
            ExpectVerdict(problems, "a missing notification count is PARTIAL",
                GeneralCalculationAcceptance.Classify(noCount, ValidReadback()),
                GeneralCalculationAcceptance.Partial);

            GeneralCalculationObservation zeroCount = NotificationCalculation("Information");
            zeroCount.WarningNotificationCount = 0;
            ExpectVerdict(problems, "a zero notification count is PARTIAL",
                GeneralCalculationAcceptance.Classify(zeroCount, ValidReadback()),
                GeneralCalculationAcceptance.Partial);

            GeneralCalculationObservation shortCount = NotificationCalculation("Information");
            shortCount.WarningNotificationCount = 2;
            ExpectVerdict(problems, "a count that disagrees with the array is PARTIAL",
                GeneralCalculationAcceptance.Classify(shortCount, ValidReadback()),
                GeneralCalculationAcceptance.Partial);

            GeneralCalculationObservation truncated = NotificationCalculation("Information");
            int bound = GeneralModelCalculationHost.MaxEngineWarnings;
            var pastBound = new GeneralEngineWarning[bound + 1];
            for (int index = 0; index <= bound; index++)
                pastBound[index] = new GeneralEngineWarning { MessageKey = "EngineNotification.1", Level = "Information" };
            truncated.Warnings = pastBound;
            truncated.WarningNotificationCount = pastBound.Length;
            ExpectVerdict(problems, "a collection past the capture bound is never PASS",
                GeneralCalculationAcceptance.Classify(truncated, ValidReadback()),
                GeneralCalculationAcceptance.Partial);

            GeneralCalculationObservation captureFailed = NotificationCalculation("Information");
            captureFailed.WarningCaptureFailure = "the capture was truncated";
            ExpectVerdict(problems, "a recorded capture failure is PARTIAL",
                GeneralCalculationAcceptance.Classify(captureFailed, ValidReadback()),
                GeneralCalculationAcceptance.Partial);

            GeneralCalculationObservation noKey = NotificationCalculation("Information");
            noKey.Warnings[0].MessageKey = "   ";
            ExpectVerdict(problems, "a notification without a message key is PARTIAL",
                GeneralCalculationAcceptance.Classify(noKey, ValidReadback()),
                GeneralCalculationAcceptance.Partial);

            GeneralCalculationObservation contradictory = NotificationCalculation("Information");
            contradictory.ErrorType = "System.InvalidOperationException";
            ExpectVerdict(problems, "contradictory exception types are never PASS",
                GeneralCalculationAcceptance.Classify(contradictory, ValidReadback()),
                GeneralCalculationAcceptance.Partial);

            // 2b. Details unavailable: still PARTIAL, and explicitly not clean.
            GeneralCalculationObservation unknownWarnings = CleanCalculation();
            unknownWarnings.EngineOutcome = "COMPLETED_WITH_EXCEPTION_REPORTED";
            unknownWarnings.WarningExceptionType = GeneralModelCalculationHost.EngineWarningsExceptionType;
            unknownWarnings.WarningCaptureStatus = GeneralModelCalculationHost.WarningCaptureUnknown;
            unknownWarnings.WarningCaptureFailure = "UserNotifications was null";
            GeneralAcceptance unknownAcceptance = GeneralCalculationAcceptance.Classify(unknownWarnings,
                ValidReadback());
            ExpectVerdict(problems, "unavailable warning details stay PARTIAL rather than clean",
                unknownAcceptance, GeneralCalculationAcceptance.Partial);
            if (unknownAcceptance.WarningDetailsAvailable)
                problems.Add("unavailable warning details were reported as available");

            // 3. Any other engine exception is BLOCKED.
            GeneralCalculationObservation otherFailure = CleanCalculation();
            otherFailure.EngineOutcome = "COMPLETED_WITH_EXCEPTION_REPORTED";
            otherFailure.ErrorType = "System.InvalidDataException";
            ExpectVerdict(problems, "a non-warning engine exception is BLOCKED",
                GeneralCalculationAcceptance.Classify(otherFailure, ValidReadback()),
                GeneralCalculationAcceptance.Blocked);

            GeneralCalculationObservation noOutcome = CleanCalculation();
            noOutcome.EngineOutcome = null;
            ExpectVerdict(problems, "an unknown engine outcome is BLOCKED",
                GeneralCalculationAcceptance.Classify(noOutcome, ValidReadback()),
                GeneralCalculationAcceptance.Blocked);

            GeneralCalculationObservation inconsistent = CleanCalculation();
            inconsistent.WarningExceptionType = GeneralModelCalculationHost.EngineWarningsExceptionType;
            ExpectVerdict(problems, "a warning type with a clean outcome claim is BLOCKED",
                GeneralCalculationAcceptance.Classify(inconsistent, ValidReadback()),
                GeneralCalculationAcceptance.Blocked);

            // 4. Missing or invalid readback, and unpersisted or over-budget solves, are BLOCKED.
            ExpectVerdict(problems, "a missing readback is BLOCKED",
                GeneralCalculationAcceptance.Classify(CleanCalculation(), null),
                GeneralCalculationAcceptance.Blocked);

            GeneralReadbackObservation noResults = ValidReadback();
            noResults.HasResults = false;
            ExpectVerdict(problems, "a readback without results is BLOCKED",
                GeneralCalculationAcceptance.Classify(CleanCalculation(), noResults),
                GeneralCalculationAcceptance.Blocked);

            GeneralReadbackObservation noRows = ValidReadback();
            noRows.AvailableRowCount = 0;
            ExpectVerdict(problems, "a readback with no available rows is BLOCKED",
                GeneralCalculationAcceptance.Classify(CleanCalculation(), noRows),
                GeneralCalculationAcceptance.Blocked);

            GeneralReadbackObservation noValues = ValidReadback();
            foreach (GeneralHydraulicResultRow row in noValues.Rows) row.Value = null;
            ExpectVerdict(problems, "a readback with no numeric hydraulic value is BLOCKED",
                GeneralCalculationAcceptance.Classify(CleanCalculation(), noValues),
                GeneralCalculationAcceptance.Blocked);

            GeneralReadbackObservation unverified = ValidReadback();
            unverified.Status = "READBACK_FAILED";
            ExpectVerdict(problems, "an unverified readback is BLOCKED",
                GeneralCalculationAcceptance.Classify(CleanCalculation(), unverified),
                GeneralCalculationAcceptance.Blocked);

            GeneralReadbackObservation foreign = ValidReadback();
            foreign.CalculationEventId = "another-event";
            ExpectVerdict(problems, "a readback from another event is BLOCKED",
                GeneralCalculationAcceptance.Classify(CleanCalculation(), foreign),
                GeneralCalculationAcceptance.Blocked);

            GeneralCalculationObservation notPersisted = CleanCalculation();
            notPersisted.SaveResultClassification = "NOT_PERSISTED";
            ExpectVerdict(problems, "an unpersisted calculation is BLOCKED",
                GeneralCalculationAcceptance.Classify(notPersisted, ValidReadback()),
                GeneralCalculationAcceptance.Blocked);

            GeneralCalculationObservation twoSolves = CleanCalculation();
            twoSolves.SolverRunInvocations = 2;
            ExpectVerdict(problems, "more than one solve is BLOCKED",
                GeneralCalculationAcceptance.Classify(twoSolves, ValidReadback()),
                GeneralCalculationAcceptance.Blocked);

            // 5. An accepted informational notification must still be visible in the human-readable
            // report, with its own level, key and reason - an informational PASS is not a silent one.
            CheckNotificationVisibility(problems, informational, informationalAcceptance);
        }

        // The report is the only place a user sees what the engine said. This reads the same private
        // formatter the service uses; nothing is launched and no file is written.
        private static void CheckNotificationVisibility(List<string> problems,
            GeneralCalculationObservation calculation, GeneralAcceptance acceptance)
        {
            MethodInfo build = typeof(GeneralModelCalculationService).GetMethod("BuildSummary",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (build == null) { problems.Add("the calculation summary formatter was not found"); return; }
            string summary = build.Invoke(null, new object[]
            {
                calculation, ValidReadback(), "FEP-AOC-030826.stsw", acceptance
            }) as string;
            if (string.IsNullOrEmpty(summary)) { problems.Add("the calculation summary was empty"); return; }
            if (summary.IndexOf("Engine notifications", StringComparison.Ordinal) < 0)
                problems.Add("an informational notification was not labelled as an engine notification");
            if (summary.IndexOf("EngineNotification.1", StringComparison.Ordinal) < 0)
                problems.Add("the original notification key was not printed for an informational PASS");
            if (summary.IndexOf("- Information EngineNotification.1", StringComparison.Ordinal) < 0)
                problems.Add("the original notification level and key were not printed together");
            if (summary.IndexOf(GeneralCalculationAcceptance.InformationalNotificationsVerifiedReadback,
                    StringComparison.Ordinal) < 0)
                problems.Add("the informational acceptance reason was not printed");
            if (summary.IndexOf("CLEAN_ENGINE_VERIFIED_READBACK", StringComparison.Ordinal) >= 0)
                problems.Add("an informational PASS was described as a clean engine run");

            // The machine-readable record must carry the same notification evidence, so a later reader
            // can never reconstruct this verdict as a silent, exception-free run.
            MethodInfo record = typeof(GeneralModelCalculationService).GetMethod("AcceptanceJson",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (record == null) { problems.Add("the machine-readable acceptance record was not found"); return; }
            string json = record.Invoke(null, new object[] { acceptance, calculation }) as string;
            if (string.IsNullOrEmpty(json)) { problems.Add("the machine-readable acceptance record was empty"); return; }
            if (json.IndexOf("\"verdict\":\"PASS\"", StringComparison.Ordinal) < 0)
                problems.Add("the machine-readable record does not carry the informational PASS verdict");
            if (json.IndexOf(GeneralCalculationAcceptance.InformationalNotificationsVerifiedReadback,
                    StringComparison.Ordinal) < 0)
                problems.Add("the machine-readable record does not carry the informational reason");
            if (json.IndexOf("EngineNotification.1", StringComparison.Ordinal) < 0)
                problems.Add("the machine-readable record dropped the notification key");
            if (json.IndexOf("\"warning_notification_count\":1", StringComparison.Ordinal) < 0)
                problems.Add("the machine-readable record dropped the notification count");
            if (json.IndexOf("\"warning_capture_status\":\"CAPTURED\"", StringComparison.Ordinal) < 0)
                problems.Add("the machine-readable record dropped the capture status");
            if (json.IndexOf("\"warnings_unresolved\":false", StringComparison.Ordinal) < 0)
                problems.Add("the machine-readable record still flags unresolved warnings");
        }

        // Synthetic notifications carrying the vendor's member names. The mapping is exercised without
        // an exception object and without the vendor assembly, so nothing is solved.
        private sealed class SyntheticNotification
        {
            public string MessageKey { get; set; }
            public string Level { get; set; }
            public int ElementId { get; set; }
            public string ElementType { get; set; }
            public int ScenarioId { get; set; }
            public string MessageId { get; set; }
            public string SourceKey { get; set; }
            public object Parameters { get; set; }
        }

        private static void CheckWarningCapture(List<string> problems)
        {
            var parameters = new Dictionary<string, string> { ["Conduit"] = "CO-6", ["Depth"] = "0.42" };
            var notifications = new List<object>
            {
                new SyntheticNotification
                {
                    MessageKey = "EngineWarning.HydraulicGradeBelowInvert",
                    Level = "Warning",
                    ElementId = 80,
                    ElementType = "Conduit",
                    ScenarioId = 1,
                    MessageId = "42",
                    SourceKey = "DelawareProductEngine",
                    Parameters = parameters
                },
                new SyntheticNotification
                {
                    MessageKey = "EngineWarning.IterationLimit",
                    Level = "Warning",
                    ElementId = 61,
                    ElementType = "Node",
                    ScenarioId = 1,
                    MessageId = "43",
                    SourceKey = "DelawareProductEngine",
                    Parameters = null
                }
            };

            string failure;
            GeneralEngineWarning[] mapped = GeneralModelCalculationHost.MapEngineWarnings(notifications, out failure);
            if (failure != null) { problems.Add("warning mapping failed: " + failure); return; }
            if (mapped == null || mapped.Length != 2)
            {
                problems.Add("warning mapping did not produce two records");
                return;
            }
            if (!string.Equals(mapped[0].MessageKey, "EngineWarning.HydraulicGradeBelowInvert", StringComparison.Ordinal))
                problems.Add("warning MessageKey was not mapped");
            if (!string.Equals(mapped[0].Level, "Warning", StringComparison.Ordinal))
                problems.Add("warning Level was not mapped");
            if (mapped[0].ElementId != 80) problems.Add("warning ElementId was not mapped");
            if (!string.Equals(mapped[0].ElementType, "Conduit", StringComparison.Ordinal))
                problems.Add("warning ElementType was not mapped");
            if (mapped[0].ScenarioId != 1) problems.Add("warning ScenarioId was not mapped");
            if (!string.Equals(mapped[0].MessageId, "42", StringComparison.Ordinal))
                problems.Add("warning MessageId was not mapped");
            if (!string.Equals(mapped[0].SourceKey, "DelawareProductEngine", StringComparison.Ordinal))
                problems.Add("warning SourceKey was not mapped");
            if (mapped[0].Parameters == null || mapped[0].Parameters.Length != 2 ||
                !mapped[0].Parameters.Contains("Conduit=CO-6"))
                problems.Add("warning Parameters were not flattened to key=value");
            if (mapped[1].Parameters == null || mapped[1].Parameters.Length != 0)
                problems.Add("a notification without parameters produced parameter text");

            // Unavailable details are UNKNOWN, never an empty-but-clean result.
            GeneralEngineWarning[] absent = GeneralModelCalculationHost.MapEngineWarnings(null, out failure);
            if (absent != null || string.IsNullOrEmpty(failure))
                problems.Add("a null notification collection was not reported as unavailable");

            // A hole in the collection cannot be reported as a shorter complete list.
            GeneralEngineWarning[] withHole = GeneralModelCalculationHost.MapEngineWarnings(
                new List<object> { notifications[0], null }, out failure);
            if (withHole != null || string.IsNullOrEmpty(failure))
                problems.Add("a null notification entry was silently skipped instead of failing the capture");

            // Past the bound the collection is truncated, so the capture must be UNKNOWN - a list
            // silently cut at the limit would hide every later warning.
            var pastBound = new List<object>();
            for (int index = 0; index <= GeneralModelCalculationHost.MaxEngineWarnings; index++)
                pastBound.Add(notifications[0]);
            GeneralEngineWarning[] truncated = GeneralModelCalculationHost.MapEngineWarnings(pastBound, out failure);
            if (truncated != null || string.IsNullOrEmpty(failure))
                problems.Add("a collection past the capture bound was returned as a complete capture");

            // Exactly at the bound is still complete and must not be rejected.
            var atBound = new List<object>();
            for (int index = 0; index < GeneralModelCalculationHost.MaxEngineWarnings; index++)
                atBound.Add(notifications[0]);
            GeneralEngineWarning[] complete = GeneralModelCalculationHost.MapEngineWarnings(atBound, out failure);
            if (failure != null || complete == null || complete.Length != GeneralModelCalculationHost.MaxEngineWarnings)
                problems.Add("a collection of exactly the capture bound was not accepted as complete");

            Type vendorType;
            if (GeneralModelCalculationHost.IsEngineWarningsException(new InvalidDataException("x"), out vendorType))
                problems.Add("an unrelated exception was detected as an engine warning exception");
            if (GeneralModelCalculationHost.IsEngineWarningsException(null, out vendorType))
                problems.Add("a null exception was detected as an engine warning exception");
        }

        private static void CheckWiring(List<string> problems)
        {
            var adapter = typeof(GeneralModelCalculationService).Assembly;
            Type panel = adapter.GetType("SewerPilot.OpenFlows.Adapter.GeneralModelCalculationPanel", false);
            Type form = adapter.GetType("SewerPilot.OpenFlows.Adapter.ProjectWorkspaceForm", false);
            Type workflow = adapter.GetType("SewerPilot.OpenFlows.Adapter.SingleOperatorDesktopWorkflow", false);
            if (panel == null) { problems.Add("calculation panel type is absent"); return; }
            if (form == null) { problems.Add("workspace form type is absent"); return; }
            if (workflow == null) { problems.Add("desktop workflow type is absent"); return; }

            var button = panel.GetField("CalculateExistingModel", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (button == null || button.FieldType != typeof(Button))
                problems.Add("the Calculate Existing Model action is absent from the panel");

            var workspaceField = form.GetField("HydraulicCalculationWorkspace",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (workspaceField == null || workspaceField.FieldType != panel)
                problems.Add("the workspace form does not host the calculation panel");

            var ready = form.GetMethod("SetCalculationReady", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (ready == null) problems.Add("the calculation readiness gate is absent from the form");

            if (!References(workflow, "GeneralModelCalculationService", "Execute"))
                problems.Add("the desktop workflow does not invoke the general calculation service");
            if (!References(workflow, "GeneralModelCalculationPanel", "CalculateExistingModel"))
                problems.Add("the desktop workflow does not bind the Calculate Existing Model action");
            if (!References(workflow, "GeneralModelCalculationPanel", "ShowReceipt"))
                problems.Add("the desktop workflow does not present the accepted results");
            if (!References(workflow, "GeneralModelCalculationPanel", "BeginCalculation"))
                problems.Add("the desktop workflow does not enter the calculation busy state");

            // The action must be disabled until a model is loaded, and enabled afterwards.
            RunOnSta(() =>
            {
                try
                {
                    using (var instance = (Form)Activator.CreateInstance(form,
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
                        new object[] { GravityOperatingMode.SingleOperator }, CultureInfo.InvariantCulture))
                    {
                        var panelInstance = (GeneralModelCalculationPanel)workspaceField.GetValue(instance);
                        if (panelInstance.CalculateExistingModel.Enabled)
                            problems.Add("the calculation action is enabled before a model is loaded");
                        ready.Invoke(instance, new object[] { true });
                        if (!panelInstance.CalculateExistingModel.Enabled)
                            problems.Add("the calculation action stays disabled after a model is loaded");
                        ready.Invoke(instance, new object[] { false });
                        if (panelInstance.CalculateExistingModel.Enabled)
                            problems.Add("the calculation action stays enabled after the model is cleared");
                    }
                }
                catch (Exception error)
                {
                    problems.Add("workspace form wiring could not be exercised: " + error.GetType().Name);
                }
            });
        }

        // First available conduit row for one metric with a non-zero value, falling back to any value.
        private static GeneralHydraulicResultRow First(GeneralHydraulicResultRow[] rows, string metric) =>
            rows.FirstOrDefault(r => r.Category == "Conduit" && r.Status == "AVAILABLE" && r.Value.HasValue &&
                                     r.Value.Value != 0d && (metric == null || r.Metric == metric))
            ?? rows.FirstOrDefault(r => r.Category == "Conduit" && r.Status == "AVAILABLE" && r.Value.HasValue &&
                                        (metric == null || r.Metric == metric));

        private static void RunOnSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception error) { failure = error; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        // Scans the compiled IL of a type for a reference to one exact member (method or field).
        // This is a deterministic wiring check: if the workflow did not reference the service or the
        // action, no token would resolve to it. Async event handlers compile into nested
        // compiler-generated state machines, so nested types are scanned as well.
        private static bool References(Type type, string declaringTypeName, string memberName)
        {
            if (ReferencesIn(type, declaringTypeName, memberName)) return true;
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                if (References(nested, declaringTypeName, memberName)) return true;
            return false;
        }

        private static bool ReferencesIn(Type type, string declaringTypeName, string memberName)
        {
            const BindingFlags all = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public |
                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (MethodBase method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                MethodBody body;
                try { body = method.GetMethodBody(); }
                catch (Exception) { continue; }
                if (body == null) continue;
                byte[] il = body.GetILAsByteArray();
                if (il == null) continue;
                for (int index = 0; index + 4 < il.Length; index++)
                {
                    // call/callvirt (method) and ldfld/ldsfld/stfld (field) all carry a 4-byte token.
                    byte opcode = il[index];
                    if (opcode != 0x28 && opcode != 0x6F && opcode != 0x7B && opcode != 0x7E && opcode != 0x7D)
                        continue;
                    int token = BitConverter.ToInt32(il, index + 1);
                    if (token == 0) continue;
                    try
                    {
                        var resolved = method.Module.ResolveMember(token, Type.EmptyTypes, Type.EmptyTypes);
                        if (resolved == null || resolved.Name != memberName) continue;
                        Type owner = resolved.DeclaringType;
                        if (owner != null && (owner.FullName == declaringTypeName || owner.Name == declaringTypeName))
                            return true;
                    }
                    catch (Exception) { /* not a resolvable member token at this offset */ }
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ helpers

        private static void Expect(List<string> problems, string description, Action action, bool shouldThrow)
        {
            try
            {
                action();
                if (shouldThrow) problems.Add(description + " (was accepted)");
            }
            catch (Exception)
            {
                if (!shouldThrow) problems.Add(description + " (was rejected)");
            }
        }

        // Asserts one metric-classification outcome: the availability status and, when numeric,
        // the exact value. A Boolean must classify as available with no numeric value.
        private static void Check(List<string> problems, string description, string rawValue,
            string expectedStatus, double? expectedValue)
        {
            double? value;
            string status = GeneralModelCalculationHost.ClassifyMetricValue(rawValue, out value);
            if (!string.Equals(status, expectedStatus, StringComparison.Ordinal))
                problems.Add(description + ": status " + status + " != " + expectedStatus);
            bool valueMatches = expectedValue.HasValue ? value == expectedValue : !value.HasValue;
            if (!valueMatches)
                problems.Add(description + ": value " +
                    (value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "null") + " != " +
                    (expectedValue.HasValue ? expectedValue.Value.ToString("R", CultureInfo.InvariantCulture) : "null"));
        }

        private static HostRequest Request(string model, string directory, string digest, string phase,
            string terrainTarget = null)
        {
            var diagnostics = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [GeneralModelCalculationHost.CopyIdKey] = Path.GetFileName(directory),
                [GeneralModelCalculationHost.InventoryKey] = digest,
                [GeneralModelCalculationHost.PhaseKey] = phase
            };
            if (phase == GeneralModelCalculationHost.PhaseRead)
            {
                diagnostics[GeneralModelCalculationHost.ScenarioKey] = "1";
                diagnostics[GeneralModelCalculationHost.OptionsKey] = "1";
                diagnostics[GeneralModelCalculationHost.EngineKey] = "TestEngine";
                diagnostics[GeneralModelCalculationHost.EventKey] = new string('a', 32);
            }
            if (phase == GeneralModelCalculationHost.PhaseBind || phase == GeneralModelCalculationHost.PhaseVerify)
            {
                if (terrainTarget == null) diagnostics[GeneralModelCalculationHost.TargetKey] = "";
                else diagnostics[GeneralModelCalculationHost.TargetKey] = terrainTarget;
            }
            return new HostRequest
            {
                SchemaVersion = 1,
                RunId = Guid.NewGuid().ToString("N"),
                Operation = GeneralModelCalculationHost.Operation,
                TrustedTempRoot = Path.GetTempPath(),
                WorkingModelPath = model,
                WorkingCopyId = Path.GetFileName(directory),
                RuntimeCandidateCount = 1,
                DetectedArchitecture = "x64",
                SelectedRuntime = Path.GetDirectoryName(typeof(GeneralModelCalculationService).Assembly.Location),
                CoreFiles = new HostMetadata[0],
                Diagnostics = diagnostics
            };
        }

        private static List<string> Fingerprints(IEnumerable<string> paths)
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

        private static T ReadObservation<T>(string directory, string name) where T : class
        {
            string path = Path.Combine(directory, name);
            if (!File.Exists(path)) return null;
            return RuntimeHostProtocol.Serializer().Deserialize<T>(File.ReadAllText(path));
        }

        private static void WriteEvidence(string directory, GeneralCalculationReceipt receipt,
            GeneralCalculationObservation calculation, GeneralReadbackObservation readback,
            List<string> before, List<string> after, bool unchanged,
            GeneralHydraulicResultRow conduit, GeneralHydraulicResultRow structure, List<string> problems,
            GeneralAcceptance acceptance, string verdict)
        {
            var json = new StringBuilder();
            json.Append("{\r\n");
            json.Append("  \"verdict\": \"").Append(verdict).Append("\",\r\n");
            json.Append("  \"acceptance_reason\": \"").Append(acceptance.Reason).Append("\",\r\n");
            json.Append("  \"acceptance_explanation\": ").Append(Quote(acceptance.Explanation)).Append(",\r\n");
            json.Append("  \"warnings_unresolved\": ").Append(acceptance.WarningsUnresolved ? "true" : "false").Append(",\r\n");
            json.Append("  \"engine_outcome\": ").Append(Quote(calculation.EngineOutcome)).Append(",\r\n");
            json.Append("  \"warning_capture_status\": ").Append(Quote(calculation.WarningCaptureStatus)).Append(",\r\n");
            json.Append("  \"warning_exception_type\": ")
                .Append(Quote(calculation.WarningExceptionType ?? calculation.ErrorType)).Append(",\r\n");
            json.Append("  \"warning_notification_count\": ")
                .Append(calculation.WarningNotificationCount.HasValue
                    ? calculation.WarningNotificationCount.Value.ToString(CultureInfo.InvariantCulture)
                    : "null").Append(",\r\n");
            json.Append("  \"warning_capture_failure\": ").Append(Quote(calculation.WarningCaptureFailure)).Append(",\r\n");
            json.Append("  \"warnings\": ").Append(Warnings(calculation.Warnings)).Append(",\r\n");
            json.Append("  \"timestamp_utc\": \"").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\",\r\n");
            json.Append("  \"model_file\": \"").Append(Path.GetFileName(receipt.WorkingModelPath)).Append("\",\r\n");
            json.Append("  \"scenario_id\": ").Append(receipt.ScenarioId).Append(",\r\n");
            json.Append("  \"calculation_options_id\": ").Append(receipt.CalculationOptionsId).Append(",\r\n");
            json.Append("  \"engine\": \"").Append(receipt.Engine).Append("\",\r\n");
            json.Append("  \"engine_selection_basis\": \"").Append(calculation.EngineSelectionBasis).Append("\",\r\n");
            json.Append("  \"calculation_event_id\": \"").Append(receipt.CalculationEventId).Append("\",\r\n");
            json.Append("  \"solver_run_invocations\": ").Append(calculation.SolverRunInvocations).Append(",\r\n");
            json.Append("  \"save_invocations\": ").Append(calculation.SaveInvocations).Append(",\r\n");
            json.Append("  \"scenario_compute_status\": \"").Append(calculation.ScenarioComputeStatus).Append("\",\r\n");
            json.Append("  \"save_result_classification\": \"").Append(calculation.SaveResultClassification).Append("\",\r\n");
            json.Append("  \"conduits_read\": ").Append(readback.ConduitCount).Append(",\r\n");
            json.Append("  \"structures_read\": ").Append(readback.StructureCount).Append(",\r\n");
            json.Append("  \"result_rows\": ").Append(readback.RowCount).Append(",\r\n");
            json.Append("  \"available_rows\": ").Append(readback.AvailableRowCount).Append(",\r\n");
            json.Append("  \"conduit_sample\": ").Append(Row(conduit)).Append(",\r\n");
            json.Append("  \"structure_sample\": ").Append(Row(structure)).Append(",\r\n");
            json.Append("  \"source_family_unchanged\": ").Append(unchanged ? "true" : "false").Append(",\r\n");
            json.Append("  \"source_family_before\": [").Append(string.Join(", ", before.Select(Quote))).Append("],\r\n");
            json.Append("  \"source_family_after\": [").Append(string.Join(", ", after.Select(Quote))).Append("],\r\n");
            json.Append("  \"problems\": [").Append(string.Join(", ", problems.Select(Quote))).Append("]\r\n");
            json.Append("}\r\n");
            File.WriteAllText(Path.Combine(directory, "general-hydraulic-execution.json"), json.ToString(),
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "source-family-before.txt"),
                string.Join(Environment.NewLine, before), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "source-family-after.txt"),
                string.Join(Environment.NewLine, after), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "desktop-acceptance.txt"), receipt.Summary, new UTF8Encoding(false));
        }

        private static string Row(GeneralHydraulicResultRow row)
        {
            if (row == null) return "null";
            return "{ \"category\": " + Quote(row.Category) + ", \"element\": " + Quote(row.ElementLabel) +
                   ", \"element_id\": " + row.ElementId.ToString(CultureInfo.InvariantCulture) +
                   ", \"metric\": " + Quote(row.Metric) + ", \"value\": " +
                   (row.Value.HasValue ? row.Value.Value.ToString("R", CultureInfo.InvariantCulture) : "null") +
                   ", \"unit\": " + Quote(row.Unit) + ", \"status\": " + Quote(row.Status) + " }";
        }

        private static string Quote(string value) =>
            value == null ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string Warnings(GeneralEngineWarning[] warnings)
        {
            if (warnings == null) return "null";
            return "[" + string.Join(", ", warnings.Select(warning =>
                "{ \"message_key\": " + Quote(warning.MessageKey) +
                ", \"level\": " + Quote(warning.Level) +
                ", \"element_id\": " + (warning.ElementId.HasValue
                    ? warning.ElementId.Value.ToString(CultureInfo.InvariantCulture) : "null") +
                ", \"element_type\": " + Quote(warning.ElementType) +
                ", \"scenario_id\": " + (warning.ScenarioId.HasValue
                    ? warning.ScenarioId.Value.ToString(CultureInfo.InvariantCulture) : "null") +
                ", \"message_id\": " + Quote(warning.MessageId) +
                ", \"source_key\": " + Quote(warning.SourceKey) +
                ", \"parameters\": " + (warning.Parameters == null
                    ? "null" : "[" + string.Join(", ", warning.Parameters.Select(Quote)) + "]") +
                " }")) + "]";
        }

        private static void WriteConsole(GeneralCalculationObservation calculation,
            GeneralReadbackObservation readback, GeneralHydraulicResultRow conduit,
            GeneralHydraulicResultRow structure, List<string> problems,
            GeneralAcceptance acceptance, string verdict)
        {
            Console.WriteLine("SCENARIO=" + readback.ScenarioId);
            Console.WriteLine("OPTIONS=" + readback.CalculationOptionsId);
            Console.WriteLine("ENGINE=" + readback.Engine + " (" + calculation.EngineSelectionBasis + ")");
            Console.WriteLine("EVENT=" + readback.CalculationEventId);
            Console.WriteLine("SOLVER_STATUS=" + calculation.ScenarioComputeStatus);
            Console.WriteLine("ENGINE_OUTCOME=" + (calculation.EngineOutcome ?? "(none)"));
            Console.WriteLine("WARNING_CAPTURE=" + (calculation.WarningCaptureStatus ?? "NOT_RECORDED") +
                " COUNT=" + (calculation.WarningNotificationCount.HasValue
                    ? calculation.WarningNotificationCount.Value.ToString(CultureInfo.InvariantCulture) : "null"));
            Console.WriteLine("CONDUITS=" + readback.ConduitCount + " STRUCTURES=" + readback.StructureCount);
            Console.WriteLine("ROWS=" + readback.RowCount + " AVAILABLE=" + readback.AvailableRowCount);
            if (conduit != null)
                Console.WriteLine("CONDUIT_SAMPLE=" + conduit.ElementLabel + " (id " + conduit.ElementId + ") " +
                    conduit.Metric + "=" + conduit.Value.GetValueOrDefault().ToString("R", CultureInfo.InvariantCulture) +
                    " " + conduit.Unit);
            if (structure != null)
                Console.WriteLine("STRUCTURE_SAMPLE=" + structure.ElementLabel + " (id " + structure.ElementId + ") " +
                    structure.Metric + "=" + (structure.Value.HasValue
                        ? structure.Value.Value.ToString("R", CultureInfo.InvariantCulture) : structure.RawValue) +
                    " " + structure.Unit);
            Console.WriteLine("PROVENANCE=" + readback.Provenance);
            Console.WriteLine("ACCEPTANCE=" + verdict + " REASON=" + acceptance.Reason);
            foreach (string problem in problems) Console.WriteLine("PROBLEM: " + problem);
        }
    }
}
