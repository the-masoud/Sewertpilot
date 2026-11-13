using System;
using System.IO;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    public static class Civil3dLiveTests
    {
        public static int RunCivil3dLiveTest()
        {
            Console.WriteLine("================================================================================");
            Console.WriteLine("[Civil3dLiveTests] Starting Live Autodesk Civil 3D Integration Round-Trip Test");
            Console.WriteLine("================================================================================");

            var options = new Civil3dLiveOptions
            {
                Civil3dExecutablePath = @"C:\Program Files\Autodesk\AutoCAD 2021\accoreconsole.exe",
                SourceTemplateDwg = @"C:\Program Files\Autodesk\AutoCAD 2021\C3D\Help\Civil Tutorials\Drawings\Pipe Networks-3.dwg",
                TargetElementName = "Structure - (1)",
                TargetProperty = "RimElevation",
                TargetMutatedValue = 655.500
            };

            Console.WriteLine("[Civil3dLiveTests] Executable: " + options.Civil3dExecutablePath);
            Console.WriteLine("[Civil3dLiveTests] Template DWG: " + options.SourceTemplateDwg);
            Console.WriteLine("[Civil3dLiveTests] Target: " + options.TargetElementName + "." + options.TargetProperty + " -> " + options.TargetMutatedValue);

            var result = Civil3dLiveWorkflow.ExecuteLiveRoundTrip(options);

            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine("[Civil3dLiveTests] Execution Result:");
            Console.WriteLine("  Success: " + result.Success);
            Console.WriteLine("  Duration: " + result.Duration.TotalSeconds.ToString("F2") + "s");

            if (!result.Success)
            {
                Console.WriteLine("  ERROR: " + result.ErrorMessage);
                Console.WriteLine("  PreMutationValue: " + result.PreMutationValue);
                Console.WriteLine("  TargetMutationValue: " + result.TargetMutationValue);
                Console.WriteLine("  PostMutationReRead: " + result.PostMutationReReadValue);
                Console.WriteLine("  Parity: " + result.ParityVerified);
                Console.WriteLine("  RawRead: " + (result.RawJsonReadPayload ?? "(null)"));
                Console.WriteLine("  RawMutate: " + (result.RawJsonMutationPayload ?? "(null)"));
                Console.WriteLine("  RawReRead: " + (result.RawJsonReReadPayload ?? "(null)"));
                return 1;
            }

            Console.WriteLine("  Civil 3D PID: " + result.ProcessInfo.Pid);
            Console.WriteLine("  Civil 3D Version: " + result.ProcessInfo.Civil3DVersion);
            Console.WriteLine("  Active Drawing: " + result.ProcessInfo.ActiveDrawingName);
            Console.WriteLine("  Discovered Conduits: " + (result.NetworkRead?.Conduits?.Count ?? 0));
            Console.WriteLine("  Discovered Structures: " + (result.NetworkRead?.Structures?.Count ?? 0));
            Console.WriteLine("  Pre-Mutation Value: " + result.PreMutationValue.ToString("F3"));
            Console.WriteLine("  Target Mutation Value: " + result.TargetMutationValue.ToString("F3"));
            Console.WriteLine("  Post-Mutation Re-Read: " + result.PostMutationReReadValue.ToString("F3"));
            Console.WriteLine("  Parity Verified: " + result.ParityVerified);
            Console.WriteLine("  Snapshot Ingested: " + (result.Snapshot != null));
            Console.WriteLine("  Topology IsValid: " + (result.NetworkRead?.TopologyCheck?.IsValid ?? false));
            Console.WriteLine("================================================================================");

            return result.Success ? 0 : 1;
        }
    }
}
