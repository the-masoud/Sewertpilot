using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private static async Task RunGravityCandidatePreviewChecks(McpClient client, string fixture, string root, CancellationToken token)
        {
            activeCheck = "MCP03_GRAVITY_PREVIEW_CHECKS_START";
            var wire = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            var state = ProjectStore.LoadProject(fixture);
            string pristineProjectJson = File.ReadAllText(Path.Combine(fixture, "project.json"));
            string gfRef = state.EngineeringDecisionReferenceId;
            string gfPath = Path.Combine(fixture, "engineering", gfRef + ".json");
            string gravityJson = File.ReadAllText(gfPath, Encoding.UTF8);
            var pristine = HashDesignInputs(fixture);

            // 1. Tool listing and schemas
            activeCheck = "MCP03_TOOLS_LIST_AND_SCHEMAS";
            var tools = await client.ListToolsAsync(cancellationToken: token);
            var feasTool = tools.Single(t => t.Name == GravityCandidatePreviewTools.FeasibilityToolName);
            var candTool = tools.Single(t => t.Name == GravityCandidatePreviewTools.CandidatePreviewToolName);

            Check(feasTool.ProtocolTool.Annotations.ReadOnlyHint == true && feasTool.ProtocolTool.Annotations.DestructiveHint == false,
                "MCP03_FEASIBILITY_READ_ONLY_HINTS");
            Check(feasTool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Count() == 0 &&
                !feasTool.ProtocolTool.InputSchema.GetProperty("additionalProperties").GetBoolean(),
                "MCP03_FEASIBILITY_ZERO_ARG_SCHEMA");

            Check(candTool.ProtocolTool.Annotations.ReadOnlyHint == true && candTool.ProtocolTool.Annotations.DestructiveHint == false,
                "MCP03_CANDIDATE_PREVIEW_READ_ONLY_HINTS");
            Check(candTool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Count() == 2 &&
                candTool.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("candidateId", out _) &&
                candTool.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("recipeId", out _) &&
                !candTool.ProtocolTool.InputSchema.GetProperty("additionalProperties").GetBoolean(),
                "MCP03_CANDIDATE_PREVIEW_SCHEMA");

            // 2. Initial stored state checks (fixture has SourceRead = null)
            activeCheck = "MCP03_FEASIBILITY_STORED_INCOMPLETE";
            var before = HashDesignInputs(fixture);
            CallToolResult feasResult = await client.CallToolAsync(GravityCandidatePreviewTools.FeasibilityToolName, cancellationToken: token);
            Check(feasResult.IsError != true, "MCP03_FEASIBILITY_CALL_SUCCESS");
            using (var body = JsonDocument.Parse(Text(feasResult)))
            {
                var elem = body.RootElement;
                Check(elem.GetProperty("kind").GetString() == "STORED_VERTICAL_FEASIBILITY_SUMMARY", "MCP03_FEASIBILITY_KIND");
                Check(elem.GetProperty("projectId").GetString() == ProjectId, "MCP03_FEASIBILITY_PROJECT_ID");
                Check(elem.GetProperty("storageStatus").GetString() == "STORED", "MCP03_FEASIBILITY_STORAGE_STORED");
                Check(elem.GetProperty("feasibilityStatus").GetString() == "Incomplete", "MCP03_FEASIBILITY_STATUS_INCOMPLETE");
                var items = elem.GetProperty("incompleteItems").EnumerateArray().Select(x => x.GetString()).ToArray();
                Check(items.Contains("SOURCE_READ_UNAVAILABLE"), "MCP03_FEASIBILITY_INCOMPLETE_REASON");
                Check(!Text(feasResult).Contains(Sentinel) && !Text(feasResult).Contains(fixture), "MCP03_FEASIBILITY_SANITIZED");
            }
            UnchangedDesign(before, fixture, "MCP03_FEASIBILITY_READ_ONLY");

            activeCheck = "MCP03_CANDIDATE_PREVIEW_STORED_EMPTY";
            before = HashDesignInputs(fixture);
            CallToolResult candResult = await client.CallToolAsync(GravityCandidatePreviewTools.CandidatePreviewToolName, cancellationToken: token);
            Check(candResult.IsError != true, "MCP03_CANDIDATE_PREVIEW_CALL_SUCCESS");
            using (var body = JsonDocument.Parse(Text(candResult)))
            {
                var elem = body.RootElement;
                Check(elem.GetProperty("kind").GetString() == "STORED_GRAVITY_CANDIDATE_PREVIEW", "MCP03_CANDIDATE_PREVIEW_KIND");
                Check(elem.GetProperty("projectId").GetString() == ProjectId, "MCP03_CANDIDATE_PREVIEW_PROJECT_ID");
                Check(elem.GetProperty("storageStatus").GetString() == "STORED", "MCP03_CANDIDATE_PREVIEW_STORAGE_STORED");
                Check(elem.GetProperty("candidateCount").GetInt32() == 0, "MCP03_CANDIDATE_PREVIEW_ZERO_COUNT");
                Check(!Text(candResult).Contains(Sentinel) && !Text(candResult).Contains(fixture), "MCP03_CANDIDATE_PREVIEW_SANITIZED");
            }
            UnchangedDesign(before, fixture, "MCP03_CANDIDATE_PREVIEW_READ_ONLY");

            // 3. Argument validation rejections
            activeCheck = "MCP03_FEASIBILITY_REJECT_EXTRA_ARGS";
            CallToolResult feasExtra = await client.CallToolAsync(GravityCandidatePreviewTools.FeasibilityToolName,
                new Dictionary<string, object> { ["unexpected"] = "val" }, cancellationToken: token);
            Check(feasExtra.IsError == true && Classification(feasExtra) == "MCP_ARGUMENTS_NOT_ALLOWED", "MCP03_FEASIBILITY_REJECT_EXTRA_ARGS");

            activeCheck = "MCP03_CANDIDATE_PREVIEW_REJECT_EXTRA_ARGS";
            CallToolResult candExtra = await client.CallToolAsync(GravityCandidatePreviewTools.CandidatePreviewToolName,
                new Dictionary<string, object> { ["unexpected"] = "val" }, cancellationToken: token);
            Check(candExtra.IsError == true && Classification(candExtra) == "MCP_ARGUMENTS_NOT_ALLOWED", "MCP03_CANDIDATE_PREVIEW_REJECT_EXTRA_ARGS");

            activeCheck = "MCP03_CANDIDATE_PREVIEW_REJECT_INVALID_TYPE";
            CallToolResult candBadType = await client.CallToolAsync(GravityCandidatePreviewTools.CandidatePreviewToolName,
                new Dictionary<string, object> { ["candidateId"] = 99999 }, cancellationToken: token);
            Check(candBadType.IsError == true && Classification(candBadType) == "MCP_ARGUMENT_TYPE_INVALID", "MCP03_CANDIDATE_PREVIEW_REJECT_INVALID_TYPE");

            // 4. Stored candidate preview with non-existent candidateId / recipeId
            activeCheck = "MCP03_CANDIDATE_PREVIEW_FILTER_NONEXISTENT";
            CallToolResult candFiltered = await client.CallToolAsync(GravityCandidatePreviewTools.CandidatePreviewToolName,
                new Dictionary<string, object> { ["candidateId"] = "NON_EXISTENT_ID" }, cancellationToken: token);
            Check(candFiltered.IsError != true, "MCP03_CANDIDATE_PREVIEW_FILTER_CALL");
            using (var body = JsonDocument.Parse(Text(candFiltered)))
            {
                var elem = body.RootElement;
                Check(elem.GetProperty("candidateCount").GetInt32() == 0, "MCP03_CANDIDATE_PREVIEW_FILTERED_ZERO");
            }

            // 5. NOT_SAVED state (no engineering reference in project.json)
            activeCheck = "MCP03_NOT_SAVED_CHECKS";
            var emptyState = wire.Deserialize<ProjectState>(wire.Serialize(state));
            emptyState.EngineeringDecisionReferenceId = null;
            emptyState.DesignBasisReference = null;
            WriteFixtureState(emptyState, fixture, wire);

            CallToolResult feasNotSaved = await client.CallToolAsync(GravityCandidatePreviewTools.FeasibilityToolName, cancellationToken: token);
            Check(feasNotSaved.IsError != true, "MCP03_FEASIBILITY_NOT_SAVED_SUCCESS");
            using (var body = JsonDocument.Parse(Text(feasNotSaved)))
            {
                var elem = body.RootElement;
                Check(elem.GetProperty("storageStatus").GetString() == "NOT_SAVED", "MCP03_FEASIBILITY_STATUS_NOT_SAVED");
                Check(elem.GetProperty("feasibilityStatus").GetString() == "NOT_AVAILABLE", "MCP03_FEASIBILITY_AVAIL_NOT_SAVED");
            }

            CallToolResult candNotSaved = await client.CallToolAsync(GravityCandidatePreviewTools.CandidatePreviewToolName, cancellationToken: token);
            Check(candNotSaved.IsError != true, "MCP03_CANDIDATE_PREVIEW_NOT_SAVED_SUCCESS");
            using (var body = JsonDocument.Parse(Text(candNotSaved)))
            {
                var elem = body.RootElement;
                Check(elem.GetProperty("storageStatus").GetString() == "NOT_SAVED", "MCP03_CANDIDATE_PREVIEW_STATUS_NOT_SAVED");
                Check(elem.GetProperty("candidateCount").GetInt32() == 0, "MCP03_CANDIDATE_PREVIEW_COUNT_NOT_SAVED");
            }
            WriteFixtureState(state, fixture, wire);

            // 6. Fail-closed: missing artifact file
            activeCheck = "MCP03_FAIL_CLOSED_MISSING_ARTIFACT";
            File.Delete(gfPath);
            CallToolResult feasMissing = await client.CallToolAsync(GravityCandidatePreviewTools.FeasibilityToolName, cancellationToken: token);
            Check(feasMissing.IsError == true && Classification(feasMissing) == "ENGINEERING_ARTIFACT_MISSING", "MCP03_FEASIBILITY_ARTIFACT_MISSING");
            CallToolResult candMissing = await client.CallToolAsync(GravityCandidatePreviewTools.CandidatePreviewToolName, cancellationToken: token);
            Check(candMissing.IsError == true && Classification(candMissing) == "ENGINEERING_ARTIFACT_MISSING", "MCP03_CANDIDATE_PREVIEW_ARTIFACT_MISSING");
            File.WriteAllText(gfPath, gravityJson, new UTF8Encoding(false));

            // 7. Fail-closed: corrupted artifact content
            activeCheck = "MCP03_FAIL_CLOSED_CONTENT_MISMATCH";
            File.WriteAllText(gfPath, gravityJson + Sentinel, new UTF8Encoding(false));
            CallToolResult feasMismatch = await client.CallToolAsync(GravityCandidatePreviewTools.FeasibilityToolName, cancellationToken: token);
            Check(feasMismatch.IsError == true && Classification(feasMismatch) == "ENGINEERING_CONTENT_IDENTITY_MISMATCH", "MCP03_FEASIBILITY_CONTENT_MISMATCH");
            CallToolResult candMismatch = await client.CallToolAsync(GravityCandidatePreviewTools.CandidatePreviewToolName, cancellationToken: token);
            Check(candMismatch.IsError == true && Classification(candMismatch) == "ENGINEERING_CONTENT_IDENTITY_MISMATCH", "MCP03_CANDIDATE_PREVIEW_CONTENT_MISMATCH");
            File.WriteAllText(gfPath, gravityJson, new UTF8Encoding(false));

            // 8. Fail-closed: busy artifact input
            activeCheck = "MCP03_FAIL_CLOSED_BUSY_ARTIFACT";
            using (var lockStream = new FileStream(gfPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                CallToolResult feasBusy = await client.CallToolAsync(GravityCandidatePreviewTools.FeasibilityToolName, cancellationToken: token);
                Check(feasBusy.IsError == true && Classification(feasBusy) == "PROJECT_INPUT_BUSY", "MCP03_FEASIBILITY_BUSY");
                CallToolResult candBusy = await client.CallToolAsync(GravityCandidatePreviewTools.CandidatePreviewToolName, cancellationToken: token);
                Check(candBusy.IsError == true && Classification(candBusy) == "PROJECT_INPUT_BUSY", "MCP03_CANDIDATE_PREVIEW_BUSY");
            }

            // 9. Restore fixture to pristine state and verify unchanged inputs
            activeCheck = "MCP03_INTEGRITY_FINAL_RESTORE";
            File.WriteAllText(Path.Combine(fixture, "project.json"), pristineProjectJson, new UTF8Encoding(false));
            File.WriteAllText(gfPath, gravityJson, new UTF8Encoding(false));
            UnchangedDesign(pristine, fixture, "MCP03_ALL_FIXTURE_RESTORATIONS_AND_INTEGRITY");
        }
    }
}
