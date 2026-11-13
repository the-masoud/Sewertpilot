using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace SewerPilot.Mcp.Server
{
    public sealed class DesignBasisTools
    {
        public const string ToolName = "sewerpilot_get_design_basis_summary";
        private readonly ReadOnlyProjectQuery query;
        private readonly ProjectTools metadata;
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        public DesignBasisTools(ReadOnlyProjectQuery query) { this.query = query; metadata = new ProjectTools(query); }

        public ListToolsResult List()
        {
            ListToolsResult result = metadata.List();
            using (JsonDocument schema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"module\":{\"type\":\"string\",\"enum\":[\"GravityFlow\",\"StormFlow\"]}},\"required\":[\"module\"],\"additionalProperties\":false}"))
                result.Tools.Add(new Tool
                {
                    Name = ToolName,
                    Description = "Inspect validated STORED_DESIGN_BASIS_SUMMARY counts for GravityFlow or StormFlow. Structural validation/review state grants no execution authority, engineering approval, live network verification or solver results.",
                    InputSchema = schema.RootElement.Clone(),
                    Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false }
                });
            return result;
        }

        public CallToolResult Call(CallToolRequestParams request)
        {
            if (request == null || request.Name != ToolName) return metadata.Call(request);
            if (request.Arguments == null || !request.Arguments.TryGetValue("module", out JsonElement value)) return Error("MCP_MODULE_REQUIRED", null);
            if (request.Arguments.Count != 1) return Error("MCP_ARGUMENTS_NOT_ALLOWED", null);
            if (value.ValueKind != JsonValueKind.String || (value.GetString() != "GravityFlow" && value.GetString() != "StormFlow"))
                return Error("MCP_MODULE_UNSUPPORTED", null);
            string module = value.GetString();
            try { return Text(JsonSerializer.Serialize(query.ReadDesignBasis(module), Json), false); }
            catch (MetadataReadException error) { return Error(error.Classification, module); }
            catch (Exception) { return Error("DESIGN_BASIS_SUMMARY_UNAVAILABLE", module); }
        }

        private static CallToolResult Error(string classification, string module)
        {
            Console.Error.WriteLine("MCP_DESIGN_BASIS_REJECTED: " + classification);
            return Text(JsonSerializer.Serialize(new { kind = "STORED_DESIGN_BASIS_SUMMARY", module,
                storageStatus = module == null ? null : "INVALID", classification }, Json), true);
        }
        private static CallToolResult Text(string value, bool error) => new CallToolResult
        { IsError = error, Content = new List<ContentBlock> { new TextContentBlock { Text = value } } };
    }
}
