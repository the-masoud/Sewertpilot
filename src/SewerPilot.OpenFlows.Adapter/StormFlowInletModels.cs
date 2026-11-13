using System;
using System.Collections.Generic;
using System.Globalization;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum InletType
    {
        CurbOpening = 1,
        Grate = 2,
        Combination = 3,
        SlottedDrain = 4
    }

    public enum InletLocation
    {
        OnContinuousGrade = 1,
        InSag = 2
    }

    public enum GrateType
    {
        P50 = 1,          // Parallel bar, 1-7/8" clear
        P50x100 = 2,      // Parallel bar with 4" transverse spacers
        P30 = 3,          // Parallel bar, 1-1/8" clear
        CurvedVane = 4,   // Curved vane grate
        TiltBar45 = 5,    // 45-degree tilt bar
        TiltBar30 = 6,    // 30-degree tilt bar
        Reticuline = 7    // Honeycomb pattern
    }

    public enum InletFlowRegime
    {
        FrontalAndSide = 1,
        Weir = 2,
        Orifice = 3,
        Transitional = 4
    }

    public sealed class GutterCrossSection
    {
        public double CrossSlopeSx { get; set; } = 0.02; // ft/ft
        public double LongitudinalSlopeSL { get; set; } = 0.01; // ft/ft
        public double ManningN { get; set; } = 0.016; // Standard concrete gutter
        public double GutterWidthFeet { get; set; } = 0.0; // W in ft (0 = uniform cross slope)
        public double LocalDepressionInches { get; set; } = 0.0; // a in inches

        public bool IsComposite => GutterWidthFeet > 0.0 && LocalDepressionInches > 0.0;

        public double GetGutterCrossSlopeSw()
        {
            if (!IsComposite) return CrossSlopeSx;
            return CrossSlopeSx + ((LocalDepressionInches / 12.0) / GutterWidthFeet);
        }

        public void Validate()
        {
            if (CrossSlopeSx <= 0.0 || double.IsNaN(CrossSlopeSx) || double.IsInfinity(CrossSlopeSx))
                throw new ArgumentException("Cross slope (Sx) must be a positive finite number.", nameof(CrossSlopeSx));
            if (LongitudinalSlopeSL <= 0.0 || double.IsNaN(LongitudinalSlopeSL) || double.IsInfinity(LongitudinalSlopeSL))
                throw new ArgumentException("Longitudinal slope (SL) must be a positive finite number.", nameof(LongitudinalSlopeSL));
            if (ManningN <= 0.0 || double.IsNaN(ManningN) || double.IsInfinity(ManningN))
                throw new ArgumentException("Manning's roughness coefficient (n) must be a positive finite number.", nameof(ManningN));
            if (GutterWidthFeet < 0.0 || double.IsNaN(GutterWidthFeet) || double.IsInfinity(GutterWidthFeet))
                throw new ArgumentException("Gutter width must be non-negative.", nameof(GutterWidthFeet));
            if (LocalDepressionInches < 0.0 || double.IsNaN(LocalDepressionInches) || double.IsInfinity(LocalDepressionInches))
                throw new ArgumentException("Local depression must be non-negative.", nameof(LocalDepressionInches));
        }
    }

    public sealed class InletConfiguration
    {
        public string InletId { get; set; }
        public int TargetNodeId { get; set; }
        public string DownstreamInletId { get; set; } // Null or empty if outlet/terminal
        public InletType Type { get; set; } = InletType.CurbOpening;
        public InletLocation Location { get; set; } = InletLocation.OnContinuousGrade;

        public double LengthFeet { get; set; } = 5.0; // Curb opening length or grate length
        public double WidthFeet { get; set; } = 2.0; // Grate width (or slotted width)
        public double CurbOpeningHeightInches { get; set; } = 6.0; // Throat height for curb opening
        public GrateType Grate { get; set; } = GrateType.P50;
        public double CloggingFactor { get; set; } = 0.0; // 0.0 to 0.90 (e.g. 0.50 for sag grate)
        public double LocalDepressionInches { get; set; } = 0.0;
        public double AllowableSpreadFeet { get; set; } = 8.0; // Spread criterion from Design Basis
        public GutterCrossSection Gutter { get; set; } = new GutterCrossSection();

        public double CurbOpeningLengthFeet { get => LengthFeet; set => LengthFeet = value; }
        public double GrateLengthFeet { get => LengthFeet; set => LengthFeet = value; }
        public double GrateWidthFeet { get => WidthFeet; set => WidthFeet = value; }
        public double ThroatHeightInches { get => CurbOpeningHeightInches; set => CurbOpeningHeightInches = value; }
        public GrateType GrateType { get => Grate; set => Grate = value; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(InletId))
                throw new ArgumentException("InletId cannot be null or empty.", nameof(InletId));
            if (TargetNodeId <= 0)
                throw new ArgumentException("TargetNodeId must be a positive integer.", nameof(TargetNodeId));
            if (LengthFeet <= 0.0 || double.IsNaN(LengthFeet) || double.IsInfinity(LengthFeet))
                throw new ArgumentException("Inlet length must be a positive finite number.", nameof(LengthFeet));
            if (WidthFeet < 0.0 || double.IsNaN(WidthFeet) || double.IsInfinity(WidthFeet))
                throw new ArgumentException("Inlet width must be non-negative.", nameof(WidthFeet));
            if (CurbOpeningHeightInches < 0.0 || double.IsNaN(CurbOpeningHeightInches) || double.IsInfinity(CurbOpeningHeightInches))
                throw new ArgumentException("Throat height must be non-negative.", nameof(CurbOpeningHeightInches));
            if (CloggingFactor < 0.0 || CloggingFactor >= 1.0 || double.IsNaN(CloggingFactor))
                throw new ArgumentException("CloggingFactor must be in range [0.0, 1.0).", nameof(CloggingFactor));
            if (AllowableSpreadFeet <= 0.0 || double.IsNaN(AllowableSpreadFeet))
                throw new ArgumentException("AllowableSpreadFeet must be positive.", nameof(AllowableSpreadFeet));
            if (Gutter == null)
                throw new ArgumentNullException(nameof(Gutter), "Gutter configuration cannot be null.");

            Gutter.Validate();
        }
    }

    public sealed class GutterFlowResult
    {
        public double TotalFlowCfs { get; set; }
        public double SpreadFeet { get; set; }
        public double DepthAtCurbInches { get; set; }
        public double VelocityFps { get; set; }
        public double CrossSectionalAreaSqFt { get; set; }
        public double FrontalFlowRatio { get; set; } = 1.0; // Eo = Qw / Q
    }

    public sealed class InletInterceptionResult
    {
        public string InletId { get; set; }
        public int TargetNodeId { get; set; }
        public InletType InletType { get; set; }
        public InletLocation InletLocation { get; set; }

        public double LocalRunoffCfs { get; set; }
        public double CarryoverInflowCfs { get; set; }
        public double TotalApproachFlowCfs { get; set; } // Qtotal = Qlocal + Qcarryover
        public double InterceptedFlowCfs { get; set; }   // Qi
        public double BypassFlowCfs { get; set; }        // Qb = Qtotal - Qi
        public double InterceptionEfficiency { get; set; } // E = Qi / Qtotal

        public double GutterSpreadFeet { get; set; }
        public double GutterDepthInches { get; set; }
        public double GutterVelocityFps { get; set; }
        public InletFlowRegime FlowRegime { get; set; }
        public double AllowableSpreadFeet { get; set; }
        public bool SpreadExceeded => GutterSpreadFeet > AllowableSpreadFeet;

        public Dictionary<string, string> Diagnostics { get; } = new Dictionary<string, string>();
    }

    public sealed class InletNetworkRoutingResult
    {
        public List<InletInterceptionResult> InletResults { get; set; } = new List<InletInterceptionResult>();
        public double TotalApproachFlowCfs { get; set; }
        public double TotalInterceptedFlowCfs { get; set; }
        public double TotalSystemBypassFlowCfs { get; set; }
        public double OverallCaptureEfficiency => TotalApproachFlowCfs > 0.0 ? TotalInterceptedFlowCfs / TotalApproachFlowCfs : 1.0;
        public List<string> SpreadExceedanceInletIds { get; set; } = new List<string>();
    }
}
