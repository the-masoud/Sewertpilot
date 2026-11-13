using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace SewerPilot.Mcp.Server
{
    public sealed class GravityCandidatePreviewTools
    {
        public const string FeasibilityToolName = "sewerpilot_assess_vertical_feasibility";
        public const string CandidatePreviewToolName = "sewerpilot_preview_gravity_candidate";

        private readonly ReadOnlyProjectQuery query;
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        public GravityCandidatePreviewTools(ReadOnlyProjectQuery query)
        {
            this.query = query ?? throw new ArgumentNullException(nameof(query));
        }

        public void RegisterTools(IList<Tool> toolList)
        {
            if (toolList == null) throw new ArgumentNullException(nameof(toolList));

            using (JsonDocument schema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"))
            {
                toolList.Add(new Tool
                {
                    Name = FeasibilityToolName,
                    Description = "Assess GravityFlow vertical design feasibility, constraint propagation, elevation bounds, and engineering decision requests for the active project. Non-mutating read-only calculation.",
                    InputSchema = schema.RootElement.Clone(),
                    Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false }
                });
            }

            using (JsonDocument schema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"candidateId\":{\"type\":\"string\",\"description\":\"Optional specific candidate identifier to preview\"},\"recipeId\":{\"type\":\"string\",\"description\":\"Optional specific recipe identifier to preview\"}},\"additionalProperties\":false}"))
            {
                toolList.Add(new Tool
                {
                    Name = CandidatePreviewToolName,
                    Description = "Preview candidate design vertical geometry (invert elevations, slopes, crowns, covers) and recipe resolutions for GravityFlow. Non-mutating read-only preview.",
                    InputSchema = schema.RootElement.Clone(),
                    Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false }
                });
            }
        }

        public bool CanHandle(string toolName)
        {
            return toolName == FeasibilityToolName || toolName == CandidatePreviewToolName;
        }

        public CallToolResult Handle(CallToolRequestParams request)
        {
            if (request == null) return Error("TOOL_REQUEST_NULL");

            if (request.Name == FeasibilityToolName)
            {
                if (request.Arguments != null && request.Arguments.Count != 0)
                    return Error("MCP_ARGUMENTS_NOT_ALLOWED");

                try
                {
                    var result = query.ReadVerticalFeasibility();
                    return Text(JsonSerializer.Serialize(result, Json), false);
                }
                catch (MetadataReadException error) { return Error(error.Classification); }
                catch (Exception) { return Error("VERTICAL_FEASIBILITY_UNAVAILABLE"); }
            }

            if (request.Name == CandidatePreviewToolName)
            {
                string candidateId = null;
                string recipeId = null;

                if (request.Arguments != null)
                {
                    foreach (var key in request.Arguments.Keys)
                    {
                        if (key != "candidateId" && key != "recipeId")
                            return Error("MCP_ARGUMENTS_NOT_ALLOWED");
                    }

                    if (request.Arguments.TryGetValue("candidateId", out JsonElement cElem))
                    {
                        if (cElem.ValueKind != JsonValueKind.String)
                            return Error("MCP_ARGUMENT_TYPE_INVALID");
                        candidateId = cElem.GetString();
                    }

                    if (request.Arguments.TryGetValue("recipeId", out JsonElement rElem))
                    {
                        if (rElem.ValueKind != JsonValueKind.String)
                            return Error("MCP_ARGUMENT_TYPE_INVALID");
                        recipeId = rElem.GetString();
                    }
                }

                try
                {
                    var result = query.ReadCandidatePreview(candidateId, recipeId);
                    return Text(JsonSerializer.Serialize(result, Json), false);
                }
                catch (MetadataReadException error) { return Error(error.Classification); }
                catch (Exception) { return Error("CANDIDATE_PREVIEW_UNAVAILABLE"); }
            }

            return Error("TOOL_NOT_AVAILABLE");
        }

        private static CallToolResult Error(string classification)
        {
            Console.Error.WriteLine("MCP_GRAVITY_PREVIEW_REJECTED: " + classification);
            return Text(JsonSerializer.Serialize(new { classification, message = "Gravity design capability rejected." }, Json), true);
        }

        private static CallToolResult Text(string value, bool error) => new CallToolResult
        {
            IsError = error,
            Content = new List<ContentBlock> { new TextContentBlock { Text = value } }
        };
    }
}
