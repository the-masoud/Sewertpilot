using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public static class StormFlowConduitDesignEngine
    {
        private const double ManningCoeffUs = 1.486;

        public static double ComputeFullFlowDischarge(double diameterFeet, double slope, double manningN)
        {
            if (diameterFeet <= 0.0 || slope <= 0.0 || manningN <= 0.0 ||
                double.IsNaN(diameterFeet) || double.IsInfinity(diameterFeet) ||
                double.IsNaN(slope) || double.IsInfinity(slope) ||
                double.IsNaN(manningN) || double.IsInfinity(manningN))
                throw new ArgumentOutOfRangeException("Diameter, slope, and Manning n must be positive finite values.");
            double area = Math.PI * Math.Pow(diameterFeet, 2.0) / 4.0;
            double hydraulicRadius = diameterFeet / 4.0;
            return (ManningCoeffUs / manningN) * area * Math.Pow(hydraulicRadius, 2.0 / 3.0) * Math.Sqrt(slope);
        }

        public static double ComputeFullFlowVelocity(double diameterFeet, double slope, double manningN)
        {
            if (diameterFeet <= 0.0 || slope <= 0.0 || manningN <= 0.0 ||
                double.IsNaN(diameterFeet) || double.IsInfinity(diameterFeet) ||
                double.IsNaN(slope) || double.IsInfinity(slope) ||
                double.IsNaN(manningN) || double.IsInfinity(manningN))
                throw new ArgumentOutOfRangeException("Diameter, slope, and Manning n must be positive finite values.");
            double hydraulicRadius = diameterFeet / 4.0;
            return (ManningCoeffUs / manningN) * Math.Pow(hydraulicRadius, 2.0 / 3.0) * Math.Sqrt(slope);
        }

        public static double ComputeMinimumSlopeForVelocity(double diameterFeet, double targetVelocityFps, double manningN)
        {
            if (diameterFeet <= 0.0 || targetVelocityFps <= 0.0 || manningN <= 0.0 ||
                double.IsNaN(diameterFeet) || double.IsInfinity(diameterFeet) ||
                double.IsNaN(targetVelocityFps) || double.IsInfinity(targetVelocityFps) ||
                double.IsNaN(manningN) || double.IsInfinity(manningN))
                throw new ArgumentOutOfRangeException("Diameter, target velocity, and Manning n must be positive finite values.");
            double hydraulicRadius = diameterFeet / 4.0;
            double denom = (ManningCoeffUs / manningN) * Math.Pow(hydraulicRadius, 2.0 / 3.0);
            if (denom <= 0.0) throw new InvalidOperationException("Denominator for minimum slope calculation must be positive.");
            double sqrtS = targetVelocityFps / denom;
            return Math.Pow(sqrtS, 2.0);
        }

        public static (double depthFeet, double velocityFps) SolvePartiallyFullFlow(
            double flowCfs, double diameterFeet, double slope, double manningN)
        {
            if (diameterFeet <= 0.0 || slope <= 0.0 || manningN <= 0.0 ||
                double.IsNaN(diameterFeet) || double.IsInfinity(diameterFeet) ||
                double.IsNaN(slope) || double.IsInfinity(slope) ||
                double.IsNaN(manningN) || double.IsInfinity(manningN))
                throw new ArgumentOutOfRangeException("Diameter, slope, and Manning n must be positive finite values.");
            if (double.IsNaN(flowCfs) || double.IsInfinity(flowCfs) || flowCfs < 0.0)
                throw new ArgumentOutOfRangeException(nameof(flowCfs), "Flow must be a non-negative finite value.");

            if (flowCfs == 0.0)
                return (0.0, 0.0);

            double qFull = ComputeFullFlowDischarge(diameterFeet, slope, manningN);
            if (qFull <= 0.0) return (0.0, 0.0);

            if (flowCfs >= qFull)
            {
                // Pipe flowing full or surcharged
                double aFull = Math.PI * Math.Pow(diameterFeet, 2.0) / 4.0;
                double v = aFull > 0 ? flowCfs / aFull : 0.0;
                return (Math.Round(diameterFeet, 4), Math.Round(v, 4));
            }

            // Bisection on central angle theta in (0, 2*PI)
            double lowTheta = 0.001;
            double highTheta = 2.0 * Math.PI;
            double d = diameterFeet;
            double sqrtS = Math.Sqrt(slope);

            for (int iter = 0; iter < 50; iter++)
            {
                double midTheta = 0.5 * (lowTheta + highTheta);
                double a = (d * d / 8.0) * (midTheta - Math.Sin(midTheta));
                double p = d * midTheta / 2.0;
                double r = p > 0 ? a / p : 0.0;
                double q = (ManningCoeffUs / manningN) * a * Math.Pow(r, 2.0 / 3.0) * sqrtS;

                if (Math.Abs(q - flowCfs) < 1e-6)
                {
                    lowTheta = highTheta = midTheta;
                    break;
                }
                if (q < flowCfs) lowTheta = midTheta;
                else highTheta = midTheta;
            }

            double theta = 0.5 * (lowTheta + highTheta);
            double depth = (d / 2.0) * (1.0 - Math.Cos(theta / 2.0));
            double area = (d * d / 8.0) * (theta - Math.Sin(theta));
            double velocity = area > 0 ? flowCfs / area : 0.0;

            return (Math.Round(depth, 4), Math.Round(velocity, 4));
        }

        public static HydraulicSizingResult SizeConduit(
            StormConduitSpecification spec,
            StormConduitConstraint constraints,
            double incomingUpstreamCrownElevation = double.NaN)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (constraints == null) throw new ArgumentNullException(nameof(constraints));
            spec.Validate();
            constraints.Validate();

            var result = new HydraulicSizingResult
            {
                ConduitId = spec.ConduitId,
                StartNodeId = spec.StartNodeId,
                StopNodeId = spec.StopNodeId,
                LengthFeet = spec.LengthFeet,
                DesignFlowCfs = spec.DesignFlowCfs
            };

            // Determine design slope
            double slope;
            if (spec.FixedSlope.HasValue)
            {
                slope = Math.Max(constraints.MinimumSlope, Math.Min(constraints.MaximumSlope, spec.FixedSlope.Value));
            }
            else
            {
                // Estimate ground slope: (Gstart - Gstop) / L
                double groundDrop = spec.GroundElevationStartFeet - spec.GroundElevationStopFeet;
                double naturalSlope = spec.LengthFeet > 0 ? groundDrop / spec.LengthFeet : constraints.MinimumSlope;
                slope = Math.Max(constraints.MinimumSlope, Math.Min(constraints.MaximumSlope, naturalSlope));
            }

            // Select smallest commercial diameter satisfying Qdes <= Qfull * MaxCapacityUtilizationRatio
            int selectedDiaInches = constraints.AvailableDiametersInches.Last();
            double selectedQFull = 0.0;
            double selectedVFull = 0.0;
            double selectedDepth = 0.0;
            double selectedVelocity = 0.0;
            double minRequiredSlope = 0.0;

            var sortedDiameters = constraints.AvailableDiametersInches.OrderBy(d => d).ToArray();
            bool foundSizing = false;

            foreach (int diaInches in sortedDiameters)
            {
                double dFt = diaInches / 12.0;
                double qFull = ComputeFullFlowDischarge(dFt, slope, constraints.ManningN);
                double vFull = ComputeFullFlowVelocity(dFt, slope, constraints.ManningN);
                double sMin = ComputeMinimumSlopeForVelocity(dFt, constraints.MinimumVelocityFps, constraints.ManningN);

                // Capacity check
                if (spec.DesignFlowCfs <= qFull * constraints.MaxCapacityUtilizationRatio)
                {
                    // Check self-cleansing slope requirement
                    double effectiveSlope = Math.Max(slope, sMin);
                    if (effectiveSlope > slope && !spec.FixedSlope.HasValue)
                    {
                        slope = Math.Min(effectiveSlope, constraints.MaximumSlope);
                        qFull = ComputeFullFlowDischarge(dFt, slope, constraints.ManningN);
                        vFull = ComputeFullFlowVelocity(dFt, slope, constraints.ManningN);
                    }

                    var partFlow = SolvePartiallyFullFlow(spec.DesignFlowCfs, dFt, slope, constraints.ManningN);

                    selectedDiaInches = diaInches;
                    selectedQFull = qFull;
                    selectedVFull = vFull;
                    selectedDepth = partFlow.depthFeet;
                    selectedVelocity = partFlow.velocityFps;
                    minRequiredSlope = sMin;
                    foundSizing = true;
                    break;
                }
            }

            if (!foundSizing)
            {
                // Fall back to largest diameter
                int maxDia = sortedDiameters.Last();
                double dFt = maxDia / 12.0;
                selectedDiaInches = maxDia;
                selectedQFull = ComputeFullFlowDischarge(dFt, slope, constraints.ManningN);
                selectedVFull = ComputeFullFlowVelocity(dFt, slope, constraints.ManningN);
                var partFlow = SolvePartiallyFullFlow(spec.DesignFlowCfs, dFt, slope, constraints.ManningN);
                selectedDepth = partFlow.depthFeet;
                selectedVelocity = partFlow.velocityFps;
                minRequiredSlope = ComputeMinimumSlopeForVelocity(dFt, constraints.MinimumVelocityFps, constraints.ManningN);
            }

            result.SelectedDiameterInches = selectedDiaInches;
            result.FullFlowCapacityCfs = Math.Round(selectedQFull, 4);
            result.FullFlowVelocityFps = Math.Round(selectedVFull, 4);
            result.DepthOfFlowFeet = Math.Round(selectedDepth, 4);
            result.ActualVelocityFps = Math.Round(selectedVelocity, 4);
            result.DesignSlope = Math.Round(slope, 6);
            result.MinimumRequiredSlope = Math.Round(minRequiredSlope, 6);

            // Invert and profile determination
            double diaFt = selectedDiaInches / 12.0;
            double drop = spec.LengthFeet * slope;

            double upInvert;
            double downInvert;
            double crownDrop = 0.0;

            if (spec.FixedUpstreamInvertFeet.HasValue)
            {
                upInvert = spec.FixedUpstreamInvertFeet.Value;
                downInvert = upInvert - drop;
            }
            else if (spec.FixedDownstreamInvertFeet.HasValue)
            {
                downInvert = spec.FixedDownstreamInvertFeet.Value;
                upInvert = downInvert + drop;
            }
            else
            {
                // Enforce crown-to-crown matching if incoming crown elevation is specified
                if (!double.IsNaN(incomingUpstreamCrownElevation))
                {
                    // Crown elevation matches incoming crown
                    upInvert = incomingUpstreamCrownElevation - diaFt;
                    downInvert = upInvert - drop;
                    crownDrop = 0.0; // Perfect soffit match
                }
                else
                {
                    // Set upstream invert to satisfy minimum cover
                    double maxUpInvert = spec.GroundElevationStartFeet - constraints.MinimumCoverFeet - diaFt;
                    upInvert = maxUpInvert;
                    downInvert = upInvert - drop;

                    // Ensure downstream cover is also satisfied
                    double maxDownInvert = spec.GroundElevationStopFeet - constraints.MinimumCoverFeet - diaFt;
                    if (downInvert > maxDownInvert)
                    {
                        // Drop inverts to meet downstream cover
                        downInvert = maxDownInvert;
                        upInvert = downInvert + drop;
                    }
                }
            }

            result.UpstreamInvertFeet = Math.Round(upInvert, 4);
            result.DownstreamInvertFeet = Math.Round(downInvert, 4);
            result.UpstreamCoverFeet = Math.Round(spec.GroundElevationStartFeet - (upInvert + diaFt), 4);
            result.DownstreamCoverFeet = Math.Round(spec.GroundElevationStopFeet - (downInvert + diaFt), 4);
            result.CrownDropFromUpstreamFeet = Math.Round(crownDrop, 4);

            // Compliance checks
            if (result.ActualVelocityFps < constraints.MinimumVelocityFps)
                result.VelocityCompliance = VelocityComplianceStatus.LowVelocity;
            else if (result.ActualVelocityFps > constraints.MaximumVelocityFps)
                result.VelocityCompliance = VelocityComplianceStatus.ExcessiveVelocity;
            else
                result.VelocityCompliance = VelocityComplianceStatus.Pass;

            if (result.UpstreamCoverFeet < constraints.MinimumCoverFeet || result.DownstreamCoverFeet < constraints.MinimumCoverFeet)
                result.CoverCompliance = CoverComplianceStatus.InsufficientCover;
            else
                result.CoverCompliance = CoverComplianceStatus.Pass;

            if (result.CapacityRatio > constraints.MaxCapacityUtilizationRatio)
                result.CapacityCompliance = CapacityComplianceStatus.Surcharged;
            else
                result.CapacityCompliance = CapacityComplianceStatus.Pass;

            result.Diagnostics["SelectedDiameterInches"] = selectedDiaInches.ToString(CultureInfo.InvariantCulture);
            result.Diagnostics["CapacityRatio"] = result.CapacityRatio.ToString("F4", CultureInfo.InvariantCulture);
            result.Diagnostics["DepthToDiameterRatio"] = result.DepthToDiameterRatio.ToString("F4", CultureInfo.InvariantCulture);
            result.Diagnostics["DesignSlopePercent"] = (slope * 100.0).ToString("F2", CultureInfo.InvariantCulture) + "%";
            result.Diagnostics["VelocityCompliance"] = result.VelocityCompliance.ToString();
            result.Diagnostics["CoverCompliance"] = result.CoverCompliance.ToString();
            result.Diagnostics["CapacityCompliance"] = result.CapacityCompliance.ToString();

            return result;
        }

        public static StormNetworkDesignResult DesignNetwork(
            IEnumerable<StormConduitSpecification> specifications,
            StormConduitConstraint constraints)
        {
            if (specifications == null) throw new ArgumentNullException(nameof(specifications));
            if (constraints == null) throw new ArgumentNullException(nameof(constraints));
            constraints.Validate();

            var specList = specifications.ToList();
            if (specList.Count == 0)
            {
                return new StormNetworkDesignResult();
            }

            foreach (var s in specList) s.Validate();

            // Topological sort based on StartNodeId -> StopNodeId
            var inDegree = new Dictionary<int, int>();
            var adj = new Dictionary<int, List<StormConduitSpecification>>();

            foreach (var s in specList)
            {
                if (!inDegree.ContainsKey(s.StartNodeId)) inDegree[s.StartNodeId] = 0;
                if (!inDegree.ContainsKey(s.StopNodeId)) inDegree[s.StopNodeId] = 0;
                if (!adj.ContainsKey(s.StartNodeId)) adj[s.StartNodeId] = new List<StormConduitSpecification>();
                adj[s.StartNodeId].Add(s);
                inDegree[s.StopNodeId]++;
            }

            var queue = new Queue<int>();
            foreach (var kvp in inDegree)
            {
                if (kvp.Value == 0) queue.Enqueue(kvp.Key);
            }

            var sortedConduits = new List<StormConduitSpecification>();
            var visitedNodes = new HashSet<int>();

            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                visitedNodes.Add(u);
                if (adj.ContainsKey(u))
                {
                    foreach (var edge in adj[u])
                    {
                        sortedConduits.Add(edge);
                        inDegree[edge.StopNodeId]--;
                        if (inDegree[edge.StopNodeId] == 0)
                        {
                            queue.Enqueue(edge.StopNodeId);
                        }
                    }
                }
            }

            if (sortedConduits.Count != specList.Count)
            {
                throw new InvalidOperationException("Circular dependency or disjoint cycle detected in storm conduit network.");
            }

            var nodeCrownElevation = new Dictionary<int, double>();
            var results = new List<HydraulicSizingResult>();
            var violations = new List<string>();

            double totalLength = 0.0;
            double totalFlow = 0.0;

            foreach (var spec in sortedConduits)
            {
                double incomingCrown = nodeCrownElevation.ContainsKey(spec.StartNodeId)
                    ? nodeCrownElevation[spec.StartNodeId]
                    : double.NaN;

                var res = SizeConduit(spec, constraints, incomingCrown);
                results.Add(res);

                totalLength += res.LengthFeet;
                totalFlow += res.DesignFlowCfs;

                // Update junction node crown elevation for downstream conduits (soffit alignment)
                double outgoingCrown = res.DownstreamCrownFeet;
                if (!nodeCrownElevation.ContainsKey(spec.StopNodeId) || outgoingCrown < nodeCrownElevation[spec.StopNodeId])
                {
                    nodeCrownElevation[spec.StopNodeId] = outgoingCrown;
                }

                if (res.VelocityCompliance != VelocityComplianceStatus.Pass)
                {
                    violations.Add(string.Format(CultureInfo.InvariantCulture,
                        "Conduit {0}: Velocity violation ({1}, {2:F2} fps).",
                        res.ConduitId, res.VelocityCompliance, res.ActualVelocityFps));
                }
                if (res.CoverCompliance != CoverComplianceStatus.Pass)
                {
                    violations.Add(string.Format(CultureInfo.InvariantCulture,
                        "Conduit {0}: Insufficient cover (Upstream: {1:F2} ft, Downstream: {2:F2} ft, Min: {3:F2} ft).",
                        res.ConduitId, res.UpstreamCoverFeet, res.DownstreamCoverFeet, constraints.MinimumCoverFeet));
                }
                if (res.CapacityCompliance != CapacityComplianceStatus.Pass)
                {
                    violations.Add(string.Format(CultureInfo.InvariantCulture,
                        "Conduit {0}: Capacity surcharged (Q/Qfull = {1:F2}).",
                        res.ConduitId, res.CapacityRatio));
                }
            }

            return new StormNetworkDesignResult
            {
                ConduitResults = results,
                TotalSystemLengthFeet = Math.Round(totalLength, 2),
                TotalDesignDischargeCfs = Math.Round(totalFlow, 2),
                Violations = violations
            };
        }
    }
}
