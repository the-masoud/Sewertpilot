using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class GravityVerticalFeasibilityEngine
    {
        private sealed class VariableState
        {
            public GravityConduitEnd End { get; }
            public double MinInvert { get; set; } = -1e9;
            public double MaxInvert { get; set; } = 1e9;
            public double? ExactInvert { get; set; }
            public StandardProvenance ExactProv { get; set; }
            public StandardProvenance MinProv { get; set; }
            public StandardProvenance MaxProv { get; set; }

            public VariableState(GravityConduitEnd end)
            {
                End = end;
            }
        }

        private static bool IsCompatibleLengthUnit(SourceUnitIdentity unit)
        {
            if (unit == null) return false;
            var expected = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            return expected.Equals(unit);
        }

        private static bool IsFinitePositive(double? v) =>
            v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value) && v.Value > 0;

        private static double? GetEffectiveHeight(GravityNetworkConduit c)
        {
            if (c?.ExistingPhysicalState == null) return null;
            return c.ExistingPhysicalState.EffectiveSectionHeight ?? c.ExistingPhysicalState.Rise;
        }

        public VerticalFeasibilityResult Analyze(
            GravityNetworkSnapshot network,
            GravityVerticalDesignConstraintSet constraintSet)
        {
            if (network == null) throw new ArgumentNullException(nameof(network));
            if (constraintSet == null) throw new ArgumentNullException(nameof(constraintSet));

            var conflicts = new List<VerticalConstraintConflict>();
            var decisionRequests = new List<EngineeringDecisionRequest>();
            var incompleteItems = new List<string>();

            // 1. Validate conduit lengths and orientations
            foreach (var c in network.Conduits)
            {
                double? len = c.ExistingPhysicalState?.Length;
                if (!len.HasValue || double.IsNaN(len.Value) || double.IsInfinity(len.Value) || len.Value <= 0.0)
                {
                    throw new ArgumentException(
                        string.Format(CultureInfo.InvariantCulture, "Conduit {0} has invalid length {1}. Length must be finite and positive.",
                            c.SourceElementId, len.HasValue ? len.Value.ToString(CultureInfo.InvariantCulture) : "null"), nameof(network));
                }

                if (!constraintSet.TryGetOrientation(c.SourceElementId, out var o))
                {
                    var facts = new Dictionary<string, string>
                    {
                        { "ConduitId", c.SourceElementId.ToString(CultureInfo.InvariantCulture) },
                        { "StartNodeId", c.StartNodeId.ToString(CultureInfo.InvariantCulture) },
                        { "StopNodeId", c.StopNodeId.ToString(CultureInfo.InvariantCulture) },
                        { "Label", c.Label ?? string.Empty }
                    };
                    var consequences = new Dictionary<string, string>
                    {
                        { "StartToStop", string.Format(CultureInfo.InvariantCulture, "Slope evaluated from StartNode ({0}) to StopNode ({1})", c.StartNodeId, c.StopNodeId) },
                        { "StopToStart", string.Format(CultureInfo.InvariantCulture, "Slope evaluated from StopNode ({0}) to StartNode ({1})", c.StopNodeId, c.StartNodeId) }
                    };
                    decisionRequests.Add(new EngineeringDecisionRequest(
                        string.Format(CultureInfo.InvariantCulture, "Specify design orientation for Conduit {0}", c.SourceElementId),
                        facts,
                        new[] { "StartToStop", "StopToStart" },
                        consequences,
                        new[] { "Feasibility evaluation for conduits with explicit design orientation" }));
                }
                else
                {
                    bool validEndpoints = (o.FromNodeId == c.StartNodeId && o.ToNodeId == c.StopNodeId) ||
                                         (o.FromNodeId == c.StopNodeId && o.ToNodeId == c.StartNodeId);
                    if (!validEndpoints)
                    {
                        throw new ArgumentException(
                            string.Format(CultureInfo.InvariantCulture, "Orientation for Conduit {0} references non-endpoint nodes ({1} -> {2}). Conduit actual endpoints are StartNode {3} and StopNode {4}.",
                                c.SourceElementId, o.FromNodeId, o.ToNodeId, c.StartNodeId, c.StopNodeId));
                    }
                }
            }

            // 2. Validate outfall design intent
            foreach (var n in network.Nodes)
            {
                if (n.Kind == GravityNodeKind.Outfall)
                {
                    bool hasOutfallConstraint = constraintSet.OutfallConstraints.Any(oc => oc.OutfallNodeId == n.SourceElementId);
                    if (!hasOutfallConstraint)
                    {
                        var facts = new Dictionary<string, string>
                        {
                            { "OutfallNodeId", n.SourceElementId.ToString(CultureInfo.InvariantCulture) },
                            { "Label", n.Label ?? string.Empty }
                        };
                        var consequences = new Dictionary<string, string>
                        {
                            { "FixedInvert", "Fixes outfall boundary elevation" },
                            { "FreeOutfall", "Permits connected conduit end to vary within slope and cover limits" },
                            { "ConnectedEndpointConstraint", "Links connected conduit end directly to outfall boundary" }
                        };
                        decisionRequests.Add(new EngineeringDecisionRequest(
                            string.Format(CultureInfo.InvariantCulture, "Specify vertical design constraint for Outfall {0}", n.SourceElementId),
                            facts,
                            new[] { "FixedInvert", "FreeOutfall", "ConnectedEndpointConstraint" },
                            consequences,
                            new[] { "Upstream vertical profiles" }));
                    }
                }
            }

            // 3. Unit verification: verify all input quantities are compatible with canonical length unit (Feet)
            // An explicit connected outfall endpoint is an exact P13 elevation, not just
            // evidence that the outfall decision request was answered. A protected node
            // invert alone must NOT silently force a pipe-end invert (drops may exist).
            var effectiveFixedConstraints = constraintSet.FixedConstraints.Concat(
                constraintSet.OutfallConstraints.Where(oc => oc.ConnectedEndpointConstraint != null)
                    .Select(oc => oc.ConnectedEndpointConstraint)).ToArray();
            foreach (var fc in effectiveFixedConstraints)
            {
                if (fc.InvertElevation?.Unit != null && !IsCompatibleLengthUnit(fc.InvertElevation.Unit))
                {
                    conflicts.Add(new VerticalConstraintConflict(
                        "INCOMPATIBLE_VERTICAL_UNITS",
                        new[] { fc.ConduitEnd.ToString() },
                        new[] { "Fixed:" + fc.ConduitEnd },
                        new Dictionary<string, double>(),
                        new[] { fc.Provenance },
                        string.Format(CultureInfo.InvariantCulture, "Fixed elevation on {0} uses incompatible unit: {1}", fc.ConduitEnd, fc.InvertElevation.Unit)));
                }
            }
            foreach (var cov in constraintSet.CoverConstraints)
            {
                if (cov.GroundElevation?.Unit != null && !IsCompatibleLengthUnit(cov.GroundElevation.Unit))
                {
                    conflicts.Add(new VerticalConstraintConflict(
                        "INCOMPATIBLE_VERTICAL_UNITS",
                        new[] { cov.ConduitEnd.ToString() },
                        new[] { "Cover:" + cov.ConduitEnd },
                        new Dictionary<string, double>(),
                        new[] { cov.Provenance },
                        string.Format(CultureInfo.InvariantCulture, "Cover ground elevation on {0} uses incompatible unit: {1}", cov.ConduitEnd, cov.GroundElevation.Unit)));
                }
                if (cov.MinimumCover?.Unit != null && !IsCompatibleLengthUnit(cov.MinimumCover.Unit))
                {
                    conflicts.Add(new VerticalConstraintConflict(
                        "INCOMPATIBLE_VERTICAL_UNITS",
                        new[] { cov.ConduitEnd.ToString() },
                        new[] { "Cover:" + cov.ConduitEnd },
                        new Dictionary<string, double>(),
                        new[] { cov.Provenance },
                        string.Format(CultureInfo.InvariantCulture, "Cover minimum cover on {0} uses incompatible unit: {1}", cov.ConduitEnd, cov.MinimumCover.Unit)));
                }
            }

            // 4. Completeness checks for CrownMatch and Cover
            foreach (var mc in constraintSet.MatchConstraints)
            {
                if (mc.Mode == VerticalMatchingMode.CrownMatch)
                {
                    foreach (var end in mc.ParticipatingConduitEnds)
                    {
                        var c = network.Conduits.FirstOrDefault(x => x.SourceElementId == end.ConduitId);
                        double? h = GetEffectiveHeight(c);
                        if (!IsFinitePositive(h))
                        {
                            incompleteItems.Add(string.Format(CultureInfo.InvariantCulture,
                                "VERTICAL_INPUT_INCOMPLETE: CrownMatch on Node {0} requires explicit Rise for Conduit {1}", mc.NodeId, end.ConduitId));
                        }
                    }
                }
            }
            foreach (var cov in constraintSet.CoverConstraints)
            {
                var c = network.Conduits.FirstOrDefault(x => x.SourceElementId == cov.ConduitEnd.ConduitId);
                double? h = GetEffectiveHeight(c);
                if (!IsFinitePositive(h))
                {
                    incompleteItems.Add(string.Format(CultureInfo.InvariantCulture,
                        "VERTICAL_INPUT_INCOMPLETE: Cover constraint requires explicit Rise for Conduit {0}", cov.ConduitEnd.ConduitId));
                }
                if (!cov.GroundElevation.HasValue)
                {
                    incompleteItems.Add(string.Format(CultureInfo.InvariantCulture,
                        "VERTICAL_INPUT_INCOMPLETE: Ground elevation missing for Cover constraint on Conduit {0}", cov.ConduitEnd.ConduitId));
                }
                if (!cov.MinimumCover.HasValue)
                {
                    incompleteItems.Add(string.Format(CultureInfo.InvariantCulture,
                        "VERTICAL_INPUT_INCOMPLETE: Minimum cover criterion missing for Conduit {0}", cov.ConduitEnd.ConduitId));
                }
            }

            // 5. Initialize variable table: two variables per conduit (Start and Stop)
            var vars = new Dictionary<GravityConduitEnd, VariableState>();
            foreach (var c in network.Conduits)
            {
                var startEnd = new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Start, c.StartNodeId);
                var stopEnd = new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Stop, c.StopNodeId);
                vars[startEnd] = new VariableState(startEnd);
                vars[stopEnd] = new VariableState(stopEnd);
            }

            // 6. Apply explicit fixed conduit ends AND connected outfall ends.
            foreach (var fc in effectiveFixedConstraints)
            {
                if (!vars.TryGetValue(fc.ConduitEnd, out var vs)) continue;
                if (!fc.InvertElevation.HasValue) continue;
                if (fc.InvertElevation.Unit != null && !IsCompatibleLengthUnit(fc.InvertElevation.Unit)) continue;

                double val = fc.InvertElevation.NumericValue.Value;
                if (vs.ExactInvert.HasValue)
                {
                    if (Math.Abs(vs.ExactInvert.Value - val) > 1e-4)
                    {
                        conflicts.Add(new VerticalConstraintConflict(
                            "INFEASIBLE_VERTICAL_CONSTRAINTS",
                            new[] { fc.ConduitEnd.ToString() },
                            new[] { "Fixed:" + fc.ConduitEnd },
                            new Dictionary<string, double> { { "ExistingExact", vs.ExactInvert.Value }, { "AttemptedExact", val } },
                            new[] { vs.ExactProv, fc.Provenance },
                            string.Format(CultureInfo.InvariantCulture, "Contradictory exact fixed elevations on {0}: {1} vs {2}", fc.ConduitEnd, vs.ExactInvert.Value, val)));
                    }
                }
                else
                {
                    vs.ExactInvert = val;
                    vs.ExactProv = fc.Provenance;
                    vs.MinInvert = Math.Max(vs.MinInvert, val);
                    vs.MaxInvert = Math.Min(vs.MaxInvert, val);
                }
            }

            // 7. Apply cover constraints: GroundToCrown -> Invert <= GroundElevation - Rise - MinimumCover
            foreach (var cov in constraintSet.CoverConstraints)
            {
                if (!vars.TryGetValue(cov.ConduitEnd, out var vs)) continue;
                var c = network.Conduits.FirstOrDefault(x => x.SourceElementId == cov.ConduitEnd.ConduitId);
                double? h = GetEffectiveHeight(c);
                if (!IsFinitePositive(h) || !cov.GroundElevation.HasValue || !cov.MinimumCover.HasValue)
                    continue;
                if ((cov.GroundElevation.Unit != null && !IsCompatibleLengthUnit(cov.GroundElevation.Unit)) ||
                    (cov.MinimumCover.Unit != null && !IsCompatibleLengthUnit(cov.MinimumCover.Unit)))
                    continue;

                double ground = cov.GroundElevation.NumericValue.Value;
                double rise = h.Value;
                double minCover = cov.MinimumCover.NumericValue.Value;
                double maxAllowed = ground - rise - minCover;

                if (maxAllowed < vs.MaxInvert)
                {
                    vs.MaxInvert = maxAllowed;
                    vs.MaxProv = cov.Provenance;
                }

                if (vs.ExactInvert.HasValue && vs.ExactInvert.Value > maxAllowed + 1e-4)
                {
                    conflicts.Add(new VerticalConstraintConflict(
                        "INFEASIBLE_VERTICAL_CONSTRAINTS",
                        new[] { cov.ConduitEnd.ToString() },
                        new[] { "Cover:" + cov.ConduitEnd },
                        new Dictionary<string, double> { { "ExactInvert", vs.ExactInvert.Value }, { "MaxAllowedInvert", maxAllowed } },
                        new[] { vs.ExactProv, cov.Provenance },
                        string.Format(CultureInfo.InvariantCulture, "Cover constraint violated on {0}: Invert {1} exceeds max allowed {2} (Ground {3} - Rise {4} - MinCover {5})",
                            cov.ConduitEnd, vs.ExactInvert.Value, maxAllowed, ground, rise, minCover)));
                }

                if (vs.MinInvert > vs.MaxInvert + 1e-4)
                {
                    conflicts.Add(new VerticalConstraintConflict(
                        "INFEASIBLE_VERTICAL_CONSTRAINTS",
                        new[] { cov.ConduitEnd.ToString() },
                        new[] { "Cover:" + cov.ConduitEnd },
                        new Dictionary<string, double> { { "MinInvert", vs.MinInvert }, { "MaxInvert", vs.MaxInvert } },
                        new[] { vs.MinProv, cov.Provenance },
                        string.Format(CultureInfo.InvariantCulture, "Cover constraint forces upper bound {0} below existing lower bound {1} on {2}",
                            vs.MaxInvert, vs.MinInvert, cov.ConduitEnd)));
                }
            }

            // 8. Deterministic Iterative Propagation Loop
            int maxPasses = (vars.Count + constraintSet.MatchConstraints.Count + constraintSet.SlopeConstraints.Count + 10) * 2;
            int passes = 0;
            bool changed = true;

            while (changed && passes < maxPasses && conflicts.Count == 0)
            {
                changed = false;
                passes++;

                // A. Match propagation
                foreach (var mc in constraintSet.MatchConstraints)
                {
                    if (mc.Mode == VerticalMatchingMode.InvertMatch)
                    {
                        double? exact = null;
                        StandardProvenance exactP = null;
                        foreach (var end in mc.ParticipatingConduitEnds)
                        {
                            if (!vars.TryGetValue(end, out var vs)) continue;
                            if (vs.ExactInvert.HasValue)
                            {
                                if (exact.HasValue && Math.Abs(exact.Value - vs.ExactInvert.Value) > 1e-4)
                                {
                                    conflicts.Add(new VerticalConstraintConflict(
                                        "INFEASIBLE_VERTICAL_CONSTRAINTS",
                                        mc.ParticipatingConduitEnds.Select(e => e.ToString()),
                                        new[] { "InvertMatch:Node" + mc.NodeId },
                                        new Dictionary<string, double> { { "Exact1", exact.Value }, { "Exact2", vs.ExactInvert.Value } },
                                        new[] { exactP, vs.ExactProv },
                                        string.Format(CultureInfo.InvariantCulture, "InvertMatch conflict at Node {0}: {1} vs {2}", mc.NodeId, exact.Value, vs.ExactInvert.Value)));
                                    break;
                                }
                                exact = vs.ExactInvert;
                                exactP = vs.ExactProv;
                            }
                        }
                        if (conflicts.Count > 0) break;

                        if (exact.HasValue)
                        {
                            foreach (var end in mc.ParticipatingConduitEnds)
                            {
                                if (!vars.TryGetValue(end, out var vs)) continue;
                                if (!vs.ExactInvert.HasValue)
                                {
                                    vs.ExactInvert = exact.Value;
                                    vs.ExactProv = exactP;
                                    vs.MinInvert = Math.Max(vs.MinInvert, exact.Value);
                                    vs.MaxInvert = Math.Min(vs.MaxInvert, exact.Value);
                                    changed = true;
                                }
                            }
                        }

                        double commonMin = mc.ParticipatingConduitEnds.Where(vars.ContainsKey).Max(e => vars[e].MinInvert);
                        double commonMax = mc.ParticipatingConduitEnds.Where(vars.ContainsKey).Min(e => vars[e].MaxInvert);
                        if (commonMin > commonMax + 1e-4)
                        {
                            conflicts.Add(new VerticalConstraintConflict(
                                "INFEASIBLE_VERTICAL_CONSTRAINTS",
                                mc.ParticipatingConduitEnds.Select(e => e.ToString()),
                                new[] { "InvertMatch:Node" + mc.NodeId },
                                new Dictionary<string, double> { { "CommonMin", commonMin }, { "CommonMax", commonMax } },
                                new[] { mc.Provenance },
                                string.Format(CultureInfo.InvariantCulture, "InvertMatch bound contradiction at Node {0}: Min {1} > Max {2}", mc.NodeId, commonMin, commonMax)));
                            break;
                        }
                        foreach (var end in mc.ParticipatingConduitEnds)
                        {
                            if (!vars.TryGetValue(end, out var vs)) continue;
                            if (commonMin > vs.MinInvert + 1e-6) { vs.MinInvert = commonMin; changed = true; }
                            if (commonMax < vs.MaxInvert - 1e-6) { vs.MaxInvert = commonMax; changed = true; }
                        }
                    }
                    else if (mc.Mode == VerticalMatchingMode.CrownMatch)
                    {
                        // Crown = Invert + Rise
                        double? crownExact = null;
                        StandardProvenance crownExactP = null;
                        bool canEvaluate = true;

                        foreach (var end in mc.ParticipatingConduitEnds)
                        {
                            var c = network.Conduits.FirstOrDefault(x => x.SourceElementId == end.ConduitId);
                            double? h = GetEffectiveHeight(c);
                            if (!IsFinitePositive(h)) { canEvaluate = false; break; }
                            if (!vars.TryGetValue(end, out var vs)) continue;
                            double rise = h.Value;
                            if (vs.ExactInvert.HasValue)
                            {
                                double cVal = vs.ExactInvert.Value + rise;
                                if (crownExact.HasValue && Math.Abs(crownExact.Value - cVal) > 1e-4)
                                {
                                    conflicts.Add(new VerticalConstraintConflict(
                                        "INFEASIBLE_VERTICAL_CONSTRAINTS",
                                        mc.ParticipatingConduitEnds.Select(e => e.ToString()),
                                        new[] { "CrownMatch:Node" + mc.NodeId },
                                        new Dictionary<string, double> { { "CrownExact1", crownExact.Value }, { "CrownExact2", cVal } },
                                        new[] { crownExactP, vs.ExactProv },
                                        string.Format(CultureInfo.InvariantCulture, "CrownMatch conflict at Node {0}: Crown {1} vs {2}", mc.NodeId, crownExact.Value, cVal)));
                                    break;
                                }
                                crownExact = cVal;
                                crownExactP = vs.ExactProv;
                            }
                        }
                        if (conflicts.Count > 0) break;
                        if (!canEvaluate) continue;

                        if (crownExact.HasValue)
                        {
                            foreach (var end in mc.ParticipatingConduitEnds)
                            {
                                if (!vars.TryGetValue(end, out var vs)) continue;
                                var c = network.Conduits.First(x => x.SourceElementId == end.ConduitId);
                                double rise = GetEffectiveHeight(c).Value;
                                double targetInvert = crownExact.Value - rise;
                                if (!vs.ExactInvert.HasValue)
                                {
                                    vs.ExactInvert = targetInvert;
                                    vs.ExactProv = crownExactP;
                                    vs.MinInvert = Math.Max(vs.MinInvert, targetInvert);
                                    vs.MaxInvert = Math.Min(vs.MaxInvert, targetInvert);
                                    changed = true;
                                }
                            }
                        }

                        double crownMin = mc.ParticipatingConduitEnds.Where(vars.ContainsKey).Max(e => vars[e].MinInvert + GetEffectiveHeight(network.Conduits.First(x => x.SourceElementId == e.ConduitId)).Value);
                        double crownMax = mc.ParticipatingConduitEnds.Where(vars.ContainsKey).Min(e => vars[e].MaxInvert + GetEffectiveHeight(network.Conduits.First(x => x.SourceElementId == e.ConduitId)).Value);
                        if (crownMin > crownMax + 1e-4)
                        {
                            conflicts.Add(new VerticalConstraintConflict(
                                "INFEASIBLE_VERTICAL_CONSTRAINTS",
                                mc.ParticipatingConduitEnds.Select(e => e.ToString()),
                                new[] { "CrownMatch:Node" + mc.NodeId },
                                new Dictionary<string, double> { { "CrownMin", crownMin }, { "CrownMax", crownMax } },
                                new[] { mc.Provenance },
                                string.Format(CultureInfo.InvariantCulture, "CrownMatch bound contradiction at Node {0}: CrownMin {1} > CrownMax {2}", mc.NodeId, crownMin, crownMax)));
                            break;
                        }
                        foreach (var end in mc.ParticipatingConduitEnds)
                        {
                            if (!vars.TryGetValue(end, out var vs)) continue;
                            double rise = GetEffectiveHeight(network.Conduits.First(x => x.SourceElementId == end.ConduitId)).Value;
                            double targetMin = crownMin - rise;
                            double targetMax = crownMax - rise;
                            if (targetMin > vs.MinInvert + 1e-6) { vs.MinInvert = targetMin; changed = true; }
                            if (targetMax < vs.MaxInvert - 1e-6) { vs.MaxInvert = targetMax; changed = true; }
                        }
                    }
                }

                // B. Slope propagation
                foreach (var sc in constraintSet.SlopeConstraints)
                {
                    var conduit = network.Conduits.FirstOrDefault(x => x.SourceElementId == sc.ConduitId);
                    if (conduit == null) continue;
                    if (!constraintSet.TryGetOrientation(sc.ConduitId, out var o)) continue;

                    var fromEnd = o.FromNodeId == conduit.StartNodeId
                        ? new GravityConduitEnd(conduit.SourceElementId, ConduitEndKind.Start, conduit.StartNodeId)
                        : new GravityConduitEnd(conduit.SourceElementId, ConduitEndKind.Stop, conduit.StopNodeId);
                    var toEnd = o.ToNodeId == conduit.StopNodeId
                        ? new GravityConduitEnd(conduit.SourceElementId, ConduitEndKind.Stop, conduit.StopNodeId)
                        : new GravityConduitEnd(conduit.SourceElementId, ConduitEndKind.Start, conduit.StartNodeId);

                    if (!vars.TryGetValue(fromEnd, out var vf) || !vars.TryGetValue(toEnd, out var vt)) continue;
                    double len = conduit.ExistingPhysicalState.Length.Value;

                    // If both exact known: check slope bounds
                    if (vf.ExactInvert.HasValue && vt.ExactInvert.HasValue)
                    {
                        double slope = (vf.ExactInvert.Value - vt.ExactInvert.Value) / len;
                        if (sc.MinimumSlope.HasValue && slope < sc.MinimumSlope.Value - 1e-4)
                        {
                            conflicts.Add(new VerticalConstraintConflict(
                                "INFEASIBLE_VERTICAL_CONSTRAINTS",
                                new[] { fromEnd.ToString(), toEnd.ToString() },
                                new[] { "Slope:Conduit" + sc.ConduitId },
                                new Dictionary<string, double> { { "CalculatedSlope", slope }, { "MinimumSlope", sc.MinimumSlope.Value } },
                                new[] { vf.ExactProv, vt.ExactProv, sc.Provenance },
                                string.Format(CultureInfo.InvariantCulture, "Minimum slope violation on Conduit {0}: Slope {1} < MinimumSlope {2}", sc.ConduitId, slope, sc.MinimumSlope.Value)));
                            break;
                        }
                        if (sc.MaximumSlope.HasValue && slope > sc.MaximumSlope.Value + 1e-4)
                        {
                            conflicts.Add(new VerticalConstraintConflict(
                                "INFEASIBLE_VERTICAL_CONSTRAINTS",
                                new[] { fromEnd.ToString(), toEnd.ToString() },
                                new[] { "Slope:Conduit" + sc.ConduitId },
                                new Dictionary<string, double> { { "CalculatedSlope", slope }, { "MaximumSlope", sc.MaximumSlope.Value } },
                                new[] { vf.ExactProv, vt.ExactProv, sc.Provenance },
                                string.Format(CultureInfo.InvariantCulture, "Maximum slope violation on Conduit {0}: Slope {1} > MaximumSlope {2}", sc.ConduitId, slope, sc.MaximumSlope.Value)));
                            break;
                        }
                    }

                    // Slope bound coupling
                    if (sc.MinimumSlope.HasValue)
                    {
                        double minDrop = sc.MinimumSlope.Value * len;
                        if (vt.MinInvert + minDrop > vf.MinInvert + 1e-6) { vf.MinInvert = vt.MinInvert + minDrop; changed = true; }
                        if (vf.MaxInvert - minDrop < vt.MaxInvert - 1e-6) { vt.MaxInvert = vf.MaxInvert - minDrop; changed = true; }
                    }
                    if (sc.MaximumSlope.HasValue)
                    {
                        double maxDrop = sc.MaximumSlope.Value * len;
                        if (vt.MaxInvert + maxDrop < vf.MaxInvert - 1e-6) { vf.MaxInvert = vt.MaxInvert + maxDrop; changed = true; }
                        if (vf.MinInvert - maxDrop > vt.MinInvert + 1e-6) { vt.MinInvert = vf.MinInvert - maxDrop; changed = true; }
                    }

                    if (vf.MinInvert > vf.MaxInvert + 1e-4)
                    {
                        conflicts.Add(new VerticalConstraintConflict(
                            "INFEASIBLE_VERTICAL_CONSTRAINTS",
                            new[] { fromEnd.ToString() },
                            new[] { "Slope:Conduit" + sc.ConduitId },
                            new Dictionary<string, double> { { "MinInvert", vf.MinInvert }, { "MaxInvert", vf.MaxInvert } },
                            new[] { sc.Provenance },
                            string.Format(CultureInfo.InvariantCulture, "Slope constraint forces contradictory bounds on {0}: Min {1} > Max {2}", fromEnd, vf.MinInvert, vf.MaxInvert)));
                        break;
                    }
                    if (vt.MinInvert > vt.MaxInvert + 1e-4)
                    {
                        conflicts.Add(new VerticalConstraintConflict(
                            "INFEASIBLE_VERTICAL_CONSTRAINTS",
                            new[] { toEnd.ToString() },
                            new[] { "Slope:Conduit" + sc.ConduitId },
                            new Dictionary<string, double> { { "MinInvert", vt.MinInvert }, { "MaxInvert", vt.MaxInvert } },
                            new[] { sc.Provenance },
                            string.Format(CultureInfo.InvariantCulture, "Slope constraint forces contradictory bounds on {0}: Min {1} > Max {2}", toEnd, vt.MinInvert, vt.MaxInvert)));
                        break;
                    }
                }
            }

            if (changed && passes >= maxPasses)
            {
                conflicts.Add(new VerticalConstraintConflict(
                    "P13_B_BLOCKED_FEASIBILITY_CONVERGENCE",
                    vars.Keys.Select(k => k.ToString()),
                    new[] { "ConvergenceLimit" },
                    new Dictionary<string, double> { { "Passes", passes }, { "MaxPasses", maxPasses } },
                    null,
                    "Feasibility propagation failed to converge within deterministic limit"));
            }

            // 9. Format output endpoint ranges
            var canonicalFeetUnit = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            var outputRanges = new Dictionary<string, EndpointElevationRange>(StringComparer.Ordinal);
            foreach (var kvp in vars)
            {
                var vs = kvp.Value;
                double? minRange = vs.MinInvert > -1e8 ? (double?)vs.MinInvert : null;
                double? maxRange = vs.MaxInvert < 1e8 ? (double?)vs.MaxInvert : null;
                outputRanges[kvp.Key.ToString()] = new EndpointElevationRange(
                    kvp.Key,
                    minRange,
                    maxRange,
                    vs.ExactInvert,
                    canonicalFeetUnit);
            }

            // 10. Classify final result status
            VerticalFeasibilityStatus finalStatus;
            if (conflicts.Count > 0)
            {
                finalStatus = VerticalFeasibilityStatus.Infeasible;
            }
            else if (decisionRequests.Count > 0)
            {
                finalStatus = VerticalFeasibilityStatus.EngineeringDecisionRequired;
            }
            else if (incompleteItems.Count > 0)
            {
                finalStatus = VerticalFeasibilityStatus.Incomplete;
            }
            else
            {
                bool allExact = vars.Values.All(v => v.ExactInvert.HasValue);
                finalStatus = allExact ? VerticalFeasibilityStatus.Feasible : VerticalFeasibilityStatus.FeasibleMultipleSolutions;
            }

            return new VerticalFeasibilityResult(
                finalStatus,
                constraintSet.VerticalConstraintFingerprint,
                outputRanges,
                conflicts,
                decisionRequests,
                incompleteItems,
                passes);
        }
    }
}
