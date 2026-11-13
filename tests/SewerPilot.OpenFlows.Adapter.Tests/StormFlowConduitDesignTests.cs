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
        private static void P21_FullFlowManning_BenchmarkCalculation()
        {
            double dFt = 2.0; // 24-inch pipe
            double slope = 0.01; // 1%
            double n = 0.013;

            double qFull = StormFlowConduitDesignEngine.ComputeFullFlowDischarge(dFt, slope, n);
            double vFull = StormFlowConduitDesignEngine.ComputeFullFlowVelocity(dFt, slope, n);

            // Theoretical benchmark:
            // A = pi * 2^2 / 4 = 3.1416 sq ft, R = 0.5 ft
            // Q = (1.486 / 0.013) * 3.1416 * (0.5)^(2/3) * sqrt(0.01) = 114.3077 * 3.1416 * 0.62996 * 0.10 = 22.62 cfs
            // V = 22.62 / 3.1416 = 7.20 fps
            Check(Math.Abs(qFull - 22.62) < 0.10, "Full flow discharge is ~22.62 cfs.");
            Check(Math.Abs(vFull - 7.20) < 0.10, "Full flow velocity is ~7.20 fps.");
            Check(Math.Abs((vFull * (Math.PI * Math.Pow(dFt, 2.0) / 4.0)) - qFull) < 0.01, "Continuity equation Q = V * A holds.");
        }

        private static void P21_PartiallyFullFlow_DepthAndVelocityResolution()
        {
            double dFt = 2.0; // 24-inch pipe
            double slope = 0.01;
            double n = 0.013;
            double partialQ = 10.0; // cfs (about 44% of Qfull)

            var res = StormFlowConduitDesignEngine.SolvePartiallyFullFlow(partialQ, dFt, slope, n);

            Check(res.depthFeet > 0.0 && res.depthFeet < dFt, "Partial depth is between 0 and full diameter.");
            Check(res.depthFeet > 0.8 && res.depthFeet < 1.3, "Partial depth for 44% flow is approximately 1.0 ft.");
            Check(res.velocityFps > 5.0 && res.velocityFps < 8.0, "Partial velocity is reasonable and self-cleansing.");
        }

        private static void P21_CommercialDiameterSelection_SmallestAdequateSize()
        {
            var constraints = new StormConduitConstraint
            {
                MinimumSlope = 0.002,
                MinimumCoverFeet = 3.0,
                ManningN = 0.013,
                MaxCapacityUtilizationRatio = 0.90
            };

            var spec = new StormConduitSpecification
            {
                ConduitId = "PIPE-01",
                StartNodeId = 1,
                StopNodeId = 2,
                LengthFeet = 200.0,
                DesignFlowCfs = 4.0, // cfs
                GroundElevationStartFeet = 105.0,
                GroundElevationStopFeet = 104.0, // 0.5% ground slope
                FixedSlope = 0.005
            };

            var res = StormFlowConduitDesignEngine.SizeConduit(spec, constraints);

            // At S = 0.005:
            // 12": Qfull = 2.50 cfs (too small for 4.0 cfs)
            // 15": Qfull = 4.52 cfs * 0.90 = 4.07 cfs (adequately sized)
            Check(res.SelectedDiameterInches == 15, "Engine selects 15-inch pipe as smallest adequate commercial diameter.");
            Check(res.CapacityRatio <= 0.90, "Capacity ratio satisfies 90% utilization constraint.");
            Check(res.CapacityCompliance == CapacityComplianceStatus.Pass, "Capacity compliance is Pass.");
            Check(res.ActualVelocityFps >= 2.5, "Velocity satisfies self-cleansing limit.");
        }

        private static void P21_SelfCleansingVelocity_MinimumSlopeEnforcement()
        {
            var constraints = new StormConduitConstraint
            {
                MinimumSlope = 0.001,
                MinimumVelocityFps = 2.5, // fps
                ManningN = 0.013
            };

            var spec = new StormConduitSpecification
            {
                ConduitId = "PIPE-FLAT",
                StartNodeId = 2,
                StopNodeId = 3,
                LengthFeet = 100.0,
                DesignFlowCfs = 3.0,
                GroundElevationStartFeet = 100.0,
                GroundElevationStopFeet = 99.95 // Very flat: 0.0005 slope
            };

            var res = StormFlowConduitDesignEngine.SizeConduit(spec, constraints);

            Check(res.DesignSlope >= res.MinimumRequiredSlope, "Design slope meets or exceeds minimum required slope.");
            Check(res.FullFlowVelocityFps >= 2.5, "Full flow velocity meets 2.5 fps self-cleansing threshold.");
            Check(res.VelocityCompliance == VelocityComplianceStatus.Pass, "Velocity compliance is Pass.");
        }

        private static void P21_ExcessiveVelocity_FlaggedAsNonCompliance()
        {
            var constraints = new StormConduitConstraint
            {
                MaximumVelocityFps = 10.0,
                ManningN = 0.013
            };

            var spec = new StormConduitSpecification
            {
                ConduitId = "PIPE-STEEP",
                StartNodeId = 3,
                StopNodeId = 4,
                LengthFeet = 100.0,
                DesignFlowCfs = 8.0,
                GroundElevationStartFeet = 120.0,
                GroundElevationStopFeet = 110.0, // 10% steep slope
                FixedSlope = 0.08
            };

            var res = StormFlowConduitDesignEngine.SizeConduit(spec, constraints);

            Check(res.ActualVelocityFps > 10.0, "Velocity exceeds 10.0 fps on steep slope.");
            Check(res.VelocityCompliance == VelocityComplianceStatus.ExcessiveVelocity, "Excessive velocity is flagged.");
        }

        private static void P21_CrownToCrown_SoffitAlignmentAcrossJunction()
        {
            var constraints = new StormConduitConstraint { MinimumCoverFeet = 3.0 };

            // Upstream pipe: 18-inch (1.5 ft)
            var specUp = new StormConduitSpecification
            {
                ConduitId = "PIPE-UP",
                StartNodeId = 10,
                StopNodeId = 20,
                LengthFeet = 100.0,
                DesignFlowCfs = 6.0,
                GroundElevationStartFeet = 110.0,
                GroundElevationStopFeet = 109.0,
                FixedSlope = 0.01
            };

            var resUp = StormFlowConduitDesignEngine.SizeConduit(specUp, constraints);
            double incomingCrown = resUp.DownstreamCrownFeet;

            // Downstream pipe: 24-inch (2.0 ft) larger diameter
            var specDown = new StormConduitSpecification
            {
                ConduitId = "PIPE-DOWN",
                StartNodeId = 20,
                StopNodeId = 30,
                LengthFeet = 100.0,
                DesignFlowCfs = 14.0, // larger flow forcing 24" pipe
                GroundElevationStartFeet = 109.0,
                GroundElevationStopFeet = 108.0,
                FixedSlope = 0.01
            };

            var resDown = StormFlowConduitDesignEngine.SizeConduit(specDown, constraints, incomingCrown);

            Check(resDown.SelectedDiameterInches > resUp.SelectedDiameterInches, "Downstream pipe is larger than upstream pipe.");
            // Soffit matching: Crown elevations must match exactly
            Check(Math.Abs(resDown.UpstreamCrownFeet - incomingCrown) < 1e-3, "Downstream pipe crown matches upstream pipe crown (soffit alignment).");
            // Invert drop = D_down - D_up
            double expectedDrop = resDown.SelectedDiameterFeet - resUp.SelectedDiameterFeet;
            double actualInvertDrop = resUp.DownstreamInvertFeet - resDown.UpstreamInvertFeet;
            Check(Math.Abs(actualInvertDrop - expectedDrop) < 1e-3, "Physical invert drop equals difference in pipe diameters.");
        }

        private static void P21_MinimumCover_ConstraintEnforcement()
        {
            var constraints = new StormConduitConstraint
            {
                MinimumCoverFeet = 4.0 // 4 ft minimum cover
            };

            var spec = new StormConduitSpecification
            {
                ConduitId = "PIPE-COVER",
                StartNodeId = 1,
                StopNodeId = 2,
                LengthFeet = 150.0,
                DesignFlowCfs = 5.0,
                GroundElevationStartFeet = 100.0,
                GroundElevationStopFeet = 99.0,
                FixedSlope = 0.005
            };

            var res = StormFlowConduitDesignEngine.SizeConduit(spec, constraints);

            Check(res.UpstreamCoverFeet >= 4.0, "Upstream cover meets 4.0 ft constraint.");
            Check(res.DownstreamCoverFeet >= 4.0, "Downstream cover meets 4.0 ft constraint.");
            Check(res.CoverCompliance == CoverComplianceStatus.Pass, "Cover compliance is Pass.");
        }

        /// <summary>
        /// Regression guard for the P21 production default <c>StormConduitConstraint.MinimumCoverFeet = 3.0</c>.
        /// Every other P21 cover test assigns the threshold explicitly, so the compiled default was read by no
        /// test at all and a regression of that default would have survived the entire suite. This test
        /// constructs the constraint with no explicit threshold and proves the default governs the production
        /// invert/cover derivation in <see cref="StormFlowConduitDesignEngine.SizeConduit"/>.
        /// </summary>
        private static void P21_DefaultMinimumCover_ProductionDefaultGovernsCover()
        {
            // No property initializers: the compiled default must be the governing criterion.
            var constraints = new StormConduitConstraint();

            // Ground falls 0.8 ft over 200 ft. At the fixed 0.50% design slope the invert drop is 1.00 ft,
            // so upstream cover lands exactly on the default threshold while downstream cover exceeds it.
            var spec = new StormConduitSpecification
            {
                ConduitId = "PIPE-DEFAULT-COVER",
                StartNodeId = 1,
                StopNodeId = 2,
                LengthFeet = 200.0,
                DesignFlowCfs = 1.0,
                GroundElevationStartFeet = 100.0,
                GroundElevationStopFeet = 99.2,
                FixedSlope = 0.005
            };

            // Fixed inverts are deliberately left unset: that is the only path on which the engine derives
            // both inverts from MinimumCoverFeet rather than accepting a caller-supplied elevation.
            var res = StormFlowConduitDesignEngine.SizeConduit(spec, constraints);

            // 1. The production default value itself.
            Check(Math.Abs(constraints.MinimumCoverFeet - 3.0) <= 1e-9,
                string.Format(CultureInfo.InvariantCulture,
                    "PRODUCTION DEFAULT GUARD: StormConduitConstraint.MinimumCoverFeet must default to 3.0 ft, but was {0:F4} ft. Observed covers: upstream {1:F4} ft, downstream {2:F4} ft.",
                    constraints.MinimumCoverFeet, res.UpstreamCoverFeet, res.DownstreamCoverFeet));

            // 2. The engine must have placed the upstream invert from that default, not from a fixed elevation.
            Check(Math.Abs(res.UpstreamCoverFeet - constraints.MinimumCoverFeet) <= 1e-4,
                string.Format(CultureInfo.InvariantCulture,
                    "DEFAULT COVER BRANCH: the upstream invert must be derived to satisfy the default cover of {0:F4} ft, but upstream cover is {1:F4} ft.",
                    constraints.MinimumCoverFeet, res.UpstreamCoverFeet));

            // 3. Independent upstream and downstream verification against the required 3.0 ft.
            Check(res.UpstreamCoverFeet >= 3.0 - 1e-4,
                string.Format(CultureInfo.InvariantCulture,
                    "DEFAULT COVER: upstream cover {0:F4} ft is below the 3.0 ft production default.", res.UpstreamCoverFeet));
            Check(res.DownstreamCoverFeet >= 3.0 - 1e-4,
                string.Format(CultureInfo.InvariantCulture,
                    "DEFAULT COVER: downstream cover {0:F4} ft is below the 3.0 ft production default.", res.DownstreamCoverFeet));

            // 4. Compliance must be reported Pass under the default.
            Check(res.CoverCompliance == CoverComplianceStatus.Pass,
                string.Format(CultureInfo.InvariantCulture,
                    "DEFAULT COVER COMPLIANCE: expected Pass under the 3.0 ft default, but was {0} (upstream {1:F4} ft, downstream {2:F4} ft).",
                    res.CoverCompliance, res.UpstreamCoverFeet, res.DownstreamCoverFeet));
        }

        /// <summary>
        /// Production-workflow integration for P21. Drives the real end-to-end pipeline (hydrology -> inlets ->
        /// conduit sizing -> hydraulic verification -> audit reporting) through <see cref="StormFlowEndToEndPipeline"/>,
        /// the entry point consumed by the MCP server and the MunicipalFieldPilot workflow. The input never assigns
        /// <c>ConduitConstraints</c>, so the compiled 3.0 ft minimum-cover default governs the head conduit.
        /// The existing P24 pipeline test asserts only the <c>CoverCompliance</c> enum, which the engine satisfies
        /// by construction for any threshold; this test asserts the absolute cover and invert elevations the design
        /// actually produced and verifies they reach the workflow's final report outputs.
        /// </summary>
        private static void P21_WorkflowIntegration_DefaultCoverGovernsEndToEndPipeline()
        {
            var input = BuildNominalVerificationPipelineInput();

            // The workflow input must still be carrying the compiled default: no explicit threshold assigned.
            Check(Math.Abs(input.ConduitConstraints.MinimumCoverFeet - 3.0) <= 1e-9,
                string.Format(CultureInfo.InvariantCulture,
                    "WORKFLOW DEFAULT: StormFlowPipelineInput.ConduitConstraints.MinimumCoverFeet must be the 3.0 ft default, but was {0:F4} ft.",
                    input.ConduitConstraints.MinimumCoverFeet));

            var result = StormFlowEndToEndPipeline.Execute(input);

            Check(result != null && result.ConduitDesignResult != null,
                "WORKFLOW: the end-to-end pipeline must return a conduit design result.");
            Check(result.ConduitDesignResult.ConduitResults.Count == input.ConduitSpecifications.Count,
                "WORKFLOW: every specified conduit must be sized by the pipeline.");

            var head = result.ConduitDesignResult.ConduitResults.First(c => c.ConduitId == "PIPE-1");
            var headSpec = input.ConduitSpecifications.First(s => s.ConduitId == "PIPE-1");
            double diaFt = head.SelectedDiameterInches / 12.0;

            // Selected diameter and resulting geometry.
            Check(head.SelectedDiameterInches >= 12,
                "WORKFLOW: a standard commercial diameter of at least 12 in must be selected.");
            Check(input.ConduitConstraints.AvailableDiametersInches.Contains(head.SelectedDiameterInches),
                "WORKFLOW: the selected diameter must be one of the configured commercial sizes.");

            // Absolute cover produced by the production run, not merely the compliance enum.
            Check(Math.Abs(head.UpstreamCoverFeet - 3.0) <= 1e-4,
                string.Format(CultureInfo.InvariantCulture,
                    "WORKFLOW DEFAULT COVER: the head conduit upstream cover must equal the 3.0 ft default, but was {0:F4} ft.",
                    head.UpstreamCoverFeet));
            Check(head.DownstreamCoverFeet >= 3.0 - 1e-4,
                string.Format(CultureInfo.InvariantCulture,
                    "WORKFLOW DEFAULT COVER: the head conduit downstream cover {0:F4} ft is below the 3.0 ft default.",
                    head.DownstreamCoverFeet));

            // Invert derivation: ground elevation minus default cover minus diameter.
            Check(Math.Abs(head.UpstreamInvertFeet - (headSpec.GroundElevationStartFeet - 3.0 - diaFt)) <= 1e-3,
                "WORKFLOW: the upstream invert must be derived from ground elevation, the default cover and the diameter.");

            // Governing constraints.
            Check(head.CoverCompliance == CoverComplianceStatus.Pass,
                "WORKFLOW: head conduit cover compliance must be Pass under the default.");
            Check(head.DesignSlope >= input.ConduitConstraints.MinimumSlope - 1e-9 &&
                  head.DesignSlope <= input.ConduitConstraints.MaximumSlope + 1e-9,
                "WORKFLOW: the governing design slope must stay within the constraint envelope.");
            Check(head.ActualVelocityFps > 0.0 && head.FullFlowCapacityCfs > 0.0,
                "WORKFLOW: the design must report positive flow capacity and velocity.");

            // Violations must be reported truthfully: one entry per failing compliance check, none suppressed.
            int expectedViolationCount = result.ConduitDesignResult.ConduitResults.Sum(c =>
                (c.VelocityCompliance != VelocityComplianceStatus.Pass ? 1 : 0) +
                (c.CoverCompliance != CoverComplianceStatus.Pass ? 1 : 0) +
                (c.CapacityCompliance != CapacityComplianceStatus.Pass ? 1 : 0));
            Check(result.ConduitDesignResult.Violations.Count == expectedViolationCount,
                string.Format(CultureInfo.InvariantCulture,
                    "WORKFLOW: reported violations ({0}) must equal the failing compliance checks ({1}); no violation may be suppressed.",
                    result.ConduitDesignResult.Violations.Count, expectedViolationCount));

            // Propagation into the workflow's final outputs.
            string csvLine = result.ConduitsCsv
                .Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                .First(line => line.StartsWith("PIPE-1,", StringComparison.Ordinal));
            string[] csvFields = csvLine.Split(',');
            Check(csvFields[4] == head.SelectedDiameterInches.ToString(CultureInfo.InvariantCulture),
                "WORKFLOW: the conduit profile CSV must carry the diameter produced by the design.");
            Check(result.MarkdownReport.Contains("PIPE-1"),
                "WORKFLOW: the executive markdown report must include the designed conduit.");
            Check(result.VerificationResult.ConduitStates.Any(
                    c => c.ConduitId == "PIPE-1" && c.DiameterInches == head.SelectedDiameterInches),
                "WORKFLOW: hydraulic verification must consume the designed diameter.");
        }

        /// <summary>
        /// Regression guard for a false-PASS verdict. The nominal network is given one reach whose fixed design
        /// slope (0.0117) is flatter than its ground fall (4.0 ft over 300 ft). The head conduit sets a crown of
        /// 113.0 ft; PIPE-2 matches that crown, so its 3.51 ft invert drop leaves only 2.51 ft of cover at the
        /// downstream end — a genuine <see cref="CoverComplianceStatus.InsufficientCover"/> produced by the
        /// production engine, with no flooding, freeboard or surcharge condition to explain a failure.
        /// Before the correction the pipeline reported `PASS` with `Success = true` for this input.
        /// </summary>
        private static void P21_PipelineVerdict_InsufficientCoverIsBlocking()
        {
            var input = BuildNominalVerificationPipelineInput();
            input.ConduitSpecifications.First(s => s.ConduitId == "PIPE-2").FixedSlope = 0.0117;

            var result = StormFlowEndToEndPipeline.Execute(input);

            Check(result != null && result.ConduitDesignResult != null,
                "VERDICT: the pipeline must return a conduit design result.");
            Check(Math.Abs(input.ConduitConstraints.MinimumCoverFeet - 3.0) <= 1e-9,
                "VERDICT: the fixture must run on the compiled 3.0 ft cover default.");

            // Fixture preconditions: a real cover violation, and no unrelated hydraulic failure.
            var violating = result.ConduitDesignResult.ConduitResults
                .Where(c => c.CoverCompliance == CoverComplianceStatus.InsufficientCover)
                .ToList();
            Check(violating.Count > 0,
                "FIXTURE: the production engine must report InsufficientCover for at least one conduit.");
            Check(violating.Any(c => c.DownstreamCoverFeet < 3.0 - 1e-4 || c.UpstreamCoverFeet < 3.0 - 1e-4),
                "FIXTURE: the violating conduit must report an absolute cover below 3.0 ft.");
            Check(!result.VerificationResult.HasFlooding,
                "FIXTURE: no surface flooding may be present, so the failure cannot be attributed to flooding.");
            Check(!result.VerificationResult.HasFreeboardViolations,
                "FIXTURE: no freeboard violation may be present, so the failure cannot be attributed to freeboard.");
            Check(!result.VerificationResult.HasSurcharge,
                "FIXTURE: no surcharge may be present, so the failure cannot be attributed to surcharge.");

            // AC-07: the design violation must remain visible in the returned result.
            Check(result.ConduitDesignResult.Violations.Any(v => v.IndexOf("Insufficient cover", StringComparison.OrdinalIgnoreCase) >= 0),
                "VERDICT: the cover violation must remain reported in the design result's violations.");

            // AC-03: a blocking cover violation cannot yield a successful engineering verdict.
            Check(!result.Success,
                string.Format(CultureInfo.InvariantCulture,
                    "VERDICT: Success must be false when a conduit violates the minimum cover constraint, but was true (verdict '{0}').",
                    result.OverallVerdict));
            Check(result.OverallVerdict != "PASS",
                string.Format(CultureInfo.InvariantCulture,
                    "VERDICT: OverallVerdict must not be the bare PASS when minimum cover is violated, but was '{0}'.",
                    result.OverallVerdict));
            Check(result.OverallVerdict.IndexOf("CONDUIT DESIGN VIOLATION", StringComparison.OrdinalIgnoreCase) >= 0,
                string.Format(CultureInfo.InvariantCulture,
                    "VERDICT: the verdict must name the conduit design violation, but was '{0}'.", result.OverallVerdict));

            // The executive report must not contradict the pipeline verdict.
            Check(result.MarkdownReport.IndexOf("CONDUIT DESIGN VIOLATION", StringComparison.OrdinalIgnoreCase) >= 0,
                "VERDICT: the executive markdown report must carry the same blocking verdict as the pipeline.");
        }

        private static void P21_MultiConduitNetwork_TopologicalCascade()
        {
            var constraints = new StormConduitConstraint { MinimumCoverFeet = 3.0 };

            var specs = new List<StormConduitSpecification>
            {
                new StormConduitSpecification
                {
                    ConduitId = "P-1", StartNodeId = 1, StopNodeId = 2, LengthFeet = 100.0,
                    DesignFlowCfs = 2.0, GroundElevationStartFeet = 110.0, GroundElevationStopFeet = 109.0, FixedSlope = 0.005
                },
                new StormConduitSpecification
                {
                    ConduitId = "P-2", StartNodeId = 2, StopNodeId = 3, LengthFeet = 150.0,
                    DesignFlowCfs = 6.0, GroundElevationStartFeet = 109.0, GroundElevationStopFeet = 107.5, FixedSlope = 0.005
                },
                new StormConduitSpecification
                {
                    ConduitId = "P-3", StartNodeId = 3, StopNodeId = 4, LengthFeet = 200.0,
                    DesignFlowCfs = 14.0, GroundElevationStartFeet = 107.5, GroundElevationStopFeet = 105.0, FixedSlope = 0.005
                }
            };

            var networkResult = StormFlowConduitDesignEngine.DesignNetwork(specs, constraints);

            Check(networkResult.ConduitResults.Count == 3, "3 conduits designed.");
            Check(networkResult.TotalSystemLengthFeet == 450.0, "Total system length is 450 ft.");

            var p1 = networkResult.ConduitResults.Single(r => r.ConduitId == "P-1");
            var p2 = networkResult.ConduitResults.Single(r => r.ConduitId == "P-2");
            var p3 = networkResult.ConduitResults.Single(r => r.ConduitId == "P-3");

            // Verify expanding pipe diameter cascade
            Check(p1.SelectedDiameterInches <= p2.SelectedDiameterInches, "Diameter non-decreasing downstream (P1 to P2).");
            Check(p2.SelectedDiameterInches <= p3.SelectedDiameterInches, "Diameter non-decreasing downstream (P2 to P3).");

            // Verify continuous downward invert progression
            Check(p1.UpstreamInvertFeet > p1.DownstreamInvertFeet, "P1 slopes downward.");
            Check(p1.DownstreamInvertFeet >= p2.UpstreamInvertFeet, "P1 to P2 junction drops downward.");
            Check(p2.UpstreamInvertFeet > p2.DownstreamInvertFeet, "P2 slopes downward.");
            Check(p2.DownstreamInvertFeet >= p3.UpstreamInvertFeet, "P2 to P3 junction drops downward.");
            Check(p3.UpstreamInvertFeet > p3.DownstreamInvertFeet, "P3 slopes downward.");
        }

        private static void P21_CapacityUtilization_SurchargedDetection()
        {
            var constraints = new StormConduitConstraint
            {
                MaxCapacityUtilizationRatio = 0.85,
                AvailableDiametersInches = new[] { 12 } // Only 12" pipe allowed
            };

            var spec = new StormConduitSpecification
            {
                ConduitId = "PIPE-OVERLOAD",
                StartNodeId = 1,
                StopNodeId = 2,
                LengthFeet = 100.0,
                DesignFlowCfs = 10.0, // 10 cfs in 12" pipe at 0.5% will surcharge
                GroundElevationStartFeet = 100.0,
                GroundElevationStopFeet = 99.5,
                FixedSlope = 0.005
            };

            var res = StormFlowConduitDesignEngine.SizeConduit(spec, constraints);

            Check(res.CapacityRatio > 0.85, "Capacity ratio exceeds 0.85 limit.");
            Check(res.CapacityCompliance == CapacityComplianceStatus.Surcharged, "Capacity compliance flags Surcharged.");
        }

        private static void P21_FailClosed_InvalidInputsRejected()
        {
            var constraints = new StormConduitConstraint();

            // Negative length
            Throws<ArgumentException>(() =>
            {
                var spec = new StormConduitSpecification { ConduitId = "P", StartNodeId = 1, StopNodeId = 2, LengthFeet = -10.0 };
                spec.Validate();
            }, "Negative length rejected.");

            // Identical start and stop nodes
            Throws<ArgumentException>(() =>
            {
                var spec = new StormConduitSpecification { ConduitId = "P", StartNodeId = 1, StopNodeId = 1, LengthFeet = 100.0 };
                spec.Validate();
            }, "Self-loop conduit rejected.");

            // Negative flow
            Throws<ArgumentException>(() =>
            {
                var spec = new StormConduitSpecification { ConduitId = "P", StartNodeId = 1, StopNodeId = 2, LengthFeet = 100.0, DesignFlowCfs = -5.0 };
                spec.Validate();
            }, "Negative design flow rejected.");

            // Non-finite elevation
            Throws<ArgumentException>(() =>
            {
                var spec = new StormConduitSpecification
                {
                    ConduitId = "P", StartNodeId = 1, StopNodeId = 2, LengthFeet = 100.0, GroundElevationStartFeet = double.NaN
                };
                spec.Validate();
            }, "NaN ground elevation rejected.");

            // Circular network dependency
            Throws<InvalidOperationException>(() =>
            {
                var specs = new[]
                {
                    new StormConduitSpecification { ConduitId = "P1", StartNodeId = 1, StopNodeId = 2, LengthFeet = 100.0, DesignFlowCfs = 1.0 },
                    new StormConduitSpecification { ConduitId = "P2", StartNodeId = 2, StopNodeId = 1, LengthFeet = 100.0, DesignFlowCfs = 1.0 }
                };
                StormFlowConduitDesignEngine.DesignNetwork(specs, constraints);
            }, "Circular conduit topology rejected.");
        }

        internal static void P21_MaterializeAuthoritativeEvidence()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(root) && !File.Exists(Path.Combine(root, "SewerPilot.sln")))
            {
                root = Path.GetDirectoryName(root);
            }
            string outputDir = Path.Combine(root, "docs", "evidence", "p21", "20261019-stormflow-conduit-sizing");
            Directory.CreateDirectory(outputDir);

            // 1. Authoritative Conduit Constraints based on Austin DCM Section 4 criteria
            var constraints = new StormConduitConstraint
            {
                MinimumSlope = 0.0050, // 0.50% min slope per Austin DCM §4.4.1
                MaximumSlope = 0.0800, // 8.00% max slope
                MinimumVelocityFps = 3.0, // 3.0 fps self-cleansing per Austin DCM §4.3.1
                MaximumVelocityFps = 15.0, // 15.0 fps max velocity per Austin DCM §4.3.2
                MinimumCoverFeet = 3.5, // 3.5 ft min cover per Austin DCM §4.5.1
                ManningN = 0.013, // RCP per Austin DCM §4.6.1
                MaxCapacityUtilizationRatio = 0.85, // 85% capacity threshold
                AvailableDiametersInches = new[] { 12, 15, 18, 21, 24, 27, 30, 36, 42, 48, 54, 60, 66, 72 }
            };
            constraints.Validate();

            // 2. Authoritative Storm Network Specifications receiving intercepted flow from P20 inlets
            // Gradients designed to maintain minimum cover (3.5 ft) throughout
            var specs = new List<StormConduitSpecification>
            {
                new StormConduitSpecification
                {
                    ConduitId = "CONDUIT-10",
                    StartNodeId = 10,
                    StopNodeId = 11,
                    LengthFeet = 250.0,
                    DesignFlowCfs = 3.33, // Intercepted flow from INLET-01
                    GroundElevationStartFeet = 110.00,
                    GroundElevationStopFeet = 108.75, // Ground drop = 1.25 ft
                    FixedSlope = 0.0050 // Pipe drop = 250 * 0.005 = 1.25 ft
                },
                new StormConduitSpecification
                {
                    ConduitId = "CONDUIT-11",
                    StartNodeId = 11,
                    StopNodeId = 12,
                    LengthFeet = 300.0,
                    DesignFlowCfs = 6.80, // Cumulative flow (INLET-01 + INLET-02)
                    GroundElevationStartFeet = 108.75,
                    GroundElevationStopFeet = 107.25, // Ground drop = 1.50 ft
                    FixedSlope = 0.0050 // Pipe drop = 300 * 0.005 = 1.50 ft
                },
                new StormConduitSpecification
                {
                    ConduitId = "CONDUIT-12",
                    StartNodeId = 12,
                    StopNodeId = 13,
                    LengthFeet = 350.0,
                    DesignFlowCfs = 12.00, // Total system intercepted flow (including INLET-03)
                    GroundElevationStartFeet = 107.25,
                    GroundElevationStopFeet = 105.50, // Ground drop = 1.75 ft
                    FixedSlope = 0.0050 // Pipe drop = 350 * 0.005 = 1.75 ft
                }
            };

            // 3. Design Network
            var designResult = StormFlowConduitDesignEngine.DesignNetwork(specs, constraints);
            Check(designResult != null, "Network design result returned.");
            Check(designResult.ConduitResults.Count == 3, "All 3 conduits sized.");
            Check(!designResult.HasViolations, "Zero engineering compliance violations.");

            // Verify Conduit 10
            var c10 = designResult.ConduitResults.Single(c => c.ConduitId == "CONDUIT-10");
            Check(c10.SelectedDiameterInches == 15, "Conduit 10 sized to 15 inches.");
            Check(c10.DesignFlowCfs == 3.33, "Conduit 10 carries 3.33 cfs.");
            Check(c10.CapacityRatio <= 0.85, "Conduit 10 capacity ratio <= 0.85.");
            Check(c10.ActualVelocityFps >= 3.0 && c10.ActualVelocityFps <= 15.0, "Conduit 10 velocity compliant.");
            Check(c10.UpstreamCoverFeet >= 3.5 && c10.DownstreamCoverFeet >= 3.5, "Conduit 10 cover compliant.");

            // Verify Conduit 11
            var c11 = designResult.ConduitResults.Single(c => c.ConduitId == "CONDUIT-11");
            Check(c11.SelectedDiameterInches == 21, "Conduit 11 sized to 21 inches.");
            Check(c11.DesignFlowCfs == 6.80, "Conduit 11 carries 6.80 cfs.");
            Check(c11.CapacityRatio <= 0.85, "Conduit 11 capacity ratio <= 0.85.");
            Check(c11.ActualVelocityFps >= 3.0 && c11.ActualVelocityFps <= 15.0, "Conduit 11 velocity compliant.");
            // Soffit alignment at Node 11: c10.DownstreamCrownFeet == c11.UpstreamCrownFeet
            Check(Math.Abs(c10.DownstreamCrownFeet - c11.UpstreamCrownFeet) < 1e-3, "Crown-to-crown (soffit) alignment verified at Node 11.");
            // Drop in invert equals diameter difference: (21 - 15) / 12 = 0.50 ft
            Check(Math.Abs(c10.DownstreamInvertFeet - c11.UpstreamInvertFeet - 0.50) < 1e-3, "Invert drop equals diameter expansion (0.50 ft) at Node 11.");

            // Verify Conduit 12
            var c12 = designResult.ConduitResults.Single(c => c.ConduitId == "CONDUIT-12");
            Check(c12.SelectedDiameterInches == 24, "Conduit 12 sized to 24 inches.");
            Check(c12.DesignFlowCfs == 12.00, "Conduit 12 carries 12.00 cfs.");
            Check(c12.CapacityRatio <= 0.85, "Conduit 12 capacity ratio <= 0.85.");
            Check(c12.ActualVelocityFps >= 3.0 && c12.ActualVelocityFps <= 15.0, "Conduit 12 velocity compliant.");
            // Soffit alignment at Node 12: c11.DownstreamCrownFeet == c12.UpstreamCrownFeet
            Check(Math.Abs(c11.DownstreamCrownFeet - c12.UpstreamCrownFeet) < 1e-3, "Crown-to-crown (soffit) alignment verified at Node 12.");
            // Drop in invert equals diameter difference: (24 - 21) / 12 = 0.25 ft
            Check(Math.Abs(c11.DownstreamInvertFeet - c12.UpstreamInvertFeet - 0.25) < 1e-3, "Invert drop equals diameter expansion (0.25 ft) at Node 12.");

            // Monotonic expansion: 15" -> 21" -> 24"
            Check(c10.SelectedDiameterInches < c11.SelectedDiameterInches && c11.SelectedDiameterInches < c12.SelectedDiameterInches,
                "Conduit diameters expand monotonically downstream.");

            // Total system length: 250 + 300 + 350 = 900 ft
            Check(Math.Abs(designResult.TotalSystemLengthFeet - 900.0) < 1e-4, "Total system length is 900 ft.");

            // 4. Materialize Authoritative Artifacts
            var serializer = RuntimeHostProtocol.Serializer();
            File.WriteAllText(Path.Combine(outputDir, "authorized-conduit-sizing-results.json"), serializer.Serialize(designResult.ConduitResults), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDir, "authorized-conduit-network-profile-results.json"), serializer.Serialize(designResult), Encoding.UTF8);

            string auditJson = string.Format(CultureInfo.InvariantCulture,
                "{{\n  \"TimestampUtc\": \"{0:o}\",\n  \"Phase\": \"P21\",\n  \"Milestone\": \"StormFlow Conduit Sizing & Invert Design\",\n  \"GoverningStandard\": \"City of Austin Drainage Criteria Manual (DCM) Section 4\",\n  \"EvaluatedConduits\": {1},\n  \"TotalSystemLengthFeet\": {2:F2},\n  \"TotalDesignDischargeCfs\": {3:F2},\n  \"Conduit10_DiameterInches\": {4},\n  \"Conduit10_DesignFlowCfs\": {5:F2},\n  \"Conduit10_CapacityRatio\": {6:F2},\n  \"Conduit10_ActualVelocityFps\": {7:F2},\n  \"Conduit10_UpstreamInvertFeet\": {8:F2},\n  \"Conduit10_DownstreamInvertFeet\": {9:F2},\n  \"Conduit11_DiameterInches\": {10},\n  \"Conduit11_DesignFlowCfs\": {11:F2},\n  \"Conduit11_CapacityRatio\": {12:F2},\n  \"Conduit11_ActualVelocityFps\": {13:F2},\n  \"Conduit11_UpstreamInvertFeet\": {14:F2},\n  \"Conduit11_DownstreamInvertFeet\": {15:F2},\n  \"Conduit12_DiameterInches\": {16},\n  \"Conduit12_DesignFlowCfs\": {17:F2},\n  \"Conduit12_CapacityRatio\": {18:F2},\n  \"Conduit12_ActualVelocityFps\": {19:F2},\n  \"Conduit12_UpstreamInvertFeet\": {20:F2},\n  \"Conduit12_DownstreamInvertFeet\": {21:F2},\n  \"SoffitAlignmentPreserved\": true,\n  \"ZeroEngineeringViolations\": true\n}}\n",
                DateTime.UtcNow,
                designResult.ConduitResults.Count,
                designResult.TotalSystemLengthFeet,
                designResult.TotalDesignDischargeCfs,
                c10.SelectedDiameterInches, c10.DesignFlowCfs, c10.CapacityRatio, c10.ActualVelocityFps, c10.UpstreamInvertFeet, c10.DownstreamInvertFeet,
                c11.SelectedDiameterInches, c11.DesignFlowCfs, c11.CapacityRatio, c11.ActualVelocityFps, c11.UpstreamInvertFeet, c11.DownstreamInvertFeet,
                c12.SelectedDiameterInches, c12.DesignFlowCfs, c12.CapacityRatio, c12.ActualVelocityFps, c12.UpstreamInvertFeet, c12.DownstreamInvertFeet);
            File.WriteAllText(Path.Combine(outputDir, "conduit-design-audit.json"), auditJson, Encoding.UTF8);
        }
    }
}
