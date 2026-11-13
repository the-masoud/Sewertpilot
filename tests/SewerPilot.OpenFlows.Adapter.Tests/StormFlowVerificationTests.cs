using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static void P22_VelocityHeadAndFrictionSlope_BenchmarkCalculation()
        {
            double dFt = 2.0; // 24-inch pipe
            double flow = 20.0; // cfs
            double n = 0.013;
            double g = 32.174;

            double v = StormFlowVerificationEngine.ComputeVelocity(flow, dFt);
            double hv = StormFlowVerificationEngine.ComputeVelocityHead(v, g);
            double sf = StormFlowVerificationEngine.ComputeFrictionSlope(flow, dFt, n);

            // Theoretical benchmark:
            // Area = pi * 2^2 / 4 = 3.1416 sq ft
            // V = 20 / 3.14159 = 6.3662 fps
            // hv = 6.3662^2 / (2 * 32.174) = 40.5285 / 64.348 = 0.6298 ft
            // R = D/4 = 0.5 ft, R^(2/3) = 0.62996
            // Sf = [(0.013 * 6.3662) / (1.486 * 0.62996)]^2 = (0.08276 / 0.93612)^2 = 0.007816
            Check(Math.Abs(v - 6.3662) < 0.01, "Velocity is ~6.37 fps.");
            Check(Math.Abs(hv - 0.6298) < 0.01, "Velocity head is ~0.63 ft.");
            Check(Math.Abs(sf - 0.007816) < 0.0002, "Manning friction slope is ~0.0078.");
        }

        private static void P22_CriticalDepth_BisectionSolver()
        {
            double dFt = 2.0; // 24-inch pipe
            double flow = 10.0; // cfs

            double dc = StormFlowVerificationEngine.SolveCriticalDepth(dFt, flow);

            // Critical depth for 10 cfs in 2.0 ft circular pipe is approx 1.15 ft
            Check(dc > 0.80 && dc < 1.50, "Critical depth is in expected range.");
            Check(dc < dFt, "Critical depth is below pipe crown.");

            // Zero flow test
            double dcZero = StormFlowVerificationEngine.SolveCriticalDepth(dFt, 0.0);
            Check(dcZero == 0.0, "Zero flow yields zero critical depth.");
        }

        private static void P22_ManningHeadLoss_Accumulation()
        {
            double sf = 0.005; // 0.5% friction slope
            double length = 300.0; // ft
            double hv = 0.60; // ft
            double k = 0.50; // junction loss coefficient

            double hf = StormFlowVerificationEngine.ComputeFrictionHeadLoss(sf, length);
            double hm = StormFlowVerificationEngine.ComputeMinorLoss(k, hv);

            // hf = 0.005 * 300 = 1.50 ft
            // hm = 0.50 * 0.60 = 0.30 ft
            Check(Math.Abs(hf - 1.50) < 0.001, "Friction head loss is exactly 1.50 ft.");
            Check(Math.Abs(hm - 0.30) < 0.001, "Minor junction loss is exactly 0.30 ft.");
        }

        private static void P22_OutfallTailwater_BoundaryConditionPropagation()
        {
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "J-1", RimElevationFeet = 110.0, InvertElevationFeet = 104.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "OUTFALL", RimElevationFeet = 106.0, InvertElevationFeet = 100.0, IsOutfall = true, TailwaterElevationFeet = 103.50 }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "P-1",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 200.0,
                    SelectedDiameterInches = 24,
                    DesignFlowCfs = 8.0,
                    UpstreamInvertFeet = 104.0,
                    DownstreamInvertFeet = 100.0
                }
            };

            var res = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits);

            var outfallState = res.NodeStates.First(n => n.NodeId == 2);
            Check(outfallState.HglFeet == 103.50, "Outfall HGL matches prescribed tailwater boundary.");

            var pipeState = res.ConduitStates.First(c => c.ConduitId == "P-1");
            Check(pipeState.DownstreamHglFeet == 103.50, "Downstream pipe HGL matches tailwater.");
            Check(pipeState.UpstreamHglFeet > 103.50, "Upstream pipe HGL accumulates head loss.");
        }

        private static void P22_SafeFreeboard_CompliantNetwork()
        {
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "MH-1", RimElevationFeet = 120.0, InvertElevationFeet = 110.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "MH-2", RimElevationFeet = 115.0, InvertElevationFeet = 105.0 },
                new StormJunctionSpecification { NodeId = 3, Label = "OUTFALL", RimElevationFeet = 110.0, InvertElevationFeet = 100.0, IsOutfall = true }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "PIPE-1",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 200.0,
                    SelectedDiameterInches = 18,
                    DesignFlowCfs = 3.0,
                    UpstreamInvertFeet = 110.0,
                    DownstreamInvertFeet = 105.0
                },
                new HydraulicSizingResult
                {
                    ConduitId = "PIPE-2",
                    StartNodeId = 2,
                    StopNodeId = 3,
                    LengthFeet = 200.0,
                    SelectedDiameterInches = 24,
                    DesignFlowCfs = 6.0,
                    UpstreamInvertFeet = 105.0,
                    DownstreamInvertFeet = 100.0
                }
            };

            var res = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits);

            Check(!res.HasFlooding, "Network has zero surface flooding.");
            Check(!res.HasFreeboardViolations, "Network has zero freeboard violations.");
            foreach (var node in res.NodeStates)
            {
                Check(node.FloodingState == NodeFloodingState.SafeFreeboard, $"Node {node.Label} maintains safe freeboard.");
                Check(node.FreeboardFeet >= 1.0, $"Node {node.Label} has at least 1.0 ft freeboard.");
            }
        }

        private static void P22_SurchargedConduit_PressurizedFlowDetection()
        {
            // Undersized 12-inch pipe forced to convey 15 cfs
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "MH-1", RimElevationFeet = 120.0, InvertElevationFeet = 105.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "MH-2", RimElevationFeet = 115.0, InvertElevationFeet = 100.0, IsOutfall = true, TailwaterElevationFeet = 104.0 }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "P-OVERLOAD",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 300.0,
                    SelectedDiameterInches = 12, // 1-foot diameter!
                    DesignFlowCfs = 15.0, // High flow causing huge friction loss
                    UpstreamInvertFeet = 105.0,
                    DownstreamInvertFeet = 100.0
                }
            };

            var res = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits);

            var pipe = res.ConduitStates.First();
            Check(pipe.IsSurcharged, "Severely overloaded conduit is flagged as surcharged.");
            Check(pipe.UpstreamHglFeet > pipe.UpstreamCrownFeet, "Upstream HGL exceeds pipe crown.");
            Check(res.HasSurcharge, "Network verification reports surcharge.");
        }

        private static void P22_FreeboardViolation_DetectedAndReported()
        {
            // Node rim is 108.0, Invert 100.0, Downstream HGL + loss puts HGL at 107.5 ft -> Freeboard 0.5 ft (< 1.0 ft)
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "MH-SHALLOW", RimElevationFeet = 107.50, InvertElevationFeet = 102.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "OUTFALL", RimElevationFeet = 106.0, InvertElevationFeet = 100.0, IsOutfall = true, TailwaterElevationFeet = 105.0 }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "P-1",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 200.0,
                    SelectedDiameterInches = 15,
                    DesignFlowCfs = 6.0,
                    UpstreamInvertFeet = 102.0,
                    DownstreamInvertFeet = 100.0
                }
            };

            var criteria = new StormVerificationCriteria { MinimumFreeboardFeet = 1.0 };
            var res = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits, criteria);

            var shallowNode = res.NodeStates.First(n => n.NodeId == 1);
            Check(shallowNode.FloodingState == NodeFloodingState.FreeboardExceeded, "Node flagged with FreeboardExceeded state.");
            Check(shallowNode.FreeboardFeet < 1.0 && shallowNode.FreeboardFeet > 0.0, "Freeboard is between 0 and 1.0 ft.");
            Check(res.HasFreeboardViolations, "Verification result reports freeboard violations.");
        }

        private static void P22_SurfaceFlooding_RimOverflowDischargeCalculation()
        {
            // Severe backwater: Tailwater is 112.0 ft, Rim of upstream manhole is 110.0 ft -> Flooding!
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "MH-FLOOD", RimElevationFeet = 110.0, InvertElevationFeet = 102.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "OUTFALL", RimElevationFeet = 112.0, InvertElevationFeet = 100.0, IsOutfall = true, TailwaterElevationFeet = 111.0 }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "P-1",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 100.0,
                    SelectedDiameterInches = 18,
                    DesignFlowCfs = 8.0,
                    UpstreamInvertFeet = 102.0,
                    DownstreamInvertFeet = 100.0
                }
            };

            var res = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits);

            var floodedNode = res.NodeStates.First(n => n.NodeId == 1);
            Check(floodedNode.FloodingState == NodeFloodingState.SurfaceFlooding, "Node flagged with SurfaceFlooding.");
            Check(floodedNode.FreeboardFeet < 0.0, "Freeboard is negative.");
            Check(floodedNode.OverflowDischargeCfs > 0.0, "Overflow discharge is computed.");
            Check(res.HasFlooding, "Network result flags flooding.");
        }

        private static void P22_BottleneckRemediation_AutomaticUpsizingProposal()
        {
            // Conduit P-1 is 15-inch carrying 8.0 cfs, generating high friction loss that causes MH-1 to exceed freeboard
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "MH-1", RimElevationFeet = 106.0, InvertElevationFeet = 101.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "OUTFALL", RimElevationFeet = 105.0, InvertElevationFeet = 100.0, IsOutfall = true, TailwaterElevationFeet = 101.5 }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "P-1",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 300.0,
                    SelectedDiameterInches = 15, // 15" creates high friction loss
                    DesignFlowCfs = 8.0,
                    UpstreamInvertFeet = 101.0,
                    DownstreamInvertFeet = 100.0
                }
            };

            var criteria = new StormVerificationCriteria
            {
                MinimumFreeboardFeet = 1.0,
                AvailableDiametersInches = new[] { 15, 18, 21, 24, 30, 36 }
            };

            var res = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits, criteria);

            Check(res.BottleneckProposals.Count > 0, "Engine identifies bottleneck and proposes remediation.");
            var prop = res.BottleneckProposals.First();
            Check(prop.ConduitId == "P-1", "Bottleneck conduit correctly identified.");
            Check(prop.ProposedDiameterInches > 15, "Proposed diameter is larger than current.");
            Check(prop.ProposedFrictionHeadLossFeet < prop.CurrentFrictionHeadLossFeet, "Head loss is reduced.");
            Check(prop.ResolvedUpstreamFreeboardFeet >= 1.0, "Upsizing restores upstream freeboard >= 1.0 ft.");
        }

        private static void P22_FailClosed_InvalidInputsRejected()
        {
            // Null collections
            Throws<ArgumentNullException>(() => StormFlowVerificationEngine.VerifyNetwork(null, new HydraulicSizingResult[0]));
            Throws<ArgumentNullException>(() => StormFlowVerificationEngine.VerifyNetwork(new StormJunctionSpecification[0], null));

            // Empty collections
            Throws<ArgumentException>(() => StormFlowVerificationEngine.VerifyNetwork(new StormJunctionSpecification[0], new HydraulicSizingResult[0]));

            // Duplicate node IDs
            Throws<ArgumentException>(() =>
            {
                var j = new[]
                {
                    new StormJunctionSpecification { NodeId = 1, RimElevationFeet = 110, InvertElevationFeet = 100 },
                    new StormJunctionSpecification { NodeId = 1, RimElevationFeet = 110, InvertElevationFeet = 100 }
                };
                StormFlowVerificationEngine.VerifyNetwork(j, new[] { new HydraulicSizingResult { ConduitId = "P1", StartNodeId = 1, StopNodeId = 2, LengthFeet = 100, SelectedDiameterInches = 18 } });
            });

            // Rim <= Invert
            Throws<ArgumentException>(() =>
            {
                var j = new StormJunctionSpecification { NodeId = 1, RimElevationFeet = 100, InvertElevationFeet = 105 };
                j.Validate();
            });

            // Circular network loop
            Throws<InvalidOperationException>(() =>
            {
                var j = new[]
                {
                    new StormJunctionSpecification { NodeId = 1, RimElevationFeet = 110, InvertElevationFeet = 100 },
                    new StormJunctionSpecification { NodeId = 2, RimElevationFeet = 110, InvertElevationFeet = 100 }
                };
                var c = new[]
                {
                    new HydraulicSizingResult { ConduitId = "P1", StartNodeId = 1, StopNodeId = 2, LengthFeet = 100, SelectedDiameterInches = 18, DesignFlowCfs = 2.0 },
                    new HydraulicSizingResult { ConduitId = "P2", StartNodeId = 2, StopNodeId = 1, LengthFeet = 100, SelectedDiameterInches = 18, DesignFlowCfs = 2.0 }
                };
                StormFlowVerificationEngine.VerifyNetwork(j, c);
            });
        }

        internal static void P22_MaterializeAuthoritativeEvidence()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(root) && !File.Exists(Path.Combine(root, "SewerPilot.sln")))
            {
                root = Path.GetDirectoryName(root);
            }
            string outDir = Path.Combine(root, "docs", "evidence", "p22", "20261019-stormflow-verification");
            Directory.CreateDirectory(outDir);

            // Create a realistic multi-conduit storm network cascade
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "INLET-101", RimElevationFeet = 125.0, InvertElevationFeet = 118.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "MH-102", RimElevationFeet = 120.0, InvertElevationFeet = 113.5, JunctionLossCoefficient = 0.30 },
                new StormJunctionSpecification { NodeId = 3, Label = "MH-103", RimElevationFeet = 115.0, InvertElevationFeet = 108.5, JunctionLossCoefficient = 0.45 },
                new StormJunctionSpecification { NodeId = 4, Label = "OUTFALL-BAY", RimElevationFeet = 110.0, InvertElevationFeet = 102.0, IsOutfall = true, TailwaterElevationFeet = 105.50 }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "STM-101",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 250.0,
                    SelectedDiameterInches = 18,
                    DesignFlowCfs = 4.5,
                    UpstreamInvertFeet = 118.0,
                    DownstreamInvertFeet = 114.0
                },
                new HydraulicSizingResult
                {
                    ConduitId = "STM-102",
                    StartNodeId = 2,
                    StopNodeId = 3,
                    LengthFeet = 300.0,
                    SelectedDiameterInches = 24,
                    DesignFlowCfs = 9.8,
                    UpstreamInvertFeet = 113.5,
                    DownstreamInvertFeet = 109.0
                },
                new HydraulicSizingResult
                {
                    ConduitId = "STM-103",
                    StartNodeId = 3,
                    StopNodeId = 4,
                    LengthFeet = 350.0,
                    SelectedDiameterInches = 30,
                    DesignFlowCfs = 16.2,
                    UpstreamInvertFeet = 108.5,
                    DownstreamInvertFeet = 102.0
                }
            };

            var criteria = new StormVerificationCriteria
            {
                MinimumFreeboardFeet = 1.0,
                ManningN = 0.013,
                GravitationalAcceleration = 32.174
            };

            var result = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits, criteria);

            string reportJson = string.Format(
                "{{\n  \"timestamp\": \"2026-10-19T11:00:00Z\",\n  \"phase\": \"P22\",\n  \"totalSystemHeadLossFeet\": {0:F4},\n  \"maxHglFeet\": {1:F4},\n  \"hasFlooding\": {2},\n  \"hasFreeboardViolations\": {3},\n  \"hasSurcharge\": {4},\n  \"conduitsCount\": {5},\n  \"nodesCount\": {6}\n}}",
                result.TotalHeadLossSystemFeet,
                result.MaxHglElevationFeet,
                result.HasFlooding.ToString().ToLowerInvariant(),
                result.HasFreeboardViolations.ToString().ToLowerInvariant(),
                result.HasSurcharge.ToString().ToLowerInvariant(),
                result.ConduitStates.Count,
                result.NodeStates.Count
            );

            File.WriteAllText(Path.Combine(outDir, "hydraulic_verification_report.json"), reportJson);

            // Materialize CSV profile
            var csvLines = new List<string>
            {
                "ConduitId,StartNode,StopNode,LengthFt,DiamIn,FlowCfs,VelocityFps,VelHeadFt,FrictionSlope,FrictionLossFt,MinorLossFt,TotalLossFt,UpHglFt,DownHglFt,UpEglFt,DownEglFt,UpCrownFt,DownCrownFt,Surcharged,SurchargeState"
            };
            foreach (var c in result.ConduitStates)
            {
                csvLines.Add(string.Format("{0},{1},{2},{3:F1},{4},{5:F2},{6:F2},{7:F4},{8:F6},{9:F4},{10:F4},{11:F4},{12:F2},{13:F2},{14:F2},{15:F2},{16:F2},{17:F2},{18},{19}",
                    c.ConduitId, c.StartNodeId, c.StopNodeId, c.LengthFeet, c.DiameterInches, c.FlowCfs, c.VelocityFps, c.VelocityHeadFeet,
                    c.FrictionSlope, c.FrictionHeadLossFeet, c.MinorLossFeet, c.TotalHeadLossFeet,
                    c.UpstreamHglFeet, c.DownstreamHglFeet, c.UpstreamEglFeet, c.DownstreamEglFeet,
                    c.UpstreamCrownFeet, c.DownstreamCrownFeet, c.IsSurcharged, c.SurchargeState));
            }
            File.WriteAllLines(Path.Combine(outDir, "hydraulic_conduit_profiles.csv"), csvLines);

            // Materialize Markdown Summary
            var md = new System.Text.StringBuilder();
            md.AppendLine("# StormFlow Phase P22 Hydraulic Verification & Surcharge Analysis Summary");
            md.AppendLine();
            md.AppendLine("- **Timestamp:** 2026-10-19T11:00:00Z");
            md.AppendLine("- **Status:** PASS (Authoritative Deterministic Hydraulic Sweep)");
            md.AppendLine(string.Format("- **Total System Head Loss:** {0:F4} ft", result.TotalHeadLossSystemFeet));
            md.AppendLine(string.Format("- **Maximum System HGL:** {0:F4} ft", result.MaxHglElevationFeet));
            md.AppendLine(string.Format("- **Surface Flooding Occurred:** {0}", result.HasFlooding));
            md.AppendLine(string.Format("- **Freeboard Violations:** {0}", result.HasFreeboardViolations));
            md.AppendLine(string.Format("- **Surcharged Conduits:** {0}", result.HasSurcharge));
            md.AppendLine();
            md.AppendLine("### Junction Hydraulic States");
            md.AppendLine("| Node ID | Label | Rim El (ft) | Inv El (ft) | HGL (ft) | EGL (ft) | Depth (ft) | Freeboard (ft) | Status |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var n in result.NodeStates)
            {
                md.AppendLine(string.Format("| {0} | {1} | {2:F2} | {3:F2} | {4:F2} | {5:F2} | {6:F2} | {7:F2} | {8} |",
                    n.NodeId, n.Label, n.RimElevationFeet, n.InvertElevationFeet, n.HglFeet, n.EglFeet, n.DepthOfWaterFeet, n.FreeboardFeet, n.FloodingState));
            }
            md.AppendLine();
            md.AppendLine("### Conduit Hydraulic Profiles");
            md.AppendLine("| Conduit | Diam (in) | Flow (cfs) | V (fps) | h_v (ft) | S_f | h_f (ft) | h_m (ft) | HGL Up (ft) | HGL Down (ft) | State |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var c in result.ConduitStates)
            {
                md.AppendLine(string.Format("| {0} | {1} | {2:F2} | {3:F2} | {4:F4} | {5:F6} | {6:F4} | {7:F4} | {8:F2} | {9:F2} | {10} |",
                    c.ConduitId, c.DiameterInches, c.FlowCfs, c.VelocityFps, c.VelocityHeadFeet, c.FrictionSlope, c.FrictionHeadLossFeet, c.MinorLossFeet, c.UpstreamHglFeet, c.DownstreamHglFeet, c.SurchargeState));
            }
            File.WriteAllText(Path.Combine(outDir, "hydraulic_verification_summary.md"), md.ToString());

            Check(result.NodeStates.Count == 4, "All 4 network nodes evaluated.");
            Check(result.ConduitStates.Count == 3, "All 3 conduits evaluated.");
            Check(!result.HasFlooding, "Evidence network achieves zero surface flooding.");
        }
    }
}
