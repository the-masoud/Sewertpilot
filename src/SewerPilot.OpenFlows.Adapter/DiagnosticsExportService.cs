using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    // Result of one diagnostics export: where the archive was written and what it contains.
    public sealed class DiagnosticsExportResult
    {
        public string ArchivePath { get; set; }
        public long SizeBytes { get; set; }
        public IList<string> Entries { get; set; }
    }

    // Builds a local, redacted support archive. It deliberately excludes project contents,
    // engineering model data, credentials and identifying file paths, and never transmits anything.
    public static class DiagnosticsExportService
    {
        // Creates the archive, replacing any file already at the destination.
        public static DiagnosticsExportResult Export(string destinationArchivePath)
        {
            if (string.IsNullOrWhiteSpace(destinationArchivePath))
                throw new ArgumentException("A destination file is required.", nameof(destinationArchivePath));

            string full = Path.GetFullPath(destinationArchivePath);
            string directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var entries = new List<string>();
            var redaction = new RedactionContext();

            if (File.Exists(full)) File.Delete(full);
            using (var archive = ZipFile.Open(full, ZipArchiveMode.Create))
            {
                AddText(archive, entries, "README.txt", SupportReadme());
                AddText(archive, entries, "build-identity.json", BuildIdentity(redaction));
                AddText(archive, entries, "environment.json", EnvironmentReport(redaction));
                AddText(archive, entries, "runtime-compatibility.json", RuntimeCompatibility(redaction));

                string failure = LastFailure(redaction);
                if (failure != null) AddText(archive, entries, "last-failure.json", failure);

                string log = ApplicationDiagnostics.ReadLogTail(256 * 1024);
                AddText(archive, entries, "application.log",
                    string.IsNullOrWhiteSpace(log)
                        ? "No application log entries were recorded.\r\n"
                        : redaction.Redact(log));

                AddText(archive, entries, "support-summary.txt", SupportSummary(redaction));
            }

            return new DiagnosticsExportResult
            {
                ArchivePath = full,
                SizeBytes = new FileInfo(full).Length,
                Entries = entries
            };
        }

        private static void AddText(ZipArchive archive, IList<string> entries, string name, string content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(content ?? string.Empty);
            entries.Add(name);
        }

        private static string SupportReadme()
        {
            var sb = new StringBuilder();
            sb.AppendLine("SewerPilot Engineering Beta - diagnostics archive");
            sb.AppendLine("================================================");
            sb.AppendLine();
            sb.AppendLine("This archive was created on your computer and has NOT been sent anywhere.");
            sb.AppendLine("Review it, then attach it to your message to the SewerPilot team if you wish.");
            sb.AppendLine();
            sb.AppendLine("Contents:");
            sb.AppendLine("  build-identity.json        Product version, build identity and assembly versions.");
            sb.AppendLine("  environment.json           Windows and .NET runtime details of this workstation.");
            sb.AppendLine("  runtime-compatibility.json Whether a supported Bentley OpenFlows runtime was found.");
            sb.AppendLine("  application.log            Recent SewerPilot application log entries.");
            sb.AppendLine("  last-failure.json          The most recent failed operation, if any.");
            sb.AppendLine("  support-summary.txt        A short human-readable summary.");
            sb.AppendLine();
            sb.AppendLine("What is deliberately NOT included:");
            sb.AppendLine("  - Any engineering model, project file or calculation result content.");
            sb.AppendLine("  - Your user name, computer name or folder structure (replaced with placeholders).");
            sb.AppendLine("  - Passwords, licence keys or other credentials.");
            sb.AppendLine();
            return sb.ToString();
        }

        private static string BuildIdentity(RedactionContext redaction)
        {
            Assembly entry = Assembly.GetEntryAssembly() ?? typeof(DiagnosticsExportService).Assembly;
            var sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"product_name\": ").Append(redaction.Json("SewerPilot Engineering Beta")).Append(",\r\n");
            sb.Append("  \"channel\": ").Append(redaction.Json("BETA")).Append(",\r\n");
            sb.Append("  \"product_version\": ").Append(redaction.Json(ReadProductVersion(entry))).Append(",\r\n");
            sb.Append("  \"assembly_version\": ").Append(redaction.Json(entry.GetName().Version == null ? null : entry.GetName().Version.ToString())).Append(",\r\n");
            sb.Append("  \"informational_version\": ").Append(redaction.Json(ReadInformationalVersion(entry))).Append(",\r\n");
            sb.Append("  \"source_revision\": ").Append(redaction.Json(ReadSourceRevision(entry))).Append(",\r\n");
            sb.Append("  \"entry_assembly\": ").Append(redaction.Json(entry.GetName().Name)).Append(",\r\n");
            sb.Append("  \"process_architecture\": ").Append(redaction.Json(Environment.Is64BitProcess ? "x64" : "x86")).Append(",\r\n");
            sb.Append("  \"target_framework\": ").Append(redaction.Json(".NET Framework 4.8")).Append(",\r\n");
            sb.Append("  \"export_timestamp_utc\": ").Append(redaction.Json(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))).Append(",\r\n");
            sb.Append("  \"loaded_assemblies\": [\r\n");
            var lines = new List<string>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name != null && a.GetName().Name.StartsWith("SewerPilot", StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add("    { \"name\": " + redaction.Json(assembly.GetName().Name) + ", \"version\": " +
                    redaction.Json(assembly.GetName().Version == null ? null : assembly.GetName().Version.ToString()) + " }");
            }
            sb.Append(string.Join(",\r\n", lines.ToArray()));
            sb.Append("\r\n  ]\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }

        private static string EnvironmentReport(RedactionContext redaction)
        {
            var sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"os_version\": ").Append(redaction.Json(Environment.OSVersion.VersionString)).Append(",\r\n");
            sb.Append("  \"os_64bit\": ").Append(Environment.Is64BitOperatingSystem ? "true" : "false").Append(",\r\n");
            sb.Append("  \"process_64bit\": ").Append(Environment.Is64BitProcess ? "true" : "false").Append(",\r\n");
            sb.Append("  \"clr_version\": ").Append(redaction.Json(Environment.Version.ToString())).Append(",\r\n");
            sb.Append("  \"processor_count\": ").Append(Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
            sb.Append("  \"system_page_size_bytes\": ").Append(Environment.SystemPageSize.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
            sb.Append("  \"ui_culture\": ").Append(redaction.Json(CultureInfo.CurrentUICulture.Name)).Append(",\r\n");
            sb.Append("  \"screen_primary_size\": ").Append(redaction.Json(PrimaryScreenSize())).Append(",\r\n");
            sb.Append("  \"application_directory\": ").Append(redaction.Json(AppDomain.CurrentDomain.BaseDirectory)).Append(",\r\n");
            sb.Append("  \"log_directory\": ").Append(redaction.Json(ApplicationDiagnostics.LogDirectory)).Append(",\r\n");
            sb.Append("  \"log_file_exists\": ").Append(File.Exists(ApplicationDiagnostics.LogFilePath) ? "true" : "false").Append(",\r\n");
            sb.Append("  \"last_failure_recorded\": ").Append(File.Exists(ApplicationDiagnostics.LastFailureFilePath) ? "true" : "false").Append("\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }

        private static string PrimaryScreenSize()
        {
            try
            {
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) return "unknown";
                return screen.Bounds.Width.ToString(CultureInfo.InvariantCulture) + "x" +
                       screen.Bounds.Height.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception) { return "unavailable"; }
        }

        // Reports whether a Bentley OpenFlows runtime that SewerPilot can use is present.
        private static string RuntimeCompatibility(RedactionContext redaction)
        {
            var sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"checked_at_utc\": ").Append(redaction.Json(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))).Append(",\r\n");
            string status;
            int candidateCount = 0;
            var candidates = new List<string>();
            try
            {
                var diagnostics = new DiscoveryDiagnostics();
                var options = new OpenFlowsDiscoveryOptions();
                var locations = new WindowsRuntimeLocationSource().Read(options, diagnostics);
                var inspector = new InstalledRuntimeInspector();
                var found = locations.SelectMany(location => inspector.Inspect(location, options, diagnostics)).ToList();
                candidateCount = found.Count;
                status = found.Count == 0 ? "NOT_FOUND" : "FOUND";
                foreach (var candidate in found)
                {
                    candidates.Add("    { \"install_root\": " + redaction.Json(candidate.InstallRoot) +
                        ", \"product_version\": " + redaction.Json(candidate.Executable == null ? null : candidate.Executable.ProductVersion) +
                        ", \"statically_usable\": " + (candidate.IsStaticallyUsable ? "true" : "false") + " }");
                }
            }
            catch (Exception error)
            {
                status = "DETECTION_ERROR";
                candidates.Add("    { \"error\": " + redaction.Json(error.GetType().Name + ": " + error.Message) + " }");
            }

            sb.Append("  \"bentley_openflows_status\": ").Append(redaction.Json(status)).Append(",\r\n");
            sb.Append("  \"candidate_count\": ").Append(candidateCount.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
            sb.Append("  \"candidates\": [\r\n").Append(string.Join(",\r\n", candidates.ToArray())).Append("\r\n  ],\r\n");
            sb.Append("  \"note\": ").Append(redaction.Json(
                "Native hydraulic calculation additionally requires a licensed Bentley session. " +
                "SewerPilot never redistributes Bentley assemblies or licences.")).Append("\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }

        private static string LastFailure(RedactionContext redaction)
        {
            var record = ApplicationDiagnostics.ReadLastFailure();
            if (record == null) return null;
            var sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"timestamp_utc\": ").Append(redaction.Json(record.TimestampUtc)).Append(",\r\n");
            sb.Append("  \"operation\": ").Append(redaction.Json(record.Operation)).Append(",\r\n");
            sb.Append("  \"exception_type\": ").Append(redaction.Json(record.ExceptionType)).Append(",\r\n");
            sb.Append("  \"message\": ").Append(redaction.Json(record.Message)).Append(",\r\n");
            sb.Append("  \"detail\": ").Append(redaction.Json(record.Detail)).Append("\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }

        private static string SupportSummary(RedactionContext redaction)
        {
            Assembly entry = Assembly.GetEntryAssembly() ?? typeof(DiagnosticsExportService).Assembly;
            var sb = new StringBuilder();
            sb.AppendLine("SewerPilot Engineering Beta - support summary");
            sb.AppendLine("Created (UTC): " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            sb.AppendLine("Product version: " + ReadProductVersion(entry));
            sb.AppendLine("Build identity: " + ReadInformationalVersion(entry));
            sb.AppendLine("Source revision: " + ReadSourceRevision(entry));
            sb.AppendLine("Process architecture: " + (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine("Windows: " + Environment.OSVersion.VersionString);
            sb.AppendLine("CLR: " + Environment.Version);
            sb.AppendLine();

            var record = ApplicationDiagnostics.ReadLastFailure();
            sb.AppendLine("Most recent failed operation:");
            if (record == null)
            {
                sb.AppendLine("  None recorded.");
            }
            else
            {
                sb.AppendLine("  When (UTC): " + record.TimestampUtc);
                sb.AppendLine("  Operation:  " + record.Operation);
                sb.AppendLine("  Error:      " + record.ExceptionType + ": " + record.Message);
            }
            sb.AppendLine();
            sb.AppendLine("Capabilities that are not qualified in this beta:");
            sb.AppendLine("  - General hydraulic calculation on arbitrary SewerGEMS models is not enabled.");
            sb.AppendLine("  - The supervised production Execute workflow requires a local Python interpreter");
            sb.AppendLine("    and is not part of the beta user workflow.");
            sb.AppendLine("  - Native calculation is restricted to the models qualified during testing.");
            return redaction.Redact(sb.ToString());
        }

        private static string ReadProductVersion(Assembly assembly)
        {
            try
            {
                string informational = ReadInformationalVersion(assembly);
                if (!string.IsNullOrWhiteSpace(informational))
                {
                    int plus = informational.IndexOf('+');
                    return plus > 0 ? informational.Substring(0, plus) : informational;
                }
                var attribute = assembly.GetCustomAttributes(typeof(AssemblyProductAttribute), false)
                    .OfType<AssemblyProductAttribute>().FirstOrDefault();
                if (attribute != null) return attribute.Product;
                return assembly.GetName().Version == null ? "unknown" : assembly.GetName().Version.ToString();
            }
            catch (Exception) { return "unknown"; }
        }

        private static string ReadInformationalVersion(Assembly assembly)
        {
            try
            {
                var attribute = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                    .OfType<AssemblyInformationalVersionAttribute>().FirstOrDefault();
                return attribute == null ? null : attribute.InformationalVersion;
            }
            catch (Exception) { return null; }
        }

        private static string ReadSourceRevision(Assembly assembly)
        {
            try
            {
                string informational = ReadInformationalVersion(assembly);
                if (string.IsNullOrWhiteSpace(informational)) return "unknown";
                int plus = informational.IndexOf('+');
                if (plus < 0 || plus + 1 >= informational.Length) return "unknown";
                return informational.Substring(plus + 1);
            }
            catch (Exception) { return "unknown"; }
        }

        // Removes identifying workstation detail from exported values. Every value is redacted
        // before it is JSON-escaped, so escaping can never hide a path from this filter.
        private sealed class RedactionContext
        {
            private readonly List<KeyValuePair<string, string>> roots = new List<KeyValuePair<string, string>>();
            private readonly List<KeyValuePair<string, string>> identity = new List<KeyValuePair<string, string>>();

            public RedactionContext()
            {
                AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
                AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%");
                AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%");
                AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "%PROGRAMDATA%");
                AddRoot(Path.GetTempPath(), "%TEMP%");
                // Longest first so the most specific root wins.
                roots.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));

                AddIdentity(Environment.UserName, "<user>");
                AddIdentity(Environment.MachineName, "<machine>");
            }

            private void AddRoot(string value, string placeholder)
            {
                if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 4) return;
                roots.Add(new KeyValuePair<string, string>(value.TrimEnd('\\'), placeholder));
            }

            private void AddIdentity(string value, string placeholder)
            {
                if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 3) return;
                identity.Add(new KeyValuePair<string, string>(value, placeholder));
            }

            // Redacts a raw value and returns it as a JSON string literal.
            public string Json(string value)
            {
                if (value == null) return "null";
                return Quote(Redact(value));
            }

            public string Redact(string text)
            {
                if (string.IsNullOrEmpty(text)) return text;
                string result = text;

                foreach (var root in roots)
                    result = Regex.Replace(result, Regex.Escape(root.Key), root.Value,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                // Engineering file identities are project data and are always removed.
                result = Regex.Replace(result, @"[A-Za-z0-9_.\- ]*\.stsw(\.bak)?", "<model-file>",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                result = Regex.Replace(result, @"[A-Za-z0-9_.\- ]*project\.json", "<project-file>",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                // Under a known root, keep only the root and the final segment: the intermediate
                // folder structure is the identifying part.
                result = Regex.Replace(result, @"%[A-Z]+%(\\([^\\\s" + "\"" + @"]+)){2,}",
                    match =>
                    {
                        string[] parts = match.Value.Split('\\');
                        return parts[0] + "\\" + parts[parts.Length - 1];
                    }, RegexOptions.CultureInvariant);

                // Any remaining absolute path keeps only its final segment.
                result = Regex.Replace(result, @"[A-Za-z]:\\[^\s" + "\"" + @"]*",
                    match =>
                    {
                        string trimmed = match.Value.TrimEnd('\\');
                        try
                        {
                            string leaf = Path.GetFileName(trimmed);
                            return string.IsNullOrEmpty(leaf) ? "<path>" : "<path>\\" + leaf;
                        }
                        catch (Exception) { return "<path>"; }
                    }, RegexOptions.CultureInvariant);

                foreach (var item in identity)
                    result = Regex.Replace(result, Regex.Escape(item.Key), item.Value,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                return result;
            }

            private static string Quote(string value)
            {
                if (value == null) return "null";
                return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
            }
        }
    }
}
