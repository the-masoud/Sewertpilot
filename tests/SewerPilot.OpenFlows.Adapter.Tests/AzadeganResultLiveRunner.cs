using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    // Single bounded CLI runner for the Azadegan read-only result-access attempt.
    // It performs exactly one runtime attempt and writes the evidence documents.
    internal static class AzadeganResultLiveRunner
    {
        internal static int Run(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: --p16-azadegan-result-live <sourceModelPath> <evidenceDir> [runtimeRoot] [runtimeHostExe]");
                return 2;
            }
            string source = args[1];
            string evidenceDirectory = args[2];
            string runtimeRoot = args.Length > 3 ? args[3] : @"C:\Program Files\Bentley\OpenFlows Sewer";
            string runtimeHost = args.Length > 4 ? args[4] : null;

            Directory.CreateDirectory(evidenceDirectory);
            var outcome = P16AzadeganReadOnlyResultProof.Execute(source, runtimeRoot, runtimeHost);

            Write(Path.Combine(evidenceDirectory, "source-copy-manifest.txt"), outcome.SourceManifest);
            Write(Path.Combine(evidenceDirectory, "disposable-copy-manifest.txt"), outcome.WorkingManifest);
            Write(Path.Combine(evidenceDirectory, "source-copy-manifest-after.txt"), outcome.SourceAfterManifest);
            Write(Path.Combine(evidenceDirectory, "disposable-copy-manifest-after.txt"), outcome.WorkingAfterManifest);
            if (outcome.RequestJson != null) File.WriteAllText(Path.Combine(evidenceDirectory, "request.json"), outcome.RequestJson);
            if (outcome.ResultJson != null) File.WriteAllText(Path.Combine(evidenceDirectory, "result.json"), outcome.ResultJson);
            if (outcome.ObservationJson != null) File.WriteAllText(Path.Combine(evidenceDirectory, "observation.json"), outcome.ObservationJson);
            if (outcome.RunDirectory != null)
            {
                string journal = Path.Combine(outcome.RunDirectory, "stages.jsonl");
                if (File.Exists(journal)) File.Copy(journal, Path.Combine(evidenceDirectory, "runtime-stages.jsonl"), true);
            }
            File.WriteAllText(Path.Combine(evidenceDirectory, "outcome.json"),
                RuntimeHostProtocol.Serializer().Serialize(new Dictionary<string, object>
                {
                    { "SourceModelPath", outcome.SourceModelPath },
                    { "WorkingCopyDirectory", outcome.WorkingCopyDirectory },
                    { "WorkingModelPath", outcome.WorkingModelPath },
                    { "InventoryDigest", outcome.InventoryDigest },
                    { "Status", outcome.Status },
                    { "Passed", outcome.Passed },
                    { "RuntimeExitCode", outcome.RuntimeExitCode },
                    { "FailureClassification", outcome.FailureClassification },
                    { "FailureMessageSafe", outcome.FailureMessageSafe },
                    { "SourceIntegrityPreserved", outcome.SourceIntegrityPreserved },
                    { "WorkingIntegrityPreserved", outcome.WorkingIntegrityPreserved },
                    { "WorkingAddedFiles", outcome.WorkingAddedFiles },
                    { "Error", outcome.Error }
                }));

            Console.WriteLine("STATUS: " + (outcome.Status ?? "<none>"));
            Console.WriteLine("PASSED: " + outcome.Passed);
            Console.WriteLine("EXIT: " + outcome.RuntimeExitCode);
            Console.WriteLine("SOURCE_INTEGRITY: " + outcome.SourceIntegrityPreserved);
            Console.WriteLine("WORKING_INTEGRITY: " + outcome.WorkingIntegrityPreserved);
            if (outcome.FailureClassification != null) Console.WriteLine("FAILURE: " + outcome.FailureClassification);
            if (outcome.Error != null) Console.WriteLine("ERROR: " + outcome.Error);
            return outcome.Passed ? 0 : 1;
        }

        private static void Write(string path, IEnumerable<string> lines)
        {
            var text = new StringBuilder();
            foreach (string line in lines ?? Enumerable.Empty<string>()) text.Append(line).Append('\n');
            File.WriteAllText(path, text.ToString());
        }
    }
}
