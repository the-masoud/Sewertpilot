using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static void P20_TriangularGutter_BenchmarkCalculation()
        {
            var gutter = new GutterCrossSection
            {
                CrossSlopeSx = 0.02,
                LongitudinalSlopeSL = 0.01,
                ManningN = 0.016,
                GutterWidthFeet = 0.0,
                LocalDepressionInches = 0.0
            };

            double flow = 2.0; // cfs
            var res = StormFlowInletHydraulicsEngine.ComputeGutterFlow(flow, gutter);

            Check(res != null, "Gutter flow result returned.");
            // Factor Ku/n * Sx^(5/3) * SL^(1/2) = (0.56/0.016) * 0.02^(5/3) * 0.1 = 35 * 0.0014736 * 0.1 = 0.0051576
            // T = (2.0 / 0.0051576)^(3/8) = (387.777)^0.375 = 9.388 ft
            Check(Math.Abs(res.SpreadFeet - 9.39) < 0.05, "Gutter spread is ~9.39 ft.");
            // Depth at curb: d = T * Sx = 9.39 * 0.02 * 12 = 2.25 inches
            Check(Math.Abs(res.DepthAtCurbInches - 2.25) < 0.05, "Depth at curb is ~2.25 inches.");
            // Area = 0.5 * Sx * T^2 = 0.5 * 0.02 * 9.39^2 = 0.882 sq ft
            Check(Math.Abs(res.CrossSectionalAreaSqFt - 0.88) < 0.05, "Flow area is ~0.88 sq ft.");
            // Velocity = Q / A = 2.0 / 0.882 = 2.27 fps
            Check(Math.Abs(res.VelocityFps - 2.27) < 0.05, "Flow velocity is ~2.27 fps.");
            Check(res.FrontalFlowRatio == 1.0, "Uniform triangular gutter has Eo = 1.0.");
        }

        private static void P20_CompositeGutter_DepressedSectionFrontalFlow()
        {
            var gutter = new GutterCrossSection
            {
                CrossSlopeSx = 0.02,
                LongitudinalSlopeSL = 0.02,
                ManningN = 0.016,
                GutterWidthFeet = 2.0,
                LocalDepressionInches = 2.0 // 2" depression in 2 ft gutter
            };

            Check(gutter.IsComposite, "Gutter is classified as composite.");
            // Sw = 0.02 + (2/12)/2.0 = 0.02 + 0.08333 = 0.10333
            Check(Math.Abs(gutter.GetGutterCrossSlopeSw() - 0.10333) < 1e-4, "Gutter cross slope Sw is ~0.1033.");

            double flow = 12.0; // cfs (exceeds depressed gutter capacity of 7.2 cfs)
            var res = StormFlowInletHydraulicsEngine.ComputeGutterFlow(flow, gutter);

            Check(res.SpreadFeet > 2.0, "Spread exceeds depressed gutter width.");
            Check(res.FrontalFlowRatio > 0.40 && res.FrontalFlowRatio < 0.95, "Frontal flow ratio Eo is valid fraction.");
            Check(res.DepthAtCurbInches > 2.0, "Depth at curb reflects composite depression.");
            Check(res.VelocityFps > 0.0, "Velocity is positive.");
        }

        private static void P20_CurbOpening_OnContinuousGrade_100PercentInterception()
        {
            var inlet = new InletConfiguration
            {
                InletId = "INLET-01",
                TargetNodeId = 10,
                Type = InletType.CurbOpening,
                Location = InletLocation.OnContinuousGrade,
                LengthFeet = 25.0, // Large length for 100% capture
                CurbOpeningHeightInches = 6.0,
                Gutter = new GutterCrossSection
                {
                    CrossSlopeSx = 0.02,
                    LongitudinalSlopeSL = 0.01,
                    ManningN = 0.016
                }
            };

            double approachQ = 1.5; // cfs
            var res = StormFlowInletHydraulicsEngine.ComputeInletInterception(inlet, approachQ);

            Check(res.InterceptionEfficiency == 1.0, "Efficiency is 100%.");
            Check(Math.Abs(res.InterceptedFlowCfs - 1.5) < 1e-4, "Intercepted flow equals approach flow.");
            Check(res.BypassFlowCfs == 0.0, "Bypass flow is zero.");
            Check(res.Diagnostics.ContainsKey("100PercentInterceptionLengthFeet"), "Diagnostics contains LT.");
            double lt = double.Parse(res.Diagnostics["100PercentInterceptionLengthFeet"]);
            Check(inlet.LengthFeet >= lt, "Inlet length is >= LT.");
        }

        private static void P20_CurbOpening_OnContinuousGrade_PartialEfficiency()
        {
            var inlet = new InletConfiguration
            {
                InletId = "INLET-02",
                TargetNodeId = 11,
                Type = InletType.CurbOpening,
                Location = InletLocation.OnContinuousGrade,
                LengthFeet = 5.0, // Typical 5 ft curb opening
                CurbOpeningHeightInches = 6.0,
                Gutter = new GutterCrossSection
                {
                    CrossSlopeSx = 0.02,
                    LongitudinalSlopeSL = 0.02,
                    ManningN = 0.016
                }
            };

            double approachQ = 4.0; // cfs
            var res = StormFlowInletHydraulicsEngine.ComputeInletInterception(inlet, approachQ);

            double lt = double.Parse(res.Diagnostics["100PercentInterceptionLengthFeet"]);
            Check(inlet.LengthFeet < lt, "Inlet length is less than LT for 4 cfs.");

            // Efficiency E = 1 - (1 - L/LT)^1.8
            double expectedE = 1.0 - Math.Pow(1.0 - (5.0 / lt), 1.8);
            Check(Math.Abs(res.InterceptionEfficiency - expectedE) < 1e-3, "Calculated efficiency matches HEC-22 formula.");
            Check(res.InterceptedFlowCfs > 0.0 && res.InterceptedFlowCfs < 4.0, "Intercepted flow is positive and less than total.");
            Check(Math.Abs((res.InterceptedFlowCfs + res.BypassFlowCfs) - 4.0) < 1e-3, "Mass balance: Qi + Qb = Q.");
        }

        private static void P20_CurbOpening_InSag_WeirAndOrificeRegimes()
        {
            var inlet = new InletConfiguration
            {
                InletId = "INLET-SAG",
                TargetNodeId = 12,
                Type = InletType.CurbOpening,
                Location = InletLocation.InSag,
                LengthFeet = 10.0,
                CurbOpeningHeightInches = 6.0, // h = 0.5 ft
                Gutter = new GutterCrossSection
                {
                    CrossSlopeSx = 0.02,
                    LongitudinalSlopeSL = 0.005, // Flatter sag slope
                    ManningN = 0.016
                }
            };

            // Small flow -> depth <= 6 inches (h) -> Weir regime
            var resShallow = StormFlowInletHydraulicsEngine.ComputeInletInterception(inlet, localRunoffCfs: 0.8);
            Check(resShallow.FlowRegime == InletFlowRegime.Weir, "Shallow sag flow operates in Weir regime.");
            Check(resShallow.InterceptedFlowCfs > 0, "Weir interception is positive.");

            // Large flow -> depth > 1.4 * 6" = 8.4 inches -> Orifice regime
            var resDeep = StormFlowInletHydraulicsEngine.ComputeInletInterception(inlet, localRunoffCfs: 50.0);
            Check(resDeep.FlowRegime == InletFlowRegime.Orifice || resDeep.FlowRegime == InletFlowRegime.Transitional,
                "Submerged sag flow operates in Orifice or Transitional regime.");
            Check(resDeep.InterceptedFlowCfs > resShallow.InterceptedFlowCfs, "Orifice flow capacity exceeds shallow weir capacity.");
        }

        private static void P20_GrateInlet_OnContinuousGrade_FrontalAndSideInterception()
        {
            var inlet = new InletConfiguration
            {
                InletId = "INLET-GRATE",
                TargetNodeId = 13,
                Type = InletType.Grate,
                Location = InletLocation.OnContinuousGrade,
                LengthFeet = 3.0,
                WidthFeet = 2.0,
                Grate = GrateType.P50,
                Gutter = new GutterCrossSection
                {
                    CrossSlopeSx = 0.02,
                    LongitudinalSlopeSL = 0.03, // Steep grade -> higher velocity
                    ManningN = 0.016
                }
            };

            double approachQ = 3.0; // cfs
            var res = StormFlowInletHydraulicsEngine.ComputeInletInterception(inlet, approachQ);

            Check(res.Diagnostics.ContainsKey("SplashOverVelocityVo"), "Splash over velocity recorded.");
            Check(res.Diagnostics.ContainsKey("FrontalFlowEfficiencyRf"), "Frontal flow efficiency Rf recorded.");
            Check(res.Diagnostics.ContainsKey("SideFlowEfficiencyRs"), "Side flow efficiency Rs recorded.");

            double rf = double.Parse(res.Diagnostics["FrontalFlowEfficiencyRf"]);
            double rs = double.Parse(res.Diagnostics["SideFlowEfficiencyRs"]);
            Check(rf > 0.0 && rf <= 1.0, "Rf is valid ratio.");
            Check(rs > 0.0 && rs <= 1.0, "Rs is valid ratio.");
            Check(res.InterceptedFlowCfs > 0.0, "Grate intercepts flow on grade.");
            Check(Math.Abs((res.InterceptedFlowCfs + res.BypassFlowCfs) - 3.0) < 1e-3, "Mass balance preserved on grate.");
        }

        private static void P20_GrateInlet_InSag_CloggingFactor()
        {
            var inletClean = new InletConfiguration
            {
                InletId = "GRATE-CLEAN",
                TargetNodeId = 14,
                Type = InletType.Grate,
                Location = InletLocation.InSag,
                LengthFeet = 4.0,
                WidthFeet = 2.0,
                CloggingFactor = 0.0,
                Gutter = new GutterCrossSection { CrossSlopeSx = 0.02, LongitudinalSlopeSL = 0.005 }
            };

            var inletClogged = new InletConfiguration
            {
                InletId = "GRATE-CLOGGED",
                TargetNodeId = 14,
                Type = InletType.Grate,
                Location = InletLocation.InSag,
                LengthFeet = 4.0,
                WidthFeet = 2.0,
                CloggingFactor = 0.50, // 50% clogged standard sag criterion
                Gutter = new GutterCrossSection { CrossSlopeSx = 0.02, LongitudinalSlopeSL = 0.005 }
            };

            double q = 10.0;
            var resClean = StormFlowInletHydraulicsEngine.ComputeInletInterception(inletClean, q);
            var resClogged = StormFlowInletHydraulicsEngine.ComputeInletInterception(inletClogged, q);

            double cleanCap = double.Parse(resClean.Diagnostics["SagTheoreticalCapacityCfs"]);
            double cloggedCap = double.Parse(resClogged.Diagnostics["SagCloggedCapacityCfs"]);

            Check(Math.Abs(cloggedCap - (0.50 * cleanCap)) < 0.02, "Clogged capacity is exactly 50% of clean capacity.");
            Check(resClogged.InterceptedFlowCfs <= resClean.InterceptedFlowCfs, "Clogged grate intercepts less or equal flow.");
        }

        private static void P20_CombinationInlet_OnGradeAndInSag()
        {
            var inletGrade = new InletConfiguration
            {
                InletId = "COMBO-GRADE",
                TargetNodeId = 15,
                Type = InletType.Combination,
                Location = InletLocation.OnContinuousGrade,
                LengthFeet = 5.0,
                WidthFeet = 2.0,
                Grate = GrateType.CurvedVane,
                Gutter = new GutterCrossSection { CrossSlopeSx = 0.02, LongitudinalSlopeSL = 0.015 }
            };

            var resGrade = StormFlowInletHydraulicsEngine.ComputeInletInterception(inletGrade, localRunoffCfs: 3.5);
            Check(resGrade.InterceptedFlowCfs > 0.0, "Combination inlet intercepts flow on grade.");
            Check(resGrade.BypassFlowCfs >= 0.0, "Bypass is non-negative.");

            var inletSag = new InletConfiguration
            {
                InletId = "COMBO-SAG",
                TargetNodeId = 15,
                Type = InletType.Combination,
                Location = InletLocation.InSag,
                LengthFeet = 5.0,
                WidthFeet = 2.0,
                CurbOpeningHeightInches = 6.0,
                Gutter = new GutterCrossSection { CrossSlopeSx = 0.02, LongitudinalSlopeSL = 0.005 }
            };

            var resSag = StormFlowInletHydraulicsEngine.ComputeInletInterception(inletSag, localRunoffCfs: 8.0);
            Check(resSag.InterceptedFlowCfs > 0.0, "Combination inlet intercepts flow in sag.");
            double cap = double.Parse(resSag.Diagnostics["SagTheoreticalCapacityCfs"]);
            Check(cap > 5.0, "Combined capacity reflects both grate and curb opening.");
        }

        private static void P20_InletNetwork_CarryoverRoutingAndCascade()
        {
            // Inlet 1: On grade, 5 ft curb opening, generates bypass flow
            var inlet1 = new InletConfiguration
            {
                InletId = "INLET-1",
                TargetNodeId = 101,
                DownstreamInletId = "INLET-2", // Routes carryover to Inlet 2
                Type = InletType.CurbOpening,
                Location = InletLocation.OnContinuousGrade,
                LengthFeet = 5.0,
                AllowableSpreadFeet = 8.0,
                Gutter = new GutterCrossSection { CrossSlopeSx = 0.02, LongitudinalSlopeSL = 0.02 }
            };

            // Inlet 2: On grade, receives local runoff + Inlet 1 carryover, passes bypass to Inlet 3
            var inlet2 = new InletConfiguration
            {
                InletId = "INLET-2",
                TargetNodeId = 102,
                DownstreamInletId = "INLET-3", // Routes carryover to Inlet 3
                Type = InletType.CurbOpening,
                Location = InletLocation.OnContinuousGrade,
                LengthFeet = 8.0,
                AllowableSpreadFeet = 8.0,
                Gutter = new GutterCrossSection { CrossSlopeSx = 0.02, LongitudinalSlopeSL = 0.015 }
            };

            // Inlet 3: In sag (low point), terminal inlet
            var inlet3 = new InletConfiguration
            {
                InletId = "INLET-3",
                TargetNodeId = 103,
                DownstreamInletId = null, // Terminal
                Type = InletType.Combination,
                Location = InletLocation.InSag,
                LengthFeet = 10.0,
                WidthFeet = 2.0,
                AllowableSpreadFeet = 10.0,
                Gutter = new GutterCrossSection { CrossSlopeSx = 0.02, LongitudinalSlopeSL = 0.005 }
            };

            var network = new Dictionary<string, InletConfiguration>
            {
                { "INLET-1", inlet1 },
                { "INLET-2", inlet2 },
                { "INLET-3", inlet3 }
            };

            var localRunoffs = new Dictionary<string, double>
            {
                { "INLET-1", 4.0 }, // cfs
                { "INLET-2", 2.0 }, // cfs
                { "INLET-3", 1.0 }  // cfs
            };

            var routingResult = StormFlowInletHydraulicsEngine.RouteInletNetwork(network, localRunoffs);

            Check(routingResult.InletResults.Count == 3, "3 inlet results evaluated in network.");

            var res1 = routingResult.InletResults.Single(r => r.InletId == "INLET-1");
            Check(res1.LocalRunoffCfs == 4.0 && res1.CarryoverInflowCfs == 0.0, "Inlet 1 has zero carryover approach.");
            Check(res1.BypassFlowCfs > 0.0, "Inlet 1 produces bypass flow.");

            var res2 = routingResult.InletResults.Single(r => r.InletId == "INLET-2");
            Check(res2.LocalRunoffCfs == 2.0, "Inlet 2 has 2.0 cfs local runoff.");
            Check(Math.Abs(res2.CarryoverInflowCfs - res1.BypassFlowCfs) < 1e-4, "Inlet 2 receives exact Inlet 1 bypass flow.");
            Check(Math.Abs(res2.TotalApproachFlowCfs - (2.0 + res1.BypassFlowCfs)) < 1e-4, "Inlet 2 total approach = local + carryover.");

            var res3 = routingResult.InletResults.Single(r => r.InletId == "INLET-3");
            Check(Math.Abs(res3.CarryoverInflowCfs - res2.BypassFlowCfs) < 1e-4, "Inlet 3 receives Inlet 2 bypass flow.");

            // Total mass balance verification:
            // Total system local approach = 4.0 + 2.0 + 1.0 = 7.0 cfs
            Check(Math.Abs(routingResult.TotalApproachFlowCfs - 7.0) < 1e-4, "Total approach flow sum is 7.0 cfs.");
            double totalCaptured = res1.InterceptedFlowCfs + res2.InterceptedFlowCfs + res3.InterceptedFlowCfs;
            Check(Math.Abs(routingResult.TotalInterceptedFlowCfs - totalCaptured) < 1e-4, "Total intercepted matches sum of inlets.");
            Check(Math.Abs((routingResult.TotalInterceptedFlowCfs + routingResult.TotalSystemBypassFlowCfs) - 7.0) < 1e-3,
                "Global system mass balance: Intercepted + Bypass = Total Approach.");
        }

        private static void P20_FailClosed_InvalidInputsRejected()
        {
            // Negative slope
            Throws<ArgumentException>(() =>
            {
                var g = new GutterCrossSection { CrossSlopeSx = -0.02 };
                g.Validate();
            }, "Negative cross slope rejected.");

            // Negative Manning's n
            Throws<ArgumentException>(() =>
            {
                var g = new GutterCrossSection { ManningN = 0.0 };
                g.Validate();
            }, "Zero Manning's n rejected.");

            // Negative flow rate
            Throws<ArgumentException>(() =>
            {
                var g = new GutterCrossSection();
                StormFlowInletHydraulicsEngine.ComputeGutterFlow(-1.0, g);
            }, "Negative flow rate rejected.");

            // Invalid inlet ID
            Throws<ArgumentException>(() =>
            {
                var inlet = new InletConfiguration { InletId = "", TargetNodeId = 1 };
                inlet.Validate();
            }, "Empty inlet ID rejected.");

            // Invalid target node ID
            Throws<ArgumentException>(() =>
            {
                var inlet = new InletConfiguration { InletId = "I1", TargetNodeId = 0 };
                inlet.Validate();
            }, "Zero target node ID rejected.");

            // Clogging factor >= 1.0
            Throws<ArgumentException>(() =>
            {
                var inlet = new InletConfiguration { InletId = "I1", TargetNodeId = 1, CloggingFactor = 1.0 };
                inlet.Validate();
            }, "Clogging factor >= 1.0 rejected.");

            // Circular network dependency
            Throws<InvalidOperationException>(() =>
            {
                var i1 = new InletConfiguration { InletId = "A", TargetNodeId = 1, DownstreamInletId = "B" };
                var i2 = new InletConfiguration { InletId = "B", TargetNodeId = 2, DownstreamInletId = "A" };
                var net = new Dictionary<string, InletConfiguration> { { "A", i1 }, { "B", i2 } };
                StormFlowInletHydraulicsEngine.RouteInletNetwork(net, new Dictionary<string, double> { { "A", 1.0 }, { "B", 1.0 } });
            }, "Circular inlet carryover dependency rejected.");
        }

        internal static void P20_MaterializeAuthoritativeEvidence()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(root) && !File.Exists(Path.Combine(root, "SewerPilot.sln")))
            {
                root = Path.GetDirectoryName(root);
            }
            string outputDir = Path.Combine(root, "docs", "evidence", "p20", "20261019-stormflow-inlets");
            Directory.CreateDirectory(outputDir);

            // 1. Authoritative Gutter Cross Sections
            var triangularGutter = new GutterCrossSection
            {
                CrossSlopeSx = 0.02,
                LongitudinalSlopeSL = 0.015,
                ManningN = 0.016,
                GutterWidthFeet = 0.0,
                LocalDepressionInches = 0.0
            };

            var compositeGutter1 = new GutterCrossSection
            {
                CrossSlopeSx = 0.02,
                LongitudinalSlopeSL = 0.015,
                ManningN = 0.016,
                GutterWidthFeet = 2.0,
                LocalDepressionInches = 2.0
            };

            var compositeGutter2 = new GutterCrossSection
            {
                CrossSlopeSx = 0.02,
                LongitudinalSlopeSL = 0.012,
                ManningN = 0.016,
                GutterWidthFeet = 2.0,
                LocalDepressionInches = 2.0
            };

            var sagGutter = new GutterCrossSection
            {
                CrossSlopeSx = 0.02,
                LongitudinalSlopeSL = 0.005,
                ManningN = 0.016,
                GutterWidthFeet = 2.0,
                LocalDepressionInches = 3.0
            };

            // 2. Authoritative Gutter Flow Calculations across flow rates
            var gutterFlowResults = new List<GutterFlowResult>
            {
                StormFlowInletHydraulicsEngine.ComputeGutterFlow(3.0, triangularGutter),
                StormFlowInletHydraulicsEngine.ComputeGutterFlow(5.0, compositeGutter1),
                StormFlowInletHydraulicsEngine.ComputeGutterFlow(7.5, compositeGutter2),
                StormFlowInletHydraulicsEngine.ComputeGutterFlow(10.0, sagGutter)
            };
            foreach (var gf in gutterFlowResults)
            {
                Check(gf != null && gf.SpreadFeet > 0 && gf.VelocityFps > 0, "Gutter flow computation valid.");
            }

            // 3. Authoritative Inlets based on City of Austin DCM Standards
            var inlet1 = new InletConfiguration
            {
                InletId = "INLET-01",
                Type = InletType.CurbOpening,
                Location = InletLocation.OnContinuousGrade,
                CurbOpeningLengthFeet = 10.0,
                ThroatHeightInches = 6.0,
                Gutter = compositeGutter1,
                AllowableSpreadFeet = 8.0,
                TargetNodeId = 10,
                DownstreamInletId = "INLET-02"
            };

            var inlet2 = new InletConfiguration
            {
                InletId = "INLET-02",
                Type = InletType.Combination,
                Location = InletLocation.OnContinuousGrade,
                CurbOpeningLengthFeet = 10.0,
                GrateWidthFeet = 2.0,
                GrateLengthFeet = 3.0,
                GrateType = GrateType.P50,
                ThroatHeightInches = 6.0,
                Gutter = compositeGutter2,
                AllowableSpreadFeet = 8.0,
                TargetNodeId = 10,
                DownstreamInletId = "INLET-03"
            };

            var inlet3 = new InletConfiguration
            {
                InletId = "INLET-03",
                Type = InletType.CurbOpening,
                Location = InletLocation.InSag,
                CurbOpeningLengthFeet = 15.0,
                ThroatHeightInches = 6.0,
                Gutter = sagGutter,
                AllowableSpreadFeet = 10.0,
                TargetNodeId = 12,
                DownstreamInletId = null // Terminal Sag
            };

            var network = new Dictionary<string, InletConfiguration>(StringComparer.OrdinalIgnoreCase)
            {
                { "INLET-01", inlet1 },
                { "INLET-02", inlet2 },
                { "INLET-03", inlet3 }
            };

            var localRunoffs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "INLET-01", 5.0 }, // cfs local approach
                { "INLET-02", 4.0 }, // cfs local approach
                { "INLET-03", 3.0 }  // cfs local approach
            };

            // 4. Cascade Network Routing & Spread Assessment
            var routing = StormFlowInletHydraulicsEngine.RouteInletNetwork(network, localRunoffs);
            Check(routing != null, "Inlet network routing returned.");
            Check(routing.InletResults.Count == 3, "All 3 inlets evaluated.");

            // Verify Inlet 1
            var res1 = routing.InletResults.Single(r => r.InletId == "INLET-01");
            Check(res1.LocalRunoffCfs == 5.0 && res1.CarryoverInflowCfs == 0.0, "Inlet 1 has zero carryover approach.");
            Check(res1.InterceptedFlowCfs > 0 && res1.BypassFlowCfs > 0, "Inlet 1 captures portion and produces bypass.");
            Check(Math.Abs(res1.TotalApproachFlowCfs - (res1.InterceptedFlowCfs + res1.BypassFlowCfs)) < 1e-4, "Inlet 1 mass balance intact.");

            // Verify Inlet 2
            var res2 = routing.InletResults.Single(r => r.InletId == "INLET-02");
            Check(res2.LocalRunoffCfs == 4.0, "Inlet 2 local approach is 4.0 cfs.");
            Check(Math.Abs(res2.CarryoverInflowCfs - res1.BypassFlowCfs) < 1e-4, "Inlet 2 receives exact Inlet 1 bypass.");
            Check(Math.Abs(res2.TotalApproachFlowCfs - (4.0 + res1.BypassFlowCfs)) < 1e-4, "Inlet 2 total approach = local + carryover.");

            // Verify Inlet 3 (In-Sag)
            var res3 = routing.InletResults.Single(r => r.InletId == "INLET-03");
            Check(res3.LocalRunoffCfs == 3.0, "Inlet 3 local approach is 3.0 cfs.");
            Check(Math.Abs(res3.CarryoverInflowCfs - res2.BypassFlowCfs) < 1e-4, "Inlet 3 receives Inlet 2 bypass.");
            Check(res3.InterceptedFlowCfs == res3.TotalApproachFlowCfs, "Inlet 3 in-sag captures 100% of approach.");
            Check(res3.BypassFlowCfs == 0.0, "Inlet 3 produces 0 bypass.");

            // System Mass Balance: 5.0 + 4.0 + 3.0 = 12.0 cfs
            Check(Math.Abs(routing.TotalApproachFlowCfs - 12.0) < 1e-4, "Total system approach flow is 12.0 cfs.");
            Check(Math.Abs(routing.TotalInterceptedFlowCfs - 12.0) < 1e-4, "Terminal sag captures entire system approach.");
            Check(routing.TotalSystemBypassFlowCfs == 0.0, "System terminal bypass is 0.0 cfs.");

            // 5. Materialize Authoritative Artifacts
            var serializer = RuntimeHostProtocol.Serializer();
            File.WriteAllText(Path.Combine(outputDir, "authorized-gutter-flow-results.json"), serializer.Serialize(gutterFlowResults), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDir, "authorized-inlet-interception-results.json"), serializer.Serialize(routing.InletResults), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDir, "authorized-inlet-network-routing-results.json"), serializer.Serialize(routing), Encoding.UTF8);

            string auditJson = string.Format(CultureInfo.InvariantCulture,
                "{{\n  \"TimestampUtc\": \"{0:o}\",\n  \"Phase\": \"P20\",\n  \"Milestone\": \"StormFlow Inlet & Catchbasin Hydraulics\",\n  \"GoverningStandard\": \"City of Austin Drainage Criteria Manual (DCM) / FHWA HEC-22\",\n  \"EvaluatedInlets\": {1},\n  \"TotalSystemApproachFlowCfs\": {2:F2},\n  \"TotalSystemInterceptedFlowCfs\": {3:F2},\n  \"TotalSystemBypassFlowCfs\": {4:F2},\n  \"Inlet01_InterceptedCfs\": {5:F2},\n  \"Inlet01_BypassCfs\": {6:F2},\n  \"Inlet01_Efficiency\": {7:F2},\n  \"Inlet01_SpreadFeet\": {8:F2},\n  \"Inlet02_InterceptedCfs\": {9:F2},\n  \"Inlet02_BypassCfs\": {10:F2},\n  \"Inlet02_Efficiency\": {11:F2},\n  \"Inlet02_SpreadFeet\": {12:F2},\n  \"Inlet03_InterceptedCfs\": {13:F2},\n  \"Inlet03_SpreadFeet\": {14:F2},\n  \"MassBalanceConserved\": true\n}}\n",
                DateTime.UtcNow,
                routing.InletResults.Count,
                routing.TotalApproachFlowCfs,
                routing.TotalInterceptedFlowCfs,
                routing.TotalSystemBypassFlowCfs,
                res1.InterceptedFlowCfs,
                res1.BypassFlowCfs,
                res1.InterceptionEfficiency,
                res1.GutterSpreadFeet,
                res2.InterceptedFlowCfs,
                res2.BypassFlowCfs,
                res2.InterceptionEfficiency,
                res2.GutterSpreadFeet,
                res3.InterceptedFlowCfs,
                res3.GutterSpreadFeet);
            File.WriteAllText(Path.Combine(outputDir, "inlet-hydraulics-audit.json"), auditJson, Encoding.UTF8);
        }
    }
}
