using System;
using System.Collections.Generic;
using System.IO;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    // Prepared live runners for the two P16 operations added by the output-reader mission.
    // Neither is executed by the offline verification mission; both exist so a separately
    // authorized live mission has one exact, bounded command.
    internal static class AzadeganOutputReaderLiveRunner
    {
        internal static int Run(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: --p16-azadegan-output-reader-live <sourceArtifactPath> <evidenceDir> [runtimeRoot] [runtimeHostExe]");
                return 2;
            }
            string source = args[1];
            string evidenceDirectory = args[2];
            string runtimeRoot = args.Length > 3 ? args[3] : @"C:\Program Files\Bentley\OpenFlows Sewer";
            string runtimeHost = args.Length > 4 ? args[4] : null;

            Directory.CreateDirectory(evidenceDirectory);
            var outcome = P16AzadeganOutputReaderProof.Execute(source, runtimeRoot, runtimeHost);

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
                    { "SourceArtifactPath", outcome.SourceArtifactPath },
                    { "SourceArtifactSHA256Before", outcome.SourceArtifactSHA256Before },
                    { "SourceArtifactSHA256After", outcome.SourceArtifactSHA256After },
                    { "SourceArtifactUnchanged", outcome.SourceArtifactUnchanged },
                    { "CopyId", outcome.CopyId },
                    { "CopyDirectory", outcome.CopyDirectory },
                    { "CopyArtifactSHA256Before", outcome.CopyArtifactSHA256Before },
                    { "CopyArtifactSHA256After", outcome.CopyArtifactSHA256After },
                    { "CopyArtifactUnchanged", outcome.CopyArtifactUnchanged },
                    { "CopyDirectoryEntries", outcome.CopyDirectoryEntries },
                    { "ReaderRoot", outcome.ReaderRoot },
                    { "ReaderAssemblyNames", outcome.ReaderAssemblyNames },
                    { "ReaderAssemblySHA256", outcome.ReaderAssemblySHA256 },
                    { "RuntimeHostExecutable", outcome.RuntimeHostExecutable },
                    { "LaunchArguments", outcome.LaunchArguments },
                    { "RunDirectory", outcome.RunDirectory },
                    { "Status", outcome.Status },
                    { "MechanicsCompleted", outcome.MechanicsCompleted },
                    { "Passed", outcome.Passed },
                    { "RuntimeExitCode", outcome.RuntimeExitCode },
                    { "FailureClassification", outcome.FailureClassification },
                    { "FailureMessageSafe", outcome.FailureMessageSafe },
                    { "Error", outcome.Error }
                }));

            Console.WriteLine("STATUS: " + (outcome.Status ?? "<none>"));
            Console.WriteLine("PASSED: " + outcome.Passed);
            Console.WriteLine("SOURCE_UNCHANGED: " + outcome.SourceArtifactUnchanged);
            Console.WriteLine("COPY_UNCHANGED: " + outcome.CopyArtifactUnchanged);
            Console.WriteLine("EXIT_CODE: " + outcome.RuntimeExitCode);
            if (outcome.Error != null) Console.Error.WriteLine(outcome.Error);
            return outcome.MechanicsCompleted ? 0 : 1;
        }

        internal static int RunSubEngineProbe(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: --p16-azadegan-subengine-probe-live <sourceModelPath> <evidenceDir> [runtimeRoot] [runtimeHostExe]");
                return 2;
            }
            string source = args[1];
            string evidenceDirectory = args[2];
            string runtimeRoot = args.Length > 3 ? args[3] : @"C:\Program Files\Bentley\OpenFlows Sewer";
            string runtimeHost = args.Length > 4 ? args[4] : null;

            Directory.CreateDirectory(evidenceDirectory);
            var outcome = P16AzadeganSubEngineProbeProof.Execute(source, runtimeRoot, runtimeHost);

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
            Console.WriteLine("SOURCE_INTEGRITY: " + outcome.SourceIntegrityPreserved);
            Console.WriteLine("COPY_INTEGRITY: " + outcome.WorkingIntegrityPreserved);
            Console.WriteLine("EXIT_CODE: " + outcome.RuntimeExitCode);
            if (outcome.Error != null) Console.Error.WriteLine(outcome.Error);
            return outcome.Passed ? 0 : 1;
        }

        private static void Write(string path, List<string> lines)
        {
            if (lines != null) File.WriteAllLines(path, lines);
        }
    }
}
