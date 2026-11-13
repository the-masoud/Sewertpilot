using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum HydrologyCalculationMethod
    {
        Rational = 1,
        ScsCurveNumber = 2,
        SwmmReservoir = 3
    }

    public enum HydrologyUnitSystem
    {
        UsCustomary = 1,
        Metric = 2
    }

    public enum ScsStormType
    {
        TypeI = 1,
        TypeIA = 2,
        TypeII = 3,
        TypeIII = 4
    }

    public sealed class IdfParameters
    {
        public string Name { get; set; } = "Standard IDF";
        public int ReturnPeriodYears { get; set; } = 10;
        public double A { get; set; }
        public double B { get; set; }
        public double C { get; set; } = 1.0;
        public double MinimumTimeOfConcentrationMinutes { get; set; } = 5.0;

        public void Validate()
        {
            if (A <= 0 || double.IsNaN(A) || double.IsInfinity(A))
                throw new ArgumentException("IDF parameter 'A' must be a positive finite number.", nameof(A));
            if (B < 0 || double.IsNaN(B) || double.IsInfinity(B))
                throw new ArgumentException("IDF parameter 'B' must be a non-negative finite number.", nameof(B));
            if (C <= 0 || double.IsNaN(C) || double.IsInfinity(C))
                throw new ArgumentException("IDF parameter 'C' must be a positive finite number.", nameof(C));
            if (MinimumTimeOfConcentrationMinutes <= 0 || double.IsNaN(MinimumTimeOfConcentrationMinutes) || double.IsInfinity(MinimumTimeOfConcentrationMinutes))
                throw new ArgumentException("Minimum time of concentration must be positive and finite.", nameof(MinimumTimeOfConcentrationMinutes));
            if (ReturnPeriodYears <= 0)
                throw new ArgumentException("Return period must be positive.", nameof(ReturnPeriodYears));
        }

        public double CalculateIntensity(double durationMinutes)
        {
            Validate();
            if (double.IsNaN(durationMinutes) || double.IsInfinity(durationMinutes))
                throw new ArgumentException("Duration must be a finite number.", nameof(durationMinutes));

            double effectiveDuration = Math.Max(durationMinutes, MinimumTimeOfConcentrationMinutes);
            double denominator = Math.Pow(effectiveDuration + B, C);
            if (denominator <= 0)
                throw new InvalidOperationException("IDF denominator must be positive.");
            return A / denominator;
        }
    }

    public sealed class SubArea
    {
        public string Name { get; set; }
        public double AreaAcres { get; set; }
        public double RunoffCoefficient { get; set; }
        public double CurveNumber { get; set; }
        public double ManningN { get; set; } = 0.015;
        public double DepressionStorageInches { get; set; } = 0.10;
        public bool IsImpervious { get; set; }

        public void Validate()
        {
            if (AreaAcres <= 0 || double.IsNaN(AreaAcres) || double.IsInfinity(AreaAcres))
                throw new ArgumentException("Sub-area area must be a positive finite number.", nameof(AreaAcres));
            if (RunoffCoefficient < 0 || RunoffCoefficient > 1.0 || double.IsNaN(RunoffCoefficient) || double.IsInfinity(RunoffCoefficient))
                throw new ArgumentException("Runoff coefficient must be between 0.0 and 1.0.", nameof(RunoffCoefficient));
            if (CurveNumber != 0 && (CurveNumber < 1.0 || CurveNumber > 100.0 || double.IsNaN(CurveNumber) || double.IsInfinity(CurveNumber)))
                throw new ArgumentException("Curve Number must be between 1.0 and 100.0.", nameof(CurveNumber));
        }
    }

    public sealed class StormCatchment
    {
        public string CatchmentId { get; set; }
        public string Name { get; set; }
        public int TargetNodeId { get; set; }
        public double TotalAreaAcres { get; set; }
        public double TimeOfConcentrationMinutes { get; set; }
        public double RunoffCoefficient { get; set; }
        public double CurveNumber { get; set; }
        public double Slope { get; set; } = 0.02;
        public double FlowLengthFeet { get; set; } = 100.0;
        public double ManningN { get; set; } = 0.015;
        public double DepressionStorageInches { get; set; } = 0.10;
        public double PercentImpervious { get; set; }
        public List<SubArea> SubAreas { get; set; } = new List<SubArea>();

        public void Validate(HydrologyCalculationMethod method)
        {
            if (string.IsNullOrWhiteSpace(CatchmentId))
                throw new ArgumentException("CatchmentId cannot be null or empty.", nameof(CatchmentId));
            if (TargetNodeId <= 0)
                throw new ArgumentException("TargetNodeId must be a positive integer.", nameof(TargetNodeId));
            if (TotalAreaAcres <= 0 || double.IsNaN(TotalAreaAcres) || double.IsInfinity(TotalAreaAcres))
                throw new ArgumentException("TotalAreaAcres must be a positive finite number.", nameof(TotalAreaAcres));
            if (TimeOfConcentrationMinutes < 0 || double.IsNaN(TimeOfConcentrationMinutes) || double.IsInfinity(TimeOfConcentrationMinutes))
                throw new ArgumentException("TimeOfConcentrationMinutes must be a non-negative finite number.", nameof(TimeOfConcentrationMinutes));

            if (SubAreas != null && SubAreas.Count > 0)
            {
                foreach (var sa in SubAreas) sa.Validate();
                double subAreaSum = SubAreas.Sum(sa => sa.AreaAcres);
                if (Math.Abs(subAreaSum - TotalAreaAcres) > 1e-4)
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        "SubArea area sum ({0:F4}) does not match TotalAreaAcres ({1:F4}).", subAreaSum, TotalAreaAcres));
            }

            if (method == HydrologyCalculationMethod.Rational)
            {
                double c = GetEffectiveRunoffCoefficient();
                if (c < 0 || c > 1.0 || double.IsNaN(c) || double.IsInfinity(c))
                    throw new ArgumentException("Effective runoff coefficient must be between 0.0 and 1.0.", nameof(RunoffCoefficient));
            }
            else if (method == HydrologyCalculationMethod.ScsCurveNumber)
            {
                double cn = GetEffectiveCurveNumber();
                if (cn < 1.0 || cn > 100.0 || double.IsNaN(cn) || double.IsInfinity(cn))
                    throw new ArgumentException("Effective Curve Number must be between 1.0 and 100.0.", nameof(CurveNumber));
            }
        }

        public double GetEffectiveRunoffCoefficient()
        {
            if (SubAreas != null && SubAreas.Count > 0)
            {
                double sum = SubAreas.Sum(sa => sa.AreaAcres * sa.RunoffCoefficient);
                double total = SubAreas.Sum(sa => sa.AreaAcres);
                return total > 0 ? sum / total : RunoffCoefficient;
            }
            return RunoffCoefficient;
        }

        public double GetEffectiveCurveNumber()
        {
            if (SubAreas != null && SubAreas.Count > 0)
            {
                double sum = SubAreas.Sum(sa => sa.AreaAcres * sa.CurveNumber);
                double total = SubAreas.Sum(sa => sa.AreaAcres);
                return total > 0 ? sum / total : CurveNumber;
            }
            return CurveNumber;
        }
    }

    public sealed class HydrographPoint
    {
        public double TimeMinutes { get; set; }
        public double FlowCfs { get; set; }
        public double FlowCms => FlowCfs * 0.028316846592;

        public HydrographPoint() { }
        public HydrographPoint(double timeMinutes, double flowCfs)
        {
            TimeMinutes = timeMinutes;
            FlowCfs = flowCfs;
        }
    }

    public sealed class RunoffResult
    {
        public string CatchmentId { get; set; }
        public int TargetNodeId { get; set; }
        public HydrologyCalculationMethod Method { get; set; }
        public int ReturnPeriodYears { get; set; }
        public double AreaAcres { get; set; }
        public double AreaHectares => AreaAcres * 0.40468564224;
        public double TimeOfConcentrationMinutes { get; set; }
        public double EffectiveRunoffParameter { get; set; } // C for Rational, CN for SCS
        public double RainfallIntensityInPerHour { get; set; }
        public double PrecipitationDepthInches { get; set; }
        public double PeakDischargeCfs { get; set; }
        public double PeakDischargeCms => PeakDischargeCfs * 0.028316846592;
        public double TotalVolumeCubicFeet { get; set; }
        public double TotalVolumeCubicMeters => TotalVolumeCubicFeet * 0.028316846592;
        public List<HydrographPoint> Hydrograph { get; set; } = new List<HydrographPoint>();
        public Dictionary<string, string> Diagnostics { get; set; } = new Dictionary<string, string>();
        public DateTimeOffset CalculationTimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class AggregatedNodeInflowResult
    {
        public int TargetNodeId { get; set; }
        public List<string> ContributingCatchmentIds { get; set; } = new List<string>();
        public double TotalDrainageAreaAcres { get; set; }
        public double TotalDrainageAreaHectares => TotalDrainageAreaAcres * 0.40468564224;
        public double CompositeRunoffCoefficient { get; set; }
        public double GoverningTimeOfConcentrationMinutes { get; set; }
        public double TotalPeakDischargeCfs { get; set; }
        public double TotalPeakDischargeCms => TotalPeakDischargeCfs * 0.028316846592;
        public List<RunoffResult> CatchmentResults { get; set; } = new List<RunoffResult>();
    }
}
