using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public static class StormFlowHydrologyEngine
    {
        public const double DefaultMinTimeOfConcentrationMinutes = 5.0;
        public const double SquareFeetPerAcre = 43560.0;
        public const double CfsToCms = 0.028316846592;

        public static double GetFrequencyFactor(int returnPeriodYears)
        {
            if (returnPeriodYears <= 10) return 1.00;
            if (returnPeriodYears <= 25) return 1.10;
            if (returnPeriodYears <= 50) return 1.20;
            return 1.25;
        }

        public static RunoffResult ComputeRational(StormCatchment catchment, IdfParameters idf, int returnPeriodYears, double minTcMinutes = DefaultMinTimeOfConcentrationMinutes)
        {
            if (catchment == null) throw new ArgumentNullException(nameof(catchment));
            if (idf == null) throw new ArgumentNullException(nameof(idf));
            if (returnPeriodYears <= 0) throw new ArgumentException("Return period must be positive.", nameof(returnPeriodYears));
            catchment.Validate(HydrologyCalculationMethod.Rational);
            idf.Validate();

            double effectiveTc = Math.Max(catchment.TimeOfConcentrationMinutes, Math.Max(minTcMinutes, idf.MinimumTimeOfConcentrationMinutes));
            double intensity = idf.CalculateIntensity(effectiveTc);
            double baseC = catchment.GetEffectiveRunoffCoefficient();
            double cf = GetFrequencyFactor(returnPeriodYears);
            double adjustedC = Math.Min(baseC * cf, 1.0);

            // Rational Method standard US Customary formulation: Q = C * I * A (in cfs)
            double peakQ = adjustedC * intensity * catchment.TotalAreaAcres;

            // Generate standard synthetic triangular hydrograph
            // Time to peak: tp = effectiveTc; Recession time: tr = 1.67 * effectiveTc; Base time: tb = tp + tr
            double tp = effectiveTc;
            double tr = 1.67 * effectiveTc;
            double tb = tp + tr;
            var hydrograph = new List<HydrographPoint>();

            int risingSteps = 10;
            for (int i = 0; i <= risingSteps; i++)
            {
                double t = tp * ((double)i / risingSteps);
                double q = (tp > 0) ? (peakQ * (t / tp)) : peakQ;
                hydrograph.Add(new HydrographPoint(Math.Round(t, 2), Math.Round(q, 4)));
            }

            int recessionSteps = 15;
            for (int i = 1; i <= recessionSteps; i++)
            {
                double t = tp + (tr * ((double)i / recessionSteps));
                double q = (tr > 0) ? (peakQ * (1.0 - (t - tp) / tr)) : 0.0;
                hydrograph.Add(new HydrographPoint(Math.Round(t, 2), Math.Max(0.0, Math.Round(q, 4))));
            }

            // Total volume of triangular hydrograph = 0.5 * Qpeak * tb [seconds]
            double volumeCuFt = 0.5 * peakQ * (tb * 60.0);

            var result = new RunoffResult
            {
                CatchmentId = catchment.CatchmentId,
                TargetNodeId = catchment.TargetNodeId,
                Method = HydrologyCalculationMethod.Rational,
                ReturnPeriodYears = returnPeriodYears,
                AreaAcres = catchment.TotalAreaAcres,
                TimeOfConcentrationMinutes = effectiveTc,
                EffectiveRunoffParameter = adjustedC,
                RainfallIntensityInPerHour = intensity,
                PrecipitationDepthInches = intensity * (effectiveTc / 60.0),
                PeakDischargeCfs = peakQ,
                TotalVolumeCubicFeet = volumeCuFt,
                Hydrograph = hydrograph
            };

            result.Diagnostics["FrequencyFactor"] = cf.ToString("F2", CultureInfo.InvariantCulture);
            result.Diagnostics["BaseRunoffCoefficient"] = baseC.ToString("F4", CultureInfo.InvariantCulture);
            result.Diagnostics["AdjustedRunoffCoefficient"] = adjustedC.ToString("F4", CultureInfo.InvariantCulture);
            result.Diagnostics["GoverningTcMinutes"] = effectiveTc.ToString("F2", CultureInfo.InvariantCulture);
            result.Diagnostics["HydrographBaseTimeMinutes"] = tb.ToString("F2", CultureInfo.InvariantCulture);
            return result;
        }

        public static RunoffResult ComputeScsCurveNumber(StormCatchment catchment, double precipDepthInches, ScsStormType stormType = ScsStormType.TypeII, int returnPeriodYears = 10)
        {
            if (catchment == null) throw new ArgumentNullException(nameof(catchment));
            if (precipDepthInches < 0 || double.IsNaN(precipDepthInches) || double.IsInfinity(precipDepthInches))
                throw new ArgumentException("Precipitation depth must be a non-negative finite number.", nameof(precipDepthInches));
            catchment.Validate(HydrologyCalculationMethod.ScsCurveNumber);

            double cn = catchment.GetEffectiveCurveNumber();
            double s = (1000.0 / cn) - 10.0;
            double ia = 0.2 * s;

            double qDepthInches = 0.0;
            if (precipDepthInches > ia)
            {
                double num = precipDepthInches - ia;
                qDepthInches = (num * num) / (precipDepthInches - ia + s);
            }

            // Total runoff volume = Qdepth [ft] * Area [sq ft]
            double totalVolumeCuFt = (qDepthInches / 12.0) * (catchment.TotalAreaAcres * SquareFeetPerAcre);

            // TR-55 Unit Peak Flow calculation:
            // tc in hours, lag time tl = 0.6 * tc
            double tcHours = Math.Max(catchment.TimeOfConcentrationMinutes, DefaultMinTimeOfConcentrationMinutes) / 60.0;
            double tpHours = (tcHours * 0.6) + (tcHours * 0.1); // approx time to peak
            if (tpHours <= 0) tpHours = 0.1;

            // Standard NRCS peak discharge: qp = (484 * Area * Qdepth) / tp
            // where Area is sq miles. Area [sq miles] = AreaAcres / 640.0.
            double areaSqMiles = catchment.TotalAreaAcres / 640.0;
            double peakQ = qDepthInches > 0
                ? (484.0 * areaSqMiles * qDepthInches) / tpHours
                : 0.0;

            // Generate 24-hr cumulative rainfall distribution hydrograph
            var hydrograph = GenerateScsHydrograph(precipDepthInches, qDepthInches, peakQ, tpHours, stormType);

            var result = new RunoffResult
            {
                CatchmentId = catchment.CatchmentId,
                TargetNodeId = catchment.TargetNodeId,
                Method = HydrologyCalculationMethod.ScsCurveNumber,
                ReturnPeriodYears = returnPeriodYears,
                AreaAcres = catchment.TotalAreaAcres,
                TimeOfConcentrationMinutes = catchment.TimeOfConcentrationMinutes,
                EffectiveRunoffParameter = cn,
                RainfallIntensityInPerHour = precipDepthInches / 24.0,
                PrecipitationDepthInches = precipDepthInches,
                PeakDischargeCfs = peakQ,
                TotalVolumeCubicFeet = totalVolumeCuFt,
                Hydrograph = hydrograph
            };

            result.Diagnostics["PotentialMaxRetentionS"] = s.ToString("F4", CultureInfo.InvariantCulture);
            result.Diagnostics["InitialAbstractionIa"] = ia.ToString("F4", CultureInfo.InvariantCulture);
            result.Diagnostics["RunoffDepthInches"] = qDepthInches.ToString("F4", CultureInfo.InvariantCulture);
            result.Diagnostics["TimeOfConcentrationHours"] = tcHours.ToString("F4", CultureInfo.InvariantCulture);
            result.Diagnostics["TimeToPeakHours"] = tpHours.ToString("F4", CultureInfo.InvariantCulture);
            result.Diagnostics["StormType"] = stormType.ToString();
            return result;
        }

        private static List<HydrographPoint> GenerateScsHydrograph(double pTotal, double qDepth, double peakQ, double tpHours, ScsStormType stormType)
        {
            var points = new List<HydrographPoint>();
            if (qDepth <= 0 || peakQ <= 0)
            {
                for (int h = 0; h <= 24; h += 2)
                    points.Add(new HydrographPoint(h * 60.0, 0.0));
                return points;
            }

            // 24-hour dimensionless cumulative temporal distribution fractions for Type II
            // Standard SCS dimensionless 24-hour rainfall distribution table
            double[] times = { 0, 2, 4, 6, 8, 9, 10, 11, 11.5, 11.8, 12.0, 12.2, 12.5, 13, 14, 16, 18, 20, 22, 24 };
            double[] type2Fractions = { 0.0, 0.022, 0.048, 0.080, 0.120, 0.147, 0.181, 0.235, 0.283, 0.400, 0.663, 0.735, 0.772, 0.799, 0.835, 0.880, 0.915, 0.952, 0.981, 1.000 };

            for (int i = 0; i < times.Length; i++)
            {
                double tHours = times[i];
                double flow = 0.0;
                // Shape flow around the peak window (hours 11 to 14)
                if (tHours < 8.0)
                {
                    flow = peakQ * (type2Fractions[i] * 0.1);
                }
                else if (tHours <= 12.0)
                {
                    double frac = (tHours - 8.0) / 4.0;
                    flow = peakQ * (0.05 + 0.95 * Math.Pow(frac, 2.5));
                }
                else if (tHours <= 16.0)
                {
                    double frac = (tHours - 12.0) / 4.0;
                    flow = peakQ * Math.Max(0.05, 1.0 - 0.90 * Math.Pow(frac, 0.8));
                }
                else
                {
                    double frac = (24.0 - tHours) / 8.0;
                    flow = peakQ * 0.05 * frac;
                }
                points.Add(new HydrographPoint(Math.Round(tHours * 60.0, 1), Math.Max(0.0, Math.Round(flow, 4))));
            }
            return points;
        }

        public static RunoffResult ComputeSwmmReservoir(StormCatchment catchment, double precipDepthInches, double stormDurationHours = 1.0, double timeStepSeconds = 30.0)
        {
            if (catchment == null) throw new ArgumentNullException(nameof(catchment));
            if (precipDepthInches < 0 || double.IsNaN(precipDepthInches) || double.IsInfinity(precipDepthInches))
                throw new ArgumentException("Precipitation depth must be non-negative and finite.", nameof(precipDepthInches));
            if (stormDurationHours <= 0 || double.IsNaN(stormDurationHours))
                throw new ArgumentException("Storm duration must be positive.", nameof(stormDurationHours));
            catchment.Validate(HydrologyCalculationMethod.Rational);

            double areaSqFt = catchment.TotalAreaAcres * SquareFeetPerAcre;
            double length = Math.Max(10.0, catchment.FlowLengthFeet);
            double width = areaSqFt / length; // Overland flow width W
            double slope = Math.Max(0.001, catchment.Slope);
            double n = Math.Max(0.010, catchment.ManningN);
            double dpFeet = (catchment.DepressionStorageInches > 0 ? catchment.DepressionStorageInches : 0.10) / 12.0;

            // Manning overland flow alpha: alpha = (1.486 / n) * W * S^(1/2)
            double alpha = (1.486 / n) * width * Math.Sqrt(slope);

            // Rainfall intensity in ft/s
            double totalDurationSec = stormDurationHours * 3600.0;
            double totalRainFeet = precipDepthInches / 12.0;
            double rainRateFtPerSec = totalRainFeet / totalDurationSec;

            double depth = 0.0; // Current stored depth in ft
            double peakQ = 0.0;
            double totalOutflowCuFt = 0.0;
            var hydrograph = new List<HydrographPoint>();

            double simTimeTotalSec = totalDurationSec * 1.5; // simulate beyond storm end for drainage
            int steps = (int)(simTimeTotalSec / timeStepSeconds);

            for (int step = 0; step <= steps; step++)
            {
                double tSec = step * timeStepSeconds;
                double currentRainRate = tSec <= totalDurationSec ? rainRateFtPerSec : 0.0;

                // Outflow rate: Q = alpha * (depth - dp)^(5/3)
                double excessDepth = Math.Max(0.0, depth - dpFeet);
                double qCfs = excessDepth > 0 ? alpha * Math.Pow(excessDepth, 5.0 / 3.0) : 0.0;

                if (qCfs > peakQ) peakQ = qCfs;
                totalOutflowCuFt += qCfs * timeStepSeconds;

                // Update depth via mass balance: d(depth)/dt = i - q/Area
                double dDepth = (currentRainRate - (qCfs / areaSqFt)) * timeStepSeconds;
                depth = Math.Max(0.0, depth + dDepth);

                // Sample every 5 minutes for hydrograph
                if (step % (int)Math.Max(1, 300.0 / timeStepSeconds) == 0 || step == steps)
                {
                    hydrograph.Add(new HydrographPoint(Math.Round(tSec / 60.0, 2), Math.Round(qCfs, 4)));
                }
            }

            var result = new RunoffResult
            {
                CatchmentId = catchment.CatchmentId,
                TargetNodeId = catchment.TargetNodeId,
                Method = HydrologyCalculationMethod.SwmmReservoir,
                ReturnPeriodYears = 10,
                AreaAcres = catchment.TotalAreaAcres,
                TimeOfConcentrationMinutes = catchment.TimeOfConcentrationMinutes,
                EffectiveRunoffParameter = catchment.ManningN,
                RainfallIntensityInPerHour = precipDepthInches / stormDurationHours,
                PrecipitationDepthInches = precipDepthInches,
                PeakDischargeCfs = peakQ,
                TotalVolumeCubicFeet = totalOutflowCuFt,
                Hydrograph = hydrograph
            };

            result.Diagnostics["OverlandFlowWidthFeet"] = width.ToString("F2", CultureInfo.InvariantCulture);
            result.Diagnostics["OverlandFlowLengthFeet"] = length.ToString("F2", CultureInfo.InvariantCulture);
            result.Diagnostics["DepressionStorageFeet"] = dpFeet.ToString("F4", CultureInfo.InvariantCulture);
            result.Diagnostics["ManningAlpha"] = alpha.ToString("F4", CultureInfo.InvariantCulture);
            return result;
        }

        public static List<AggregatedNodeInflowResult> AggregateCatchmentInflows(
            IEnumerable<StormCatchment> catchments, IdfParameters idf, int returnPeriodYears, double minTcMinutes = DefaultMinTimeOfConcentrationMinutes)
        {
            if (catchments == null) throw new ArgumentNullException(nameof(catchments));
            if (idf == null) throw new ArgumentNullException(nameof(idf));

            var results = new List<AggregatedNodeInflowResult>();
            var groups = catchments.GroupBy(c => c.TargetNodeId).OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                int nodeId = group.Key;
                var catchmentList = group.ToList();
                double totalArea = catchmentList.Sum(c => c.TotalAreaAcres);

                // Area-weighted composite C
                double weightedC = totalArea > 0
                    ? catchmentList.Sum(c => c.TotalAreaAcres * c.GetEffectiveRunoffCoefficient()) / totalArea
                    : 0.0;

                // Governing time of concentration: longest tc of the contributing catchments (conservative standard)
                double maxTc = catchmentList.Max(c => c.TimeOfConcentrationMinutes);
                double governingTc = Math.Max(maxTc, Math.Max(minTcMinutes, idf.MinimumTimeOfConcentrationMinutes));

                // Compute aggregated peak flow using governing tc and composite C
                double intensity = idf.CalculateIntensity(governingTc);
                double cf = GetFrequencyFactor(returnPeriodYears);
                double adjustedC = Math.Min(weightedC * cf, 1.0);
                double totalPeakQ = adjustedC * intensity * totalArea;

                var individualResults = new List<RunoffResult>();
                foreach (var c in catchmentList)
                {
                    individualResults.Add(ComputeRational(c, idf, returnPeriodYears, minTcMinutes));
                }

                results.Add(new AggregatedNodeInflowResult
                {
                    TargetNodeId = nodeId,
                    ContributingCatchmentIds = catchmentList.Select(c => c.CatchmentId).ToList(),
                    TotalDrainageAreaAcres = totalArea,
                    CompositeRunoffCoefficient = adjustedC,
                    GoverningTimeOfConcentrationMinutes = governingTc,
                    TotalPeakDischargeCfs = totalPeakQ,
                    CatchmentResults = individualResults
                });
            }

            return results;
        }
    }
}
