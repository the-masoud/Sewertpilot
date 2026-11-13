using System;
using System.Collections.Generic;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace SewerPilot.Mcp.Server
{
    public sealed class ProjectTools
    {
        public const string ToolName = "sewerpilot_get_project_info";
        private readonly ReadOnlyProjectQuery query;
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public ProjectTools(ReadOnlyProjectQuery query) { this.query = query; }

        public ListToolsResult List()
        {
            using (JsonDocument schema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"))
                return new ListToolsResult
                {
                    Tools = new List<Tool>
                    {
                        new Tool
                        {
                            Name = ToolName,
                            Description = "Read STORED_PROJECT_METADATA from the operator-configured project. No live Bentley access, engineering-content verification or hydraulic results.",
                            InputSchema = schema.RootElement.Clone(),
                            Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false }
                        }
                    }
                };
        }

        public CallToolResult Call(CallToolRequestParams request)
        {
            if (request == null || request.Name != ToolName) return Error("TOOL_NOT_AVAILABLE");
            if (request.Arguments != null && request.Arguments.Count != 0) return Error("MCP_ARGUMENTS_NOT_ALLOWED");
            try { return Text(JsonSerializer.Serialize(query.Read(), Json), false); }
            catch (MetadataReadException error) { return Error(error.Classification); }
            catch (Exception) { return Error("PROJECT_METADATA_UNAVAILABLE"); }
        }

        private static CallToolResult Error(string classification)
        {
            Console.Error.WriteLine("MCP_PROJECT_INFO_REJECTED: " + classification);
            return Text(JsonSerializer.Serialize(new { classification, message = "Stored project metadata is unavailable." }, Json), true);
        }

        private static CallToolResult Text(string value, bool error) => new CallToolResult
        {
            IsError = error,
            Content = new List<ContentBlock> { new TextContentBlock { Text = value } }
        };
    }
}
