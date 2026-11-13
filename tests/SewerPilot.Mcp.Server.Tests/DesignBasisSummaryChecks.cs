using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SewerPilot.Mcp.Server;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.Mcp.Server.Tests
{
    internal static partial class Program
    {
        private static async Task RunDesignBasisChecks(McpClient client, string fixture, string root, CancellationToken token)
        {
            var wire = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            var state = ProjectStore.LoadProject(fixture);
            var gravity = new GravityFlowDesignBasis { CommonDesignBasis = new DesignBasis
            { ProjectId = ProjectId, DesignBasisId = "TEST_ONLY_GF", DesignBasisVersion = "GF-1" } };
            gravity.Project.Criteria.Add(new GravityDesignCriterion { Key = "Project.TestOnly", Section = GravityFlowSectionIdentifier.Project,
                Status = GravityCriterionStatus.Defined, Value = StandardValue.FromText(Sentinel) });
            gravity.Loading.Criteria.Add(new GravityDesignCriterion { Key = "Loading.TestOnlyA", Section = GravityFlowSectionIdentifier.Loading });
            gravity.Loading.Criteria.Add(new GravityDesignCriterion { Key = "Loading.TestOnlyB", Section = GravityFlowSectionIdentifier.Loading });
            var storm = new StormFlowDesignBasis { CommonDesignBasis = new DesignBasis
            { ProjectId = ProjectId, DesignBasisId = "TEST_ONLY_SF", DesignBasisVersion = "SF-1" } };
            storm.Project.Criteria.Add(new StormDesignCriterion { Key = "Project.TestOnly", Section = StormFlowSectionIdentifier.Project,
                Status = StormCriterionStatus.Defined, Value = StandardValue.FromText(Sentinel) });
            storm.Rainfall.Criteria.Add(new StormDesignCriterion { Key = "Rainfall.TestOnly", Section = StormFlowSectionIdentifier.Rainfall });
            Check(gravity.Validate().IsValid && storm.Validate().IsValid, "MCP02_TEST_ONLY_DOMAIN_FIXTURES_VALID");
            string gravityJson = wire.Serialize(new
            {
                SchemaVersion = 1, ProjectId, NetworkFingerprint = (string)null,
                DesignBasisFingerprint = gravity.CreateSnapshot().GravityContentFingerprint, ConstraintFingerprint = (string)null,
                Inputs = new GravityEngineeringInputs { ProjectId = ProjectId, ModelFingerprint = new string('a', 64), DesignBasis = gravity },
                RecipesJson = "[]", RecipeConstraintFingerprint = (string)null, PreviousReference = (string)null
            });
            string stormJson = StormFlowDesignBasisSerializer.Serialize(storm);
            string gfRef = "ui02-" + HashText(gravityJson), sfRef = "sf-" + HashText(stormJson);
            string gfPath = Path.Combine(fixture, "engineering", gfRef + ".json"), sfPath = Path.Combine(fixture, "stormflow", sfRef + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(gfPath)); Directory.CreateDirectory(Path.GetDirectoryName(sfPath));
            File.WriteAllText(gfPath, gravityJson, new UTF8Encoding(false)); File.WriteAllText(sfPath, stormJson, new UTF8Encoding(false));
            state.EngineeringDecisionReferenceId = gfRef;
            state.DesignBasisReference = gravity.CommonDesignBasis.ToProjectReference();
            state.StormFlowDesignBasisReference = new DesignBasisReference { ReferenceIdentifier = sfRef, DesignBasisVersion = "SF-1" };
            WriteFixtureState(state, fixture, wire);
            string pristineProjectJson = File.ReadAllText(Path.Combine(fixture, "project.json"));
            var pristine = HashDesignInputs(fixture);
            await ExpectStoredSummary(client, fixture, "GravityFlow", "TEST_ONLY_GF", "GF-1", new[] { "Project", "Loading", "PipeCriteria", "StructureCriteria", "VerticalDesign", "HydraulicCriteria", "SolverSettings" }, 2, token);
            await ExpectStoredSummary(client, fixture, "StormFlow", sfRef, "SF-1", new[] { "Project", "Rainfall", "Hydrology", "NetworkCriteria", "HydraulicCriteria" }, 1, token);
            UnchangedDesign(pristine, fixture, "MCP02_DUAL_MODULE_IDENTITIES_AND_ARTIFACTS_UNCHANGED");

            await ExpectSummaryError(client, fixture, null, "MCP_MODULE_REQUIRED", null, token);
            await ExpectSummaryError(client, fixture, "Unknown", "MCP_MODULE_UNSUPPORTED", null, token);
            await ExpectSummaryError(client, fixture, "gravityflow", "MCP_MODULE_UNSUPPORTED", null, token);
            await ExpectSummaryError(client, fixture, 12, "MCP_MODULE_UNSUPPORTED", null, token);
            await ExpectSummaryError(client, fixture, "GravityFlow", "MCP_ARGUMENTS_NOT_ALLOWED", new Dictionary<string, object> { ["projectPath"] = Sentinel }, token);

            var empty = wire.Deserialize<ProjectState>(wire.Serialize(state));
            empty.EngineeringDecisionReferenceId = null; empty.DesignBasisReference = null; empty.StormFlowDesignBasisReference = null;
            WriteFixtureState(empty, fixture, wire);
            foreach (string module in new[] { "GravityFlow", "StormFlow" })
            {
                var before = HashDesignInputs(fixture);
                CallToolResult absent = await client.CallToolAsync(DesignBasisTools.ToolName, Module(module), cancellationToken: token);
                using (var body = JsonDocument.Parse(Text(absent)))
                    Check(absent.IsError != true && body.RootElement.GetProperty("storageStatus").GetString() == "NOT_SAVED" &&
                        body.RootElement.GetProperty("structuralValidationStatus").GetString() == "NOT_AVAILABLE", "MCP02_NOT_SAVED_" + module);
                UnchangedDesign(before, fixture, "MCP02_ABSENT_READ_ONLY_" + module);
            }
            WriteFixtureState(state, fixture, wire);

            File.Delete(gfPath);
            await ExpectSummaryError(client, fixture, "GravityFlow", "ENGINEERING_ARTIFACT_MISSING", null, token);
            File.WriteAllText(gfPath, gravityJson, new UTF8Encoding(false));
            File.Delete(sfPath);
            await ExpectSummaryError(client, fixture, "StormFlow", "STORMS_BASIS_MISSING", null, token);
            File.WriteAllText(sfPath, stormJson + Sentinel, new UTF8Encoding(false));
            await ExpectSummaryError(client, fixture, "StormFlow", "STORMS_BASIS_IDENTITY_MISMATCH", null, token);
            await ExpectStoredSummary(client, fixture, "GravityFlow", "TEST_ONLY_GF", "GF-1", new[] { "Project", "Loading", "PipeCriteria", "StructureCriteria", "VerticalDesign", "HydraulicCriteria", "SolverSettings" }, 2, token);
            File.WriteAllText(sfPath, stormJson, new UTF8Encoding(false));
            File.WriteAllText(gfPath, gravityJson + Sentinel, new UTF8Encoding(false));
            await ExpectSummaryError(client, fixture, "GravityFlow", "ENGINEERING_CONTENT_IDENTITY_MISMATCH", null, token);
            await ExpectStoredSummary(client, fixture, "StormFlow", sfRef, "SF-1", new[] { "Project", "Rainfall", "Hydrology", "NetworkCriteria", "HydraulicCriteria" }, 1, token);
            File.WriteAllText(gfPath, gravityJson, new UTF8Encoding(false));

            state.DesignBasisReference.DesignBasisVersion = "stale";
            WriteFixtureState(state, fixture, wire);
            await ExpectSummaryError(client, fixture, "GravityFlow", "ENGINEERING_REFERENCE_MISMATCH", null, token);
            state.DesignBasisReference.DesignBasisVersion = "GF-1";
            state.StormFlowDesignBasisReference.DesignBasisVersion = "stale";
            WriteFixtureState(state, fixture, wire);
            await ExpectSummaryError(client, fixture, "StormFlow", "STORMS_REFERENCE_MISMATCH", null, token);
            state.StormFlowDesignBasisReference.DesignBasisVersion = "SF-1";
            state.Model.ProtectedBaselineFingerprint = new string('b', 64);
            WriteFixtureState(state, fixture, wire);
            await ExpectSummaryError(client, fixture, "GravityFlow", "ENGINEERING_REFERENCE_MISMATCH", null, token);
            state.Model.ProtectedBaselineFingerprint = new string('a', 64);
            state.EngineeringDecisionReferenceId = "ui02-../" + Sentinel;
            WriteFixtureState(state, fixture, wire);
            await ExpectSummaryError(client, fixture, "GravityFlow", "ENGINEERING_REFERENCE_INVALID", null, token);
            state.EngineeringDecisionReferenceId = gfRef;
            state.StormFlowDesignBasisReference.ReferenceIdentifier = "sf-../" + Sentinel;
            WriteFixtureState(state, fixture, wire);
            await ExpectSummaryError(client, fixture, "StormFlow", "STORMS_REFERENCE_INVALID", null, token);
            state.StormFlowDesignBasisReference.ReferenceIdentifier = gfRef;
            WriteFixtureState(state, fixture, wire);
            await ExpectSummaryError(client, fixture, "StormFlow", "STORMS_REFERENCE_INVALID", null, token);
            state.StormFlowDesignBasisReference.ReferenceIdentifier = sfRef;
            WriteFixtureState(state, fixture, wire);

            string malformed = "{" + Sentinel;
            string malformedRef = "ui02-" + HashText(malformed);
            string malformedPath = Path.Combine(fixture, "engineering", malformedRef + ".json");
            File.WriteAllText(malformedPath, malformed, new UTF8Encoding(false));
            state.EngineeringDecisionReferenceId = malformedRef; WriteFixtureState(state, fixture, wire);
            await ExpectSummaryError(client, fixture, "GravityFlow", "ENGINEERING_ARTIFACT_INVALID", null, token);
            state.EngineeringDecisionReferenceId = gfRef; File.Delete(malformedPath);

            storm.CommonDesignBasis.ProjectId = "TEST_ONLY_OTHER_PROJECT";
            string mismatchJson = StormFlowDesignBasisSerializer.Serialize(storm);
            string mismatchRef = "sf-" + HashText(mismatchJson), mismatchPath = Path.Combine(fixture, "stormflow", mismatchRef + ".json");
            File.WriteAllText(mismatchPath, mismatchJson, new UTF8Encoding(false));
            state.StormFlowDesignBasisReference.ReferenceIdentifier = mismatchRef; WriteFixtureState(state, fixture, wire);
            await ExpectSummaryError(client, fixture, "StormFlow", "STORMS_BASIS_IDENTITY_MISMATCH", null, token);
            File.Delete(mismatchPath); storm.CommonDesignBasis.ProjectId = ProjectId;
            storm.Project.Criteria[0].Value = null;
            string invalidJson = StormFlowDesignBasisSerializer.Serialize(storm);
            string invalidRef = "sf-" + HashText(invalidJson), invalidPath = Path.Combine(fixture, "stormflow", invalidRef + ".json");
            File.WriteAllText(invalidPath, invalidJson, new UTF8Encoding(false));
            state.StormFlowDesignBasisReference.ReferenceIdentifier = invalidRef; WriteFixtureState(state, fixture, wire);
            await ExpectSummaryError(client, fixture, "StormFlow", "DESIGN_BASIS_STRUCTURALLY_INVALID", null, token);
            File.Delete(invalidPath); state.StormFlowDesignBasisReference.ReferenceIdentifier = sfRef; WriteFixtureState(state, fixture, wire);

            SetOversized(sfPath);
            await ExpectSummaryError(client, fixture, "StormFlow", "PROJECT_INPUT_TOO_LARGE", null, token);
            File.WriteAllText(sfPath, stormJson, new UTF8Encoding(false));
            var beforeBusy = HashDesignInputs(fixture);
            using (var lease = new FileStream(sfPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                CallToolResult busy = await client.CallToolAsync(DesignBasisTools.ToolName, Module("StormFlow"), cancellationToken: token);
                CheckSummaryError(busy, fixture, "PROJECT_INPUT_BUSY", true);
            }
            UnchangedDesign(beforeBusy, fixture, "MCP02_ARTIFACT_BUSY_READ_ONLY");

            string stormFolder = Path.Combine(fixture, "stormflow"), backup = Path.Combine(root, "stormflow-test-backup");
            RequireOwnedFixturePath(root, stormFolder); RequireOwnedFixturePath(root, backup);
            Directory.Move(stormFolder, backup);
            CreateJunction(stormFolder, backup);
            await ExpectSummaryError(client, fixture, "StormFlow", "PROJECT_REPARSE_POINT_REJECTED", null, token);
            RemoveJunction(stormFolder); // Non-recursive unlinking of the owned junction only.
            RequireOwnedFixturePath(root, backup); RequireOwnedFixturePath(root, stormFolder);
            Directory.Move(backup, stormFolder);
            File.WriteAllText(Path.Combine(fixture, "project.json"), pristineProjectJson, new UTF8Encoding(false));
            UnchangedDesign(pristine, fixture, "MCP02_ALL_FIXTURE_RESTORATIONS_AND_ARTIFACT_INTEGRITY");
        }

        private static async Task ExpectStoredSummary(McpClient client, string fixture, string module, string reference, string version, string[] names, int undefined, CancellationToken token)
        {
            activeCheck = "MCP02_STORED_" + module;
            var before = HashDesignInputs(fixture);
            CallToolResult result = await client.CallToolAsync(DesignBasisTools.ToolName, Module(module), cancellationToken: token);
            using (var body = JsonDocument.Parse(Text(result)))
            {
                var p = body.RootElement;
                Check(result.IsError != true && p.GetProperty("kind").GetString() == "STORED_DESIGN_BASIS_SUMMARY" &&
                    p.GetProperty("projectId").GetString() == ProjectId && p.GetProperty("module").GetString() == module &&
                    p.GetProperty("storageStatus").GetString() == "STORED" && p.GetProperty("referenceIdentifier").GetString() == reference &&
                    p.GetProperty("designBasisVersion").GetString() == version && p.GetProperty("reviewState").GetString() == "Draft" &&
                    p.GetProperty("structuralValidationStatus").GetString() == "VALID", "MCP02_STORED_IDENTITY_" + module);
                var rows = p.GetProperty("sections").EnumerateArray().ToArray();
                Check(rows.Select(r => r.GetProperty("name").GetString()).SequenceEqual(names) &&
                    rows[0].GetProperty("definedCriterionCount").GetInt32() == 1 &&
                    rows.Sum(r => r.GetProperty("definedCriterionCount").GetInt32()) == 1 &&
                    rows[1].GetProperty("undefinedCriterionCount").GetInt32() == undefined &&
                    rows.Sum(r => r.GetProperty("undefinedCriterionCount").GetInt32()) == undefined &&
                    p.GetProperty("validationErrorCount").GetInt32() == 0 && p.GetProperty("validationWarningCount").GetInt32() == 0 &&
                    !p.TryGetProperty("storedNetworkFingerprint", out _), "MCP02_CANONICAL_COUNTS_AND_NO_INVENTED_NETWORK_" + module);
                Check(!Text(result).Contains(Sentinel) && !Text(result).Contains(fixture) && !Text(result).Contains("criteria") &&
                    !Text(result).Contains("audit") && Text(result).Length < 4096, "MCP02_BOUNDED_PRIVATE_CONTENT_EXCLUDED_" + module);
            }
            UnchangedDesign(before, fixture, "MCP02_STORED_READ_ONLY_" + module);
        }
        private static async Task ExpectSummaryError(McpClient client, string fixture, object module, string expected, Dictionary<string, object> extra, CancellationToken token)
        {
            activeCheck = expected;
            var before = HashDesignInputs(fixture);
            var arguments = module == null ? new Dictionary<string, object>() : new Dictionary<string, object> { ["module"] = module };
            if (extra != null) foreach (var pair in extra) arguments.Add(pair.Key, pair.Value);
            CallToolResult result = await client.CallToolAsync(DesignBasisTools.ToolName, arguments, cancellationToken: token);
            CheckSummaryError(result, fixture, expected, module is string && ((string)module == "GravityFlow" || (string)module == "StormFlow") && extra == null);
            UnchangedDesign(before, fixture, "MCP02_REJECTION_READ_ONLY_" + expected);
        }
        private static void CheckSummaryError(CallToolResult result, string fixture, string expected, bool invalid)
        {
            Check(result.IsError == true && Classification(result) == expected, "MCP02_REJECT_" + expected);
            Check(!Text(result).Contains(Sentinel) && !Text(result).Contains(fixture) && !Text(result).Contains("Exception"), "MCP02_SANITIZE_" + expected);
            if (invalid) using (var body = JsonDocument.Parse(Text(result))) Check(body.RootElement.GetProperty("storageStatus").GetString() == "INVALID", "MCP02_CLASSIFIED_INVALID_" + expected);
        }
        private static Dictionary<string, object> Module(string name) => new Dictionary<string, object> { ["module"] = name };
        private static void RequireOwnedFixturePath(string root, string path)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(path);
            Check(Path.GetFileName(fullRoot).StartsWith("SewerPilot-MCP01-TEST_ONLY-", StringComparison.Ordinal) &&
                fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "MCP02_OWNED_TEMP_PATH_VERIFIED");
        }
        private static void WriteFixtureState(ProjectState state, string directory, JavaScriptSerializer wire) => File.WriteAllText(Path.Combine(directory, "project.json"), wire.Serialize(state), new UTF8Encoding(false));
        private static string HashText(string value)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        private static Dictionary<string, string> HashDesignInputs(string fixture)
        {
            var files = Directory.GetFiles(fixture).ToList();
            foreach (string folder in new[] { "engineering", "stormflow" })
            {
                string path = Path.Combine(fixture, folder);
                if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) files.AddRange(Directory.GetFiles(path));
            }
            return files.ToDictionary(f => f.Substring(fixture.Length), Hash);
        }
        private static void UnchangedDesign(Dictionary<string, string> before, string fixture, string name)
        {
            var after = HashDesignInputs(fixture);
            Check(before.Count == after.Count && before.All(p => after.ContainsKey(p.Key) && after[p.Key] == p.Value), name);
        }
    }
}
