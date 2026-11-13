using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum EndpointSelectionMode
    {
        ExistingValue = 1,
        ExplicitValue = 2,
        MinimumFeasible = 3,
        MaximumFeasible = 4
    }

    public sealed class CandidateEndpointSelection
    {
        public GravityConduitEnd ConduitEnd { get; }
        public EndpointSelectionMode Mode { get; }
        public SourceEngineeringValue ExplicitElevation { get; }
        public StandardProvenance Provenance { get; }

        public CandidateEndpointSelection(
            GravityConduitEnd conduitEnd,
            EndpointSelectionMode mode,
            StandardProvenance provenance,
            SourceEngineeringValue explicitElevation = null)
        {
            ConduitEnd = conduitEnd ?? throw new ArgumentNullException(nameof(conduitEnd));
            if (!Enum.IsDefined(typeof(EndpointSelectionMode), mode))
                throw new ArgumentOutOfRangeException(nameof(mode));
            Mode = mode;
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            if (mode == EndpointSelectionMode.ExplicitValue)
            {
                if (explicitElevation == null || !explicitElevation.HasValue)
                    throw new ArgumentException("ExplicitValue mode requires a non-null explicit elevation.", nameof(explicitElevation));
            }
            ExplicitElevation = explicitElevation;
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0} -> {1}", ConduitEnd, Mode);
    }

    public sealed class GravityCandidateRecipe
    {
        public string RecipeId { get; }
        public string Name { get; }
        public ReadOnlyCollection<CandidateEndpointSelection> EndpointSelections { get; }
        public StandardProvenance Provenance { get; }

        public GravityCandidateRecipe(
            string recipeId,
            IEnumerable<CandidateEndpointSelection> endpointSelections,
            StandardProvenance provenance,
            string name = null)
        {
            if (string.IsNullOrWhiteSpace(recipeId))
                throw new ArgumentException("RecipeId is required.", nameof(recipeId));
            RecipeId = recipeId.Trim();
            Name = name?.Trim() ?? string.Empty;
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            EndpointSelections = new ReadOnlyCollection<CandidateEndpointSelection>(
                endpointSelections != null ? endpointSelections.ToList() : new List<CandidateEndpointSelection>());
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Recipe {0} ({1} selections)", RecipeId, EndpointSelections.Count);
    }

    public sealed class GravityCandidateGenerationRequest
    {
        public string SourceConstraintFingerprint { get; }
        public ReadOnlyCollection<GravityCandidateRecipe> Recipes { get; }

        public GravityCandidateGenerationRequest(
            string sourceConstraintFingerprint,
            IEnumerable<GravityCandidateRecipe> recipes)
        {
            SourceConstraintFingerprint = sourceConstraintFingerprint ?? string.Empty;
            Recipes = new ReadOnlyCollection<GravityCandidateRecipe>(
                recipes != null ? recipes.ToList() : new List<GravityCandidateRecipe>());
        }
    }

    public enum CandidateSelectionSource
    {
        ExactConstraint = 1,
        ExistingValue = 2,
        ExplicitValue = 3,
        MinimumFeasible = 4,
        MaximumFeasible = 5,
        PropagatedEquality = 6
    }

    public sealed class CandidateEndpointTrace
    {
        public GravityConduitEnd ConduitEnd { get; }
        public CandidateSelectionSource SelectionSource { get; }
        public string RecipeId { get; }
        public ReadOnlyCollection<string> ConstraintIds { get; }
        public StandardProvenance Provenance { get; }
        public double? OriginalValue { get; }
        public double ProposedValue { get; }

        public CandidateEndpointTrace(
            GravityConduitEnd conduitEnd,
            CandidateSelectionSource selectionSource,
            string recipeId,
            IEnumerable<string> constraintIds,
            StandardProvenance provenance,
            double? originalValue,
            double proposedValue)
        {
            ConduitEnd = conduitEnd ?? throw new ArgumentNullException(nameof(conduitEnd));
            SelectionSource = selectionSource;
            RecipeId = recipeId ?? string.Empty;
            ConstraintIds = new ReadOnlyCollection<string>(constraintIds != null ? constraintIds.ToList() : new List<string>());
            Provenance = provenance;
            OriginalValue = originalValue;
            ProposedValue = proposedValue;
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0} [{1}]: {2}", ConduitEnd, SelectionSource, ProposedValue);
    }

    public sealed class CandidateGenerationTrace
    {
        public ReadOnlyCollection<CandidateEndpointTrace> EndpointTraces { get; }

        public CandidateGenerationTrace(IEnumerable<CandidateEndpointTrace> traces)
        {
            EndpointTraces = new ReadOnlyCollection<CandidateEndpointTrace>(
                traces != null ? traces.ToList() : new List<CandidateEndpointTrace>());
        }

        public CandidateEndpointTrace GetTrace(GravityConduitEnd end)
        {
            if (end == null) return null;
            return EndpointTraces.FirstOrDefault(t => t.ConduitEnd.Equals(end));
        }
    }

    public sealed class CandidateEndpointDesign
    {
        public int ConduitId { get; }
        public ConduitEndKind End { get; }
        public int NodeId { get; }
        public double? ExistingInvert { get; }
        public double ProposedInvert { get; }
        public SourceUnitIdentity Unit { get; }
        public CandidateSelectionSource SelectionSource { get; }

        public GravityConduitEnd ConduitEnd => new GravityConduitEnd(ConduitId, End, NodeId);

        public CandidateEndpointDesign(
            int conduitId,
            ConduitEndKind end,
            int nodeId,
            double? existingInvert,
            double proposedInvert,
            SourceUnitIdentity unit,
            CandidateSelectionSource selectionSource)
        {
            if (conduitId <= 0) throw new ArgumentOutOfRangeException(nameof(conduitId));
            if (!Enum.IsDefined(typeof(ConduitEndKind), end)) throw new ArgumentOutOfRangeException(nameof(end));
            if (nodeId <= 0) throw new ArgumentOutOfRangeException(nameof(nodeId));
            if (double.IsNaN(proposedInvert) || double.IsInfinity(proposedInvert))
                throw new ArgumentException("ProposedInvert must be finite.", nameof(proposedInvert));

            ConduitId = conduitId;
            End = end;
            NodeId = nodeId;
            ExistingInvert = existingInvert;
            ProposedInvert = proposedInvert;
            Unit = unit ?? throw new ArgumentNullException(nameof(unit));
            SelectionSource = selectionSource;
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "C{0}:{1} @ N{2} = {3} {4}", ConduitId, End, NodeId, ProposedInvert, Unit?.Symbol);
    }

    public sealed class CandidateConduitVerticalState
    {
        public int ConduitId { get; }
        public int FromNodeId { get; }
        public int ToNodeId { get; }
        public double StartInvert { get; }
        public double StopInvert { get; }
        public double FromInvert { get; }
        public double ToInvert { get; }
        public double Length { get; }
        public double Slope { get; }
        public double? StartCrown { get; }
        public double? StopCrown { get; }
        public double? StartCover { get; }
        public double? StopCover { get; }
        public SourceUnitIdentity Unit { get; }

        public CandidateConduitVerticalState(
            int conduitId,
            int fromNodeId,
            int toNodeId,
            double startInvert,
            double stopInvert,
            double fromInvert,
            double toInvert,
            double length,
            double slope,
            double? startCrown,
            double? stopCrown,
            double? startCover,
            double? stopCover,
            SourceUnitIdentity unit)
        {
            ConduitId = conduitId;
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
            StartInvert = startInvert;
            StopInvert = stopInvert;
            FromInvert = fromInvert;
            ToInvert = toInvert;
            Length = length;
            Slope = slope;
            StartCrown = startCrown;
            StopCrown = stopCrown;
            StartCover = startCover;
            StopCover = stopCover;
            Unit = unit;
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Conduit {0}: Slope={1:F4}, From={2:F2}, To={3:F2}", ConduitId, Slope, FromInvert, ToInvert);
    }

    public sealed class GravityDesignCandidate
    {
        public string CandidateId { get; }
        public string RecipeId { get; }
        public string SourceNetworkFingerprint { get; }
        public string VerticalConstraintFingerprint { get; }
        public ReadOnlyCollection<CandidateEndpointDesign> EndpointDesigns { get; }
        public ReadOnlyCollection<CandidateConduitVerticalState> DerivedConduits { get; }
        public string CandidateFingerprint { get; }
        public CandidateGenerationTrace GenerationTrace { get; }

        public GravityDesignCandidate(
            string candidateId,
            string recipeId,
            string sourceNetworkFingerprint,
            string verticalConstraintFingerprint,
            IEnumerable<CandidateEndpointDesign> endpointDesigns,
            IEnumerable<CandidateConduitVerticalState> derivedConduits,
            CandidateGenerationTrace generationTrace)
        {
            CandidateId = candidateId ?? string.Empty;
            RecipeId = recipeId ?? string.Empty;
            SourceNetworkFingerprint = sourceNetworkFingerprint ?? string.Empty;
            VerticalConstraintFingerprint = verticalConstraintFingerprint ?? string.Empty;

            var endpoints = endpointDesigns != null
                ? endpointDesigns.OrderBy(e => e.ConduitId).ThenBy(e => (int)e.End).ToList()
                : new List<CandidateEndpointDesign>();
            EndpointDesigns = new ReadOnlyCollection<CandidateEndpointDesign>(endpoints);

            var derived = derivedConduits != null
                ? derivedConduits.OrderBy(d => d.ConduitId).ToList()
                : new List<CandidateConduitVerticalState>();
            DerivedConduits = new ReadOnlyCollection<CandidateConduitVerticalState>(derived);

            GenerationTrace = generationTrace ?? new CandidateGenerationTrace(null);
            CandidateFingerprint = CalculateFingerprint(SourceNetworkFingerprint, VerticalConstraintFingerprint, EndpointDesigns, DerivedConduits);
        }

        public static string CalculateFingerprint(
            string networkFingerprint,
            string constraintFingerprint,
            IEnumerable<CandidateEndpointDesign> endpoints,
            IEnumerable<CandidateConduitVerticalState> derived)
        {
            var sb = new StringBuilder();
            sb.Append("NET_FP:").Append(networkFingerprint ?? string.Empty).Append("\n");
            sb.Append("VERT_FP:").Append(constraintFingerprint ?? string.Empty).Append("\n");

            sb.Append("ENDPOINTS:\n");
            if (endpoints != null)
            {
                foreach (var ep in endpoints.OrderBy(e => e.ConduitId).ThenBy(e => (int)e.End))
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  C{0}:{1}:N{2}|INV={3}|UNIT={4}\n",
                        ep.ConduitId,
                        ep.End,
                        ep.NodeId,
                        ep.ProposedInvert.ToString("R", CultureInfo.InvariantCulture),
                        ep.Unit?.UnitName ?? "");
                }
            }

            sb.Append("CONDUITS:\n");
            if (derived != null)
            {
                foreach (var c in derived.OrderBy(x => x.ConduitId))
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  C{0}|FROM={1}:TO={2}|LEN={3}|SLOPE={4}|SC={5}|EC={6}|SCOV={7}|ECOV={8}\n",
                        c.ConduitId,
                        c.FromNodeId,
                        c.ToNodeId,
                        c.Length.ToString("R", CultureInfo.InvariantCulture),
                        c.Slope.ToString("R", CultureInfo.InvariantCulture),
                        c.StartCrown?.ToString("R", CultureInfo.InvariantCulture) ?? "N/A",
                        c.StopCrown?.ToString("R", CultureInfo.InvariantCulture) ?? "N/A",
                        c.StartCover?.ToString("R", CultureInfo.InvariantCulture) ?? "N/A",
                        c.StopCover?.ToString("R", CultureInfo.InvariantCulture) ?? "N/A");
                }
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    public enum CandidateGenerationStatus
    {
        Generated = 1,
        Rejected = 2,
        Incomplete = 3,
        Unbounded = 4,
        Duplicate = 5
    }

    public sealed class GravityCandidateGenerationResult
    {
        public string RecipeId { get; }
        public CandidateGenerationStatus Status { get; }
        public GravityDesignCandidate Candidate { get; }
        public ReadOnlyCollection<string> Issues { get; }

        public GravityCandidateGenerationResult(
            string recipeId,
            CandidateGenerationStatus status,
            GravityDesignCandidate candidate = null,
            IEnumerable<string> issues = null)
        {
            RecipeId = recipeId ?? string.Empty;
            Status = status;
            Candidate = candidate;
            Issues = new ReadOnlyCollection<string>(issues != null ? issues.ToList() : new List<string>());
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Recipe {0}: {1}", RecipeId, Status);
    }

    public sealed class GravityDesignCandidateSet
    {
        public string SourceConstraintFingerprint { get; }
        public ReadOnlyCollection<GravityDesignCandidate> Candidates { get; }
        public ReadOnlyCollection<GravityCandidateGenerationResult> RecipeResults { get; }

        public GravityDesignCandidateSet(
            string sourceConstraintFingerprint,
            IEnumerable<GravityDesignCandidate> candidates,
            IEnumerable<GravityCandidateGenerationResult> recipeResults)
        {
            SourceConstraintFingerprint = sourceConstraintFingerprint ?? string.Empty;

            var candList = candidates != null
                ? candidates.OrderBy(c => c.CandidateFingerprint, StringComparer.Ordinal).ToList()
                : new List<GravityDesignCandidate>();
            Candidates = new ReadOnlyCollection<GravityDesignCandidate>(candList);

            RecipeResults = new ReadOnlyCollection<GravityCandidateGenerationResult>(
                recipeResults != null ? recipeResults.ToList() : new List<GravityCandidateGenerationResult>());
        }
    }
}
