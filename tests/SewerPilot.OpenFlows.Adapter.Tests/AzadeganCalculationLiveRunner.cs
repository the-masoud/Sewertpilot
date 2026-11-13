using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    // Single bounded CLI runner for the Azadegan native calculation-and-readback attempt.
    internal static class AzadeganCalculationLiveRunner
    {
        internal static int Run(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: --p16-azadegan-calculation-live <sourceModelPath> <evidenceDir> [runtimeRoot] [runtimeHostExe]");
                return 2;
            }
            string source = args[1];
            string evidenceDirectory = args[2];
            string runtimeRoot = args.Length > 3 ? args[3] : @"C:\Program Files\Bentley\OpenFlows Sewer";
            string runtimeHost = args.Length > 4 ? args[4] : null;

            Directory.CreateDirectory(evidenceDirectory);
            var outcome = P16AzadeganCalculationProof.Execute(source, runtimeRoot, runtimeHost);

            Write(Path.Combine(evidenceDirectory, "source-family-manifest-before.txt"), outcome.SourceManifestBefore);
            Write(Path.Combine(evidenceDirectory, "source-family-manifest-after.txt"), outcome.SourceManifestAfter);
            Write(Path.Combine(evidenceDirectory, "disposable-manifest-before.txt"), outcome.WorkingManifestBefore);
            Write(Path.Combine(evidenceDirectory, "disposable-manifest-after.txt"), outcome.WorkingManifestAfter);
            if (outcome.RequestJson != null) File.WriteAllText(Path.Combine(evidenceDirectory, "request.json"), outcome.RequestJson);
            if (outcome.ResultJson != null) File.WriteAllText(Path.Combine(evidenceDirectory, "result.json"), outcome.ResultJson);
            if (outcome.ObservationJson != null) File.WriteAllText(Path.Combine(evidenceDirectory, "observation.json"), outcome.ObservationJson);
            if (outcome.CalculateObservationJson != null) File.WriteAllText(Path.Combine(evidenceDirectory, "calculate-observation.json"), outcome.CalculateObservationJson);
            if (outcome.PreSaveObservationJson != null) File.WriteAllText(Path.Combine(evidenceDirectory, "pre-save-observation.json"), outcome.PreSaveObservationJson);
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
                    { "DeclaredInventoryDigest", outcome.DeclaredInventoryDigest },
                    { "Status", outcome.Status },
                    { "Passed", outcome.Passed },
                    { "RuntimeExitCode", outcome.RuntimeExitCode },
                    { "FailureClassification", outcome.FailureClassification },
                    { "FailureMessageSafe", outcome.FailureMessageSafe },
                    { "SourceIntegrityPreserved", outcome.SourceIntegrityPreserved },
                    { "WorkingCreatedFiles", outcome.WorkingCreatedFiles },
                    { "WorkingModifiedFiles", outcome.WorkingModifiedFiles },
                    { "WatchdogDetail", outcome.WatchdogDetail },
                    { "SaveInvocationsAttempted", outcome.SaveInvocationsAttempted },
                    { "SaveInvocationsCompleted", outcome.SaveInvocationsCompleted },
                    { "PreSaveEvidenceRecovered", outcome.PreSaveObservationJson != null },
                    { "IsPass", outcome.IsPass },
                    { "Error", outcome.Error }
                }));

            Console.WriteLine("STATUS: " + (outcome.Status ?? "<none>"));
            Console.WriteLine("PASSED: " + outcome.IsPass);
            Console.WriteLine("EXIT: " + outcome.RuntimeExitCode);
            Console.WriteLine("SOURCE_INTEGRITY: " + outcome.SourceIntegrityPreserved);
            Console.WriteLine("WATCHDOG: " + (outcome.WatchdogDetail ?? "<none>"));
            Console.WriteLine("SAVE_ATTEMPTED: " + outcome.SaveInvocationsAttempted + " SAVE_COMPLETED: " + outcome.SaveInvocationsCompleted);
            Console.WriteLine("PRE_SAVE_EVIDENCE: " + (outcome.PreSaveObservationJson != null));
            Console.WriteLine("CREATED: " + string.Join(",", outcome.WorkingCreatedFiles));
            Console.WriteLine("MODIFIED: " + string.Join(",", outcome.WorkingModifiedFiles));
            if (outcome.FailureClassification != null) Console.WriteLine("FAILURE: " + outcome.FailureClassification);
            if (outcome.Error != null) Console.WriteLine("ERROR: " + outcome.Error);
            return outcome.IsPass ? 0 : 1;
        }

        private static void Write(string path, IEnumerable<string> lines)
        {
            var text = new StringBuilder();
            foreach (string line in lines ?? Enumerable.Empty<string>()) text.Append(line).Append('\n');
            File.WriteAllText(path, text.ToString());
        }
    }
}
