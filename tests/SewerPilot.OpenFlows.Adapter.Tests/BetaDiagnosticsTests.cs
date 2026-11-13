using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    // Focused, hermetic verification of the engineering-beta diagnostics export.
    // Proves the archive is produced, that it carries the expected identity entries, and that
    // workstation identity and engineering file paths are redacted rather than exported.
    internal static class BetaDiagnosticsTests
    {
        internal static int Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "SewerPilotBetaDiag_" + Guid.NewGuid().ToString("N"));
            string logs = Path.Combine(root, "logs");
            string archivePath = Path.Combine(root, "diagnostics.zip");
            string previousOverride = ApplicationDiagnostics.DirectoryOverride;
            try
            {
                Directory.CreateDirectory(logs);
                ApplicationDiagnostics.DirectoryOverride = logs;

                // A synthetic failure carrying identifying detail and an engineering model path.
                string sensitiveModel = Path.Combine(root, "ClientConfidentialSite", "FEP-AOC-030826.stsw");
                ApplicationDiagnostics.RecordFailure("Open protected source model",
                    new InvalidOperationException("Cannot open " + sensitiveModel + " for user " +
                        Environment.UserName + " on " + Environment.MachineName));

                var result = DiagnosticsExportService.Export(archivePath);

                if (!File.Exists(result.ArchivePath)) throw new Exception("Diagnostics archive was not created.");
                if (result.SizeBytes <= 0 || new FileInfo(result.ArchivePath).Length != result.SizeBytes)
                    throw new Exception("Diagnostics archive size was not reported accurately.");

                string[] required = { "README.txt", "build-identity.json", "environment.json",
                    "runtime-compatibility.json", "application.log", "support-summary.txt", "last-failure.json" };
                foreach (string name in required)
                    if (!result.Entries.Contains(name))
                        throw new Exception("Diagnostics archive is missing required entry " + name + ".");
                if (result.Entries.Count != required.Length)
                    throw new Exception("Diagnostics archive contains unexpected extra entries: " + string.Join(",", result.Entries.ToArray()));

                string buildIdentity, runtime, failure, log, summary;
                using (var archive = ZipFile.OpenRead(result.ArchivePath))
                {
                    buildIdentity = Read(archive, "build-identity.json");
                    runtime = Read(archive, "runtime-compatibility.json");
                    failure = Read(archive, "last-failure.json");
                    log = Read(archive, "application.log");
                    summary = Read(archive, "support-summary.txt");
                }

                if (!buildIdentity.Contains("\"channel\": \"BETA\""))
                    throw new Exception("Build identity does not declare the beta channel.");
                if (!buildIdentity.Contains("source_revision"))
                    throw new Exception("Build identity does not carry a source revision field.");
                if (!runtime.Contains("bentley_openflows_status"))
                    throw new Exception("Runtime compatibility entry has no detection status.");

                string all = buildIdentity + runtime + failure + log + summary;
                foreach (string secret in new[] { Environment.UserName, Environment.MachineName, "FEP-AOC-030826.stsw", "ClientConfidentialSite" })
                    if (all.IndexOf(secret, StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new Exception("Diagnostics archive leaked identifying detail: " + secret);

                if (!failure.Contains("Open protected source model"))
                    throw new Exception("Last failed operation name was not exported.");
                if (!failure.Contains("timestamp_utc"))
                    throw new Exception("Last failed operation has no timestamp.");
                if (!log.Contains("ERROR"))
                    throw new Exception("Application log did not record the failure.");

                // Re-export must overwrite deterministically rather than append duplicate entries.
                var second = DiagnosticsExportService.Export(archivePath);
                if (second.Entries.Count != required.Length)
                    throw new Exception("Repeated export produced an inconsistent archive.");

                Console.WriteLine("BETA_DIAGNOSTICS_SAFETY=PASS; entries=" + result.Entries.Count +
                    "; redacted_user/machine/model_path=PASS; last_failure_exported=PASS; reexport_stable=PASS");
                return 0;
            }
            finally
            {
                ApplicationDiagnostics.DirectoryOverride = previousOverride;
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        private static string Read(ZipArchive archive, string name)
        {
            var entry = archive.GetEntry(name);
            if (entry == null) throw new Exception("Missing archive entry " + name + ".");
            using (var reader = new StreamReader(entry.Open()))
                return reader.ReadToEnd();
        }
    }
}
