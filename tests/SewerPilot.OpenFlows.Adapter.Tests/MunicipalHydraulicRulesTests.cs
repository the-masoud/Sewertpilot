using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter.HydraulicRules;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        public static void P24_Rules_MinimumSelfCleansingVelocity_NominalAndViolations()
        {
            var rule = new MinimumSelfCleansingVelocityRule(2.50);
            Check(rule.RuleId == "MUNICIPAL-RULE-VMIN", "RuleId is MUNICIPAL-RULE-VMIN");
            Check(rule.MinimumVelocityFps == 2.50, "Default threshold is 2.50 ft/s");

            // 1. Compliant case
            var ctxCompliant = new HydraulicRuleEvaluationContext
            {
                ElementId = 101,
                ElementLabel = "C-101",
                ElementKind = "Conduit",
                ActualVelocityFps = 3.25
            };
            var res1 = rule.Evaluate(ctxCompliant);
            Check(res1.Status == RuleEvaluationStatus.Compliant, "Velocity 3.25 fps is Compliant.");
            Check(res1.ObservedValue == 3.25, "ObservedValue is 3.25");
            Check(res1.RequiredThreshold == 2.50, "RequiredThreshold is 2.50");

            // 2. Exact threshold boundary (>= passes)
            var ctxBoundary = new HydraulicRuleEvaluationContext
            {
                ElementId = 102,
                ElementLabel = "C-102",
                ActualVelocityFps = 2.50
            };
            var res2 = rule.Evaluate(ctxBoundary);
            Check(res2.Status == RuleEvaluationStatus.Compliant, "Velocity 2.50 fps is Compliant at boundary.");

            // 3. Violation case
            var ctxViolation = new HydraulicRuleEvaluationContext
            {
                ElementId = 103,
                ElementLabel = "C-103",
                ActualVelocityFps = 1.85
            };
            var res3 = rule.Evaluate(ctxViolation);
            Check(res3.Status == RuleEvaluationStatus.Violation, "Velocity 1.85 fps is Violation (< 2.50).");
            Check(res3.FindingsMessage.Contains("deposition risk"), "Findings message explains deposition risk.");

            // 4. Derived velocity from design flow and diameter (V = Q / A)
            // Q = 3.5 cfs, D = 1.0 ft => Area = PI * 0.25 = 0.785398 => V = 3.5 / 0.785398 = 4.4563 fps
            var ctxDerived = new HydraulicRuleEvaluationContext
            {
                ElementId = 104,
                ElementLabel = "C-104",
                ActualVelocityFps = null, // Missing direct velocity
                DesignDischargeCfs = 3.50,
                DiameterFeet = 1.00
            };
            var res4 = rule.Evaluate(ctxDerived);
            // F07: a velocity derived from an assumed full-flow wetted area cannot prove compliance.
            Check(res4.Status == RuleEvaluationStatus.RequiresEngineeringDisposition,
                "Derived velocity is indeterminate (requires engineering disposition), never Compliant.");
            Check(res4.FindingsMessage.Contains("assumed full-flow wetted area"), "Findings disclose the assumed wetted area.");
            Check(res4.ObservedValue.HasValue && Math.Abs(res4.ObservedValue.Value - 4.4563) < 0.01, "Derived velocity computed accurately.");
            Check(res4.MathematicalDerivation.Contains("derived from Q and D"), "Derivation notes physical origin.");

            // 5. Incomplete data case (missing velocity and insufficient geometric flow data)
            var ctxIncomplete = new HydraulicRuleEvaluationContext
            {
                ElementId = 105,
                ElementLabel = "C-105",
                ActualVelocityFps = null,
                DesignDischargeCfs = null
            };
            var res5 = rule.Evaluate(ctxIncomplete);
            Check(res5.Status == RuleEvaluationStatus.IncompleteData, "Missing velocity yields IncompleteData, never silent pass.");

            // 6. Non-finite (NaN) velocity fails closed as IncompleteData
            var ctxNan = new HydraulicRuleEvaluationContext
            {
                ElementId = 106,
                ElementLabel = "C-106",
                ActualVelocityFps = double.NaN
            };
            var res6 = rule.Evaluate(ctxNan);
            Check(res6.Status == RuleEvaluationStatus.IncompleteData, "NaN velocity yields IncompleteData.");
        }

        public static void P24_Rules_MaximumErosiveVelocity_NominalAndViolations()
        {
            var rule = new MaximumErosiveVelocityLimitRule(10.00);
            Check(rule.RuleId == "MUNICIPAL-RULE-VMAX", "RuleId is MUNICIPAL-RULE-VMAX");
            Check(rule.MaximumVelocityFps == 10.00, "Maximum threshold is 10.00 ft/s");

            // 1. Compliant case
            var ctxCompliant = new HydraulicRuleEvaluationContext
            {
                ElementId = 201,
                ElementLabel = "C-201",
                ActualVelocityFps = 6.40
            };
            var res1 = rule.Evaluate(ctxCompliant);
            Check(res1.Status == RuleEvaluationStatus.Compliant, "Velocity 6.40 fps is Compliant.");

            // 2. Exact threshold boundary (<= passes)
            var ctxBoundary = new HydraulicRuleEvaluationContext
            {
                ElementId = 202,
                ElementLabel = "C-202",
                ActualVelocityFps = 10.00
            };
            var res2 = rule.Evaluate(ctxBoundary);
            Check(res2.Status == RuleEvaluationStatus.Compliant, "Velocity 10.00 fps is Compliant at boundary.");

            // 3. Violation case (erosive velocity)
            var ctxViolation = new HydraulicRuleEvaluationContext
            {
                ElementId = 203,
                ElementLabel = "C-203",
                ActualVelocityFps = 14.80
            };
            var res3 = rule.Evaluate(ctxViolation);
            Check(res3.Status == RuleEvaluationStatus.Violation, "Velocity 14.80 fps is Violation (> 10.00).");
            Check(res3.FindingsMessage.Contains("scour/abrasion risk"), "Findings message explains scour risk.");

            // 4. Derived velocity exceeding threshold
            // Q = 15.0 cfs, D = 1.0 ft => Area = 0.7854 => V = 19.10 fps > 10.00
            var ctxDerivedViol = new HydraulicRuleEvaluationContext
            {
                ElementId = 204,
                ElementLabel = "C-204",
                DesignDischargeCfs = 15.00,
                DiameterFeet = 1.00
            };
            var res4 = rule.Evaluate(ctxDerivedViol);
            Check(res4.Status == RuleEvaluationStatus.Violation, "Derived high velocity yields Violation.");

            // 5. Incomplete data (missing velocity)
            var ctxIncomplete = new HydraulicRuleEvaluationContext
            {
                ElementId = 205,
                ElementLabel = "C-205",
                ActualVelocityFps = null
            };
            var res5 = rule.Evaluate(ctxIncomplete);
            Check(res5.Status == RuleEvaluationStatus.IncompleteData, "Missing velocity yields IncompleteData.");
        }

        public static void P24_Rules_LongitudinalSlope_NominalAndManningSelfCleansing()
        {
            var rule = new PipeSlopeConstraintRule(
                nominalMinSlope: 0.0050,
                nominalMaxSlope: 0.1000,
                designCleansingVelocityFps: 2.50,
                fallbackManningN: 0.013);

            Check(rule.RuleId == "MUNICIPAL-RULE-SLOPE", "RuleId is MUNICIPAL-RULE-SLOPE");

            // 1. Nominal slope satisfied: S = 0.0080 ft/ft (0.80%) on 12" pipe
            // D = 1.0 ft, R = 0.25 ft => S_clean = [(0.013*2.5)/(1.486*(0.25)^(2/3))]^2 = 0.003037 ft/ft
            // Required min = max(0.0050, 0.003037) = 0.0050 ft/ft
            var ctxCompliant = new HydraulicRuleEvaluationContext
            {
                ElementId = 301,
                ElementLabel = "C-301",
                DiameterFeet = 1.00,
                LongitudinalSlope = 0.0080,
                ManningN = 0.013
            };
            var res1 = rule.Evaluate(ctxCompliant);
            Check(res1.Status == RuleEvaluationStatus.Compliant, "Slope 0.0080 is Compliant.");
            Check(res1.RequiredThreshold == 0.0050, "Required threshold is nominal min 0.0050.");

            // 2. Manning self-cleansing slope governs: small 8" pipe (D = 0.6667 ft)
            // R = 0.16667 ft => R^(2/3) = 0.30285 => S_clean = [(0.013*2.5)/(1.486*0.30285)]^2 = 0.005216 ft/ft > 0.0050
            // Test slope S = 0.0051 ft/ft (which satisfies nominal 0.0050, but violates Manning self-cleansing 0.005216!)
            var ctxManningViol = new HydraulicRuleEvaluationContext
            {
                ElementId = 302,
                ElementLabel = "C-302",
                DiameterFeet = 8.0 / 12.0,
                LongitudinalSlope = 0.0051,
                ManningN = 0.013
            };
            var res2 = rule.Evaluate(ctxManningViol);
            Check(res2.Status == RuleEvaluationStatus.Violation, "Slope 0.0051 violates Manning self-cleansing slope 0.005216.");
            Check(res2.RequiredThreshold.HasValue && res2.RequiredThreshold.Value > 0.0052, "Required threshold reflected Manning self-cleansing.");

            // 3. Adverse uphill slope (S < 0)
            var ctxAdverse = new HydraulicRuleEvaluationContext
            {
                ElementId = 303,
                ElementLabel = "C-303",
                DiameterFeet = 1.00,
                LongitudinalSlope = -0.0020
            };
            var res3 = rule.Evaluate(ctxAdverse);
            Check(res3.Status == RuleEvaluationStatus.Violation, "Adverse slope is Violation.");
            Check(res3.FindingsMessage.Contains("Adverse pipe slope"), "Adverse slope message reported.");

            // 4. Excessive steep slope (S > 0.10)
            var ctxExcessive = new HydraulicRuleEvaluationContext
            {
                ElementId = 304,
                ElementLabel = "C-304",
                DiameterFeet = 1.00,
                LongitudinalSlope = 0.1450
            };
            var res4 = rule.Evaluate(ctxExcessive);
            Check(res4.Status == RuleEvaluationStatus.Violation, "Slope 0.1450 exceeds maximum allowable 0.1000.");
            Check(res4.FindingsMessage.Contains("exceeds maximum allowable slope"), "Steep drop violation reported.");

            // 5. Invert-derived slope: StartInvert = 100.0, StopInvert = 98.0, Length = 250.0 => S = 2.0 / 250.0 = 0.0080
            var ctxDerived = new HydraulicRuleEvaluationContext
            {
                ElementId = 305,
                ElementLabel = "C-305",
                DiameterFeet = 1.00,
                StartInvertFeet = 100.00,
                StopInvertFeet = 98.00,
                LengthFeet = 250.00
            };
            var res5 = rule.Evaluate(ctxDerived);
            Check(res5.Status == RuleEvaluationStatus.Compliant, "Invert-derived slope 0.0080 is Compliant.");
            Check(res5.ObservedValue == 0.0080, "Derived slope equals 0.0080.");

            // 6. Incomplete data: missing slope and missing invert geometry
            var ctxIncomplete = new HydraulicRuleEvaluationContext
            {
                ElementId = 306,
                ElementLabel = "C-306",
                DiameterFeet = 1.00
            };
            var res6 = rule.Evaluate(ctxIncomplete);
            Check(res6.Status == RuleEvaluationStatus.IncompleteData, "Missing slope geometry yields IncompleteData.");
        }

        /// <summary>
        /// P17 production slope regression guard.
        ///
        /// Why this test exists: every pre-existing slope test constructs PipeSlopeConstraintRule with an
        /// explicit nominalMinSlope argument (MunicipalHydraulicRulesTests.cs, P24 slope test). That bypasses
        /// DefaultNominalMinSlope entirely, so mutating the production default from 0.0050 to 0.0010 left the
        /// whole suite green — a surviving mutant. This test deliberately constructs the rule through
        /// MunicipalHydraulicRuleEngine's parameterless constructor, which registers
        /// "new PipeSlopeConstraintRule()" with no explicit thresholds. The governing minimum slope is
        /// therefore the production default constant, and changing it changes this test's outcome.
        ///
        /// Diameter choice is load-bearing. The rule computes
        ///     effectiveMinSlope = max(NominalMinimumSlope, S_clean)
        /// where S_clean = [(n*Vmin)/(1.486*(D/4)^(2/3))]^2. S_clean crosses 0.0050 at D = 0.6881 ft, so for
        /// conduits smaller than ~8.3 in the Manning term dominates and the nominal default is unobservable.
        /// A 12 in (D = 1.00 ft) conduit gives S_clean = 0.003037 ft/ft, so the nominal default governs and a
        /// 0.0040 slope must be a Violation. Under the 0.0010 mutation the governing minimum collapses to
        /// 0.003037 and the same conduit becomes Compliant — the exact geometry class of P17 conduit 29.
        /// </summary>
        public static void P17_ProductionSlopeRule_DefaultConfigurationDetectsSlope0040Violation()
        {
            // Production registration path: no explicit thresholds anywhere in this construction.
            var engine = new MunicipalHydraulicRuleEngine();
            var slopeRules = engine.ConduitRules.OfType<PipeSlopeConstraintRule>().ToList();

            Check(slopeRules.Count == 1, "Default engine registers exactly one PipeSlopeConstraintRule.");
            var slopeRule = slopeRules[0];

            // --- Case 1: slope 0.0040 on a 12 in conduit must be a Violation under the default minimum. ---
            var violationCtx = new HydraulicRuleEvaluationContext
            {
                ElementId = 2901,
                ElementLabel = "P17-C29-ANALOGUE",
                ElementKind = "Conduit",
                DiameterFeet = 1.00,
                LongitudinalSlope = 0.0040,
                ManningN = 0.013
            };

            var violationResults = engine.EvaluateConduit(violationCtx);
            var slopeViolation = violationResults.FirstOrDefault(r => r.RuleId == slopeRule.RuleId);

            Check(slopeViolation != null, "Slope rule produced a result for the 12 in / 0.0040 conduit.");
            Check(slopeViolation.Status == RuleEvaluationStatus.Violation,
                "PRODUCTION DEFAULT GUARD: slope 0.0040 ft/ft on a 12 in conduit is a Violation under the default minimum slope. " +
                "If this fails, the governing default is no longer 0.0050 and the municipal design requirement has changed " +
                "without an approved engineering change. Observed status: " + slopeViolation.Status);
            Check(slopeViolation.ObservedValue.HasValue && Math.Abs(slopeViolation.ObservedValue.Value - 0.0040) < 1e-9,
                "Observed slope is reported as 0.0040 ft/ft.");
            Check(slopeViolation.RequiredThreshold.HasValue && Math.Abs(slopeViolation.RequiredThreshold.Value - 0.0050) < 1e-9,
                "The threshold the rule actually applied is 0.0050 ft/ft (reported by the rule, not a test constant). " +
                "Reported: " + (slopeViolation.RequiredThreshold.HasValue
                    ? slopeViolation.RequiredThreshold.Value.ToString("F6", CultureInfo.InvariantCulture) : "<null>"));
            Check(slopeViolation.FindingsMessage.Contains("below required minimum"),
                "Finding states the slope is below the required minimum.");

            // --- Case 2: slope 0.0060 on the same conduit is Compliant (positive control). ---
            // This prevents a vacuous test that would pass if the rule always returned Violation.
            var compliantCtx = new HydraulicRuleEvaluationContext
            {
                ElementId = 2902,
                ElementLabel = "P17-C29-COMPLIANT-CONTROL",
                ElementKind = "Conduit",
                DiameterFeet = 1.00,
                LongitudinalSlope = 0.0060,
                ManningN = 0.013
            };

            var compliantResults = engine.EvaluateConduit(compliantCtx);
            var slopeCompliant = compliantResults.FirstOrDefault(r => r.RuleId == slopeRule.RuleId);

            Check(slopeCompliant != null, "Slope rule produced a result for the 12 in / 0.0060 conduit.");
            Check(slopeCompliant.Status == RuleEvaluationStatus.Compliant,
                "Slope 0.0060 ft/ft on a 12 in conduit is Compliant above the default minimum.");
            Check(slopeCompliant.RequiredThreshold.HasValue && Math.Abs(slopeCompliant.RequiredThreshold.Value - 0.0050) < 1e-9,
                "Compliant case also reports the applied threshold as 0.0050 ft/ft.");

            // --- Case 3: exact boundary is inclusive (>=), so 0.0050 passes. ---
            var boundaryCtx = new HydraulicRuleEvaluationContext
            {
                ElementId = 2903,
                ElementLabel = "P17-BOUNDARY",
                ElementKind = "Conduit",
                DiameterFeet = 1.00,
                LongitudinalSlope = 0.0050,
                ManningN = 0.013
            };

            var boundaryResults = engine.EvaluateConduit(boundaryCtx);
            var slopeBoundary = boundaryResults.FirstOrDefault(r => r.RuleId == slopeRule.RuleId);

            Check(slopeBoundary != null && slopeBoundary.Status == RuleEvaluationStatus.Compliant,
                "Slope exactly equal to the default minimum is Compliant (inclusive >= boundary).");
        }

        public static void P24_Rules_CrownAlignmentAndSectionDrop_JunctionEvaluations()
        {
            var rule = new CrownAlignmentSectionDropRule();
            Check(rule.RuleId == "MUNICIPAL-RULE-CROWN-DROP", "RuleId is MUNICIPAL-RULE-CROWN-DROP");

            // 1. Equal crowns: Upstream = 105.00 ft, Downstream = 105.00 ft => drop = 0.00 ft
            var ctxEqual = new HydraulicRuleEvaluationContext
            {
                JunctionNodeId = 10,
                JunctionLabel = "MH-10",
                UpstreamExitCrownFeet = 105.00,
                DownstreamEntranceCrownFeet = 105.00
            };
            var res1 = rule.Evaluate(ctxEqual);
            Check(res1.Status == RuleEvaluationStatus.Compliant, "Equal crowns are Compliant (0.00 ft drop).");
            Check(res1.ObservedValue == 0.0, "Observed drop is 0.00 ft.");

            // 2. Positive section drop: Upstream = 105.00 ft, Downstream = 104.60 ft => drop = +0.40 ft
            var ctxPositive = new HydraulicRuleEvaluationContext
            {
                JunctionNodeId = 11,
                JunctionLabel = "MH-11",
                UpstreamExitCrownFeet = 105.00,
                DownstreamEntranceCrownFeet = 104.60
            };
            var res2 = rule.Evaluate(ctxPositive);
            Check(res2.Status == RuleEvaluationStatus.Compliant, "Positive section drop +0.40 ft is Compliant.");
            Check(res2.ObservedValue == 0.40, "Observed drop is 0.40 ft.");

            // 3. Adverse crown step-up violation: Upstream = 105.00 ft, Downstream = 105.35 ft => drop = -0.35 ft
            // Downstream crown is higher than upstream crown!
            var ctxViolation = new HydraulicRuleEvaluationContext
            {
                JunctionNodeId = 12,
                JunctionLabel = "MH-12",
                UpstreamExitCrownFeet = 105.00,
                DownstreamEntranceCrownFeet = 105.35
            };
            var res3 = rule.Evaluate(ctxViolation);
            Check(res3.Status == RuleEvaluationStatus.Violation, "Downstream crown step-up is a Violation.");
            Check(res3.ObservedValue == -0.35, "Observed drop is -0.35 ft.");
            Check(res3.FindingsMessage.Contains("Crown alignment violation"), "Adverse crown violation reported.");

            // 4. Invert + Diameter derived crown alignment:
            // Upstream: Invert = 100.00 ft, Dia = 2.0 ft => Crown = 102.00 ft
            // Downstream: Invert = 98.50 ft, Dia = 3.0 ft => Crown = 101.50 ft
            // Drop = 102.00 - 101.50 = +0.50 ft => Compliant
            var ctxDerived = new HydraulicRuleEvaluationContext
            {
                JunctionNodeId = 13,
                JunctionLabel = "MH-13",
                UpstreamExitInvertFeet = 100.00,
                UpstreamDiameterFeet = 2.00,
                DownstreamEntranceInvertFeet = 98.50,
                DownstreamDiameterFeet = 3.00
            };
            var res4 = rule.Evaluate(ctxDerived);
            Check(res4.Status == RuleEvaluationStatus.Compliant, "Invert+Dia derived crowns are Compliant.");
            Check(res4.ObservedValue == 0.50, "Derived drop is 0.50 ft.");

            // 5. Incomplete data (missing crown elevations)
            var ctxIncomplete = new HydraulicRuleEvaluationContext
            {
                JunctionNodeId = 14,
                JunctionLabel = "MH-14",
                UpstreamExitCrownFeet = 105.00,
                DownstreamEntranceCrownFeet = null
            };
            var res5 = rule.Evaluate(ctxIncomplete);
            Check(res5.Status == RuleEvaluationStatus.IncompleteData, "Missing crown data yields IncompleteData.");
        }

        public static void P24_Rules_MunicipalHydraulicRuleEngine_FullNetworkVerification()
        {
            var engine = new MunicipalHydraulicRuleEngine();
            Check(engine.ConduitRules.Count == 3, "Engine registers 3 default conduit rules.");
            Check(engine.JunctionRules.Count == 1, "Engine registers 1 default junction rule.");

            // Build benchmark 3-conduit, 2-junction municipal network
            // MH-1 -> C-1 -> MH-2 -> C-2 -> MH-3 -> C-3 -> OF-4
            var geometry = new NetworkGeometryInfo();
            geometry.Nodes.Add(new NodeGeometryInfo { NodeId = 1, Label = "MH-1", NodeKind = "Manhole", GroundElevation = 110.0, RimElevation = 110.0, InvertElevation = 104.0 });
            geometry.Nodes.Add(new NodeGeometryInfo { NodeId = 2, Label = "MH-2", NodeKind = "Manhole", GroundElevation = 107.0, RimElevation = 107.0, InvertElevation = 101.0 });
            geometry.Nodes.Add(new NodeGeometryInfo { NodeId = 3, Label = "MH-3", NodeKind = "Manhole", GroundElevation = 104.0, RimElevation = 104.0, InvertElevation = 98.0 });
            geometry.Nodes.Add(new NodeGeometryInfo { NodeId = 4, Label = "OF-4", NodeKind = "Outfall", GroundElevation = 100.0, RimElevation = 100.0, InvertElevation = 95.0 });

            // C-1: 1 -> 2, L = 200, D = 1.5 ft (18"), StartInv = 104.0, StopInv = 102.0 => S = 0.010, US Crown = 105.5, DS Crown = 103.5
            // Velocity = 4.2 fps (Compliant)
            geometry.Conduits.Add(new ConduitGeometryInfo
            {
                ConduitId = 101,
                Label = "C-1",
                StartNodeId = 1,
                StopNodeId = 2,
                Length = 200.0,
                Diameter = 1.50,
                StartInvert = 104.0,
                StopInvert = 102.0,
                ManningN = 0.013
            });

            // C-2: 2 -> 3, L = 200, D = 2.0 ft (24"), StartInv = 101.0, StopInv = 99.0 => S = 0.010, US Crown = 103.0, DS Crown = 101.0
            // Note: At MH-2, InConduit C-1 Stop Crown is 102.0 + 1.5 = 103.5; OutConduit C-2 Start Crown is 101.0 + 2.0 = 103.0 => drop = +0.5 ft (Compliant)
            // Velocity = 1.5 fps (Violation: < 2.5 fps)
            geometry.Conduits.Add(new ConduitGeometryInfo
            {
                ConduitId = 102,
                Label = "C-2",
                StartNodeId = 2,
                StopNodeId = 3,
                Length = 200.0,
                Diameter = 2.00,
                StartInvert = 101.0,
                StopInvert = 99.0,
                ManningN = 0.013
            });

            // C-3: 3 -> 4, L = 200, D = 2.5 ft (30"), StartInv = 99.0, StopInv = 95.0 => S = 0.020
            // Note: At MH-3, InConduit C-2 Stop Crown is 99.0 + 2.0 = 101.0; OutConduit C-3 Start Crown is 99.0 + 2.5 = 101.5 => drop = -0.5 ft (Violation: Crown Step-Up!)
            // Velocity = 12.0 fps (Violation: > 10.0 fps)
            geometry.Conduits.Add(new ConduitGeometryInfo
            {
                ConduitId = 103,
                Label = "C-3",
                StartNodeId = 3,
                StopNodeId = 4,
                Length = 200.0,
                Diameter = 2.50,
                StartInvert = 99.0,
                StopInvert = 95.0,
                ManningN = 0.013
            });

            // Build hydraulic results
            var results = new HydraulicResultSet
            {
                ProjectId = "PRJ-P24-BENCHMARK",
                NetworkFingerprint = "NET-P24-001"
            };

            // C-1 results: V = 4.2 fps, S = 0.010, Q = 7.42 cfs
            var r1 = new HydraulicElementResult { ElementId = 101, ElementKind = "Conduit" };
            r1.Metrics.Add(new HydraulicMetricResult { Metric = HydraulicMetricKind.Velocity, NumericValue = 4.2, Availability = HydraulicResultAvailability.Available, Unit = "ft/s" });
            r1.Metrics.Add(new HydraulicMetricResult { Metric = HydraulicMetricKind.Flow, NumericValue = 7.42, Availability = HydraulicResultAvailability.Available, Unit = "ft3/s" });
            r1.Metrics.Add(new HydraulicMetricResult { Metric = HydraulicMetricKind.Slope, NumericValue = 0.010, Availability = HydraulicResultAvailability.Available, Unit = "dimensionless" });
            results.Elements.Add(r1);

            // C-2 results: V = 1.5 fps (< 2.5 Vmin violation!), S = 0.010, Q = 4.71 cfs
            var r2 = new HydraulicElementResult { ElementId = 102, ElementKind = "Conduit" };
            r2.Metrics.Add(new HydraulicMetricResult { Metric = HydraulicMetricKind.Velocity, NumericValue = 1.5, Availability = HydraulicResultAvailability.Available, Unit = "ft/s" });
            r2.Metrics.Add(new HydraulicMetricResult { Metric = HydraulicMetricKind.Flow, NumericValue = 4.71, Availability = HydraulicResultAvailability.Available, Unit = "ft3/s" });
            r2.Metrics.Add(new HydraulicMetricResult { Metric = HydraulicMetricKind.Slope, NumericValue = 0.010, Availability = HydraulicResultAvailability.Available, Unit = "dimensionless" });
            results.Elements.Add(r2);

            // C-3 results: V = 12.0 fps (> 10.0 Vmax violation!), S = 0.020, Q = 58.9 cfs
            var r3 = new HydraulicElementResult { ElementId = 103, ElementKind = "Conduit" };
            r3.Metrics.Add(new HydraulicMetricResult { Metric = HydraulicMetricKind.Velocity, NumericValue = 12.0, Availability = HydraulicResultAvailability.Available, Unit = "ft/s" });
            r3.Metrics.Add(new HydraulicMetricResult { Metric = HydraulicMetricKind.Flow, NumericValue = 58.9, Availability = HydraulicResultAvailability.Available, Unit = "ft3/s" });
            r3.Metrics.Add(new HydraulicMetricResult { Metric = HydraulicMetricKind.Slope, NumericValue = 0.020, Availability = HydraulicResultAvailability.Available, Unit = "dimensionless" });
            results.Elements.Add(r3);

            // Execute engine
            var report = engine.EvaluateNetwork(results, geometry);
            Check(report != null, "Report generated.");
            Check(report.Results.Count == 11, "11 total rule checks evaluated (3 conduits * 3 rules + 2 junctions * 1 rule = 11).");

            // Check counts:
            // C-1: Vmin pass, Vmax pass, Slope pass (3 Compliant)
            // C-2: Vmin VIOLATION, Vmax pass, Slope pass (2 Compliant, 1 Violation)
            // C-3: Vmin pass, Vmax VIOLATION, Slope pass (2 Compliant, 1 Violation)
            // MH-2: Crown drop +0.5 ft pass (1 Compliant)
            // MH-3: Crown drop -0.5 ft VIOLATION (1 Violation)
            // Total: 8 Compliant, 3 Violations, 0 IncompleteData.
            Check(report.CompliantCount == 8, "Exactly 8 Compliant rule evaluations (was: " + report.CompliantCount + ")");
            Check(report.ViolationCount == 3, "Exactly 3 Violations detected (was: " + report.ViolationCount + ")");
            Check(report.IncompleteDataCount == 0, "Zero IncompleteData findings.");

            // Verify fingerprint reproducibility
            string fp1 = report.ReportFingerprint;
            Check(!string.IsNullOrWhiteSpace(fp1), "Report fingerprint computed.");
            var report2 = engine.EvaluateNetwork(results, geometry);
            Check(report2.ReportFingerprint == fp1, "Report fingerprint is 100% deterministic and reproducible.");

            // Verify integration with NetworkResultComposer
            var composerReport = NetworkResultComposer.EvaluateMunicipalRules(results, geometry, engine);
            Check(composerReport.ReportFingerprint == fp1, "NetworkResultComposer produces identical evaluation report.");

            // Verify integration with GravityHydraulicVerificationEngine
            var verificationEngineReport = GravityHydraulicVerificationEngine.VerifyMunicipalRules(results, geometry, engine);
            Check(verificationEngineReport.ReportFingerprint == fp1, "GravityHydraulicVerificationEngine produces identical report.");
        }

        public static void P24_Rules_FailClosed_NullOrMalformedInputHandling()
        {
            var ruleVmin = new MinimumSelfCleansingVelocityRule();
            var ruleVmax = new MaximumErosiveVelocityLimitRule();
            var ruleSlope = new PipeSlopeConstraintRule();
            var ruleCrown = new CrownAlignmentSectionDropRule();
            var engine = new MunicipalHydraulicRuleEngine();

            // Null contexts must throw ArgumentNullException
            Throws<ArgumentNullException>(() => ruleVmin.Evaluate(null));
            Throws<ArgumentNullException>(() => ruleVmax.Evaluate(null));
            Throws<ArgumentNullException>(() => ruleSlope.Evaluate(null));
            Throws<ArgumentNullException>(() => ruleCrown.Evaluate(null));
            Throws<ArgumentNullException>(() => engine.EvaluateConduit(null));
            Throws<ArgumentNullException>(() => engine.EvaluateJunction(null));
            Throws<ArgumentNullException>(() => engine.EvaluateNetwork(null, new NetworkGeometryInfo()));
            Throws<ArgumentNullException>(() => engine.EvaluateNetwork(new HydraulicResultSet(), null));

            // Malformed thresholds must throw ArgumentException
            Throws<ArgumentException>(() => new MinimumSelfCleansingVelocityRule(-1.0));
            Throws<ArgumentException>(() => new MinimumSelfCleansingVelocityRule(0.0));
            Throws<ArgumentException>(() => new MinimumSelfCleansingVelocityRule(double.NaN));
            Throws<ArgumentException>(() => new MaximumErosiveVelocityLimitRule(-5.0));
            Throws<ArgumentException>(() => new MaximumErosiveVelocityLimitRule(double.NaN));
            Throws<ArgumentException>(() => new PipeSlopeConstraintRule(nominalMinSlope: -0.01));
            Throws<ArgumentException>(() => new PipeSlopeConstraintRule(nominalMinSlope: 0.05, nominalMaxSlope: 0.02)); // max < min
        }

        public static void P24_Rules_Integration_PreservesP17BaselineAndRemediationViolations()
        {
            // Verify that the P17 baseline results from the remediation phase remain qualified
            // with exactly 43 Pass and 20 Violations (zero regressions).
            string repo = null;
            var cur = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (cur != null)
            {
                if (File.Exists(Path.Combine(cur.FullName, "SEWERPILOT_MASTER_ROADMAP_HANDOFF.md")))
                {
                    repo = cur.FullName;
                    break;
                }
                cur = cur.Parent;
            }
            Check(repo != null, "Repository root found.");
            string resultsPath = Path.Combine(repo, "docs", "evidence", "remediation-20261021", "p17", "hydraulic-results.json");
            Check(File.Exists(resultsPath), "P17 hydraulic results artifact exists at " + resultsPath);

            string json = File.ReadAllText(resultsPath);
            var results = RuntimeHostProtocol.Serializer().Deserialize<HydraulicResultSet>(json);
            Check(results != null, "HydraulicResultSet deserialized from authoritative P17 artifact.");
            Check(results.Elements.Count == 19, "Exactly 19 elements in P17 result set (9 conduits, 10 structures).");

            // Build standard P17 criteria
            var criteria = new List<HydraulicVerificationCriterion>
            {
                new HydraulicVerificationCriterion { CriterionKey = "hydraulic.min_velocity", Metric = HydraulicMetricKind.Velocity, Operator = HydraulicCriterionOperator.Minimum, Threshold = 2.0, Unit = "ft/s" },
                new HydraulicVerificationCriterion { CriterionKey = "hydraulic.max_velocity", Metric = HydraulicMetricKind.Velocity, Operator = HydraulicCriterionOperator.Maximum, Threshold = 15.0, Unit = "ft/s" },
                new HydraulicVerificationCriterion { CriterionKey = "hydraulic.min_slope", Metric = HydraulicMetricKind.Slope, Operator = HydraulicCriterionOperator.Minimum, Threshold = 0.005, Unit = "dimensionless" },
                new HydraulicVerificationCriterion { CriterionKey = "hydraulic.max_slope", Metric = HydraulicMetricKind.Slope, Operator = HydraulicCriterionOperator.Maximum, Threshold = 0.100, Unit = "dimensionless" },
                new HydraulicVerificationCriterion { CriterionKey = "hydraulic.min_cover", Metric = HydraulicMetricKind.Cover, Operator = HydraulicCriterionOperator.Minimum, Threshold = 3.0, Unit = "ft" },
                new HydraulicVerificationCriterion { CriterionKey = "hydraulic.max_cover", Metric = HydraulicMetricKind.Cover, Operator = HydraulicCriterionOperator.Maximum, Threshold = 15.0, Unit = "ft" },
                new HydraulicVerificationCriterion { CriterionKey = "hydraulic.full_flow_capacity", Metric = HydraulicMetricKind.Capacity, Operator = HydraulicCriterionOperator.Minimum, Threshold = 10.0, Unit = "ft3/s" }
            };

            var p17Report = GravityHydraulicVerificationEngine.Verify(results, criteria);
            Check(p17Report != null, "P17 verification report evaluated.");

            // Strict regression invariants:
            // 43 Pass, 20 Violations (11 cover/slope + 9 velocity in dry conduits), 0 NotAvailable, 0 InvalidInput
            Check(p17Report.PassCount == 43, "PassCount is exactly 43 (got " + p17Report.PassCount + ").");
            Check(p17Report.ViolationCount == 20, "ViolationCount is exactly 20 (got " + p17Report.ViolationCount + ").");
            Check(p17Report.NotAvailableCount == 0, "NotAvailableCount is strictly 0 (got " + p17Report.NotAvailableCount + ").");
            Check(p17Report.InvalidInputCount == 0, "InvalidInputCount is strictly 0 (got " + p17Report.InvalidInputCount + ").");
        }
    }
}
