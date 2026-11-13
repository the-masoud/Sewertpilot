using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    // Separate, internal TEST_ONLY proof for the operator-calculated package. Not a trust authority.
    // The original seven-file P16 diagnostic and its ReadOnly guard remain independent.
    internal static class P16CalculatedFlowDiagnostic
    {
        internal const string Operation = "InternalP16CalculatedFlowRead";
        internal const string LaunchSwitch = "--internal-p16-calculated-flow";
        internal const string CopyId = "1b7cf72024f7425799f73738aa6d14e0";
        internal const string ManifestSHA256 = "90FC8A4FF942B14E9BF194097F863A2A89CADC7EF13FC4B8B30BFE513D728656";
        internal const string Engine = "CivilStormDynamicEngine";
        internal const string FlowField = "BasicFlowResults_Flow", FlowRecord = "BasicFlowResults";
        internal const string DataSetGuid = "203f9c2e-c788-4b7a-a360-a6a438d62c2c";
        internal static string ModelPath => Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "Working", CopyId, "Lesson_1.stsw");
        private static readonly string[] Family = new[] {
            "Lesson_1.stsw", "Lesson_1.stsw.01.bak", "Lesson_1.stsw.dwh", "Lesson_1.stsw.dwh.01.bak",
            "Lesson_1.stsw.profiles.dwh", "Lesson_1.stsw.profiles.dwh.01.bak", "Lesson_1.stsw.sqlite", "Lesson_1.stsw.sqlite.01.bak",
            "Lesson_1.stsw_1_81.fld", "Lesson_1.stsw_1_81.hyg", "Lesson_1.stsw_1_81.out", "Lesson_1.stsw_1_81.txt" };

        internal static Dictionary<string, string> Context() => new Dictionary<string, string> {
            { "P16ScenarioId", "81" }, { "P16PhysicalAlternativeId", "82" }, { "P16CalculationOptionsId", "67" },
            { "P16Engine", Engine }, { "P16ManifestSHA256", ManifestSHA256 }, { "P16ElementId", "27" },
            { "P16TimeSeconds", "0" }, { "P16Classification", "TEST_ONLY" } };

        internal static HostRequest ParseRequest(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 256 * 1024) throw new InvalidDataException("P16 request absent or oversized.");
            try
            {
                var serializer = RuntimeHostProtocol.Serializer();
                var fields = serializer.DeserializeObject(json) as Dictionary<string, object>;
                var properties = typeof(HostRequest).GetProperties();
                if (fields == null || fields.Count != properties.Length || properties.Any(p => !fields.ContainsKey(p.Name)))
                    throw new InvalidDataException();
                return serializer.Deserialize<HostRequest>(json);
            }
            catch (Exception) { throw new InvalidDataException("Malformed or noncanonical calculated P16 request."); }
        }

        internal static void ValidateRequest(HostRequest request)
        {
            var expected = Context();
            if (request == null || request.Operation != Operation || request.WorkingCopyId != CopyId ||
                !string.Equals(request.WorkingModelPath, ModelPath, StringComparison.OrdinalIgnoreCase) ||
                request.EngineeringPlan != null || request.SourceBaselineFingerprint != null ||
                request.WriteAuthorizationTokenId != null || request.WriteAuthorizationScope != null ||
                request.WriteAuthorizationTargetWorkingPath != null || request.Diagnostics == null ||
                request.Diagnostics.Count != expected.Count || expected.Any(p => !request.Diagnostics.ContainsKey(p.Key) || request.Diagnostics[p.Key] != p.Value))
                throw new InvalidDataException("Calculated P16 proof requires the exact TEST_ONLY selection and no write/Solver/trust authority.");
            VerifyManifest();
        }

        internal static void ValidateInventoryEntries(IEnumerable<string> entries, string digest)
        {
            if (entries == null || !entries.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(Family) || digest != ManifestSHA256)
                throw new InvalidDataException("Calculated P16 model/result manifest does not match the pinned post-calculation package.");
        }

        internal static void VerifyManifest()
        {
            string directory = Path.GetDirectoryName(ModelPath);
            RuntimeHostProtocol.ControlledDirectory(directory, "Working");
            WorkingCopyIntegrity.RejectLinks(directory);
            var entries = Directory.GetFileSystemEntries(directory);
            if (!entries.Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(Family))
                throw new InvalidDataException("Calculated P16 package requires exactly its twelve pinned files.");
            var text = new StringBuilder();
            long total = 0;
            foreach (string name in Family)
            {
                string path = Path.Combine(directory, name);
                WorkingCopyIntegrity.RejectLinks(path);
                var file = new FileInfo(path);
                if (!file.Exists || file.Length <= 0 || file.Length > 128L * 1024 * 1024 || (total += file.Length) > 256L * 1024 * 1024)
                    throw new InvalidDataException("Calculated P16 file is absent or exceeds the bounded manifest size.");
                text.Append(name).Append('|').Append(file.Length.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(WorkingCopyIntegrity.Fingerprint(path).SHA256).Append('\n');
            }
            using (var hash = SHA256.Create())
                ValidateInventoryEntries(entries.Select(Path.GetFileName), BitConverter.ToString(hash.ComputeHash(new UTF8Encoding(false).GetBytes(text.ToString()))).Replace("-", ""));
        }

        internal static int SelectTime(double[] times)
        {
            if (times == null || times.Length == 0 || times.Length > 10000 || times.Any(t => double.IsNaN(t) || double.IsInfinity(t) || t < 0) ||
                times.Skip(1).Where((t, i) => t <= times[i]).Any())
                throw new InvalidDataException("Calculated P16 time vector is missing, invalid or not strictly increasing.");
            int index = Array.IndexOf(times, 0d); // Match the independently observed GUI time, never infer an output interval.
            if (index < 0) throw new InvalidDataException("The requested GUI time is not an available result timestep.");
            return index;
        }

        internal static string FlowUnit(string identity)
        {
            const string prefix = "Haestad.Support.Units.UnitConversionManager+UnitIndex:";
            if (identity == prefix + "CubicFeetPerSecond:38" || identity == prefix + "CFS:27") return "ft3/s";
            if (identity == prefix + "CubicMetersPerSecond:48") return "m3/s";
            return null; // Only exact semantic enum literals verified in the installed metadata; no guessed conversion.
        }

        internal static void ValidateObservation(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 256 * 1024) throw new InvalidDataException("P16 observation absent or oversized.");
            P16FlowObservation seen;
            try
            {
                var serializer = RuntimeHostProtocol.Serializer();
                var fields = serializer.DeserializeObject(json) as Dictionary<string, object>;
                var properties = typeof(P16FlowObservation).GetProperties();
                if (fields == null || fields.Count != properties.Length || properties.Any(p => !fields.ContainsKey(p.Name)))
                    throw new InvalidDataException();
                seen = serializer.Deserialize<P16FlowObservation>(json);
            }
            catch (Exception) { throw new InvalidDataException("Malformed or noncanonical P16 observation."); }
            string[] allowed = { "RESULTS_MISSING", "RESULT_CONTRACT_UNAVAILABLE", "RESULT_FIELD_UNSUPPORTED", "RESULT_UNIT_UNVERIFIED", "RESULT_VALUE_UNAVAILABLE", "VALUE_OBSERVED_UNTRUSTED" };
            if (seen == null || !allowed.Contains(seen.Status) || seen.FreshnessVerified || !seen.ResultCleanupCompleted || seen.CleanupError != null ||
                seen.FlowReadInvocations < 0 || seen.FlowReadInvocations > 1 || seen.OpenedModelPath != ModelPath ||
                seen.SupportedFieldNames == null || seen.SupportedFieldNames.Length > 1024 ||
                seen.SupportedFieldCount != seen.SupportedFieldNames.Length || seen.SupportedFieldNames.Any(n => n == null || n.Length > 256))
                throw new InvalidDataException("P16 result violates bounded untrusted/cleanup semantics.");
            if (seen.Status == "VALUE_OBSERVED_UNTRUSTED")
            {
                double value;
                if (seen.FlowReadInvocations != 1 || seen.HasResults != true || seen.FieldHasResults != true || seen.FlowFieldSupported != true ||
                    !seen.ResultManagerAvailable || seen.DataSetId != 1 || seen.DataSetGuid != DataSetGuid || seen.ScenarioId != 81 ||
                    seen.ActiveScenarioBefore != 81 || seen.ActiveScenarioAfter != 81 || seen.CurrentPhysicalAlternative != 82 ||
                    seen.CurrentCalculationOptions != 67 || seen.Engine != Engine || seen.ElementId != 27 ||
                    seen.FieldName != FlowField || seen.RecordName != FlowRecord || !seen.TimeStepIndex.HasValue ||
                    seen.TimeStepCount != (seen.AvailableTimeSeconds?.Length ?? 0) || seen.TimeStepIndex.Value != SelectTime(seen.AvailableTimeSeconds) ||
                    seen.TimeSeconds != seen.AvailableTimeSeconds[seen.TimeStepIndex.Value] || seen.WorkingUnitSymbol == null ||
                    seen.WorkingUnitSymbol != FlowUnit(seen.WorkingUnitIdentity) || string.IsNullOrEmpty(seen.StorageUnitIdentity) ||
                    (seen.ValueClrType != "System.Double" && seen.ValueClrType != "System.Single") ||
                    !double.TryParse(seen.RawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value) ||
                    seen.RawValueClassification == null || !seen.RawValueClassification.StartsWith("FINITE_", StringComparison.Ordinal) ||
                    seen.ErrorType != null || seen.FailureOperation != null)
                    throw new InvalidDataException("P16 Flow observation lacks exact context, field, time, unit or finite raw-value evidence.");
            }
        }

        internal static void ValidateResult(HostResult result)
        {
            string json;
            if (!result.Diagnostics.TryGetValue(P16FlowDiagnostic.EvidenceKey, out json)) throw new InvalidDataException("P16 observation missing.");
            ValidateObservation(json);
            VerifyManifest(); // Also called by the receiving process after owned RuntimeHost cleanup/exit.
        }
    }
}
