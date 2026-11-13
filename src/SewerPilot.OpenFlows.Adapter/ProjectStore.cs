using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class ProjectStorageException : Exception
    {
        public string Classification { get; }

        public ProjectStorageException(string classification, string message)
            : base(message)
        {
            Classification = classification ?? "PROJECT_STORAGE_ERROR";
        }

        public ProjectStorageException(string classification, string message, Exception innerException)
            : base(message, innerException)
        {
            Classification = classification ?? "PROJECT_STORAGE_ERROR";
        }
    }

    public static class ProjectStore
    {
        public const int SupportedSchemaVersion = 1;

        private static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        public static ProjectState CreateProject(
            string directory,
            string projectName,
            ProjectModule module = ProjectModule.Unselected,
            ProjectModelMetadata model = null,
            ProjectRuntimeMetadata runtime = null,
            ProjectScenarioAlternativeState scenarioState = null,
            DesignBasisReference designBasis = null,
            string designRunId = null)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Project directory must be specified.", nameof(directory));
            if (string.IsNullOrWhiteSpace(projectName))
                throw new ArgumentException("Project name must be specified.", nameof(projectName));

            string fullDir = Path.GetFullPath(directory);
            if (!Directory.Exists(fullDir))
                Directory.CreateDirectory(fullDir);

            string stateFile = Path.Combine(fullDir, "project.json");
            if (File.Exists(stateFile))
                throw new ProjectStorageException("PROJECT_STATE_CONFLICT", "A project state file already exists in directory: " + fullDir);

            string now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var state = new ProjectState
            {
                SchemaVersion = SupportedSchemaVersion,
                ProjectId = Guid.NewGuid().ToString("D"),
                ProjectName = projectName.Trim(),
                StateRevision = 1,
                SelectedModule = ProjectState.FormatModule(module),
                Model = model,
                Runtime = runtime,
                ScenarioAlternativeState = scenarioState,
                DesignBasisReference = designBasis,
                CurrentDesignRunId = designRunId,
                CreatedUtc = now,
                UpdatedUtc = now
            };

            ValidateState(state);
            WriteStateAtomic(fullDir, state);

            var initialAudit = new AuditRecord
            {
                AuditRecordId = Guid.NewGuid().ToString("D"),
                Sequence = 1,
                TimestampUtc = now,
                ProjectId = state.ProjectId,
                ActionType = AuditActionType.ProjectCreated,
                Module = state.SelectedModule,
                ModelFingerprint = model != null ? model.ProtectedBaselineFingerprint : null,
                DesignRunId = designRunId,
                Payload = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { "ProjectName", state.ProjectName }
                }
            };

            AppendAuditRecordUnchecked(fullDir, initialAudit);

            return state;
        }

        public static ProjectState LoadProject(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Project directory must be specified.", nameof(directory));

            string fullDir = Path.GetFullPath(directory);
            string stateFile = Path.Combine(fullDir, "project.json");
            if (!File.Exists(stateFile))
                throw new FileNotFoundException("Project state file not found: " + stateFile, stateFile);

            string json;
            try
            {
                json = File.ReadAllText(stateFile, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                throw new ProjectStorageException("MALFORMED_PROJECT_STATE", "Failed to read project file: " + ex.Message, ex);
            }

            ProjectState state;
            try
            {
                state = Serializer().Deserialize<ProjectState>(json);
            }
            catch (Exception ex)
            {
                throw new ProjectStorageException("MALFORMED_PROJECT_STATE", "Malformed project JSON: " + ex.Message, ex);
            }

            if (state == null)
                throw new ProjectStorageException("MALFORMED_PROJECT_STATE", "Project state deserialized to null.");

            ValidateState(state);

            string auditFile = Path.Combine(fullDir, "audit.jsonl");
            if (File.Exists(auditFile))
            {
                ValidateAndReadAuditLog(auditFile, state.ProjectId);
            }

            return state;
        }

        public static ProjectState SaveProject(string directory, ProjectState state, int expectedRevision, AuditRecord associatedAudit = null)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Project directory must be specified.", nameof(directory));
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            string fullDir = Path.GetFullPath(directory);
            string stateFile = Path.Combine(fullDir, "project.json");
            if (!File.Exists(stateFile))
                throw new FileNotFoundException("Project state file not found for update: " + stateFile, stateFile);

            ProjectState stored;
            try
            {
                string storedJson = File.ReadAllText(stateFile, Encoding.UTF8);
                stored = Serializer().Deserialize<ProjectState>(storedJson);
            }
            catch (Exception ex)
            {
                throw new ProjectStorageException("MALFORMED_PROJECT_STATE", "Failed to inspect stored state revision: " + ex.Message, ex);
            }

            if (stored == null)
                throw new ProjectStorageException("MALFORMED_PROJECT_STATE", "Stored state deserialized to null.");

            if (stored.StateRevision != expectedRevision)
            {
                throw new ProjectStorageException("PROJECT_STATE_CONFLICT",
                    string.Format(CultureInfo.InvariantCulture, "PROJECT_STATE_CONFLICT: Expected revision {0} but stored revision is {1}.", expectedRevision, stored.StateRevision));
            }

            ValidateState(state);

            state.StateRevision = expectedRevision + 1;
            state.UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            WriteStateAtomic(fullDir, state);

            if (associatedAudit != null)
            {
                try
                {
                    associatedAudit.ProjectId = state.ProjectId;
                    AppendAudit(fullDir, associatedAudit);
                }
                catch (Exception ex)
                {
                    throw new ProjectStorageException("STATE_SAVED_AUDIT_APPEND_FAILED",
                        string.Format(CultureInfo.InvariantCulture, "STATE_SAVED_AUDIT_APPEND_FAILED: State revision {0} saved but audit append failed: {1}", state.StateRevision, ex.Message),
                        ex);
                }
            }

            return state;
        }

        public static AuditRecord AppendAudit(string directory, AuditRecord record)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Project directory must be specified.", nameof(directory));
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            string fullDir = Path.GetFullPath(directory);
            string stateFile = Path.Combine(fullDir, "project.json");
            if (!File.Exists(stateFile))
                throw new FileNotFoundException("Cannot append audit to non-existent project: " + stateFile, stateFile);

            string projectJson = File.ReadAllText(stateFile, Encoding.UTF8);
            var project = Serializer().Deserialize<ProjectState>(projectJson);
            if (project == null || string.IsNullOrWhiteSpace(project.ProjectId))
                throw new ProjectStorageException("MALFORMED_PROJECT_STATE", "Project state is missing ProjectId.");

            if (string.IsNullOrWhiteSpace(record.ProjectId))
            {
                record.ProjectId = project.ProjectId;
            }
            else if (!string.Equals(record.ProjectId, project.ProjectId, StringComparison.Ordinal))
            {
                throw new ProjectStorageException("AUDIT_PROJECT_ID_MISMATCH",
                    string.Format(CultureInfo.InvariantCulture, "Audit ProjectId mismatch rejected: Record ProjectId '{0}' does not match project ProjectId '{1}'.", record.ProjectId, project.ProjectId));
            }

            if (!AuditActionType.IsValid(record.ActionType))
                throw new ProjectStorageException("MALFORMED_AUDIT_LOG", "Invalid audit action type: " + record.ActionType);

            long lastSequence = GetLastAuditSequence(fullDir);
            long expectedSequence = lastSequence + 1;

            if (record.Sequence <= 0)
            {
                record.Sequence = expectedSequence;
            }
            else if (record.Sequence != expectedSequence)
            {
                throw new ProjectStorageException("MALFORMED_AUDIT_LOG",
                    string.Format(CultureInfo.InvariantCulture, "Audit sequence invalid: expected {0} but got {1}", expectedSequence, record.Sequence));
            }

            if (string.IsNullOrWhiteSpace(record.AuditRecordId))
                record.AuditRecordId = Guid.NewGuid().ToString("D");

            if (string.IsNullOrWhiteSpace(record.TimestampUtc))
                record.TimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            AppendAuditRecordUnchecked(fullDir, record);
            return record;
        }

        public static IReadOnlyList<AuditRecord> ReadAudit(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Project directory must be specified.", nameof(directory));

            string fullDir = Path.GetFullPath(directory);
            string auditFile = Path.Combine(fullDir, "audit.jsonl");
            if (!File.Exists(auditFile))
                return new AuditRecord[0];

            string expectedProjectId = null;
            string stateFile = Path.Combine(fullDir, "project.json");
            if (File.Exists(stateFile))
            {
                try
                {
                    var project = Serializer().Deserialize<ProjectState>(File.ReadAllText(stateFile, Encoding.UTF8));
                    if (project != null)
                        expectedProjectId = project.ProjectId;
                }
                catch (Exception ex)
                {
                    // Fail closed: a corrupt manifest must not silently disable the audit-log
                    // project-identity cross-validation that follows.
                    throw new InvalidDataException(
                        "Corrupted or unreadable project manifest at '" + stateFile + "'.", ex);
                }
            }

            return ValidateAndReadAuditLog(auditFile, expectedProjectId);
        }

        public static long GetLastAuditSequence(string directory)
        {
            string auditPath = Path.Combine(directory, "audit.jsonl");
            if (!File.Exists(auditPath))
                return 0;

            var records = ValidateAndReadAuditLog(auditPath, null);
            return records.Count > 0 ? records[records.Count - 1].Sequence : 0;
        }

        public static void ValidateState(ProjectState state)
        {
            if (state == null)
                throw new ProjectStorageException("MALFORMED_PROJECT_STATE", "Project state is null.");

            if (state.SchemaVersion != SupportedSchemaVersion)
            {
                throw new ProjectStorageException("UNSUPPORTED_PROJECT_SCHEMA",
                    string.Format(CultureInfo.InvariantCulture, "UNSUPPORTED_PROJECT_SCHEMA: Schema version {0} is unsupported.", state.SchemaVersion));
            }

            if (string.IsNullOrWhiteSpace(state.ProjectId) || !Regex.IsMatch(state.ProjectId, @"^[0-9a-fA-F-]{8,40}$"))
            {
                throw new ProjectStorageException("INVALID_PROJECT_IDENTITY", "ProjectId is invalid or empty: " + state.ProjectId);
            }

            if (string.IsNullOrWhiteSpace(state.ProjectName))
            {
                throw new ProjectStorageException("MALFORMED_PROJECT_STATE", "ProjectName is required.");
            }

            if (state.StateRevision < 1)
            {
                throw new ProjectStorageException("MALFORMED_PROJECT_STATE", "StateRevision must be >= 1.");
            }

            try
            {
                ProjectState.ParseModule(state.SelectedModule);
            }
            catch (Exception ex)
            {
                throw new ProjectStorageException("MALFORMED_PROJECT_STATE", "Invalid module value: " + state.SelectedModule, ex);
            }

            if (state.Model != null)
            {
                if (state.Model.ProtectedBaselineFingerprint != null &&
                    !Regex.IsMatch(state.Model.ProtectedBaselineFingerprint, @"^[a-f0-9]{64}$"))
                {
                    throw new ProjectStorageException("MALFORMED_PROJECT_STATE",
                        "Model ProtectedBaselineFingerprint must be a 64-character lowercase hex string: " + state.Model.ProtectedBaselineFingerprint);
                }
            }

            if (state.ScenarioAlternativeState != null)
            {
                if (state.ScenarioAlternativeState.SelectedPhysicalAlternativeId.HasValue &&
                    state.ScenarioAlternativeState.SelectedPhysicalAlternativeId.Value <= 0)
                    throw new ProjectStorageException("MALFORMED_PROJECT_STATE", "Selected Physical alternative ID must be positive when supplied.");
                if (state.ScenarioAlternativeState.ScenarioMappings != null)
                {
                    var seenScenarios = new HashSet<int>();
                    foreach (var sm in state.ScenarioAlternativeState.ScenarioMappings)
                    {
                        if (!seenScenarios.Add(sm.ScenarioId))
                        {
                            throw new ProjectStorageException("DUPLICATE_MAPPING_KEY",
                                string.Format(CultureInfo.InvariantCulture, "Duplicate scenario mapping key rejected: {0}", sm.ScenarioId));
                        }
                    }
                }

                if (state.ScenarioAlternativeState.AlternativeMappings != null)
                {
                    var seenAlts = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var am in state.ScenarioAlternativeState.AlternativeMappings)
                    {
                        string key = am.AlternativeTypeId + ":" + am.AlternativeId;
                        if (!seenAlts.Add(key))
                        {
                            throw new ProjectStorageException("DUPLICATE_MAPPING_KEY",
                                string.Format(CultureInfo.InvariantCulture, "Duplicate alternative mapping key rejected: {0}", key));
                        }
                    }
                }
            }

            AssertNoSecretsInState(state);
        }

        private static readonly string[] SecretKeySubstrings = new[]
        {
            "password", "passwd", "pwd", "apikey", "api_key", "secret", "credential", "auth_token"
        };

        private static void AssertNoSecretsInState(ProjectState state)
        {
            if (state.Runtime != null && state.Runtime.CapabilityStates != null)
            {
                foreach (var key in state.Runtime.CapabilityStates.Keys)
                {
                    CheckSecretField(key);
                    CheckSecretField(state.Runtime.CapabilityStates[key]);
                }
            }
        }

        private static void CheckSecretField(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (var sub in SecretKeySubstrings)
            {
                if (value.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new ProjectStorageException("SECURITY_VIOLATION", "Secret-like material detected in persistent state: " + sub);
            }
        }

        private static void WriteStateAtomic(string directory, ProjectState state)
        {
            string json = Serializer().Serialize(state);
            string finalPath = Path.Combine(directory, "project.json");
            string tempPath = Path.Combine(directory, "project.json." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(fs, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    fs.Flush(true);
                }

                if (File.Exists(finalPath))
                {
                    string backupPath = Path.Combine(directory, "project.json." + Guid.NewGuid().ToString("N") + ".bak");
                    File.Replace(tempPath, finalPath, backupPath);
                    try
                    {
                        if (File.Exists(backupPath))
                            File.Delete(backupPath);
                    }
                    catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ProjectStore.cs: " + suppressedEx.Message); }
                }
                else
                {
                    File.Move(tempPath, finalPath);
                }
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ProjectStore.cs: " + suppressedEx.Message); }
                throw;
            }
        }

        private static void AppendAuditRecordUnchecked(string directory, AuditRecord record)
        {
            string line = Serializer().Serialize(record);
            string auditPath = Path.Combine(directory, "audit.jsonl");

            using (var fs = new FileStream(auditPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                writer.WriteLine(line);
                writer.Flush();
                fs.Flush(true);
            }
        }

        private static IReadOnlyList<AuditRecord> ValidateAndReadAuditLog(string auditFile, string expectedProjectId)
        {
            var records = new List<AuditRecord>();
            if (!File.Exists(auditFile))
                return records;

            long expectedSeq = 1;
            using (var reader = new StreamReader(auditFile, Encoding.UTF8))
            {
                string line;
                int lineNum = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNum++;
                    if (string.IsNullOrWhiteSpace(line))
                        throw new ProjectStorageException("MALFORMED_AUDIT_LOG", "Empty or whitespace line in audit log at line " + lineNum);

                    AuditRecord rec;
                    try
                    {
                        rec = Serializer().Deserialize<AuditRecord>(line);
                    }
                    catch (Exception ex)
                    {
                        throw new ProjectStorageException("MALFORMED_AUDIT_LOG", "Malformed audit JSON at line " + lineNum + ": " + ex.Message, ex);
                    }

                    if (rec == null)
                        throw new ProjectStorageException("MALFORMED_AUDIT_LOG", "Null audit record deserialized at line " + lineNum);

                    if (rec.Sequence != expectedSeq)
                    {
                        throw new ProjectStorageException("MALFORMED_AUDIT_LOG",
                            string.Format(CultureInfo.InvariantCulture, "Audit sequence invalid at line {0}: expected {1} but found {2}", lineNum, expectedSeq, rec.Sequence));
                    }

                    if (!string.IsNullOrWhiteSpace(expectedProjectId) && !string.Equals(rec.ProjectId, expectedProjectId, StringComparison.Ordinal))
                    {
                        throw new ProjectStorageException("AUDIT_PROJECT_ID_MISMATCH",
                            string.Format(CultureInfo.InvariantCulture, "Audit ProjectId mismatch rejected at line {0}: expected '{1}' but found '{2}'.", lineNum, expectedProjectId, rec.ProjectId));
                    }

                    if (!AuditActionType.IsValid(rec.ActionType))
                    {
                        throw new ProjectStorageException("MALFORMED_AUDIT_LOG", "Invalid audit action type at line " + lineNum + ": " + rec.ActionType);
                    }

                    records.Add(rec);
                    expectedSeq++;
                }
            }

            return records;
        }
    }
}
