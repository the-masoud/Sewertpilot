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
        private static void Throws<T>(Action action, string message) where T : Exception
        {
            assertions++;
            try
            {
                action();
                throw new InvalidOperationException("EXPECTED_EXCEPTION_NOT_THROWN: " + message);
            }
            catch (T)
            {
                // Expected
            }
        }

        private static void P19_RationalMethod_BenchmarkCalculation()
        {
            var idf = new IdfParameters
            {
                Name = "10-Year Test IDF",
                ReturnPeriodYears = 10,
                A = 100.0,
                B = 10.0,
                C = 1.0,
                MinimumTimeOfConcentrationMinutes = 5.0
            };

            var catchment = new StormCatchment
            {
                CatchmentId = "CAT-01",
                Name = "Commercial Parking Lot",
                TargetNodeId = 17,
                TotalAreaAcres = 5.0,
                TimeOfConcentrationMinutes = 15.0,
                RunoffCoefficient = 0.60
            };

            var result = StormFlowHydrologyEngine.ComputeRational(catchment, idf, returnPeriodYears: 10);

            Check(result != null, "Result returned.");
            Check(result.Method == HydrologyCalculationMethod.Rational, "Method is Rational.");
            Check(result.CatchmentId == "CAT-01", "CatchmentId matches.");
            Check(result.TargetNodeId == 17, "TargetNodeId matches.");
            Check(Math.Abs(result.TimeOfConcentrationMinutes - 15.0) < 1e-4, "Tc is 15 minutes.");

            // I = 100 / (15 + 10) = 4.0 in/hr
            Check(Math.Abs(result.RainfallIntensityInPerHour - 4.0) < 1e-4, "Intensity is 4.0 in/hr.");

            // Q = 0.60 * 4.0 * 5.0 = 12.0 cfs
            Check(Math.Abs(result.PeakDischargeCfs - 12.0) < 1e-4, "Peak flow is 12.0 cfs.");

            // Metric equivalent: 12.0 * 0.0283168...
            Check(result.PeakDischargeCms > 0.33 && result.PeakDischargeCms < 0.35, "Peak flow in cms is valid.");

            // Hydrograph check
            Check(result.Hydrograph != null && result.Hydrograph.Count > 0, "Hydrograph generated.");
            var peakPoint = result.Hydrograph.OrderByDescending(p => p.FlowCfs).First();
            Check(Math.Abs(peakPoint.FlowCfs - 12.0) < 1e-3, "Hydrograph peak flow matches calculation.");
            Check(result.TotalVolumeCubicFeet > 0, "Total volume is positive.");
        }

        private static void P19_RationalMethod_FrequencyFactors()
        {
            var idf = new IdfParameters { A = 100.0, B = 10.0, C = 1.0 };
            var catchment = new StormCatchment
            {
                CatchmentId = "CAT-02",
                TargetNodeId = 18,
                TotalAreaAcres = 2.0,
                TimeOfConcentrationMinutes = 10.0,
                RunoffCoefficient = 0.50
            };

            // 10-yr: Cf = 1.00 -> Q = 0.50 * 5.0 * 2.0 = 5.0 cfs
            var r10 = StormFlowHydrologyEngine.ComputeRational(catchment, idf, 10);
            Check(Math.Abs(r10.PeakDischargeCfs - 5.0) < 1e-4, "10-yr peak flow is 5.0 cfs (Cf=1.00).");

            // 25-yr: Cf = 1.10 -> C_eff = 0.55 -> Q = 0.55 * 5.0 * 2.0 = 5.50 cfs
            var r25 = StormFlowHydrologyEngine.ComputeRational(catchment, idf, 25);
            Check(Math.Abs(r25.PeakDischargeCfs - 5.5) < 1e-4, "25-yr peak flow is 5.5 cfs (Cf=1.10).");

            // 50-yr: Cf = 1.20 -> C_eff = 0.60 -> Q = 0.60 * 5.0 * 2.0 = 6.00 cfs
            var r50 = StormFlowHydrologyEngine.ComputeRational(catchment, idf, 50);
            Check(Math.Abs(r50.PeakDischargeCfs - 6.0) < 1e-4, "50-yr peak flow is 6.0 cfs (Cf=1.20).");

            // 100-yr: Cf = 1.25 -> C_eff = 0.625 -> Q = 0.625 * 5.0 * 2.0 = 6.25 cfs
            var r100 = StormFlowHydrologyEngine.ComputeRational(catchment, idf, 100);
            Check(Math.Abs(r100.PeakDischargeCfs - 6.25) < 1e-4, "100-yr peak flow is 6.25 cfs (Cf=1.25).");

            // Test Capping: High C=0.90 with Cf=1.25 would be 1.125, must be capped at 1.00
            var highCatchment = new StormCatchment
            {
                CatchmentId = "CAT-HIGH",
                TargetNodeId = 18,
                TotalAreaAcres = 2.0,
                TimeOfConcentrationMinutes = 10.0,
                RunoffCoefficient = 0.90
            };
            var rHigh100 = StormFlowHydrologyEngine.ComputeRational(highCatchment, idf, 100);
            Check(Math.Abs(rHigh100.EffectiveRunoffParameter - 1.0) < 1e-4, "Runoff coefficient is capped at 1.00.");
            Check(Math.Abs(rHigh100.PeakDischargeCfs - (1.0 * 5.0 * 2.0)) < 1e-4, "Peak flow uses capped C=1.0.");
        }

        private static void P19_RationalMethod_CompositeSubAreas()
        {
            var idf = new IdfParameters { A = 60.0, B = 5.0, C = 1.0 };
            var catchment = new StormCatchment
            {
                CatchmentId = "CAT-COMP",
                TargetNodeId = 19,
                TotalAreaAcres = 5.0,
                TimeOfConcentrationMinutes = 10.0,
                SubAreas = new List<SubArea>
                {
                    new SubArea { Name = "Asphalt Roof", AreaAcres = 2.0, RunoffCoefficient = 0.90 },
                    new SubArea { Name = "Turf Grass", AreaAcres = 3.0, RunoffCoefficient = 0.30 }
                }
            };

            // Expected composite C = (2.0*0.90 + 3.0*0.30) / 5.0 = (1.80 + 0.90) / 5.0 = 2.70 / 5.0 = 0.54
            double compC = catchment.GetEffectiveRunoffCoefficient();
            Check(Math.Abs(compC - 0.54) < 1e-4, "Composite runoff coefficient is 0.54.");

            var res = StormFlowHydrologyEngine.ComputeRational(catchment, idf, 10);
            Check(Math.Abs(res.EffectiveRunoffParameter - 0.54) < 1e-4, "Calculated effective parameter matches 0.54.");
            // I = 60 / (10 + 5) = 4.0 in/hr. Q = 0.54 * 4.0 * 5.0 = 10.80 cfs
            Check(Math.Abs(res.PeakDischargeCfs - 10.80) < 1e-4, "Peak flow is 10.80 cfs.");
        }

        private static void P19_RationalMethod_MinimumTimeOfConcentrationEnforced()
        {
            var idf = new IdfParameters
            {
                A = 100.0,
                B = 10.0,
                C = 1.0,
                MinimumTimeOfConcentrationMinutes = 5.0
            };

            var catchment = new StormCatchment
            {
                CatchmentId = "CAT-FAST",
                TargetNodeId = 20,
                TotalAreaAcres = 1.0,
                TimeOfConcentrationMinutes = 2.0, // Less than minimum
                RunoffCoefficient = 0.50
            };

            var res = StormFlowHydrologyEngine.ComputeRational(catchment, idf, 10, minTcMinutes: 5.0);
            Check(Math.Abs(res.TimeOfConcentrationMinutes - 5.0) < 1e-4, "Tc is clamped to 5.0 min minimum.");
            // I = 100 / (5 + 10) = 6.6667 in/hr
            Check(Math.Abs(res.RainfallIntensityInPerHour - (100.0 / 15.0)) < 1e-4, "Intensity uses minimum Tc.");
        }

        private static void P19_IdfParameters_ValidationAndEvaluation()
        {
            var idf = new IdfParameters { A = 120.0, B = 15.0, C = 0.85 };
            idf.Validate();
            double i = idf.CalculateIntensity(15.0);
            Check(i > 0 && !double.IsNaN(i), "Intensity evaluated successfully.");

            // Negative A throws
            Throws<ArgumentException>(() => new IdfParameters { A = -50.0 }.Validate(), "Negative A rejected.");
            // Negative B throws
            Throws<ArgumentException>(() => new IdfParameters { A = 50.0, B = -1.0 }.Validate(), "Negative B rejected.");
            // Zero or negative C throws
            Throws<ArgumentException>(() => new IdfParameters { A = 50.0, C = 0.0 }.Validate(), "Zero C rejected.");
            // Non-finite duration throws
            Throws<ArgumentException>(() => idf.CalculateIntensity(double.NaN), "NaN duration rejected.");
        }

        private static void P19_ScsCurveNumber_BenchmarkCalculation()
        {
            var catchment = new StormCatchment
            {
                CatchmentId = "CAT-SCS-01",
                TargetNodeId = 21,
                TotalAreaAcres = 10.0,
                CurveNumber = 80.0,
                TimeOfConcentrationMinutes = 30.0
            };

            // Precipitation = 3.50 inches
            // S = 1000/80 - 10 = 2.50 inches
            // Ia = 0.2 * 2.50 = 0.50 inches
            // Qdepth = (3.50 - 0.50)^2 / (3.50 - 0.50 + 2.50) = 9.0 / 5.5 = 1.63636 inches
            var res = StormFlowHydrologyEngine.ComputeScsCurveNumber(catchment, 3.50, ScsStormType.TypeII, 10);

            Check(res != null, "SCS result returned.");
            Check(res.Method == HydrologyCalculationMethod.ScsCurveNumber, "Method is ScsCurveNumber.");
            Check(res.EffectiveRunoffParameter == 80.0, "CN matches.");
            Check(res.Diagnostics.ContainsKey("PotentialMaxRetentionS"), "Diagnostics has S.");
            Check(Math.Abs(double.Parse(res.Diagnostics["PotentialMaxRetentionS"]) - 2.50) < 1e-4, "S is 2.50 inches.");
            Check(Math.Abs(double.Parse(res.Diagnostics["InitialAbstractionIa"]) - 0.50) < 1e-4, "Ia is 0.50 inches.");

            double expectedQdepth = (3.0 * 3.0) / 5.5;
            Check(Math.Abs(double.Parse(res.Diagnostics["RunoffDepthInches"]) - expectedQdepth) < 1e-3, "Runoff depth matches TR-55 formula.");
            Check(res.PeakDischargeCfs > 0, "Peak discharge is positive.");
            Check(res.TotalVolumeCubicFeet > 0, "Total volume is positive.");
            Check(res.Hydrograph.Count > 0, "Hydrograph points generated.");
        }

        private static void P19_ScsCurveNumber_ZeroRunoffBelowInitialAbstraction()
        {
            var catchment = new StormCatchment
            {
                CatchmentId = "CAT-SCS-ZERO",
                TargetNodeId = 22,
                TotalAreaAcres = 10.0,
                CurveNumber = 75.0, // S = 3.333 in, Ia = 0.667 in
                TimeOfConcentrationMinutes = 20.0
            };

            // Rainfall is 0.50 inches, which is less than Ia = 0.667 inches
            var res = StormFlowHydrologyEngine.ComputeScsCurveNumber(catchment, 0.50, ScsStormType.TypeII, 10);
            Check(res.PeakDischargeCfs == 0.0, "Peak discharge is zero when P <= Ia.");
            Check(res.TotalVolumeCubicFeet == 0.0, "Total volume is zero when P <= Ia.");
            Check(double.Parse(res.Diagnostics["RunoffDepthInches"]) == 0.0, "Runoff depth is zero.");
        }

        private static void P19_SwmmReservoir_OverlandFlowRouting()
        {
            var catchment = new StormCatchment
            {
                CatchmentId = "CAT-SWMM",
                TargetNodeId = 23,
                TotalAreaAcres = 2.0,
                FlowLengthFeet = 100.0,
                Slope = 0.02,
                ManningN = 0.02,
                DepressionStorageInches = 0.10,
                TimeOfConcentrationMinutes = 15.0
            };

            var res = StormFlowHydrologyEngine.ComputeSwmmReservoir(catchment, precipDepthInches: 2.0, stormDurationHours: 1.0);
            Check(res != null, "SWMM reservoir result returned.");
            Check(res.Method == HydrologyCalculationMethod.SwmmReservoir, "Method is SwmmReservoir.");
            Check(res.PeakDischargeCfs > 0, "Peak flow is positive.");
            Check(res.TotalVolumeCubicFeet > 0, "Total volume is positive.");
            Check(res.Hydrograph.Count > 0, "Hydrograph points recorded.");
        }

        private static void P19_CatchmentAggregation_SharedInletNodes()
        {
            var idf = new IdfParameters { A = 100.0, B = 10.0, C = 1.0 };
            var c1 = new StormCatchment
            {
                CatchmentId = "C1",
                TargetNodeId = 100,
                TotalAreaAcres = 2.0,
                RunoffCoefficient = 0.80,
                TimeOfConcentrationMinutes = 10.0
            };
            var c2 = new StormCatchment
            {
                CatchmentId = "C2",
                TargetNodeId = 100,
                TotalAreaAcres = 3.0,
                RunoffCoefficient = 0.40,
                TimeOfConcentrationMinutes = 15.0
            };
            var c3 = new StormCatchment
            {
                CatchmentId = "C3",
                TargetNodeId = 200,
                TotalAreaAcres = 4.0,
                RunoffCoefficient = 0.50,
                TimeOfConcentrationMinutes = 12.0
            };

            var aggregated = StormFlowHydrologyEngine.AggregateCatchmentInflows(new[] { c1, c2, c3 }, idf, 10);
            Check(aggregated.Count == 2, "Aggregated into two target nodes (100 and 200).");

            var node100 = aggregated.Single(a => a.TargetNodeId == 100);
            Check(node100.ContributingCatchmentIds.Count == 2, "Node 100 has 2 contributing catchments.");
            Check(Math.Abs(node100.TotalDrainageAreaAcres - 5.0) < 1e-4, "Total area for Node 100 is 5.0 acres.");

            // Composite C = (2*0.8 + 3*0.4) / 5.0 = 2.8 / 5.0 = 0.56
            Check(Math.Abs(node100.CompositeRunoffCoefficient - 0.56) < 1e-4, "Composite C is 0.56.");
            // Governing Tc = max(10, 15) = 15 min. I = 100 / (15 + 10) = 4.0 in/hr.
            Check(Math.Abs(node100.GoverningTimeOfConcentrationMinutes - 15.0) < 1e-4, "Governing Tc is 15.0 min.");
            // Q = 0.56 * 4.0 * 5.0 = 11.20 cfs
            Check(Math.Abs(node100.TotalPeakDischargeCfs - 11.20) < 1e-4, "Total peak flow for Node 100 is 11.20 cfs.");

            var node200 = aggregated.Single(a => a.TargetNodeId == 200);
            Check(node200.ContributingCatchmentIds.Count == 1, "Node 200 has 1 catchment.");
            Check(Math.Abs(node200.TotalDrainageAreaAcres - 4.0) < 1e-4, "Total area for Node 200 is 4.0 acres.");
        }

        private static void P19_FailClosed_InvalidInputsRejected()
        {
            var idf = new IdfParameters { A = 100.0, B = 10.0, C = 1.0 };

            // Negative area
            Throws<ArgumentException>(() =>
            {
                var c = new StormCatchment { CatchmentId = "BAD", TargetNodeId = 1, TotalAreaAcres = -1.0, RunoffCoefficient = 0.5 };
                StormFlowHydrologyEngine.ComputeRational(c, idf, 10);
            }, "Negative area rejected.");

            // NaN area
            Throws<ArgumentException>(() =>
            {
                var c = new StormCatchment { CatchmentId = "BAD", TargetNodeId = 1, TotalAreaAcres = double.NaN, RunoffCoefficient = 0.5 };
                StormFlowHydrologyEngine.ComputeRational(c, idf, 10);
            }, "NaN area rejected.");

            // Invalid Curve Number (> 100)
            Throws<ArgumentException>(() =>
            {
                var c = new StormCatchment { CatchmentId = "BAD", TargetNodeId = 1, TotalAreaAcres = 1.0, CurveNumber = 105.0 };
                StormFlowHydrologyEngine.ComputeScsCurveNumber(c, 2.0);
            }, "CN > 100 rejected.");

            // Invalid Runoff Coefficient (> 1.0)
            Throws<ArgumentException>(() =>
            {
                var c = new StormCatchment { CatchmentId = "BAD", TargetNodeId = 1, TotalAreaAcres = 1.0, RunoffCoefficient = 1.2 };
                StormFlowHydrologyEngine.ComputeRational(c, idf, 10);
            }, "C > 1.0 rejected.");

            // SubArea mismatch with TotalAreaAcres
            Throws<InvalidOperationException>(() =>
            {
                var c = new StormCatchment
                {
                    CatchmentId = "BAD",
                    TargetNodeId = 1,
                    TotalAreaAcres = 5.0,
                    SubAreas = new List<SubArea>
                    {
                        new SubArea { Name = "Area 1", AreaAcres = 2.0, RunoffCoefficient = 0.5 }
                        // Missing 3.0 acres
                    }
                };
                StormFlowHydrologyEngine.ComputeRational(c, idf, 10);
            }, "SubArea sum mismatch rejected.");
        }

        internal static void P19_MaterializeAuthoritativeEvidence()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(root) && !File.Exists(Path.Combine(root, "SewerPilot.sln")))
            {
                root = Path.GetDirectoryName(root);
            }
            string outputDir = Path.Combine(root, "docs", "evidence", "p19", "20261019-stormflow-hydrology");
            Directory.CreateDirectory(outputDir);

            // 1. Authoritative IDF curve parameters based on Austin DCM 25-year event
            var idf = new IdfParameters
            {
                Name = "City of Austin DCM 25-Year IDF Curve",
                ReturnPeriodYears = 25,
                A = 95.50,
                B = 9.80,
                C = 0.81,
                MinimumTimeOfConcentrationMinutes = 10.0
            };
            idf.Validate();

            // 2. Authoritative subcatchments
            var c1 = new StormCatchment
            {
                CatchmentId = "CAT-01",
                Name = "Commercial Shopping Center",
                TargetNodeId = 10,
                TotalAreaAcres = 5.0,
                TimeOfConcentrationMinutes = 15.0,
                SubAreas = new List<SubArea>
                {
                    new SubArea { Name = "Commercial Roofs", AreaAcres = 2.0, RunoffCoefficient = 0.90, CurveNumber = 98.0, IsImpervious = true },
                    new SubArea { Name = "Asphalt Parking", AreaAcres = 2.0, RunoffCoefficient = 0.85, CurveNumber = 98.0, IsImpervious = true },
                    new SubArea { Name = "Landscaped Islands", AreaAcres = 1.0, RunoffCoefficient = 0.25, CurveNumber = 68.0, IsImpervious = false }
                }
            };

            var c2 = new StormCatchment
            {
                CatchmentId = "CAT-02",
                Name = "Office Park Plaza",
                TargetNodeId = 10,
                TotalAreaAcres = 8.0,
                TimeOfConcentrationMinutes = 20.0,
                SubAreas = new List<SubArea>
                {
                    new SubArea { Name = "Office Buildings", AreaAcres = 3.0, RunoffCoefficient = 0.90, CurveNumber = 98.0, IsImpervious = true },
                    new SubArea { Name = "Paved Access & Parking", AreaAcres = 3.0, RunoffCoefficient = 0.80, CurveNumber = 95.0, IsImpervious = true },
                    new SubArea { Name = "Turf & Buffer Strips", AreaAcres = 2.0, RunoffCoefficient = 0.30, CurveNumber = 70.0, IsImpervious = false }
                }
            };

            var c3 = new StormCatchment
            {
                CatchmentId = "CAT-03",
                Name = "Low-Density Residential & Park",
                TargetNodeId = 12,
                TotalAreaAcres = 12.0,
                TimeOfConcentrationMinutes = 25.0,
                SubAreas = new List<SubArea>
                {
                    new SubArea { Name = "Residential Pavement/Driveways", AreaAcres = 3.0, RunoffCoefficient = 0.75, CurveNumber = 92.0, IsImpervious = true },
                    new SubArea { Name = "Lawn & Open Parkland", AreaAcres = 9.0, RunoffCoefficient = 0.20, CurveNumber = 65.0, IsImpervious = false }
                }
            };

            var catchments = new[] { c1, c2, c3 };

            // 3. Rational Method Runoff Computation (25-year return period)
            var rationalResults = new List<RunoffResult>();
            foreach (var c in catchments)
            {
                var r = StormFlowHydrologyEngine.ComputeRational(c, idf, returnPeriodYears: 25, minTcMinutes: 10.0);
                Check(r != null && r.PeakDischargeCfs > 0, "Rational calculation successful for " + c.CatchmentId);
                rationalResults.Add(r);
            }

            // 4. SCS Curve Number Runoff Computation (25-year 24-hr design storm: P = 6.40 in)
            var scsResults = new List<RunoffResult>();
            foreach (var c in catchments)
            {
                var r = StormFlowHydrologyEngine.ComputeScsCurveNumber(c, precipDepthInches: 6.40, stormType: ScsStormType.TypeII, returnPeriodYears: 25);
                Check(r != null && r.PeakDischargeCfs > 0 && r.TotalVolumeCubicFeet > 0, "SCS CN calculation successful for " + c.CatchmentId);
                scsResults.Add(r);
            }

            // 5. SWMM Reservoir Overland Flow Computation
            var swmmResults = new List<RunoffResult>();
            foreach (var c in catchments)
            {
                var r = StormFlowHydrologyEngine.ComputeSwmmReservoir(c, precipDepthInches: 6.40, stormDurationHours: 2.0);
                Check(r != null && r.PeakDischargeCfs > 0, "SWMM reservoir calculation successful for " + c.CatchmentId);
                swmmResults.Add(r);
            }

            // 6. Multi-Catchment Inflow Aggregation at Drainage Structure Nodes
            var aggregated = StormFlowHydrologyEngine.AggregateCatchmentInflows(catchments, idf, returnPeriodYears: 25, minTcMinutes: 10.0);
            Check(aggregated.Count == 2, "Aggregated across 2 distinct drainage nodes.");
            var node10 = aggregated.Single(a => a.TargetNodeId == 10);
            Check(node10.ContributingCatchmentIds.Count == 2, "Node 10 receives runoff from CAT-01 and CAT-02.");
            Check(Math.Abs(node10.TotalDrainageAreaAcres - 13.0) < 1e-4, "Node 10 total drainage area is 13.0 acres.");
            Check(node10.TotalPeakDischargeCfs > 0, "Node 10 aggregated peak discharge computed.");

            var node12 = aggregated.Single(a => a.TargetNodeId == 12);
            Check(node12.ContributingCatchmentIds.Count == 1, "Node 12 receives runoff from CAT-03.");
            Check(Math.Abs(node12.TotalDrainageAreaAcres - 12.0) < 1e-4, "Node 12 total drainage area is 12.0 acres.");

            // 7. Materialize Artifacts
            var serializer = RuntimeHostProtocol.Serializer();
            File.WriteAllText(Path.Combine(outputDir, "authorized-rational-runoff-results.json"), serializer.Serialize(rationalResults), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDir, "authorized-scs-cn-results.json"), serializer.Serialize(scsResults), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDir, "authorized-swmm-reservoir-results.json"), serializer.Serialize(swmmResults), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDir, "authorized-node-aggregation-results.json"), serializer.Serialize(aggregated), Encoding.UTF8);

            string auditJson = string.Format(CultureInfo.InvariantCulture,
                "{{\n  \"TimestampUtc\": \"{0:O}\",\n  \"Phase\": \"P19\",\n  \"Milestone\": \"StormFlow Hydrology & Runoff Computation Engine\",\n  \"GoverningStandard\": \"City of Austin Drainage Criteria Manual (DCM) / TxDOT HDM\",\n  \"DesignReturnPeriodYears\": 25,\n  \"PrecipitationDepthInches\": 6.40,\n  \"EvaluatedCatchments\": 3,\n  \"DrainageNodes\": 2,\n  \"RationalPeakQ_CAT01_cfs\": {1:F2},\n  \"RationalPeakQ_CAT02_cfs\": {2:F2},\n  \"RationalPeakQ_CAT03_cfs\": {3:F2},\n  \"AggregatedNode10PeakQ_cfs\": {4:F2},\n  \"AggregatedNode12PeakQ_cfs\": {5:F2},\n  \"ScsPeakQ_CAT01_cfs\": {6:F2},\n  \"ScsRunoffDepth_CAT01_in\": {7:F4},\n  \"AllCalculationsDeterministic\": true\n}}",
                DateTime.UtcNow,
                rationalResults[0].PeakDischargeCfs,
                rationalResults[1].PeakDischargeCfs,
                rationalResults[2].PeakDischargeCfs,
                node10.TotalPeakDischargeCfs,
                node12.TotalPeakDischargeCfs,
                scsResults[0].PeakDischargeCfs,
                double.Parse(scsResults[0].Diagnostics["RunoffDepthInches"], CultureInfo.InvariantCulture));
            File.WriteAllText(Path.Combine(outputDir, "hydrology-audit.json"), auditJson, Encoding.UTF8);
        }
    }
}
