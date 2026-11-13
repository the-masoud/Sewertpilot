using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SewerPilot.OpenFlows.Adapter;
using SewerPilot.OpenFlows.Contracts;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static ResultCompositionContext CreateSampleCompositionContext(string projectId = "P16_COMP_TEST_PROJECT", int scenarioId = 81, int alternativeId = 82)
        {
            return new ResultCompositionContext
            {
                ProjectId = projectId,
                ModelFingerprint = "032665DC5733359BCECC699E9EC91C66515A78BCFC59B731E3B1C40F67778493",
                NetworkFingerprint = "TEST_NETWORK_FINGERPRINT_12345",
                CandidateFingerprint = "TEST_CANDIDATE_FINGERPRINT_67890",
                ScenarioId = scenarioId,
                PhysicalAlternativeId = alternativeId,
                CalculationContext = "TEST_CALCULATION_CONTEXT_P16",
                CalculationRunId = "c1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6",
                ResultTimeSeconds = 0.0,
                IsTestOnly = true,
                Provenance = new HydraulicResultProvenance
                {
                    SourceDescription = "Offline test result composition context",
                    EvidenceReference = "TEST_EVIDENCE_REF_001",
                    CalculationContext = "TEST_CALCULATION_CONTEXT_P16"
                }
            };
        }

        private static NetworkGeometryInfo CreateSampleNetworkGeometry()
        {
            return new NetworkGeometryInfo
            {
                Conduits = new List<ConduitGeometryInfo>
                {
                    new ConduitGeometryInfo
                    {
                        ConduitId = 27,
                        Label = "CO-1",
                        StartNodeId = 16,
                        StopNodeId = 17,
                        Length = 100.0,
                        LengthUnit = "ft",
                        Diameter = 2.0,
                        DiameterUnit = "ft",
                        StartInvert = 100.0,
                        StartInvertUnit = "ft",
                        StopInvert = 99.0,
                        StopInvertUnit = "ft",
                        UpstreamGroundElevation = 105.0,
                        DownstreamGroundElevation = 104.0,
                        ElevationUnit = "ft"
                    },
                    new ConduitGeometryInfo
                    {
                        ConduitId = 28,
                        Label = "CO-2",
                        StartNodeId = 17,
                        StopNodeId = 18,
                        Length = 120.0,
                        LengthUnit = "ft",
                        Diameter = 2.5,
                        DiameterUnit = "ft",
                        StartInvert = 98.8,
                        StartInvertUnit = "ft",
                        StopInvert = 97.6,
                        StopInvertUnit = "ft",
                        UpstreamGroundElevation = 104.0,
                        DownstreamGroundElevation = 103.0,
                        ElevationUnit = "ft"
                    }
                },
                Nodes = new List<NodeGeometryInfo>
                {
                    new NodeGeometryInfo
                    {
                        NodeId = 16,
                        Label = "MH-1",
                        NodeKind = "Manhole",
                        GroundElevation = 105.0,
                        InvertElevation = 100.0,
                        ElevationUnit = "ft"
                    },
                    new NodeGeometryInfo
                    {
                        NodeId = 17,
                        Label = "MH-2",
                        NodeKind = "Manhole",
                        GroundElevation = 104.0,
                        InvertElevation = 98.8,
                        ElevationUnit = "ft"
                    }
                }
            };
        }

        // Test 1: Multi-element and multi-metric composition
        private static void P16Network_MultiElementAndMultiMetricComposition()
        {
            var ctx = CreateSampleCompositionContext();
            var net = CreateSampleNetworkGeometry();

            var obs = new List<HydraulicMetricObservation>
            {
                // Conduit 27 observations
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 0.0, Unit = "ft3/s", EngineName = "CivilStormDynamicEngine", FieldName = "BasicFlowResults_Flow" },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Velocity, NumericValue = 1.25, Unit = "ft/s", EngineName = "CivilStormDynamicEngine", FieldName = "BasicVelocityResults_Velocity" },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Depth, NumericValue = 0.8, Unit = "ft", EngineName = "CivilStormDynamicEngine", FieldName = "MiddleDepthResults_DepthMiddle" },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Capacity, NumericValue = 14.5, Unit = "ft3/s", EngineName = "GravityDerivedEngine", FieldName = "GravityDerivedCapacityResults_FullFlowCapacity" },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Hgl, NumericValue = 100.8, Unit = "ft", EngineName = "CivilStormDynamicEngine", FieldName = "MiddleHydraulicGradeResults_HydraulicGradeMiddle" },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Surcharge, BooleanValue = false, Unit = "dimensionless", EngineName = "CivilStormDynamicEngine", FieldName = "BasicSurchargedResults_IsSurcharged" },

                // Conduit 28 observations
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Flow, NumericValue = 3.5, Unit = "ft3/s", EngineName = "CivilStormDynamicEngine", FieldName = "BasicFlowResults_Flow" },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Velocity, NumericValue = 2.1, Unit = "ft/s", EngineName = "CivilStormDynamicEngine", FieldName = "BasicVelocityResults_Velocity" },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Depth, NumericValue = 1.5, Unit = "ft", EngineName = "CivilStormDynamicEngine", FieldName = "MiddleDepthResults_DepthMiddle" },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Capacity, NumericValue = 22.0, Unit = "ft3/s", EngineName = "GravityDerivedEngine", FieldName = "GravityDerivedCapacityResults_FullFlowCapacity" },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Hgl, NumericValue = 100.3, Unit = "ft", EngineName = "CivilStormDynamicEngine", FieldName = "MiddleHydraulicGradeResults_HydraulicGradeMiddle" },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Surcharge, BooleanValue = true, Unit = "dimensionless", EngineName = "CivilStormDynamicEngine", FieldName = "BasicSurchargedResults_IsSurcharged" }
            };

            var set = NetworkResultComposer.Compose(ctx, net, obs);

            Check(set != null, "Result set is composed.");
            Check(set.Elements.Count == 4, "Four elements represented (2 conduits + 2 nodes).");
            Check(set.ProjectId == ctx.ProjectId, "ProjectId preserved.");
            Check(set.ModelFingerprint == ctx.ModelFingerprint, "ModelFingerprint preserved.");

            var c27 = set.FindElement(27);
            Check(c27 != null, "Conduit 27 present.");
            Check(c27.ElementKind == "Conduit", "Conduit 27 is Conduit kind.");
            Check(c27.Metrics.Count == 9, "Conduit 27 has 9 metrics (6 native + 3 derived).");

            var c27Flow = c27.FindMetric(HydraulicMetricKind.Flow);
            Check(c27Flow.Availability == HydraulicResultAvailability.Available, "Flow is available.");
            Check(c27Flow.NumericValue == 0.0, "Flow is exactly 0.0.");
            Check(c27Flow.Provenance.Notes.Contains("CivilStormDynamicEngine"), "Flow engine is CivilStormDynamicEngine.");

            var c27Cap = c27.FindMetric(HydraulicMetricKind.Capacity);
            Check(c27Cap.Availability == HydraulicResultAvailability.Available, "Capacity is available.");
            Check(c27Cap.NumericValue == 14.5, "Capacity value preserved.");
            Check(c27Cap.Provenance.Notes.Contains("GravityDerivedEngine"), "Capacity engine is GravityDerivedEngine, not assumed same as flow.");

            var c27Sur = c27.FindMetric(HydraulicMetricKind.Surcharge);
            Check(c27Sur.Availability == HydraulicResultAvailability.Available, "Surcharge is available.");
            Check(c27Sur.BooleanValue == false, "Surcharge is Boolean false.");
            Check(!c27Sur.NumericValue.HasValue, "Surcharge has null NumericValue.");

            // Check derived metrics for conduit 27
            var c27Dd = c27.FindMetric(HydraulicMetricKind.DepthRatio);
            Check(c27Dd.Availability == HydraulicResultAvailability.Available, "DepthRatio is available.");
            Check(Math.Abs(c27Dd.NumericValue.Value - 0.4) < 1e-6, "DepthRatio d/D is 0.8 / 2.0 = 0.4.");
            Check(c27Dd.Derivation == HydraulicDerivedMetrics.DepthRatioFormula, "DepthRatio formula recorded.");

            var c27Slope = c27.FindMetric(HydraulicMetricKind.Slope);
            Check(c27Slope.Availability == HydraulicResultAvailability.Available, "Slope is available.");
            Check(Math.Abs(c27Slope.NumericValue.Value - 0.01) < 1e-6, "Slope is (100 - 99) / 100 = 0.01.");

            var c27Cover = c27.FindMetric(HydraulicMetricKind.Cover);
            Check(c27Cover.Availability == HydraulicResultAvailability.Available, "Cover is available.");
            Check(Math.Abs(c27Cover.NumericValue.Value - 3.0) < 1e-6, "Cover is 105 - (100 + 2) = 3.0 ft.");

            // Conduit 28 checks
            var c28 = set.FindElement(28);
            Check(c28 != null, "Conduit 28 present.");
            var c28Sur = c28.FindMetric(HydraulicMetricKind.Surcharge);
            Check(c28Sur.BooleanValue == true, "Conduit 28 Surcharge is Boolean true.");

            // Node 16 checks
            var n16 = set.FindElement(16);
            Check(n16 != null, "Node 16 present.");
            Check(n16.ElementKind == "Manhole", "Node 16 is Manhole.");
        }

        // Test 2: Deterministic output and fingerprint ordering
        private static void P16Network_DeterministicOutputAndFingerprintOrdering()
        {
            var ctx = CreateSampleCompositionContext();
            var net = CreateSampleNetworkGeometry();

            var obsForward = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 1.0, Unit = "ft3/s" },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Velocity, NumericValue = 2.0, Unit = "ft/s" },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Flow, NumericValue = 3.0, Unit = "ft3/s" },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Velocity, NumericValue = 4.0, Unit = "ft/s" }
            };

            var obsReverse = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Velocity, NumericValue = 4.0, Unit = "ft/s" },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Flow, NumericValue = 3.0, Unit = "ft3/s" },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Velocity, NumericValue = 2.0, Unit = "ft/s" },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 1.0, Unit = "ft3/s" }
            };

            var set1 = NetworkResultComposer.Compose(ctx, net, obsForward);
            var set2 = NetworkResultComposer.Compose(ctx, net, obsReverse);

            string fp1 = HydraulicResultSet.ComputeFingerprint(set1);
            string fp2 = HydraulicResultSet.ComputeFingerprint(set2);

            Check(fp1 == fp2, "Scrambled input observations yield byte-identical canonical fingerprint.");
            Check(set1.Elements.Select(e => e.ElementId).SequenceEqual(set2.Elements.Select(e => e.ElementId)), "Element ordering is identical.");

            for (int i = 0; i < set1.Elements.Count; i++)
            {
                var m1 = set1.Elements[i].Metrics.Select(m => (int)m.Metric);
                var m2 = set2.Elements[i].Metrics.Select(m => (int)m.Metric);
                Check(m1.SequenceEqual(m2), "Metric ordering within element is identical and deterministic.");
            }
        }

        // Test 3: Zero versus unavailable values
        private static void P16Network_ZeroVersusUnavailableValues()
        {
            var ctx = CreateSampleCompositionContext();
            var net = CreateSampleNetworkGeometry();

            var obs = new List<HydraulicMetricObservation>
            {
                // Conduit 27 has explicit observed zero for Flow
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 0.0, Unit = "ft3/s", Availability = HydraulicResultAvailability.Available },
                // Conduit 27 has explicit NotAvailable for Velocity (e.g. sensor failure)
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Velocity, NumericValue = null, Unit = "ft/s", Availability = HydraulicResultAvailability.NotAvailable, ErrorReason = "Sensor failure" }
                // Capacity is omitted entirely
            };

            var set = NetworkResultComposer.Compose(ctx, net, obs);
            var c27 = set.FindElement(27);

            var flow = c27.FindMetric(HydraulicMetricKind.Flow);
            Check(flow.Availability == HydraulicResultAvailability.Available, "Zero flow is Available.");
            Check(flow.NumericValue == 0.0, "Real numeric zero remains 0.0.");

            var vel = c27.FindMetric(HydraulicMetricKind.Velocity);
            Check(vel.Availability == HydraulicResultAvailability.NotAvailable, "Velocity is NotAvailable.");
            Check(!vel.NumericValue.HasValue, "Unavailable velocity does NOT default to zero.");

            var cap = c27.FindMetric(HydraulicMetricKind.Capacity);
            Check(cap.Availability == HydraulicResultAvailability.NotAvailable, "Omitted capacity is NotAvailable.");
            Check(!cap.NumericValue.HasValue, "Omitted capacity does NOT default to zero.");
        }

        // Test 4: Boolean Surcharge preservation
        private static void P16Network_BooleanSurchargePreservation()
        {
            var ctx = CreateSampleCompositionContext();
            var net = CreateSampleNetworkGeometry();

            var obs = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Surcharge, BooleanValue = false, Unit = "dimensionless" },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Surcharge, BooleanValue = true, Unit = "dimensionless" }
            };

            var set = NetworkResultComposer.Compose(ctx, net, obs);

            var c27Sur = set.FindElement(27).FindMetric(HydraulicMetricKind.Surcharge);
            Check(c27Sur.BooleanValue.HasValue && c27Sur.BooleanValue.Value == false, "Conduit 27 Surcharge is Boolean false.");
            Check(!c27Sur.NumericValue.HasValue, "Boolean false is not converted to numeric zero.");
            Check(c27Sur.HasUsableValue, "HasUsableValue is true for Boolean observation.");

            var c28Sur = set.FindElement(28).FindMetric(HydraulicMetricKind.Surcharge);
            Check(c28Sur.BooleanValue.HasValue && c28Sur.BooleanValue.Value == true, "Conduit 28 Surcharge is Boolean true.");
            Check(!c28Sur.NumericValue.HasValue, "Boolean true is not converted to numeric 1.0.");

            // Clone check
            var cloned = c27Sur.Clone();
            Check(cloned.BooleanValue == false, "Cloned metric preserves BooleanValue.");

            // Fingerprint check
            string fp = HydraulicResultSet.ComputeFingerprint(set);
            Check(!string.IsNullOrEmpty(fp) && fp.Length == 64, "Fingerprint successfully computed with Boolean surcharge.");
        }

        // Test 5: Derived-metric unit and input guards
        private static void P16Network_DerivedMetricUnitAndInputGuards()
        {
            var ctx = CreateSampleCompositionContext();
            var net = new NetworkGeometryInfo
            {
                Conduits = new List<ConduitGeometryInfo>
                {
                    // Conduit 1: Mismatched depth/diameter units (depth in ft, diameter in m)
                    new ConduitGeometryInfo { ConduitId = 1, Diameter = 1.0, DiameterUnit = "m", Length = 100.0, LengthUnit = "ft", StartInvert = 50.0, StartInvertUnit = "ft", StopInvert = 49.0, StopInvertUnit = "ft" },
                    // Conduit 2: Non-positive diameter (0.0)
                    new ConduitGeometryInfo { ConduitId = 2, Diameter = 0.0, DiameterUnit = "ft", Length = 100.0, LengthUnit = "ft", StartInvert = 50.0, StartInvertUnit = "ft", StopInvert = 49.0, StopInvertUnit = "ft" },
                    // Conduit 3: Mismatched slope units (invert in ft, length in m)
                    new ConduitGeometryInfo { ConduitId = 3, Diameter = 2.0, DiameterUnit = "ft", Length = 100.0, LengthUnit = "m", StartInvert = 50.0, StartInvertUnit = "ft", StopInvert = 49.0, StopInvertUnit = "ft" },
                    // Conduit 4: Non-positive length (0.0)
                    new ConduitGeometryInfo { ConduitId = 4, Diameter = 2.0, DiameterUnit = "ft", Length = 0.0, LengthUnit = "ft", StartInvert = 50.0, StartInvertUnit = "ft", StopInvert = 49.0, StopInvertUnit = "ft" }
                }
            };

            var obs = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 1, Metric = HydraulicMetricKind.Depth, NumericValue = 2.0, Unit = "ft" },
                new HydraulicMetricObservation { ElementId = 2, Metric = HydraulicMetricKind.Depth, NumericValue = 2.0, Unit = "ft" },
                new HydraulicMetricObservation { ElementId = 3, Metric = HydraulicMetricKind.Depth, NumericValue = 1.0, Unit = "ft" },
                new HydraulicMetricObservation { ElementId = 4, Metric = HydraulicMetricKind.Depth, NumericValue = 1.0, Unit = "ft" }
            };

            var set = NetworkResultComposer.Compose(ctx, net, obs);

            // Conduit 1: Unit mismatch on d/D
            var c1Dd = set.FindElement(1).FindMetric(HydraulicMetricKind.DepthRatio);
            Check(c1Dd.Availability == HydraulicResultAvailability.NotAvailable, "Mismatched units on d/D yield NotAvailable.");

            // Conduit 2: Non-positive diameter
            var c2Dd = set.FindElement(2).FindMetric(HydraulicMetricKind.DepthRatio);
            Check(c2Dd.Availability == HydraulicResultAvailability.NotAvailable, "Non-positive diameter yields NotAvailable on d/D.");

            // Conduit 3: Mismatched slope units
            var c3Slope = set.FindElement(3).FindMetric(HydraulicMetricKind.Slope);
            Check(c3Slope.Availability == HydraulicResultAvailability.NotAvailable, "Mismatched units on slope yield NotAvailable.");

            // Conduit 4: Zero length on slope
            var c4Slope = set.FindElement(4).FindMetric(HydraulicMetricKind.Slope);
            Check(c4Slope.Availability == HydraulicResultAvailability.NotAvailable, "Zero length yields NotAvailable on slope.");
        }

        // Test 6: Duplicate, conflicting, non-finite, and mismatched-context rejection
        private static void P16Network_DuplicateConflictingNonFiniteAndMismatchedContextRejection()
        {
            var ctx = CreateSampleCompositionContext();
            var net = CreateSampleNetworkGeometry();

            // 1. Duplicate observation
            var dupObs = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 1.0 },
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 2.0 }
            };
            bool dupRejected = false;
            try { NetworkResultComposer.Compose(ctx, net, dupObs); }
            catch (InvalidOperationException ex) { dupRejected = ex.Message.Contains("DUPLICATE_OBSERVATION"); }
            Check(dupRejected, "Duplicate observation rejected.");

            // 2. Non-finite value (NaN)
            var nanObs = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Velocity, NumericValue = double.NaN }
            };
            bool nanRejected = false;
            try { NetworkResultComposer.Compose(ctx, net, nanObs); }
            catch (InvalidDataException ex) { nanRejected = ex.Message.Contains("NON_FINITE_OBSERVATION"); }
            Check(nanRejected, "NaN observation rejected.");

            // 3. Scenario mismatch
            var scenObs = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 1.0, ScenarioId = 999 }
            };
            bool scenRejected = false;
            try { NetworkResultComposer.Compose(ctx, net, scenObs); }
            catch (InvalidOperationException ex) { scenRejected = ex.Message.Contains("SCENARIO_MISMATCH"); }
            Check(scenRejected, "Mismatched scenario ID rejected.");

            // 4. Alternative mismatch
            var altObs = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 1.0, PhysicalAlternativeId = 999 }
            };
            bool altRejected = false;
            try { NetworkResultComposer.Compose(ctx, net, altObs); }
            catch (InvalidOperationException ex) { altRejected = ex.Message.Contains("ALTERNATIVE_MISMATCH"); }
            Check(altRejected, "Mismatched physical alternative rejected.");

            // 5. Context mismatch
            var ctxObs = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 1.0, CalculationContext = "DIFFERENT_CONTEXT" }
            };
            bool ctxRejected = false;
            try { NetworkResultComposer.Compose(ctx, net, ctxObs); }
            catch (InvalidOperationException ex) { ctxRejected = ex.Message.Contains("CALCULATION_CONTEXT_MISMATCH"); }
            Check(ctxRejected, "Mismatched calculation context rejected.");
        }

        // Test 7: Confirmation that composed offline data cannot bypass genuine-result authority
        private static void P16Network_ComposedOfflineDataCannotBypassGenuineResultAuthority()
        {
            var ctx = CreateSampleCompositionContext();
            var net = CreateSampleNetworkGeometry();

            var obs = new List<HydraulicMetricObservation>
            {
                new HydraulicMetricObservation { ElementId = 27, Metric = HydraulicMetricKind.Flow, NumericValue = 1.0, Unit = "ft3/s" },
                new HydraulicMetricObservation { ElementId = 28, Metric = HydraulicMetricKind.Flow, NumericValue = 2.0, Unit = "ft3/s" }
            };

            var composed = NetworkResultComposer.Compose(ctx, net, obs);
            string fp = HydraulicResultSet.ComputeFingerprint(composed);

            // 1. String-based runtime trust overload throws unconditionally
            bool strRejected = false;
            try { HydraulicResultTrustProvenance.AcceptedResultAccessRuntime(fp, "TEST_EVIDENCE"); }
            catch (NotSupportedException ex) { strRejected = ex.Message.Contains("RESULT_ACCESS_RUNTIME_UNAVAILABLE"); }
            Check(strRejected, "String-based AcceptedResultAccessRuntime throws RESULT_ACCESS_RUNTIME_UNAVAILABLE.");

            // 2. Typed authority overload rejects offline-composed results with IsTestOnly = true
            var recordedAuthority = RecordedAuthority();
            bool testOnlyRejected = false;
            try { HydraulicResultTrustProvenance.AcceptedResultAccessRuntime(recordedAuthority, composed); }
            catch (InvalidDataException ex) { testOnlyRejected = ex.Message.Contains("GENUINE_CONTENT_REQUIRED"); }
            Check(testOnlyRejected, "Composed test-only result set rejected with GENUINE_CONTENT_REQUIRED.");

            // 3. Even if IsTestOnly is falsified, content mismatch rejects it
            composed.IsTestOnly = false;
            bool mismatchRejected = false;
            try { HydraulicResultTrustProvenance.AcceptedResultAccessRuntime(recordedAuthority, composed); }
            catch (InvalidDataException ex) { mismatchRejected = ex.Message.Contains("CONTENT_MISMATCH"); }
            Check(mismatchRejected, "Multi-element composed result set rejected with CONTENT_MISMATCH against recorded proof.");
        }

        private sealed class TestModelStub : IOpenFlowsModel
        {
            public ModelIdentity Identity { get; }
            public TestModelStub(string id) { Identity = new ModelIdentity(id); }
            public void Dispose() { }
        }

        private sealed class TestModelWithNetwork : IOpenFlowsModel
        {
            public ModelIdentity Identity { get; }
            public NetworkReadResult NetworkRead { get; }
            public TestModelWithNetwork(NetworkReadResult net)
            {
                Identity = new ModelIdentity("MODEL_WITH_NETWORK");
                NetworkRead = net;
            }
            public void Dispose() { }
        }

        private sealed class TestClosedModel : IOpenFlowsModel
        {
            public ModelIdentity Identity { get; }
            public bool IsOpen => false;
            public TestClosedModel(string id) { Identity = new ModelIdentity(id); }
            public void Dispose() { }
        }

        // Test 8: ReadElements returns real identities or throws a typed contract exception (anti-stub assertion)
        private static void P16Network_ReadElementsAntiStubContractGuard()
        {
            var reader = new WaterObjectsResultReader();
            var run = new CalculationRunIdentity("RUN_01");

            // 1. Null argument validation
            bool nullModelCaught = false;
            try { reader.ReadElements(null, run); } catch (ArgumentNullException) { nullModelCaught = true; }
            Check(nullModelCaught, "Null model throws ArgumentNullException.");

            bool nullRunCaught = false;
            try { reader.ReadElements(new TestModelStub("M1"), null); } catch (ArgumentNullException) { nullRunCaught = true; }
            Check(nullRunCaught, "Null run throws ArgumentNullException.");

            // 2. Inactive / unsupported model throws typed HydraulicDataContractException
            bool contractExceptionCaught = false;
            try
            {
                reader.ReadElements(new TestModelStub("INACTIVE_MODEL"), run);
            }
            catch (HydraulicDataContractException ex)
            {
                contractExceptionCaught = ex.Message.Contains("cannot enumerate element identities");
            }
            Check(contractExceptionCaught, "Inactive model throws typed HydraulicDataContractException (anti-stub guard).");

            // 3. Active model with empty element collection throws HydraulicDataContractException (empty list prohibited)
            bool emptyRejected = false;
            var emptyActiveModel = new WaterObjectsActiveModel(new ModelIdentity("EMPTY_ACTIVE"), elements: new ElementIdentity[0]);
            try
            {
                reader.ReadElements(emptyActiveModel, run);
            }
            catch (HydraulicDataContractException)
            {
                emptyRejected = true;
            }
            Check(emptyRejected, "Active model with 0 elements throws HydraulicDataContractException; empty list prohibited.");

            // 4. Active model with genuine elements returns deterministically sorted identities
            var realElements = new[] { new ElementIdentity("27"), new ElementIdentity("16"), new ElementIdentity("28") };
            var activeModel = new WaterObjectsActiveModel(new ModelIdentity("ACTIVE_MODEL"), elements: realElements);
            var resultElements = reader.ReadElements(activeModel, run);
            Check(resultElements != null, "Genuine elements returned.");
            Check(resultElements.Count == 3, "Three elements returned.");
            Check(resultElements[0].Value == "16" && resultElements[1].Value == "27" && resultElements[2].Value == "28", "Elements deterministically sorted.");

            // 5. Model with network reflection returns conduit and structure identities
            var readResult = new NetworkReadResult();
            readResult.Conduits.Add(new ConduitReadDto { Id = 101 });
            readResult.Conduits.Add(new ConduitReadDto { Id = 102 });
            readResult.Structures.Add(new StructureReadDto { Id = 201 });
            var modelWithNetwork = new TestModelWithNetwork(readResult);
            var netElements = reader.ReadElements(modelWithNetwork, run);
            Check(netElements != null && netElements.Count == 3, "Network model returns genuine identities.");
            Check(netElements[0].Value == "101" && netElements[1].Value == "102" && netElements[2].Value == "201", "Network conduit and structure identities bound.");

            // 6. Closed / uninitialized model throws InvalidOperationException
            bool closedModelCaught = false;
            try
            {
                reader.ReadElements(new TestClosedModel("CLOSED_MODEL"), run);
            }
            catch (InvalidOperationException ex)
            {
                closedModelCaught = ex.Message.Contains("closed or uninitialized");
            }
            Check(closedModelCaught, "Closed model throws InvalidOperationException.");

            // 7. Missing/whitespace label triggers Zero-Fabrication Guard (fails closed)
            bool zeroFabricationCaught = false;
            var invalidNet = new NetworkReadResult();
            invalidNet.Conduits.Add(new ConduitReadDto { Id = 0, Label = "   " });
            try
            {
                reader.ReadElements(new TestModelWithNetwork(invalidNet), run);
            }
            catch (InvalidOperationException ex)
            {
                zeroFabricationCaught = ex.Message.Contains("authoritative label");
            }
            Check(zeroFabricationCaught, "Blank/whitespace label triggers Zero-Fabrication Guard.");

            // 8. Execute dedicated 5-branch unit test suite
            WaterObjectsResultReaderTests.RunAll();
        }

        // Test 9: Simulated IPC timeout aborts cleanly within deadline limits and terminates orphaned process
        private static void P16Network_Civil3dIpcResponseTimeoutGuard()
        {
            string pipeName = "SewerPilot_TimeoutTest_" + Guid.NewGuid().ToString("N");

            // Spawn a dummy process to simulate orphaned worker
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command Start-Sleep -Seconds 30",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };
            var dummyProcess = Process.Start(psi);
            Check(dummyProcess != null && !dummyProcess.HasExited, "Dummy child process spawned for timeout guard.");

            try
            {
                using (var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    var serverConnected = new ManualResetEvent(false);
                    var serverThread = new Thread(() =>
                    {
                        try
                        {
                            server.WaitForConnection();
                            serverConnected.Set();
                            using (var serverReader = new StreamReader(server, System.Text.Encoding.UTF8))
                            {
                                serverReader.ReadLine();
                                // Purposefully stay silent and never reply to simulate hanging Civil 3D IPC server
                                Thread.Sleep(5000);
                            }
                        }
                        catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in P16NetworkResultCompositionTests.cs: " + suppressedEx.Message); }
                    }) { IsBackground = true };
                    serverThread.Start();

                    using (var client = new Civil3dLiveClient(pipeName, dummyProcess, responseTimeoutMs: 500))
                    {
                        client.Connect(5000);
                        Check(serverConnected.WaitOne(3000), "Server connected to IPC test pipe.");

                        var sw = Stopwatch.StartNew();
                        bool timedOut = false;
                        try
                        {
                            // Server reads request but purposefully never replies; client must abort and throw within bounded deadline
                            client.SendCommand(new LiveIpcRequest { Command = "PING" }, timeoutMs: 500);
                        }
                        catch (TimeoutException ex)
                        {
                            timedOut = true;
                            Check(ex.Message.Contains("timed out"), "TimeoutException contains timeout description.");
                        }
                        sw.Stop();

                        Check(timedOut, "IPC read threw TimeoutException when response was not received within deadline.");
                        Check(sw.ElapsedMilliseconds < 5000, "Timeout completed well within maximum deadline.");

                        // Assert orphaned process was killed
                        bool processTerminated = dummyProcess.WaitForExit(3000);
                        Check(processTerminated, "Orphaned process was terminated upon IPC timeout.");

                        // Assert client connection was aborted cleanly
                        bool connectionAborted = false;
                        try
                        {
                            client.SendCommand(new LiveIpcRequest { Command = "PING" });
                        }
                        catch (InvalidOperationException)
                        {
                            connectionAborted = true;
                        }
                        Check(connectionAborted, "Client connection was cleanly aborted.");
                    }
                }
            }
            finally
            {
                try { if (dummyProcess != null && !dummyProcess.HasExited) dummyProcess.Kill(); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in P16NetworkResultCompositionTests.cs: " + suppressedEx.Message); }
                try { dummyProcess?.Dispose(); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in P16NetworkResultCompositionTests.cs: " + suppressedEx.Message); }
            }
        }
    }
}
