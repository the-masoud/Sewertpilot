using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SewerPilot.Mcp.Server;

namespace SewerPilot.Mcp.Server.Tests
{
    internal static partial class Program
    {
        private static async Task RunStormFlowCalculationChecks(McpClient client, CancellationToken token)
        {
            activeCheck = "STORMFLOW_MCP_CALCULATIONS_START";

            // 1. Rational Runoff Tool Check
            activeCheck = "MCP_RATIONAL_TOOL_SUCCESS";
            var rationalArgs = new Dictionary<string, object>
            {
                ["areaAcres"] = 3.5,
                ["runoffCoefficient"] = 0.85,
                ["timeOfConcentrationMinutes"] = 12.0,
                ["returnPeriodYears"] = 10,
                ["idfA"] = 60.0,
                ["idfB"] = 12.0,
                ["idfC"] = 1.0,
                ["catchmentId"] = "TEST-CAT-01"
            };
            CallToolResult rationalResult = await client.CallToolAsync(StormFlowCalculationTools.RationalToolName, rationalArgs, cancellationToken: token);
            Check(rationalResult.IsError != true, "MCP_RATIONAL_TOOL_SUCCESS");
            using (var doc = JsonDocument.Parse(Text(rationalResult)))
            {
                var rootElem = doc.RootElement;
                Check(rootElem.GetProperty("catchmentId").GetString() == "TEST-CAT-01", "MCP_RATIONAL_CATCHMENT_ID");
                Check(rootElem.GetProperty("returnPeriodYears").GetInt32() == 10, "MCP_RATIONAL_RETURN_PERIOD");
                Check(rootElem.GetProperty("peakDischargeCfs").GetDouble() > 0.0, "MCP_RATIONAL_PEAK_DISCHARGE_POSITIVE");
                Check(rootElem.GetProperty("totalVolumeCubicFeet").GetDouble() > 0.0, "MCP_RATIONAL_VOLUME_POSITIVE");
            }

            // 1b. Rational Missing Argument Failure
            activeCheck = "MCP_RATIONAL_REJECT_MISSING_ARGS";
            var rationalBadArgs = new Dictionary<string, object> { ["areaAcres"] = 3.5 };
            CallToolResult rationalBadResult = await client.CallToolAsync(StormFlowCalculationTools.RationalToolName, rationalBadArgs, cancellationToken: token);
            Check(rationalBadResult.IsError == true, "MCP_RATIONAL_REJECT_MISSING_ARGS");

            // 2. Gutter & Inlet Interception Tool Check
            activeCheck = "MCP_INLET_TOOL_SUCCESS";
            var inletArgs = new Dictionary<string, object>
            {
                ["approachFlowCfs"] = 4.2,
                ["longitudinalSlope"] = 0.02,
                ["crossSlope"] = 0.02,
                ["inletType"] = "CurbOpening",
                ["inletLocation"] = "OnContinuousGrade",
                ["lengthFeet"] = 10.0,
                ["allowableSpreadFeet"] = 8.0,
                ["inletId"] = "INL-TEST-01"
            };
            CallToolResult inletResult = await client.CallToolAsync(StormFlowCalculationTools.InletToolName, inletArgs, cancellationToken: token);
            Check(inletResult.IsError != true, "MCP_INLET_TOOL_SUCCESS");
            using (var doc = JsonDocument.Parse(Text(inletResult)))
            {
                var rootElem = doc.RootElement;
                Check(rootElem.GetProperty("inletId").GetString() == "INL-TEST-01", "MCP_INLET_ID");
                Check(rootElem.GetProperty("interceptedFlowCfs").GetDouble() > 0.0, "MCP_INLET_INTERCEPTED_FLOW_POSITIVE");
                Check(rootElem.GetProperty("interceptionEfficiency").GetDouble() > 0.0, "MCP_INLET_EFFICIENCY_POSITIVE");
                Check(rootElem.GetProperty("gutterSpreadFeet").GetDouble() > 0.0, "MCP_INLET_SPREAD_POSITIVE");
            }

            // 3. Conduit Sizing Tool Check
            activeCheck = "MCP_CONDUIT_TOOL_SUCCESS";
            var conduitArgs = new Dictionary<string, object>
            {
                ["designFlowCfs"] = 7.5,
                ["lengthFeet"] = 250.0,
                ["groundElevationStartFeet"] = 105.0,
                ["groundElevationStopFeet"] = 102.0,
                ["conduitId"] = "CON-TEST-01"
            };
            CallToolResult conduitResult = await client.CallToolAsync(StormFlowCalculationTools.ConduitSizingToolName, conduitArgs, cancellationToken: token);
            Check(conduitResult.IsError != true, "MCP_CONDUIT_TOOL_SUCCESS");
            using (var doc = JsonDocument.Parse(Text(conduitResult)))
            {
                var rootElem = doc.RootElement;
                Check(rootElem.GetProperty("conduitId").GetString() == "CON-TEST-01", "MCP_CONDUIT_ID");
                Check(rootElem.GetProperty("selectedDiameterInches").GetInt32() >= 12, "MCP_CONDUIT_DIAMETER_VALID");
                Check(rootElem.GetProperty("fullFlowCapacityCfs").GetDouble() >= 7.5, "MCP_CONDUIT_CAPACITY_ADEQUATE");
                Check(rootElem.GetProperty("isCompliant").GetBoolean() == true, "MCP_CONDUIT_COMPLIANT");
            }

            // 4. Hydraulic Verification Tool Check
            activeCheck = "MCP_HYDRAULIC_VERIFICATION_TOOL_SUCCESS";
            var j1 = new Dictionary<string, object>
            {
                ["nodeId"] = 1,
                ["label"] = "MH-1",
                ["rimElevationFeet"] = 105.0,
                ["invertElevationFeet"] = 98.0
            };
            var j2 = new Dictionary<string, object>
            {
                ["nodeId"] = 2,
                ["label"] = "OUTFALL-2",
                ["rimElevationFeet"] = 101.0,
                ["invertElevationFeet"] = 94.0,
                ["isOutfall"] = true,
                ["tailwaterElevationFeet"] = 95.5
            };
            var c1 = new Dictionary<string, object>
            {
                ["conduitId"] = "PIPE-1",
                ["startNodeId"] = 1,
                ["stopNodeId"] = 2,
                ["lengthFeet"] = 200.0,
                ["selectedDiameterInches"] = 24,
                ["designFlowCfs"] = 6.0,
                ["upstreamInvertFeet"] = 98.0,
                ["downstreamInvertFeet"] = 95.0
            };
            var hydroArgs = new Dictionary<string, object>
            {
                ["junctions"] = new object[] { j1, j2 },
                ["conduits"] = new object[] { c1 },
                ["minFreeboardFeet"] = 1.0
            };
            CallToolResult hydroResult = await client.CallToolAsync(StormFlowCalculationTools.HydraulicVerificationToolName, hydroArgs, cancellationToken: token);
            Check(hydroResult.IsError != true, "MCP_HYDRAULIC_VERIFICATION_TOOL_SUCCESS");
            using (var doc = JsonDocument.Parse(Text(hydroResult)))
            {
                var rootElem = doc.RootElement;
                Check(rootElem.GetProperty("totalHeadLossSystemFeet").GetDouble() >= 0.0, "MCP_HYDRAULIC_HEAD_LOSS_VALID");
                Check(rootElem.GetProperty("conduitStates").GetArrayLength() == 1, "MCP_HYDRAULIC_CONDUIT_STATES_COUNT");
                Check(rootElem.GetProperty("nodeStates").GetArrayLength() == 2, "MCP_HYDRAULIC_NODE_STATES_COUNT");
            }

            // 5. End-to-End Pipeline Tool Check
            activeCheck = "MCP_PIPELINE_TOOL_SUCCESS";
            var pipeArgs = new Dictionary<string, object>
            {
                ["projectName"] = "MCP Verified Municipal Network",
                ["projectId"] = "MCP-E2E-TEST-001",
                ["returnPeriodYears"] = 10,
                ["idfA"] = 60.0,
                ["idfB"] = 12.0,
                ["idfC"] = 1.0,
                ["catchments"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["catchmentId"] = "CAT-A",
                        ["targetNodeId"] = 1,
                        ["totalAreaAcres"] = 2.5,
                        ["runoffCoefficient"] = 0.85,
                        ["timeOfConcentrationMinutes"] = 10.0
                    },
                    new Dictionary<string, object>
                    {
                        ["catchmentId"] = "CAT-B",
                        ["targetNodeId"] = 2,
                        ["totalAreaAcres"] = 3.0,
                        ["runoffCoefficient"] = 0.80,
                        ["timeOfConcentrationMinutes"] = 12.0
                    }
                },
                ["inlets"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["inletId"] = "INL-1",
                        ["targetNodeId"] = 1,
                        ["inletType"] = "CurbOpening",
                        ["inletLocation"] = "OnContinuousGrade",
                        ["lengthFeet"] = 10.0,
                        ["downstreamInletId"] = "INL-2",
                        ["longitudinalSlope"] = 0.015,
                        ["crossSlope"] = 0.02
                    },
                    new Dictionary<string, object>
                    {
                        ["inletId"] = "INL-2",
                        ["targetNodeId"] = 2,
                        ["inletType"] = "CurbOpening",
                        ["inletLocation"] = "InSag",
                        ["lengthFeet"] = 15.0,
                        ["longitudinalSlope"] = 0.010,
                        ["crossSlope"] = 0.02
                    }
                },
                ["conduits"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["conduitId"] = "C-1-2",
                        ["startNodeId"] = 1,
                        ["stopNodeId"] = 2,
                        ["lengthFeet"] = 250.0,
                        ["groundElevationStartFeet"] = 110.0,
                        ["groundElevationStopFeet"] = 106.0
                    },
                    new Dictionary<string, object>
                    {
                        ["conduitId"] = "C-2-3",
                        ["startNodeId"] = 2,
                        ["stopNodeId"] = 3,
                        ["lengthFeet"] = 300.0,
                        ["groundElevationStartFeet"] = 106.0,
                        ["groundElevationStopFeet"] = 100.0
                    }
                },
                ["junctions"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["nodeId"] = 1,
                        ["label"] = "MH-1",
                        ["rimElevationFeet"] = 110.0,
                        ["invertElevationFeet"] = 102.0
                    },
                    new Dictionary<string, object>
                    {
                        ["nodeId"] = 2,
                        ["label"] = "MH-2",
                        ["rimElevationFeet"] = 106.0,
                        ["invertElevationFeet"] = 98.0
                    },
                    new Dictionary<string, object>
                    {
                        ["nodeId"] = 3,
                        ["label"] = "OF-3",
                        ["rimElevationFeet"] = 100.0,
                        ["invertElevationFeet"] = 92.0,
                        ["isOutfall"] = true,
                        ["tailwaterElevationFeet"] = 93.5
                    }
                }
            };
            CallToolResult pipelineResult = await client.CallToolAsync(StormFlowCalculationTools.EndToEndPipelineToolName, pipeArgs, cancellationToken: token);
            Check(pipelineResult.IsError != true, "MCP_PIPELINE_TOOL_SUCCESS");
            using (var doc = JsonDocument.Parse(Text(pipelineResult)))
            {
                var rootElem = doc.RootElement;
                Check(rootElem.GetProperty("overallVerdict").GetString() == "PASS", "MCP_PIPELINE_VERDICT_PASS");
                string md = rootElem.GetProperty("markdownReport").GetString();
                Check(md.Contains("StormFlow Executive Engineering Audit Report"), "MCP_PIPELINE_REPORT_CONTENT");
                Check(rootElem.GetProperty("jsonAuditPackage").GetString().Length > 50, "MCP_PIPELINE_JSON_PACKAGE_VALID");
            }
        }
    }
}
