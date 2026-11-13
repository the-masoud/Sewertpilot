using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace SewerPilot.OpenFlows.Adapter
{
    /// <summary>
    /// Configuration for multi-return-period storm analysis.
    /// </summary>
    public sealed class MultiReturnPeriodScenario
    {
        public int ReturnPeriodYears { get; set; }
        public string EventName { get; set; }
        public IdfParameters Idf { get; set; }
        public double Precip24HrInches { get; set; }
        public double FrequencyFactor { get; set; }
        public double TailwaterElevationFeet { get; set; }
    }

    /// <summary>
    /// Results summary for a single return-period evaluation in the municipal field pilot.
    /// </summary>
    public sealed class ReturnPeriodPilotEvaluation
    {
        public int ReturnPeriodYears { get; set; }
        public string EventName { get; set; }
        public double TotalRunoffVolumeCuFt { get; set; }
        public double PeakTotalDischargeCfs { get; set; }
        public double MaxGutterSpreadFeet { get; set; }
        public double TotalBypassFlowCfs { get; set; }
        public double MaxHglElevationFeet { get; set; }
        public double MinFreeboardMarginFeet { get; set; }
        public bool HasSurcharge { get; set; }
        public bool HasFreeboardViolation { get; set; }
        public bool HasSurfaceFlooding { get; set; }
        public string ComplianceVerdict { get; set; }
        public StormFlowPipelineResult PipelineResult { get; set; }
    }

    /// <summary>
    /// Master results container for the Milestone 3 Practice-Ready Municipal Field Pilot.
    /// </summary>
    public sealed class MunicipalFieldPilotResult
    {
        public string ProjectId { get; set; } = "AUSTIN-MUNI-PILOT-2026-M3";
        public string ProjectName { get; set; } = "City of Austin — Barton Springs / South Congress Municipal Stormwater District";
        public string RegulatoryStandard { get; set; } = "AUSTIN_DCM_STORM_2026 (Austin Drainage Criteria Manual / TxDOT HDM)";
        public string AuthoritativeEngineer { get; set; } = "User-authorized project variance, 2026-10-21; no professional engineer credential asserted";
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public bool Success { get; set; }
        public string OverallStatus { get; set; }

        public int CatchmentCount { get; set; }
        public double TotalDrainageAreaAcres { get; set; }
        public int InletCount { get; set; }
        public int ConduitCount { get; set; }
        public int JunctionCount { get; set; }

        public List<RunoffResult> ScsHydrologyResults { get; set; } = new List<RunoffResult>();
        public List<ReturnPeriodPilotEvaluation> ReturnPeriodEvaluations { get; set; } = new List<ReturnPeriodPilotEvaluation>();
        public List<EngineeringDecision> FormalEngineeringDecisions { get; set; } = new List<EngineeringDecision>();

        public string PlanViewImagePath { get; set; }
        public string ProfileViewImagePath { get; set; }
        public string MarkdownReportPath { get; set; }
        public string JsonAuditPath { get; set; }
        public string ConduitProfilesCsvPath { get; set; }
        public string JunctionProfilesCsvPath { get; set; }
    }

    /// <summary>
    /// Milestone 3: Practice-Ready Municipal Field Pilot Coordinator.
    /// Executes live end-to-end multi-basin municipal storm drainage runs across 10-yr, 25-yr, 50-yr, and 100-yr events,
    /// evaluates SCS Curve Number & Rational Method hydrology, complex inlet layouts with gutter carryover cascades,
    /// commercial conduit sizing, HGL/EGL backwater surcharge verification, formal P25 decision resolution,
    /// and deterministic offscreen rendering of 2D Plan Canvas and Longitudinal Profile views.
    /// </summary>
    public static class MunicipalFieldPilotPipeline
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        /// <summary>
        /// Builds the canonical practice-ready municipal field network dataset for the City of Austin.
        /// </summary>
        public static (List<StormCatchment> Catchments,
                       Dictionary<string, InletConfiguration> Inlets,
                       List<StormConduitSpecification> Conduits,
                       List<StormJunctionSpecification> Junctions,
                       List<MultiReturnPeriodScenario> Scenarios) CreateAustinMunicipalNetwork()
        {
            // ----------------------------------------------------------------------------------
            // 1. Multi-Basin Catchments with Diverse Hydrologic Soil Groups (HSG A, B, C, D)
            // ----------------------------------------------------------------------------------
            var catchments = new List<StormCatchment>
            {
                // Basin 1: Commercial core / retail plaza (HSG D - Houston Black clay, high runoff)
                new StormCatchment
                {
                    CatchmentId = "CAT-COMM-01",
                    Name = "Barton Commercial Plaza (HSG D)",
                    TargetNodeId = 101,
                    TotalAreaAcres = 5.20,
                    TimeOfConcentrationMinutes = 10.0,
                    RunoffCoefficient = 0.85,
                    CurveNumber = 92.0,
                    PercentImpervious = 85.0,
                    Slope = 0.020,
                    FlowLengthFeet = 250.0
                },
                // Basin 2: Multi-family urban residential (HSG C - Austin silty clay loam)
                new StormCatchment
                {
                    CatchmentId = "CAT-MED-02",
                    Name = "South Congress Multi-Family (HSG C)",
                    TargetNodeId = 102,
                    TotalAreaAcres = 7.50,
                    TimeOfConcentrationMinutes = 15.0,
                    RunoffCoefficient = 0.65,
                    CurveNumber = 82.0,
                    PercentImpervious = 60.0,
                    Slope = 0.018,
                    FlowLengthFeet = 380.0
                },
                // Basin 3: Single-family neighborhood (HSG B - Tinn clay loam / gravelly loam)
                new StormCatchment
                {
                    CatchmentId = "CAT-RES-03",
                    Name = "Dawson Neighborhood Residential (HSG B)",
                    TargetNodeId = 103,
                    TotalAreaAcres = 9.80,
                    TimeOfConcentrationMinutes = 20.0,
                    RunoffCoefficient = 0.45,
                    CurveNumber = 72.0,
                    PercentImpervious = 40.0,
                    Slope = 0.015,
                    FlowLengthFeet = 520.0
                },
                // Basin 4: Zilker greenbelt parkland (HSG A - deep sandy loam, high infiltration)
                new StormCatchment
                {
                    CatchmentId = "CAT-PARK-04",
                    Name = "Greenbelt Parkland & Buffer (HSG A)",
                    TargetNodeId = 104,
                    TotalAreaAcres = 6.40,
                    TimeOfConcentrationMinutes = 25.0,
                    RunoffCoefficient = 0.25,
                    CurveNumber = 58.0,
                    PercentImpervious = 10.0,
                    Slope = 0.025,
                    FlowLengthFeet = 450.0
                },
                // Basin 5: South Congress arterial boulevard corridor (100% paved impervious HSG D)
                new StormCatchment
                {
                    CatchmentId = "CAT-ROAD-05",
                    Name = "South Congress Boulevard Corridor (HSG D)",
                    TargetNodeId = 105,
                    TotalAreaAcres = 4.10,
                    TimeOfConcentrationMinutes = 10.0,
                    RunoffCoefficient = 0.90,
                    CurveNumber = 98.0,
                    PercentImpervious = 95.0,
                    Slope = 0.012,
                    FlowLengthFeet = 600.0
                }
            };

            // ----------------------------------------------------------------------------------
            // 2. Complex Inlet Layouts: Continuous Grade, Grates, Sags, and Carryover Cascades
            // ----------------------------------------------------------------------------------
            var inlets = new Dictionary<string, InletConfiguration>(StringComparer.OrdinalIgnoreCase)
            {
                // INL-01: Continuous grade 10-ft curb opening on commercial street; bypass cascades to INL-02
                {
                    "INL-01",
                    new InletConfiguration
                    {
                        InletId = "INL-01",
                        TargetNodeId = 101,
                        Type = InletType.CurbOpening,
                        Location = InletLocation.OnContinuousGrade,
                        LengthFeet = 10.0,
                        LocalDepressionInches = 2.0,
                        AllowableSpreadFeet = 8.0,
                        Gutter = new GutterCrossSection
                        {
                            CrossSlopeSx = 0.025,
                            LongitudinalSlopeSL = 0.020,
                            ManningN = 0.016,
                            GutterWidthFeet = 2.0,
                            LocalDepressionInches = 2.0
                        },
                        DownstreamInletId = "INL-02" // Cascade carryover
                    }
                },
                // INL-02: Continuous grade 15-ft curb opening; intercepts local runoff + INL-01 bypass; cascades to INL-03
                {
                    "INL-02",
                    new InletConfiguration
                    {
                        InletId = "INL-02",
                        TargetNodeId = 102,
                        Type = InletType.CurbOpening,
                        Location = InletLocation.OnContinuousGrade,
                        LengthFeet = 15.0,
                        LocalDepressionInches = 3.0,
                        AllowableSpreadFeet = 8.0,
                        Gutter = new GutterCrossSection
                        {
                            CrossSlopeSx = 0.025,
                            LongitudinalSlopeSL = 0.015,
                            ManningN = 0.016,
                            GutterWidthFeet = 2.0,
                            LocalDepressionInches = 3.0
                        },
                        DownstreamInletId = "INL-03" // Cascade carryover
                    }
                },
                // INL-03: Combination curb + grate inlet on grade; high efficiency; remaining bypass cascades to sag INL-04
                {
                    "INL-03",
                    new InletConfiguration
                    {
                        InletId = "INL-03",
                        TargetNodeId = 103,
                        Type = InletType.Combination,
                        Location = InletLocation.OnContinuousGrade,
                        LengthFeet = 10.0,
                        WidthFeet = 2.0,
                        Grate = GrateType.P50,
                        LocalDepressionInches = 2.0,
                        AllowableSpreadFeet = 8.0,
                        Gutter = new GutterCrossSection
                        {
                            CrossSlopeSx = 0.020,
                            LongitudinalSlopeSL = 0.012,
                            ManningN = 0.016,
                            GutterWidthFeet = 2.0,
                            LocalDepressionInches = 2.0
                        },
                        DownstreamInletId = "INL-04" // Cascade carryover to sag
                    }
                },
                // INL-04: Sag/Sump curb opening in roadway low point (100% ponding interception; zero carryover)
                {
                    "INL-04",
                    new InletConfiguration
                    {
                        InletId = "INL-04",
                        TargetNodeId = 104,
                        Type = InletType.CurbOpening,
                        Location = InletLocation.InSag,
                        LengthFeet = 15.0,
                        CurbOpeningHeightInches = 6.0,
                        AllowableSpreadFeet = 8.0,
                        Gutter = new GutterCrossSection
                        {
                            CrossSlopeSx = 0.030,
                            LongitudinalSlopeSL = 0.010,
                            ManningN = 0.016
                        }
                    }
                },
                // INL-05: Boulevard cul-de-sac sag grate inlet (100% ponding interception)
                {
                    "INL-05",
                    new InletConfiguration
                    {
                        InletId = "INL-05",
                        TargetNodeId = 105,
                        Type = InletType.Grate,
                        Location = InletLocation.InSag,
                        LengthFeet = 4.0,
                        WidthFeet = 3.0,
                        Grate = GrateType.CurvedVane,
                        CloggingFactor = 0.50,
                        AllowableSpreadFeet = 8.0,
                        Gutter = new GutterCrossSection
                        {
                            CrossSlopeSx = 0.025,
                            LongitudinalSlopeSL = 0.010,
                            ManningN = 0.016
                        }
                    }
                }
            };

            // ----------------------------------------------------------------------------------
            // 3. Multi-Junction Municipal Network Topology & Spatial Layout
            // ----------------------------------------------------------------------------------
            var junctions = new List<StormJunctionSpecification>
            {
                new StormJunctionSpecification
                {
                    NodeId = 101,
                    Label = "MH-101 (Barton Commercial)",
                    RimElevationFeet = 650.00,
                    InvertElevationFeet = 642.00,
                    SumpElevationFeet = 640.50,
                    JunctionLossCoefficient = 0.25
                },
                new StormJunctionSpecification
                {
                    NodeId = 102,
                    Label = "MH-102 (Congress Multi-Family)",
                    RimElevationFeet = 646.00,
                    InvertElevationFeet = 638.50,
                    SumpElevationFeet = 637.00,
                    JunctionLossCoefficient = 0.30
                },
                new StormJunctionSpecification
                {
                    NodeId = 103,
                    Label = "MH-103 (Dawson Residential)",
                    RimElevationFeet = 642.50,
                    InvertElevationFeet = 635.00,
                    SumpElevationFeet = 633.50,
                    JunctionLossCoefficient = 0.30
                },
                new StormJunctionSpecification
                {
                    NodeId = 104,
                    Label = "MH-104 (Low Sag Junction)",
                    RimElevationFeet = 638.00,
                    InvertElevationFeet = 630.50,
                    SumpElevationFeet = 629.00,
                    JunctionLossCoefficient = 0.45
                },
                new StormJunctionSpecification
                {
                    NodeId = 105,
                    Label = "MH-105 (Boulevard Cul-de-Sac)",
                    RimElevationFeet = 640.00,
                    InvertElevationFeet = 633.00,
                    SumpElevationFeet = 631.50,
                    JunctionLossCoefficient = 0.25
                },
                new StormJunctionSpecification
                {
                    NodeId = 106,
                    Label = "MH-106 (Trunk Main Confluence)",
                    RimElevationFeet = 634.00,
                    InvertElevationFeet = 626.00,
                    SumpElevationFeet = 624.50,
                    JunctionLossCoefficient = 0.35
                },
                new StormJunctionSpecification
                {
                    NodeId = 901,
                    Label = "OUTFALL-901 (Barton Creek Discharge)",
                    RimElevationFeet = 626.00,
                    InvertElevationFeet = 620.00,
                    SumpElevationFeet = 620.00,
                    IsOutfall = true,
                    TailwaterElevationFeet = 621.50 // Nominal 10-yr tailwater
                }
            };

            // ----------------------------------------------------------------------------------
            // 4. Conduit Specifications
            // ----------------------------------------------------------------------------------
            var conduits = new List<StormConduitSpecification>
            {
                new StormConduitSpecification
                {
                    ConduitId = "P-101-102",
                    StartNodeId = 101,
                    StopNodeId = 102,
                    LengthFeet = 220.0,
                    GroundElevationStartFeet = 650.00,
                    GroundElevationStopFeet = 646.00,
                    FixedSlope = 0.0159
                },
                new StormConduitSpecification
                {
                    ConduitId = "P-102-103",
                    StartNodeId = 102,
                    StopNodeId = 103,
                    LengthFeet = 280.0,
                    GroundElevationStartFeet = 646.00,
                    GroundElevationStopFeet = 642.50,
                    FixedSlope = 0.0125
                },
                new StormConduitSpecification
                {
                    ConduitId = "P-103-104",
                    StartNodeId = 103,
                    StopNodeId = 104,
                    LengthFeet = 250.0,
                    GroundElevationStartFeet = 642.50,
                    GroundElevationStopFeet = 638.00,
                    FixedSlope = 0.0180
                },
                new StormConduitSpecification
                {
                    ConduitId = "P-105-104",
                    StartNodeId = 105,
                    StopNodeId = 104,
                    LengthFeet = 190.0,
                    GroundElevationStartFeet = 640.00,
                    GroundElevationStopFeet = 638.00,
                    FixedSlope = 0.0132
                },
                new StormConduitSpecification
                {
                    ConduitId = "P-104-106",
                    StartNodeId = 104,
                    StopNodeId = 106,
                    LengthFeet = 310.0,
                    GroundElevationStartFeet = 638.00,
                    GroundElevationStopFeet = 634.00,
                    FixedSlope = 0.0145
                },
                new StormConduitSpecification
                {
                    ConduitId = "P-106-901",
                    StartNodeId = 106,
                    StopNodeId = 901,
                    LengthFeet = 240.0,
                    GroundElevationStartFeet = 634.00,
                    GroundElevationStopFeet = 626.00,
                    FixedSlope = 0.0250
                }
            };

            // ----------------------------------------------------------------------------------
            // 5. Multi-Return-Period Rainfall Scenarios (Austin DCM Table 2-2)
            // ----------------------------------------------------------------------------------
            var scenarios = new List<MultiReturnPeriodScenario>
            {
                new MultiReturnPeriodScenario
                {
                    ReturnPeriodYears = 10,
                    EventName = "10-Year Municipal Minor Storm (Nominal Design Event)",
                    Idf = new IdfParameters { Name = "Austin DCM 10-Yr", ReturnPeriodYears = 10, A = 56.40, B = 10.80, C = 0.79 },
                    Precip24HrInches = 5.20,
                    FrequencyFactor = 1.00,
                    TailwaterElevationFeet = 621.50
                },
                new MultiReturnPeriodScenario
                {
                    ReturnPeriodYears = 25,
                    EventName = "25-Year Urban Commercial Storm (Conduit Sizing Event)",
                    Idf = new IdfParameters { Name = "Austin DCM 25-Yr", ReturnPeriodYears = 25, A = 66.80, B = 11.20, C = 0.79 },
                    Precip24HrInches = 6.40,
                    FrequencyFactor = 1.10,
                    TailwaterElevationFeet = 622.80
                },
                new MultiReturnPeriodScenario
                {
                    ReturnPeriodYears = 50,
                    EventName = "50-Year Major Drainage Check Event",
                    Idf = new IdfParameters { Name = "Austin DCM 50-Yr", ReturnPeriodYears = 50, A = 78.20, B = 11.80, C = 0.80 },
                    Precip24HrInches = 7.60,
                    FrequencyFactor = 1.20,
                    TailwaterElevationFeet = 624.00
                },
                new MultiReturnPeriodScenario
                {
                    ReturnPeriodYears = 100,
                    EventName = "100-Year Extreme Flood & Surface Inundation Event",
                    Idf = new IdfParameters { Name = "Austin DCM 100-Yr", ReturnPeriodYears = 100, A = 89.50, B = 12.00, C = 0.80 },
                    Precip24HrInches = 9.20,
                    FrequencyFactor = 1.25,
                    TailwaterElevationFeet = 625.50
                }
            };

            return (catchments, inlets, conduits, junctions, scenarios);
        }

        /// <summary>
        /// Executes the complete live municipal field pilot run across all 4 recurrence intervals.
        /// </summary>
        public static MunicipalFieldPilotResult ExecutePilot(string outputDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SewerPilot.sln")) && Directory.GetParent(dir) != null)
                {
                    dir = Directory.GetParent(dir).FullName;
                }
                if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "SewerPilot.sln")))
                {
                    outputDirectory = Path.Combine(dir, "docs", "evidence", "m3-field-pilot");
                }
                else
                {
                    outputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\docs\evidence\m3-field-pilot");
                }
            }
            outputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outputDirectory);

            var network = CreateAustinMunicipalNetwork();
            var catchments = network.Catchments;
            var inlets = network.Inlets;
            var conduits = network.Conduits;
            var junctions = network.Junctions;
            var scenarios = network.Scenarios;

            var pilotResult = new MunicipalFieldPilotResult
            {
                CatchmentCount = catchments.Count,
                TotalDrainageAreaAcres = Math.Round(catchments.Sum(c => c.TotalAreaAcres), 2),
                InletCount = inlets.Count,
                ConduitCount = conduits.Count,
                JunctionCount = junctions.Count
            };

            // ----------------------------------------------------------------------------------
            // Part A: Soil Hydrologic Group & SCS Curve Number Analysis
            // ----------------------------------------------------------------------------------
            // Evaluate 24-hr SCS TR-55 hydrographs for each catchment using 25-year rainfall (6.40 in)
            double designP24 = 6.40;
            foreach (var c in catchments)
            {
                var scsRunoff = StormFlowHydrologyEngine.ComputeScsCurveNumber(c, designP24, ScsStormType.TypeII, 25);
                pilotResult.ScsHydrologyResults.Add(scsRunoff);
            }

            // ----------------------------------------------------------------------------------
            // Part B: Multi-Return-Period Execution Sweep (10-yr, 25-yr, 50-yr, 100-yr)
            // ----------------------------------------------------------------------------------
            StormFlowPipelineResult governing25YrResult = null;
            StormFlowPipelineResult governing100YrResult = null;

            foreach (var scenario in scenarios)
            {
                // Update junction tailwater for this recurrence interval
                var scenarioJunctions = junctions.Select(j => new StormJunctionSpecification
                {
                    NodeId = j.NodeId,
                    Label = j.Label,
                    RimElevationFeet = j.RimElevationFeet,
                    InvertElevationFeet = j.InvertElevationFeet,
                    SumpElevationFeet = j.SumpElevationFeet,
                    JunctionLossCoefficient = j.JunctionLossCoefficient,
                    IsOutfall = j.IsOutfall,
                    TailwaterElevationFeet = j.IsOutfall ? scenario.TailwaterElevationFeet : (double?)null
                }).ToList();

                var pipelineInput = new StormFlowPipelineInput
                {
                    ProjectName = "Austin Barton Springs Municipal Pilot (" + scenario.ReturnPeriodYears + "-Year)",
                    ProjectId = "AUSTIN-M3-RP-" + scenario.ReturnPeriodYears,
                    ReturnPeriodYears = scenario.ReturnPeriodYears,
                    Idf = scenario.Idf,
                    Catchments = catchments,
                    Inlets = inlets,
                    ConduitSpecifications = conduits,
                    JunctionSpecifications = scenarioJunctions,
                    ConduitConstraints = new StormConduitConstraint
                    {
                        ManningN = 0.013,
                        MinimumVelocityFps = 3.0,
                        MaximumVelocityFps = 15.0,
                        MinimumCoverFeet = 3.5,
                        MaxCapacityUtilizationRatio = 0.85
                    },
                    VerificationCriteria = new StormVerificationCriteria
                    {
                        MinimumFreeboardFeet = 1.0,
                        ManningN = 0.013,
                        MaxAllowableVelocityFps = 15.0
                    }
                };

                var pipelineRes = StormFlowEndToEndPipeline.Execute(pipelineInput);

                if (scenario.ReturnPeriodYears == 25) governing25YrResult = pipelineRes;
                if (scenario.ReturnPeriodYears == 100) governing100YrResult = pipelineRes;

                double maxSpread = pipelineRes.InletRoutingResult.InletResults.Max(i => i.GutterSpreadFeet);
                double totalBypass = pipelineRes.InletRoutingResult.TotalSystemBypassFlowCfs;
                double maxHgl = pipelineRes.VerificationResult.MaxHglElevationFeet;
                double minFreeboard = pipelineRes.VerificationResult.NodeStates.Min(n => n.FreeboardFeet);

                var eval = new ReturnPeriodPilotEvaluation
                {
                    ReturnPeriodYears = scenario.ReturnPeriodYears,
                    EventName = scenario.EventName,
                    TotalRunoffVolumeCuFt = Math.Round(pipelineRes.HydrologyResults.Sum(h => h.TotalVolumeCubicFeet), 1),
                    PeakTotalDischargeCfs = Math.Round(pipelineRes.HydrologyResults.Sum(h => h.PeakDischargeCfs), 2),
                    MaxGutterSpreadFeet = Math.Round(maxSpread, 2),
                    TotalBypassFlowCfs = Math.Round(totalBypass, 2),
                    MaxHglElevationFeet = Math.Round(maxHgl, 2),
                    MinFreeboardMarginFeet = Math.Round(minFreeboard, 2),
                    HasSurcharge = pipelineRes.VerificationResult.HasSurcharge,
                    HasFreeboardViolation = pipelineRes.VerificationResult.HasFreeboardViolations,
                    HasSurfaceFlooding = pipelineRes.VerificationResult.HasFlooding,
                    ComplianceVerdict = pipelineRes.OverallVerdict,
                    PipelineResult = pipelineRes
                };

                pilotResult.ReturnPeriodEvaluations.Add(eval);
            }

            // ----------------------------------------------------------------------------------
            // Part C: Phase P25 Formal Engineering Decision Integration
            // ----------------------------------------------------------------------------------
            var decisionEngine = new EngineeringDecisionWorkflowEngine();
            var register = new EngineeringDecisionRegister { ProjectId = pilotResult.ProjectId };

            // Calculated findings create pending decisions; the application cannot sign for an engineer.
            var critical = pilotResult.ReturnPeriodEvaluations.Single(e => e.ReturnPeriodYears == 100);
            var spread = decisionEngine.CreateGenericDecision(
                "DEC-AUSTIN-M3-001", pilotResult.ProjectId, "StormFlow", EngineeringDecisionCategory.InletBypassPolicy,
                new[] { "INL-01", "INL-02", "INL-03" },
                "Review calculated gutter spread and carryover against explicit project criteria.",
                "No human approval is supplied by this generated fixture.",
                new Dictionary<string,string> { { "MeasuredMaximumSpreadFeet", critical.MaxGutterSpreadFeet.ToString("R", CultureInfo.InvariantCulture) } },
                new[] { new EngineeringDecisionAlternative { AlternativeId = "REVIEW", Title = "Engineer review required", Description = "Supply explicit spread criterion and disposition." }, new EngineeringDecisionAlternative { AlternativeId = "REDESIGN", Title = "Recalculate inlet design", Description = "Evaluate an explicit engineer-selected candidate." } });
            var freeboard = decisionEngine.CreateGenericDecision(
                "DEC-AUSTIN-M3-002", pilotResult.ProjectId, "StormFlow", EngineeringDecisionCategory.BottleneckCapacityRemediation,
                new[] { "P-104-106", "MH-104", "MH-106" },
                "100-year freeboard requires remediation or an authentic engineer-approved variance.",
                "The previous 42-inch upsize narrative was not a measured post-remediation result or human sign-off.",
                new Dictionary<string,string> { { "MeasuredMinimumFreeboardFeet", critical.MinFreeboardMarginFeet.ToString("R", CultureInfo.InvariantCulture) }, { "RequiredFreeboardFeet", "1.0" } },
                new[] { new EngineeringDecisionAlternative { AlternativeId = "CANDIDATE-42IN", Title = "Evaluate proposed 42-inch upsize", Description = "Unapproved candidate; recalculate before claiming performance." }, new EngineeringDecisionAlternative { AlternativeId = "REVIEW-VARIANCE", Title = "Review measured freeboard variance", Description = "Requires explicit project disposition." } });
            // Explicit user instruction dated 2026-10-21 authorizes this measured fixture variance only.
            // Do not extend the authorization to a changed design or an unmeasured recurrence result.
            if (critical.HasFreeboardViolation && Math.Abs(critical.MinFreeboardMarginFeet - 0.50) < 0.005 && !critical.HasSurfaceFlooding)
            {
                freeboard.Alternatives.Add(new EngineeringDecisionAlternative { AlternativeId = "USER-VARIANCE-20261021",
                    Title = "Accept documented 0.50 ft freeboard shortfall", Description = "User explicitly accepts the measured 100-year 0.50 ft freeboard versus the unchanged 1.0 ft criterion as a non-blocking minor variance." });
                decisionEngine.ResolveDecision(freeboard, "USER-VARIANCE-20261021",
                    "User instruction 2026-10-21: CONDITIONAL PASS / ACCEPTED WITH DOCUMENTED VARIANCE. The measured 0.50 ft freeboard shortfall is non-blocking for pipeline/project closeout. No hydraulic remediation or professional sign-off is implied.",
                    new EngineeringDecisionMaker { Name = "Project user", ProfessionalTitle = "User-supplied project disposition", IdentityIdentifier = "USER-DIRECTIVE-20261021", ActionType = "Approved conditionally" },
                    new EngineeringDecisionImpact { HydraulicImpact = "Calculated result unchanged: 0.50 ft available, 1.0 ft criterion, 0.50 ft shortfall at the 100-year event.",
                        ComplianceStatus = EngineeringComplianceStatus.CompliantWithVariance });
            }
            register.AddOrUpdate(spread);
            register.AddOrUpdate(freeboard);
            pilotResult.FormalEngineeringDecisions.Add(spread);
            pilotResult.FormalEngineeringDecisions.Add(freeboard);

            // ----------------------------------------------------------------------------------
            // Part D: 2D Plan Canvas & Profile Viewer Offline Rendering
            // ----------------------------------------------------------------------------------
            // 1. Map to canonical NetworkReadResult with realistic CAD coordinates
            var canonicalRead = new NetworkReadResult
            {
                Phase = "M3-FIELD-PILOT",
                Status = "MUNICIPAL_PILOT_VERIFIED",
                ScenarioContext = new NetworkScenarioContext
                {
                    ActiveScenarioId = 1,
                    ActiveScenarioLabel = "Austin Barton Springs Municipal District (25-Yr Design)",
                    TopologyAlternativeId = 1,
                    PhysicalAlternativeId = 1,
                    ActiveTopologyAlternativeId = 1
                }
            };

            // Assign spatial coordinates (Austin Central State Plane NAD83 representation)
            var coords = new Dictionary<int, (double X, double Y)>
            {
                { 101, (3105200.0, 10068400.0) },
                { 102, (3105420.0, 10068400.0) },
                { 103, (3105700.0, 10068400.0) },
                { 104, (3105950.0, 10068400.0) },
                { 105, (3105950.0, 10068590.0) },
                { 106, (3106260.0, 10068400.0) },
                { 901, (3106500.0, 10068400.0) }
            };

            foreach (var j in junctions)
            {
                var pt = coords[j.NodeId];
                canonicalRead.Structures.Add(new StructureReadDto
                {
                    Id = j.NodeId,
                    Label = j.Label,
                    RuntimeCategory = j.IsOutfall ? "Outfall" : "Manhole",
                    ElementTypeId = j.IsOutfall ? 55 : 52,
                    X = pt.X,
                    Y = pt.Y,
                    RimElevation = j.RimElevationFeet,
                    GroundElevation = j.RimElevationFeet,
                    InvertElevation = j.InvertElevationFeet
                });
            }

            int condIdSeq = 2001;
            foreach (var c in conduits)
            {
                var pStart = coords[c.StartNodeId];
                var pStop = coords[c.StopNodeId];
                canonicalRead.Conduits.Add(new ConduitReadDto
                {
                    Id = condIdSeq++,
                    Label = c.ConduitId,
                    StartNodeId = c.StartNodeId,
                    StopNodeId = c.StopNodeId,
                    Diameter = 2.0, // Initial diameter representation
                    Length = c.LengthFeet,
                    StartInvert = junctions.First(j => j.NodeId == c.StartNodeId).InvertElevationFeet,
                    StopInvert = junctions.First(j => j.NodeId == c.StopNodeId).InvertElevationFeet,
                    Shape = "Circular",
                    ConduitType = "Conduit",
                    Material = "Concrete",
                    ManningsN = 0.013,
                    GeometryPoints = new List<Point2DDto>
                    {
                        new Point2DDto(pStart.X, pStart.Y),
                        new Point2DDto(pStop.X, pStop.Y)
                    }
                });
            }

            // Render 2D Plan Canvas offscreen
            string planImgPath = Path.Combine(outputDirectory, "canvas-plan-view.png");
            using (var canvas = new NetworkPlanCanvasControl())
            {
                canvas.Width = 1400;
                canvas.Height = 900;
                canvas.LoadNetwork(canonicalRead, governing25YrResult?.ReportContext, pilotResult.FormalEngineeringDecisions);
                using (var bmp = canvas.RenderToBitmap(1400, 900))
                {
                    AdapterWriteBinding.EncodeImage(bmp, planImgPath, ImageFormat.Png);
                }
            }
            pilotResult.PlanViewImagePath = planImgPath;

            // Render Longitudinal Profile Viewer offscreen
            string profileImgPath = Path.Combine(outputDirectory, "profile-view.png");
            using (var profileViewer = new HydraulicProfileViewerControl())
            {
                profileViewer.Width = 1600;
                profileViewer.Height = 900;
                if (governing25YrResult?.ReportContext != null)
                {
                    profileViewer.LoadResults(governing25YrResult.ReportContext);
                    using (var bmp = profileViewer.RenderOffscreen(1600, 900))
                    {
                        AdapterWriteBinding.EncodeImage(bmp, profileImgPath, ImageFormat.Png);
                    }
                }
            }
            pilotResult.ProfileViewImagePath = profileImgPath;

            // ----------------------------------------------------------------------------------
            // Part E: Export Authoritative Reports and Data Packages
            // ----------------------------------------------------------------------------------
            // 1. Export 25-Year Audit Package (CSV profiles, JSON audit)
            governing25YrResult?.ExportAuditPackage(outputDirectory);

            pilotResult.ConduitProfilesCsvPath = Path.Combine(outputDirectory, "Conduit_Hydraulic_Profiles.csv");
            pilotResult.JunctionProfilesCsvPath = Path.Combine(outputDirectory, "Junction_Hydraulic_Profiles.csv");
            pilotResult.JsonAuditPath = Path.Combine(outputDirectory, "field-pilot-results.json");
            pilotResult.MarkdownReportPath = Path.Combine(outputDirectory, "municipal-field-pilot-report.md");

            pilotResult.Success = pilotResult.ReturnPeriodEvaluations.All(e => !e.HasSurfaceFlooding &&
                (!e.HasFreeboardViolation || (e.ReturnPeriodYears == 100 && Math.Abs(e.MinFreeboardMarginFeet - 0.50) < 0.005)));
            pilotResult.OverallStatus = pilotResult.Success ? "CONDITIONAL PASS / ACCEPTED WITH DOCUMENTED VARIANCE" : "ENGINEERING_REMEDIATION_REQUIRED";

            // 2. Export Master JSON
            string masterJson = Serializer.Serialize(pilotResult);
            File.WriteAllText(pilotResult.JsonAuditPath, masterJson);

            // 3. Export Decisions JSON
            string decisionsJson = Serializer.Serialize(pilotResult.FormalEngineeringDecisions);
            File.WriteAllText(Path.Combine(outputDirectory, "engineering-decisions.json"), decisionsJson);

            // 4. Generate Comprehensive Milestone 3 Markdown Report
            string mdReport = GenerateMasterPilotReport(pilotResult);
            File.WriteAllText(pilotResult.MarkdownReportPath, mdReport);


            return pilotResult;
        }

        private static string GenerateMasterPilotReport(MunicipalFieldPilotResult r)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# SewerPilot — Milestone 3: Practice-Ready Municipal Field Pilot Report");
            sb.AppendLine();
            sb.AppendLine("**Project Name:** " + r.ProjectName);
            sb.AppendLine("**Project ID:** `" + r.ProjectId + "`");
            sb.AppendLine("**Date:** " + r.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));
            sb.AppendLine("**Authoritative Engineer:** `" + r.AuthoritativeEngineer + "`");
            sb.AppendLine("**Governing Standard:** `" + r.RegulatoryStandard + "`");
            sb.AppendLine("**Overall Milestone Verdict:** `" + r.OverallStatus + "`");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 1. Executive Summary");
            sb.AppendLine();
            sb.AppendLine("This generated Austin-labelled demonstration evaluates a 33.0-acre fixture. It is not evidence of municipal adoption, engineer approval, or native Bentley hydraulic acceptance. Calculated violations and pending decisions remain open. The workflow exercises:");
            sb.AppendLine();
            sb.AppendLine("1. **Multi-Basin Soil Hydrology:** Simultaneous Rational Method ($Q = C_f \\cdot C \\cdot I \\cdot A$) and NRCS/SCS TR-55 Curve Number hydrographs across 5 diverse Hydrologic Soil Groups (**HSG A, B, C, D**).");
            sb.AppendLine("2. **Inlet Interception & Gutter Hydraulics:** Mixed continuous-grade curb openings, combination inlets, and depressed sag inlets with full street gutter spread tracking and downstream carryover cascades.");
            sb.AppendLine("3. **Commercial Conduit Sizing:** Circular Manning sizing (15\" to 42\" RCP), soffit/crown alignment, partial-flow angle ($\\theta$) checks, and velocity validation ($3.0 \\le V \\le 15.0\\text{ fps}$).");
            sb.AppendLine("4. **Hydraulic Grade Line (HGL) Sweep:** Multi-event backwater profiles across 10-yr, 25-yr, 50-yr, and 100-yr return periods starting from Barton Creek tailwater.");
            sb.AppendLine("5. **Phase P25 Engineering Decision Resolution:** Pending lifecycle decisions for inlet carryover and 100-yr freeboard remediation; no generated human sign-off.");
            sb.AppendLine("6. **Visual Inspection & 2D Canvas Synchronization:** Deterministic offscreen headless rendering of the interactive 2D Plan Canvas and Longitudinal Profile Viewer.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 2. Multi-Basin Soil Hydrologic Group Evaluation");
            sb.AppendLine();
            sb.AppendLine("| Catchment ID | Name | Soil Group (HSG) | Area (ac) | Runoff Coeff ($C$) | Curve Number ($CN$) | $T_c$ (min) | 25-Yr Peak $Q$ (cfs) | 25-Yr Volume (cu ft) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var h in r.ScsHydrologyResults)
            {
                var eval25 = r.ReturnPeriodEvaluations.First(e => e.ReturnPeriodYears == 25);
                var rat = eval25.PipelineResult.HydrologyResults.First(hr => hr.CatchmentId == h.CatchmentId);
                string soil = h.CatchmentId.Contains("COMM") ? "HSG D" :
                              h.CatchmentId.Contains("MED") ? "HSG C" :
                              h.CatchmentId.Contains("RES") ? "HSG B" :
                              h.CatchmentId.Contains("PARK") ? "HSG A" : "HSG D";

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| `{0}` | {1} | **{2}** | {3:F2} | {4:F2} | {5:F0} | {6:F1} | **{7:F2}** | {8:N0} |",
                    h.CatchmentId, h.CatchmentId, soil, h.AreaAcres, rat.EffectiveRunoffParameter, h.EffectiveRunoffParameter,
                    h.TimeOfConcentrationMinutes, rat.PeakDischargeCfs, h.TotalVolumeCubicFeet));
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 3. Multi-Return-Period Hydraulic Performance Sweep");
            sb.AppendLine();
            sb.AppendLine("| Storm Event | Return Period | Peak Runoff (cfs) | Max Gutter Spread (ft) | Total Bypass (cfs) | Max HGL (ft) | Min Freeboard (ft) | Compliance Status |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var ev in r.ReturnPeriodEvaluations)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| {0} | **{1}-Year** | {2:F2} | {3:F2} | {4:F2} | {5:F2} | **{6:F2}** | `{7}` |",
                    ev.EventName, ev.ReturnPeriodYears, ev.PeakTotalDischargeCfs, ev.MaxGutterSpreadFeet,
                    ev.TotalBypassFlowCfs, ev.MaxHglElevationFeet, ev.MinFreeboardMarginFeet, ev.ComplianceVerdict));
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 4. Phase P25 Formal Engineering Decision Register");
            sb.AppendLine();
            foreach (var d in r.FormalEngineeringDecisions)
            {
                sb.AppendLine("### Decision: `" + d.DecisionId + "` — " + d.ProblemStatement);
                sb.AppendLine("- **Status:** `" + d.Status + "`");
                sb.AppendLine("- **Category:** `" + d.Category + "`");
                sb.AppendLine("- **Affected Elements:** " + string.Join(", ", d.SubjectElements));
                sb.AppendLine("- **Selected Alternative:** `" + (d.Resolution?.SelectedAlternativeId ?? "PENDING") + "`");
                sb.AppendLine("- **Engineering Justification:** " + (d.Resolution?.EngineeringJustification ?? "No approved disposition supplied."));
                sb.AppendLine("- **Decision Maker:** `" + (d.Resolution?.DecisionMaker?.Name ?? "None") + "`");
                sb.AppendLine("- **Hydraulic Impact:** " + (d.Resolution?.ResultingImpact?.HydraulicImpact ?? "No physical change approved or applied."));
                sb.AppendLine("- **Audit Fingerprint:** `" + d.LineageFingerprint + "`");
                sb.AppendLine();
            }
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 5. Visual Inspection & Graphical Deliverables");
            sb.AppendLine();
            sb.AppendLine("- **Interactive 2D Plan Canvas Rendering (1400x900):** `canvas-plan-view.png`");
            sb.AppendLine("- **Longitudinal Profile Viewer Rendering (1600x900):** `profile-view.png`");
            sb.AppendLine("- **Conduit Hydraulic Profiles:** `Conduit_Hydraulic_Profiles.csv`");
            sb.AppendLine("- **Junction Hydraulic Profiles:** `Junction_Hydraulic_Profiles.csv`");
            sb.AppendLine("- **Full Field Pilot Audit Package:** `field-pilot-results.json`");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 6. Regulatory Compliance & Final Milestone Disposition");
            sb.AppendLine();
            sb.AppendLine("**Final Verdict: `" + r.OverallStatus + "`**  ");
            sb.AppendLine("The user directive dated 2026-10-21 accepts the measured 100-year 0.50 ft freeboard shortfall as a non-blocking minor variance. The 1.0 ft criterion and calculated results remain unchanged. This is a user project disposition, not a professional engineer signature, municipal adoption, or universal compliance certification. Other pending decisions remain visible in the register.");
            return sb.ToString();
        }
    }
}
