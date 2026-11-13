using System;
using System.Collections.Generic;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum ConduitSurchargeState
    {
        GravityOpenChannel = 1,
        TransitionPressure = 2,
        SeverelySurcharged = 3
    }

    public enum NodeFloodingState
    {
        SafeFreeboard = 1,
        FreeboardExceeded = 2,
        SurfaceFlooding = 3,
        Flooded = 3
    }

    public enum JunctionLossMethod
    {
        StandardCoefficient = 1,
        DeflectionAngle = 2
    }

    public sealed class StormJunctionSpecification
    {
        public int NodeId { get; set; }
        public string Label { get; set; }
        public double RimElevationFeet { get; set; }
        public double InvertElevationFeet { get; set; }
        public double? SumpElevationFeet { get; set; }
        public double JunctionLossCoefficient { get; set; } = 0.25; // Standard straight-through manhole
        public double ExternalInflowCfs { get; set; } = 0.0;
        public bool IsOutfall { get; set; }
        public double? TailwaterElevationFeet { get; set; }

        public void Validate()
        {
            if (NodeId <= 0)
                throw new ArgumentException("NodeId must be a positive integer.", nameof(NodeId));
            if (double.IsNaN(RimElevationFeet) || double.IsInfinity(RimElevationFeet))
                throw new ArgumentException("RimElevationFeet must be a finite number.", nameof(RimElevationFeet));
            if (double.IsNaN(InvertElevationFeet) || double.IsInfinity(InvertElevationFeet))
                throw new ArgumentException("InvertElevationFeet must be a finite number.", nameof(InvertElevationFeet));
            if (RimElevationFeet <= InvertElevationFeet)
                throw new ArgumentException("Rim elevation must be strictly greater than invert elevation.");
            if (JunctionLossCoefficient < 0.0 || double.IsNaN(JunctionLossCoefficient) || double.IsInfinity(JunctionLossCoefficient))
                throw new ArgumentException("JunctionLossCoefficient cannot be negative or non-finite.", nameof(JunctionLossCoefficient));
            if (ExternalInflowCfs < 0.0 || double.IsNaN(ExternalInflowCfs) || double.IsInfinity(ExternalInflowCfs))
                throw new ArgumentException("ExternalInflowCfs cannot be negative or non-finite.", nameof(ExternalInflowCfs));
            if (TailwaterElevationFeet.HasValue && (double.IsNaN(TailwaterElevationFeet.Value) || double.IsInfinity(TailwaterElevationFeet.Value)))
                throw new ArgumentException("TailwaterElevationFeet must be a finite number when specified.", nameof(TailwaterElevationFeet));
        }
    }

    public sealed class StormVerificationCriteria
    {
        public double MinimumFreeboardFeet { get; set; } = 1.0; // Standard 1.0 ft or 2.0 ft freeboard requirement
        public double ManningN { get; set; } = 0.013;
        public double DefaultJunctionLossK { get; set; } = 0.25;
        public double MaxAllowableVelocityFps { get; set; } = 10.0;
        public double GravitationalAcceleration { get; set; } = 32.174; // ft/s^2

        public int[] AvailableDiametersInches { get; set; } =
        {
            12, 15, 18, 21, 24, 27, 30, 36, 42, 48, 54, 60, 66, 72
        };

        public void Validate()
        {
            if (MinimumFreeboardFeet < 0.0 || double.IsNaN(MinimumFreeboardFeet) || double.IsInfinity(MinimumFreeboardFeet))
                throw new ArgumentException("MinimumFreeboardFeet must be non-negative and finite.", nameof(MinimumFreeboardFeet));
            if (ManningN <= 0.0 || double.IsNaN(ManningN) || double.IsInfinity(ManningN))
                throw new ArgumentException("ManningN must be positive and finite.", nameof(ManningN));
            if (DefaultJunctionLossK < 0.0 || double.IsNaN(DefaultJunctionLossK))
                throw new ArgumentException("DefaultJunctionLossK must be non-negative.", nameof(DefaultJunctionLossK));
            if (GravitationalAcceleration <= 0.0 || double.IsNaN(GravitationalAcceleration))
                throw new ArgumentException("GravitationalAcceleration must be positive.", nameof(GravitationalAcceleration));
            if (AvailableDiametersInches == null || AvailableDiametersInches.Length == 0 || AvailableDiametersInches.Any(d => d <= 0))
                throw new ArgumentException("AvailableDiametersInches must contain positive integers.", nameof(AvailableDiametersInches));
        }
    }

    public sealed class StormConduitHydraulicState
    {
        public string ConduitId { get; set; }
        public int StartNodeId { get; set; }
        public int StopNodeId { get; set; }
        public double LengthFeet { get; set; }
        public int DiameterInches { get; set; }
        public double DiameterFeet => DiameterInches / 12.0;

        public double FlowCfs { get; set; }
        public double VelocityFps { get; set; }
        public double VelocityHeadFeet { get; set; }
        public double FrictionSlope { get; set; }
        public double FrictionHeadLossFeet { get; set; }
        public double MinorLossFeet { get; set; }
        public double TotalHeadLossFeet => Math.Round(FrictionHeadLossFeet + MinorLossFeet, 4);

        public double UpstreamInvertFeet { get; set; }
        public double DownstreamInvertFeet { get; set; }
        public double UpstreamCrownFeet => Math.Round(UpstreamInvertFeet + DiameterFeet, 4);
        public double DownstreamCrownFeet => Math.Round(DownstreamInvertFeet + DiameterFeet, 4);

        public double UpstreamHglFeet { get; set; }
        public double DownstreamHglFeet { get; set; }
        public double UpstreamEglFeet => Math.Round(UpstreamHglFeet + VelocityHeadFeet, 4);
        public double DownstreamEglFeet => Math.Round(DownstreamHglFeet + VelocityHeadFeet, 4);

        public bool IsSurcharged => UpstreamHglFeet > UpstreamCrownFeet || DownstreamHglFeet > DownstreamCrownFeet;
        public ConduitSurchargeState SurchargeState { get; set; }

        public Dictionary<string, string> Diagnostics { get; } = new Dictionary<string, string>();
    }

    public sealed class StormNodeHydraulicState
    {
        public int NodeId { get; set; }
        public string Label { get; set; }
        public double RimElevationFeet { get; set; }
        public double InvertElevationFeet { get; set; }
        public double HglFeet { get; set; }
        public double EglFeet { get; set; }
        public double DepthOfWaterFeet => Math.Max(0.0, Math.Round(HglFeet - InvertElevationFeet, 4));
        public double FreeboardFeet => Math.Round(RimElevationFeet - HglFeet, 4);
        public double RequiredFreeboardFeet { get; set; }

        public NodeFloodingState FloodingState { get; set; }
        public double OverflowDischargeCfs { get; set; }
        public bool IsOutfall { get; set; }

        public Dictionary<string, string> Diagnostics { get; } = new Dictionary<string, string>();
    }

    public sealed class BottleneckRemediationProposal
    {
        public string ConduitId { get; set; }
        public int CurrentDiameterInches { get; set; }
        public int ProposedDiameterInches { get; set; }
        public double FlowCfs { get; set; }
        public double CurrentVelocityFps { get; set; }
        public double ProposedVelocityFps { get; set; }
        public double CurrentFrictionHeadLossFeet { get; set; }
        public double ProposedFrictionHeadLossFeet { get; set; }
        public double HeadLossReductionFeet => Math.Round(CurrentFrictionHeadLossFeet - ProposedFrictionHeadLossFeet, 4);
        public double ResolvedUpstreamHglFeet { get; set; }
        public double ResolvedUpstreamFreeboardFeet { get; set; }
        public string RemediationDescription { get; set; }
    }

    public sealed class StormNetworkVerificationResult
    {
        public List<StormConduitHydraulicState> ConduitStates { get; set; } = new List<StormConduitHydraulicState>();
        public List<StormNodeHydraulicState> NodeStates { get; set; } = new List<StormNodeHydraulicState>();
        public List<BottleneckRemediationProposal> BottleneckProposals { get; set; } = new List<BottleneckRemediationProposal>();

        public double TotalHeadLossSystemFeet { get; set; }
        public double MaxHglElevationFeet { get; set; }

        public bool HasFlooding => NodeStates.Any(n => n.FloodingState == NodeFloodingState.SurfaceFlooding);
        public bool HasFreeboardViolations => NodeStates.Any(n => n.FloodingState == NodeFloodingState.FreeboardExceeded);
        public bool HasSurcharge => ConduitStates.Any(c => c.IsSurcharged);

        public List<string> ViolationsSummary { get; set; } = new List<string>();
        public Dictionary<string, string> Metadata { get; } = new Dictionary<string, string>();
    }
}
