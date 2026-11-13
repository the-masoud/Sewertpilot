using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static class FarzaneganSourceDataReviewLiveRunner
    {
        private static string Hash(string file)
        { using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
        internal static int Run(string model, string evidence)
        {
            string[] files = null, before = null;
            try
            {
                model = Path.GetFullPath(model); evidence = Path.GetFullPath(evidence);
                if (!File.Exists(model) || !model.EndsWith(".stsw", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("EXACT_MODEL_REQUIRED");
                if (Directory.Exists(evidence)) throw new InvalidOperationException("FRESH_EVIDENCE_REQUIRED");
                files = P16FarzaneganNativePilotHost.Family.Select(n => Path.Combine(Path.GetDirectoryName(model), n)).ToArray();
                before = files.Select(Hash).ToArray(); Directory.CreateDirectory(evidence);
                using (var reader = new OpenFlowsModelReader())
                {
                    var read = reader.ReadWithEvidence(model); var ev = read.Evidence;
                    File.WriteAllText(Path.Combine(evidence, "read-status.json"), GravityExecutionPlanBuilder.Json(new {
                        ev.Accepted, ev.FailureCode, ev.SourceIntegrity, ev.WorkingIntegrity, ev.CloseCompleted,
                        SaveCalls = ev.SaveWriteInvocations.Count, SolverCalls = 0, ModelSHA256 = Hash(model)
                    }), new UTF8Encoding(false));
                    if (!ev.Accepted || !ev.SourceIntegrity || !ev.WorkingIntegrity || !ev.CloseCompleted || ev.SaveWriteInvocations.Count != 0)
                        throw new InvalidDataException("NATIVE_READ_NOT_ACCEPTED:" + ev.FailureCode);
                    if (!ev.Diagnostics.TryGetValue("P12ANetworkReadJson", out string json)) throw new InvalidDataException("NETWORK_EXPORT_MISSING");
                    File.WriteAllText(Path.Combine(evidence, "native-source-read.json"), json, new UTF8Encoding(false));
                    var network = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(json);
                    var catchments = network.SourceData.Parameters.Where(p => p.Category == "Catchment").ToArray();
                    File.WriteAllText(Path.Combine(evidence, "catchment-observations.json"), GravityExecutionPlanBuilder.Json(catchments), new UTF8Encoding(false));
                    var count = catchments.SingleOrDefault(p => p.ParameterIdentity == "ElementCount");
                    Console.WriteLine("NETWORK=" + network.Conduits.Count + "/" + network.Structures.Count);
                    Console.WriteLine("CATCHMENT_COUNT=" + count?.Value + " STATUS=" + count?.Availability);
                    Console.WriteLine("CATCHMENT_OBSERVATIONS=" + catchments.Length + " PROVEN=" + catchments.Count(p => p.SourceClassification == "PROVEN"));
                    if (network.Conduits.Count != 71 || network.Structures.Count != 73 || count?.SourceClassification != "PROVEN" || Convert.ToInt32(count.Value) <= 0)
                        throw new InvalidDataException("REAL_CATCHMENT_EXPORT_NOT_QUALIFIED");
                    return 0;
                }
            }
            catch (Exception error) { Console.Error.WriteLine("SOURCE_REVIEW_BLOCKED=" + error.GetType().Name + ":" + error.Message); return 2; }
            finally
            {
                if (files != null && before != null)
                {
                    bool unchanged = before.SequenceEqual(files.Select(Hash));
                    File.WriteAllText(Path.Combine(evidence, "input-integrity.txt"), "SEVEN_FILES_UNCHANGED=" + unchanged);
                    Console.WriteLine("SEVEN_FILES_UNCHANGED=" + unchanged);
                }
            }
        }
    }
}
