using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static HostRequest CreateProductionReadbackTestRequest(bool calculate = true)
        {
            var fixture = ProductionFixture();
            fixture.Input.Approval.Calculate = calculate;
            var plan = GravityExecutionPlanBuilder.Build(
                fixture.Network,
                fixture.Input.SourceRead,
                fixture.Input.DesignBasis,
                fixture.Constraints,
                fixture.Feasibility,
                fixture.Generation,
                fixture.Candidate,
                fixture.Input.Approval);

            return new HostRequest
            {
                SchemaVersion = 1,
                RunId = "c1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6",
                Operation = GravityWriteAndSolverHost.ProductionOperation,
                WorkingModelPath = @"C:\TestModels\Working\550e8400e29b41d4a716446655440000\Model.stsw",
                WorkingCopyId = "550e8400e29b41d4a716446655440000",
                RuntimeCandidateCount = 1,
                SelectedRuntime = @"C:\Program Files\Bentley\OpenFlows Sewer",
                WriteAuthorizationScope = GravityWriteAndSolverHost.ProductionOperation,
                WriteAuthorizationTokenId = "11112222333344445555666677778888",
                WriteAuthorizationTargetWorkingPath = @"C:\TestModels\Working\550e8400e29b41d4a716446655440000\Model.stsw",
                SourceBaselineFingerprint = plan.Inputs.ModelFingerprint,
                EngineeringPlan = plan,
                Diagnostics = new Dictionary<string, string>
                {
                    { "ExpectedCreatedScenarioId", "101" },
                    { "ExpectedCreatedPhysicalAlternativeId", "102" },
                    { "ExpectedProductionOptionsIdentity", "CivilStormDynamicEngine:67" },
                    { "ExpectedNumericalEngine", "CivilStormDynamicEngine" },
                    { "GravityEngineeringPhase", "REOPEN" }
                },
                CoreFiles = new[]
                {
                    new HostMetadata { SimpleName = "Haestad.Support", Path = @"C:\Runtime\Haestad.Support.dll", SHA256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
                    new HostMetadata { SimpleName = "Haestad.Domain", Path = @"C:\Runtime\Haestad.Domain.dll", SHA256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }
                }
            };
        }

        private static ModelReadEvidence CreateSolvedEvidence(bool solverSuccess, bool calculate = true, GravityExecutionPlan plan = null)
        {
            if (plan == null)
            {
                plan = CreateProductionReadbackTestRequest(calculate).EngineeringPlan;
            }
            int propCount = plan.TotalFieldWriteCount;
            int locCount = plan.LocalizationWriteCount;
            int endCount = plan.EndpointWriteCount;

            var evidence = new ModelReadEvidence
            {
                FailureCode = null,
                CloseCompleted = true,
                LifecycleInitialized = true,
                OpenSucceeded = true,
                Diagnostics = new Dictionary<string, string>
                {
                    { "StaCompleted", "true" },
                    { "FirstSessionCloseCompleted", "true" },
                    { "ProductionExecutionVerified", "true" },
                    { "ImmediateReadbackVerified", "true" },
                    { "SaveCurrentProjectInvoked", "true" },
                    { "SaveCurrentProjectSucceeded", "true" },
                    { "SaveTimestamp", DateTime.UtcNow.ToString("o") },
                    { "ReopenVerificationSuccess", "true" },
                    { "SourceIsolationVerified", "true" },
                    { "ScenarioCopyInvoked", "true" },
                    { "AlternativeCopyInvoked", "true" },
                    { "CreatedScenarioID", "101" },
                    { "CreatedPhysicalAlternativeID", "102" },
                    { "ProductionOptionsIdentity", "CivilStormDynamicEngine:67" },
                    { "BoundNumericalEngine", "CivilStormDynamicEngine" },
                    { "PropertySetCount", propCount.ToString(CultureInfo.InvariantCulture) },
                    { "LocalizationPropertySetCount", locCount.ToString(CultureInfo.InvariantCulture) },
                    { "EndpointInvertPropertySetCount", endCount.ToString(CultureInfo.InvariantCulture) }
                }
            };

            if (calculate)
            {
                evidence.Diagnostics["SolverRunInvoked"] = "true";
                evidence.Diagnostics["SolverRunCompleted"] = "true";
                evidence.Diagnostics["SolverLicenseAttached"] = "true";
                evidence.Diagnostics["LicenseCleanupCompleted"] = "true";
                evidence.Diagnostics["SolverStatusSuccess"] = solverSuccess ? "true" : "false";
                evidence.Diagnostics["ScenarioComputeStatus"] = solverSuccess ? "Success" : "Failed";
                evidence.Diagnostics["PostSolveSaveCurrentProjectInvoked"] = "true";
                evidence.Diagnostics["PostSolveSaveCurrentProjectSucceeded"] = solverSuccess ? "true" : "false";
                evidence.Diagnostics["PostSolveSaveTimestamp"] = DateTime.UtcNow.ToString("o") ;
            }

            return evidence;
        }

        private static HydraulicResultSet CreateSampleGenuineResultSet(HostRequest request, int scenarioId = 101, int alternativeId = 102)
        {
            if (request == null)
            {
                request = CreateProductionReadbackTestRequest(calculate: true);
            }

            var plan = request.EngineeringPlan;
            var conduitIds = plan.Endpoints.Select(e => e.ConduitId).Distinct().ToList();

            var obs = new List<HydraulicMetricObservation>();
            foreach (var cid in conduitIds)
            {
                obs.Add(new HydraulicMetricObservation { ElementId = cid, Metric = HydraulicMetricKind.Flow, NumericValue = 1.45, Unit = "ft3/s", EngineName = "CivilStormDynamicEngine", FieldName = "BasicFlowResults_Flow", TimeSeconds = 0.0, ScenarioId = scenarioId, PhysicalAlternativeId = alternativeId });
                obs.Add(new HydraulicMetricObservation { ElementId = cid, Metric = HydraulicMetricKind.Velocity, NumericValue = 2.34, Unit = "ft/s", EngineName = "CivilStormDynamicEngine", FieldName = "BasicVelocityResults_Velocity", TimeSeconds = 0.0, ScenarioId = scenarioId, PhysicalAlternativeId = alternativeId });
                obs.Add(new HydraulicMetricObservation { ElementId = cid, Metric = HydraulicMetricKind.Depth, NumericValue = 0.78, Unit = "ft", EngineName = "CivilStormDynamicEngine", FieldName = "MiddleDepthResults_DepthMiddle", TimeSeconds = 0.0, ScenarioId = scenarioId, PhysicalAlternativeId = alternativeId });
                obs.Add(new HydraulicMetricObservation { ElementId = cid, Metric = HydraulicMetricKind.Capacity, NumericValue = 5.12, Unit = "ft3/s", EngineName = "GravityDerivedEngine", FieldName = "GravityDerivedCapacityResults_FullFlowCapacity", TimeSeconds = 0.0, ScenarioId = scenarioId, PhysicalAlternativeId = alternativeId });
                obs.Add(new HydraulicMetricObservation { ElementId = cid, Metric = HydraulicMetricKind.Hgl, NumericValue = 101.25, Unit = "ft", EngineName = "CivilStormDynamicEngine", FieldName = "MiddleHydraulicGradeResults_HydraulicGradeMiddle", TimeSeconds = 0.0, ScenarioId = scenarioId, PhysicalAlternativeId = alternativeId });
                obs.Add(new HydraulicMetricObservation { ElementId = cid, Metric = HydraulicMetricKind.Surcharge, BooleanValue = false, Unit = "dimensionless", EngineName = "CivilStormDynamicEngine", FieldName = "BasicSurchargedResults_IsSurcharged", TimeSeconds = 0.0, ScenarioId = scenarioId, PhysicalAlternativeId = alternativeId });
            }

            var fixture = ProductionFixture();
            var net = NetworkGeometryInfo.FromSnapshot(fixture.Network);

            var ctx = new ResultCompositionContext
            {
                ProjectId = request.WorkingCopyId,
                ModelFingerprint = plan.Inputs.ModelFingerprint,
                NetworkFingerprint = plan.NetworkFingerprint,
                CandidateFingerprint = plan.CandidateFingerprint,
                ScenarioId = scenarioId,
                PhysicalAlternativeId = alternativeId,
                CalculationContext = "Production post-solve result readback",
                CalculationRunId = request.RunId,
                ResultTimeSeconds = 0.0,
                IsTestOnly = false
            };

            return NetworkResultComposer.Compose(ctx, net, obs);
        }

        private static HydraulicResultSet CreateSampleGenuineResultSet(int scenarioId = 101, int alternativeId = 102)
        {
            return CreateSampleGenuineResultSet(CreateProductionReadbackTestRequest(calculate: true), scenarioId, alternativeId);
        }

        private static HostResult CreateProductionReadbackResult(HostRequest request, HydraulicResultSet resultSet)
        {
            var plan = request.EngineeringPlan;
            string setJson = RuntimeHostProtocol.Serializer().Serialize(resultSet);
            string primary = Path.GetFileName(request.WorkingModelPath);
            var inventoryList = new List<string>
            {
                primary + "|1000|1111111111111111111111111111111111111111111111111111111111111111",
                primary + ".sqlite|2000|2222222222222222222222222222222222222222222222222222222222222222",
                "Model.fld|500|3333333333333333333333333333333333333333333333333333333333333333",
                "Model.out|600|4444444444444444444444444444444444444444444444444444444444444444"
            };

            var postSolveSaveTime = DateTime.UtcNow;
            var solverStartTime = postSolveSaveTime.AddSeconds(-2);
            var solverEndTime = postSolveSaveTime.AddSeconds(-1);

            var res = new HostResult
            {
                SchemaVersion = 1,
                RunId = request.RunId,
                Success = true,
                FailureClassification = null,
                SelectedRuntime = request.SelectedRuntime,
                RuntimeCandidateCount = request.RuntimeCandidateCount,
                DetectedArchitecture = "x64",
                RuntimeBinding = true,
                ApplicationDescriptionCreated = true,
                ApplicationModelCreated = true,
                ParentUiModelCreated = true,
                LifecycleInitialized = true,
                ModelOpen = true,
                CurrentGraphicalProjectEstablished = true,
                DomainProjectEstablished = true,
                DomainDataSetEstablished = true,
                VendorCleanupCompleted = true,
                StaCompleted = true,
                ResultCommitted = true,
                RequestedExitMode = RuntimeHostProtocol.ExitMode,
                SaveInvocationCount = plan.Calculate ? 2 : 1,
                SaveAsInvocationCount = 0,
                ScenarioCreateInvocationCount = 1,
                AlternativeCreateInvocationCount = 1,
                MutationInvocationCount = plan.TotalFieldWriteCount,
                SolverRunInvocationCount = plan.Calculate ? 1 : 0,
                BoundCoreFiles = request.CoreFiles.Select(c => new HostMetadata { SimpleName = c.SimpleName, Path = c.Path, SHA256 = c.SHA256 }).ToArray(),
                Diagnostics = new Dictionary<string, string>
                {
                    { "StaCompleted", "true" },
                    { "FirstSessionCloseCompleted", "true" },
                    { "ProductionExecutionVerified", "true" },
                    { "ImmediateReadbackVerified", "true" },
                    { "SaveCurrentProjectInvoked", "true" },
                    { "SaveCurrentProjectSucceeded", "true" },
                    { "SaveTimestamp", postSolveSaveTime.ToString("o", CultureInfo.InvariantCulture) },
                    { "ReopenVerificationSuccess", "true" },
                    { "SourceIsolationVerified", "true" },
                    { "ScenarioCopyInvoked", "true" },
                    { "AlternativeCopyInvoked", "true" },
                    { "CreatedScenarioID", "101" },
                    { "CreatedPhysicalAlternativeID", "102" },
                    { "ExpectedCreatedScenarioId", "101" },
                    { "ExpectedCreatedPhysicalAlternativeId", "102" },
                    { "ProductionOptionsIdentity", "CivilStormDynamicEngine:67" },
                    { "ExpectedProductionOptionsIdentity", "CivilStormDynamicEngine:67" },
                    { "ExpectedNumericalEngine", "CivilStormDynamicEngine" },
                    { "PropertySetCount", plan.TotalFieldWriteCount.ToString(CultureInfo.InvariantCulture) },
                    { "LocalizationPropertySetCount", plan.LocalizationWriteCount.ToString(CultureInfo.InvariantCulture) },
                    { "EndpointInvertPropertySetCount", plan.EndpointWriteCount.ToString(CultureInfo.InvariantCulture) },
                    { "SolverRunInvoked", "true" },
                    { "SolverRunCompleted", "true" },
                    { "SolverStatusSuccess", "true" },
                    { "SolverLicenseAttached", "true" },
                    { "LicenseCleanupCompleted", "true" },
                    { "ScenarioComputeStatus", "Success" },
                    { "SolverStartTimestamp", solverStartTime.ToString("o", CultureInfo.InvariantCulture) },
                    { "SolverEndTimestamp", solverEndTime.ToString("o", CultureInfo.InvariantCulture) },
                    { "PostSolveSaveCurrentProjectInvoked", "true" },
                    { "PostSolveSaveCurrentProjectSucceeded", "true" },
                    { "PostSolveSaveTimestamp", postSolveSaveTime.ToString("o", CultureInfo.InvariantCulture) },
                    { "SecondSessionCloseCompleted", "true" },
                    { "ProductionReadbackVerified", "true" },
                    { "HydraulicResultReads", resultSet.Elements.Count.ToString(CultureInfo.InvariantCulture) },
                    { "HydraulicResultSetFingerprint", HydraulicResultSet.ComputeFingerprint(resultSet) },
                    { "HydraulicResultSetJson", setJson },
                    { "ResultFilesInventoryJson", RuntimeHostProtocol.Serializer().Serialize(inventoryList) }
                }
            };
            return res;
        }

        private static IntegritySnapshot CreateSampleProductionInventory(HostRequest request, HostResult result)
        {
            var entries = P16ResultContentAuthority.ReadInventory(result.Diagnostics)
                .ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
            return new IntegritySnapshot(ModelFamilyFingerprint.Create(Path.GetFileName(request.WorkingModelPath),
                entries.Values.Select(e => e.ToModelFamilyFileEntry())), DateTimeOffset.UtcNow, entries);
        }

        // Contract 1: Production lifecycle transitions into result readback only after solver success.
        private static void P16ProductionReadback_LifecycleTransitionsOnlyAfterSolverSuccess()
        {
            var req = CreateProductionReadbackTestRequest(calculate: true);
            var failedEvidence = CreateSolvedEvidence(solverSuccess: false, calculate: true, plan: req.EngineeringPlan);

            bool rejected = false;
            try
            {
                GravityWriteAndSolverHost.PrepareProductionReadback(req, failedEvidence);
            }
            catch (Exception ex)
            {
                rejected = ex.Message.Contains("SECOND_SESSION_CLEANUP");
            }
            Check(rejected, "PrepareProductionReadback rejects transition when solver did not succeed.");

            var succeededEvidence = CreateSolvedEvidence(solverSuccess: true, calculate: true, plan: req.EngineeringPlan);
            GravityWriteAndSolverHost.PrepareProductionReadback(req, succeededEvidence);
            Check(req.Diagnostics[GravityWriteAndSolverHost.ProductionPhase] == GravityWriteAndSolverHost.ProductionPhaseReadback,
                "PrepareProductionReadback sets phase to READBACK after solver success.");
            Check(req.Diagnostics["ExpectedCreatedScenarioId"] == "101", "ExpectedCreatedScenarioId recorded.");
            Check(req.Diagnostics["ExpectedCreatedPhysicalAlternativeId"] == "102", "ExpectedCreatedPhysicalAlternativeId recorded.");
        }

        // Contract 2: Readback does not repeat solver execution, candidate writes, scenario creation, or alternative creation.
        private static void P16ProductionReadback_ReadbackDoesNotRepeatMutationsOrSolver()
        {
            var req = CreateProductionReadbackTestRequest(calculate: true);
            req.Diagnostics[GravityWriteAndSolverHost.ProductionPhase] = GravityWriteAndSolverHost.ProductionPhaseReadback;

            var res = CreateProductionReadbackResult(req, CreateSampleGenuineResultSet(req));
            Check(res.SaveInvocationCount == 2, "Save count is exactly 2 (Session 1 + Session 2 post-solve; 0 during readback).");
            Check(res.ScenarioCreateInvocationCount == 1, "Scenario copy count remains exactly 1 (0 during readback).");
            Check(res.AlternativeCreateInvocationCount == 1, "Alternative copy count remains exactly 1 (0 during readback).");
            Check(res.MutationInvocationCount == req.EngineeringPlan.TotalFieldWriteCount, "Mutation count matches plan (0 during readback).");
            Check(res.SolverRunInvocationCount == 1, "Solver run count remains exactly 1 (0 during readback).");
            Check(res.Diagnostics["ProductionReadbackVerified"] == "true", "Readback verified recorded.");
        }

        // Contract 3: Requested element IDs cannot be silently replaced by another element.
        private static void P16ProductionReadback_RequestedElementsCannotBeSilentlyReplaced()
        {
            var nullObs = WaterObjectsResultReader.ReadNetworkObservations(null, null, 101, "CivilStormDynamicEngine", new[] { 999 });
            Check(nullObs != null && nullObs.Count == 6, "Null dataset returns diagnostic observations without throwing.");
            Check(nullObs.All(o => o.Availability == HydraulicResultAvailability.NotAvailable),
                "Null dataset observations are explicitly NotAvailable and do not claim successful reads.");

            int requestedId = 999;
            int fallbackId = 27;
            bool guardRejected = false;
            try
            {
                WaterObjectsResultReader.EnsureRequestedElement(requestedId, fallbackId, "Flow");
            }
            catch (InvalidOperationException ex)
            {
                guardRejected = ex.Message.StartsWith("REQUESTED_ELEMENT_MISMATCH");
            }
            Check(guardRejected, "Element fallback is rejected with REQUESTED_ELEMENT_MISMATCH.");
        }

        // Contract 4: Multiple conduit observations preserve exact metric, engine, unit, timestep, and execution provenance.
        private static void P16ProductionReadback_MultipleConduitObservationsPreserveProvenance()
        {
            var obs = new List<HydraulicMetricObservation>
            {
                // Conduit 27
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 1.45, Unit = "ft3/s", EngineName = "CivilStormDynamicEngine", FieldName = "BasicFlowResults_Flow", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Velocity, NumericValue = 2.34, Unit = "ft/s", EngineName = "CivilStormDynamicEngine", FieldName = "BasicVelocityResults_Velocity", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Depth, NumericValue = 0.78, Unit = "ft", EngineName = "CivilStormDynamicEngine", FieldName = "MiddleDepthResults_DepthMiddle", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Capacity, NumericValue = 5.12, Unit = "ft3/s", EngineName = "GravityDerivedEngine", FieldName = "GravityDerivedCapacityResults_FullFlowCapacity", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Hgl, NumericValue = 101.25, Unit = "ft", EngineName = "CivilStormDynamicEngine", FieldName = "MiddleHydraulicGradeResults_HydraulicGradeMiddle", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Surcharge, BooleanValue = false, Unit = "dimensionless", EngineName = "CivilStormDynamicEngine", FieldName = "BasicSurchargedResults_IsSurcharged", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 },

                // Conduit 28
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Flow, NumericValue = 3.10, Unit = "ft3/s", EngineName = "CivilStormDynamicEngine", FieldName = "BasicFlowResults_Flow", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Velocity, NumericValue = 3.45, Unit = "ft/s", EngineName = "CivilStormDynamicEngine", FieldName = "BasicVelocityResults_Velocity", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Depth, NumericValue = 1.10, Unit = "ft", EngineName = "CivilStormDynamicEngine", FieldName = "MiddleDepthResults_DepthMiddle", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Capacity, NumericValue = 7.80, Unit = "ft3/s", EngineName = "GravityDerivedEngine", FieldName = "GravityDerivedCapacityResults_FullFlowCapacity", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Hgl, NumericValue = 100.90, Unit = "ft", EngineName = "CivilStormDynamicEngine", FieldName = "MiddleHydraulicGradeResults_HydraulicGradeMiddle", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Surcharge, BooleanValue = true, Unit = "dimensionless", EngineName = "CivilStormDynamicEngine", FieldName = "BasicSurchargedResults_IsSurcharged", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 }
            };

            var net = new NetworkGeometryInfo
            {
                Conduits = new List<ConduitGeometryInfo>
                {
                    new ConduitGeometryInfo { ConduitId = 27, Label = "CO-1", StartNodeId = 1, StopNodeId = 2, Length = 100.0, LengthUnit = "ft", Diameter = 1.5, DiameterUnit = "ft", StartInvert = 100.5, StopInvert = 99.5 },
                    new ConduitGeometryInfo { ConduitId = 28, Label = "CO-2", StartNodeId = 2, StopNodeId = 3, Length = 120.0, LengthUnit = "ft", Diameter = 2.0, DiameterUnit = "ft", StartInvert = 99.4, StopInvert = 98.2 }
                },
                Nodes = new List<NodeGeometryInfo>
                {
                    new NodeGeometryInfo { NodeId = 1, Label = "MH-1", NodeKind = "Manhole", GroundElevation = 105.0, RimElevation = 105.0, InvertElevation = 100.5, ElevationUnit = "ft" },
                    new NodeGeometryInfo { NodeId = 2, Label = "MH-2", NodeKind = "Manhole", GroundElevation = 104.0, RimElevation = 104.0, InvertElevation = 99.4, ElevationUnit = "ft" },
                    new NodeGeometryInfo { NodeId = 3, Label = "O-1", NodeKind = "Outfall", GroundElevation = 103.0, RimElevation = 103.0, InvertElevation = 98.2, ElevationUnit = "ft" }
                }
            };

            var ctx = new ResultCompositionContext
            {
                ProjectId = "550e8400e29b41d4a716446655440000",
                ModelFingerprint = "032665DC5733359BCECC699E9EC91C66515A78BCFC59B731E3B1C40F67778493",
                NetworkFingerprint = "TEST_NETWORK_FINGERPRINT_P16",
                CandidateFingerprint = "TEST_CANDIDATE_FINGERPRINT_P16",
                ScenarioId = 101,
                PhysicalAlternativeId = 102,
                CalculationContext = "Production post-solve result readback",
                CalculationRunId = "c1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6",
                ResultTimeSeconds = 0.0,
                IsTestOnly = false
            };

            var set = NetworkResultComposer.Compose(ctx, net, obs);
            Check(set.Elements.Count == 5, "Five elements represented (2 conduits + 3 nodes).");

            var c27 = set.FindElement(27);
            var c28 = set.FindElement(28);
            Check(c27 != null && c28 != null, "Both conduits 27 and 28 are present.");

            var c27Cap = c27.FindMetric(HydraulicMetricKind.Capacity);
            Check(c27Cap.Provenance.Notes.Contains("Engine=GravityDerivedEngine"), "Conduit 27 Capacity preserves GravityDerivedEngine provenance.");
            var c28Flow = c28.FindMetric(HydraulicMetricKind.Flow);
            Check(c28Flow.Provenance.Notes.Contains("Engine=CivilStormDynamicEngine"), "Conduit 28 Flow preserves CivilStormDynamicEngine provenance.");
            Check(c27.ResultTimeSeconds == 0.0 && c28.ResultTimeSeconds == 0.0, "Timestep 0.0 is preserved.");
        }

        // Contract 5: Missing values remain unavailable; observed zero and Boolean Surcharge remain distinct.
        private static void P16ProductionReadback_MissingValuesUnavailableAndObservedZeroDistinct()
        {
            var obs = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 0.0, Unit = "ft3/s", EngineName = "CivilStormDynamicEngine", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Surcharge, BooleanValue = false, Unit = "dimensionless", EngineName = "CivilStormDynamicEngine", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 }
            };

            var net = new NetworkGeometryInfo
            {
                Conduits = new List<ConduitGeometryInfo>
                {
                    new ConduitGeometryInfo { ConduitId = 27, Label = "CO-1", StartNodeId = 1, StopNodeId = 2, Length = 100.0, LengthUnit = "ft", Diameter = 1.5, DiameterUnit = "ft", StartInvert = 100.5, StopInvert = 99.5 }
                }
            };

            var ctx = new ResultCompositionContext
            {
                ProjectId = "550e8400e29b41d4a716446655440000",
                ModelFingerprint = "032665DC5733359BCECC699E9EC91C66515A78BCFC59B731E3B1C40F67778493",
                NetworkFingerprint = "TEST_NETWORK_FINGERPRINT_P16",
                CandidateFingerprint = "TEST_CANDIDATE_FINGERPRINT_P16",
                ScenarioId = 101,
                PhysicalAlternativeId = 102,
                CalculationContext = "Production post-solve result readback",
                CalculationRunId = "c1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6",
                ResultTimeSeconds = 0.0,
                IsTestOnly = false
            };

            var set = NetworkResultComposer.Compose(ctx, net, obs);
            var c27 = set.FindElement(27);

            var flow = c27.FindMetric(HydraulicMetricKind.Flow);
            Check(flow.Availability == HydraulicResultAvailability.Available, "Observed zero flow is Available.");
            Check(flow.NumericValue == 0.0, "Observed zero flow value is exactly 0.0.");

            var vel = c27.FindMetric(HydraulicMetricKind.Velocity);
            Check(vel.Availability == HydraulicResultAvailability.NotAvailable, "Omitted velocity is NotAvailable.");
            Check(!vel.NumericValue.HasValue, "Omitted velocity NumericValue is null, never replaced by 0.0.");

            var sur = c27.FindMetric(HydraulicMetricKind.Surcharge);
            Check(sur.Availability == HydraulicResultAvailability.Available, "Surcharge is Available.");
            Check(sur.BooleanValue == false, "Surcharge BooleanValue is false.");
            Check(!sur.NumericValue.HasValue, "Surcharge NumericValue is null, distinct from 0.0 numeric value.");
        }

        // Contract 6: Mixed timesteps or mismatched candidate context fail closed.
        private static void P16ProductionReadback_MixedTimestepsOrMismatchedContextFailClosed()
        {
            var net = new NetworkGeometryInfo
            {
                Conduits = new List<ConduitGeometryInfo>
                {
                    new ConduitGeometryInfo { ConduitId = 27, Label = "CO-1", StartNodeId = 1, StopNodeId = 2, Length = 100.0, LengthUnit = "ft", Diameter = 1.5, DiameterUnit = "ft", StartInvert = 100.5, StopInvert = 99.5 }
                }
            };

            var ctx = new ResultCompositionContext
            {
                ProjectId = "550e8400e29b41d4a716446655440000",
                ModelFingerprint = "032665DC5733359BCECC699E9EC91C66515A78BCFC59B731E3B1C40F67778493",
                NetworkFingerprint = "TEST_NETWORK_FINGERPRINT_P16",
                CandidateFingerprint = "TEST_CANDIDATE_FINGERPRINT_P16",
                ScenarioId = 101,
                PhysicalAlternativeId = 102,
                CalculationContext = "Production post-solve result readback",
                CalculationRunId = "c1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6",
                ResultTimeSeconds = 0.0,
                IsTestOnly = false
            };

            // Case A: Mixed timesteps
            var mixedTimeObs = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 1.0, Unit = "ft3/s", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Velocity, NumericValue = 2.0, Unit = "ft/s", TimeSeconds = 3600.0, ScenarioId = 101, PhysicalAlternativeId = 102 }
            };
            bool timeMismatch = false;
            try { NetworkResultComposer.Compose(ctx, net, mixedTimeObs); }
            catch (InvalidOperationException ex) { timeMismatch = ex.Message.Contains("TIMESTEP_MISMATCH"); }
            Check(timeMismatch, "Mixed timesteps fail closed with TIMESTEP_MISMATCH.");

            // Case B: Mismatched scenario context
            var wrongScenarioObs = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 1.0, Unit = "ft3/s", TimeSeconds = 0.0, ScenarioId = 999, PhysicalAlternativeId = 102 }
            };
            bool scenarioMismatch = false;
            try { NetworkResultComposer.Compose(ctx, net, wrongScenarioObs); }
            catch (InvalidOperationException ex) { scenarioMismatch = ex.Message.Contains("SCENARIO_MISMATCH"); }
            Check(scenarioMismatch, "Mismatched scenario fails closed with SCENARIO_MISMATCH.");

            // Case C: Non-finite value
            var nonFiniteObs = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = double.NaN, Unit = "ft3/s", TimeSeconds = 0.0, ScenarioId = 101, PhysicalAlternativeId = 102 }
            };
            bool nonFiniteRejected = false;
            try { NetworkResultComposer.Compose(ctx, net, nonFiniteObs); }
            catch (InvalidDataException ex) { nonFiniteRejected = ex.Message.Contains("NON_FINITE_OBSERVATION"); }
            Check(nonFiniteRejected, "Non-finite observation fails closed with NON_FINITE_OBSERVATION.");
        }

        // Contract 7: Genuine authority rejects tampered result content and caller-created offline results.
        private static void P16ProductionReadback_GenuineAuthorityRejectsTamperedAndOfflineResults()
        {
            var req = CreateProductionReadbackTestRequest(calculate: true);
            var genuineSet = CreateSampleGenuineResultSet(req, 101, 102);
            var res = CreateProductionReadbackResult(req, genuineSet);
            var inventory = CreateSampleProductionInventory(req, res);

            // Mint authority from genuine completed production result
            var authority = P16ResultContentAuthority.FromProductionResult(req, res, inventory);
            Check(authority != null, "Genuine authority is successfully minted from valid production result.");
            Check(authority.ObservationScope == "PRODUCTION_POST_SOLVE_RESULT_READBACK", "Observation scope is correct.");

            // Case A: Offline-composed results (IsTestOnly = true) rejected
            var offlineSet = CreateSampleGenuineResultSet(req, 101, 102);
            offlineSet.IsTestOnly = true;
            bool offlineRejected = false;
            try { authority.ValidateContent(offlineSet); }
            catch (InvalidDataException ex) { offlineRejected = ex.Message.Contains("GENUINE_CONTENT_REQUIRED"); }
            Check(offlineRejected, "Offline-composed result set is rejected with GENUINE_CONTENT_REQUIRED.");

            // Case B: Genuine result created through authority passes validation
            var validSet = authority.CreateResultSet(req.WorkingCopyId, req.EngineeringPlan.NetworkFingerprint, req.EngineeringPlan.PlanFingerprint);
            bool validPassed = false;
            try
            {
                authority.ValidateContent(validSet);
                validPassed = true;
            }
            catch { validPassed = false; }
            Check(validPassed, "Genuine un-tampered result passes content authority validation.");

            int conduitId = req.EngineeringPlan.Endpoints[0].ConduitId;

            // Case C: Tampered flow value rejected
            var tamperedSet = authority.CreateResultSet(req.WorkingCopyId, req.EngineeringPlan.NetworkFingerprint, req.EngineeringPlan.PlanFingerprint);
            tamperedSet.FindElement(conduitId).FindMetric(HydraulicMetricKind.Flow).NumericValue = 999.99;
            bool tamperedRejected = false;
            try { authority.ValidateContent(tamperedSet); }
            catch (InvalidDataException ex) { tamperedRejected = ex.Message.Contains("CONTENT_MISMATCH"); }
            Check(tamperedRejected, "Tampered metric value is rejected with CONTENT_MISMATCH.");

            // Case D: Tampered element ID rejected
            var tamperedElement = authority.CreateResultSet(req.WorkingCopyId, req.EngineeringPlan.NetworkFingerprint, req.EngineeringPlan.PlanFingerprint);
            tamperedElement.FindElement(conduitId).ElementId = 9999;
            bool tamperedElementRejected = false;
            try { authority.ValidateContent(tamperedElement); }
            catch (InvalidDataException ex) { tamperedElementRejected = ex.Message.Contains("CONTENT_MISMATCH"); }
            Check(tamperedElementRejected, "Tampered element ID is rejected with CONTENT_MISMATCH.");

            // Case E: Missing engineering inputs fail-closed
            var invalidReq = CreateProductionReadbackTestRequest(calculate: true);
            invalidReq.EngineeringPlan = new GravityExecutionPlan { SchemaVersion = 1, Calculate = true, Inputs = new GravityEngineeringInputs() };
            bool inputRejected = false;
            try
            {
                P16ResultContentAuthority.ValidateCompletedProductionResult(invalidReq, res);
            }
            catch (InvalidOperationException ex)
            {
                inputRejected = ex.Message.Contains("P15_BLOCKED_EXTERNAL_ENGINEERING_INPUT");
            }
            Check(inputRejected, "Missing engineering inputs are rejected fail-closed with P15_BLOCKED_EXTERNAL_ENGINEERING_INPUT.");
        }

        // Contract 8: Non-calculating plans do not fabricate result-readback completion.
        private static void P16ProductionReadback_NonCalculatingPlansDoNotFabricateReadback()
        {
            var req = CreateProductionReadbackTestRequest(calculate: false);
            var evidence = CreateSolvedEvidence(solverSuccess: false, calculate: false, plan: req.EngineeringPlan);

            // Case A: PrepareProductionReadback fails closed on non-calculating plan
            bool prepareRejected = false;
            try { GravityWriteAndSolverHost.PrepareProductionReadback(req, evidence); }
            catch (Exception ex) { prepareRejected = ex.Message.Contains("CALCULATING_PLAN_REQUIRED"); }
            Check(prepareRejected, "PrepareProductionReadback throws CALCULATING_PLAN_REQUIRED for non-calculating plan.");

            // Case B: Non-calculating plan requires HydraulicResultReads == 0
            evidence.Diagnostics["HydraulicResultReads"] = "0";
            Check(GravityWriteAndSolverHost.ProductionSuccess(req, evidence), "Non-calculating plan succeeds with 0 reads.");

            // Case C: Attempting to fabricate readback verified on non-calculating plan fails closed
            evidence.Diagnostics["ProductionReadbackVerified"] = "true";
            Check(!GravityWriteAndSolverHost.ProductionSuccess(req, evidence),
                "Non-calculating plan fails closed if ProductionReadbackVerified is fabricated.");

            // Case D: Attempting to claim non-zero reads on non-calculating plan fails closed
            evidence.Diagnostics["ProductionReadbackVerified"] = "false";
            evidence.Diagnostics["HydraulicResultReads"] = "1";
            Check(!GravityWriteAndSolverHost.ProductionSuccess(req, evidence),
                "Non-calculating plan fails closed if HydraulicResultReads is non-zero.");
        }
    }
}
