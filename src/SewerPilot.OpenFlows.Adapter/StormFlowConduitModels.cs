using System;
using System.Collections.Generic;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum VelocityComplianceStatus
    {
        Pass = 1,
        LowVelocity = 2,
        ExcessiveVelocity = 3
    }

    public enum CoverComplianceStatus
    {
        Pass = 1,
        InsufficientCover = 2
    }

    public enum CapacityComplianceStatus
    {
        Pass = 1,
        Surcharged = 2
    }

    public sealed class StormConduitConstraint
    {
        public double MinimumSlope { get; set; } = 0.002; // 0.20% minimum
        public double MaximumSlope { get; set; } = 0.100; // 10.0% maximum
        public double MinimumVelocityFps { get; set; } = 2.5; // Self-cleansing velocity
        public double MaximumVelocityFps { get; set; } = 10.0; // Non-erosive velocity
        public double MinimumCoverFeet { get; set; } = 3.0; // Minimum earth cover
        public double MaxCapacityUtilizationRatio { get; set; } = 1.00; // 100% full or 85% full design rule
        public double ManningN { get; set; } = 0.013; // Concrete pipe standard

        // Standard commercially available circular pipe diameters (inches)
        public int[] AvailableDiametersInches { get; set; } =
        {
            12, 15, 18, 21, 24, 27, 30, 36, 42, 48, 54, 60, 66, 72
        };

        public void Validate()
        {
            if (MinimumSlope <= 0.0 || double.IsNaN(MinimumSlope) || double.IsInfinity(MinimumSlope))
                throw new ArgumentException("Minimum slope must be a positive finite number.", nameof(MinimumSlope));
            if (MaximumSlope <= MinimumSlope || double.IsNaN(MaximumSlope) || double.IsInfinity(MaximumSlope))
                throw new ArgumentException("Maximum slope must exceed minimum slope.", nameof(MaximumSlope));
            if (MinimumVelocityFps <= 0.0 || double.IsNaN(MinimumVelocityFps))
                throw new ArgumentException("Minimum velocity must be positive.", nameof(MinimumVelocityFps));
            if (MaximumVelocityFps <= MinimumVelocityFps || double.IsNaN(MaximumVelocityFps))
                throw new ArgumentException("Maximum velocity must exceed minimum velocity.", nameof(MaximumVelocityFps));
            if (MinimumCoverFeet < 0.0 || double.IsNaN(MinimumCoverFeet))
                throw new ArgumentException("Minimum cover cannot be negative.", nameof(MinimumCoverFeet));
            if (MaxCapacityUtilizationRatio <= 0.0 || MaxCapacityUtilizationRatio > 1.20)
                throw new ArgumentException("Max capacity utilization ratio must be in range (0.0, 1.20].", nameof(MaxCapacityUtilizationRatio));
            if (ManningN <= 0.0 || double.IsNaN(ManningN))
                throw new ArgumentException("Manning's n must be positive.", nameof(ManningN));
            if (AvailableDiametersInches == null || AvailableDiametersInches.Length == 0 || AvailableDiametersInches.Any(d => d <= 0))
                throw new ArgumentException("Available diameters must contain positive integers.", nameof(AvailableDiametersInches));
        }
    }

    public sealed class StormConduitSpecification
    {
        public string ConduitId { get; set; }
        public int StartNodeId { get; set; }
        public int StopNodeId { get; set; }
        public double LengthFeet { get; set; }
        public double DesignFlowCfs { get; set; }
        public double GroundElevationStartFeet { get; set; }
        public double GroundElevationStopFeet { get; set; }

        public double? FixedSlope { get; set; } // Optional fixed slope constraint
        public double? FixedUpstreamInvertFeet { get; set; }
        public double? FixedDownstreamInvertFeet { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ConduitId))
                throw new ArgumentException("ConduitId cannot be null or empty.", nameof(ConduitId));
            if (StartNodeId <= 0 || StopNodeId <= 0)
                throw new ArgumentException("Node IDs must be positive integers.");
            if (StartNodeId == StopNodeId)
                throw new ArgumentException("StartNodeId and StopNodeId cannot be identical.");
            if (LengthFeet <= 0.0 || double.IsNaN(LengthFeet) || double.IsInfinity(LengthFeet))
                throw new ArgumentException("Conduit length must be a positive finite number.", nameof(LengthFeet));
            if (DesignFlowCfs < 0.0 || double.IsNaN(DesignFlowCfs) || double.IsInfinity(DesignFlowCfs))
                throw new ArgumentException("Design flow must be a non-negative finite number.", nameof(DesignFlowCfs));
            if (double.IsNaN(GroundElevationStartFeet) || double.IsInfinity(GroundElevationStartFeet) ||
                double.IsNaN(GroundElevationStopFeet) || double.IsInfinity(GroundElevationStopFeet))
                throw new ArgumentException("Ground elevations must be finite numbers.");
        }
    }

    public sealed class HydraulicSizingResult
    {
        public string ConduitId { get; set; }
        public int StartNodeId { get; set; }
        public int StopNodeId { get; set; }
        public double LengthFeet { get; set; }

        public int SelectedDiameterInches { get; set; }
        public double SelectedDiameterFeet => SelectedDiameterInches / 12.0;

        public double DesignFlowCfs { get; set; }
        public double FullFlowCapacityCfs { get; set; }
        public double CapacityRatio => FullFlowCapacityCfs > 0.0 ? Math.Round(DesignFlowCfs / FullFlowCapacityCfs, 4) : 0.0;

        public double DesignSlope { get; set; }
        public double MinimumRequiredSlope { get; set; }

        public double ActualVelocityFps { get; set; }
        public double FullFlowVelocityFps { get; set; }
        public double DepthOfFlowFeet { get; set; }
        public double DepthToDiameterRatio => SelectedDiameterFeet > 0.0 ? Math.Round(DepthOfFlowFeet / SelectedDiameterFeet, 4) : 0.0;

        public double UpstreamInvertFeet { get; set; }
        public double DownstreamInvertFeet { get; set; }
        public double UpstreamCrownFeet => Math.Round(UpstreamInvertFeet + SelectedDiameterFeet, 4);
        public double DownstreamCrownFeet => Math.Round(DownstreamInvertFeet + SelectedDiameterFeet, 4);

        public double UpstreamCoverFeet { get; set; }
        public double DownstreamCoverFeet { get; set; }
        public double CrownDropFromUpstreamFeet { get; set; }

        public VelocityComplianceStatus VelocityCompliance { get; set; } = VelocityComplianceStatus.Pass;
        public CoverComplianceStatus CoverCompliance { get; set; } = CoverComplianceStatus.Pass;
        public CapacityComplianceStatus CapacityCompliance { get; set; } = CapacityComplianceStatus.Pass;

        public Dictionary<string, string> Diagnostics { get; } = new Dictionary<string, string>();
    }

    public sealed class StormNetworkDesignResult
    {
        public List<HydraulicSizingResult> ConduitResults { get; set; } = new List<HydraulicSizingResult>();
        public double TotalSystemLengthFeet { get; set; }
        public double TotalDesignDischargeCfs { get; set; }
        public bool HasViolations => ConduitResults.Any(r =>
            r.VelocityCompliance != VelocityComplianceStatus.Pass ||
            r.CoverCompliance != CoverComplianceStatus.Pass ||
            r.CapacityCompliance != CapacityComplianceStatus.Pass);

        public List<string> Violations { get; set; } = new List<string>();
    }
}
