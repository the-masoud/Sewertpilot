using System;
using System.Collections.Generic;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public static class StormFlowVerificationEngine
    {
        public const double DefaultGravityFps2 = 32.174; // Standard US Customary gravity acceleration

        /// <summary>
        /// Computes velocity head (ft): h_v = V^2 / (2g)
        /// </summary>
        public static double ComputeVelocityHead(double velocityFps, double g = DefaultGravityFps2)
        {
            if (velocityFps < 0.0 || double.IsNaN(velocityFps) || double.IsInfinity(velocityFps))
                throw new ArgumentException("Velocity must be a non-negative finite number.", nameof(velocityFps));
            if (g <= 0.0 || double.IsNaN(g))
                throw new ArgumentException("Gravity acceleration must be positive.", nameof(g));

            return Math.Round(Math.Pow(velocityFps, 2.0) / (2.0 * g), 4);
        }

        /// <summary>
        /// Computes full-flow circular pipe velocity (fps): V = Q / A
        /// </summary>
        public static double ComputeVelocity(double flowCfs, double diameterFeet)
        {
            if (flowCfs < 0.0 || double.IsNaN(flowCfs) || double.IsInfinity(flowCfs))
                throw new ArgumentException("Flow must be non-negative and finite.", nameof(flowCfs));
            if (diameterFeet <= 0.0 || double.IsNaN(diameterFeet) || double.IsInfinity(diameterFeet))
                throw new ArgumentException("Diameter must be strictly positive.", nameof(diameterFeet));

            double area = Math.PI * Math.Pow(diameterFeet, 2.0) / 4.0;
            return Math.Round(flowCfs / area, 4);
        }

        /// <summary>
        /// Computes friction slope using Manning's equation for circular pipe flowing full or surcharged:
        /// S_f = [ (n * V) / (1.486 * R^(2/3)) ]^2
        /// </summary>
        public static double ComputeFrictionSlope(double flowCfs, double diameterFeet, double manningN)
        {
            if (flowCfs < 0.0 || double.IsNaN(flowCfs) || double.IsInfinity(flowCfs))
                throw new ArgumentException("Flow must be non-negative and finite.", nameof(flowCfs));
            if (diameterFeet <= 0.0 || double.IsNaN(diameterFeet) || double.IsInfinity(diameterFeet))
                throw new ArgumentException("Diameter must be strictly positive.", nameof(diameterFeet));
            if (manningN <= 0.0 || double.IsNaN(manningN) || double.IsInfinity(manningN))
                throw new ArgumentException("Manning's n must be strictly positive.", nameof(manningN));

            if (flowCfs == 0.0) return 0.0;

            double area = Math.PI * Math.Pow(diameterFeet, 2.0) / 4.0;
            double velocity = flowCfs / area;
            double hydraulicRadius = diameterFeet / 4.0; // Circular full-flow R = D/4

            double sf = Math.Pow((manningN * velocity) / (1.486 * Math.Pow(hydraulicRadius, 2.0 / 3.0)), 2.0);
            return Math.Round(sf, 6);
        }

        /// <summary>
        /// Computes Manning friction head loss (ft): h_f = S_f * L
        /// </summary>
        public static double ComputeFrictionHeadLoss(double frictionSlope, double lengthFeet)
        {
            if (frictionSlope < 0.0 || double.IsNaN(frictionSlope) || double.IsInfinity(frictionSlope))
                throw new ArgumentException("Friction slope must be non-negative.", nameof(frictionSlope));
            if (lengthFeet <= 0.0 || double.IsNaN(lengthFeet) || double.IsInfinity(lengthFeet))
                throw new ArgumentException("Length must be positive.", nameof(lengthFeet));

            return Math.Round(frictionSlope * lengthFeet, 4);
        }

        /// <summary>
        /// Computes minor loss (junction or bend) in feet: h_m = K * (V^2 / 2g)
        /// </summary>
        public static double ComputeMinorLoss(double lossCoefficientK, double velocityHeadFeet)
        {
            if (lossCoefficientK < 0.0 || double.IsNaN(lossCoefficientK) || double.IsInfinity(lossCoefficientK))
                throw new ArgumentException("Loss coefficient K must be non-negative.", nameof(lossCoefficientK));
            if (velocityHeadFeet < 0.0 || double.IsNaN(velocityHeadFeet) || double.IsInfinity(velocityHeadFeet))
                throw new ArgumentException("Velocity head must be non-negative.", nameof(velocityHeadFeet));

            return Math.Round(lossCoefficientK * velocityHeadFeet, 4);
        }

        /// <summary>
        /// Solves critical depth (ft) for a circular pipe using bisection where Fr = 1: g*A^3 - Q^2*T = 0.
        /// </summary>
        public static double SolveCriticalDepth(double diameterFeet, double flowCfs, double g = DefaultGravityFps2)
        {
            if (diameterFeet <= 0.0 || double.IsNaN(diameterFeet) || double.IsInfinity(diameterFeet))
                throw new ArgumentException("Diameter must be positive.", nameof(diameterFeet));
            if (flowCfs < 0.0 || double.IsNaN(flowCfs) || double.IsInfinity(flowCfs))
                throw new ArgumentException("Flow must be non-negative.", nameof(flowCfs));

            if (flowCfs == 0.0) return 0.0;

            double low = 0.001 * diameterFeet;
            double high = 0.999 * diameterFeet;

            // Check if flow exceeds capacity of circular section before crown
            double thetaHigh = 2.0 * Math.Acos(1.0 - (2.0 * high / diameterFeet));
            double aHigh = (Math.Pow(diameterFeet, 2.0) / 8.0) * (thetaHigh - Math.Sin(thetaHigh));
            double tHigh = diameterFeet * Math.Sin(thetaHigh / 2.0);
            if ((g * Math.Pow(aHigh, 3.0)) - (Math.Pow(flowCfs, 2.0) * tHigh) < 0.0)
            {
                return diameterFeet; // Critical depth equals full pipe diameter (pipe is surcharged)
            }

            for (int i = 0; i < 40; i++)
            {
                double mid = (low + high) / 2.0;
                double theta = 2.0 * Math.Acos(Math.Max(-1.0, Math.Min(1.0, 1.0 - (2.0 * mid / diameterFeet))));
                double a = (Math.Pow(diameterFeet, 2.0) / 8.0) * (theta - Math.Sin(theta));
                double t = diameterFeet * Math.Sin(theta / 2.0);

                double f = (g * Math.Pow(a, 3.0)) - (Math.Pow(flowCfs, 2.0) * t);
                if (Math.Abs(f) < 1e-5 || (high - low) < 1e-4)
                    return Math.Round(mid, 4);

                if (f < 0.0)
                    low = mid;
                else
                    high = mid;
            }

            return Math.Round((low + high) / 2.0, 4);
        }

        /// <summary>
        /// Executes deterministic hydraulic verification of a storm sewer network:
        /// 1. Verifies input integrity and detects topological cycles.
        /// 2. Computes outfall boundary HGL/EGL from tailwater or critical depth.
        /// 3. Propagates HGL/EGL profiles upstream accumulating Manning friction & junction minor losses.
        /// 4. Classifies conduit surcharge (GravityOpenChannel, TransitionPressure, SeverelySurcharged).
        /// 5. Classifies node flooding (SafeFreeboard, FreeboardExceeded, SurfaceFlooding) & computes rim overflow.
        /// 6. Identifies hydraulic bottlenecks and generates diameter enlargement remediation proposals.
        /// </summary>
        public static StormNetworkVerificationResult VerifyNetwork(
            IEnumerable<StormJunctionSpecification> junctions,
            IEnumerable<HydraulicSizingResult> conduits,
            StormVerificationCriteria criteria = null)
        {
            if (junctions == null) throw new ArgumentNullException(nameof(junctions));
            if (conduits == null) throw new ArgumentNullException(nameof(conduits));

            criteria = criteria ?? new StormVerificationCriteria();
            criteria.Validate();

            var junctionList = junctions.ToList();
            var conduitList = conduits.ToList();

            if (junctionList.Count == 0)
                throw new ArgumentException("Junction collection cannot be empty.", nameof(junctions));
            if (conduitList.Count == 0)
                throw new ArgumentException("Conduit collection cannot be empty.", nameof(conduits));

            // Validate junctions
            var junctionMap = new Dictionary<int, StormJunctionSpecification>();
            foreach (var j in junctionList)
            {
                j.Validate();
                if (junctionMap.ContainsKey(j.NodeId))
                    throw new ArgumentException($"Duplicate junction NodeId: {j.NodeId}");
                junctionMap[j.NodeId] = j;
            }

            // Validate conduits and ensure node references exist
            foreach (var c in conduitList)
            {
                if (string.IsNullOrWhiteSpace(c.ConduitId))
                    throw new ArgumentException("ConduitId cannot be null or empty.");
                if (!junctionMap.ContainsKey(c.StartNodeId))
                    throw new ArgumentException($"Conduit '{c.ConduitId}' references unknown StartNodeId: {c.StartNodeId}");
                if (!junctionMap.ContainsKey(c.StopNodeId))
                    throw new ArgumentException($"Conduit '{c.ConduitId}' references unknown StopNodeId: {c.StopNodeId}");
                if (c.StartNodeId == c.StopNodeId)
                    throw new ArgumentException($"Conduit '{c.ConduitId}' has identical start and stop nodes (self-loop).");
                if (c.LengthFeet <= 0.0 || double.IsNaN(c.LengthFeet) || double.IsInfinity(c.LengthFeet))
                    throw new ArgumentException($"Conduit '{c.ConduitId}' length must be positive.", nameof(c.LengthFeet));
                if (c.SelectedDiameterInches <= 0)
                    throw new ArgumentException($"Conduit '{c.ConduitId}' diameter must be positive.", nameof(c.SelectedDiameterInches));
                if (c.DesignFlowCfs < 0.0 || double.IsNaN(c.DesignFlowCfs) || double.IsInfinity(c.DesignFlowCfs))
                    throw new ArgumentException($"Conduit '{c.ConduitId}' design flow must be non-negative.", nameof(c.DesignFlowCfs));
            }

            // Topological ordering and cycle detection
            var outgoing = new Dictionary<int, List<HydraulicSizingResult>>();
            var incoming = new Dictionary<int, List<HydraulicSizingResult>>();
            foreach (var jId in junctionMap.Keys)
            {
                outgoing[jId] = new List<HydraulicSizingResult>();
                incoming[jId] = new List<HydraulicSizingResult>();
            }
            foreach (var c in conduitList)
            {
                outgoing[c.StartNodeId].Add(c);
                incoming[c.StopNodeId].Add(c);
            }

            // Kahn's algorithm or DFS cycle detection
            var inDegree = new Dictionary<int, int>();
            foreach (var jId in junctionMap.Keys)
                inDegree[jId] = incoming[jId].Count;

            var queue = new Queue<int>(junctionMap.Keys.Where(id => inDegree[id] == 0));
            int visitedCount = 0;
            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                visitedCount++;
                foreach (var edge in outgoing[curr])
                {
                    inDegree[edge.StopNodeId]--;
                    if (inDegree[edge.StopNodeId] == 0)
                        queue.Enqueue(edge.StopNodeId);
                }
            }

            if (visitedCount < junctionMap.Count)
                throw new InvalidOperationException("Circular topological network loop detected; hydraulic backwater profile cannot be evaluated.");

            // Identify Outfall Node(s)
            var outfallNodes = junctionMap.Values
                .Where(j => j.IsOutfall || outgoing[j.NodeId].Count == 0)
                .ToList();

            if (outfallNodes.Count == 0)
                throw new InvalidOperationException("No outfall node found in the storm network.");

            // Initialize Node Hydraulic States
            var nodeStateMap = new Dictionary<int, StormNodeHydraulicState>();
            foreach (var j in junctionMap.Values)
            {
                nodeStateMap[j.NodeId] = new StormNodeHydraulicState
                {
                    NodeId = j.NodeId,
                    Label = j.Label ?? $"Node-{j.NodeId}",
                    RimElevationFeet = j.RimElevationFeet,
                    InvertElevationFeet = j.InvertElevationFeet,
                    RequiredFreeboardFeet = criteria.MinimumFreeboardFeet,
                    IsOutfall = j.IsOutfall || outgoing[j.NodeId].Count == 0,
                    HglFeet = double.MinValue // Unresolved initial state
                };
            }

            // Initialize conduit states map
            var conduitStateMap = new Dictionary<string, StormConduitHydraulicState>();
            foreach (var c in conduitList)
            {
                double dFt = c.SelectedDiameterFeet;
                double v = ComputeVelocity(c.DesignFlowCfs, dFt);
                double hv = ComputeVelocityHead(v, criteria.GravitationalAcceleration);
                double sf = ComputeFrictionSlope(c.DesignFlowCfs, dFt, criteria.ManningN);
                double hf = ComputeFrictionHeadLoss(sf, c.LengthFeet);

                // Junction loss at StopNodeId
                var stopJunction = junctionMap[c.StopNodeId];
                double kLoss = stopJunction.JunctionLossCoefficient > 0.0
                    ? stopJunction.JunctionLossCoefficient
                    : criteria.DefaultJunctionLossK;
                double hm = ComputeMinorLoss(kLoss, hv);

                var state = new StormConduitHydraulicState
                {
                    ConduitId = c.ConduitId,
                    StartNodeId = c.StartNodeId,
                    StopNodeId = c.StopNodeId,
                    LengthFeet = c.LengthFeet,
                    DiameterInches = c.SelectedDiameterInches,
                    FlowCfs = c.DesignFlowCfs,
                    VelocityFps = v,
                    VelocityHeadFeet = hv,
                    FrictionSlope = sf,
                    FrictionHeadLossFeet = hf,
                    MinorLossFeet = hm,
                    UpstreamInvertFeet = c.UpstreamInvertFeet,
                    DownstreamInvertFeet = c.DownstreamInvertFeet
                };
                conduitStateMap[c.ConduitId] = state;
            }

            // Step 1: Establish Outfall Boundary Conditions
            foreach (var outfall in outfallNodes)
            {
                var nState = nodeStateMap[outfall.NodeId];
                double boundaryHgl;

                if (outfall.TailwaterElevationFeet.HasValue)
                {
                    boundaryHgl = Math.Max(outfall.InvertElevationFeet, outfall.TailwaterElevationFeet.Value);
                    nState.Diagnostics["Boundary"] = $"Prescribed tailwater elevation: {outfall.TailwaterElevationFeet.Value:F2} ft";
                }
                else
                {
                    // Free outfall: determine critical depth of incoming conduits
                    double maxDc = 0.0;
                    foreach (var inConduit in incoming[outfall.NodeId])
                    {
                        double dc = SolveCriticalDepth(inConduit.SelectedDiameterFeet, inConduit.DesignFlowCfs, criteria.GravitationalAcceleration);
                        if (dc > maxDc) maxDc = dc;
                    }
                    if (maxDc <= 0.0) maxDc = 0.50; // Minimal default depth if no flow

                    boundaryHgl = Math.Round(outfall.InvertElevationFeet + maxDc, 4);
                    nState.Diagnostics["Boundary"] = $"Free discharge critical depth: {maxDc:F2} ft";
                }

                nState.HglFeet = boundaryHgl;
                nState.EglFeet = boundaryHgl; // Stagnant or exit pool EGL ~ HGL
            }

            // Step 2: Propagate HGL and EGL upstream from outfalls
            // Traverse nodes in reverse topological order (from downstream to upstream)
            var reverseQueue = new Queue<int>(outfallNodes.Select(o => o.NodeId));
            var resolvedNodes = new HashSet<int>(reverseQueue);

            while (reverseQueue.Count > 0)
            {
                int currNodeId = reverseQueue.Dequeue();
                var currNodeState = nodeStateMap[currNodeId];

                // Propagate upstream along all incoming conduits
                foreach (var inConduit in incoming[currNodeId])
                {
                    var cState = conduitStateMap[inConduit.ConduitId];
                    cState.DownstreamHglFeet = currNodeState.HglFeet;

                    // Hydraulic Grade Line upstream of conduit:
                    // HGL_up = HGL_down + h_f + h_m
                    double hglUp = Math.Round(cState.DownstreamHglFeet + cState.TotalHeadLossFeet, 4);

                    // Physical floor constraint: HGL cannot be lower than upstream invert + critical depth
                    double dcUp = SolveCriticalDepth(cState.DiameterFeet, cState.FlowCfs, criteria.GravitationalAcceleration);
                    double minHglUp = cState.UpstreamInvertFeet + dcUp;
                    if (hglUp < minHglUp)
                    {
                        hglUp = Math.Round(minHglUp, 4);
                    }

                    cState.UpstreamHglFeet = hglUp;

                    // Evaluate Surcharge State for conduit
                    if (cState.UpstreamHglFeet > cState.UpstreamCrownFeet && cState.DownstreamHglFeet > cState.DownstreamCrownFeet)
                    {
                        var upJunc = junctionMap[cState.StartNodeId];
                        if (cState.UpstreamHglFeet >= (upJunc.RimElevationFeet - 0.50))
                            cState.SurchargeState = ConduitSurchargeState.SeverelySurcharged;
                        else
                            cState.SurchargeState = ConduitSurchargeState.TransitionPressure;
                    }
                    else if (cState.UpstreamHglFeet > cState.UpstreamCrownFeet || cState.DownstreamHglFeet > cState.DownstreamCrownFeet)
                    {
                        cState.SurchargeState = ConduitSurchargeState.TransitionPressure;
                    }
                    else
                    {
                        cState.SurchargeState = ConduitSurchargeState.GravityOpenChannel;
                    }

                    // Update upstream node HGL
                    var upNodeState = nodeStateMap[cState.StartNodeId];
                    if (upNodeState.HglFeet < hglUp)
                    {
                        upNodeState.HglFeet = hglUp;
                        upNodeState.EglFeet = Math.Round(hglUp + cState.VelocityHeadFeet, 4);
                    }

                    if (!resolvedNodes.Contains(cState.StartNodeId))
                    {
                        resolvedNodes.Add(cState.StartNodeId);
                        reverseQueue.Enqueue(cState.StartNodeId);
                    }
                }
            }

            // Step 3: Classify Node Flooding States & Evaluate Freeboard
            var result = new StormNetworkVerificationResult();
            double totalHeadLoss = 0.0;
            double maxHgl = double.MinValue;

            foreach (var n in nodeStateMap.Values.OrderBy(n => n.NodeId))
            {
                if (n.HglFeet > maxHgl) maxHgl = n.HglFeet;

                if (n.FreeboardFeet <= 0.0)
                {
                    n.FloodingState = NodeFloodingState.SurfaceFlooding;
                    // Compute weir overflow rate over manhole rim: Q = C_w * (pi * D_rim) * Head^1.5
                    double headAboveRim = Math.Max(0.0, n.HglFeet - n.RimElevationFeet);
                    double rimPerimeter = Math.PI * 2.0; // Standard 24-inch manhole ring
                    n.OverflowDischargeCfs = Math.Round(3.0 * rimPerimeter * Math.Pow(headAboveRim, 1.5), 2);

                    result.ViolationsSummary.Add(
                        $"Node '{n.Label}' (ID {n.NodeId}) SURFACE FLOODING: HGL ({n.HglFeet:F2} ft) >= Rim ({n.RimElevationFeet:F2} ft). Overflow: {n.OverflowDischargeCfs:F2} cfs.");
                }
                else if (n.FreeboardFeet < criteria.MinimumFreeboardFeet)
                {
                    n.FloodingState = NodeFloodingState.FreeboardExceeded;
                    result.ViolationsSummary.Add(
                        $"Node '{n.Label}' (ID {n.NodeId}) FREEBOARD VIOLATION: Margin ({n.FreeboardFeet:F2} ft) < Required ({criteria.MinimumFreeboardFeet:F2} ft).");
                }
                else
                {
                    n.FloodingState = NodeFloodingState.SafeFreeboard;
                }

                result.NodeStates.Add(n);
            }

            foreach (var c in conduitStateMap.Values.OrderBy(c => c.ConduitId, StringComparer.Ordinal))
            {
                totalHeadLoss += c.TotalHeadLossFeet;
                if (c.IsSurcharged)
                {
                    result.ViolationsSummary.Add(
                        $"Conduit '{c.ConduitId}' SURCHARGED: Upstream HGL ({c.UpstreamHglFeet:F2} ft) > Crown ({c.UpstreamCrownFeet:F2} ft). Surcharge State: {c.SurchargeState}.");
                }
                result.ConduitStates.Add(c);
            }

            result.TotalHeadLossSystemFeet = Math.Round(totalHeadLoss, 4);
            result.MaxHglElevationFeet = Math.Round(maxHgl, 4);

            // Step 4: Hydraulic Bottleneck Identification & Sizing Iteration Proposals
            // If a node suffers flooding or freeboard violation, examine outgoing conduit(s) for diameter enlargement
            var nonCompliantNodes = result.NodeStates
                .Where(n => n.FloodingState == NodeFloodingState.SurfaceFlooding || n.FloodingState == NodeFloodingState.FreeboardExceeded)
                .ToList();

            foreach (var floodedNode in nonCompliantNodes)
            {
                var outConduits = outgoing[floodedNode.NodeId];
                foreach (var outConduit in outConduits)
                {
                    var cState = conduitStateMap[outConduit.ConduitId];
                    if (cState.FrictionHeadLossFeet <= 0.05) continue; // Minor head loss, not the primary bottleneck

                    // Test larger commercial diameters
                    int currentDiam = cState.DiameterInches;
                    var largerDiameters = criteria.AvailableDiametersInches
                        .Where(d => d > currentDiam)
                        .OrderBy(d => d);

                    foreach (int candidateDiam in largerDiameters)
                    {
                        double candDiamFt = candidateDiam / 12.0;
                        double candV = ComputeVelocity(cState.FlowCfs, candDiamFt);
                        double candHv = ComputeVelocityHead(candV, criteria.GravitationalAcceleration);
                        double candSf = ComputeFrictionSlope(cState.FlowCfs, candDiamFt, criteria.ManningN);
                        double candHf = ComputeFrictionHeadLoss(candSf, cState.LengthFeet);
                        double candHm = ComputeMinorLoss(junctionMap[cState.StopNodeId].JunctionLossCoefficient, candHv);
                        double candTotalLoss = candHf + candHm;

                        double resolvedHglUp = Math.Round(cState.DownstreamHglFeet + candTotalLoss, 4);
                        double resolvedFreeboard = Math.Round(floodedNode.RimElevationFeet - resolvedHglUp, 4);

                        if (resolvedFreeboard >= criteria.MinimumFreeboardFeet)
                        {
                            result.BottleneckProposals.Add(new BottleneckRemediationProposal
                            {
                                ConduitId = cState.ConduitId,
                                CurrentDiameterInches = currentDiam,
                                ProposedDiameterInches = candidateDiam,
                                FlowCfs = cState.FlowCfs,
                                CurrentVelocityFps = cState.VelocityFps,
                                ProposedVelocityFps = candV,
                                CurrentFrictionHeadLossFeet = cState.FrictionHeadLossFeet,
                                ProposedFrictionHeadLossFeet = candHf,
                                ResolvedUpstreamHglFeet = resolvedHglUp,
                                ResolvedUpstreamFreeboardFeet = resolvedFreeboard,
                                RemediationDescription = $"Upsize conduit '{cState.ConduitId}' from {currentDiam}\" to {candidateDiam}\" reduces friction head loss by {Math.Round(cState.FrictionHeadLossFeet - candHf, 2)} ft, restoring upstream node '{floodedNode.Label}' freeboard to {resolvedFreeboard:F2} ft."
                            });
                            break; // Smallest adequate enlargement found
                        }
                    }
                }
            }

            return result;
        }
    }
}
