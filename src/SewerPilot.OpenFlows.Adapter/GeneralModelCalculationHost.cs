using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    // One genuine hydraulic calculation of an already-configured SewerGEMS scenario, for a model the
    // engineer selected. This path performs no engineering design mutation: it does not create or copy
    // scenarios, alternatives or calculation options, and it never writes to the engineer's own file.
    //
    // The scenario, calculation options and numerical engine are discovered from the model itself.
    // Nothing here is keyed to a specific project name, checksum, scenario id, option id or element id.
    // Where the model's own configuration cannot be resolved unambiguously the attempt fails closed
    // with a classification rather than guessing.
    internal static class GeneralModelCalculationHost
    {
        internal const string Operation = "GeneralModelNativeCalculation";
        internal const string LaunchSwitch = "--internal-general-model-calculation";
        internal const string EvidenceKey = "GeneralModelCalculationObservationJson";
        internal const string BlockedFailureCode = "GENERAL_MODEL_CALCULATION_BLOCKED";

        internal const string PhaseKey = "GeneralModelPhase";
        internal const string PhaseCalculate = "CALCULATE";
        internal const string PhaseRead = "READ";
        // A read-only metadata pass over a disposable copy of the model family. It exists so the
        // declared external inputs of a model can be read through the vendor API *before* the final
        // working copy is built; it performs no solve, no save and no mutation.
        internal const string PhaseDiscover = "DISCOVER";

        internal const string TerrainEvidenceKey = "GeneralModelTerrainObservationJson";
        internal const string DiscoverPassFlag = "GeneralModelTerrainDiscoveryPassed";
        internal const string DiscoverStatus = "GENERAL_TERRAIN_DISCOVERY_COMPLETE";

        // Correcting the reference of a declared external input, and independently verifying the
        // persisted result. Neither phase may solve; only BIND may save, exactly once.
        internal const string PhaseBind = "BIND_TERRAIN";
        internal const string PhaseVerify = "VERIFY_TERRAIN";
        internal const string TargetKey = "GeneralModelTerrainTarget";
        internal const string BindEvidenceKey = "GeneralModelTerrainBindObservationJson";
        internal const string VerifyEvidenceKey = "GeneralModelTerrainVerifyObservationJson";
        internal const string BindStatus = "TERRAIN_REFERENCE_REBOUND_AND_PERSISTED";
        internal const string VerifyStatus = "TERRAIN_REFERENCE_VERIFIED";
        internal const string BindPassFlag = "GeneralModelTerrainBindPassed";
        internal const string VerifyPassFlag = "GeneralModelTerrainVerifyPassed";

        internal const string CopyIdKey = "GeneralModelCopyId";
        internal const string InventoryKey = "GeneralModelInventorySHA256";
        internal const string ScenarioKey = "GeneralModelScenarioId";
        internal const string OptionsKey = "GeneralModelOptionsId";
        internal const string EngineKey = "GeneralModelEngine";
        internal const string EventKey = "GeneralModelCalculationEventId";
        internal const string RowCountKey = "GeneralModelResultRowCount";
        // What this child process's temporary environment actually resolved to. Evidence only: it
        // proves the redirected scratch location was honoured and nothing reads it back as a boundary.
        internal const string HostTemporaryRootKey = "GeneralModelHostTemporaryRoot";

        // Status values. Only the first is a completed solve; only the second is an accepted readback.
        internal const string CompletedStatus = "GENERAL_CALCULATION_COMPLETED_AWAITING_REOPEN";
        internal const string ReadVerifiedStatus = "GENERAL_NATIVE_READBACK_VERIFIED";

        internal const string PassFlag = "GeneralModelCalculationPassed";
        internal const string ReadPassFlag = "GeneralModelReadbackPassed";

        // Bounds. A model larger than this is refused rather than truncated, so a partial result set
        // can never be presented as complete.
        internal const int MaxResultRows = 20000;
        internal const int MaxConduits = 20000;
        internal const int MaxStructures = 20000;
        private const int MaxEngineCandidates = 32;
        private const int MaxTextLength = 1024;
        private const int MaxTerrainElements = 32;
        private const int MaxTerrainPropertyDump = 64;
        internal const int MaxEngineWarnings = 100;
        private const int MaxEngineWarningParameters = 12;

        // The vendor type that carries engine warnings. Matched by name because the assembly that
        // defines it is only present when the installed runtime is loaded.
        internal const string EngineWarningsExceptionType = "Haestad.Calculations.Support.EngineWarningsException";

        // Capture outcomes. UNKNOWN is a first-class result: details that could not be read are never
        // reported as a warning-free run.
        internal const string WarningCaptureCaptured = "CAPTURED";
        internal const string WarningCaptureUnknown = "UNKNOWN";
        internal const string WarningCaptureNotApplicable = "NOT_APPLICABLE";

        // Manager names are tried in order because the element type enumeration name for terrain has
        // varied across OpenFlows releases. A model with no terrain manager simply declares no
        // external terrain, which is not an error.
        private static readonly string[] TerrainManagerNames =
        {
            "DigitalTerrainModelElementManager", "DTMElementManager",
            "TerrainModelElementManager", "TerrainElementManager",
            "DigitalTerrainModelManager", "DigitalTerrainModel"
        };

        // Support-element field names carrying the declared file reference. The bare names are what
        // the support-field accessor accepts; the prefixed names are the storage column names and are
        // tried as well so a different release cannot silently read nothing.
        private static readonly string[] TerrainFieldNames =
        {
            "FilePath", "RelativeFilePath", "IsActive", "IsChecked", "FileProxy", "DTMSourceType",
            "SourceType", "ModelString", "ElevationField", "SourceUnit",
            "DigitalTerrainModel_FilePath", "DigitalTerrainModel_RelativeFilePath",
            "DigitalTerrainModel_IsActive", "DigitalTerrainModel_IsChecked",
            "DigitalTerrainModel_FileProxy"
        };

        // The verified copy of each two-dimensional data source, supplied by the caller so the
        // binding pass can prove the file it is about to name is the file that was verified. One
        // line per family: declared relative destination, then the three member fingerprints.
        internal const string DataSourceManifestKey = "GeneralModelDataSourceManifest";

        // Reference-write evidence. The count alone cannot say *which* fields were written, so the
        // allowlist, the element identities and the count are published separately and the protocol
        // checks all three rather than trusting the phase's own arithmetic.
        internal const string ReferenceFieldsKey = "GeneralModelReferenceFields";
        internal const string ReferenceElementsKey = "GeneralModelReferenceElements";

        // The only fields a reference correction may ever write. Anything else is a contract
        // violation, not a correction: no engineering value, grid setting or scenario field is here.
        internal static readonly string[] ReferenceFieldAllowlist =
        {
            "DigitalTerrainModel_FilePath",
            "DigitalTerrainModel_RelativeFilePath",
            "DigitalTerrainModel_FileProxy",
            "TwoDDataSource_FileProxy",
            "TwoDDataSource_FileRelativePath"
        };

        // A Shapefile is a family and travels as one; the model names only the geometry file.
        private const string DataSourceShapefileExtension = ".shp";
        private static readonly string[] DataSourceMemberExtensions = { ".shp", ".shx", ".dbf" };

        private const string NameScenarioManager = "Scenario" + "Manager";

        internal static string WorkingRoot => P16AzadeganCalculationHost.WorkingRoot;

        // ------------------------------------------------------------------ request validation

        // The request may only ever target a controlled per-run working copy. The engineer's own
        // model is never a legal target, and no external write authority is accepted.
        internal static void ValidateRequest(HostRequest request)
        {
            if (request == null || request.Operation != Operation)
                throw new InvalidDataException("General model calculation operation mismatch.");
            if (request.EngineeringPlan != null || request.WriteAuthorizationTokenId != null ||
                request.WriteAuthorizationScope != null || request.WriteAuthorizationTargetWorkingPath != null)
                throw new InvalidDataException("General model calculation permits no engineering plan or external write authority.");
            if (request.Diagnostics == null)
                throw new InvalidDataException("General model calculation diagnostics are absent.");

            string copyId;
            if (!request.Diagnostics.TryGetValue(CopyIdKey, out copyId) ||
                !System.Text.RegularExpressions.Regex.IsMatch(copyId ?? "", "^[a-f0-9]{32}$") ||
                !string.Equals(request.WorkingCopyId, copyId, StringComparison.Ordinal))
                throw new InvalidDataException("General model calculation copy identity is not a controlled per-run identifier.");

            string model = Path.GetFullPath(request.WorkingModelPath ?? "");
            if (!model.EndsWith(".stsw", StringComparison.OrdinalIgnoreCase) || !File.Exists(model))
                throw new InvalidDataException("General model calculation requires an existing .stsw working copy.");
            string phase;
            if (!request.Diagnostics.TryGetValue(PhaseKey, out phase) ||
                (phase != PhaseCalculate && phase != PhaseRead && phase != PhaseDiscover &&
                 phase != PhaseBind && phase != PhaseVerify))
                throw new InvalidDataException("General model calculation requires an explicit CALCULATE, READ, DISCOVER, BIND_TERRAIN or VERIFY_TERRAIN phase.");
            string directory = Path.GetDirectoryName(model);
            // Discovery never persists, so it runs against a read-only category copy; the solving and
            // readback phases stay confined to the working category they have always used.
            // The host process runs with a redirected temporary environment, so the boundary must be
            // carried explicitly and is re-measured against the application anchor, never the scratch.
            if (string.IsNullOrWhiteSpace(request.TrustedTempRoot))
                throw new InvalidDataException("General model calculation requires an explicitly established trusted working root.");
            RuntimeHostProtocol.ControlledDirectory(directory,
                phase == PhaseDiscover ? "ReadOnly" : "Working", request.TrustedTempRoot);
            WorkingCopyIntegrity.RejectLinks(directory);
            WorkingCopyIntegrity.RejectLinks(model);
            // The target must be the model file of this copy's own controlled directory, never a
            // nested path and never a sibling of the run root.
            if (!string.Equals(Path.GetFileName(directory), copyId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(model), directory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("General model calculation must target its own disposable working copy only.");

            string digest;
            if (!request.Diagnostics.TryGetValue(InventoryKey, out digest) ||
                !System.Text.RegularExpressions.Regex.IsMatch(digest ?? "", "^[A-F0-9]{64}$"))
                throw new InvalidDataException("General model calculation input digest is malformed.");

            // Discovery only reads declared metadata: it names no scenario, engine or event because it
            // never solves and never reads results.
            if (phase == PhaseDiscover) return;
            // Rebinding and its verification name the verified terrain they act on, and that file must
            // already be inside this copy. Neither phase names a scenario or an engine.
            if (phase == PhaseBind || phase == PhaseVerify)
            {
                RequireTerrainTarget(request.Diagnostics, directory);
                return;
            }
            if (phase != PhaseRead) return;

            // The READ phase must name the exact solve it is reading back, so a result set can never be
            // attributed to a different run.
            RequireInteger(request.Diagnostics, ScenarioKey, 1);
            RequireInteger(request.Diagnostics, OptionsKey, 1);
            string engine;
            if (!request.Diagnostics.TryGetValue(EngineKey, out engine) || string.IsNullOrWhiteSpace(engine) ||
                engine.Length > 128)
                throw new InvalidDataException("General model readback engine identity is absent or malformed.");
            string eventId;
            if (!request.Diagnostics.TryGetValue(EventKey, out eventId) ||
                !System.Text.RegularExpressions.Regex.IsMatch(eventId ?? "", "^[a-f0-9]{32}$"))
                throw new InvalidDataException("General model readback calculation event is absent or malformed.");
        }

        // A rebinding target must be a real file inside the disposable copy. An external source path,
        // the engineer's own model directory or the installed lesson directory is never a legal target.
        private static string RequireTerrainTarget(Dictionary<string, string> diagnostics, string workingDirectory)
        {
            string target;
            if (!diagnostics.TryGetValue(TargetKey, out target) || string.IsNullOrWhiteSpace(target))
                throw new InvalidDataException(
                    "A terrain rebinding request must name the verified terrain inside its own working copy.");
            string full;
            try { full = Path.GetFullPath(target); }
            catch (Exception error) { throw new InvalidDataException("The terrain rebinding target path is malformed.", error); }
            string root = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A terrain rebinding target must be inside the disposable working copy.");
            if (!File.Exists(full))
                throw new InvalidDataException("The terrain rebinding target does not exist inside the disposable working copy.");
            return full;
        }

        private static int RequireInteger(Dictionary<string, string> diagnostics, string key, int minimum)
        {
            string text;
            int value;
            if (!diagnostics.TryGetValue(key, out text) ||
                !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < minimum)
                throw new InvalidDataException("General model readback context is absent or malformed: " + key);
            return value;
        }

        // ------------------------------------------------------------------ discovery phase

        // Reads the model's own declared external inputs. Nothing is solved, saved or mutated: the
        // purpose is to tell the caller which files the project expects beside it, so the final
        // working copy can be built complete before any calculation is launched.
        internal static void ExecuteDiscover(object dataset, object project, HostRequest request,
            ModelReadEvidence evidence)
        {
            var seen = new GeneralTerrainObservation
            {
                OpenedModelPath = request.WorkingModelPath,
                WorkingCopyId = request.WorkingCopyId
            };
            try
            {
                // Read-only preflight of the grid surface, so a missing vendor contract or an
                // unresolvable active grid is known before any calculation budget is spent.
                GridPreflight(dataset, project, evidence);

                // Two-dimensional data sources are reported here, before the terrain branch can
                // return, because a model can declare either, both or neither.
                seen.DataSources = DataSourceDependencies(dataset, out bool dataSourceManagerResolved);
                seen.DataSourceManagerResolved = dataSourceManagerResolved;

                // The element-manager enumeration is the authoritative list of what this model can
                // hold. Recording the terrain-related members makes an unresolved manager name name
                // its own cause instead of silently reporting "no terrain".
                seen.AvailableManagerNames = ManagerNames("Terrain", "DTM", "Surface", "Grid");
                object manager = TerrainManager(dataset);
                if (manager == null)
                {
                    seen.TerrainManagerResolved = false;
                    seen.TerrainElementCount = 0;
                    seen.Dependencies = new GeneralTerrainDependency[0];
                    seen.Status = DiscoverStatus;
                    return;
                }
                seen.TerrainManagerResolved = true;
                seen.TerrainManagerName = Bound(manager.GetType().Name);
                List<int> ids = ElementIdsOf(manager);
                seen.TerrainElementCount = ids.Count;
                var dependencies = new List<GeneralTerrainDependency>();
                var dump = new List<string>();
                foreach (int id in ids.Take(MaxTerrainElements))
                {
                    object element = ElementById(manager, id);
                    if (element == null) continue;
                    dependencies.Add(TerrainDependency(manager, element, id));
                    // One element's full readable surface is enough to show what the vendor exposes
                    // for resolution; it is recorded so a later failure names its own cause.
                    if (dump.Count == 0)
                    {
                        dump.AddRange(DescribeProperties(element));
                        dump.AddRange(SupportFieldDump(manager, id));
                    }
                }
                seen.Dependencies = dependencies.ToArray();
                seen.ElementPropertyDump = dump.ToArray();
                seen.Status = DiscoverStatus;
            }
            catch (Exception error)
            {
                var exact = Unwrap(error);
                seen.Status = "TERRAIN_DISCOVERY_FAILED";
                seen.FailureMessage = "The model's declared external inputs could not be read: " + Bound(exact.Message);
                seen.ErrorType = exact.GetType().FullName;
                seen.ErrorMessage = Bound(exact.Message);
                if (evidence.FailureCode == null) evidence.FailureCode = BlockedFailureCode;
            }
            finally
            {
                evidence.Diagnostics[TerrainEvidenceKey] = RuntimeHostProtocol.Serializer().Serialize(seen);
                evidence.Diagnostics["GeneralModelTerrainStatus"] = seen.Status;
                evidence.Diagnostics[DiscoverPassFlag] =
                    string.Equals(seen.Status, DiscoverStatus, StringComparison.Ordinal) ? "true" : "false";
                if (!string.Equals(seen.Status, DiscoverStatus, StringComparison.Ordinal) && evidence.FailureCode == null)
                    evidence.FailureCode = BlockedFailureCode;
            }
        }

        // ------------------------------------------------------------------ reference rebinding

        // Corrects the declared reference of an active external terrain input so it points at the
        // verified copy inside this working copy, then persists once. No solver is invoked and no
        // engineering value is touched: only the file-reference fields are written.
        internal static void ExecuteBind(object dataset, object project, HostRequest request,
            ModelReadEvidence evidence)
        {
            var seen = new GeneralTerrainBindObservation
            {
                OpenedModelPath = request.WorkingModelPath,
                WorkingCopyId = request.WorkingCopyId,
                FieldDiagnostics = new string[0]
            };
            var fields = new List<string>();
            var writtenFields = new List<string>();
            var writeCounts = new List<KeyValuePair<string, int>>();
            var dataSources = new List<GeneralDataSourceBinding>();
            int dataSourceElementCount = 0;
            try
            {
                string root = Path.GetFullPath(Path.GetDirectoryName(request.WorkingModelPath))
                    .TrimEnd(Path.DirectorySeparatorChar);
                string target = RequireTerrainTarget(request.Diagnostics, root);
                seen.TargetPath = target;
                seen.TargetRelativePath = RelativeInside(root, target);

                object manager = TerrainManager(dataset);
                if (manager == null)
                {
                    seen.Status = "TERRAIN_MANAGER_ABSENT";
                    seen.FailureMessage = "The model exposes no terrain manager, so there is no reference to correct.";
                    return;
                }
                List<int> active = ActiveTerrainElementIds(manager);
                seen.TerrainElementCount = active.Count;
                if (active.Count == 0)
                {
                    seen.Status = "TERRAIN_ELEMENT_ABSENT";
                    seen.FailureMessage = "The model declares no active terrain element, so there is no reference to correct.";
                    return;
                }
                if (active.Count > 1)
                {
                    seen.Status = "TERRAIN_ELEMENT_AMBIGUOUS";
                    seen.FailureMessage = "The model declares " + active.Count + " active terrain elements; " +
                        "a single unambiguous reference cannot be established.";
                    return;
                }
                int elementId = active[0];
                seen.ElementId = elementId;
                seen.Before = TerrainDependency(manager, ElementById(manager, elementId), elementId);

                seen.FilePathWritten = WriteSupportField(manager, elementId, "DigitalTerrainModel_FilePath",
                    target, fields);
                if (seen.FilePathWritten)
                {
                    RecordWrite(writtenFields, writeCounts, "Terrain:" + elementId, "DigitalTerrainModel_FilePath");
                }
                // A relative reference is only written when the target really is expressible relative
                // to the copy. Writing null would be a guess, not a correction.
                if (!string.IsNullOrWhiteSpace(seen.TargetRelativePath))
                    seen.RelativePathWritten = WriteSupportField(manager, elementId,
                        "DigitalTerrainModel_RelativeFilePath", seen.TargetRelativePath, fields);
                if (seen.RelativePathWritten)
                {
                    RecordWrite(writtenFields, writeCounts, "Terrain:" + elementId,
                        "DigitalTerrainModel_RelativeFilePath");
                }
                if (!seen.FilePathWritten)
                {
                    seen.FieldDiagnostics = fields.ToArray();
                    seen.Status = "REBIND_SETTER_UNAVAILABLE";
                    seen.FailureMessage = "The installed build exposes no supported setter for the terrain file " +
                        "reference, so nothing was changed and nothing was saved.";
                    return;
                }

                // The stored document-specification proxy is a separate column on this vendor build: it
                // does not follow the file path across a save and reopen, so when it still names a
                // location outside the copy it is an effective independent reference. It is corrected
                // through the vendor's own field setter, keeping the vendor's serialised form intact and
                // replacing only the path token. Nothing is reconstructed and no storage bytes are edited.
                object proxyValue = SupportFieldValue(SupportField(manager, "DigitalTerrainModel_FileProxy"), elementId);
                seen.ProxyTypeName = proxyValue == null ? null : Bound(proxyValue.GetType().FullName);
                seen.ProxyPathBefore = ProxyPath(Bound(Convert.ToString(proxyValue, CultureInfo.InvariantCulture)));
                seen.ProxyIndependent = RequiresExternalPath(ResolveAgainst(root, seen.ProxyPathBefore), root);
                if (seen.ProxyIndependent)
                {
                    var notes = new List<string>();
                    object correctedProxy;
                    if (!TryRebindProxyPath(proxyValue, seen.ProxyPathBefore, target, notes, out correctedProxy))
                    {
                        seen.ProxyDiagnostics = notes.ToArray();
                        seen.FieldDiagnostics = fields.ToArray();
                        seen.Status = "REBIND_PROXY_UNSUPPORTED";
                        seen.FailureMessage = "The stored document-specification proxy still names an external " +
                            "location and exposes no supported way to correct it, so nothing was saved.";
                        return;
                    }
                    seen.ProxyFieldWritten = WriteSupportFieldValue(manager, elementId,
                        "DigitalTerrainModel_FileProxy", correctedProxy, fields);
                    seen.ProxyDiagnostics = notes.ToArray();
                    if (seen.ProxyFieldWritten)
                    {
                        RecordWrite(writtenFields, writeCounts, "Terrain:" + elementId,
                            "DigitalTerrainModel_FileProxy");
                    }
                    if (!seen.ProxyFieldWritten)
                    {
                        seen.FieldDiagnostics = fields.ToArray();
                        seen.Status = "REBIND_PROXY_NOT_ACCEPTED";
                        seen.FailureMessage = "The corrected document-specification proxy was not accepted by " +
                            "the vendor, so nothing was saved.";
                        return;
                    }
                }
                seen.FieldDiagnostics = fields.ToArray();

                // The two-dimensional data sources carry their own document-specification proxy, and
                // this vendor build stores it independently of the relative path exactly as it does for
                // the terrain. Every record that still names the authoring machine is corrected to the
                // copy the model was just given, through the same field setter and the same path-token
                // replacement. Element identities are discovered here and never assumed.
                List<DataSourceManifestEntry> manifest = DataSourceManifest(request.Diagnostics);
                object dataManager = SupportElementManager(dataset, "TwoDDataSource");
                if (dataManager != null)
                {
                    foreach (int dataId in ElementIdsOf(dataManager).Take(MaxTerrainElements))
                    {
                        dataSourceElementCount++;
                        var item = new GeneralDataSourceBinding { ElementId = dataId };
                        dataSources.Add(item);
                        item.DeclaredRelativePath = SupportText(dataManager, dataId, "RelativeFilePath",
                            "TwoDDataSource_FileRelativePath");
                        object dataProxy = SupportFieldValue(SupportField(dataManager, "TwoDDataSource_FileProxy"),
                            dataId);
                        item.ProxyPathBefore = ProxyPath(Bound(Convert.ToString(dataProxy, CultureInfo.InvariantCulture)));
                        DataSourceResolution resolution = ResolveDataSourceTarget(root, item.DeclaredRelativePath,
                            item.ProxyPathBefore, manifest);
                        if (resolution == null)
                        {
                            seen.DataSources = dataSources.ToArray();
                            seen.Status = "REBIND_DATA_SOURCE_UNRESOLVED";
                            seen.FailureMessage = "A two-dimensional data source could not be matched to a verified " +
                                "copy inside the working copy (element " + dataId + "), so nothing was saved.";
                            return;
                        }
                        string dataTarget = resolution.Path;
                        item.SHA256 = resolution.SHA256;
                        item.TargetExists = true;
                        item.FamilyVerified = resolution.FamilyVerified;
                        // A relative reference that already resolves inside the copy is left exactly as
                        // the vendor stored it. Only a reference that does not resolve is corrected.
                        if (!ResolvesTo(root, item.DeclaredRelativePath, dataTarget))
                        {
                            string correctedRelative = RelativeInside(root, dataTarget);
                            item.RelativePathWritten = !string.IsNullOrWhiteSpace(correctedRelative) &&
                                WriteSupportFieldValue(dataManager, dataId, "TwoDDataSource_FileRelativePath",
                                    correctedRelative, fields);
                            if (item.RelativePathWritten)
                            {
                                RecordWrite(writtenFields, writeCounts, "TwoDDataSource:" + dataId,
                                    "TwoDDataSource_FileRelativePath");
                            }
                            if (!item.RelativePathWritten)
                            {
                                seen.DataSources = dataSources.ToArray();
                                seen.Status = "REBIND_DATA_SOURCE_RELATIVE_UNRESOLVED";
                                seen.FailureMessage = "A two-dimensional data source does not resolve inside the " +
                                    "working copy and its relative reference could not be corrected (element " +
                                    dataId + "), so nothing was saved.";
                                return;
                            }
                        }
                        item.ProxyIndependent = RequiresExternalPath(ResolveAgainst(root, item.ProxyPathBefore), root);
                        if (item.ProxyIndependent)
                        {
                            var notes = new List<string>();
                            object correctedDataProxy;
                            if (!TryRebindProxyPath(dataProxy, item.ProxyPathBefore, dataTarget, notes,
                                    out correctedDataProxy))
                            {
                                item.Diagnostics = notes.ToArray();
                                seen.DataSources = dataSources.ToArray();
                                seen.Status = "REBIND_DATA_SOURCE_PROXY_UNSUPPORTED";
                                seen.FailureMessage = "A two-dimensional data source still names an external " +
                                    "location and exposes no supported way to correct it (element " + dataId +
                                    "), so nothing was saved.";
                                return;
                            }
                            item.ProxyWritten = WriteSupportFieldValue(dataManager, dataId,
                                "TwoDDataSource_FileProxy", correctedDataProxy, fields);
                            item.Diagnostics = notes.ToArray();
                            if (item.ProxyWritten)
                            {
                                RecordWrite(writtenFields, writeCounts, "TwoDDataSource:" + dataId,
                                    "TwoDDataSource_FileProxy");
                            }
                            if (!item.ProxyWritten)
                            {
                                seen.DataSources = dataSources.ToArray();
                                seen.Status = "REBIND_DATA_SOURCE_PROXY_NOT_ACCEPTED";
                                seen.FailureMessage = "The corrected two-dimensional data-source proxy was not " +
                                    "accepted by the vendor (element " + dataId + "), so nothing was saved.";
                                return;
                            }
                        }
                        // Read back in memory. An external reference that survived the write would be
                        // persisted stale, so the save is refused rather than producing a half-corrected model.
                        object reread = SupportFieldValue(SupportField(dataManager, "TwoDDataSource_FileProxy"), dataId);
                        item.ProxyPathAfter = ProxyPath(Bound(Convert.ToString(reread, CultureInfo.InvariantCulture)));
                        item.TargetPath = dataTarget;
                        item.TargetRelativePath = RelativeInside(root, dataTarget);
                        if (RequiresExternalPath(ResolveAgainst(root, item.ProxyPathAfter), root))
                        {
                            seen.DataSources = dataSources.ToArray();
                            seen.Status = "REBIND_DATA_SOURCE_PROXY_NOT_ACCEPTED";
                            seen.FailureMessage = "A two-dimensional data-source proxy still names an external " +
                                "location after correction (element " + dataId + "), so nothing was saved.";
                            return;
                        }
                    }
                }
                else if (manifest.Count > 0)
                {
                    // The caller verified data sources this model does not expose: the copy and the model
                    // disagree, so no correction can honestly be claimed.
                    seen.Status = "REBIND_DATA_SOURCE_MANAGER_ABSENT";
                    seen.FailureMessage = "Verified copies name two-dimensional data sources but the model " +
                        "exposes no such manager, so nothing was saved.";
                    return;
                }
                seen.DataSourceElementCount = dataSourceElementCount;
                seen.DataSources = dataSources.ToArray();
                seen.DataSourceReferencesCorrected = dataSources.All(item =>
                    !RequiresExternalPath(ResolveAgainst(root, item.ProxyPathAfter), root));

                // The vendor must retain the value we just wrote before it is persisted; a setter that
                // silently ignores the value is not a correction.
                seen.After = TerrainDependency(manager, ElementById(manager, elementId), elementId);
                if (!SamePath(seen.After.DeclaredAbsolutePath, target))
                {
                    seen.Status = "REBIND_NOT_ACCEPTED";
                    seen.FailureMessage = "The vendor did not retain the corrected terrain reference in memory.";
                    return;
                }
                // An external proxy that survived the in-memory correction would be persisted stale, so
                // the save is refused rather than producing a half-corrected model.
                seen.ProxyPathAfter = ProxyPath(seen.After.FileProxy);
                if (RequiresExternalPath(ResolveAgainst(root, seen.ProxyPathAfter), root))
                {
                    seen.Status = "REBIND_PROXY_NOT_ACCEPTED";
                    seen.FailureMessage = "The stored document-specification proxy still names an external " +
                        "location after correction, so nothing was saved.";
                    return;
                }

                var before = P16AzadeganCalculationHost.Inventory(root);
                var carrier = new GeneralCalculationObservation
                {
                    OpenedModelPath = request.WorkingModelPath,
                    WorkingCopyId = request.WorkingCopyId
                };
                if (!PersistWorkingCopy(project, request, evidence, carrier, before))
                {
                    seen.Status = "REBIND_SAVE_FAILED";
                    seen.SaveSucceeded = false;
                    seen.SaveResultClassification = carrier.SaveResultClassification;
                    seen.SaveQuestionCount = carrier.SaveQuestionCount;
                    seen.FailureOperation = carrier.FailureOperation;
                    seen.ErrorType = carrier.ErrorType;
                    seen.ErrorMessage = carrier.ErrorMessage;
                    seen.FailureMessage = "The corrected reference could not be persisted on the disposable copy.";
                    return;
                }
                seen.SaveSucceeded = carrier.SaveSucceeded;
                seen.SaveResultClassification = carrier.SaveResultClassification;
                seen.SaveQuestionCount = carrier.SaveQuestionCount;
                seen.SaveTimestamp = carrier.SaveTimestamp;
                seen.CreatedFiles = carrier.CreatedFiles;
                seen.ModifiedFiles = carrier.ModifiedFiles;
                seen.Status = BindStatus;
            }
            catch (Exception error)
            {
                var exact = Unwrap(error);
                seen.Status = "REBIND_FAILED";
                seen.FailureMessage = "The terrain reference could not be corrected: " + Bound(exact.Message);
                seen.FailureOperation = exact.TargetSite == null ? null : exact.TargetSite.Name;
                seen.ErrorType = exact.GetType().FullName;
                seen.ErrorMessage = Bound(exact.Message);
            }
            finally
            {
                evidence.Diagnostics[BindEvidenceKey] = RuntimeHostProtocol.Serializer().Serialize(seen);
                evidence.Diagnostics["GeneralModelTerrainBindStatus"] = seen.Status;
                // Only the reference fields may be written on this path. The count, the field allowlist
                // and the element identities are published separately so the protocol can bound them
                // instead of trusting the phase's own claim.
                evidence.Diagnostics["GeneralModelTerrainBindWrites"] = writeCounts
                    .Sum(item => item.Value).ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics[ReferenceFieldsKey] = string.Join(";", writtenFields);
                evidence.Diagnostics[ReferenceElementsKey] = string.Join(";", writeCounts.Select(item =>
                    item.Key + ":" + item.Value.ToString(CultureInfo.InvariantCulture)));
                evidence.Diagnostics["GeneralModelDataSourceElements"] =
                    dataSourceElementCount.ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics[BindPassFlag] =
                    string.Equals(seen.Status, BindStatus, StringComparison.Ordinal) ? "true" : "false";
                if (!string.Equals(seen.Status, BindStatus, StringComparison.Ordinal) && evidence.FailureCode == null)
                    evidence.FailureCode = BlockedFailureCode;
            }
        }

        // Reopens the persisted copy in a separate process and reports what the vendor now holds. It
        // writes nothing and saves nothing: only a reopened model can prove the correction survived.
        internal static void ExecuteVerify(object dataset, object project, HostRequest request,
            ModelReadEvidence evidence)
        {
            var seen = new GeneralTerrainVerifyObservation
            {
                OpenedModelPath = request.WorkingModelPath,
                WorkingCopyId = request.WorkingCopyId
            };
            try
            {
                string root = Path.GetFullPath(Path.GetDirectoryName(request.WorkingModelPath))
                    .TrimEnd(Path.DirectorySeparatorChar);
                string target = RequireTerrainTarget(request.Diagnostics, root);
                seen.TargetPath = target;
                seen.TargetRelativePath = RelativeInside(root, target);

                object manager = TerrainManager(dataset);
                if (manager == null)
                {
                    seen.Status = "TERRAIN_MANAGER_ABSENT";
                    seen.FailureMessage = "The reopened model exposes no terrain manager.";
                    return;
                }
                List<int> active = ActiveTerrainElementIds(manager);
                seen.TerrainElementCount = active.Count;
                if (active.Count != 1)
                {
                    seen.Status = "TERRAIN_ELEMENT_COUNT_UNEXPECTED";
                    seen.FailureMessage = "The reopened model declares " + active.Count +
                        " active terrain elements; exactly one was expected.";
                    return;
                }
                int elementId = active[0];
                seen.ElementId = elementId;
                seen.Reference = TerrainDependency(manager, ElementById(manager, elementId), elementId);
                seen.ProxyPath = ProxyPath(seen.Reference.FileProxy);
                seen.TargetExists = File.Exists(target);
                seen.TargetSHA256 = seen.TargetExists ? WorkingCopyIntegrity.Fingerprint(target).SHA256 : null;
                seen.AbsolutePathMatchesTarget = SamePath(seen.Reference.DeclaredAbsolutePath, target);
                seen.RelativePathResolves = ResolvesTo(root, seen.Reference.DeclaredRelativePath, target);
                // A proxy path may itself be relative; it is only "external" when it resolves outside
                // the copy once anchored at the copy root.
                seen.AuthoringPathStillRequired =
                    RequiresExternalPath(ResolveAgainst(root, seen.Reference.DeclaredAbsolutePath), root) ||
                    RequiresExternalPath(ResolveAgainst(root, seen.ProxyPath), root);

                // The same reopen proves the two-dimensional data sources. Their proxies were corrected
                // on the binding pass, and only a fresh process can show the correction was persisted.
                List<DataSourceManifestEntry> manifest = DataSourceManifest(request.Diagnostics);
                var dataSources = new List<GeneralDataSourceVerification>();
                object dataManager = SupportElementManager(dataset, "TwoDDataSource");
                if (dataManager != null)
                {
                    foreach (int dataId in ElementIdsOf(dataManager).Take(MaxTerrainElements))
                    {
                        var item = new GeneralDataSourceVerification { ElementId = dataId };
                        dataSources.Add(item);
                        item.DeclaredRelativePath = SupportText(dataManager, dataId, "RelativeFilePath",
                            "TwoDDataSource_FileRelativePath");
                        object dataProxy = SupportFieldValue(SupportField(dataManager, "TwoDDataSource_FileProxy"),
                            dataId);
                        item.ProxyPath = ProxyPath(Bound(Convert.ToString(dataProxy, CultureInfo.InvariantCulture)));
                        DataSourceResolution resolution = ResolveDataSourceTarget(root, item.DeclaredRelativePath,
                            item.ProxyPath, manifest);
                        string dataTarget = resolution == null ? null : resolution.Path;
                        if (resolution != null)
                        {
                            item.SHA256 = resolution.SHA256;
                            item.FamilyVerified = resolution.FamilyVerified;
                        }
                        item.TargetPath = dataTarget;
                        item.TargetRelativePath = dataTarget == null ? null : RelativeInside(root, dataTarget);
                        item.TargetExists = dataTarget != null && File.Exists(dataTarget);
                        item.ProxyInsideCopy = !RequiresExternalPath(ResolveAgainst(root, item.ProxyPath), root);
                        item.RelativePathResolves = dataTarget != null &&
                            ResolvesTo(root, item.DeclaredRelativePath, dataTarget);
                        item.ExternalPathRequired =
                            !item.ProxyInsideCopy ||
                            RequiresExternalPath(ResolveAgainst(root, item.DeclaredRelativePath), root);
                    }
                }
                seen.DataSources = dataSources.ToArray();
                seen.DataSourceReferencesVerified = manifest.Count > 0
                    ? dataSources.Count > 0 && dataSources.All(item =>
                        item.TargetExists && item.FamilyVerified && item.ProxyInsideCopy &&
                        item.RelativePathResolves && !item.ExternalPathRequired)
                    : dataSources.Count == 0 || dataSources.All(item => !item.ExternalPathRequired);

                seen.Status = seen.AbsolutePathMatchesTarget && seen.RelativePathResolves &&
                              seen.TargetExists && !seen.AuthoringPathStillRequired &&
                              seen.DataSourceReferencesVerified
                    ? VerifyStatus
                    : "TERRAIN_REFERENCE_NOT_VERIFIED";
                if (!string.Equals(seen.Status, VerifyStatus, StringComparison.Ordinal))
                    seen.FailureMessage = seen.DataSourceReferencesVerified
                        ? "The reopened model does not hold the corrected terrain reference."
                        : "The reopened model does not hold corrected, self-contained two-dimensional " +
                          "data-source references.";
            }
            catch (Exception error)
            {
                var exact = Unwrap(error);
                seen.Status = "TERRAIN_VERIFY_FAILED";
                seen.FailureMessage = "The persisted terrain reference could not be verified: " + Bound(exact.Message);
                seen.ErrorType = exact.GetType().FullName;
                seen.ErrorMessage = Bound(exact.Message);
            }
            finally
            {
                evidence.Diagnostics[VerifyEvidenceKey] = RuntimeHostProtocol.Serializer().Serialize(seen);
                evidence.Diagnostics["GeneralModelTerrainVerifyStatus"] = seen.Status;
                evidence.Diagnostics[VerifyPassFlag] =
                    string.Equals(seen.Status, VerifyStatus, StringComparison.Ordinal) ? "true" : "false";
                if (!string.Equals(seen.Status, VerifyStatus, StringComparison.Ordinal) && evidence.FailureCode == null)
                    evidence.FailureCode = BlockedFailureCode;
            }
        }

        private static List<int> ActiveTerrainElementIds(object manager)
        {
            var active = new List<int>();
            foreach (int id in ElementIdsOf(manager).Take(MaxTerrainElements))
            {
                object element = ElementById(manager, id);
                if (element == null) continue;
                if (TerrainDependency(manager, element, id).IsActive == false) continue;
                active.Add(id);
            }
            return active;
        }

        // Writes one support-element field through the vendor's own setter. A field without a setter,
        // or a setter that refuses the value, is reported rather than worked around.
        private static bool WriteSupportField(object manager, int elementId, string name, string value,
            List<string> diagnostics) => WriteSupportFieldValue(manager, elementId, name, value, diagnostics);

        // The same writer for a typed vendor object, so a document-specification proxy can be pushed
        // back through the field it came from without being reconstructed as text.
        private static bool WriteSupportFieldValue(object manager, int elementId, string name, object value,
            List<string> diagnostics)
        {
            try
            {
                object field = SupportField(manager, name);
                if (field == null) { diagnostics.Add(name + "=<absent>"); return false; }
                MethodInfo setter = null;
                foreach (Type contract in WaterObjectsLifecycleHost.Hierarchy(field.GetType()))
                {
                    setter = contract.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(method => method.Name == "SetValue" && !method.ContainsGenericParameters &&
                            method.GetParameters().Length == 2 &&
                            method.GetParameters()[0].ParameterType == typeof(int));
                    if (setter != null) break;
                }
                if (setter == null) { diagnostics.Add(name + "=<no supported setter>"); return false; }
                setter.Invoke(field, new object[] { elementId, value });
                diagnostics.Add(name + "=SET");
                return true;
            }
            catch (Exception error)
            {
                diagnostics.Add(name + "=<" + Unwrap(error).GetType().Name + ">");
                return false;
            }
        }

        // Rebinds the path held inside the vendor's own document-specification proxy. On this build
        // the proxy is the vendor's serialised form, so only the path token inside it is replaced and
        // the surrounding form is preserved byte for byte; no format is invented. When the proxy is a
        // typed object instead, only a settable string path property holding the stale reference is
        // written, so an unrelated property can never be chosen.
        private static bool TryRebindProxyPath(object proxy, string stalePath, string target, List<string> notes,
            out object corrected)
        {
            corrected = proxy;
            if (proxy == null) { notes.Add("proxy=<null>"); return false; }
            string text = proxy as string;
            if (text != null)
            {
                if (string.IsNullOrWhiteSpace(stalePath)) { notes.Add("proxy=<no readable path token>"); return false; }
                // The stored form escapes '&' as "&amp;", so the token is matched in both spellings.
                foreach (string token in new[] { stalePath, stalePath.Replace("&", "&amp;") })
                {
                    int index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                    if (index < 0) continue;
                    corrected = text.Substring(0, index) + target + text.Substring(index + token.Length);
                    notes.Add("proxy.text=SET");
                    return true;
                }
                notes.Add("proxy=<path token not present in stored form>");
                return false;
            }
            if (TrySetPathProperty(proxy, stalePath, target, notes)) return true;
            foreach (PropertyInfo property in SafeProperties(proxy))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                object inner;
                try { inner = property.GetValue(proxy, null); }
                catch (Exception) { continue; }
                if (inner == null || inner is string || inner.GetType().IsPrimitive) continue;
                if (TrySetPathProperty(inner, stalePath, target, notes)) return true;
            }
            notes.Add("proxy=<no supported settable path property>");
            return false;
        }

        private static bool TrySetPathProperty(object holder, string stalePath, string target, List<string> notes)
        {
            foreach (PropertyInfo property in SafeProperties(holder))
            {
                if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0) continue;
                if (property.PropertyType != typeof(string)) continue;
                if (!property.Name.EndsWith("Path", StringComparison.OrdinalIgnoreCase)) continue;
                string current;
                try { current = property.GetValue(holder, null) as string; }
                catch (Exception) { continue; }
                if (!SamePath(current, stalePath)) continue;
                try
                {
                    AdapterWriteBinding.SetPropertyValue(property, holder, target);
                    notes.Add("proxy." + property.Name + "=SET");
                    return true;
                }
                catch (Exception error)
                {
                    notes.Add("proxy." + property.Name + "=<" + Unwrap(error).GetType().Name + ">");
                }
            }
            return false;
        }

        private static PropertyInfo[] SafeProperties(object instance)
        {
            try { return instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance); }
            catch (Exception) { return new PropertyInfo[0]; }
        }

        // ------------------------------------------------------------------ data-source references

        // Records one successful reference write against its element, so the count is derived from the
        // writes that actually happened rather than from a claim made afterwards.
        private static void RecordWrite(List<string> fields, List<KeyValuePair<string, int>> counts,
            string element, string field)
        {
            if (!fields.Contains(field)) fields.Add(field);
            for (int index = 0; index < counts.Count; index++)
            {
                if (!string.Equals(counts[index].Key, element, StringComparison.Ordinal)) continue;
                counts[index] = new KeyValuePair<string, int>(element, counts[index].Value + 1);
                return;
            }
            counts.Add(new KeyValuePair<string, int>(element, 1));
        }

        // Parses the verified-copy manifest the caller supplies. One line per family: the declared
        // relative destination followed by the three member fingerprints.
        private static List<DataSourceManifestEntry> DataSourceManifest(Dictionary<string, string> diagnostics)
        {
            var entries = new List<DataSourceManifestEntry>();
            string text;
            if (diagnostics == null || !diagnostics.TryGetValue(DataSourceManifestKey, out text) ||
                string.IsNullOrWhiteSpace(text)) return entries;
            foreach (string line in text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Trim().Split('|');
                if (parts.Length != 4 || string.IsNullOrWhiteSpace(parts[0])) continue;
                entries.Add(new DataSourceManifestEntry
                {
                    RelativePath = parts[0].Trim(),
                    SHA256 = new[] { parts[1].Trim(), parts[2].Trim(), parts[3].Trim() }
                });
            }
            return entries;
        }

        // The manifest entry describing a family, matched on the declared relative location first and
        // on the bare file name second, so a differently-spelled directory separator still matches.
        private static DataSourceManifestEntry ManifestFor(List<DataSourceManifestEntry> manifest,
            string relative, string fileName)
        {
            string normalized = NormalizeReference(relative);
            foreach (DataSourceManifestEntry entry in manifest)
            {
                if (normalized != null && string.Equals(NormalizeReference(entry.RelativePath), normalized,
                        StringComparison.OrdinalIgnoreCase)) return entry;
                if (fileName != null && string.Equals(Path.GetFileName(entry.RelativePath), fileName,
                        StringComparison.OrdinalIgnoreCase)) return entry;
            }
            return null;
        }

        private static string NormalizeReference(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string text = path.Trim().Replace('/', '\\');
            while (text.StartsWith(".\\", StringComparison.Ordinal)) text = text.Substring(2);
            return text;
        }

        private static void AddDataSourceCandidate(List<string> candidates, string root, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string full;
            try
            {
                full = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));
            }
            catch (Exception) { return; }
            if (!string.Equals(Path.GetExtension(full), DataSourceShapefileExtension, StringComparison.OrdinalIgnoreCase))
                return;
            if (!File.Exists(full)) return;
            if (candidates.Any(item => string.Equals(item, full, StringComparison.OrdinalIgnoreCase))) return;
            candidates.Add(full);
        }

        // Matches one data source to the verified copy inside the working copy and proves the whole
        // family is present with the bytes that were verified. Returns null when that cannot be shown,
        // so the caller can refuse the save rather than bind to a file it has not checked.
        private static DataSourceResolution ResolveDataSourceTarget(string root, string declaredRelative,
            string proxyPath, List<DataSourceManifestEntry> manifest)
        {
            var candidates = new List<string>();
            AddDataSourceCandidate(candidates, root, declaredRelative);
            AddDataSourceCandidate(candidates, root, declaredRelative == null
                ? null : Path.GetFileName(declaredRelative));
            AddDataSourceCandidate(candidates, root, proxyPath == null ? null : Path.GetFileName(proxyPath));
            if (candidates.Count == 0) return null;

            string target = candidates[0];
            string stem = target.Substring(0, target.Length - DataSourceShapefileExtension.Length);
            var hashes = new List<string>();
            foreach (string extension in DataSourceMemberExtensions)
            {
                string member = stem + extension;
                if (!File.Exists(member)) return null;
                hashes.Add(WorkingCopyIntegrity.Fingerprint(member).SHA256);
            }

            DataSourceManifestEntry entry = ManifestFor(manifest, RelativeInside(root, target),
                Path.GetFileName(target));
            if (entry == null) return null;
            for (int index = 0; index < DataSourceMemberExtensions.Length; index++)
            {
                if (!string.Equals(hashes[index], entry.SHA256[index], StringComparison.OrdinalIgnoreCase))
                    return null;
            }
            return new DataSourceResolution { Path = target, SHA256 = hashes.ToArray(), FamilyVerified = true };
        }

        // A bounded, read-only description of a stored vendor object and its immediate members.
        // Diagnostic only: it decides nothing and writes nothing.
        private static string DescribeShape(object value, int depth)
        {
            var text = new StringBuilder();
            text.Append(value.GetType().FullName);
            if (depth > 0) return text.ToString();
            foreach (PropertyInfo property in SafeProperties(value).Take(24))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                string member;
                try
                {
                    object inner = property.GetValue(value, null);
                    member = inner == null ? "<null>" : Bound(Convert.ToString(inner, CultureInfo.InvariantCulture));
                }
                catch (Exception error) { member = "<" + Unwrap(error).GetType().Name + ">"; }
                text.Append(" | ").Append(property.Name).Append(':').Append(property.PropertyType.Name)
                    .Append(property.CanWrite ? ":RW" : ":R").Append('=').Append(member);
            }
            return Bound(text.ToString());
        }

        internal static string RelativeInside(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full.Substring(prefix.Length) : null;
        }

        private static bool SamePath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static bool ResolvesTo(string root, string relative, string target)
        {
            if (string.IsNullOrWhiteSpace(relative)) return false;
            try { return SamePath(Path.Combine(root, relative), target); }
            catch (Exception) { return false; }
        }

        // Anchors a possibly-relative stored path at the copy root. Absolute paths pass through so
        // they can be judged on their own merits.
        internal static string ResolveAgainst(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path)); }
            catch (Exception) { return path; }
        }

        // True when a stored reference still points at a location outside the disposable copy.
        private static bool RequiresExternalPath(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string full = Path.GetFullPath(path);
                string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return true; }
        }

        // Reads the path out of a document-specification proxy string. Read-only: the proxy is never
        // constructed or rewritten by hand.
        internal static string ProxyPath(string proxy)
        {
            if (string.IsNullOrEmpty(proxy)) return null;
            int index = proxy.IndexOf("Path=\"", StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;
            int start = index + 6;
            int end = proxy.IndexOf('"', start);
            if (end <= start) return null;
            return proxy.Substring(start, end - start).Replace("&amp;", "&");
        }

        // ------------------------------------------------------------------ calculate phase

        internal static void ExecuteCalculate(object dataset, object ui, object project, HostRequest request,
            ModelReadEvidence evidence)
        {
            var seen = new GeneralCalculationObservation
            {
                OpenedModelPath = request.WorkingModelPath,
                WorkingCopyId = request.WorkingCopyId,
                SolverRunInvocations = 0,
                SaveInvocations = 0,
                NotificationCaptureLimitation =
                    "The general calculation path establishes completion from the vendor scenario compute status, " +
                    "persisted output inventory and an independent reopen/readback. Engine notification trees are " +
                    "not enumerated on this path, so a warning-bearing run is not distinguished from a clean run here."
            };
            var calls = new P16AzadeganCalculationHost.Vendor();
            try
            {
                var dataType = Contract("Haestad.Domain", "Haestad.Domain.IDomainDataSet");
                var scenarioType = Contract("Haestad.Domain", "Haestad.Domain.IScenario");
                var scenarioManagerType = Contract("Haestad.Domain", "Haestad.Domain.IScenarioManager");
                var elementsType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElementManager");
                var elementType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElement");

                object scenarioManager = calls.Get(dataset, dataType, NameScenarioManager, scenarioManagerType);
                int activeId = (int)calls.Get(scenarioManager, scenarioManagerType, "ActiveScenarioID", typeof(int));
                seen.ActiveScenarioId = activeId;
                if (activeId <= 0)
                {
                    seen.Status = "SCENARIO_NOT_SAVED";
                    seen.FailureMessage = "The model has no saved active scenario to calculate.";
                    return;
                }

                object scenario = calls.Invoke(scenarioManager, elementsType, "Element", elementType,
                    new[] { typeof(int) }, activeId);
                if (!scenarioType.IsInstanceOfType(scenario))
                {
                    seen.Status = "SCENARIO_NOT_RESOLVED";
                    seen.FailureMessage = "The saved active scenario could not be resolved in this model.";
                    return;
                }
                seen.ScenarioId = activeId;

                // The physical alternative identifies the network the scenario actually calculates.
                // Its absence means the scenario is not a hydraulically runnable configuration.
                try
                {
                    seen.PhysicalAlternativeId = (int)calls.Invoke(scenario, scenarioType, "AlternativeID", typeof(int),
                        new[] { typeof(int) }, int.Parse(P16AzadeganCalculationHost.PhysicalAlternativeTypeId,
                            CultureInfo.InvariantCulture));
                }
                catch (Exception error)
                {
                    seen.Status = "PHYSICAL_ALTERNATIVE_UNAVAILABLE";
                    seen.FailureMessage = "The active scenario has no physical alternative: " +
                        Bound(Unwrap(error).Message);
                    return;
                }

                string engine = ResolveEngine(dataset, scenario, scenarioType, calls, seen);
                if (engine == null) return;
                seen.Engine = engine;
                seen.CalculationOptionsId = (int)calls.Invoke(scenario, scenarioType, "CalculationOptionsID",
                    typeof(int), new[] { typeof(string) }, engine);
                if (seen.CalculationOptionsId.GetValueOrDefault() <= 0)
                {
                    seen.Status = "CALCULATION_OPTIONS_UNRESOLVED";
                    seen.FailureMessage = "The active scenario carries no calculation options for engine '" + engine + "'.";
                    return;
                }

                GravityWriteAndSolverHost.NumericalEngineBinding binding;
                try
                {
                    binding = GravityWriteAndSolverHost.BindNumericalEngine(dataset, scenario, engine, evidence,
                        AppDomain.CurrentDomain.GetAssemblies());
                }
                catch (Exception error)
                {
                    seen.Status = "SOLVER_BINDING_FAILED";
                    seen.FailureOperation = calls.Last;
                    seen.ErrorType = Unwrap(error).GetType().FullName;
                    seen.ErrorMessage = Bound(Unwrap(error).Message);
                    return;
                }
                if (!string.Equals(binding.EngineName, engine, StringComparison.Ordinal))
                {
                    seen.Status = "SOLVER_ENGINE_IDENTITY_MISMATCH";
                    seen.FailureMessage = "The bound engine '" + binding.EngineName + "' is not the scenario engine '" + engine + "'.";
                    return;
                }
                seen.ResolvedEngine = binding.EngineName;
                seen.EngineRuntimeType = binding.Engine.GetType().FullName;

                // Bounded input inventory before any calculation state exists. The copy must still be
                // the exact one the caller declared; otherwise no solve is attempted.
                var before = P16AzadeganCalculationHost.Inventory(Path.GetDirectoryName(request.WorkingModelPath));
                seen.InputInventoryDigest = P16AzadeganCalculationHost.InventoryDigest(before);
                if (!string.Equals(seen.InputInventoryDigest, request.Diagnostics[InventoryKey], StringComparison.Ordinal))
                {
                    seen.Status = "INPUT_INTEGRITY_MISMATCH";
                    seen.FailureMessage = "The disposable working copy changed after it was declared; no solve was attempted.";
                    return;
                }

                // A model that declares an external terrain input must find it inside this copy before
                // the engine is launched. Without it the engine builds an empty grid and aborts mid-run,
                // so the requirement is checked here and refused rather than discovered by failure.
                if (!VerifyTerrainResolution(dataset, Path.GetDirectoryName(request.WorkingModelPath), seen))
                    return;

                // A Grid2D record is configuration, not a generated grid: the engine opens a grid file
                // and rejects the run when it is absent or malformed. The vendor's own generator is
                // invoked here, in this process, so the grid lands in this run's private scratch root
                // and is still there when the engine reads it moments later. No solver has run yet, so
                // a grid failure stops the phase without spending a calculation.
                if (!GenerateComputationalGridStep(dataset, project, seen))
                    return;

                seen.CalculationEventId = Guid.NewGuid().ToString("N");
                seen.SolverStartTimestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                evidence.Diagnostics["CalculationEventId"] = seen.CalculationEventId;
                evidence.Diagnostics["SolverStartTimestamp"] = seen.SolverStartTimestamp;

                object collection = binding.CollectionConstructor.Invoke(new object[0]);
                binding.Add.Invoke(collection, new[] { scenario });
                int count = (int)binding.Count.GetValue(collection, null);
                if (count != 1)
                    throw new InvalidOperationException("The solver collection must contain exactly the selected scenario.");
                evidence.Diagnostics["SolverTargetScenarioID"] = seen.ScenarioId.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
                evidence.Diagnostics["SolverEngineTypeName"] = binding.EngineName;
                SolverLicenseContext.AttachCurrent(binding.Engine);
                evidence.Diagnostics["SolverRunInvoked"] = "true";
                seen.SolverRunInvocations = 1;

                calls.Last = "Haestad.Domain.INumericalEngine.Run";
                Exception engineFailure = null;
                try { RuntimeStageJournal.Call("SolverExecution", () => { binding.Run.Invoke(binding.Engine, new[] { collection }); }); }
                catch (Exception error) { engineFailure = Unwrap(error); }
                seen.SolverEndTimestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                evidence.Diagnostics["SolverEndTimestamp"] = seen.SolverEndTimestamp;

                if (engineFailure != null)
                {
                    seen.FailureOperation = "Haestad.Domain.INumericalEngine.Run";
                    seen.ErrorType = engineFailure.GetType().FullName;
                    seen.ErrorHResult = engineFailure.HResult;
                    seen.ErrorMessage = Bound(engineFailure.Message);
                    // Read the notification collection now: it lives only on the exception object, and
                    // it is the only place the individual warnings are ever named.
                    CaptureEngineWarnings(engineFailure, seen);
                }

                // The vendor's own scenario status is the completion boundary. A solver call that
                // returned without an exception is not evidence of a completed calculation.
                MethodInfo statusMethod = project.GetType().GetMethod("GetScenarioComputeStatus", new[] { typeof(int) });
                if (statusMethod == null || statusMethod.ReturnType == typeof(void))
                    throw new MissingMethodException("Scenario compute status member is unavailable.");
                MethodInfo statusTextMethod = project.GetType().GetMethod("ScenarioStatusText", new[] { typeof(int) });
                object status = statusMethod.Invoke(project, new object[] { seen.ScenarioId });
                seen.ScenarioComputeStatus = status == null ? "Unknown" : status.ToString();
                seen.ScenarioStatusText = statusTextMethod == null ? "" :
                    Bound(statusTextMethod.Invoke(project, new object[] { seen.ScenarioId }) as string);
                evidence.Diagnostics["ScenarioComputeStatus"] = seen.ScenarioComputeStatus;
                evidence.Diagnostics["ScenarioStatusText"] = seen.ScenarioStatusText ?? "";

                if (!string.Equals(seen.ScenarioComputeStatus, "Success", StringComparison.Ordinal))
                {
                    seen.Status = engineFailure == null ? "SOLVER_STATUS_FAILED" : "COMPLETION_STATE_UNCERTAIN";
                    if (string.IsNullOrEmpty(seen.FailureMessage))
                        seen.FailureMessage = "Bentley reported scenario status '" + seen.ScenarioComputeStatus + "'.";
                    return;
                }
                seen.EngineOutcome = engineFailure == null ? "COMPLETED_CLEAN" : "COMPLETED_WITH_EXCEPTION_REPORTED";
                evidence.Diagnostics["SolverRunCompleted"] = "true";
                evidence.Diagnostics["SolverStatusSuccess"] = "true";

                // Durable capture before the save, so a hang inside the vendor save cannot destroy the
                // observation. If it cannot be written the run fails closed and never reaches the save.
                try
                {
                    PersistPreSaveEvidence(seen);
                    evidence.Diagnostics["PreSaveEvidenceWritten"] = "true";
                }
                catch (Exception persistenceError)
                {
                    seen.Status = "PRESAVE_EVIDENCE_UNWRITABLE";
                    seen.FailureOperation = "PersistPreSaveEvidence";
                    seen.ErrorType = persistenceError.GetType().FullName;
                    seen.ErrorMessage = Bound(persistenceError.Message);
                    return;
                }

                if (!PersistWorkingCopy(project, request, evidence, seen, before))
                {
                    seen.Status = "PERSISTENCE_FAILED";
                    return;
                }

                evidence.Diagnostics["ResultFilesInventoryJson"] = seen.OutputInventoryJson;
                evidence.Diagnostics["SolverRunCount"] = "1";
                seen.Status = CompletedStatus;
            }
            catch (Exception error)
            {
                var exact = Unwrap(error);
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
                evidence.Diagnostics[EvidenceKey] = RuntimeHostProtocol.Serializer().Serialize(seen);
                evidence.Diagnostics["GeneralModelStatus"] = seen.Status;
                evidence.Diagnostics[PassFlag] = string.Equals(seen.Status, CompletedStatus, StringComparison.Ordinal)
                    ? "true" : "false";
                if (!string.Equals(seen.Status, CompletedStatus, StringComparison.Ordinal) && evidence.FailureCode == null)
                    evidence.FailureCode = BlockedFailureCode;
            }
        }

        // ------------------------------------------------------------------ engine warnings

        // The vendor's warning exception carries the only copy of the individual warnings, and only on
        // the exception object. They are read here into a bounded serialisable form. Anything that
        // cannot be read marks the capture UNKNOWN - never a warning-free run, and never a zero count
        // presented as if it were an observation.
        private static void CaptureEngineWarnings(Exception failure, GeneralCalculationObservation seen)
        {
            if (failure == null) { seen.WarningCaptureStatus = WarningCaptureNotApplicable; return; }
            Type vendorType;
            if (!IsEngineWarningsException(failure, out vendorType))
            {
                seen.WarningCaptureStatus = WarningCaptureNotApplicable;
                return;
            }
            seen.WarningExceptionType = vendorType.FullName;
            PropertyInfo property = vendorType.GetProperty("UserNotifications",
                BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanRead || property.GetIndexParameters().Length != 0)
            {
                seen.WarningCaptureStatus = WarningCaptureUnknown;
                seen.WarningCaptureFailure =
                    "EngineWarningsException exposes no readable UserNotifications member.";
                return;
            }
            object notifications;
            try { notifications = property.GetValue(failure, null); }
            catch (Exception error)
            {
                seen.WarningCaptureStatus = WarningCaptureUnknown;
                seen.WarningCaptureFailure = "UserNotifications could not be read: " +
                    Bound(Unwrap(error).GetType().Name);
                return;
            }
            string mappingFailure;
            GeneralEngineWarning[] mapped = MapEngineWarnings(notifications as IEnumerable, out mappingFailure);
            if (mappingFailure != null)
            {
                seen.WarningCaptureStatus = WarningCaptureUnknown;
                seen.WarningCaptureFailure = mappingFailure;
                return;
            }
            seen.Warnings = mapped;
            seen.WarningNotificationCount = mapped.Length;
            seen.WarningCaptureStatus = WarningCaptureCaptured;
        }

        // True only for the vendor's own warning exception, matched by name up the inheritance chain so
        // a derived type is still recognised and no unrelated exception can be mistaken for it.
        internal static bool IsEngineWarningsException(Exception failure, out Type vendorType)
        {
            for (Type type = failure == null ? null : failure.GetType(); type != null; type = type.BaseType)
            {
                if (!string.Equals(type.FullName, EngineWarningsExceptionType, StringComparison.Ordinal))
                    continue;
                vendorType = type;
                return true;
            }
            vendorType = null;
            return false;
        }

        // Maps the vendor's notification collection. Separated from the exception so the mapping can be
        // exercised against synthetic notifications without ever invoking the solver.
        internal static GeneralEngineWarning[] MapEngineWarnings(IEnumerable notifications, out string failure)
        {
            failure = null;
            if (notifications == null)
            {
                failure = "UserNotifications was null, so the individual warnings are unavailable.";
                return null;
            }
            var mapped = new List<GeneralEngineWarning>();
            try
            {
                foreach (object notification in notifications)
                {
                    // A hole in the collection means the individual warnings cannot be reported in full,
                    // so the capture is UNKNOWN rather than a shorter list presented as complete.
                    if (notification == null)
                    {
                        failure = "The notification collection contains a null entry, so a complete capture " +
                            "cannot be claimed.";
                        return null;
                    }
                    // Reaching the bound with another notification still to read means the collection was
                    // truncated. Reporting that as CAPTURED would hide every warning past the bound.
                    if (mapped.Count >= MaxEngineWarnings)
                    {
                        failure = "The notification collection holds more than " + MaxEngineWarnings +
                            " entries, so the capture was truncated and cannot be claimed complete.";
                        return null;
                    }
                    mapped.Add(MapEngineWarning(notification));
                }
            }
            catch (Exception error)
            {
                failure = "The notification collection could not be enumerated: " +
                    Bound(Unwrap(error).GetType().Name);
                return null;
            }
            return mapped.ToArray();
        }

        private static GeneralEngineWarning MapEngineWarning(object notification)
        {
            Type type = notification.GetType();
            return new GeneralEngineWarning
            {
                MessageKey = MemberText(notification, type, "MessageKey"),
                Level = MemberText(notification, type, "Level"),
                ElementId = MemberInt(notification, type, "ElementId"),
                ElementType = MemberText(notification, type, "ElementType"),
                ScenarioId = MemberInt(notification, type, "ScenarioId"),
                MessageId = MemberText(notification, type, "MessageId"),
                SourceKey = MemberText(notification, type, "SourceKey"),
                Parameters = MemberParameters(notification, type)
            };
        }

        private static object MemberValue(object instance, Type type, string name)
        {
            try
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                    return property.GetValue(instance, null);
                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field != null) return field.GetValue(instance);
            }
            catch (Exception) { }
            return null;
        }

        private static string MemberText(object instance, Type type, string name)
        {
            object value = MemberValue(instance, type, name);
            return value == null ? null : Bound(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static int? MemberInt(object instance, Type type, string name)
        {
            object value = MemberValue(instance, type, name);
            if (value == null) return null;
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch (Exception) { return null; }
        }

        // Parameters are the only free-form part of a notification, so they are flattened to bounded
        // key=value text and capped in both count and length.
        private static string[] MemberParameters(object instance, Type type)
        {
            object value = MemberValue(instance, type, "Parameters");
            var items = value as IEnumerable;
            if (items == null || value is string) return new string[0];
            var text = new List<string>();
            try
            {
                foreach (object item in items)
                {
                    if (text.Count >= MaxEngineWarningParameters) break;
                    if (item == null) { text.Add("(null)"); continue; }
                    object key = MemberValue(item, item.GetType(), "Key");
                    object content = MemberValue(item, item.GetType(), "Value");
                    text.Add(key == null && content == null
                        ? Bound(Convert.ToString(item, CultureInfo.InvariantCulture))
                        : Bound(Convert.ToString(key, CultureInfo.InvariantCulture)) + "=" +
                          Bound(Convert.ToString(content, CultureInfo.InvariantCulture)));
                }
            }
            catch (Exception) { }
            return text.ToArray();
        }

        // Resolves the numerical engine the model's own saved scenario is configured for, using the
        // the dataset *type* owner (IDomainDataSetType), not to the dataset instance. The model's
        // main-engine accessor is preferred; otherwise a single engine that carries calculation
        // options for this scenario is accepted. More than one such engine is ambiguous and refused.
        private static string ResolveEngine(object dataset, object scenario, Type scenarioType,
            P16AzadeganCalculationHost.Vendor calls, GeneralCalculationObservation seen)
        {
            object owner = null;
            MethodInfo ownerMethod = dataset.GetType().GetMethod("DomainDataSetType", Type.EmptyTypes);
            if (ownerMethod != null && !ownerMethod.IsStatic && ownerMethod.ReturnType != typeof(void))
            {
                try { owner = ownerMethod.Invoke(dataset, null); }
                catch (Exception) { owner = null; }
            }

            string main = null;
            if (owner != null)
            {
                MethodInfo getMain = owner.GetType().GetMethod("GetMainNumericalEngineTypeName",
                    BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (getMain != null && !getMain.IsStatic && getMain.ReturnType == typeof(string))
                {
                    try { main = getMain.Invoke(owner, null) as string; }
                    catch (Exception) { main = null; }
                }
            }
            if (string.IsNullOrWhiteSpace(main)) main = null;
            seen.MainEngineFromModel = main;

            var declared = new List<string>();
            if (owner != null)
            {
                MethodInfo enumerate = owner.GetType().GetMethod("NumericalEngineTypes", Type.EmptyTypes);
                if (enumerate != null && !enumerate.IsStatic)
                {
                    object collection = null;
                    try { collection = enumerate.Invoke(owner, null); }
                    catch (Exception) { collection = null; }
                    if (collection is IEnumerable enumerable)
                    {
                        foreach (object item in enumerable)
                        {
                            if (declared.Count >= MaxEngineCandidates) break;
                            string name = item == null ? null :
                                item.GetType().GetProperty("Name")?.GetValue(item, null) as string;
                            if (!string.IsNullOrWhiteSpace(name) && name.Length <= 256 && !declared.Contains(name, StringComparer.Ordinal))
                                declared.Add(name);
                        }
                    }
                }
            }
            seen.ModelEngineTypeNames = declared.ToArray();

            var candidates = new List<string>();
            if (main != null) candidates.Add(main);
            foreach (string name in declared)
                if (!candidates.Contains(name, StringComparer.Ordinal)) candidates.Add(name);
            seen.EngineCandidates = candidates.ToArray();
            if (candidates.Count == 0)
            {
                seen.Status = "ENGINE_UNRESOLVED";
                seen.FailureMessage =
                    "This model does not expose a numerical engine through the installed Bentley interface, " +
                    "so its scenario cannot be calculated by SewerPilot.";
                return null;
            }

            // An engine is a candidate only when this scenario carries its own calculation options for it.
            var usable = new List<string>();
            foreach (string candidate in candidates)
            {
                int options;
                try
                {
                    options = (int)calls.Invoke(scenario, scenarioType, "CalculationOptionsID", typeof(int),
                        new[] { typeof(string) }, candidate);
                }
                catch (Exception) { continue; }
                if (options > 0) usable.Add(candidate);
            }
            if (usable.Count == 0)
            {
                seen.Status = "ENGINE_UNRESOLVED";
                seen.FailureMessage = "No numerical engine carries calculation options for the saved active scenario.";
                return null;
            }
            if (main != null && usable.Contains(main, StringComparer.Ordinal))
            {
                seen.EngineSelectionBasis = "MODEL_MAIN_ENGINE_FOR_ACTIVE_SCENARIO";
                return main;
            }
            if (usable.Count > 1)
            {
                seen.Status = "ENGINE_AMBIGUOUS";
                seen.FailureMessage = "More than one numerical engine carries calculation options for this scenario (" +
                    string.Join(", ", usable) + "); select a single-engine scenario before calculating.";
                return null;
            }
            seen.EngineSelectionBasis = "SINGLE_ENGINE_WITH_SCENARIO_CALCULATION_OPTIONS";
            return usable[0];
        }

        // ------------------------------------------------------------------ non-UI persistence

        // Mirrors the verified non-UI persistence boundary already used by the P16 native path:
        // Haestad.Framework.Application.IProject::Save(ProjectProperties) with a bounded question
        // handler. It never enters the UI-bound SaveProject path, which can block in a MessageBox.
        // Persistence is established from the output inventory delta plus the caller's reopen/readback,
        // never from a return value.
        private static bool PersistWorkingCopy(object project, HostRequest request, ModelReadEvidence evidence,
            GeneralCalculationObservation seen, SortedDictionary<string, string> before)
        {
            var recorder = new P16SaveQuestionRecorder(request.WorkingModelPath);
            seen.SaveInvocations = 1;
            evidence.Diagnostics["P16PersistenceBoundary"] = P16AzadeganCalculationHost.PersistenceBoundary;

            bool raised = false;
            bool? dirty = null;
            try
            {
                Type projectContract = Contract("Haestad.Framework", "Haestad.Framework.Application.IProject");
                Type propertiesType = Contract("Haestad.Framework", "Haestad.Framework.Application.ProjectProperties");
                Type questionContract = Contract("Haestad.Support", "Haestad.Support.User.IMessageQuestionHandler");

                MethodInfo persist = AdapterWriteBinding.ResolveProjectSave(projectContract, propertiesType);
                MethodInfo fullPath = projectContract.GetMethod("get_FullPath",
                    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                MethodInfo isDirty = projectContract.GetMethod("get_IsDirty",
                    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                ConstructorInfo propertiesConstructor = propertiesType.GetConstructor(Type.EmptyTypes);
                PropertyInfo questionProperty = propertiesType.GetProperty("MessageQuestionHandler",
                    BindingFlags.Public | BindingFlags.Instance);
                MethodInfo showQuestion = questionContract.GetMethod("ShowQuestion",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(string), typeof(string), typeof(bool) }, null);
                Type resultEnum = showQuestion == null ? null : showQuestion.ReturnType;

                if (persist == null || persist.ReturnType != typeof(void) ||
                    fullPath == null || fullPath.ReturnType != typeof(string) ||
                    isDirty == null || isDirty.ReturnType != typeof(bool) ||
                    propertiesConstructor == null || questionProperty == null ||
                    questionProperty.PropertyType != questionContract || questionProperty.GetSetMethod() == null ||
                    showQuestion == null || resultEnum == null || !resultEnum.IsEnum)
                    throw new MissingMethodException("The verified non-UI persistence contract is unavailable on this installed build.");
                if (!projectContract.IsInstanceOfType(project))
                    throw new InvalidOperationException("The current project does not implement Haestad.Framework.Application.IProject.");

                string openedPath = RuntimeStageJournal.Call("P16PersistenceTarget",
                    () => fullPath.Invoke(project, null) as string, value => value);
                if (!P16SaveQuestionPolicy.SamePath(openedPath, request.WorkingModelPath))
                    throw new InvalidOperationException("The persistence target is not the authorised disposable working model.");

                object properties = propertiesConstructor.Invoke(null);
                object handler = P16SaveQuestionHandler.Create(questionContract, resultEnum, recorder);
                AdapterWriteBinding.InvokeSetValue(questionProperty, properties, handler);

                evidence.Diagnostics["SaveCurrentProjectInvoked"] = "true";
                RuntimeStageJournal.Call(P16AzadeganCalculationHost.PersistenceStageName,
                    () => AdapterWriteBinding.ExecuteSave(project, propertiesType, properties));

                seen.SaveTimestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                try { dirty = (bool)isDirty.Invoke(project, null); }
                catch (Exception) { dirty = null; }
            }
            catch (Exception error)
            {
                raised = true;
                var exact = Unwrap(error);
                seen.FailureOperation = "Haestad.Framework.Application.IProject.Save";
                seen.ErrorType = exact.GetType().FullName;
                seen.ErrorHResult = exact.HResult;
                seen.ErrorMessage = Bound(exact.Message);
                if (seen.SaveTimestamp == null)
                    seen.SaveTimestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            }

            int changed = 0;
            if (!raised)
            {
                try
                {
                    var after = P16AzadeganCalculationHost.Inventory(Path.GetDirectoryName(request.WorkingModelPath));
                    seen.OutputInventoryJson = P16AzadeganCalculationHost.InventoryJson(after);
                    seen.CreatedFiles = after.Keys.Where(name => !before.ContainsKey(name)).ToArray();
                    seen.ModifiedFiles = after.Keys.Where(name => before.ContainsKey(name) && before[name] != after[name]).ToArray();
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
            seen.ProjectIsDirtyAfterSave = dirty;
            seen.SaveSucceeded = P16ProjectPersistenceResult.IsPersisted(classification);
            evidence.Diagnostics["P16SaveResultClassification"] = classification;
            evidence.Diagnostics["SaveCurrentProjectSucceeded"] = seen.SaveSucceeded ? "true" : "false";
            return seen.SaveSucceeded;
        }

        private static void PersistPreSaveEvidence(GeneralCalculationObservation seen)
        {
            if (string.IsNullOrEmpty(seen.CalculationEventId))
                throw new InvalidOperationException("No calculation event was established.");
            seen.PreSaveStatus = "CALCULATION_COMPLETED_AWAITING_PERSISTENCE";
            var record = new GeneralPreSaveRecord
            {
                Status = seen.PreSaveStatus,
                CalculationEventId = seen.CalculationEventId,
                ScenarioId = seen.ScenarioId,
                PhysicalAlternativeId = seen.PhysicalAlternativeId,
                CalculationOptionsId = seen.CalculationOptionsId,
                Engine = seen.Engine,
                ScenarioComputeStatus = seen.ScenarioComputeStatus,
                SolverStartTimestamp = seen.SolverStartTimestamp,
                SolverEndTimestamp = seen.SolverEndTimestamp,
                SolverRunInvocations = seen.SolverRunInvocations
            };
            string path = Path.Combine(Path.GetDirectoryName(seen.OpenedModelPath), "general-presave-observation.json");
            File.WriteAllText(path, RuntimeHostProtocol.Serializer().Serialize(record), new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------ read phase

        // Reopens the calculated working copy in a separate process and reads the genuine persisted
        // results. No solver runs here, nothing is saved, and a metric that the engine does not expose
        // stays NotAvailable rather than being defaulted to zero.
        internal static void ExecuteRead(object dataset, object project, HostRequest request, ModelReadEvidence evidence)
        {
            var seen = new GeneralReadbackObservation { OpenedModelPath = request.WorkingModelPath };
            var calls = new P16AzadeganCalculationHost.Vendor();
            try
            {
                var dataType = Contract("Haestad.Domain", "Haestad.Domain.IDomainDataSet");
                var scenarioType = Contract("Haestad.Domain", "Haestad.Domain.IScenario");
                var scenarioManagerType = Contract("Haestad.Domain", "Haestad.Domain.IScenarioManager");
                var elementsType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElementManager");
                var elementType = Contract("Haestad.Domain", "Haestad.Domain.IModelingElement");

                int scenarioId = int.Parse(request.Diagnostics[ScenarioKey], CultureInfo.InvariantCulture);
                string engine = request.Diagnostics[EngineKey];
                seen.ScenarioId = scenarioId;
                seen.Engine = engine;
                seen.CalculationEventId = request.Diagnostics[EventKey];

                // The reopened copy must still be byte-identical to the state the calculate phase
                // persisted. Otherwise the rows read here could belong to a different run.
                seen.ReopenedInventoryDigest = P16AzadeganCalculationHost.InventoryDigest(
                    P16AzadeganCalculationHost.Inventory(Path.GetDirectoryName(request.WorkingModelPath)));
                if (!InventoryMatches(request.Diagnostics[InventoryKey], seen.ReopenedInventoryDigest))
                {
                    seen.Status = "REOPENED_INVENTORY_MISMATCH";
                    seen.FailureMessage = "The reopened model files differ from the persisted calculation output.";
                    return;
                }

                object scenarioManager = calls.Get(dataset, dataType, NameScenarioManager, scenarioManagerType);
                seen.ActiveScenarioId = (int)calls.Get(scenarioManager, scenarioManagerType, "ActiveScenarioID", typeof(int));
                if (seen.ActiveScenarioId != scenarioId)
                {
                    seen.Status = "REOPENED_SCENARIO_MISMATCH";
                    seen.FailureMessage = "The reopened model's active scenario " + seen.ActiveScenarioId +
                        " is not the calculated scenario " + scenarioId + ".";
                    return;
                }

                object scenario = calls.Invoke(scenarioManager, elementsType, "Element", elementType,
                    new[] { typeof(int) }, scenarioId);
                if (scenario == null || !scenarioType.IsInstanceOfType(scenario))
                {
                    seen.Status = "REOPENED_SCENARIO_UNAVAILABLE";
                    seen.FailureMessage = "The calculated scenario could not be resolved after reopening.";
                    return;
                }
                seen.CalculationOptionsId = (int)calls.Invoke(scenario, scenarioType, "CalculationOptionsID",
                    typeof(int), new[] { typeof(string) }, engine);
                if (seen.CalculationOptionsId != int.Parse(request.Diagnostics[OptionsKey], CultureInfo.InvariantCulture))
                {
                    seen.Status = "REOPENED_OPTIONS_MISMATCH";
                    seen.FailureMessage = "The reopened calculation options do not match the calculated run.";
                    return;
                }

                // The persisted result store must actually hold results for this scenario and engine.
                var resultManagerType = Contract("Haestad.Domain", "Haestad.Domain.IResultManager");
                object manager = calls.Invoke(scenario, scenarioType, "ResultManager", resultManagerType,
                    new[] { typeof(string) }, engine);
                if (manager == null)
                {
                    seen.Status = "RESULT_MANAGER_UNAVAILABLE";
                    seen.FailureMessage = "No result manager exists for engine '" + engine + "' on this scenario.";
                    return;
                }
                object active = resultManagerType.GetProperty("IsActive").GetValue(manager, null);
                if (!(active is bool isActive && isActive))
                    resultManagerType.GetMethod("Open", Type.EmptyTypes).Invoke(manager, null);
                object hasResults = resultManagerType.GetProperty("HasResults").GetValue(manager, null);
                seen.HasResults = hasResults is bool has && has;
                evidence.Diagnostics["ResultManagerAvailable"] = "true";
                evidence.Diagnostics["HasResults"] = seen.HasResults ? "true" : "false";
                if (!seen.HasResults)
                {
                    seen.Status = "HAS_RESULTS_FALSE";
                    seen.FailureMessage = "The reopened model reports no persisted results for this scenario.";
                    return;
                }
                try { resultManagerType.GetMethod("Close", Type.EmptyTypes).Invoke(manager, null); }
                catch (Exception) { /* Closing the manager is a convenience; the reader reopens as needed. */ }

                var reader = new WaterObjectsResultReader();
                var rows = new List<GeneralHydraulicResultRow>();
                int conduitCount = ReadConduits(dataset, project, reader, scenarioId, engine, rows);
                seen.ConduitCount = conduitCount;
                int structureCount = ReadStructures(dataset, project, reader, scenarioId, engine, rows);
                seen.StructureCount = structureCount;

                seen.Rows = rows.ToArray();
                seen.RowCount = rows.Count;
                int available = rows.Count(row => string.Equals(row.Status, "AVAILABLE", StringComparison.Ordinal));
                seen.AvailableRowCount = available;
                if (rows.Count == 0)
                {
                    seen.Status = "NO_RESULT_ROWS";
                    seen.FailureMessage = "The reopened model produced no readable hydraulic result rows.";
                    return;
                }
                if (available == 0)
                {
                    seen.Status = "NO_AVAILABLE_METRICS";
                    seen.FailureMessage = "The reopened model exposes no available hydraulic metric for any element.";
                    return;
                }
                seen.Status = ReadVerifiedStatus;
                seen.Provenance = "MODEL:" + Path.GetFileName(request.WorkingModelPath) + "->SCENARIO:" + scenarioId +
                    "->OPTS:" + seen.CalculationOptionsId + "->ENGINE:" + engine + "->EVENT:" + seen.CalculationEventId +
                    "->HAS_RESULTS:TRUE->ROWS:" + rows.Count + "->AVAILABLE:" + available;
                evidence.Diagnostics["GeneralModelReadStatus"] = seen.Status;
                evidence.Diagnostics["GeneralModelReadRowCount"] = rows.Count.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception error)
            {
                var exact = Unwrap(error);
                seen.Status = "READBACK_FAILED";
                seen.FailureOperation = calls.Last;
                seen.ErrorType = exact.GetType().FullName;
                seen.ErrorMessage = Bound(exact.Message);
                if (evidence.FailureCode == null) evidence.FailureCode = BlockedFailureCode;
            }
            finally
            {
                evidence.Diagnostics[EvidenceKey] = RuntimeHostProtocol.Serializer().Serialize(seen);
                evidence.Diagnostics["GeneralModelReadStatus"] = seen.Status;
                evidence.Diagnostics[ReadPassFlag] = string.Equals(seen.Status, ReadVerifiedStatus, StringComparison.Ordinal)
                    ? "true" : "false";
                if (!string.Equals(seen.Status, ReadVerifiedStatus, StringComparison.Ordinal) && evidence.FailureCode == null)
                    evidence.FailureCode = BlockedFailureCode;
            }
        }

        private static int ReadConduits(object dataset, object project, WaterObjectsResultReader reader,
            int scenarioId, string engine, List<GeneralHydraulicResultRow> rows)
        {
            var ids = ElementIds(dataset, "ConduitElementManager");
            if (ids.Count > MaxConduits)
                throw new InvalidDataException("The model contains more conduits than this path will report (" + ids.Count + ").");
            foreach (int id in ids)
            {
                string label = ElementLabel(dataset, "ConduitElementManager", id);
                AddFlow(rows, id, label, reader.ReadFlow(dataset, project, scenarioId, engine, id));
                Add(rows, id, label, "Conduit", "Velocity", reader.ReadVelocity(dataset, project, scenarioId, engine, id));
                Add(rows, id, label, "Conduit", "Depth", reader.ReadDepth(dataset, project, scenarioId, engine, id));
                Add(rows, id, label, "Conduit", "HGL", reader.ReadHgl(dataset, project, scenarioId, engine, id));
                Add(rows, id, label, "Conduit", "Capacity", reader.ReadCapacity(dataset, project, scenarioId, engine, id));
                Add(rows, id, label, "Conduit", "Surcharge", reader.ReadSurcharge(dataset, project, scenarioId, engine, id));
                if (rows.Count > MaxResultRows)
                    throw new InvalidDataException("The result set exceeds the bound this path will report.");
            }
            return ids.Count;
        }

        private static int ReadStructures(object dataset, object project, WaterObjectsResultReader reader,
            int scenarioId, string engine, List<GeneralHydraulicResultRow> rows)
        {
            var ids = ElementIds(dataset, "ManholeElementManager");
            if (ids.Count > MaxStructures)
                throw new InvalidDataException("The model contains more structures than this path will report (" + ids.Count + ").");
            foreach (int id in ids)
            {
                string label = ElementLabel(dataset, "ManholeElementManager", id);
                Add(rows, id, label, "Structure", "HGL",
                    reader.ReadNodeHgl(dataset, project, scenarioId, engine, id, "Manhole"));
                if (rows.Count > MaxResultRows)
                    throw new InvalidDataException("The result set exceeds the bound this path will report.");
            }
            return ids.Count;
        }

        // One rule for inventory identity, shared by the host guard and by acceptance checks:
        // a missing or different digest is a mismatch. An absent digest never counts as a match,
        // so two empty values can never satisfy the check.
        internal static bool InventoryMatches(string declared, string actual) =>
            !string.IsNullOrEmpty(declared) && !string.IsNullOrEmpty(actual) &&
            string.Equals(declared, actual, StringComparison.Ordinal);

        // One availability rule for every metric.
        //  - A parsed finite number is an available value. A real zero stays zero.
        //  - An explicit vendor Boolean ("True"/"False") is an available NON-NUMERIC result: it must
        //    never be turned into a number and never be mistaken for missing data.
        //  - Anything else (empty, "N/A", an error or warning string) is NOT_AVAILABLE, so an
        //    unavailable metric can never be counted as available.
        internal static string ClassifyMetricValue(string rawValue, out double? value)
        {
            value = null;
            if (string.IsNullOrEmpty(rawValue)) return "NOT_AVAILABLE";
            double parsed;
            if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
                !double.IsNaN(parsed) && !double.IsInfinity(parsed))
            {
                value = parsed;
                return "AVAILABLE";
            }
            if (string.Equals(rawValue, "True", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawValue, "False", StringComparison.OrdinalIgnoreCase))
                return "AVAILABLE";
            return "NOT_AVAILABLE";
        }

        // The dedicated flow reader returns its own result type. It is projected onto the same row
        // shape so every metric obeys one availability rule.
        private static void AddFlow(List<GeneralHydraulicResultRow> rows, int id, string label,
            WaterObjectsFlowResult result)
        {
            var row = new GeneralHydraulicResultRow
            {
                ElementId = id,
                ElementLabel = label,
                Category = "Conduit",
                Metric = "Flow",
                FieldName = result == null ? null : WaterObjectsResultReader.FlowFieldName,
                RecordName = result == null ? null : WaterObjectsResultReader.FlowRecordName,
                Status = "NOT_AVAILABLE",
                Unit = result == null ? "" : (result.WorkingUnitSymbol ?? "")
            };
            if (result != null && string.Equals(result.Status, "SUCCESS", StringComparison.Ordinal))
            {
                double? value;
                row.Status = ClassifyMetricValue(result.RawValue, out value);
                row.Value = value;
                row.RawValue = result.RawValue;
                row.TimeSeconds = result.TimeSeconds;
                row.TimeStepIndex = result.TimeStepIndex;
            }
            else if (result != null)
            {
                row.Status = string.Equals(result.Status, "HAS_RESULTS_FALSE", StringComparison.Ordinal)
                    ? "NOT_AVAILABLE" : (result.Status ?? "NOT_AVAILABLE");
            }
            rows.Add(row);
        }

        private static void Add(List<GeneralHydraulicResultRow> rows, int id, string label, string category,
            string metric, WaterObjectsMetricResult result)
        {
            var row = new GeneralHydraulicResultRow
            {
                ElementId = id,
                ElementLabel = label,
                Category = category,
                Metric = metric,
                FieldName = result == null ? null : result.FieldName,
                RecordName = result == null ? null : result.RecordName,
                Status = "NOT_AVAILABLE",
                Unit = result == null ? "" : (result.WorkingUnitSymbol ?? "")
            };
            if (result != null && string.Equals(result.Status, "SUCCESS", StringComparison.Ordinal))
            {
                double? value;
                row.Status = ClassifyMetricValue(result.RawValue, out value);
                row.Value = value;
                // A Boolean result keeps its exact vendor text ("False"), so a legitimate False is
                // never reported as missing data and never as the number zero.
                row.RawValue = result.RawValue;
                row.TimeSeconds = result.TimeSeconds;
                row.TimeStepIndex = result.TimeStepIndex;
            }
            else if (result != null)
            {
                row.Status = string.Equals(result.Status, "HAS_RESULTS_FALSE", StringComparison.Ordinal)
                    ? "NOT_AVAILABLE" : (result.Status ?? "NOT_AVAILABLE");
            }
            rows.Add(row);
        }

        // Element ids and labels come from the model's own element managers. Enumeration is exact-name
        // and bounded; no directory scan and no id list is ever hardcoded.
        private static List<int> ElementIds(object dataset, string managerName) =>
            ElementIdsOf(ElementManager(dataset, managerName));

        private static List<int> ElementIdsOf(object manager)
        {
            var ids = new List<int>();
            if (manager == null) return ids;
            MethodInfo idsMethod = WaterObjectsLifecycleHost.ElementIDs(manager.GetType());
            object collection = idsMethod.Invoke(manager, null);
            if (collection == null) return ids;
            PropertyInfo indexer = WaterObjectsLifecycleHost.Indexer(collection.GetType());
            int count;
            try { count = (int)collection.GetType().GetProperty("Count").GetValue(collection, null); }
            catch (Exception) { count = MaxResultRows + 1; }
            if (count > MaxConduits) throw new InvalidDataException("The model element count exceeds the reporting bound.");
            for (int index = 0; index < count; index++)
            {
                object datum = indexer.GetValue(collection, new object[] { index });
                if (datum is int id) ids.Add(id);
            }
            return ids;
        }

        private static object ElementManager(object dataset, string managerName)
        {
            var elementType = Contract("Haestad.Domain", "Haestad.Domain.DomainElementType");
            object enumValue;
            try { enumValue = WaterObjectsLifecycleHost.EnumMember(elementType, managerName); }
            catch (Exception) { return null; }
            if (enumValue == null) return null;
            var datasetType = Contract("Haestad.Domain", "Haestad.Domain.IDomainDataSet");
            MethodInfo method = datasetType.GetMethod("DomainElementManager", new[] { typeof(int) });
            if (method == null) return null;
            return method.Invoke(dataset, new object[] { Convert.ToInt32(enumValue) });
        }

        private static string ElementLabel(object dataset, string managerName, int id)
        {
            try
            {
                object manager = ElementManager(dataset, managerName);
                if (manager == null) return "";
                object instance = ElementById(manager, id);
                if (instance == null) return "";
                var label = instance.GetType().GetProperty("Label", BindingFlags.Public | BindingFlags.Instance);
                string text = label == null ? null : label.GetValue(instance, null) as string;
                return Bound(text) ?? "";
            }
            catch (Exception) { return ""; }
        }

        // The element accessor is declared on a base interface of the manager contract, so it is
        // resolved across the full interface hierarchy exactly as the proven read path does.
        private static object ElementById(object manager, int id)
        {
            foreach (Type contract in WaterObjectsLifecycleHost.Hierarchy(manager.GetType()))
            {
                MethodInfo method = contract
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(item => item.Name == "Element" && !item.ContainsGenericParameters &&
                        item.GetParameters().Length == 1 && item.GetParameters()[0].ParameterType == typeof(int));
                if (method == null || method.ReturnType == typeof(void)) continue;
                return method.Invoke(manager, new object[] { id });
            }
            return null;
        }

        // ------------------------------------------------------------------ declared external inputs

        private static object TerrainManager(object dataset)
        {
            foreach (string name in TerrainManagerNames)
            {
                object manager = ElementManager(dataset, name) ?? SupportElementManager(dataset, name);
                if (manager != null) return manager;
            }
            return null;
        }

        // Terrain models are carried as support elements rather than domain elements, so both
        // enumerations are consulted before a model is reported as having no terrain at all.
        private static object SupportElementManager(object dataset, string name)
        {
            try
            {
                Type supportType = Contract("Haestad.Domain", "Haestad.Domain.SupportElementType");
                object enumValue = WaterObjectsLifecycleHost.EnumMember(supportType, name);
                if (enumValue == null) return null;
                MethodInfo method = dataset.GetType().GetMethod("SupportElementManager", new[] { typeof(int) });
                if (method == null) return null;
                return method.Invoke(dataset, new object[] { Convert.ToInt32(enumValue, CultureInfo.InvariantCulture) });
            }
            catch (Exception) { return null; }
        }

        // Declared element-manager names whose meaning relates to terrain. Diagnostic only.
        private static string[] ManagerNames(params string[] hints)
        {
            var names = new List<string>();
            foreach (var spec in new[]
                     {
                         new { Type = "Haestad.Domain.DomainElementType", Prefix = "DOMAIN:" },
                         new { Type = "Haestad.Domain.SupportElementType", Prefix = "SUPPORT:" }
                     })
            {
                try
                {
                    Type type = Contract("Haestad.Domain", spec.Type);
                    names.AddRange(Enum.GetNames(type)
                        .Where(name => hints.Any(hint => name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0))
                        .Select(name => spec.Prefix + name));
                }
                catch (Exception) { }
            }
            return names.OrderBy(name => name, StringComparer.Ordinal).Take(MaxTerrainPropertyDump).ToArray();
        }

        private static GeneralTerrainDependency TerrainDependency(object manager, object element, int id) =>
            new GeneralTerrainDependency
            {
                ElementId = id,
                ElementLabel = TerrainText(element, "Label") ?? SupportText(manager, id, "Label"),
                IsActive = TerrainBool(element, "IsActive") ??
                           SupportBool(manager, id, "IsActive", "DigitalTerrainModel_IsActive"),
                IsChecked = TerrainBool(element, "IsChecked") ??
                            SupportBool(manager, id, "IsChecked", "DigitalTerrainModel_IsChecked"),
                SourceType = TerrainText(element, "DTMSourceType", "SourceType") ??
                             SupportText(manager, id, "DTMSourceType", "SourceType"),
                DeclaredAbsolutePath = TerrainText(element, "FilePath", "File") ??
                                       SupportText(manager, id, "FilePath", "DigitalTerrainModel_FilePath"),
                DeclaredRelativePath = TerrainText(element, "RelativeFilePath", "RelativeFile") ??
                                       SupportText(manager, id, "RelativeFilePath", "DigitalTerrainModel_RelativeFilePath"),
                FileProxy = TerrainText(element, "FileProxy") ??
                            SupportText(manager, id, "FileProxy", "DigitalTerrainModel_FileProxy")
            };

        // Reads the model's two-dimensional data sources. These are support elements, not domain
        // elements, and they name their file through the same document-specification field shape the
        // terrain uses. Read-only: nothing is written and no reference is changed.
        private static GeneralTerrainDependency[] DataSourceDependencies(object dataset, out bool managerResolved)
        {
            var found = new List<GeneralTerrainDependency>();
            object manager = SupportElementManager(dataset, "TwoDDataSource");
            managerResolved = manager != null;
            if (manager == null) return found.ToArray();
            foreach (int id in ElementIdsOf(manager).Take(MaxTerrainElements))
            {
                object element = ElementById(manager, id);
                if (element == null) continue;
                found.Add(DataSourceDependency(manager, element, id));
            }
            return found.ToArray();
        }

        private static GeneralTerrainDependency DataSourceDependency(object manager, object element, int id) =>
            new GeneralTerrainDependency
            {
                ElementId = id,
                ElementLabel = TerrainText(element, "Label") ?? SupportText(manager, id, "Label"),
                IsActive = TerrainBool(element, "IsActive") ?? SupportBool(manager, id, "IsActive"),
                IsChecked = TerrainBool(element, "IsChecked") ?? SupportBool(manager, id, "IsChecked"),
                SourceType = TerrainText(element, "SourceShape") ??
                             SupportText(manager, id, "TwoDDataSource_SourceShape"),
                DeclaredAbsolutePath = TerrainText(element, "FilePath", "File") ??
                                       SupportText(manager, id, "FilePath", "TwoDDataSource_FilePath"),
                DeclaredRelativePath = TerrainText(element, "RelativeFilePath", "RelativeFile") ??
                                       SupportText(manager, id, "RelativeFilePath",
                                           "TwoDDataSource_FileRelativePath"),
                FileProxy = TerrainText(element, "FileProxy") ??
                            SupportText(manager, id, "FileProxy", "TwoDDataSource_FileProxy")
            };

        // A terrain model is a support element, and its file references are support-element fields
        // rather than plain properties. They are read through the same support-field accessor the
        // proven catalog read uses.
        private static object SupportField(object manager, string name)
        {
            try
            {
                Type contract = Contract("Haestad.Domain", "Haestad.Domain.ISupportElementManager");
                if (manager == null || !contract.IsInstanceOfType(manager)) return null;
                MethodInfo method = contract.GetMethod("SupportElementField", new[] { typeof(string) });
                if (method == null) return null;
                return method.Invoke(manager, new object[] { name });
            }
            catch (Exception) { return null; }
        }

        private static object SupportFieldValue(object field, int elementId)
        {
            if (field == null) return null;
            try
            {
                MethodInfo get = field.GetType().GetMethod("GetValue", new[] { typeof(int) });
                if (get == null || get.ReturnType == typeof(void)) return null;
                return get.Invoke(field, new object[] { elementId });
            }
            catch (Exception) { return null; }
        }

        private static string SupportText(object manager, int elementId, params string[] names)
        {
            foreach (string name in names)
            {
                object value = SupportFieldValue(SupportField(manager, name), elementId);
                if (value == null) continue;
                string text = Bound(Convert.ToString(value, CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(text)) return text;
            }
            return null;
        }

        private static bool? SupportBool(object manager, int elementId, params string[] names)
        {
            foreach (string name in names)
            {
                object value = SupportFieldValue(SupportField(manager, name), elementId);
                if (value == null) continue;
                try { return Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
                catch (Exception) { }
            }
            return null;
        }

        private static string[] SupportFieldDump(object manager, int elementId)
        {
            var lines = new List<string>();
            foreach (string name in TerrainFieldNames)
            {
                object field = SupportField(manager, name);
                if (field == null) { lines.Add("FIELD:" + name + "=<absent>"); continue; }
                object value = SupportFieldValue(field, elementId);
                lines.Add("FIELD:" + name + "=" +
                    (value == null ? "<null>" : Bound(Convert.ToString(value, CultureInfo.InvariantCulture))));
                if (name.IndexOf("FilePath", StringComparison.OrdinalIgnoreCase) >= 0)
                    lines.Add("FIELDAPI:" + name + "=" + FieldWriters(field));
                // Diagnostic only: records the shape of the stored document-specification proxy so a
                // correction can name a supported property instead of guessing one.
                if (name.IndexOf("FileProxy", StringComparison.OrdinalIgnoreCase) >= 0 && value != null)
                    lines.Add("FIELDSHAPE:" + name + "=" + DescribeShape(value, 0));
            }
            return lines.ToArray();
        }

        // Reports the writer surface of one support field, so a rebind can be attempted only where
        // the vendor actually offers a setter rather than by guessing a method name.
        private static string FieldWriters(object field)
        {
            try
            {
                return string.Join(",", field.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(method => !method.IsSpecialName && method.GetParameters().Length > 0 &&
                        (method.Name.IndexOf("Set", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         method.Name.IndexOf("Value", StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(method => method.Name + "(" +
                        string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + ")")
                    .Distinct(StringComparer.Ordinal).Take(12).ToArray());
            }
            catch (Exception) { return ""; }
        }

        // Element property names are either bare or prefixed with the element type name
        // ("DigitalTerrainModel_FilePath"), so the suffix after the last underscore is what is matched.
        private static PropertyInfo TerrainProperty(object element, string[] names)
        {
            foreach (Type contract in WaterObjectsLifecycleHost.Hierarchy(element.GetType()))
            {
                PropertyInfo[] properties;
                try { properties = contract.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly); }
                catch (Exception) { continue; }
                foreach (PropertyInfo property in properties)
                {
                    if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                    string name = property.Name;
                    int underscore = name.LastIndexOf('_');
                    if (underscore >= 0 && underscore + 1 < name.Length) name = name.Substring(underscore + 1);
                    if (names.Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)))
                        return property;
                }
            }
            return null;
        }

        private static string TerrainText(object element, params string[] names)
        {
            try
            {
                PropertyInfo property = TerrainProperty(element, names);
                if (property == null) return null;
                object value = property.GetValue(element, null);
                if (value == null) return null;
                return Bound(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            catch (Exception) { return null; }
        }

        private static bool? TerrainBool(object element, params string[] names)
        {
            try
            {
                PropertyInfo property = TerrainProperty(element, names);
                if (property == null) return null;
                object value = property.GetValue(element, null);
                return value == null ? (bool?)null : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch (Exception) { return null; }
        }

        // A bounded, best-effort record of what the vendor exposes for one terrain element. It is
        // diagnostic only: it never decides an outcome, it explains one.
        private static string[] DescribeProperties(object element)
        {
            var lines = new List<string>();
            foreach (Type contract in WaterObjectsLifecycleHost.Hierarchy(element.GetType()))
            {
                PropertyInfo[] properties;
                try { properties = contract.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly); }
                catch (Exception) { continue; }
                foreach (PropertyInfo property in properties)
                {
                    if (lines.Count >= MaxTerrainPropertyDump) return lines.ToArray();
                    if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                    string text;
                    try { text = Bound(Convert.ToString(property.GetValue(element, null), CultureInfo.InvariantCulture)) ?? ""; }
                    catch (Exception error) { text = "<" + Unwrap(error).GetType().Name + ">"; }
                    lines.Add(property.Name + "=" + text);
                }
            }
            return lines.ToArray();
        }

        // The active terrain reference must resolve to a file that is physically inside this copy.
        // The stored absolute path is a location on the authoring machine and is never trusted; only
        // a path confined to the working copy can satisfy the requirement.
        private static bool VerifyTerrainResolution(object dataset, string workingDirectory,
            GeneralCalculationObservation seen)
        {
            object manager = TerrainManager(dataset);
            if (manager == null)
            {
                seen.TerrainRequirement = "NONE_DECLARED";
                return true;
            }
            var active = new List<GeneralTerrainDependency>();
            foreach (int id in ElementIdsOf(manager).Take(MaxTerrainElements))
            {
                object element = ElementById(manager, id);
                if (element == null) continue;
                GeneralTerrainDependency dependency = TerrainDependency(manager, element, id);
                if (dependency.IsActive == false) continue;
                active.Add(dependency);
            }
            seen.TerrainDependencies = active.ToArray();
            if (active.Count == 0)
            {
                seen.TerrainRequirement = "NONE_ACTIVE";
                return true;
            }
            string root = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var resolved = new List<string>();
            foreach (GeneralTerrainDependency dependency in active)
            {
                string found = ResolveTerrainInside(root, dependency);
                if (found == null)
                {
                    seen.Status = "TERRAIN_DEPENDENCY_UNRESOLVED";
                    seen.TerrainRequirement = "UNRESOLVED";
                    seen.FailureMessage = "The model declares an active external terrain input that is not present in the " +
                        "disposable copy, so no solve was attempted. Declared path: " +
                        (dependency.DeclaredRelativePath ?? dependency.DeclaredAbsolutePath ?? "(none)");
                    return false;
                }
                resolved.Add(found);
            }
            seen.TerrainRequirement = "RESOLVED";
            seen.ResolvedTerrainPaths = resolved.ToArray();
            return true;
        }

        // ------------------------------------------------------------------ computational grid

        // Read-only: resolves the vendor grid surface and the model's active grid identity and records
        // them. Nothing is generated, saved or solved here, so it can run before the calculation budget
        // is committed.
        private static void GridPreflight(object dataset, object project, ModelReadEvidence evidence)
        {
            try
            {
                Type twoDType = VendorType("Haestad.StormSewerProduct",
                    "Haestad.StormSewerProduct.Support.ITwoDProject");
                Type generatorType = VendorType("Haestad.StormSewerProduct",
                    "Haestad.StormSewerProduct.Support.TwoD.ComputationalGridGenerator");
                Type validatorType = VendorType("Haestad.StormSewerProduct",
                    "Haestad.StormSewerProduct.Support.TwoD.GridGenerationValidator");
                Type contextType = VendorType("Haestad.TwoD", "Haestad.TwoD.Grids.CreateGridExecutionContext");
                Type libraryType = VendorType("Haestad.StormSewerProduct",
                    "Haestad.StormSewerProduct.Library.TwoD.TwoDLibrary");

                evidence.Diagnostics["GridPreflightApi"] = "ComputationalGridGenerator and GridGenerationValidator resolved";
                evidence.Diagnostics["GridPreflightProjectType"] =
                    project == null ? "(none)" : project.GetType().FullName;
                bool isTwoD = project != null && twoDType.IsInstanceOfType(project);
                evidence.Diagnostics["GridPreflightProjectIsTwoD"] = isTwoD ? "true" : "false";
                evidence.Diagnostics["GridPreflightContextMembers"] = string.Join(",", Enum.GetNames(contextType));
                evidence.Diagnostics["GridPreflightSignatures"] =
                    "generatorCtor=" + (generatorType.GetConstructor(new[] { twoDType }) != null) +
                    ";generate=" + (generatorType.GetMethod("GenerateComputationalGrid",
                        new[] { typeof(int), validatorType }) != null) +
                    ";validatorCtor=" + (validatorType.GetConstructor(new[] { twoDType, contextType }) != null);

                object gridManager = ElementManager(dataset, "Grid");
                List<int> ids = ElementIdsOf(gridManager);
                evidence.Diagnostics["GridPreflightGridManager"] =
                    gridManager == null ? "(absent)" : gridManager.GetType().Name;
                evidence.Diagnostics["GridPreflightGridElementIds"] = string.Join(",", ids);

                if (!isTwoD) return;
                MethodInfo active = libraryType.GetMethod("GetRealActiveGridID",
                    BindingFlags.Public | BindingFlags.Static, null, new[] { twoDType }, null);
                object id = active == null ? null : active.Invoke(null, new[] { project });
                evidence.Diagnostics["GridPreflightActiveGridId"] = id == null ? "(unresolved)" : id.ToString();
            }
            catch (Exception error)
            {
                Exception exact = Unwrap(error);
                evidence.Diagnostics["GridPreflightError"] = exact.GetType().Name + ": " + Bound(exact.Message);
            }
        }

        // Produces the grid file the engine actually opens. Every part of the call is resolved from the
        // installed vendor surface: the two-dimensional project comes from the opened project itself,
        // the grid identity from the vendor's own active-grid resolver, and the execution context from
        // the vendor's own enumeration. Nothing about the grid is assumed or written by hand.
        // Unlike the general reporting enumerator, absence here must be proved, never inferred from
        // a null collection, a failed read, a truncated list or an invalid entry.
        internal static int[] ConfiguredGridIds(object manager)
        {
            if (manager == null) throw new InvalidDataException("The configured-grid manager is unavailable.");
            object collection = WaterObjectsLifecycleHost.ElementIDs(manager.GetType()).Invoke(manager, null);
            if (collection == null) throw new InvalidDataException("The configured-grid collection is unavailable.");
            PropertyInfo countProperty = collection.GetType().GetProperty("Count");
            if (countProperty == null || countProperty.PropertyType != typeof(int))
                throw new InvalidDataException("The configured-grid count is unavailable.");
            int count = (int)countProperty.GetValue(collection, null);
            if (count < 0 || count > MaxConduits)
                throw new InvalidDataException("The configured-grid count is invalid or exceeds the inspection bound.");
            PropertyInfo indexer = WaterObjectsLifecycleHost.Indexer(collection.GetType());
            var ids = new HashSet<int>();
            for (int index = 0; index < count; index++)
            {
                object value = indexer.GetValue(collection, new object[] { index });
                if (!(value is int id) || id <= 0 || !ids.Add(id))
                    throw new InvalidDataException("The configured-grid inventory contains an invalid or duplicate identity.");
            }
            return ids.OrderBy(id => id).ToArray();
        }

        internal static bool GridPreparationRequired(int? activeGridId, int[] configuredGridIds)
        {
            if (!activeGridId.HasValue || activeGridId.Value < 0 || configuredGridIds == null ||
                configuredGridIds.Length > MaxConduits || configuredGridIds.Any(id => id <= 0) ||
                configuredGridIds.Distinct().Count() != configuredGridIds.Length)
                throw new InvalidDataException("The model's grid requirement could not be established safely.");
            if (activeGridId.Value == 0 && configuredGridIds.Length == 0) return false;
            if (activeGridId.Value <= 0 || !configuredGridIds.Contains(activeGridId.Value))
                throw new InvalidDataException("The configured grid has no valid active identity in the model inventory.");
            return true;
        }

        private static bool GenerateComputationalGridStep(object dataset, object project, GeneralCalculationObservation seen)
        {
            seen.GridRequirement = "UNRESOLVED";
            try
            {
                Type twoDType = VendorType("Haestad.StormSewerProduct",
                    "Haestad.StormSewerProduct.Support.ITwoDProject");
                Type libraryType = VendorType("Haestad.StormSewerProduct",
                    "Haestad.StormSewerProduct.Library.TwoD.TwoDLibrary");

                seen.GridProjectType = project == null ? "(none)" : project.GetType().FullName;
                if (project == null || !twoDType.IsInstanceOfType(project))
                {
                    seen.GridGenerationStatus = "GRID_PROJECT_NOT_TWOD";
                    seen.FailureMessage = "The opened project does not expose the vendor two-dimensional project " +
                        "surface, so the computational grid cannot be generated through a supported call.";
                    return false;
                }

                MethodInfo activeGrid = libraryType.GetMethod("GetRealActiveGridID",
                    BindingFlags.Public | BindingFlags.Static, null, new[] { twoDType }, null);
                if (activeGrid == null || activeGrid.ReturnType != typeof(int))
                {
                    seen.GridGenerationStatus = "GRID_ACTIVE_ID_UNRESOLVED";
                    seen.FailureMessage = "The vendor active-grid resolver is unavailable, so the grid to generate " +
                        "cannot be established without guessing.";
                    return false;
                }
                int gridId = (int)activeGrid.Invoke(null, new[] { project });
                seen.GridElementId = gridId;
                seen.ConfiguredGridElementIds = ConfiguredGridIds(ElementManager(dataset, "Grid"));
                if (!GridPreparationRequired(gridId, seen.ConfiguredGridElementIds))
                {
                    seen.GridRequirement = "NOT_REQUIRED_NO_CONFIGURED_GRID";
                    seen.GridGenerationStatus = "GRID_NOT_REQUIRED";
                    return true;
                }
                seen.GridRequirement = "REQUIRED";
                if (gridId <= 0)
                {
                    seen.GridGenerationStatus = "GRID_NOT_ACTIVE";
                    seen.FailureMessage = "The model has no active grid element to generate.";
                    return false;
                }

                Type generatorType = VendorType("Haestad.StormSewerProduct",
                    "Haestad.StormSewerProduct.Support.TwoD.ComputationalGridGenerator");
                Type validatorType = VendorType("Haestad.StormSewerProduct",
                    "Haestad.StormSewerProduct.Support.TwoD.GridGenerationValidator");
                Type contextType = VendorType("Haestad.TwoD", "Haestad.TwoD.Grids.CreateGridExecutionContext");
                object context = WaterObjectsLifecycleHost.EnumMember(contextType, "ComputingScenario");
                if (context == null)
                {
                    seen.GridGenerationStatus = "GRID_CONTEXT_UNAVAILABLE";
                    seen.FailureMessage = "The vendor grid execution context has no ComputingScenario member.";
                    return false;
                }
                seen.GridGenerationContext = context.ToString();

                ConstructorInfo validatorCtor = validatorType.GetConstructor(new[] { twoDType, contextType });
                ConstructorInfo generatorCtor = generatorType.GetConstructor(new[] { twoDType });
                MethodInfo generate = generatorType.GetMethod("GenerateComputationalGrid",
                    new[] { typeof(int), validatorType });
                if (validatorCtor == null || generatorCtor == null || generate == null ||
                    generate.ReturnType != typeof(bool))
                {
                    seen.GridGenerationStatus = "GRID_API_UNSUPPORTED";
                    seen.FailureMessage = "The installed vendor build does not expose the expected computational " +
                        "grid generation contract, so nothing was generated and no solve was attempted.";
                    return false;
                }

                object validator = validatorCtor.Invoke(new[] { project, context });
                object generator = generatorCtor.Invoke(new[] { project });
                bool generated;
                try
                {
                    generated = (bool)RuntimeStageJournal.Call("ComputationalGridGeneration",
                        () => generate.Invoke(generator, new object[] { gridId, validator }),
                        value => value);
                }
                catch (Exception error)
                {
                    Exception exact = Unwrap(error);
                    seen.GridGenerationStatus = "GRID_GENERATION_FAILED";
                    seen.GridErrorType = exact.GetType().FullName;
                    seen.GridErrorMessage = Bound(exact.Message);
                    seen.GridNotifications = GridNotifications(validator);
                    seen.FailureMessage = "The computational grid could not be generated: " + Bound(exact.Message);
                    return false;
                }

                seen.GridGenerated = generated;
                seen.GridNotifications = GridNotifications(validator);
                seen.GridCanBeComputed = GridCanBeComputed(project, twoDType);
                seen.GridFiles = GridFiles();

                // Computability must be affirmatively true. A null or unavailable value is not evidence
                // that the grid can be computed, and an unresolved external-layer notification is a
                // defect the vendor has already named, so neither may reach the solver.
                bool unresolvedLayer = seen.GridNotifications.Any(item => item != null &&
                    item.IndexOf("GridDataExternalLayerFileIsMissing", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!generated || seen.GridCanBeComputed != true || unresolvedLayer)
                {
                    seen.GridGenerationStatus = "GRID_NOT_COMPUTABLE";
                    string reason = !generated
                        ? "The vendor reported that the computational grid was not generated."
                        : unresolvedLayer
                            ? "The vendor still reports an external grid data layer file as missing."
                            : seen.GridCanBeComputed == false
                                ? "The vendor reported the computational grid as not computable."
                                : "The vendor did not report the computational grid as computable.";
                    seen.FailureMessage = reason + " No solve was attempted." +
                        (seen.GridNotifications.Length == 0 ? "" : " " + string.Join(" | ", seen.GridNotifications));
                    return false;
                }
                if (seen.GridFiles.Length == 0)
                {
                    seen.GridGenerationStatus = "GRID_OUTPUT_MISSING";
                    seen.FailureMessage = "The generator reported success but produced no grid data file inside " +
                        "this run's private scratch root, so no solve was attempted.";
                    return false;
                }
                seen.GridGenerationStatus = "GRID_GENERATED";
                return true;
            }
            catch (Exception error)
            {
                Exception exact = Unwrap(error);
                seen.GridGenerationStatus = "GRID_GENERATION_UNSUPPORTED";
                seen.GridErrorType = exact.GetType().FullName;
                seen.GridErrorMessage = Bound(exact.Message);
                seen.FailureMessage = "The computational grid step could not be established safely: " +
                    Bound(exact.Message);
                return false;
            }
        }

        // Resolves a vendor type from the already loaded runtime, and only falls back to the installed
        // directory of that same runtime. No other assembly location is ever considered.
        private static Type VendorType(string assembly, string name)
        {
            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (loaded.IsDynamic || loaded.GetName().Name != assembly) continue;
                Type found = loaded.GetType(name, false, false);
                if (found != null) return found;
            }
            string root = VendorRoot();
            string candidate = Path.Combine(root, assembly + ".dll");
            if (!File.Exists(candidate))
                throw new MissingMemberException("The installed vendor assembly is unavailable: " + assembly);
            Type resolved = Assembly.LoadFrom(candidate).GetType(name, false, false);
            if (resolved == null)
                throw new MissingMemberException("The installed vendor contract is unavailable: " +
                    assembly + "!" + name);
            return resolved;
        }

        private static string VendorRoot()
        {
            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (loaded.IsDynamic || loaded.GetName().Name != "Haestad.Support") continue;
                if (!string.IsNullOrEmpty(loaded.Location)) return Path.GetDirectoryName(loaded.Location);
            }
            throw new InvalidOperationException("The installed vendor runtime directory is unknown.");
        }

        // The validator collects the vendor's own warnings and errors. They are reported, never
        // interpreted as success.
        private static string[] GridNotifications(object validator)
        {
            var lines = new List<string>();
            if (validator == null) return lines.ToArray();
            try
            {
                PropertyInfo list = validator.GetType().GetProperty("UserNotificationList");
                object value = list == null ? null : list.GetValue(validator, null);
                var items = value as System.Collections.IEnumerable;
                if (items == null) return lines.ToArray();
                foreach (object item in items)
                {
                    if (item == null || lines.Count >= 8) break;
                    string message = null;
                    try
                    {
                        PropertyInfo text = item.GetType().GetProperty("Message");
                        message = text == null ? item.ToString() : text.GetValue(item, null) as string;
                    }
                    catch (Exception) { message = null; }
                    if (!string.IsNullOrWhiteSpace(message)) lines.Add(Bound(message));
                }
            }
            catch (Exception) { }
            return lines.ToArray();
        }

        private static bool? GridCanBeComputed(object project, Type twoDType)
        {
            try
            {
                PropertyInfo active = twoDType.GetProperty("ActiveComputationalGrid");
                object grid = active == null ? null : active.GetValue(project, null);
                if (grid == null) return null;
                PropertyInfo flag = grid.GetType().GetProperty("CanBeComputed");
                if (flag == null || flag.PropertyType != typeof(bool)) return null;
                return (bool)flag.GetValue(grid, null);
            }
            catch (Exception) { return null; }
        }

        // The grid files this run produced. The search is confined to the process's own temporary root,
        // which the launcher set to a private per-run directory, so another run's grid can never be
        // mistaken for this one's output.
        private static string[] GridFiles()
        {
            var found = new List<string>();
            try
            {
                string root = Path.GetTempPath();
                foreach (string file in Directory.GetFiles(root, "ActiveGrid*.dat", SearchOption.AllDirectories)
                             .Take(8))
                {
                    var info = new FileInfo(file);
                    found.Add(RelativeInside(root, file) + "|" + info.Length.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (Exception) { }
            return found.ToArray();
        }

        private static string ResolveTerrainInside(string root, GeneralTerrainDependency dependency)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(dependency.DeclaredAbsolutePath))
                candidates.Add(dependency.DeclaredAbsolutePath);
            if (!string.IsNullOrWhiteSpace(dependency.DeclaredRelativePath))
                candidates.Add(Path.Combine(root, dependency.DeclaredRelativePath));
            string basename = FileName(dependency.DeclaredAbsolutePath) ?? FileName(dependency.DeclaredRelativePath);
            if (basename != null) candidates.Add(Path.Combine(root, basename));
            foreach (string candidate in candidates)
            {
                string full;
                try { full = Path.GetFullPath(candidate); }
                catch (Exception) { continue; }
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(full)) continue;
                return full;
            }
            return null;
        }

        internal static string FileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string trimmed = path.Trim().TrimEnd('\\', '/');
            int separator = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            string name = separator >= 0 ? trimmed.Substring(separator + 1) : trimmed;
            return name.Length == 0 ? null : name;
        }

        // ------------------------------------------------------------------ helpers

        private static Type Contract(string assembly, string name)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && a.GetName().Name == assembly).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Exact loaded contract assembly unavailable: " + assembly);
            return matches[0].GetType(name, true, false);
        }

        private static Exception Unwrap(Exception error)
        {
            var current = error;
            for (int depth = 0; depth < 8 && current is TargetInvocationException && current.InnerException != null; depth++)
                current = current.InnerException;
            return current;
        }

        private static string Bound(string text) =>
            text == null ? null : (text.Length <= MaxTextLength ? text : text.Substring(0, MaxTextLength));
    }

    // Observation of one general calculation attempt. Public properties only: the runtime protocol
    // serializes this with JavaScriptSerializer.
    internal sealed class GeneralCalculationObservation
    {
        public string Status { get; set; }
        public string FailureMessage { get; set; }
        public string FailureOperation { get; set; }
        public string ErrorType { get; set; }
        public int ErrorHResult { get; set; }
        public string ErrorMessage { get; set; }

        public string OpenedModelPath { get; set; }
        public string WorkingCopyId { get; set; }
        public int? ActiveScenarioId { get; set; }
        public int? ScenarioId { get; set; }
        public int? PhysicalAlternativeId { get; set; }
        public int? CalculationOptionsId { get; set; }
        public string Engine { get; set; }
        public string ResolvedEngine { get; set; }
        public string EngineRuntimeType { get; set; }
        public string MainEngineFromModel { get; set; }
        public string[] ModelEngineTypeNames { get; set; }
        public string[] EngineCandidates { get; set; }
        public string EngineSelectionBasis { get; set; }

        public string CalculationEventId { get; set; }
        public string SolverStartTimestamp { get; set; }
        public string SolverEndTimestamp { get; set; }
        public string SaveTimestamp { get; set; }
        public int SolverRunInvocations { get; set; }
        public int SaveInvocations { get; set; }
        public string ScenarioComputeStatus { get; set; }
        public string ScenarioStatusText { get; set; }
        public string EngineOutcome { get; set; }

        // Engine warnings are a first-class outcome, not a footnote. The vendor raises
        // EngineWarningsException with a notification collection; that collection is the only place the
        // individual warnings are ever named, so it is read while the exception is still in hand.
        public string WarningExceptionType { get; set; }
        public int? WarningNotificationCount { get; set; }
        public string WarningCaptureStatus { get; set; }
        public string WarningCaptureFailure { get; set; }
        public GeneralEngineWarning[] Warnings { get; set; }

        // The computational grid is generated in this same host process, under this process's private
        // scratch root, immediately before the solve, because the engine reads a grid file rather than
        // building one itself.
        public string GridGenerationStatus { get; set; }
        public string GridRequirement { get; set; }
        public int[] ConfiguredGridElementIds { get; set; }
        public int? GridElementId { get; set; }
        public string GridProjectType { get; set; }
        public string GridGenerationContext { get; set; }
        public bool GridGenerated { get; set; }
        public bool? GridCanBeComputed { get; set; }
        public string[] GridNotifications { get; set; }
        public string[] GridFiles { get; set; }
        public string GridErrorType { get; set; }
        public string GridErrorMessage { get; set; }
        public string NotificationCaptureLimitation { get; set; }

        public string InputInventoryDigest { get; set; }
        public string OutputInventoryJson { get; set; }
        public string[] CreatedFiles { get; set; }
        public string[] ModifiedFiles { get; set; }

        public string PreSaveStatus { get; set; }
        public string SaveResultClassification { get; set; }
        public bool SaveSucceeded { get; set; }
        public int SaveQuestionCount { get; set; }
        public bool? ProjectIsDirtyAfterSave { get; set; }
        public bool ResultCleanupCompleted { get; set; }

        // Declared external inputs: what the model requires beside it, and what was actually found
        // inside the disposable copy before the engine was launched.
        public string TerrainRequirement { get; set; }
        public GeneralTerrainDependency[] TerrainDependencies { get; set; }
        public string[] ResolvedTerrainPaths { get; set; }
    }

    // One external input a model declares for itself. These values are read from the model, never
    // assumed: they are what the vendor reports, not what a caller expects.
    // One verified Shapefile family the caller has already placed inside the working copy.
    internal sealed class DataSourceManifestEntry
    {
        public string RelativePath { get; set; }
        public string[] SHA256 { get; set; }
    }

    // The verified family a data source resolves to inside the working copy.
    internal sealed class DataSourceResolution
    {
        public string Path { get; set; }
        public string[] SHA256 { get; set; }
        public bool FamilyVerified { get; set; }
    }

    // What one binding pass did to one two-dimensional data source. Both sides of every write are
    // recorded, so a stale proxy cannot be reported as corrected on the strength of a setter's return.
    internal sealed class GeneralDataSourceBinding
    {
        public int ElementId { get; set; }
        public string DeclaredRelativePath { get; set; }
        public string ProxyPathBefore { get; set; }
        public string ProxyPathAfter { get; set; }
        public string TargetPath { get; set; }
        public string TargetRelativePath { get; set; }
        public bool TargetExists { get; set; }
        public string[] SHA256 { get; set; }
        public bool FamilyVerified { get; set; }
        public bool ProxyIndependent { get; set; }
        public bool ProxyWritten { get; set; }
        public bool RelativePathWritten { get; set; }
        public string[] Diagnostics { get; set; }
    }

    // What a fresh process sees for one data source after reopening the persisted copy.
    internal sealed class GeneralDataSourceVerification
    {
        public int ElementId { get; set; }
        public string DeclaredRelativePath { get; set; }
        public string ProxyPath { get; set; }
        public string TargetPath { get; set; }
        public string TargetRelativePath { get; set; }
        public bool TargetExists { get; set; }
        public string[] SHA256 { get; set; }
        public bool FamilyVerified { get; set; }
        public bool ProxyInsideCopy { get; set; }
        public bool RelativePathResolves { get; set; }
        public bool ExternalPathRequired { get; set; }
    }

    internal sealed class GeneralTerrainDependency
    {
        public int ElementId { get; set; }
        public string ElementLabel { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsChecked { get; set; }
        public string SourceType { get; set; }
        public string DeclaredAbsolutePath { get; set; }
        public string DeclaredRelativePath { get; set; }
        public string FileProxy { get; set; }
    }

    internal sealed class GeneralTerrainObservation
    {
        public string Status { get; set; }
        public string FailureMessage { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public string OpenedModelPath { get; set; }
        public string WorkingCopyId { get; set; }
        public bool TerrainManagerResolved { get; set; }
        public string TerrainManagerName { get; set; }
        public int TerrainElementCount { get; set; }
        public string[] AvailableManagerNames { get; set; }
        public GeneralTerrainDependency[] Dependencies { get; set; }
        // Two-dimensional data sources are separate support elements that name their own external
        // files. They are reported here so the copy step never has to guess a file name or an id.
        public bool DataSourceManagerResolved { get; set; }
        public GeneralTerrainDependency[] DataSources { get; set; }
        public string[] ElementPropertyDump { get; set; }
    }

    // What one binding attempt did. Both sides of the write are recorded so the change is auditable
    // without trusting the setter's own return value.
    internal sealed class GeneralTerrainBindObservation
    {
        public string Status { get; set; }
        public string FailureMessage { get; set; }
        public string FailureOperation { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }

        public string OpenedModelPath { get; set; }
        public string WorkingCopyId { get; set; }
        public string TargetPath { get; set; }
        public string TargetRelativePath { get; set; }
        public int TerrainElementCount { get; set; }
        public int ElementId { get; set; }
        public GeneralTerrainDependency Before { get; set; }
        public GeneralTerrainDependency After { get; set; }

        public bool FilePathWritten { get; set; }
        public bool RelativePathWritten { get; set; }
        public string[] FieldDiagnostics { get; set; }

        // The file proxy is stored independently of the file path on this vendor build, so it is
        // observed and corrected on its own terms rather than assumed to follow.
        public string ProxyTypeName { get; set; }
        public string ProxyPathBefore { get; set; }
        public string ProxyPathAfter { get; set; }
        public bool ProxyIndependent { get; set; }
        public bool ProxyFieldWritten { get; set; }
        public string[] ProxyDiagnostics { get; set; }

        // Two-dimensional data sources are corrected on the same pass, so their outcome is recorded
        // here rather than in a second phase with a second save.
        public int DataSourceElementCount { get; set; }
        public GeneralDataSourceBinding[] DataSources { get; set; }
        public bool DataSourceReferencesCorrected { get; set; }

        public bool SaveSucceeded { get; set; }
        public string SaveResultClassification { get; set; }
        public int SaveQuestionCount { get; set; }
        public string SaveTimestamp { get; set; }
        public string[] CreatedFiles { get; set; }
        public string[] ModifiedFiles { get; set; }
    }

    // What a fresh process sees after reopening the persisted copy. Every field is an observation,
    // never an assertion: the verdict is derived from these values by the caller.
    internal sealed class GeneralTerrainVerifyObservation
    {
        public string Status { get; set; }
        public string FailureMessage { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }

        public string OpenedModelPath { get; set; }
        public string WorkingCopyId { get; set; }
        public string TargetPath { get; set; }
        public string TargetRelativePath { get; set; }
        public int TerrainElementCount { get; set; }
        public int ElementId { get; set; }
        public GeneralTerrainDependency Reference { get; set; }

        public string ProxyPath { get; set; }
        public bool TargetExists { get; set; }
        public string TargetSHA256 { get; set; }
        public bool AbsolutePathMatchesTarget { get; set; }
        public bool RelativePathResolves { get; set; }
        public bool AuthoringPathStillRequired { get; set; }

        // The reopened copy must also prove its two-dimensional data sources point inside itself.
        public GeneralDataSourceVerification[] DataSources { get; set; }
        public bool DataSourceReferencesVerified { get; set; }
    }

    internal sealed class GeneralPreSaveRecord
    {
        public string Status { get; set; }
        public string CalculationEventId { get; set; }
        public int? ScenarioId { get; set; }
        public int? PhysicalAlternativeId { get; set; }
        public int? CalculationOptionsId { get; set; }
        public string Engine { get; set; }
        public string ScenarioComputeStatus { get; set; }
        public string SolverStartTimestamp { get; set; }
        public string SolverEndTimestamp { get; set; }
        public int SolverRunInvocations { get; set; }
    }

    internal sealed class GeneralReadbackObservation
    {
        public string Status { get; set; }
        public string FailureMessage { get; set; }
        public string FailureOperation { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }

        public string OpenedModelPath { get; set; }
        public int? ScenarioId { get; set; }
        public int? ActiveScenarioId { get; set; }
        public int? CalculationOptionsId { get; set; }
        public string Engine { get; set; }
        public string CalculationEventId { get; set; }
        public bool HasResults { get; set; }
        public int ConduitCount { get; set; }
        public int StructureCount { get; set; }
        public int RowCount { get; set; }
        public int AvailableRowCount { get; set; }
        public string ReopenedInventoryDigest { get; set; }
        public string Provenance { get; set; }
        public GeneralHydraulicResultRow[] Rows { get; set; }
    }

    // One warning the engine reported. Every member is read defensively through reflection because
    // the vendor contract is only known by name, and a missing member must degrade to UNKNOWN rather
    // than be reported as a warning-free run.
    internal sealed class GeneralEngineWarning
    {
        public string MessageKey { get; set; }
        public string Level { get; set; }
        public int? ElementId { get; set; }
        public string ElementType { get; set; }
        public int? ScenarioId { get; set; }
        public string MessageId { get; set; }
        public string SourceKey { get; set; }
        public string[] Parameters { get; set; }
    }

    internal sealed class GeneralHydraulicResultRow
    {
        public int ElementId { get; set; }
        public string ElementLabel { get; set; }
        public string Category { get; set; }
        public string Metric { get; set; }
        public string FieldName { get; set; }
        public string RecordName { get; set; }
        public double? Value { get; set; }
        public string RawValue { get; set; }
        public string Unit { get; set; }
        public double? TimeSeconds { get; set; }
        public int? TimeStepIndex { get; set; }
        public string Status { get; set; }
    }
}
