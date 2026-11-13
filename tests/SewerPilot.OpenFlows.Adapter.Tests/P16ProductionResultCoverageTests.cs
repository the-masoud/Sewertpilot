using System;
using System.Collections.Generic;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static void P16ProductionCoverage_ExactMultiElementCoverageGuard()
        {
            // Case 1: Exact coverage of two conduits and three structures: accepted.
            var readResult = new NetworkReadResult
            {
                Phase = "P12-A",
                Status = "OK",
                Conduits = new List<ConduitReadDto>
                {
                    new ConduitReadDto { Id = 101, Label = "CO-1", StartNodeId = 1, StopNodeId = 2 },
                    new ConduitReadDto { Id = 102, Label = "CO-2", StartNodeId = 2, StopNodeId = 3 }
                },
                Structures = new List<StructureReadDto>
                {
                    new StructureReadDto { Id = 1, Label = "MH-1", RuntimeCategory = "Manhole", ElementTypeId = 1 },
                    new StructureReadDto { Id = 2, Label = "MH-2", RuntimeCategory = "Manhole", ElementTypeId = 1 },
                    new StructureReadDto { Id = 3, Label = "OF-1", RuntimeCategory = "Outfall", ElementTypeId = 2 }
                }
            };
            var conduitIds = new List<int> { 101, 102 };

            var observations = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation
                {
                    ElementId = 101,
                    ElementKind = "Conduit",
                    Metric = HydraulicMetricKind.Flow,
                    NumericValue = 15.5,
                    ScenarioId = 10,
                    PhysicalAlternativeId = 20,
                    CalculationContext = "Coverage Test",
                    TimeSeconds = 0.0
                },
                new HydraulicMetricObservation
                {
                    ElementId = 102,
                    ElementKind = "Conduit",
                    Metric = HydraulicMetricKind.Flow,
                    NumericValue = 12.3,
                    ScenarioId = 10,
                    PhysicalAlternativeId = 20,
                    CalculationContext = "Coverage Test",
                    TimeSeconds = 0.0
                }
            };

            var context = new ResultCompositionContext
            {
                ProjectId = "Proj-Coverage",
                ModelFingerprint = "FP-Coverage-123",
                ScenarioId = 10,
                PhysicalAlternativeId = 20,
                CalculationRunId = "Run-Coverage-001",
                CalculationContext = "Coverage Test",
                ResultTimeSeconds = 0.0,
                IsTestOnly = true
            };

            var resultSet = NetworkResultComposer.Compose(context, readResult, observations);
            Check(resultSet != null, "Composed result set is not null.");
            Check(resultSet.Elements.Count == 5, "Composed result set contains exactly 5 elements for 2 conduits and 3 structures.");
            Check(conduitIds.Count == 2, "Requested conduits count is 2.");

            // Under the previous count-only check (resultSet.Elements.Count == conduitIds.Count), this would fail (5 != 2).
            // Under exact element-identity coverage validation, this must pass:
            GravityWriteAndSolverHost.ValidateResultCoverage(readResult, conduitIds, resultSet);
            Check(true, "ValidateResultCoverage accepts exact multi-element coverage.");

            // Case 2: Missing structure: rejected.
            var missingStructResult = new HydraulicResultSet
            {
                ProjectId = resultSet.ProjectId,
                ModelFingerprint = resultSet.ModelFingerprint,
                ScenarioId = resultSet.ScenarioId,
                PhysicalAlternativeId = resultSet.PhysicalAlternativeId,
                Elements = resultSet.Elements.Where(e => e.ElementId != 3).Select(e => e.Clone()).ToList()
            };
            bool missingStructRejected = false;
            try
            {
                GravityWriteAndSolverHost.ValidateResultCoverage(readResult, conduitIds, missingStructResult);
            }
            catch (Exception ex)
            {
                missingStructRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED");
            }
            Check(missingStructRejected, "ValidateResultCoverage rejects result set missing structure 3.");

            // Case 3: Unexpected additional element: rejected.
            var extraElemResult = new HydraulicResultSet
            {
                ProjectId = resultSet.ProjectId,
                ModelFingerprint = resultSet.ModelFingerprint,
                ScenarioId = resultSet.ScenarioId,
                PhysicalAlternativeId = resultSet.PhysicalAlternativeId,
                Elements = resultSet.Elements.Select(e => e.Clone()).ToList()
            };
            extraElemResult.Elements.Add(new HydraulicElementResult
            {
                ElementId = 999,
                ElementKind = "Conduit"
            });
            bool extraElemRejected = false;
            try
            {
                GravityWriteAndSolverHost.ValidateResultCoverage(readResult, conduitIds, extraElemResult);
            }
            catch (Exception ex)
            {
                extraElemRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED");
            }
            Check(extraElemRejected, "ValidateResultCoverage rejects unexpected additional element.");

            // Case 4: Duplicate or invalid element identities: rejected.
            // 4a. Duplicate element ID in composed result set
            var duplicateActualResult = new HydraulicResultSet
            {
                ProjectId = resultSet.ProjectId,
                ModelFingerprint = resultSet.ModelFingerprint,
                ScenarioId = resultSet.ScenarioId,
                PhysicalAlternativeId = resultSet.PhysicalAlternativeId,
                Elements = resultSet.Elements.Select(e => e.Clone()).ToList()
            };
            duplicateActualResult.Elements.Add(duplicateActualResult.Elements[0].Clone());
            bool duplicateActualRejected = false;
            try
            {
                GravityWriteAndSolverHost.ValidateResultCoverage(readResult, conduitIds, duplicateActualResult);
            }
            catch (Exception ex)
            {
                duplicateActualRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED");
            }
            Check(duplicateActualRejected, "ValidateResultCoverage rejects duplicate element ID in composed result set.");

            // 4b. Duplicate ID in expected network elements (conduit and structure sharing ID 101)
            var dupExpectedRead = new NetworkReadResult
            {
                Phase = "P12-A",
                Status = "OK",
                Conduits = new List<ConduitReadDto>
                {
                    new ConduitReadDto { Id = 101, Label = "CO-1" },
                    new ConduitReadDto { Id = 102, Label = "CO-2" }
                },
                Structures = new List<StructureReadDto>
                {
                    new StructureReadDto { Id = 101, Label = "MH-Dup" },
                    new StructureReadDto { Id = 2, Label = "MH-2" },
                    new StructureReadDto { Id = 3, Label = "OF-1" }
                }
            };
            bool dupExpectedRejected = false;
            try
            {
                GravityWriteAndSolverHost.ValidateResultCoverage(dupExpectedRead, conduitIds, resultSet);
            }
            catch (Exception ex)
            {
                dupExpectedRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED");
            }
            Check(dupExpectedRejected, "ValidateResultCoverage rejects duplicate element ID in expected network read.");

            // 4c. Invalid non-positive conduit ID in expected network elements
            var invalidConduitRead = new NetworkReadResult
            {
                Phase = "P12-A",
                Status = "OK",
                Conduits = new List<ConduitReadDto>
                {
                    new ConduitReadDto { Id = 0, Label = "CO-Invalid" },
                    new ConduitReadDto { Id = 102, Label = "CO-2" }
                },
                Structures = readResult.Structures
            };
            bool invalidConduitRejected = false;
            try
            {
                GravityWriteAndSolverHost.ValidateResultCoverage(invalidConduitRead, conduitIds, resultSet);
            }
            catch (Exception ex)
            {
                invalidConduitRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED");
            }
            Check(invalidConduitRejected, "ValidateResultCoverage rejects non-positive conduit ID in expected network read.");

            // 4d. Invalid non-positive structure ID in expected network elements
            var invalidStructureRead = new NetworkReadResult
            {
                Phase = "P12-A",
                Status = "OK",
                Conduits = readResult.Conduits,
                Structures = new List<StructureReadDto>
                {
                    new StructureReadDto { Id = -5, Label = "MH-Invalid" },
                    new StructureReadDto { Id = 2, Label = "MH-2" },
                    new StructureReadDto { Id = 3, Label = "OF-1" }
                }
            };
            bool invalidStructureRejected = false;
            try
            {
                GravityWriteAndSolverHost.ValidateResultCoverage(invalidStructureRead, conduitIds, resultSet);
            }
            catch (Exception ex)
            {
                invalidStructureRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED");
            }
            Check(invalidStructureRejected, "ValidateResultCoverage rejects non-positive structure ID in expected network read.");

            // 4e. Invalid non-positive element ID in composed result set
            var invalidActualResult = new HydraulicResultSet
            {
                ProjectId = resultSet.ProjectId,
                ModelFingerprint = resultSet.ModelFingerprint,
                ScenarioId = resultSet.ScenarioId,
                PhysicalAlternativeId = resultSet.PhysicalAlternativeId,
                Elements = new List<HydraulicElementResult>
                {
                    new HydraulicElementResult { ElementId = -1, ElementKind = "Conduit" },
                    new HydraulicElementResult { ElementId = 102, ElementKind = "Conduit" },
                    new HydraulicElementResult { ElementId = 1, ElementKind = "Manhole" },
                    new HydraulicElementResult { ElementId = 2, ElementKind = "Manhole" },
                    new HydraulicElementResult { ElementId = 3, ElementKind = "Outfall" }
                }
            };
            bool invalidActualRejected = false;
            try
            {
                GravityWriteAndSolverHost.ValidateResultCoverage(readResult, conduitIds, invalidActualResult);
            }
            catch (Exception ex)
            {
                invalidActualRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED");
            }
            Check(invalidActualRejected, "ValidateResultCoverage rejects non-positive element ID in composed result set.");

            // 4f. Missing requested conduit from actual results
            var missingConduitResult = new HydraulicResultSet
            {
                ProjectId = resultSet.ProjectId,
                ModelFingerprint = resultSet.ModelFingerprint,
                ScenarioId = resultSet.ScenarioId,
                PhysicalAlternativeId = resultSet.PhysicalAlternativeId,
                Elements = new List<HydraulicElementResult>
                {
                    new HydraulicElementResult { ElementId = 101, ElementKind = "Conduit" },
                    new HydraulicElementResult { ElementId = 4, ElementKind = "Manhole" },
                    new HydraulicElementResult { ElementId = 1, ElementKind = "Manhole" },
                    new HydraulicElementResult { ElementId = 2, ElementKind = "Manhole" },
                    new HydraulicElementResult { ElementId = 3, ElementKind = "Outfall" }
                }
            };
            bool missingConduitRejected = false;
            try
            {
                GravityWriteAndSolverHost.ValidateResultCoverage(readResult, conduitIds, missingConduitResult);
            }
            catch (Exception ex)
            {
                missingConduitRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED");
            }
            Check(missingConduitRejected, "ValidateResultCoverage rejects composed result set missing a requested conduit.");

            // 4g. Non-positive or duplicate ID in requested conduit IDs
            bool invalidReqConduitRejected = false;
            try
            {
                GravityWriteAndSolverHost.ValidateResultCoverage(readResult, new[] { 101, -2 }, resultSet);
            }
            catch (Exception ex)
            {
                invalidReqConduitRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED");
            }
            Check(invalidReqConduitRejected, "ValidateResultCoverage rejects non-positive requested conduit ID.");

            bool dupReqConduitRejected = false;
            try
            {
                GravityWriteAndSolverHost.ValidateResultCoverage(readResult, new[] { 101, 101 }, resultSet);
            }
            catch (Exception ex)
            {
                dupReqConduitRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED");
            }
            Check(dupReqConduitRejected, "ValidateResultCoverage rejects duplicate requested conduit ID.");

            // 4h. Null inputs fail-closed
            bool nullReadRejected = false;
            try { GravityWriteAndSolverHost.ValidateResultCoverage(null, conduitIds, resultSet); }
            catch (Exception ex) { nullReadRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED"); }
            Check(nullReadRejected, "ValidateResultCoverage rejects null readResult.");

            bool nullConduitsListRejected = false;
            try { GravityWriteAndSolverHost.ValidateResultCoverage(new NetworkReadResult { Conduits = null, Structures = readResult.Structures }, conduitIds, resultSet); }
            catch (Exception ex) { nullConduitsListRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED"); }
            Check(nullConduitsListRejected, "ValidateResultCoverage rejects null readResult.Conduits.");

            bool nullStructuresListRejected = false;
            try { GravityWriteAndSolverHost.ValidateResultCoverage(new NetworkReadResult { Conduits = readResult.Conduits, Structures = null }, conduitIds, resultSet); }
            catch (Exception ex) { nullStructuresListRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED"); }
            Check(nullStructuresListRejected, "ValidateResultCoverage rejects null readResult.Structures.");

            bool nullReqConduitsRejected = false;
            try { GravityWriteAndSolverHost.ValidateResultCoverage(readResult, null, resultSet); }
            catch (Exception ex) { nullReqConduitsRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED"); }
            Check(nullReqConduitsRejected, "ValidateResultCoverage rejects null requestedConduitIds.");

            bool nullResultSetRejected = false;
            try { GravityWriteAndSolverHost.ValidateResultCoverage(readResult, conduitIds, null); }
            catch (Exception ex) { nullResultSetRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED"); }
            Check(nullResultSetRejected, "ValidateResultCoverage rejects null resultSet.");

            bool nullElementsListRejected = false;
            try { GravityWriteAndSolverHost.ValidateResultCoverage(readResult, conduitIds, new HydraulicResultSet { Elements = null }); }
            catch (Exception ex) { nullElementsListRejected = ex.Message.Contains("RESULT_COMPOSITION_FAILED"); }
            Check(nullElementsListRejected, "ValidateResultCoverage rejects null resultSet.Elements.");
        }
    }
}
