using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public static class StormFlowInletHydraulicsEngine
    {
        private const double GravityFtPerSec2 = 32.174;
        private const double KuGutterUs = 0.56; // US Customary Manning's coefficient for triangular gutter
        private const double KtCurbUs = 0.60;   // US Customary curb 100% interception length factor
        private const double CwWeirUs = 3.00;   // Standard weir coefficient in US Customary
        private const double CoOrificeUs = 0.67; // Standard orifice discharge coefficient

        public static GutterFlowResult ComputeGutterFlow(double flowCfs, GutterCrossSection gutter)
        {
            if (flowCfs < 0.0 || double.IsNaN(flowCfs) || double.IsInfinity(flowCfs))
                throw new ArgumentException("Flow rate must be a non-negative finite number.", nameof(flowCfs));
            if (gutter == null) throw new ArgumentNullException(nameof(gutter));
            gutter.Validate();

            if (flowCfs == 0.0)
            {
                return new GutterFlowResult
                {
                    TotalFlowCfs = 0.0,
                    SpreadFeet = 0.0,
                    DepthAtCurbInches = 0.0,
                    VelocityFps = 0.0,
                    CrossSectionalAreaSqFt = 0.0,
                    FrontalFlowRatio = 1.0
                };
            }

            double sx = gutter.CrossSlopeSx;
            double sl = gutter.LongitudinalSlopeSL;
            double n = gutter.ManningN;
            double sqrtSl = Math.Sqrt(sl);

            if (!gutter.IsComposite)
            {
                // Standard triangular gutter: Q = (0.56 / n) * Sx^(5/3) * SL^(1/2) * T^(8/3)
                double factor = (KuGutterUs / n) * Math.Pow(sx, 5.0 / 3.0) * sqrtSl;
                double spreadT = Math.Pow(flowCfs / factor, 3.0 / 8.0);
                double depthFt = spreadT * sx;
                double areaSqFt = 0.5 * sx * spreadT * spreadT;
                double velocity = areaSqFt > 0 ? flowCfs / areaSqFt : 0.0;

                return new GutterFlowResult
                {
                    TotalFlowCfs = flowCfs,
                    SpreadFeet = Math.Round(spreadT, 4),
                    DepthAtCurbInches = Math.Round(depthFt * 12.0, 4),
                    VelocityFps = Math.Round(velocity, 4),
                    CrossSectionalAreaSqFt = Math.Round(areaSqFt, 4),
                    FrontalFlowRatio = 1.0
                };
            }
            else
            {
                // Composite gutter: width W, depression a (in inches), Sw = Sx + (a/12)/W
                double w = gutter.GutterWidthFeet;
                double sw = gutter.GetGutterCrossSlopeSw();

                // Capacity of depressed gutter section alone when T = W:
                double qwCapacity = (KuGutterUs / n) * Math.Pow(sw, 5.0 / 3.0) * sqrtSl * Math.Pow(w, 8.0 / 3.0);

                if (flowCfs <= qwCapacity)
                {
                    // Spread is within depressed section T <= W
                    double factor = (KuGutterUs / n) * Math.Pow(sw, 5.0 / 3.0) * sqrtSl;
                    double spreadT = Math.Pow(flowCfs / factor, 3.0 / 8.0);
                    double depthFt = spreadT * sw;
                    double areaSqFt = 0.5 * sw * spreadT * spreadT;
                    double vel = areaSqFt > 0 ? flowCfs / areaSqFt : 0.0;

                    return new GutterFlowResult
                    {
                        TotalFlowCfs = flowCfs,
                        SpreadFeet = Math.Round(spreadT, 4),
                        DepthAtCurbInches = Math.Round(depthFt * 12.0, 4),
                        VelocityFps = Math.Round(vel, 4),
                        CrossSectionalAreaSqFt = Math.Round(areaSqFt, 4),
                        FrontalFlowRatio = 1.0
                    };
                }

                // Spread exceeds gutter width: T > W. Solve composite spread via bisection.
                // Q_total(T) = Q_s(Ts) / (1 - Eo)
                double lowT = w;
                double highT = Math.Max(w * 10.0, 100.0);

                // Expand upper bound if needed
                while (EvaluateCompositeFlow(highT, w, sx, sw, sl, n) < flowCfs)
                {
                    highT *= 2.0;
                }

                for (int iter = 0; iter < 60; iter++)
                {
                    double midT = 0.5 * (lowT + highT);
                    double qMid = EvaluateCompositeFlow(midT, w, sx, sw, sl, n);
                    if (Math.Abs(qMid - flowCfs) < 1e-6)
                    {
                        lowT = highT = midT;
                        break;
                    }
                    if (qMid < flowCfs) lowT = midT;
                    else highT = midT;
                }

                double finalT = 0.5 * (lowT + highT);
                double ts = finalT - w;
                double qs = (KuGutterUs / n) * Math.Pow(sx, 5.0 / 3.0) * sqrtSl * Math.Pow(ts, 8.0 / 3.0);
                double qw = Math.Max(0.0, flowCfs - qs);
                double eo = flowCfs > 0 ? qw / flowCfs : 1.0;

                double depthAtCurbFt = (ts * sx) + (w * sw);
                double areaGutter = (0.5 * sw * w * w) + (ts * sx * w);
                double areaStreet = 0.5 * sx * ts * ts;
                double totalArea = areaGutter + areaStreet;
                double velocity = totalArea > 0 ? flowCfs / totalArea : 0.0;

                return new GutterFlowResult
                {
                    TotalFlowCfs = flowCfs,
                    SpreadFeet = Math.Round(finalT, 4),
                    DepthAtCurbInches = Math.Round(depthAtCurbFt * 12.0, 4),
                    VelocityFps = Math.Round(velocity, 4),
                    CrossSectionalAreaSqFt = Math.Round(totalArea, 4),
                    FrontalFlowRatio = Math.Round(eo, 4)
                };
            }
        }

        private static double EvaluateCompositeFlow(double t, double w, double sx, double sw, double sl, double n)
        {
            if (t <= w)
            {
                return (KuGutterUs / n) * Math.Pow(sw, 5.0 / 3.0) * Math.Sqrt(sl) * Math.Pow(t, 8.0 / 3.0);
            }
            double ts = t - w;
            double qs = (KuGutterUs / n) * Math.Pow(sx, 5.0 / 3.0) * Math.Sqrt(sl) * Math.Pow(ts, 8.0 / 3.0);
            double twRatio = t / w;
            double swSxRatio = sw / sx;
            double inner = 1.0 + (swSxRatio / (twRatio - 1.0));
            double bracket = Math.Pow(inner, 8.0 / 3.0) - 1.0;
            double eo = 1.0 / (1.0 + ((sx / sw) * bracket));
            return eo < 1.0 ? qs / (1.0 - eo) : qs;
        }

        public static InletInterceptionResult ComputeInletInterception(
            InletConfiguration inlet,
            double localRunoffCfs,
            double carryoverInflowCfs = 0.0)
        {
            if (inlet == null) throw new ArgumentNullException(nameof(inlet));
            inlet.Validate();
            if (localRunoffCfs < 0.0 || double.IsNaN(localRunoffCfs) || double.IsInfinity(localRunoffCfs))
                throw new ArgumentException("Local runoff must be a non-negative finite number.", nameof(localRunoffCfs));
            if (carryoverInflowCfs < 0.0 || double.IsNaN(carryoverInflowCfs) || double.IsInfinity(carryoverInflowCfs))
                throw new ArgumentException("Carryover inflow must be a non-negative finite number.", nameof(carryoverInflowCfs));

            double totalApproachQ = Math.Round(localRunoffCfs + carryoverInflowCfs, 4);

            var result = new InletInterceptionResult
            {
                InletId = inlet.InletId,
                TargetNodeId = inlet.TargetNodeId,
                InletType = inlet.Type,
                InletLocation = inlet.Location,
                LocalRunoffCfs = localRunoffCfs,
                CarryoverInflowCfs = carryoverInflowCfs,
                TotalApproachFlowCfs = totalApproachQ,
                AllowableSpreadFeet = inlet.AllowableSpreadFeet
            };

            if (totalApproachQ == 0.0)
            {
                result.InterceptedFlowCfs = 0.0;
                result.BypassFlowCfs = 0.0;
                result.InterceptionEfficiency = 1.0;
                result.GutterSpreadFeet = 0.0;
                result.GutterDepthInches = 0.0;
                result.GutterVelocityFps = 0.0;
                result.FlowRegime = inlet.Location == InletLocation.InSag ? InletFlowRegime.Weir : InletFlowRegime.FrontalAndSide;
                return result;
            }

            var gutterResult = ComputeGutterFlow(totalApproachQ, inlet.Gutter);
            result.GutterSpreadFeet = gutterResult.SpreadFeet;
            result.GutterDepthInches = gutterResult.DepthAtCurbInches;
            result.GutterVelocityFps = gutterResult.VelocityFps;

            if (inlet.Location == InletLocation.OnContinuousGrade)
            {
                ComputeOnGradeInterception(inlet, gutterResult, result);
            }
            else
            {
                ComputeInSagInterception(inlet, gutterResult, result);
            }

            result.Diagnostics["FrontalFlowRatio"] = gutterResult.FrontalFlowRatio.ToString("F4", CultureInfo.InvariantCulture);
            result.Diagnostics["GutterVelocityFps"] = gutterResult.VelocityFps.ToString("F2", CultureInfo.InvariantCulture);
            result.Diagnostics["GutterDepthInches"] = gutterResult.DepthAtCurbInches.ToString("F2", CultureInfo.InvariantCulture);
            result.Diagnostics["GutterSpreadFeet"] = gutterResult.SpreadFeet.ToString("F2", CultureInfo.InvariantCulture);
            result.Diagnostics["AllowableSpreadFeet"] = inlet.AllowableSpreadFeet.ToString("F2", CultureInfo.InvariantCulture);
            result.Diagnostics["SpreadExceeded"] = result.SpreadExceeded.ToString();

            return result;
        }

        private static void ComputeOnGradeInterception(
            InletConfiguration inlet,
            GutterFlowResult gutter,
            InletInterceptionResult result)
        {
            double q = result.TotalApproachFlowCfs;
            double sl = inlet.Gutter.LongitudinalSlopeSL;
            double sx = inlet.Gutter.CrossSlopeSx;
            double n = inlet.Gutter.ManningN;
            result.FlowRegime = InletFlowRegime.FrontalAndSide;

            switch (inlet.Type)
            {
                case InletType.CurbOpening:
                case InletType.SlottedDrain:
                    {
                        // Equivalent cross slope Se:
                        double se = sx;
                        if (inlet.Gutter.IsComposite)
                        {
                            double swPrime = (inlet.Gutter.LocalDepressionInches / 12.0) / inlet.Gutter.GutterWidthFeet;
                            se = sx + (swPrime * gutter.FrontalFlowRatio);
                        }

                        // Length for 100% interception LT = 0.60 * Q^0.42 * SL^0.3 * (1 / (n * Se))^0.6
                        double lt = KtCurbUs * Math.Pow(q, 0.42) * Math.Pow(sl, 0.3) * Math.Pow(1.0 / (n * se), 0.6);
                        double efficiency = 1.0;
                        if (inlet.LengthFeet < lt && lt > 0)
                        {
                            double ratio = inlet.LengthFeet / lt;
                            efficiency = 1.0 - Math.Pow(1.0 - ratio, 1.8);
                        }

                        // Apply optional clogging factor
                        double cloggedEff = efficiency * (1.0 - inlet.CloggingFactor);
                        double qi = q * Math.Max(0.0, Math.Min(1.0, cloggedEff));
                        double qb = Math.Max(0.0, q - qi);

                        result.InterceptedFlowCfs = Math.Round(qi, 4);
                        result.BypassFlowCfs = Math.Round(qb, 4);
                        result.InterceptionEfficiency = Math.Round(q > 0 ? qi / q : 1.0, 4);
                        result.Diagnostics["100PercentInterceptionLengthFeet"] = lt.ToString("F2", CultureInfo.InvariantCulture);
                        result.Diagnostics["EquivalentCrossSlope"] = se.ToString("F4", CultureInfo.InvariantCulture);
                        break;
                    }

                case InletType.Grate:
                    {
                        ComputeGrateOnGrade(inlet, gutter, result);
                        break;
                    }

                case InletType.Combination:
                    {
                        // Combination on grade: Grate intercepts first; curb opening upstream or overlapping intercepts additional
                        ComputeGrateOnGrade(inlet, gutter, result);
                        double qRemaining = result.BypassFlowCfs;
                        if (qRemaining > 0 && inlet.LengthFeet > 0)
                        {
                            double se = sx;
                            double lt = KtCurbUs * Math.Pow(qRemaining, 0.42) * Math.Pow(sl, 0.3) * Math.Pow(1.0 / (n * se), 0.6);
                            double curbEff = 1.0;
                            if (inlet.LengthFeet < lt && lt > 0)
                            {
                                curbEff = 1.0 - Math.Pow(1.0 - (inlet.LengthFeet / lt), 1.8);
                            }
                            double additionalIntercept = qRemaining * curbEff * 0.5; // partial overlapping benefit
                            result.InterceptedFlowCfs = Math.Round(result.InterceptedFlowCfs + additionalIntercept, 4);
                            result.BypassFlowCfs = Math.Max(0.0, Math.Round(q - result.InterceptedFlowCfs, 4));
                            result.InterceptionEfficiency = Math.Round(result.InterceptedFlowCfs / q, 4);
                        }
                        break;
                    }
            }
        }

        private static void ComputeGrateOnGrade(
            InletConfiguration inlet,
            GutterFlowResult gutter,
            InletInterceptionResult result)
        {
            double q = result.TotalApproachFlowCfs;
            double v = gutter.VelocityFps;
            double l = inlet.LengthFeet;
            double eo = gutter.FrontalFlowRatio;

            // Splash-over velocity Vo per HEC-22 for standard grate types:
            double vo = GetGrateSplashOverVelocity(inlet.Grate, l);

            // Frontal flow ratio Rf:
            double rf = 1.0;
            if (v > vo)
            {
                rf = Math.Max(0.0, 1.0 - (0.09 * (v - vo)));
            }

            // Side flow ratio Rs = 1 / (1 + (0.15 * V^1.8) / (Sx * L^2.3))
            double denom = 1.0 + ((0.15 * Math.Pow(v, 1.8)) / (inlet.Gutter.CrossSlopeSx * Math.Pow(l, 2.3)));
            double rs = denom > 0 ? 1.0 / denom : 0.0;

            // Combined efficiency E = Rf * Eo + Rs * (1 - Eo)
            double efficiency = (rf * eo) + (rs * (1.0 - eo));
            double cloggedEff = efficiency * (1.0 - inlet.CloggingFactor);
            double qi = q * Math.Max(0.0, Math.Min(1.0, cloggedEff));
            double qb = Math.Max(0.0, q - qi);

            result.InterceptedFlowCfs = Math.Round(qi, 4);
            result.BypassFlowCfs = Math.Round(qb, 4);
            result.InterceptionEfficiency = Math.Round(q > 0 ? qi / q : 1.0, 4);

            result.Diagnostics["SplashOverVelocityVo"] = vo.ToString("F2", CultureInfo.InvariantCulture);
            result.Diagnostics["FrontalFlowEfficiencyRf"] = rf.ToString("F4", CultureInfo.InvariantCulture);
            result.Diagnostics["SideFlowEfficiencyRs"] = rs.ToString("F4", CultureInfo.InvariantCulture);
        }

        private static double GetGrateSplashOverVelocity(GrateType grate, double lengthFeet)
        {
            switch (grate)
            {
                case GrateType.P50:
                    return 2.22 * lengthFeet;
                case GrateType.P50x100:
                    return 1.95 * lengthFeet;
                case GrateType.P30:
                    return 2.80 * lengthFeet;
                case GrateType.CurvedVane:
                    return 3.10 * lengthFeet;
                case GrateType.TiltBar45:
                    return 2.40 * lengthFeet;
                case GrateType.TiltBar30:
                    return 2.65 * lengthFeet;
                case GrateType.Reticuline:
                    return 2.10 * lengthFeet;
                default:
                    return 2.00 * lengthFeet;
            }
        }

        private static void ComputeInSagInterception(
            InletConfiguration inlet,
            GutterFlowResult gutter,
            InletInterceptionResult result)
        {
            double q = result.TotalApproachFlowCfs;
            double depthFt = gutter.DepthAtCurbInches / 12.0;

            double capacityCfs = 0.0;
            InletFlowRegime regime = InletFlowRegime.Weir;

            switch (inlet.Type)
            {
                case InletType.CurbOpening:
                case InletType.SlottedDrain:
                    {
                        double h = inlet.CurbOpeningHeightInches / 12.0;
                        double l = inlet.LengthFeet;

                        // Weir regime: d <= h
                        double qWeir = CwWeirUs * l * Math.Pow(depthFt, 1.5);

                        // Orifice regime: d > 1.4 * h
                        double dEff = Math.Max(0.01, depthFt - (0.5 * h));
                        double qOrifice = CoOrificeUs * (h * l) * Math.Sqrt(2.0 * GravityFtPerSec2 * dEff);

                        if (depthFt <= h)
                        {
                            regime = InletFlowRegime.Weir;
                            capacityCfs = qWeir;
                        }
                        else if (depthFt >= 1.4 * h)
                        {
                            regime = InletFlowRegime.Orifice;
                            capacityCfs = qOrifice;
                        }
                        else
                        {
                            // Transition: linear blend between weir at h and orifice at 1.4h
                            regime = InletFlowRegime.Transitional;
                            double frac = (depthFt - h) / (0.4 * h);
                            double qWeirAtH = CwWeirUs * l * Math.Pow(h, 1.5);
                            double dEff14 = 1.4 * h - (0.5 * h);
                            double qOrificeAt14 = CoOrificeUs * (h * l) * Math.Sqrt(2.0 * GravityFtPerSec2 * dEff14);
                            capacityCfs = ((1.0 - frac) * qWeirAtH) + (frac * qOrificeAt14);
                        }
                        break;
                    }

                case InletType.Grate:
                    {
                        double p = (2.0 * inlet.WidthFeet) + inlet.LengthFeet; // Perimeter excluding curb side
                        double clearArea = inlet.LengthFeet * inlet.WidthFeet * 0.80; // 80% clear area

                        // Weir regime: d <= 0.4 ft
                        double qWeir = CwWeirUs * p * Math.Pow(depthFt, 1.5);

                        // Orifice regime: d > 1.4 ft
                        double qOrifice = CoOrificeUs * clearArea * Math.Sqrt(2.0 * GravityFtPerSec2 * Math.Max(0.01, depthFt));

                        if (depthFt <= 0.40)
                        {
                            regime = InletFlowRegime.Weir;
                            capacityCfs = qWeir;
                        }
                        else if (depthFt >= 1.40)
                        {
                            regime = InletFlowRegime.Orifice;
                            capacityCfs = qOrifice;
                        }
                        else
                        {
                            regime = InletFlowRegime.Transitional;
                            double frac = (depthFt - 0.40) / (1.40 - 0.40);
                            double qw04 = CwWeirUs * p * Math.Pow(0.40, 1.5);
                            double qo14 = CoOrificeUs * clearArea * Math.Sqrt(2.0 * GravityFtPerSec2 * 1.40);
                            capacityCfs = ((1.0 - frac) * qw04) + (frac * qo14);
                        }
                        break;
                    }

                case InletType.Combination:
                    {
                        // Grate in sag + curb opening in sag
                        double p = (2.0 * inlet.WidthFeet) + inlet.LengthFeet;
                        double clearArea = inlet.LengthFeet * inlet.WidthFeet * 0.80;
                        double qGrate = depthFt <= 0.40
                            ? CwWeirUs * p * Math.Pow(depthFt, 1.5)
                            : CoOrificeUs * clearArea * Math.Sqrt(2.0 * GravityFtPerSec2 * Math.Max(0.01, depthFt));

                        double h = inlet.CurbOpeningHeightInches / 12.0;
                        double l = inlet.LengthFeet;
                        double qCurb = depthFt <= h
                            ? CwWeirUs * l * Math.Pow(depthFt, 1.5)
                            : CoOrificeUs * (h * l) * Math.Sqrt(2.0 * GravityFtPerSec2 * Math.Max(0.01, depthFt - 0.5 * h));

                        capacityCfs = qGrate + qCurb;
                        regime = depthFt <= Math.Min(0.40, h) ? InletFlowRegime.Weir : InletFlowRegime.Orifice;
                        break;
                    }
            }

            // Apply clogging factor
            double effectiveCap = capacityCfs * (1.0 - inlet.CloggingFactor);
            double qi = Math.Min(q, effectiveCap);
            double qb = Math.Max(0.0, q - qi);
            if (effectiveCap >= q)
            {
                qi = q;
                qb = 0.0;
            }

            result.InterceptedFlowCfs = Math.Round(qi, 4);
            result.BypassFlowCfs = Math.Round(qb, 4);
            result.InterceptionEfficiency = Math.Round(q > 0 ? qi / q : 1.0, 4);
            result.FlowRegime = regime;
            result.Diagnostics["SagTheoreticalCapacityCfs"] = capacityCfs.ToString("F4", CultureInfo.InvariantCulture);
            result.Diagnostics["SagCloggedCapacityCfs"] = effectiveCap.ToString("F4", CultureInfo.InvariantCulture);
        }

        public static InletNetworkRoutingResult RouteInletNetwork(
            IReadOnlyDictionary<string, InletConfiguration> inlets,
            IReadOnlyDictionary<string, double> localRunoffPerInlet)
        {
            if (inlets == null) throw new ArgumentNullException(nameof(inlets));
            if (localRunoffPerInlet == null) throw new ArgumentNullException(nameof(localRunoffPerInlet));

            // Validate all inlets
            foreach (var kvp in inlets)
            {
                kvp.Value.Validate();
            }

            // Topological sort to route from upstream to downstream
            var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in inlets.Keys)
            {
                inDegree[key] = 0;
                adjacency[key] = new List<string>();
            }

            foreach (var kvp in inlets)
            {
                string id = kvp.Key;
                string ds = kvp.Value.DownstreamInletId;
                if (!string.IsNullOrWhiteSpace(ds))
                {
                    if (inlets.ContainsKey(ds))
                    {
                        adjacency[id].Add(ds);
                        inDegree[ds]++;
                    }
                    else
                    {
                        // Downstream ID is external or outlet; not routed further
                    }
                }
            }

            var queue = new Queue<string>();
            foreach (var kvp in inDegree)
            {
                if (kvp.Value == 0) queue.Enqueue(kvp.Key);
            }

            var sortedOrder = new List<string>();
            while (queue.Count > 0)
            {
                string u = queue.Dequeue();
                sortedOrder.Add(u);
                foreach (string v in adjacency[u])
                {
                    inDegree[v]--;
                    if (inDegree[v] == 0) queue.Enqueue(v);
                }
            }

            if (sortedOrder.Count != inlets.Count)
            {
                throw new InvalidOperationException("Circular dependency detected in inlet carryover downstream routing.");
            }

            var carryoverAccumulator = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in inlets.Keys) carryoverAccumulator[key] = 0.0;

            var results = new List<InletInterceptionResult>();
            double totalApproachSum = 0.0;
            double totalInterceptedSum = 0.0;
            double totalSystemBypass = 0.0;
            var spreadViolations = new List<string>();

            foreach (string inletId in sortedOrder)
            {
                var cfg = inlets[inletId];
                double localQ = localRunoffPerInlet.ContainsKey(inletId) ? localRunoffPerInlet[inletId] : 0.0;
                double carryoverQ = carryoverAccumulator[inletId];

                var res = ComputeInletInterception(cfg, localQ, carryoverQ);
                results.Add(res);

                totalApproachSum += localQ;
                totalInterceptedSum += res.InterceptedFlowCfs;

                if (res.SpreadExceeded)
                {
                    spreadViolations.Add(inletId);
                }

                if (!string.IsNullOrWhiteSpace(cfg.DownstreamInletId) && inlets.ContainsKey(cfg.DownstreamInletId))
                {
                    carryoverAccumulator[cfg.DownstreamInletId] += res.BypassFlowCfs;
                }
                else
                {
                    // Terminal bypass exiting system
                    totalSystemBypass += res.BypassFlowCfs;
                }
            }

            return new InletNetworkRoutingResult
            {
                InletResults = results,
                TotalApproachFlowCfs = Math.Round(totalApproachSum, 4),
                TotalInterceptedFlowCfs = Math.Round(totalInterceptedSum, 4),
                TotalSystemBypassFlowCfs = Math.Round(totalSystemBypass, 4),
                SpreadExceedanceInletIds = spreadViolations
            };
        }
    }
}
