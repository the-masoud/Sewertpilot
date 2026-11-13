using System;
using System.Globalization;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        /// <summary>
        /// Probe 1 (N05 Falsification):
        /// Pass a junction with Elevation = 100.0 and MaxDepth = 0.0 (and another with MaxDepth = -2.0).
        /// Assertion: Assert that IncompleteDataException is thrown with error code "SWMM_MISSING_RIM_ELEVATION".
        /// Anti-Cheat: Assert that RimElevation does NOT equal 106.0 or 100.0.
        /// </summary>
        private static void N05_JunctionRimFailsClosedWithoutPositiveDepth()
        {
            // Probe 1a: Elevation = 100.0, MaxDepth = 0.0
            var modelZero = new SwmmModel();
            modelZero.Junctions["J-ZERO"] = new SwmmJunctionDto
            {
                Name = "J-ZERO",
                Elevation = 100.0,
                MaxDepth = 0.0
            };

            IncompleteDataException exZero = null;
            NetworkReadResult resZero = null;
            try
            {
                resZero = SwmmInpNetworkAdapter.ConvertToNetworkReadResult(modelZero);
            }
            catch (IncompleteDataException ex)
            {
                exZero = ex;
            }

            Check(exZero != null, "Probe 1: Junction with MaxDepth = 0.0 must throw IncompleteDataException.");
            Check(exZero.ErrorCode == "SWMM_MISSING_RIM_ELEVATION",
                "Probe 1: IncompleteDataException ErrorCode must be 'SWMM_MISSING_RIM_ELEVATION'; was '" + (exZero != null ? exZero.ErrorCode : "null") + "'.");
            if (resZero != null)
            {
                var st = resZero.Structures.FirstOrDefault(s => s.Label == "J-ZERO");
                if (st != null && st.RimElevation.HasValue)
                {
                    Check(st.RimElevation.Value != 106.0, "Anti-Cheat: RimElevation must not equal 106.0.");
                    Check(st.RimElevation.Value != 100.0, "Anti-Cheat: RimElevation must not equal 100.0.");
                }
            }

            // Probe 1b: Elevation = 100.0, MaxDepth = -2.0
            var modelNeg = new SwmmModel();
            modelNeg.Junctions["J-NEG"] = new SwmmJunctionDto
            {
                Name = "J-NEG",
                Elevation = 100.0,
                MaxDepth = -2.0
            };

            IncompleteDataException exNeg = null;
            NetworkReadResult resNeg = null;
            try
            {
                resNeg = SwmmInpNetworkAdapter.ConvertToNetworkReadResult(modelNeg);
            }
            catch (IncompleteDataException ex)
            {
                exNeg = ex;
            }

            Check(exNeg != null, "Probe 1: Junction with MaxDepth = -2.0 must throw IncompleteDataException.");
            Check(exNeg.ErrorCode == "SWMM_MISSING_RIM_ELEVATION",
                "Probe 1: IncompleteDataException ErrorCode must be 'SWMM_MISSING_RIM_ELEVATION'; was '" + (exNeg != null ? exNeg.ErrorCode : "null") + "'.");
            if (resNeg != null)
            {
                var st = resNeg.Structures.FirstOrDefault(s => s.Label == "J-NEG");
                if (st != null && st.RimElevation.HasValue)
                {
                    Check(st.RimElevation.Value != 106.0, "Anti-Cheat: RimElevation must not equal 106.0.");
                    Check(st.RimElevation.Value != 100.0, "Anti-Cheat: RimElevation must not equal 100.0.");
                }
            }

            // Anti-Cheat on mapped structure: valid positive depth must equal invert + depth, not 106.0 or 100.0
            var modelValid = new SwmmModel();
            modelValid.Junctions["J-VALID"] = new SwmmJunctionDto
            {
                Name = "J-VALID",
                Elevation = 100.0,
                MaxDepth = 8.5
            };
            var resValid = SwmmInpNetworkAdapter.ConvertToNetworkReadResult(modelValid);
            var stValid = resValid.Structures.Single(s => s.Label == "J-VALID");
            Check(stValid.RimElevation.HasValue && Math.Abs(stValid.RimElevation.Value - 108.5) < 1e-6,
                "Valid junction RimElevation must equal 108.5; was " + (stValid.RimElevation.HasValue ? stValid.RimElevation.Value.ToString("R", CultureInfo.InvariantCulture) : "<null>") + ".");
            Check(stValid.RimElevation.Value != 106.0, "Anti-Cheat: RimElevation must not equal 106.0.");
            Check(stValid.RimElevation.Value != 100.0, "Anti-Cheat: RimElevation must not equal 100.0.");
        }

        /// <summary>
        /// Probe 2 (N08 Negative Cover Falsification):
        /// Pass a pipe with upstream structure RimElevation = 100.0 ft and CrownElevation = 100.8 ft (0.8 ft protrusion).
        /// Assertion: Assert that computed numerical cover equals -0.8 (+- 10^-6).
        /// Anti-Cheat: Assert that the rendered report cell string is "-0.80" and explicitly assert cellString != "0.00" and cellString != "0.0".
        /// </summary>
        private static void N08_NegativeCoverIsReportedNotClamped()
        {
            var verification = new StormNetworkVerificationResult();
            verification.NodeStates.Add(new StormNodeHydraulicState
            {
                NodeId = 1,
                Label = "MH-1",
                RimElevationFeet = 100.0,
                InvertElevationFeet = 99.3,
                HglFeet = 99.9
            });
            verification.NodeStates.Add(new StormNodeHydraulicState
            {
                NodeId = 2,
                Label = "MH-2",
                RimElevationFeet = 99.0,
                InvertElevationFeet = 98.0,
                HglFeet = 98.6
            });
            verification.ConduitStates.Add(new StormConduitHydraulicState
            {
                ConduitId = "C-PROTRUDE",
                StartNodeId = 1,
                StopNodeId = 2,
                LengthFeet = 200.0,
                DiameterInches = 18,
                UpstreamInvertFeet = 99.3,
                DownstreamInvertFeet = 98.0,
                FlowCfs = 5.0,
                VelocityFps = 3.0
            });

            var context = new StormExecutiveReportContext { VerificationResult = verification };
            var package = EngineeringReportingEngine.BuildPackageFromContext(context);
            var item = package.HydraulicDesign.Conduits.Single(c => c.ConduitId == "C-PROTRUDE");

            // Assertion: Assert that computed numerical cover equals -0.8 (+- 10^-6)
            double computedCover = item.UpstreamEarthCoverFeet;
            Check(Math.Abs(computedCover - (-0.8)) < 1e-6,
                "Computed numerical cover must equal -0.8 (+- 1e-6); was " + computedCover.ToString("R", CultureInfo.InvariantCulture));

            // Anti-Cheat: Assert that the rendered report cell string is "-0.80" and explicitly assert cellString != "0.00" and cellString != "0.0"
            string reportMarkdown = EngineeringReportingEngine.GenerateHydraulicDesignReport(package.HydraulicDesign);
            string cellString = CoverCell(reportMarkdown, "C-PROTRUDE");
            Check(cellString == "-0.80",
                "Rendered report cell string must be '-0.80'; was '" + cellString + "'.");
            Check(cellString != "0.00",
                "Anti-Cheat: cellString must not equal '0.00'.");
            Check(cellString != "0.0",
                "Anti-Cheat: cellString must not equal '0.0'.");
        }

        /// <summary>
        /// Probe 3 (N08 Missing Topology Falsification):
        /// Pass a pipe referencing a non-existent UpstreamNodeId.
        /// Assertion: Assert that the rendered report cell is literally "UNAVAILABLE".
        /// Anti-Cheat: Assert cellString != "0.00" and cellString != "0.0".
        /// </summary>
        private static void N08_OrphanedNodeRendersUnavailableCover()
        {
            var verification = new StormNetworkVerificationResult();
            verification.NodeStates.Add(new StormNodeHydraulicState
            {
                NodeId = 2,
                Label = "MH-2",
                RimElevationFeet = 99.0,
                InvertElevationFeet = 98.0,
                HglFeet = 98.6
            });
            verification.ConduitStates.Add(new StormConduitHydraulicState
            {
                ConduitId = "C-ORPHAN",
                StartNodeId = 999,
                StopNodeId = 2,
                LengthFeet = 150.0,
                DiameterInches = 18,
                UpstreamInvertFeet = 99.0,
                DownstreamInvertFeet = 98.0,
                FlowCfs = 4.0,
                VelocityFps = 2.8
            });

            var context = new StormExecutiveReportContext { VerificationResult = verification };
            var package = EngineeringReportingEngine.BuildPackageFromContext(context);
            string reportMarkdown = EngineeringReportingEngine.GenerateHydraulicDesignReport(package.HydraulicDesign);
            string cellString = CoverCell(reportMarkdown, "C-ORPHAN");

            // Assertion: Assert that the rendered report cell is literally "UNAVAILABLE"
            Check(cellString == "UNAVAILABLE",
                "Rendered report cell for orphaned node must literally be 'UNAVAILABLE'; was '" + cellString + "'.");

            // Anti-Cheat: Assert cellString != "0.00" and cellString != "0.0"
            Check(cellString != "0.00",
                "Anti-Cheat: cellString must not equal '0.00'.");
            Check(cellString != "0.0",
                "Anti-Cheat: cellString must not equal '0.0'.");
        }

        private static string CoverCell(string markdown, string conduitId)
        {
            foreach (string line in markdown.Split('\n'))
            {
                if (!line.Contains("`" + conduitId + "`")) continue;
                string[] cells = line.Split('|');
                if (cells.Length <= 13)
                {
                    throw new InvalidOperationException("Conduit row for '" + conduitId + "' has unexpected column count: " + cells.Length);
                }
                return cells[13].Trim();
            }
            throw new InvalidOperationException("Conduit row for '" + conduitId + "' was not found in report markdown.");
        }
    }
}
