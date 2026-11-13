using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    // Controlled application-level smoke test of the beta's engineer workflow:
    // open an isolated copy of a supported model through the same ProjectWorkspace read path
    // used by the "Select SewerGEMS Model" button, inspect the imported network, and prove the
    // source model family was not modified.
    internal static class BetaApplicationSmokeTest
    {
        private static readonly string[] Family = {
            "FEP-AOC-030826.stsw",
            "FEP-AOC-030826.stsw.dwh",
            "FEP-AOC-030826.stsw.profiles.dwh",
            "FEP-AOC-030826.stsw.scm",
            "FEP-AOC-030826.stsw.sqlite",
            "FEP-AOC-030826.stsw_1_1-stmc-gvf.out",
            "catchments.dxf"
        };

        internal static int Run(string sourceModelPath, string evidenceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceModelPath) || !File.Exists(sourceModelPath))
                throw new FileNotFoundException("Smoke-test source model was not found.", sourceModelPath);
            if (string.IsNullOrWhiteSpace(evidenceDirectory))
                throw new ArgumentException("An evidence directory is required.", nameof(evidenceDirectory));

            string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceModelPath));
            string modelName = Path.GetFileName(sourceModelPath);
            Directory.CreateDirectory(evidenceDirectory);

            var files = Family.Where(name => File.Exists(Path.Combine(sourceDirectory, name))).ToList();
            if (!files.Contains(modelName)) files.Insert(0, modelName);
            if (files.Count != Family.Length)
                throw new InvalidOperationException("The isolated smoke-test model family is incomplete; " +
                    "expected " + Family.Length + " files and found " + files.Count + ".");

            var before = files.ToDictionary(name => name,
                name => WorkingCopyIntegrity.Fingerprint(Path.Combine(sourceDirectory, name)).SHA256,
                StringComparer.OrdinalIgnoreCase);

            string root = Path.Combine(Path.GetTempPath(), "SewerPilotBetaSmoke_" + Guid.NewGuid().ToString("N"));
            string workingCopy = Path.Combine(root, "model-copy");
            string projectDirectory = Path.Combine(root, "project");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(workingCopy);

            foreach (string name in files)
                File.Copy(Path.Combine(sourceDirectory, name), Path.Combine(workingCopy, name), false);

            var copied = files.ToDictionary(name => name,
                name => WorkingCopyIntegrity.Fingerprint(Path.Combine(workingCopy, name)).SHA256,
                StringComparer.OrdinalIgnoreCase);
            foreach (string name in files)
                if (before[name] != copied[name])
                    throw new InvalidOperationException("Isolated copy does not match the source file: " + name);

            try
            {
                var started = DateTime.UtcNow;
                var workspace = ProjectWorkspace.Create(projectDirectory, "Beta application smoke test");
                workspace.ReadNetwork(Path.Combine(workingCopy, modelName));
                var elapsed = DateTime.UtcNow - started;

                if (workspace.Network == null) throw new InvalidOperationException("No network was read from the model.");
                if (workspace.Baseline == null) throw new InvalidOperationException("No protected baseline was established.");

                int conduits = workspace.Network.Conduits.Count;
                int structures = workspace.Network.Structures.Count;
                if (conduits <= 0 || structures <= 0)
                    throw new InvalidOperationException("The imported network is empty (" + conduits + " conduits / " + structures + " structures).");

                // Regression reference for the previously verified Farzanegan intake.
                bool referenceModel = string.Equals(modelName, Family[0], StringComparison.OrdinalIgnoreCase);
                if (referenceModel && (conduits != 71 || structures != 73))
                    throw new InvalidOperationException("Reference model regression: expected 71 conduits / 73 structures, read " +
                        conduits + " / " + structures + ".");

                // Neither the original nor the isolated copy may change as a result of the read.
                foreach (string name in files)
                {
                    if (WorkingCopyIntegrity.Fingerprint(Path.Combine(sourceDirectory, name)).SHA256 != before[name])
                        throw new InvalidOperationException("The original source file changed during the read: " + name);
                    if (WorkingCopyIntegrity.Fingerprint(Path.Combine(workingCopy, name)).SHA256 != before[name])
                        throw new InvalidOperationException("The isolated working copy changed during the read: " + name);
                }

                var sb = new StringBuilder();
                sb.Append("{\r\n");
                sb.Append("  \"timestamp_utc\": \"").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\",\r\n");
                sb.Append("  \"model_file\": \"").Append(modelName).Append("\",\r\n");
                sb.Append("  \"isolated_copy\": \"outside the repository, deleted after the run\",\r\n");
                sb.Append("  \"conduits_imported\": ").Append(conduits.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
                sb.Append("  \"structures_imported\": ").Append(structures.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
                sb.Append("  \"reference_model\": ").Append(referenceModel ? "true" : "false").Append(",\r\n");
                sb.Append("  \"read_seconds\": ").Append(elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append(",\r\n");
                sb.Append("  \"scenario_id\": ").Append(workspace.Network.ScenarioContext.ActiveScenarioId.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
                sb.Append("  \"physical_alternative_id\": ").Append(workspace.Network.ScenarioContext.PhysicalAlternativeId.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
                sb.Append("  \"baseline_fingerprint\": \"").Append(workspace.Baseline.Fingerprint.Value).Append("\",\r\n");
                sb.Append("  \"source_files_unchanged\": true,\r\n");
                sb.Append("  \"working_copy_unchanged\": true,\r\n");
                sb.Append("  \"source_files\": [\r\n");
                var entries = files.Select(name => "    { \"name\": \"" + name + "\", \"sha256\": \"" + before[name] + "\" }").ToList();
                sb.Append(string.Join(",\r\n", entries.ToArray()));
                sb.Append("\r\n  ],\r\n");
                sb.Append("  \"verdict\": \"PASS\"\r\n");
                sb.Append("}\r\n");
                File.WriteAllText(Path.Combine(evidenceDirectory, "application-smoke.json"), sb.ToString(), new UTF8Encoding(false));

                Console.WriteLine("BETA_APPLICATION_SMOKE=PASS; conduits=" + conduits + "; structures=" + structures +
                    "; reference=" + referenceModel + "; source_unchanged=true; copy_unchanged=true; seconds=" +
                    elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture));
                return 0;
            }
            finally
            {
                // The isolated copy contains customer engineering data and never persists.
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception) { }
            }
        }
    }
}
