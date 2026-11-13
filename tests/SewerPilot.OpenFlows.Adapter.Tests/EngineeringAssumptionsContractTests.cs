using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SewerPilot.OpenFlows.Adapter;
using SewerPilot.OpenFlows.Adapter.HydraulicRules;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        /// <summary>
        /// Probe 1 (F07 — Flow State Falsification):
        /// Pass a conduit with a valid diameter and flow rate, but with an uncalculated/unresolved flow state
        /// (no explicit wetted area/hydraulic radius from solver).
        /// Assertion: Assert that compliance status evaluates to RuleEvaluationStatus.RequiresEngineeringDisposition.
        /// Anti-Cheat: Explicitly assert status != RuleEvaluationStatus.Compliant and assert derivation text contains "RequiresEngineeringDisposition".
        /// </summary>
        private static void F07_UnprovenFlowStateRequiresEngineeringDisposition()
        {
            var rule = new MinimumSelfCleansingVelocityRule();
            var context = new HydraulicRuleEvaluationContext
            {
                ElementId = 101,
                ElementLabel = "C-101",
                ElementKind = "Conduit",
                DiameterFeet = 1.0,           // 12-inch pipe
                DesignDischargeCfs = 3.0,     // Flow that gives V = 3 / (pi * 0.25) ~ 3.82 ft/s (> 2.5 ft/s)
                ActualVelocityFps = null      // Uncalculated / unverified velocity from solver
            };

            var result = rule.Evaluate(context);

            // Assertion:
            Check(result.Status == RuleEvaluationStatus.RequiresEngineeringDisposition,
                "Status must evaluate to RequiresEngineeringDisposition; was " + result.Status);

            // Anti-Cheat:
            Check(result.Status != RuleEvaluationStatus.Compliant,
                "Anti-Cheat: Status must NOT be Compliant.");
            string text = (result.FindingsMessage ?? "") + " " + (result.MathematicalDerivation ?? "");
            Check(text.Contains("RequiresEngineeringDisposition") || result.Status.ToString().Contains("RequiresEngineeringDisposition"),
                "Anti-Cheat: Findings message or derivation must reference 'RequiresEngineeringDisposition'.");
        }

        /// <summary>
        /// Probe 2 (F08 — Infinite Diameter Falsification):
        /// Pass a conduit evaluation request with Diameter = double.PositiveInfinity (and another with double.NaN).
        /// Assertion: Assert that ArgumentOutOfRangeException (or IncompleteDataException) is thrown.
        /// Anti-Cheat: Assert that velocity is NOT evaluated as 0.0 and compliance is NOT marked Compliant.
        /// </summary>
        private static void F08_NonfiniteOrNonpositiveDiameterThrowsArgumentOutOfRange()
        {
            var rule = new MaximumErosiveVelocityLimitRule();

            // Probe 2a: Diameter = double.PositiveInfinity
            bool infThrown = false;
            RuleEvaluationResult infResult = null;
            try
            {
                var contextInf = new HydraulicRuleEvaluationContext
                {
                    ElementId = 201,
                    ElementLabel = "C-INF",
                    ElementKind = "Conduit",
                    DiameterFeet = double.PositiveInfinity,
                    DesignDischargeCfs = 5.0,
                    ActualVelocityFps = null
                };
                infResult = rule.Evaluate(contextInf);
            }
            catch (ArgumentOutOfRangeException)
            {
                infThrown = true;
            }
            catch (IncompleteDataException)
            {
                infThrown = true;
            }

            Check(infThrown, "Probe 2: Diameter = PositiveInfinity must throw ArgumentOutOfRangeException or IncompleteDataException.");
            if (infResult != null)
            {
                Check(infResult.ObservedValue != 0.0, "Anti-Cheat: Velocity must NOT evaluate to 0.0.");
                Check(infResult.Status != RuleEvaluationStatus.Compliant, "Anti-Cheat: Compliance must NOT be marked Compliant.");
            }

            // Probe 2b: Diameter = double.NaN
            bool nanThrown = false;
            RuleEvaluationResult nanResult = null;
            try
            {
                var contextNan = new HydraulicRuleEvaluationContext
                {
                    ElementId = 202,
                    ElementLabel = "C-NAN",
                    ElementKind = "Conduit",
                    DiameterFeet = double.NaN,
                    DesignDischargeCfs = 5.0,
                    ActualVelocityFps = null
                };
                nanResult = rule.Evaluate(contextNan);
            }
            catch (ArgumentOutOfRangeException)
            {
                nanThrown = true;
            }
            catch (IncompleteDataException)
            {
                nanThrown = true;
            }

            Check(nanThrown, "Probe 2: Diameter = NaN must throw ArgumentOutOfRangeException or IncompleteDataException.");
            if (nanResult != null)
            {
                Check(nanResult.Status != RuleEvaluationStatus.Compliant, "Anti-Cheat: Compliance must NOT be marked Compliant.");
            }
        }

        /// <summary>
        /// Probe 3 (F09 — Civil 3D Synthetic Attributes Falsification):
        /// Import a Civil 3D pipe definition that lacks catalog part mapping / material metadata.
        /// Assertion: Assert that the resulting link/pipe DTO is tagged with catalog status "UNRESOLVED_CATALOG_PART".
        /// Anti-Cheat: Explicitly assert that pipe.Material != "Concrete" and pipe.ManningsN != 0.013.
        /// </summary>
        private static void F09_Civil3dUnresolvedCatalogPartDoesNotSynthesizeAttributes()
        {
            var dto = new LiveNetworkDto();
            dto.Pipes.Add(new LivePipeDto
            {
                Name = "P-UNMAPPED",
                StartX = 100.0,
                StartY = 100.0,
                StartZ = 50.0,
                EndX = 200.0,
                EndY = 100.0,
                EndZ = 49.0,
                InnerDiameterOrWidth = 1.5,
                Length2D = 100.0
            });

            var method = typeof(Civil3dLiveWorkflow).GetMethod("MapToSewerPilotModels", BindingFlags.NonPublic | BindingFlags.Static);
            var tuple = (ValueTuple<NetworkReadResult, GravityNetworkSnapshot>)method.Invoke(null, new object[] { dto, new Civil3dLiveProcessInfo { Pid = 1234 } });
            var readResult = tuple.Item1;

            var pipe = readResult.Conduits.First(c => c.Label == "P-UNMAPPED");

            // Assertion:
            string catStatus = null;
            if (pipe.PropertyClassifications != null)
            {
                pipe.PropertyClassifications.TryGetValue("CatalogPart", out catStatus);
            }
            Check(catStatus == "UNRESOLVED_CATALOG_PART",
                "Pipe must be tagged with CatalogPart = 'UNRESOLVED_CATALOG_PART'; was '" + catStatus + "'.");

            // Anti-Cheat:
            Check(pipe.Material != "Concrete",
                "Anti-Cheat: pipe.Material must NOT default to 'Concrete'.");
            Check(!pipe.ManningsN.HasValue || pipe.ManningsN.Value != 0.013,
                "Anti-Cheat: pipe.ManningsN must NOT default to 0.013.");
        }

        /// <summary>
        /// Probe 4 (N10 — Missing Flow Unit Falsification):
        /// Execute WaterObjectsResultReader.ReadFlow against a mock/fixture result where WorkingUnitSymbol is null, "", or whitespace.
        /// Assertion: Assert that IncompleteDataException (with code "MISSING_FLOW_UNIT_METADATA") is thrown.
        /// Anti-Cheat: Explicitly assert that the reader does NOT substitute "ft3/s", "cfs", or default to imperial units without metadata.
        /// </summary>
        private static void N10_MissingFlowUnitMetadataThrowsIncompleteDataException()
        {
            var reader = new WaterObjectsResultReader();
            var rf = new MockResultFieldProbe
            {
                Name = "BasicFlowResults_Flow",
                HasResultsReturnValue = true,
                ValueReturnValue = 2.5
            };
            var cm = new MockConduitManagerProbe
            {
                ExistsReturnValue = true,
                SupportedFields = new List<MockFieldProbe> { new MockFieldProbe { Name = "BasicFlowResults_Flow" } },
                ResultFieldReturnValue = rf
            };
            var rm = new MockResultManagerProbe
            {
                IsActive = true,
                HasResults = true,
                TimeStepsReturnValue = new[] { 0.0, 180.0 }
            };
            var sc = new MockScenarioProbe { ResultManagerReturnValue = rm };
            var sm = new MockScenarioManagerProbe { ScenarioReturnValue = sc };
            var ds = new MockDataSetProbe { ScenarioManager = sm, ConduitManager = cm };

            bool unitThrown = false;
            IncompleteDataException caughtEx = null;
            WaterObjectsFlowResult flowResult = null;
            try
            {
                flowResult = reader.ReadFlow(ds, null, preferredElementId: 42);
            }
            catch (IncompleteDataException ex)
            {
                unitThrown = true;
                caughtEx = ex;
            }

            // Assertion:
            Check(unitThrown && caughtEx != null,
                "ReadFlow must throw IncompleteDataException when working unit metadata is missing.");
            Check(caughtEx.ErrorCode == "MISSING_FLOW_UNIT_METADATA",
                "ErrorCode must be 'MISSING_FLOW_UNIT_METADATA'; was '" + (caughtEx != null ? caughtEx.ErrorCode : "null") + "'.");

            // Anti-Cheat:
            if (flowResult != null)
            {
                Check(flowResult.WorkingUnitSymbol != "ft3/s", "Anti-Cheat: Reader must not substitute 'ft3/s'.");
                Check(flowResult.WorkingUnitSymbol != "cfs", "Anti-Cheat: Reader must not substitute 'cfs'.");
            }

            // Anti-Cheat: Element 27 must ALSO fail closed if metadata is missing (Zero backdoor tolerance)
            bool element27Thrown = false;
            try
            {
                reader.ReadFlow(ds, null, preferredElementId: 27);
            }
            catch (IncompleteDataException ex) when (ex.ErrorCode == "MISSING_FLOW_UNIT_METADATA")
            {
                element27Thrown = true;
            }
            Check(element27Thrown, "Anti-Cheat: Element 27 must fail closed when unit metadata is missing; no hardcoded backdoor allowed.");
        }
    }
}
