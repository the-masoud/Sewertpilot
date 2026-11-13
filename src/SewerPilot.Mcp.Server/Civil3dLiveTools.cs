using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.Mcp.Server
{
    public sealed class Civil3dLiveTools
    {
        public const string LiveSyncToolName = "sewerpilot_civil3d_live_sync";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        public void RegisterTools(IList<Tool> toolList)
        {
            if (toolList == null) throw new ArgumentNullException(nameof(toolList));

            using (JsonDocument schema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"targetElementName\":{\"type\":\"string\",\"description\":\"Optional name of structure or pipe to mutate\"},\"targetProperty\":{\"type\":\"string\",\"description\":\"Optional property name to mutate (e.g. RimElevation, SumpElevation)\"},\"targetMutatedValue\":{\"type\":\"number\",\"description\":\"Optional numeric value to apply\"}},\"additionalProperties\":false}"))
            {
                toolList.Add(new Tool
                {
                    Name = LiveSyncToolName,
                    Description = "Execute live bidirectional IPC synchronization with installed Autodesk Civil 3D. Extracts native network, maps to canonical domain model, applies controlled mutation to scratch drawing, re-reads active instance to verify parity, and preserves production drawings.",
                    InputSchema = schema.RootElement.Clone(),
                    Annotations = new ToolAnnotations { ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = false, OpenWorldHint = true }
                });
            }
        }

        public bool CanHandle(string toolName)
        {
            return toolName == LiveSyncToolName;
        }

        public CallToolResult Handle(CallToolRequestParams request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var options = new Civil3dLiveOptions();

            if (request.Arguments != null)
            {
                if (request.Arguments.TryGetValue("targetElementName", out var elemObj) && elemObj is JsonElement elemElem && elemElem.ValueKind == JsonValueKind.String)
                {
                    options.TargetElementName = elemElem.GetString();
                }
                if (request.Arguments.TryGetValue("targetProperty", out var propObj) && propObj is JsonElement propElem && propElem.ValueKind == JsonValueKind.String)
                {
                    options.TargetProperty = propElem.GetString();
                }
                if (request.Arguments.TryGetValue("targetMutatedValue", out var valObj) && valObj is JsonElement valElem && valElem.ValueKind == JsonValueKind.Number)
                {
                    options.TargetMutatedValue = valElem.GetDouble();
                }
            }

            var result = Civil3dLiveWorkflow.ExecuteLiveRoundTrip(options);

            var payload = new
            {
                success = result.Success,
                errorMessage = result.ErrorMessage,
                durationSeconds = result.Duration.TotalSeconds,
                processInfo = result.ProcessInfo != null ? new
                {
                    pid = result.ProcessInfo.Pid,
                    civil3DVersion = result.ProcessInfo.Civil3DVersion,
                    activeDrawing = result.ProcessInfo.ActiveDrawingName
                } : null,
                networkSummary = result.NetworkRead != null ? new
                {
                    pipeCount = result.NetworkRead.Conduits?.Count ?? 0,
                    structureCount = result.NetworkRead.Structures?.Count ?? 0,
                    topologyValid = result.NetworkRead.TopologyCheck?.IsValid ?? false
                } : null,
                snapshotIngested = result.Snapshot != null,
                mutationVerification = new
                {
                    elementName = result.MutatedElementName,
                    propertyName = result.MutatedProperty,
                    preMutationValue = result.PreMutationValue,
                    targetValue = result.TargetMutationValue,
                    postMutationReRead = result.PostMutationReReadValue,
                    parityVerified = result.ParityVerified
                },
                scratchDrawing = result.ScratchDwgPath
            };

            string json = JsonSerializer.Serialize(payload, Json);
            return new CallToolResult
            {
                Content = new List<ContentBlock> { new TextContentBlock { Text = json } },
                IsError = !result.Success
            };
        }
    }
}
