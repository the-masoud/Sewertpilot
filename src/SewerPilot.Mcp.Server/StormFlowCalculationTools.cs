using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.Mcp.Server
{
    public sealed class StormFlowCalculationTools
    {
        public const string RationalToolName = "sewerpilot_compute_rational_runoff";
        public const string InletToolName = "sewerpilot_compute_gutter_and_inlet";
        public const string ConduitSizingToolName = "sewerpilot_size_storm_conduit";
        public const string HydraulicVerificationToolName = "sewerpilot_verify_network_hydraulics";
        public const string EndToEndPipelineToolName = "sewerpilot_execute_end_to_end_storm_pipeline";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        public void RegisterTools(IList<Tool> toolList)
        {
            if (toolList == null) throw new ArgumentNullException(nameof(toolList));

            // Tool 1: Rational Runoff
            using (var schema = JsonDocument.Parse(@"
            {
              ""type"": ""object"",
              ""properties"": {
                ""areaAcres"": { ""type"": ""number"", ""minimum"": 0.0001, ""description"": ""Contributing catchment drainage area in acres"" },
                ""runoffCoefficient"": { ""type"": ""number"", ""minimum"": 0.0, ""maximum"": 1.0, ""description"": ""Rational runoff coefficient C (0.0 to 1.0)"" },
                ""timeOfConcentrationMinutes"": { ""type"": ""number"", ""minimum"": 1.0, ""description"": ""Time of concentration in minutes (min 5.0 min applied)"" },
                ""returnPeriodYears"": { ""type"": ""integer"", ""minimum"": 1, ""description"": ""Design rainfall recurrence interval in years (e.g. 5, 10, 25, 50, 100)"" },
                ""idfA"": { ""type"": ""number"", ""minimum"": 0.001, ""description"": ""IDF curve parameter A (intensity numerator)"" },
                ""idfB"": { ""type"": ""number"", ""minimum"": 0.0, ""description"": ""IDF curve parameter B (time offset in minutes)"" },
                ""idfC"": { ""type"": ""number"", ""minimum"": 0.001, ""description"": ""IDF curve parameter C (intensity exponent, default 1.0)"" },
                ""catchmentId"": { ""type"": ""string"", ""description"": ""Optional catchment identifier label"" }
              },
              ""required"": [""areaAcres"", ""runoffCoefficient"", ""timeOfConcentrationMinutes"", ""returnPeriodYears"", ""idfA"", ""idfB""],
              ""additionalProperties"": false
            }"))
            {
                toolList.Add(new Tool
                {
                    Name = RationalToolName,
                    Description = "Calculate peak stormwater runoff and triangular hydrograph metrics using the Rational Method (Q = Cf * C * I * A) with explicit IDF curves and frequency factor adjustments.",
                    InputSchema = schema.RootElement.Clone(),
                    Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false }
                });
            }

            // Tool 2: Gutter & Inlet Interception
            using (var schema = JsonDocument.Parse(@"
            {
              ""type"": ""object"",
              ""properties"": {
                ""approachFlowCfs"": { ""type"": ""number"", ""minimum"": 0.0, ""description"": ""Total approach discharge approaching inlet in cfs"" },
                ""longitudinalSlope"": { ""type"": ""number"", ""minimum"": 0.0001, ""description"": ""Street and gutter longitudinal slope SL (ft/ft)"" },
                ""crossSlope"": { ""type"": ""number"", ""minimum"": 0.0001, ""description"": ""Pavement cross slope Sx (ft/ft)"" },
                ""manningN"": { ""type"": ""number"", ""minimum"": 0.001, ""description"": ""Gutter Manning roughness coefficient n (default 0.016)"" },
                ""inletType"": { ""type"": ""string"", ""enum"": [""CurbOpening"", ""Grate"", ""Combination"", ""SlottedDrain""], ""description"": ""Physical inlet type"" },
                ""inletLocation"": { ""type"": ""string"", ""enum"": [""OnContinuousGrade"", ""InSag""], ""description"": ""Inlet hydraulic condition"" },
                ""lengthFeet"": { ""type"": ""number"", ""minimum"": 0.1, ""description"": ""Opening length in feet (e.g. 5.0 to 15.0 ft)"" },
                ""widthFeet"": { ""type"": ""number"", ""minimum"": 0.0, ""description"": ""Grate width in feet (for Grate or Combination inlets)"" },
                ""throatHeightInches"": { ""type"": ""number"", ""minimum"": 0.0, ""description"": ""Curb opening throat height in inches (default 6.0 in)"" },
                ""allowableSpreadFeet"": { ""type"": ""number"", ""minimum"": 1.0, ""description"": ""Design maximum allowable gutter spread in feet (default 8.0 ft)"" },
                ""cloggingFactor"": { ""type"": ""number"", ""minimum"": 0.0, ""maximum"": 0.90, ""description"": ""Debris clogging factor (0.0 to 0.90)"" },
                ""inletId"": { ""type"": ""string"", ""description"": ""Optional inlet identifier label"" }
              },
              ""required"": [""approachFlowCfs"", ""longitudinalSlope"", ""crossSlope"", ""inletType"", ""inletLocation"", ""lengthFeet""],
              ""additionalProperties"": false
            }"))
            {
                toolList.Add(new Tool
                {
                    Name = InletToolName,
                    Description = "Compute gutter spread, curb water depth, intercepted discharge, carryover bypass flow, and capture efficiency for curb opening, grate, or combination inlets on continuous grade or in sag.",
                    InputSchema = schema.RootElement.Clone(),
                    Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false }
                });
            }

            // Tool 3: Conduit Sizing
            using (var schema = JsonDocument.Parse(@"
            {
              ""type"": ""object"",
              ""properties"": {
                ""designFlowCfs"": { ""type"": ""number"", ""minimum"": 0.001, ""description"": ""Required design flow in cfs"" },
                ""lengthFeet"": { ""type"": ""number"", ""minimum"": 1.0, ""description"": ""Conduit length in feet"" },
                ""groundElevationStartFeet"": { ""type"": ""number"", ""description"": ""Upstream ground surface elevation in feet"" },
                ""groundElevationStopFeet"": { ""type"": ""number"", ""description"": ""Downstream ground surface elevation in feet"" },
                ""manningN"": { ""type"": ""number"", ""minimum"": 0.001, ""description"": ""Pipe Manning roughness coefficient n (default 0.013)"" },
                ""minSlope"": { ""type"": ""number"", ""minimum"": 0.0001, ""description"": ""Minimum allowable conduit slope (default 0.002)"" },
                ""maxSlope"": { ""type"": ""number"", ""minimum"": 0.001, ""description"": ""Maximum allowable conduit slope (default 0.100)"" },
                ""minVelocityFps"": { ""type"": ""number"", ""minimum"": 0.5, ""description"": ""Minimum self-cleansing velocity in fps (default 2.5 fps)"" },
                ""maxVelocityFps"": { ""type"": ""number"", ""minimum"": 1.0, ""description"": ""Maximum non-erosive velocity in fps (default 10.0 fps)"" },
                ""minCoverFeet"": { ""type"": ""number"", ""minimum"": 0.0, ""description"": ""Minimum earth cover requirement in feet (default 3.0 ft)"" },
                ""incomingCrownElevationFeet"": { ""type"": ""number"", ""description"": ""Optional upstream junction crown elevation for soffit-to-soffit alignment"" },
                ""conduitId"": { ""type"": ""string"", ""description"": ""Optional conduit identifier label"" }
              },
              ""required"": [""designFlowCfs"", ""lengthFeet"", ""groundElevationStartFeet"", ""groundElevationStopFeet""],
              ""additionalProperties"": false
            }"))
            {
                toolList.Add(new Tool
                {
                    Name = ConduitSizingToolName,
                    Description = "Size a circular storm conduit using Manning's equation across standard commercial diameters (12\" - 72\"), checking self-cleansing velocity, non-erosive limits, earth cover, and soffit alignment.",
                    InputSchema = schema.RootElement.Clone(),
                    Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false }
                });
            }

            // Tool 4: Hydraulic Verification
            using (var schema = JsonDocument.Parse(@"
            {
              ""type"": ""object"",
              ""properties"": {
                ""junctions"": {
                  ""type"": ""array"",
                  ""items"": {
                    ""type"": ""object"",
                    ""properties"": {
                      ""nodeId"": { ""type"": ""integer"" },
                      ""label"": { ""type"": ""string"" },
                      ""rimElevationFeet"": { ""type"": ""number"" },
                      ""invertElevationFeet"": { ""type"": ""number"" },
                      ""junctionLossK"": { ""type"": ""number"" },
                      ""isOutfall"": { ""type"": ""boolean"" },
                      ""tailwaterElevationFeet"": { ""type"": ""number"" }
                    },
                    ""required"": [""nodeId"", ""rimElevationFeet"", ""invertElevationFeet""]
                  },
                  ""description"": ""List of network junction nodes/manholes""
                },
                ""conduits"": {
                  ""type"": ""array"",
                  ""items"": {
                    ""type"": ""object"",
                    ""properties"": {
                      ""conduitId"": { ""type"": ""string"" },
                      ""startNodeId"": { ""type"": ""integer"" },
                      ""stopNodeId"": { ""type"": ""integer"" },
                      ""lengthFeet"": { ""type"": ""number"" },
                      ""selectedDiameterInches"": { ""type"": ""integer"" },
                      ""designFlowCfs"": { ""type"": ""number"" },
                      ""upstreamInvertFeet"": { ""type"": ""number"" },
                      ""downstreamInvertFeet"": { ""type"": ""number"" }
                    },
                    ""required"": [""conduitId"", ""startNodeId"", ""stopNodeId"", ""lengthFeet"", ""selectedDiameterInches"", ""designFlowCfs"", ""upstreamInvertFeet"", ""downstreamInvertFeet""]
                  },
                  ""description"": ""List of sized conduit reaches""
                },
                ""minFreeboardFeet"": { ""type"": ""number"", ""minimum"": 0.0, ""description"": ""Minimum required freeboard margin in feet (default 1.0 ft)"" },
                ""manningN"": { ""type"": ""number"", ""minimum"": 0.001, ""description"": ""Pipe Manning roughness coefficient n (default 0.013)"" }
              },
              ""required"": [""junctions"", ""conduits""],
              ""additionalProperties"": false
            }"))
            {
                toolList.Add(new Tool
                {
                    Name = HydraulicVerificationToolName,
                    Description = "Execute network hydraulic verification computing HGL/EGL backwater profiles, identifying surcharged conduits, evaluating manhole freeboards/surface flooding, and computing bottleneck remediation diameter enlargements.",
                    InputSchema = schema.RootElement.Clone(),
                    Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false }
                });
            }

            // Tool 5: End-to-End Pipeline
            using (var schema = JsonDocument.Parse(@"
            {
              ""type"": ""object"",
              ""properties"": {
                ""projectName"": { ""type"": ""string"", ""description"": ""Project display name"" },
                ""projectId"": { ""type"": ""string"", ""description"": ""Unique project identifier"" },
                ""returnPeriodYears"": { ""type"": ""integer"", ""minimum"": 1, ""description"": ""Design return period in years"" },
                ""idfA"": { ""type"": ""number"", ""minimum"": 0.001, ""description"": ""IDF curve parameter A"" },
                ""idfB"": { ""type"": ""number"", ""minimum"": 0.0, ""description"": ""IDF curve parameter B"" },
                ""idfC"": { ""type"": ""number"", ""minimum"": 0.001, ""description"": ""IDF curve parameter C (default 1.0)"" },
                ""catchments"": {
                  ""type"": ""array"",
                  ""items"": {
                    ""type"": ""object"",
                    ""properties"": {
                      ""catchmentId"": { ""type"": ""string"" },
                      ""targetNodeId"": { ""type"": ""integer"" },
                      ""totalAreaAcres"": { ""type"": ""number"" },
                      ""runoffCoefficient"": { ""type"": ""number"" },
                      ""timeOfConcentrationMinutes"": { ""type"": ""number"" }
                    },
                    ""required"": [""catchmentId"", ""targetNodeId"", ""totalAreaAcres"", ""runoffCoefficient"", ""timeOfConcentrationMinutes""]
                  }
                },
                ""inlets"": {
                  ""type"": ""array"",
                  ""items"": {
                    ""type"": ""object"",
                    ""properties"": {
                      ""inletId"": { ""type"": ""string"" },
                      ""targetNodeId"": { ""type"": ""integer"" },
                      ""inletType"": { ""type"": ""string"", ""enum"": [""CurbOpening"", ""Grate"", ""Combination"", ""SlottedDrain""] },
                      ""inletLocation"": { ""type"": ""string"", ""enum"": [""OnContinuousGrade"", ""InSag""] },
                      ""lengthFeet"": { ""type"": ""number"" },
                      ""downstreamInletId"": { ""type"": ""string"" },
                      ""longitudinalSlope"": { ""type"": ""number"" },
                      ""crossSlope"": { ""type"": ""number"" }
                    },
                    ""required"": [""inletId"", ""targetNodeId"", ""inletType"", ""inletLocation"", ""lengthFeet""]
                  }
                },
                ""conduits"": {
                  ""type"": ""array"",
                  ""items"": {
                    ""type"": ""object"",
                    ""properties"": {
                      ""conduitId"": { ""type"": ""string"" },
                      ""startNodeId"": { ""type"": ""integer"" },
                      ""stopNodeId"": { ""type"": ""integer"" },
                      ""lengthFeet"": { ""type"": ""number"" },
                      ""groundElevationStartFeet"": { ""type"": ""number"" },
                      ""groundElevationStopFeet"": { ""type"": ""number"" }
                    },
                    ""required"": [""conduitId"", ""startNodeId"", ""stopNodeId"", ""lengthFeet"", ""groundElevationStartFeet"", ""groundElevationStopFeet""]
                  }
                },
                ""junctions"": {
                  ""type"": ""array"",
                  ""items"": {
                    ""type"": ""object"",
                    ""properties"": {
                      ""nodeId"": { ""type"": ""integer"" },
                      ""label"": { ""type"": ""string"" },
                      ""rimElevationFeet"": { ""type"": ""number"" },
                      ""invertElevationFeet"": { ""type"": ""number"" },
                      ""isOutfall"": { ""type"": ""boolean"" },
                      ""tailwaterElevationFeet"": { ""type"": ""number"" }
                    },
                    ""required"": [""nodeId"", ""rimElevationFeet"", ""invertElevationFeet""]
                  }
                }
              },
              ""required"": [""returnPeriodYears"", ""idfA"", ""idfB"", ""catchments"", ""inlets"", ""conduits"", ""junctions""],
              ""additionalProperties"": false
            }"))
            {
                toolList.Add(new Tool
                {
                    Name = EndToEndPipelineToolName,
                    Description = "Execute the complete end-to-end municipal storm drainage pipeline: hydrology runoff -> inlet capture & carryover -> conduit sizing -> HGL/surcharge verification -> 8-section audit report & JSON package.",
                    InputSchema = schema.RootElement.Clone(),
                    Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false }
                });
            }
        }

        public bool CanHandle(string toolName)
        {
            return toolName == RationalToolName ||
                   toolName == InletToolName ||
                   toolName == ConduitSizingToolName ||
                   toolName == HydraulicVerificationToolName ||
                   toolName == EndToEndPipelineToolName;
        }

        public CallToolResult Handle(CallToolRequestParams request)
        {
            if (request == null) return Error("TOOL_REQUEST_NULL");
            string toolName = request.Name;
            var args = request.Arguments ?? new Dictionary<string, JsonElement>();

            try
            {
                switch (toolName)
                {
                    case RationalToolName:
                        return HandleRationalRunoff(args);
                    case InletToolName:
                        return HandleGutterAndInlet(args);
                    case ConduitSizingToolName:
                        return HandleConduitSizing(args);
                    case HydraulicVerificationToolName:
                        return HandleHydraulicVerification(args);
                    case EndToEndPipelineToolName:
                        return HandleEndToEndPipeline(args);
                    default:
                        return Error("UNKNOWN_TOOL: " + toolName);
                }
            }
            catch (Exception ex)
            {
                return Error("CALCULATION_ENGINE_ERROR: " + ex.Message);
            }
        }

        private static CallToolResult HandleRationalRunoff(IDictionary<string, JsonElement> args)
        {
            if (!args.TryGetValue("areaAcres", out var aElem) || !args.TryGetValue("runoffCoefficient", out var cElem) ||
                !args.TryGetValue("timeOfConcentrationMinutes", out var tcElem) || !args.TryGetValue("returnPeriodYears", out var rpElem) ||
                !args.TryGetValue("idfA", out var idfAElem) || !args.TryGetValue("idfB", out var idfBElem))
            {
                return Error("MISSING_REQUIRED_RATIONAL_ARGUMENTS");
            }

            double area = aElem.GetDouble();
            double c = cElem.GetDouble();
            double tc = tcElem.GetDouble();
            int rp = rpElem.GetInt32();
            double idfA = idfAElem.GetDouble();
            double idfB = idfBElem.GetDouble();
            double idfC = args.TryGetValue("idfC", out var idfCElem) ? idfCElem.GetDouble() : 1.0;
            string catchmentId = args.TryGetValue("catchmentId", out var idElem) ? idElem.GetString() : "CAT-MCP";

            var idf = new IdfParameters { A = idfA, B = idfB, C = idfC, ReturnPeriodYears = rp, MinimumTimeOfConcentrationMinutes = 5.0 };
            var catchment = new StormCatchment
            {
                CatchmentId = catchmentId,
                TargetNodeId = 1,
                TotalAreaAcres = area,
                RunoffCoefficient = c,
                TimeOfConcentrationMinutes = tc
            };

            var runoff = StormFlowHydrologyEngine.ComputeRational(catchment, idf, rp);
            return Success(new
            {
                tool = RationalToolName,
                catchmentId = runoff.CatchmentId,
                returnPeriodYears = runoff.ReturnPeriodYears,
                drainageAreaAcres = runoff.AreaAcres,
                baseRunoffCoefficient = c,
                adjustedRunoffCoefficient = runoff.EffectiveRunoffParameter,
                timeOfConcentrationMinutes = runoff.TimeOfConcentrationMinutes,
                rainfallIntensityInPerHour = Math.Round(runoff.RainfallIntensityInPerHour, 4),
                peakDischargeCfs = Math.Round(runoff.PeakDischargeCfs, 4),
                totalVolumeCubicFeet = Math.Round(runoff.TotalVolumeCubicFeet, 2),
                diagnostics = runoff.Diagnostics
            });
        }

        private static CallToolResult HandleGutterAndInlet(IDictionary<string, JsonElement> args)
        {
            if (!args.TryGetValue("approachFlowCfs", out var qElem) || !args.TryGetValue("longitudinalSlope", out var slElem) ||
                !args.TryGetValue("crossSlope", out var sxElem) || !args.TryGetValue("inletType", out var typeElem) ||
                !args.TryGetValue("inletLocation", out var locElem) || !args.TryGetValue("lengthFeet", out var lElem))
            {
                return Error("MISSING_REQUIRED_INLET_ARGUMENTS");
            }

            double q = qElem.GetDouble();
            double sl = slElem.GetDouble();
            double sx = sxElem.GetDouble();
            double length = lElem.GetDouble();
            double manningN = args.TryGetValue("manningN", out var nElem) ? nElem.GetDouble() : 0.016;
            double width = args.TryGetValue("widthFeet", out var wElem) ? wElem.GetDouble() : 2.0;
            double throat = args.TryGetValue("throatHeightInches", out var thElem) ? thElem.GetDouble() : 6.0;
            double spreadLimit = args.TryGetValue("allowableSpreadFeet", out var spElem) ? spElem.GetDouble() : 8.0;
            double clogging = args.TryGetValue("cloggingFactor", out var clElem) ? clElem.GetDouble() : 0.0;
            string inletId = args.TryGetValue("inletId", out var idElem) ? idElem.GetString() : "INL-MCP";

            if (!Enum.TryParse<InletType>(typeElem.GetString(), true, out var inletType))
                return Error("INVALID_INLET_TYPE: " + typeElem.GetString());
            if (!Enum.TryParse<InletLocation>(locElem.GetString(), true, out var inletLocation))
                return Error("INVALID_INLET_LOCATION: " + locElem.GetString());

            var gutter = new GutterCrossSection { LongitudinalSlopeSL = sl, CrossSlopeSx = sx, ManningN = manningN };
            var config = new InletConfiguration
            {
                InletId = inletId,
                TargetNodeId = 1,
                Type = inletType,
                Location = inletLocation,
                LengthFeet = length,
                WidthFeet = width,
                ThroatHeightInches = throat,
                AllowableSpreadFeet = spreadLimit,
                CloggingFactor = clogging,
                Gutter = gutter
            };

            var interception = StormFlowInletHydraulicsEngine.ComputeInletInterception(config, q, 0.0);
            return Success(new
            {
                tool = InletToolName,
                inletId = config.InletId,
                inletType = config.Type.ToString(),
                inletLocation = config.Location.ToString(),
                totalApproachFlowCfs = interception.TotalApproachFlowCfs,
                interceptedFlowCfs = interception.InterceptedFlowCfs,
                bypassFlowCfs = interception.BypassFlowCfs,
                interceptionEfficiency = interception.InterceptionEfficiency,
                gutterSpreadFeet = interception.GutterSpreadFeet,
                gutterDepthInches = interception.GutterDepthInches,
                gutterVelocityFps = interception.GutterVelocityFps,
                allowableSpreadFeet = interception.AllowableSpreadFeet,
                spreadExceeded = interception.SpreadExceeded,
                flowRegime = interception.FlowRegime.ToString(),
                diagnostics = interception.Diagnostics
            });
        }

        private static CallToolResult HandleConduitSizing(IDictionary<string, JsonElement> args)
        {
            if (!args.TryGetValue("designFlowCfs", out var qElem) || !args.TryGetValue("lengthFeet", out var lElem) ||
                !args.TryGetValue("groundElevationStartFeet", out var gsElem) || !args.TryGetValue("groundElevationStopFeet", out var geElem))
            {
                return Error("MISSING_REQUIRED_CONDUIT_ARGUMENTS");
            }

            double q = qElem.GetDouble();
            double length = lElem.GetDouble();
            double groundStart = gsElem.GetDouble();
            double groundStop = geElem.GetDouble();

            var constraint = new StormConduitConstraint
            {
                ManningN = args.TryGetValue("manningN", out var nElem) ? nElem.GetDouble() : 0.013,
                MinimumSlope = args.TryGetValue("minSlope", out var sMinElem) ? sMinElem.GetDouble() : 0.002,
                MaximumSlope = args.TryGetValue("maxSlope", out var sMaxElem) ? sMaxElem.GetDouble() : 0.100,
                MinimumVelocityFps = args.TryGetValue("minVelocityFps", out var vMinElem) ? vMinElem.GetDouble() : 2.5,
                MaximumVelocityFps = args.TryGetValue("maxVelocityFps", out var vMaxElem) ? vMaxElem.GetDouble() : 10.0,
                MinimumCoverFeet = args.TryGetValue("minCoverFeet", out var covElem) ? covElem.GetDouble() : 3.0
            };

            double incomingCrown = args.TryGetValue("incomingCrownElevationFeet", out var crElem) ? crElem.GetDouble() : double.NaN;
            string conduitId = args.TryGetValue("conduitId", out var idElem) ? idElem.GetString() : "PIPE-MCP";

            var spec = new StormConduitSpecification
            {
                ConduitId = conduitId,
                StartNodeId = 1,
                StopNodeId = 2,
                LengthFeet = length,
                DesignFlowCfs = q,
                GroundElevationStartFeet = groundStart,
                GroundElevationStopFeet = groundStop
            };

            var result = StormFlowConduitDesignEngine.SizeConduit(spec, constraint, incomingCrown);
            return Success(new
            {
                tool = ConduitSizingToolName,
                conduitId = result.ConduitId,
                designFlowCfs = result.DesignFlowCfs,
                lengthFeet = result.LengthFeet,
                selectedDiameterInches = result.SelectedDiameterInches,
                calculatedSlope = Math.Round(result.DesignSlope, 6),
                fullFlowCapacityCfs = Math.Round(result.FullFlowCapacityCfs, 4),
                capacityRatio = Math.Round(result.CapacityRatio, 4),
                actualVelocityFps = Math.Round(result.ActualVelocityFps, 2),
                flowDepthFeet = Math.Round(result.DepthOfFlowFeet, 2),
                upstreamInvertFeet = Math.Round(result.UpstreamInvertFeet, 2),
                downstreamInvertFeet = Math.Round(result.DownstreamInvertFeet, 2),
                upstreamCrownFeet = Math.Round(result.UpstreamCrownFeet, 2),
                downstreamCrownFeet = Math.Round(result.DownstreamCrownFeet, 2),
                upstreamCoverFeet = Math.Round(result.UpstreamCoverFeet, 2),
                downstreamCoverFeet = Math.Round(result.DownstreamCoverFeet, 2),
                velocityCompliance = result.VelocityCompliance.ToString(),
                coverCompliance = result.CoverCompliance.ToString(),
                capacityCompliance = result.CapacityCompliance.ToString(),
                isCompliant = result.VelocityCompliance == VelocityComplianceStatus.Pass && result.CoverCompliance == CoverComplianceStatus.Pass && result.CapacityCompliance == CapacityComplianceStatus.Pass
            });
        }

        private static CallToolResult HandleHydraulicVerification(IDictionary<string, JsonElement> args)
        {
            if (!args.TryGetValue("junctions", out var jArray) || !args.TryGetValue("conduits", out var cArray) ||
                jArray.ValueKind != JsonValueKind.Array || cArray.ValueKind != JsonValueKind.Array)
            {
                return Error("JUNCTIONS_AND_CONDUITS_ARRAYS_REQUIRED");
            }

            var junctions = new List<StormJunctionSpecification>();
            foreach (var jElem in jArray.EnumerateArray())
            {
                junctions.Add(new StormJunctionSpecification
                {
                    NodeId = jElem.GetProperty("nodeId").GetInt32(),
                    Label = jElem.TryGetProperty("label", out var l) ? l.GetString() : "NODE",
                    RimElevationFeet = jElem.GetProperty("rimElevationFeet").GetDouble(),
                    InvertElevationFeet = jElem.GetProperty("invertElevationFeet").GetDouble(),
                    JunctionLossCoefficient = jElem.TryGetProperty("junctionLossK", out var k) ? k.GetDouble() : 0.25,
                    IsOutfall = jElem.TryGetProperty("isOutfall", out var o) && o.GetBoolean(),
                    TailwaterElevationFeet = jElem.TryGetProperty("tailwaterElevationFeet", out var tw) ? (double?)tw.GetDouble() : null
                });
            }

            var conduits = new List<HydraulicSizingResult>();
            foreach (var cElem in cArray.EnumerateArray())
            {
                conduits.Add(new HydraulicSizingResult
                {
                    ConduitId = cElem.GetProperty("conduitId").GetString(),
                    StartNodeId = cElem.GetProperty("startNodeId").GetInt32(),
                    StopNodeId = cElem.GetProperty("stopNodeId").GetInt32(),
                    LengthFeet = cElem.GetProperty("lengthFeet").GetDouble(),
                    SelectedDiameterInches = cElem.GetProperty("selectedDiameterInches").GetInt32(),
                    DesignFlowCfs = cElem.GetProperty("designFlowCfs").GetDouble(),
                    UpstreamInvertFeet = cElem.GetProperty("upstreamInvertFeet").GetDouble(),
                    DownstreamInvertFeet = cElem.GetProperty("downstreamInvertFeet").GetDouble()
                });
            }

            var criteria = new StormVerificationCriteria
            {
                MinimumFreeboardFeet = args.TryGetValue("minFreeboardFeet", out var fbElem) ? fbElem.GetDouble() : 1.0,
                ManningN = args.TryGetValue("manningN", out var nElem) ? nElem.GetDouble() : 0.013
            };

            var verification = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits, criteria);

            return Success(new
            {
                tool = HydraulicVerificationToolName,
                totalHeadLossSystemFeet = Math.Round(verification.TotalHeadLossSystemFeet, 4),
                maxHglElevationFeet = Math.Round(verification.MaxHglElevationFeet, 4),
                hasSurcharge = verification.HasSurcharge,
                hasFreeboardViolations = verification.HasFreeboardViolations,
                hasFlooding = verification.HasFlooding,
                conduitStates = verification.ConduitStates.Select(c => new
                {
                    c.ConduitId,
                    c.DiameterInches,
                    c.FlowCfs,
                    c.VelocityFps,
                    c.VelocityHeadFeet,
                    c.FrictionHeadLossFeet,
                    c.MinorLossFeet,
                    c.TotalHeadLossFeet,
                    c.UpstreamHglFeet,
                    c.DownstreamHglFeet,
                    c.IsSurcharged,
                    surchargeState = c.SurchargeState.ToString()
                }),
                nodeStates = verification.NodeStates.Select(n => new
                {
                    n.NodeId,
                    n.Label,
                    n.RimElevationFeet,
                    n.InvertElevationFeet,
                    n.HglFeet,
                    n.FreeboardFeet,
                    floodingState = n.FloodingState.ToString(),
                    n.OverflowDischargeCfs,
                    n.IsOutfall
                }),
                bottleneckProposals = verification.BottleneckProposals.Select(b => new
                {
                    b.ConduitId,
                    b.CurrentDiameterInches,
                    b.ProposedDiameterInches,
                    headLossReductionFeet = b.HeadLossReductionFeet,
                    resolvedFreeboardFeet = b.ResolvedUpstreamFreeboardFeet,
                    b.RemediationDescription
                })
            });
        }

        private static CallToolResult HandleEndToEndPipeline(IDictionary<string, JsonElement> args)
        {
            if (!args.TryGetValue("idfA", out var idfAElem) ||
                !args.TryGetValue("idfB", out var idfBElem) ||
                !args.TryGetValue("returnPeriodYears", out var rpElem) ||
                !args.TryGetValue("catchments", out var catchmentsElem) ||
                !args.TryGetValue("inlets", out var inletsElem) ||
                !args.TryGetValue("conduits", out var conduitsElem) ||
                !args.TryGetValue("junctions", out var junctionsElem))
            {
                return Error("MISSING_REQUIRED_PIPELINE_ARGUMENTS");
            }

            var idf = new IdfParameters
            {
                A = idfAElem.GetDouble(),
                B = idfBElem.GetDouble(),
                C = args.TryGetValue("idfC", out var cElem) ? cElem.GetDouble() : 1.0,
                ReturnPeriodYears = rpElem.GetInt32()
            };

            var catchments = new List<StormCatchment>();
            foreach (var c in catchmentsElem.EnumerateArray())
            {
                catchments.Add(new StormCatchment
                {
                    CatchmentId = c.GetProperty("catchmentId").GetString(),
                    TargetNodeId = c.GetProperty("targetNodeId").GetInt32(),
                    TotalAreaAcres = c.GetProperty("totalAreaAcres").GetDouble(),
                    RunoffCoefficient = c.GetProperty("runoffCoefficient").GetDouble(),
                    TimeOfConcentrationMinutes = c.GetProperty("timeOfConcentrationMinutes").GetDouble()
                });
            }

            var inlets = new Dictionary<string, InletConfiguration>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in inletsElem.EnumerateArray())
            {
                string id = i.GetProperty("inletId").GetString();
                Enum.TryParse<InletType>(i.GetProperty("inletType").GetString(), true, out var it);
                Enum.TryParse<InletLocation>(i.GetProperty("inletLocation").GetString(), true, out var il);
                double sl = i.TryGetProperty("longitudinalSlope", out var slElem) ? slElem.GetDouble() : 0.015;
                double sx = i.TryGetProperty("crossSlope", out var sxElem) ? sxElem.GetDouble() : 0.02;

                inlets[id] = new InletConfiguration
                {
                    InletId = id,
                    TargetNodeId = i.GetProperty("targetNodeId").GetInt32(),
                    Type = it,
                    Location = il,
                    LengthFeet = i.GetProperty("lengthFeet").GetDouble(),
                    DownstreamInletId = i.TryGetProperty("downstreamInletId", out var ds) ? ds.GetString() : null,
                    Gutter = new GutterCrossSection { LongitudinalSlopeSL = sl, CrossSlopeSx = sx, ManningN = 0.016 }
                };
            }

            var conduits = new List<StormConduitSpecification>();
            foreach (var c in conduitsElem.EnumerateArray())
            {
                conduits.Add(new StormConduitSpecification
                {
                    ConduitId = c.GetProperty("conduitId").GetString(),
                    StartNodeId = c.GetProperty("startNodeId").GetInt32(),
                    StopNodeId = c.GetProperty("stopNodeId").GetInt32(),
                    LengthFeet = c.GetProperty("lengthFeet").GetDouble(),
                    GroundElevationStartFeet = c.GetProperty("groundElevationStartFeet").GetDouble(),
                    GroundElevationStopFeet = c.GetProperty("groundElevationStopFeet").GetDouble()
                });
            }

            var junctions = new List<StormJunctionSpecification>();
            foreach (var j in junctionsElem.EnumerateArray())
            {
                junctions.Add(new StormJunctionSpecification
                {
                    NodeId = j.GetProperty("nodeId").GetInt32(),
                    Label = j.TryGetProperty("label", out var l) ? l.GetString() : "NODE",
                    RimElevationFeet = j.GetProperty("rimElevationFeet").GetDouble(),
                    InvertElevationFeet = j.GetProperty("invertElevationFeet").GetDouble(),
                    IsOutfall = j.TryGetProperty("isOutfall", out var o) && o.GetBoolean(),
                    TailwaterElevationFeet = j.TryGetProperty("tailwaterElevationFeet", out var tw) ? (double?)tw.GetDouble() : null
                });
            }

            var input = new StormFlowPipelineInput
            {
                ProjectName = args.TryGetValue("projectName", out var pn) ? pn.GetString() : "SewerPilot MCP Drainage Run",
                ProjectId = args.TryGetValue("projectId", out var pid) ? pid.GetString() : Guid.NewGuid().ToString("D"),
                Idf = idf,
                ReturnPeriodYears = idf.ReturnPeriodYears,
                Catchments = catchments,
                Inlets = inlets,
                ConduitSpecifications = conduits,
                JunctionSpecifications = junctions
            };

            var result = StormFlowEndToEndPipeline.Execute(input);

            return Success(new
            {
                tool = EndToEndPipelineToolName,
                projectId = result.ProjectId,
                overallVerdict = result.OverallVerdict,
                success = result.Success,
                summary = new
                {
                    catchmentsCount = result.HydrologyResults.Count,
                    totalApproachFlowCfs = result.InletRoutingResult.TotalApproachFlowCfs,
                    totalInterceptedFlowCfs = result.InletRoutingResult.TotalInterceptedFlowCfs,
                    conduitsCount = result.ConduitDesignResult.ConduitResults.Count,
                    totalSystemLengthFeet = result.ConduitDesignResult.TotalSystemLengthFeet,
                    maxHglElevationFeet = result.VerificationResult.MaxHglElevationFeet,
                    hasSurcharge = result.VerificationResult.HasSurcharge,
                    hasFlooding = result.VerificationResult.HasFlooding,
                    bottlenecksCount = result.VerificationResult.BottleneckProposals.Count
                },
                markdownReport = result.MarkdownReport,
                jsonAuditPackage = result.JsonAuditPackage
            });
        }

        private static CallToolResult Success(object value)
        {
            return new CallToolResult
            {
                IsError = false,
                Content = new List<ContentBlock> { new TextContentBlock { Text = JsonSerializer.Serialize(value, Json) } }
            };
        }

        private static CallToolResult Error(string message)
        {
            Console.Error.WriteLine("MCP_STORMFLOW_CALCULATION_ERROR: " + message);
            return new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock> { new TextContentBlock { Text = JsonSerializer.Serialize(new { isError = true, error = message }, Json) } }
            };
        }
    }
}
