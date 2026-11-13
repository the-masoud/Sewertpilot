using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class GravityCandidateGenerator
    {
        public GravityDesignCandidateSet Generate(
            GravityNetworkSnapshot network,
            GravityVerticalDesignConstraintSet constraintSet,
            VerticalFeasibilityResult feasibilityResult,
            GravityCandidateGenerationRequest request)
        {
            if (network == null) throw new ArgumentNullException(nameof(network));
            if (constraintSet == null) throw new ArgumentNullException(nameof(constraintSet));
            if (feasibilityResult == null) throw new ArgumentNullException(nameof(feasibilityResult));
            if (request == null) throw new ArgumentNullException(nameof(request));

            var candidates = new List<GravityDesignCandidate>();
            var recipeResults = new List<GravityCandidateGenerationResult>();
            var seenFingerprints = new HashSet<string>(StringComparer.Ordinal);

            // Precondition gate: candidate generation allowed only when P13 result is Feasible or FeasibleMultipleSolutions
            if (feasibilityResult.Status == VerticalFeasibilityStatus.Infeasible ||
                feasibilityResult.Status == VerticalFeasibilityStatus.Incomplete ||
                feasibilityResult.Status == VerticalFeasibilityStatus.EngineeringDecisionRequired)
            {
                foreach (var recipe in request.Recipes)
                {
                    recipeResults.Add(new GravityCandidateGenerationResult(
                        recipe.RecipeId,
                        CandidateGenerationStatus.Rejected,
                        null,
                        new[] { string.Format(CultureInfo.InvariantCulture, "Upstream P13 feasibility status is {0}; 0 candidates allowed.", feasibilityResult.Status) }));
                }
                return new GravityDesignCandidateSet(constraintSet.VerticalConstraintFingerprint, candidates, recipeResults);
            }

            // Case A: Feasible (every design endpoint resolved to exact value)
            if (feasibilityResult.Status == VerticalFeasibilityStatus.Feasible)
            {
                if (request.Recipes.Count == 0)
                {
                    // Materialize the exact unique profile implied by the constraints
                    var exactRes = MaterializeCandidate(
                        network,
                        constraintSet,
                        feasibilityResult,
                        "EXACT_CONSTRAINT_PROFILE",
                        new List<CandidateEndpointSelection>(),
                        null);

                    recipeResults.Add(exactRes);
                    if (exactRes.Status == CandidateGenerationStatus.Generated && exactRes.Candidate != null)
                    {
                        candidates.Add(exactRes.Candidate);
                        seenFingerprints.Add(exactRes.Candidate.CandidateFingerprint);
                    }
                    return new GravityDesignCandidateSet(constraintSet.VerticalConstraintFingerprint, candidates, recipeResults);
                }
            }

            // Case B: FeasibleMultipleSolutions (or Feasible with explicit recipes provided)
            if (feasibilityResult.Status == VerticalFeasibilityStatus.FeasibleMultipleSolutions && request.Recipes.Count == 0)
            {
                // Multiple solutions require an explicit recipe; no candidates generated without recipe
                return new GravityDesignCandidateSet(constraintSet.VerticalConstraintFingerprint, candidates, recipeResults);
            }

            // Process recipes in request
            foreach (var recipe in request.Recipes)
            {
                var res = MaterializeCandidate(
                    network,
                    constraintSet,
                    feasibilityResult,
                    recipe.RecipeId,
                    recipe.EndpointSelections,
                    recipe.Provenance);

                if (res.Status == CandidateGenerationStatus.Generated && res.Candidate != null)
                {
                    if (seenFingerprints.Contains(res.Candidate.CandidateFingerprint))
                    {
                        // Duplicate candidate detection: same engineering content
                        recipeResults.Add(new GravityCandidateGenerationResult(
                            recipe.RecipeId,
                            CandidateGenerationStatus.Duplicate,
                            res.Candidate,
                            new[] { "Duplicate candidate profile; identical engineering fingerprint already present." }));
                    }
                    else
                    {
                        seenFingerprints.Add(res.Candidate.CandidateFingerprint);
                        candidates.Add(res.Candidate);
                        recipeResults.Add(res);
                    }
                }
                else
                {
                    recipeResults.Add(res);
                }
            }

            return new GravityDesignCandidateSet(constraintSet.VerticalConstraintFingerprint, candidates, recipeResults);
        }

        private static GravityCandidateGenerationResult MaterializeCandidate(
            GravityNetworkSnapshot network,
            GravityVerticalDesignConstraintSet constraintSet,
            VerticalFeasibilityResult feasibilityResult,
            string recipeId,
            IEnumerable<CandidateEndpointSelection> selections,
            StandardProvenance recipeProvenance)
        {
            var issues = new List<string>();
            var proposedValues = new Dictionary<GravityConduitEnd, double>();
            var sources = new Dictionary<GravityConduitEnd, CandidateSelectionSource>();
            var provs = new Dictionary<GravityConduitEnd, StandardProvenance>();

            // 1. Initialize exact values from P13 feasibility result
            foreach (var kvp in feasibilityResult.EndpointRanges)
            {
                if (kvp.Value.ExactInvert.HasValue)
                {
                    proposedValues[kvp.Value.ConduitEnd] = kvp.Value.ExactInvert.Value;
                    sources[kvp.Value.ConduitEnd] = CandidateSelectionSource.ExactConstraint;
                }
            }

            // 2. Process recipe selections
            var canonicalFeet = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            foreach (var sel in selections)
            {
                var end = sel.ConduitEnd;
                var conduit = network.Conduits.FirstOrDefault(c => c.SourceElementId == end.ConduitId);
                if (conduit == null)
                {
                    issues.Add(string.Format(CultureInfo.InvariantCulture, "Conduit {0} not found in network snapshot.", end.ConduitId));
                    return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                }

                if (!feasibilityResult.EndpointRanges.TryGetValue(end.ToString(), out var range))
                {
                    issues.Add(string.Format(CultureInfo.InvariantCulture, "Endpoint range not found for {0}.", end));
                    return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                }

                // Fixed constraints cannot be overridden by recipe
                if (range.ExactInvert.HasValue)
                {
                    double candidateTarget;
                    if (sel.Mode == EndpointSelectionMode.ExplicitValue)
                    {
                        if (sel.ExplicitElevation == null || !sel.ExplicitElevation.HasValue)
                        {
                            issues.Add(string.Format(CultureInfo.InvariantCulture, "Explicit value missing for {0}.", end));
                            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                        }
                        candidateTarget = sel.ExplicitElevation.NumericValue.Value;
                    }
                    else if (sel.Mode == EndpointSelectionMode.ExistingValue)
                    {
                        double? ex = end.End == ConduitEndKind.Start ? conduit.ExistingPhysicalState.StartInvert : conduit.ExistingPhysicalState.StopInvert;
                        if (!ex.HasValue)
                        {
                            issues.Add(string.Format(CultureInfo.InvariantCulture, "Existing value missing for {0}.", end));
                            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                        }
                        candidateTarget = ex.Value;
                    }
                    else if (sel.Mode == EndpointSelectionMode.MinimumFeasible)
                    {
                        candidateTarget = range.MinimumAllowedInvert.GetValueOrDefault(range.ExactInvert.Value);
                    }
                    else
                    {
                        candidateTarget = range.MaximumAllowedInvert.GetValueOrDefault(range.ExactInvert.Value);
                    }

                    if (Math.Abs(candidateTarget - range.ExactInvert.Value) > 1e-4)
                    {
                        issues.Add(string.Format(CultureInfo.InvariantCulture, "Fixed constraint on {0} cannot be overridden by recipe.", end));
                        return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                    }
                    continue;
                }

                switch (sel.Mode)
                {
                    case EndpointSelectionMode.ExistingValue:
                        double? existingVal = end.End == ConduitEndKind.Start
                            ? conduit.ExistingPhysicalState.StartInvert
                            : conduit.ExistingPhysicalState.StopInvert;
                        if (!existingVal.HasValue)
                        {
                            issues.Add(string.Format(CultureInfo.InvariantCulture, "Existing invert elevation is missing on {0}.", end));
                            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                        }
                        if ((range.MinimumAllowedInvert.HasValue && existingVal.Value < range.MinimumAllowedInvert.Value - 1e-4) ||
                            (range.MaximumAllowedInvert.HasValue && existingVal.Value > range.MaximumAllowedInvert.Value + 1e-4))
                        {
                            issues.Add(string.Format(CultureInfo.InvariantCulture,
                                "Existing invert {0} for {1} lies outside feasible range [{2}, {3}]. Existing value is not clamped.",
                                existingVal.Value, end, range.MinimumAllowedInvert, range.MaximumAllowedInvert));
                            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                        }
                        proposedValues[end] = existingVal.Value;
                        sources[end] = CandidateSelectionSource.ExistingValue;
                        provs[end] = sel.Provenance;
                        break;

                    case EndpointSelectionMode.ExplicitValue:
                        if (sel.ExplicitElevation == null || !sel.ExplicitElevation.HasValue)
                        {
                            issues.Add(string.Format(CultureInfo.InvariantCulture, "Explicit elevation value missing for {0}.", end));
                            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                        }
                        if (sel.ExplicitElevation.Unit != null && !canonicalFeet.Equals(sel.ExplicitElevation.Unit))
                        {
                            issues.Add(string.Format(CultureInfo.InvariantCulture,
                                "INCOMPATIBLE_VERTICAL_UNITS: Explicit value on {0} uses incompatible unit {1}.", end, sel.ExplicitElevation.Unit));
                            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                        }
                        double explicitVal = sel.ExplicitElevation.NumericValue.Value;
                        if ((range.MinimumAllowedInvert.HasValue && explicitVal < range.MinimumAllowedInvert.Value - 1e-4) ||
                            (range.MaximumAllowedInvert.HasValue && explicitVal > range.MaximumAllowedInvert.Value + 1e-4))
                        {
                            issues.Add(string.Format(CultureInfo.InvariantCulture,
                                "Explicit elevation {0} for {1} lies outside feasible range [{2}, {3}].",
                                explicitVal, end, range.MinimumAllowedInvert, range.MaximumAllowedInvert));
                            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                        }
                        proposedValues[end] = explicitVal;
                        sources[end] = CandidateSelectionSource.ExplicitValue;
                        provs[end] = sel.Provenance;
                        break;

                    case EndpointSelectionMode.MinimumFeasible:
                        if (!range.MinimumAllowedInvert.HasValue || range.MinimumAllowedInvert.Value <= -1e8)
                        {
                            issues.Add(string.Format(CultureInfo.InvariantCulture,
                                "CANDIDATE_SELECTION_UNBOUNDED: Lower bound for {0} is unbounded.", end));
                            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Unbounded, null, issues);
                        }
                        proposedValues[end] = range.MinimumAllowedInvert.Value;
                        sources[end] = CandidateSelectionSource.MinimumFeasible;
                        provs[end] = sel.Provenance;
                        break;

                    case EndpointSelectionMode.MaximumFeasible:
                        if (!range.MaximumAllowedInvert.HasValue || range.MaximumAllowedInvert.Value >= 1e8)
                        {
                            issues.Add(string.Format(CultureInfo.InvariantCulture,
                                "CANDIDATE_SELECTION_UNBOUNDED: Upper bound for {0} is unbounded.", end));
                            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Unbounded, null, issues);
                        }
                        proposedValues[end] = range.MaximumAllowedInvert.Value;
                        sources[end] = CandidateSelectionSource.MaximumFeasible;
                        provs[end] = sel.Provenance;
                        break;
                }
            }

            // 3. Propagate equality constraints (InvertMatch and CrownMatch)
            bool changed = true;
            int passes = 0;
            while (changed && passes < 50)
            {
                changed = false;
                passes++;

                foreach (var mc in constraintSet.MatchConstraints)
                {
                    if (mc.Mode == VerticalMatchingMode.InvertMatch)
                    {
                        double? known = null;
                        foreach (var e in mc.ParticipatingConduitEnds)
                        {
                            if (proposedValues.TryGetValue(e, out double v))
                            {
                                known = v;
                                break;
                            }
                        }
                        if (known.HasValue)
                        {
                            foreach (var e in mc.ParticipatingConduitEnds)
                            {
                                if (!proposedValues.ContainsKey(e))
                                {
                                    proposedValues[e] = known.Value;
                                    sources[e] = CandidateSelectionSource.PropagatedEquality;
                                    provs[e] = mc.Provenance;
                                    changed = true;
                                }
                            }
                        }
                    }
                    else if (mc.Mode == VerticalMatchingMode.CrownMatch)
                    {
                        double? knownCrown = null;
                        foreach (var e in mc.ParticipatingConduitEnds)
                        {
                            if (proposedValues.TryGetValue(e, out double v))
                            {
                                var c = network.Conduits.FirstOrDefault(x => x.SourceElementId == e.ConduitId);
                                double? h = c?.ExistingPhysicalState?.EffectiveSectionHeight ?? c?.ExistingPhysicalState?.Rise;
                                if (h.HasValue)
                                {
                                    knownCrown = v + h.Value;
                                    break;
                                }
                            }
                        }
                        if (knownCrown.HasValue)
                        {
                            foreach (var e in mc.ParticipatingConduitEnds)
                            {
                                if (!proposedValues.ContainsKey(e))
                                {
                                    var c = network.Conduits.FirstOrDefault(x => x.SourceElementId == e.ConduitId);
                                    double? h = c?.ExistingPhysicalState?.EffectiveSectionHeight ?? c?.ExistingPhysicalState?.Rise;
                                    if (h.HasValue)
                                    {
                                        proposedValues[e] = knownCrown.Value - h.Value;
                                        sources[e] = CandidateSelectionSource.PropagatedEquality;
                                        provs[e] = mc.Provenance;
                                        changed = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 4. Verify all endpoints are resolved; no implicit fallback (no midpoint/zero/default)
            foreach (var c in network.Conduits)
            {
                var startEnd = new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Start, c.StartNodeId);
                var stopEnd = new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Stop, c.StopNodeId);

                if (!proposedValues.ContainsKey(startEnd))
                {
                    issues.Add(string.Format(CultureInfo.InvariantCulture,
                        "CANDIDATE_INPUT_INCOMPLETE: Endpoint {0} remains unresolved with no explicit selection or equality propagation.", startEnd));
                    return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Incomplete, null, issues);
                }
                if (!proposedValues.ContainsKey(stopEnd))
                {
                    issues.Add(string.Format(CultureInfo.InvariantCulture,
                        "CANDIDATE_INPUT_INCOMPLETE: Endpoint {0} remains unresolved with no explicit selection or equality propagation.", stopEnd));
                    return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Incomplete, null, issues);
                }
            }

            // 5. Candidate Validation against P13 constraints
            // Fixed constraints
            foreach (var fc in constraintSet.FixedConstraints)
            {
                if (proposedValues.TryGetValue(fc.ConduitEnd, out double prop))
                {
                    if (fc.InvertElevation.HasValue && Math.Abs(prop - fc.InvertElevation.NumericValue.Value) > 1e-4)
                    {
                        issues.Add(string.Format(CultureInfo.InvariantCulture,
                            "Fixed elevation violated on {0}: proposed {1} != fixed {2}", fc.ConduitEnd, prop, fc.InvertElevation.NumericValue.Value));
                        return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                    }
                }
            }

            // Outfall constraints
            foreach (var oc in constraintSet.OutfallConstraints)
            {
                if (oc.ConnectedEndpointConstraint != null && oc.ProtectedInvert != null && oc.ProtectedInvert.HasValue)
                {
                    if (proposedValues.TryGetValue(oc.ConnectedEndpointConstraint.ConduitEnd, out double p))
                    {
                        if (Math.Abs(p - oc.ProtectedInvert.NumericValue.Value) > 1e-4)
                        {
                            issues.Add(string.Format(CultureInfo.InvariantCulture,
                                "Outfall protected invert violated on Outfall {0}: proposed {1} != {2}", oc.OutfallNodeId, p, oc.ProtectedInvert.NumericValue.Value));
                            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                        }
                    }
                }
            }

            // Match constraints
            foreach (var mc in constraintSet.MatchConstraints)
            {
                if (mc.Mode == VerticalMatchingMode.InvertMatch)
                {
                    double? first = null;
                    foreach (var e in mc.ParticipatingConduitEnds)
                    {
                        double v = proposedValues[e];
                        if (!first.HasValue) first = v;
                        else if (Math.Abs(first.Value - v) > 1e-4)
                        {
                            issues.Add(string.Format(CultureInfo.InvariantCulture,
                                "InvertMatch equality violated at Node {0}: {1} vs {2}", mc.NodeId, first.Value, v));
                            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                        }
                    }
                }
                else if (mc.Mode == VerticalMatchingMode.CrownMatch)
                {
                    double? firstCrown = null;
                    foreach (var e in mc.ParticipatingConduitEnds)
                    {
                        var c = network.Conduits.First(x => x.SourceElementId == e.ConduitId);
                        double? h = c.ExistingPhysicalState.EffectiveSectionHeight ?? c.ExistingPhysicalState.Rise;
                        if (!h.HasValue)
                        {
                            issues.Add(string.Format(CultureInfo.InvariantCulture,
                                "Conduit {0} missing effective section height or rise for CrownMatch.", c.SourceElementId));
                            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                        }
                        double crown = proposedValues[e] + h.Value;
                        if (!firstCrown.HasValue) firstCrown = crown;
                        else if (Math.Abs(firstCrown.Value - crown) > 1e-4)
                        {
                            issues.Add(string.Format(CultureInfo.InvariantCulture,
                                "CrownMatch equality violated at Node {0}: crown {1} vs {2}", mc.NodeId, firstCrown.Value, crown));
                            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                        }
                    }
                }
            }

            // Cover constraints: Cover = Ground - (Invert + Rise) >= MinimumCover
            foreach (var cov in constraintSet.CoverConstraints)
            {
                var c = network.Conduits.First(x => x.SourceElementId == cov.ConduitEnd.ConduitId);
                double ground = cov.GroundElevation.NumericValue.Value;
                double? h = c.ExistingPhysicalState.EffectiveSectionHeight ?? c.ExistingPhysicalState.Rise;
                if (!h.HasValue)
                {
                    issues.Add(string.Format(CultureInfo.InvariantCulture,
                        "Conduit {0} missing effective section height or rise for cover check.", c.SourceElementId));
                    return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                }
                double rise = h.Value;
                double minCover = cov.MinimumCover.NumericValue.Value;
                double invert = proposedValues[cov.ConduitEnd];
                double crown = invert + rise;
                double cover = ground - crown;
                if (cover < minCover - 1e-4)
                {
                    issues.Add(string.Format(CultureInfo.InvariantCulture,
                        "Cover constraint violated on {0}: cover {1:F3} < minimum cover {2:F3} (ground {3}, crown {4})",
                        cov.ConduitEnd, cover, minCover, ground, crown));
                    return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                }
            }

            // Slope constraints: MinimumSlope <= (FromInvert - ToInvert) / Length <= MaximumSlope
            foreach (var sc in constraintSet.SlopeConstraints)
            {
                var c = network.Conduits.First(x => x.SourceElementId == sc.ConduitId);
                if (!constraintSet.TryGetOrientation(sc.ConduitId, out var o))
                {
                    issues.Add(string.Format(CultureInfo.InvariantCulture, "Orientation missing for Conduit {0}.", sc.ConduitId));
                    return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                }
                var fromEnd = o.FromNodeId == c.StartNodeId
                    ? new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Start, c.StartNodeId)
                    : new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Stop, c.StopNodeId);
                var toEnd = o.ToNodeId == c.StopNodeId
                    ? new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Stop, c.StopNodeId)
                    : new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Start, c.StartNodeId);

                double fromInvert = proposedValues[fromEnd];
                double toInvert = proposedValues[toEnd];
                double len = c.ExistingPhysicalState.Length.Value;
                double slope = (fromInvert - toInvert) / len;

                if (sc.MinimumSlope.HasValue && slope < sc.MinimumSlope.Value - 1e-4)
                {
                    issues.Add(string.Format(CultureInfo.InvariantCulture,
                        "Minimum slope violated on Conduit {0}: calculated slope {1:F4} < min slope {2:F4}", sc.ConduitId, slope, sc.MinimumSlope.Value));
                    return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                }
                if (sc.MaximumSlope.HasValue && slope > sc.MaximumSlope.Value + 1e-4)
                {
                    issues.Add(string.Format(CultureInfo.InvariantCulture,
                        "Maximum slope violated on Conduit {0}: calculated slope {1:F4} > max slope {2:F4}", sc.ConduitId, slope, sc.MaximumSlope.Value));
                    return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Rejected, null, issues);
                }
            }

            // 6. Materialize endpoint designs and derived conduit states
            var endpointDesigns = new List<CandidateEndpointDesign>();
            var derivedConduits = new List<CandidateConduitVerticalState>();
            var traces = new List<CandidateEndpointTrace>();

            foreach (var c in network.Conduits.OrderBy(x => x.SourceElementId))
            {
                var startEnd = new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Start, c.StartNodeId);
                var stopEnd = new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Stop, c.StopNodeId);

                double startProp = proposedValues[startEnd];
                double stopProp = proposedValues[stopEnd];
                var startSrc = sources[startEnd];
                var stopSrc = sources[stopEnd];

                endpointDesigns.Add(new CandidateEndpointDesign(
                    c.SourceElementId,
                    ConduitEndKind.Start,
                    c.StartNodeId,
                    c.ExistingPhysicalState.StartInvert,
                    startProp,
                    canonicalFeet,
                    startSrc));

                endpointDesigns.Add(new CandidateEndpointDesign(
                    c.SourceElementId,
                    ConduitEndKind.Stop,
                    c.StopNodeId,
                    c.ExistingPhysicalState.StopInvert,
                    stopProp,
                    canonicalFeet,
                    stopSrc));

                provs.TryGetValue(startEnd, out var pStart);
                provs.TryGetValue(stopEnd, out var pStop);

                traces.Add(new CandidateEndpointTrace(
                    startEnd,
                    startSrc,
                    recipeId,
                    new[] { "Conduit:" + c.SourceElementId + ":Start" },
                    pStart ?? recipeProvenance,
                    c.ExistingPhysicalState.StartInvert,
                    startProp));

                traces.Add(new CandidateEndpointTrace(
                    stopEnd,
                    stopSrc,
                    recipeId,
                    new[] { "Conduit:" + c.SourceElementId + ":Stop" },
                    pStop ?? recipeProvenance,
                    c.ExistingPhysicalState.StopInvert,
                    stopProp));

                // Derived conduit vertical state
                constraintSet.TryGetOrientation(c.SourceElementId, out var o);
                int fromNode = o != null ? o.FromNodeId : c.StartNodeId;
                int toNode = o != null ? o.ToNodeId : c.StopNodeId;

                double fromInvert = fromNode == c.StartNodeId ? startProp : stopProp;
                double toInvert = toNode == c.StopNodeId ? stopProp : startProp;
                double len = c.ExistingPhysicalState.Length.Value;
                double slope = (fromInvert - toInvert) / len;

                double? rise = c.ExistingPhysicalState.EffectiveSectionHeight ?? c.ExistingPhysicalState.Rise;
                double? startCrown = rise.HasValue ? (double?)(startProp + rise.Value) : null;
                double? stopCrown = rise.HasValue ? (double?)(stopProp + rise.Value) : null;

                var startNodeObj = network.Nodes.FirstOrDefault(n => n.SourceElementId == c.StartNodeId);
                var stopNodeObj = network.Nodes.FirstOrDefault(n => n.SourceElementId == c.StopNodeId);

                double? startGround = startNodeObj?.ExistingElevationState?.GroundElevation;
                double? stopGround = stopNodeObj?.ExistingElevationState?.GroundElevation;

                double? startCover = (startGround.HasValue && startCrown.HasValue) ? (double?)(startGround.Value - startCrown.Value) : null;
                double? stopCover = (stopGround.HasValue && stopCrown.HasValue) ? (double?)(stopGround.Value - stopCrown.Value) : null;

                derivedConduits.Add(new CandidateConduitVerticalState(
                    c.SourceElementId,
                    fromNode,
                    toNode,
                    startProp,
                    stopProp,
                    fromInvert,
                    toInvert,
                    len,
                    slope,
                    startCrown,
                    stopCrown,
                    startCover,
                    stopCover,
                    canonicalFeet));
            }

            string candHashSuffix = constraintSet.VerticalConstraintFingerprint != null && constraintSet.VerticalConstraintFingerprint.Length >= 8
                ? constraintSet.VerticalConstraintFingerprint.Substring(0, 8)
                : "PROV";
            string candidateId = string.Format(CultureInfo.InvariantCulture, "CAN_{0}_{1}", recipeId, candHashSuffix);

            var candidate = new GravityDesignCandidate(
                candidateId,
                recipeId,
                network.NetworkFingerprint,
                constraintSet.VerticalConstraintFingerprint,
                endpointDesigns,
                derivedConduits,
                new CandidateGenerationTrace(traces));

            return new GravityCandidateGenerationResult(recipeId, CandidateGenerationStatus.Generated, candidate, issues);
        }
    }
}
