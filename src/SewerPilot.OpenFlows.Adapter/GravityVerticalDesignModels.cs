using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum ConduitEndKind
    {
        Start = 1,
        Stop = 2
    }

    public sealed class GravityConduitEnd : IEquatable<GravityConduitEnd>
    {
        public int ConduitId { get; }
        public ConduitEndKind End { get; }
        public int NodeId { get; }

        public GravityConduitEnd(int conduitId, ConduitEndKind end, int nodeId)
        {
            if (conduitId <= 0) throw new ArgumentOutOfRangeException(nameof(conduitId), "ConduitId must be positive.");
            if (!Enum.IsDefined(typeof(ConduitEndKind), end)) throw new ArgumentOutOfRangeException(nameof(end), "Invalid ConduitEndKind.");
            if (nodeId <= 0) throw new ArgumentOutOfRangeException(nameof(nodeId), "NodeId must be positive.");
            ConduitId = conduitId;
            End = end;
            NodeId = nodeId;
        }

        public bool Equals(GravityConduitEnd other)
        {
            if (other == null) return false;
            return ConduitId == other.ConduitId && End == other.End && NodeId == other.NodeId;
        }

        public override bool Equals(object obj) => Equals(obj as GravityConduitEnd);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ConduitId;
                hash = (hash * 397) ^ (int)End;
                hash = (hash * 397) ^ NodeId;
                return hash;
            }
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Conduit {0} ({1} @ Node {2})", ConduitId, End, NodeId);
    }

    public sealed class GravityDesignOrientation
    {
        public int ConduitId { get; }
        public int FromNodeId { get; }
        public int ToNodeId { get; }
        public StandardProvenance Provenance { get; }

        public GravityDesignOrientation(int conduitId, int fromNodeId, int toNodeId, StandardProvenance provenance)
        {
            if (conduitId <= 0) throw new ArgumentOutOfRangeException(nameof(conduitId), "ConduitId must be positive.");
            if (fromNodeId <= 0) throw new ArgumentOutOfRangeException(nameof(fromNodeId), "FromNodeId must be positive.");
            if (toNodeId <= 0) throw new ArgumentOutOfRangeException(nameof(toNodeId), "ToNodeId must be positive.");
            if (fromNodeId == toNodeId) throw new ArgumentException("FromNodeId and ToNodeId cannot be identical.", nameof(toNodeId));
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance), "Provenance is required.");
            ConduitId = conduitId;
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
        }

        public bool IsStartToStop(int startNodeId, int stopNodeId) => FromNodeId == startNodeId && ToNodeId == stopNodeId;
        public bool IsStopToStart(int startNodeId, int stopNodeId) => FromNodeId == stopNodeId && ToNodeId == startNodeId;
    }

    public sealed class FixedEndpointElevationConstraint
    {
        public GravityConduitEnd ConduitEnd { get; }
        public SourceEngineeringValue InvertElevation { get; }
        public StandardProvenance Provenance { get; }
        public string Reason { get; }

        public FixedEndpointElevationConstraint(
            GravityConduitEnd conduitEnd,
            SourceEngineeringValue invertElevation,
            StandardProvenance provenance,
            string reason = null)
        {
            ConduitEnd = conduitEnd ?? throw new ArgumentNullException(nameof(conduitEnd));
            InvertElevation = invertElevation ?? throw new ArgumentNullException(nameof(invertElevation));
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            Reason = reason;
        }
    }

    public sealed class ProtectedNodeElevationConstraint
    {
        public int NodeId { get; }
        public string ElevationKind { get; }
        public SourceEngineeringValue Elevation { get; }
        public StandardProvenance Provenance { get; }

        public ProtectedNodeElevationConstraint(
            int nodeId,
            string elevationKind,
            SourceEngineeringValue elevation,
            StandardProvenance provenance)
        {
            if (nodeId <= 0) throw new ArgumentOutOfRangeException(nameof(nodeId), "NodeId must be positive.");
            if (string.IsNullOrWhiteSpace(elevationKind)) throw new ArgumentException("ElevationKind is required.", nameof(elevationKind));
            NodeId = nodeId;
            ElevationKind = elevationKind.Trim();
            Elevation = elevation ?? throw new ArgumentNullException(nameof(elevation));
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        }
    }

    public sealed class OutfallVerticalConstraint
    {
        public int OutfallNodeId { get; }
        public SourceEngineeringValue ProtectedInvert { get; }
        public FixedEndpointElevationConstraint ConnectedEndpointConstraint { get; }
        public StandardProvenance Provenance { get; }

        public OutfallVerticalConstraint(
            int outfallNodeId,
            StandardProvenance provenance,
            SourceEngineeringValue protectedInvert = null,
            FixedEndpointElevationConstraint connectedEndpointConstraint = null)
        {
            if (outfallNodeId <= 0) throw new ArgumentOutOfRangeException(nameof(outfallNodeId));
            OutfallNodeId = outfallNodeId;
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            ProtectedInvert = protectedInvert;
            ConnectedEndpointConstraint = connectedEndpointConstraint;
        }
    }

    public enum VerticalMatchingMode
    {
        None = 0,
        InvertMatch = 1,
        CrownMatch = 2
    }

    public sealed class VerticalMatchConstraint
    {
        public int NodeId { get; }
        public VerticalMatchingMode Mode { get; }
        public ReadOnlyCollection<GravityConduitEnd> ParticipatingConduitEnds { get; }
        public StandardProvenance Provenance { get; }

        public VerticalMatchConstraint(
            int nodeId,
            VerticalMatchingMode mode,
            IEnumerable<GravityConduitEnd> participatingConduitEnds,
            StandardProvenance provenance)
        {
            if (nodeId <= 0) throw new ArgumentOutOfRangeException(nameof(nodeId));
            if (mode == VerticalMatchingMode.None) throw new ArgumentException("Mode cannot be None for a VerticalMatchConstraint.", nameof(mode));
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            NodeId = nodeId;
            Mode = mode;

            var list = participatingConduitEnds != null ? participatingConduitEnds.ToList() : new List<GravityConduitEnd>();
            if (list.Count < 2) throw new ArgumentException("Match constraint requires at least two participating conduit ends.", nameof(participatingConduitEnds));
            var set = new HashSet<GravityConduitEnd>();
            foreach (var end in list)
            {
                if (end.NodeId != nodeId) throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Participating end {0} does not belong to Node {1}.", end, nodeId));
                if (!set.Add(end)) throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Duplicate participating end {0} at Node {1}.", end, nodeId));
            }
            ParticipatingConduitEnds = new ReadOnlyCollection<GravityConduitEnd>(list);
        }
    }

    public enum VerticalCoverReference
    {
        GroundToCrown = 1
    }

    public sealed class VerticalCoverConstraint
    {
        public GravityConduitEnd ConduitEnd { get; }
        public SourceEngineeringValue GroundElevation { get; }
        public SourceEngineeringValue MinimumCover { get; }
        public VerticalCoverReference Reference { get; } = VerticalCoverReference.GroundToCrown;
        public StandardProvenance Provenance { get; }

        public VerticalCoverConstraint(
            GravityConduitEnd conduitEnd,
            SourceEngineeringValue groundElevation,
            SourceEngineeringValue minimumCover,
            StandardProvenance provenance,
            VerticalCoverReference reference = VerticalCoverReference.GroundToCrown)
        {
            ConduitEnd = conduitEnd ?? throw new ArgumentNullException(nameof(conduitEnd));
            GroundElevation = groundElevation ?? throw new ArgumentNullException(nameof(groundElevation));
            MinimumCover = minimumCover ?? throw new ArgumentNullException(nameof(minimumCover));
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            if (reference != VerticalCoverReference.GroundToCrown) throw new ArgumentException("Only GroundToCrown reference is supported.", nameof(reference));
            Reference = reference;
        }
    }

    public sealed class VerticalSlopeConstraint
    {
        public int ConduitId { get; }
        public double? MinimumSlope { get; }
        public double? MaximumSlope { get; }
        public StandardProvenance Provenance { get; }

        public VerticalSlopeConstraint(
            int conduitId,
            double? minimumSlope,
            double? maximumSlope,
            StandardProvenance provenance)
        {
            if (conduitId <= 0) throw new ArgumentOutOfRangeException(nameof(conduitId));
            if (!minimumSlope.HasValue && !maximumSlope.HasValue) throw new ArgumentException("At least one slope bound (MinimumSlope or MaximumSlope) must exist.");
            if (minimumSlope.HasValue && double.IsNaN(minimumSlope.Value)) throw new ArgumentException("MinimumSlope cannot be NaN.");
            if (maximumSlope.HasValue && double.IsNaN(maximumSlope.Value)) throw new ArgumentException("MaximumSlope cannot be NaN.");
            if (minimumSlope.HasValue && maximumSlope.HasValue && minimumSlope.Value > maximumSlope.Value)
                throw new ArgumentException("MinimumSlope cannot be greater than MaximumSlope.");
            ConduitId = conduitId;
            MinimumSlope = minimumSlope;
            MaximumSlope = maximumSlope;
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        }
    }

    public enum VerticalFeasibilityStatus
    {
        Feasible = 1,
        FeasibleMultipleSolutions = 2,
        Infeasible = 3,
        Incomplete = 4,
        EngineeringDecisionRequired = 5
    }

    public sealed class VerticalConstraintConflict
    {
        public string ConflictCode { get; }
        public ReadOnlyCollection<string> AffectedElements { get; }
        public ReadOnlyCollection<string> ConstraintIds { get; }
        public ReadOnlyDictionary<string, double> KnownValues { get; }
        public ReadOnlyCollection<StandardProvenance> Provenance { get; }
        public string Explanation { get; }

        public VerticalConstraintConflict(
            string conflictCode,
            IEnumerable<string> affectedElements,
            IEnumerable<string> constraintIds,
            IDictionary<string, double> knownValues,
            IEnumerable<StandardProvenance> provenance,
            string explanation)
        {
            ConflictCode = conflictCode ?? "UNKNOWN_CONFLICT";
            AffectedElements = new ReadOnlyCollection<string>(affectedElements != null ? affectedElements.ToList() : new List<string>());
            ConstraintIds = new ReadOnlyCollection<string>(constraintIds != null ? constraintIds.ToList() : new List<string>());
            KnownValues = new ReadOnlyDictionary<string, double>(knownValues != null ? new Dictionary<string, double>(knownValues) : new Dictionary<string, double>());
            Provenance = new ReadOnlyCollection<StandardProvenance>(provenance != null ? provenance.Where(p => p != null).ToList() : new List<StandardProvenance>());
            Explanation = explanation ?? string.Empty;
        }
    }

    public sealed class EngineeringDecisionRequest
    {
        public string Decision { get; }
        public ReadOnlyDictionary<string, string> KnownFacts { get; }
        public ReadOnlyCollection<string> AvailableChoices { get; }
        public ReadOnlyDictionary<string, string> Consequences { get; }
        public ReadOnlyCollection<string> IndependentWorkThatCanContinue { get; }

        public EngineeringDecisionRequest(
            string decision,
            IDictionary<string, string> knownFacts,
            IEnumerable<string> availableChoices,
            IDictionary<string, string> consequences,
            IEnumerable<string> independentWorkThatCanContinue)
        {
            Decision = decision ?? string.Empty;
            KnownFacts = new ReadOnlyDictionary<string, string>(knownFacts != null ? new Dictionary<string, string>(knownFacts) : new Dictionary<string, string>());
            AvailableChoices = new ReadOnlyCollection<string>(availableChoices != null ? availableChoices.ToList() : new List<string>());
            Consequences = new ReadOnlyDictionary<string, string>(consequences != null ? new Dictionary<string, string>(consequences) : new Dictionary<string, string>());
            IndependentWorkThatCanContinue = new ReadOnlyCollection<string>(independentWorkThatCanContinue != null ? independentWorkThatCanContinue.ToList() : new List<string>());
        }
    }

    public sealed class EndpointElevationRange
    {
        public GravityConduitEnd ConduitEnd { get; }
        public double? MinimumAllowedInvert { get; }
        public double? MaximumAllowedInvert { get; }
        public double? ExactInvert { get; }
        public SourceUnitIdentity Unit { get; }

        public EndpointElevationRange(
            GravityConduitEnd conduitEnd,
            double? minimumAllowedInvert,
            double? maximumAllowedInvert,
            double? exactInvert,
            SourceUnitIdentity unit = null)
        {
            ConduitEnd = conduitEnd ?? throw new ArgumentNullException(nameof(conduitEnd));
            MinimumAllowedInvert = minimumAllowedInvert;
            MaximumAllowedInvert = maximumAllowedInvert;
            ExactInvert = exactInvert;
            Unit = unit ?? new SourceUnitIdentity("length", "Feet", "feet", "ft");
        }
    }

    public sealed class VerticalFeasibilityResult
    {
        public VerticalFeasibilityStatus Status { get; }
        public string ConstraintFingerprint { get; }
        public ReadOnlyDictionary<string, EndpointElevationRange> EndpointRanges { get; }
        public ReadOnlyCollection<VerticalConstraintConflict> Conflicts { get; }
        public ReadOnlyCollection<EngineeringDecisionRequest> DecisionRequests { get; }
        public ReadOnlyCollection<string> IncompleteItems { get; }
        public int PropagationPasses { get; }

        public VerticalFeasibilityResult(
            VerticalFeasibilityStatus status,
            string constraintFingerprint,
            IDictionary<string, EndpointElevationRange> endpointRanges,
            IEnumerable<VerticalConstraintConflict> conflicts,
            IEnumerable<EngineeringDecisionRequest> decisionRequests,
            IEnumerable<string> incompleteItems,
            int propagationPasses)
        {
            Status = status;
            ConstraintFingerprint = constraintFingerprint ?? string.Empty;
            EndpointRanges = new ReadOnlyDictionary<string, EndpointElevationRange>(
                endpointRanges != null ? new Dictionary<string, EndpointElevationRange>(endpointRanges, StringComparer.Ordinal) : new Dictionary<string, EndpointElevationRange>(StringComparer.Ordinal));
            Conflicts = new ReadOnlyCollection<VerticalConstraintConflict>(conflicts != null ? conflicts.ToList() : new List<VerticalConstraintConflict>());
            DecisionRequests = new ReadOnlyCollection<EngineeringDecisionRequest>(decisionRequests != null ? decisionRequests.ToList() : new List<EngineeringDecisionRequest>());
            IncompleteItems = new ReadOnlyCollection<string>(incompleteItems != null ? incompleteItems.ToList() : new List<string>());
            PropagationPasses = propagationPasses;
        }
    }

    public sealed class GravityVerticalDesignConstraintSet
    {
        public string SourceNetworkFingerprint { get; }
        public string DesignBasisId { get; }
        public string DesignBasisVersion { get; }
        public ReadOnlyDictionary<string, GravityDesignOrientation> Orientations { get; }
        public ReadOnlyCollection<FixedEndpointElevationConstraint> FixedConstraints { get; }
        public ReadOnlyCollection<ProtectedNodeElevationConstraint> ProtectedConstraints { get; }
        public ReadOnlyCollection<OutfallVerticalConstraint> OutfallConstraints { get; }
        public ReadOnlyCollection<VerticalMatchConstraint> MatchConstraints { get; }
        public ReadOnlyCollection<VerticalCoverConstraint> CoverConstraints { get; }
        public ReadOnlyCollection<VerticalSlopeConstraint> SlopeConstraints { get; }
        public string VerticalConstraintFingerprint { get; }

        public bool TryGetOrientation(int conduitId, out GravityDesignOrientation orientation)
        {
            if (Orientations == null)
            {
                orientation = null;
                return false;
            }
            return Orientations.TryGetValue(conduitId.ToString(CultureInfo.InvariantCulture), out orientation);
        }

        public GravityVerticalDesignConstraintSet(
            string sourceNetworkFingerprint,
            string designBasisId,
            string designBasisVersion,
            IDictionary<int, GravityDesignOrientation> orientations,
            IEnumerable<FixedEndpointElevationConstraint> fixedConstraints,
            IEnumerable<ProtectedNodeElevationConstraint> protectedConstraints,
            IEnumerable<OutfallVerticalConstraint> outfallConstraints,
            IEnumerable<VerticalMatchConstraint> matchConstraints,
            IEnumerable<VerticalCoverConstraint> coverConstraints,
            IEnumerable<VerticalSlopeConstraint> slopeConstraints)
        {
            SourceNetworkFingerprint = sourceNetworkFingerprint ?? string.Empty;
            DesignBasisId = designBasisId ?? string.Empty;
            DesignBasisVersion = designBasisVersion ?? string.Empty;

            var oMap = new Dictionary<string, GravityDesignOrientation>(StringComparer.Ordinal);
            if (orientations != null)
            {
                foreach (var kvp in orientations)
                {
                    oMap[kvp.Key.ToString(CultureInfo.InvariantCulture)] = kvp.Value;
                }
            }
            Orientations = new ReadOnlyDictionary<string, GravityDesignOrientation>(oMap);

            FixedConstraints = new ReadOnlyCollection<FixedEndpointElevationConstraint>(
                fixedConstraints != null ? fixedConstraints.ToList() : new List<FixedEndpointElevationConstraint>());
            ProtectedConstraints = new ReadOnlyCollection<ProtectedNodeElevationConstraint>(
                protectedConstraints != null ? protectedConstraints.ToList() : new List<ProtectedNodeElevationConstraint>());
            OutfallConstraints = new ReadOnlyCollection<OutfallVerticalConstraint>(
                outfallConstraints != null ? outfallConstraints.ToList() : new List<OutfallVerticalConstraint>());
            MatchConstraints = new ReadOnlyCollection<VerticalMatchConstraint>(
                matchConstraints != null ? matchConstraints.ToList() : new List<VerticalMatchConstraint>());
            CoverConstraints = new ReadOnlyCollection<VerticalCoverConstraint>(
                coverConstraints != null ? coverConstraints.ToList() : new List<VerticalCoverConstraint>());
            SlopeConstraints = new ReadOnlyCollection<VerticalSlopeConstraint>(
                slopeConstraints != null ? slopeConstraints.ToList() : new List<VerticalSlopeConstraint>());

            VerticalConstraintFingerprint = CalculateFingerprint(
                SourceNetworkFingerprint,
                DesignBasisId,
                DesignBasisVersion,
                Orientations.Values,
                FixedConstraints,
                ProtectedConstraints,
                OutfallConstraints,
                MatchConstraints,
                CoverConstraints,
                SlopeConstraints);
        }

        public static string CalculateFingerprint(
            string networkFingerprint,
            string designBasisId,
            string designBasisVersion,
            IEnumerable<GravityDesignOrientation> orientations,
            IEnumerable<FixedEndpointElevationConstraint> fixedConstraints,
            IEnumerable<ProtectedNodeElevationConstraint> protectedConstraints,
            IEnumerable<OutfallVerticalConstraint> outfallConstraints,
            IEnumerable<VerticalMatchConstraint> matchConstraints,
            IEnumerable<VerticalCoverConstraint> coverConstraints,
            IEnumerable<VerticalSlopeConstraint> slopeConstraints)
        {
            var sb = new StringBuilder();
            sb.Append("NET_FP:").Append(networkFingerprint ?? string.Empty).Append("\n");
            sb.Append("DB_ID:").Append(designBasisId ?? string.Empty).Append("\n");
            sb.Append("DB_VER:").Append(designBasisVersion ?? string.Empty).Append("\n");

            sb.Append("ORIENTATIONS:\n");
            if (orientations != null)
            {
                foreach (var o in orientations.OrderBy(x => x.ConduitId))
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  C{0}:{1}->{2}|P:{3}\n",
                        o.ConduitId, o.FromNodeId, o.ToNodeId, FormatProvenanceKey(o.Provenance));
                }
            }

            sb.Append("FIXED:\n");
            if (fixedConstraints != null)
            {
                foreach (var f in fixedConstraints.OrderBy(x => x.ConduitEnd.ConduitId).ThenBy(x => x.ConduitEnd.End))
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  C{0}:{1}|V:{2}|P:{3}\n",
                        f.ConduitEnd.ConduitId, f.ConduitEnd.End,
                        f.InvertElevation?.NumericValue?.ToString("R", CultureInfo.InvariantCulture),
                        FormatProvenanceKey(f.Provenance));
                }
            }

            sb.Append("PROTECTED:\n");
            if (protectedConstraints != null)
            {
                foreach (var p in protectedConstraints.OrderBy(x => x.NodeId).ThenBy(x => x.ElevationKind, StringComparer.Ordinal))
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  N{0}:{1}|V:{2}|P:{3}\n",
                        p.NodeId, p.ElevationKind,
                        p.Elevation?.NumericValue?.ToString("R", CultureInfo.InvariantCulture),
                        FormatProvenanceKey(p.Provenance));
                }
            }

            sb.Append("OUTFALL:\n");
            if (outfallConstraints != null)
            {
                foreach (var o in outfallConstraints.OrderBy(x => x.OutfallNodeId))
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  N{0}|V:{1}|P:{2}\n",
                        o.OutfallNodeId,
                        o.ProtectedInvert?.NumericValue?.ToString("R", CultureInfo.InvariantCulture),
                        FormatProvenanceKey(o.Provenance));
                }
            }

            sb.Append("MATCH:\n");
            if (matchConstraints != null)
            {
                foreach (var m in matchConstraints.OrderBy(x => x.NodeId).ThenBy(x => (int)x.Mode))
                {
                    string ends = string.Join(",", m.ParticipatingConduitEnds.OrderBy(e => e.ConduitId).ThenBy(e => e.End).Select(e => string.Format(CultureInfo.InvariantCulture, "C{0}:{1}", e.ConduitId, e.End)));
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  N{0}:{1}|ENDS:{2}|P:{3}\n",
                        m.NodeId, m.Mode, ends, FormatProvenanceKey(m.Provenance));
                }
            }

            sb.Append("COVER:\n");
            if (coverConstraints != null)
            {
                foreach (var c in coverConstraints.OrderBy(x => x.ConduitEnd.ConduitId).ThenBy(x => x.ConduitEnd.End))
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  C{0}:{1}|G:{2}|MIN:{3}|REF:{4}|P:{5}\n",
                        c.ConduitEnd.ConduitId, c.ConduitEnd.End,
                        c.GroundElevation?.NumericValue?.ToString("R", CultureInfo.InvariantCulture),
                        c.MinimumCover?.NumericValue?.ToString("R", CultureInfo.InvariantCulture),
                        c.Reference,
                        FormatProvenanceKey(c.Provenance));
                }
            }

            sb.Append("SLOPE:\n");
            if (slopeConstraints != null)
            {
                foreach (var s in slopeConstraints.OrderBy(x => x.ConduitId))
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  C{0}|MIN:{1}|MAX:{2}|P:{3}\n",
                        s.ConduitId,
                        s.MinimumSlope?.ToString("R", CultureInfo.InvariantCulture),
                        s.MaximumSlope?.ToString("R", CultureInfo.InvariantCulture),
                        FormatProvenanceKey(s.Provenance));
                }
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string FormatProvenanceKey(StandardProvenance p)
        {
            if (p == null) return "NO_PROV";
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}:{3}",
                (int)p.SourceLayer, p.StandardId ?? "", p.StandardVersion ?? "", p.ParameterKey ?? "");
        }
    }

    public sealed class GravityVerticalDesignConstraintSetBuilder
    {
        private readonly Dictionary<int, GravityDesignOrientation> orientations = new Dictionary<int, GravityDesignOrientation>();
        private readonly List<FixedEndpointElevationConstraint> fixedConstraints = new List<FixedEndpointElevationConstraint>();
        private readonly List<ProtectedNodeElevationConstraint> protectedConstraints = new List<ProtectedNodeElevationConstraint>();
        private readonly List<OutfallVerticalConstraint> outfallConstraints = new List<OutfallVerticalConstraint>();
        private readonly List<VerticalMatchConstraint> matchConstraints = new List<VerticalMatchConstraint>();
        private readonly List<VerticalCoverConstraint> coverConstraints = new List<VerticalCoverConstraint>();
        private readonly List<VerticalSlopeConstraint> slopeConstraints = new List<VerticalSlopeConstraint>();

        public GravityVerticalDesignConstraintSetBuilder SetOrientation(int conduitId, int fromNodeId, int toNodeId, StandardProvenance provenance)
        {
            orientations[conduitId] = new GravityDesignOrientation(conduitId, fromNodeId, toNodeId, provenance);
            return this;
        }

        public GravityVerticalDesignConstraintSetBuilder AddFixedConstraint(GravityConduitEnd conduitEnd, SourceEngineeringValue invertElevation, StandardProvenance provenance, string reason = null)
        {
            fixedConstraints.Add(new FixedEndpointElevationConstraint(conduitEnd, invertElevation, provenance, reason));
            return this;
        }

        public GravityVerticalDesignConstraintSetBuilder AddProtectedConstraint(int nodeId, string elevationKind, SourceEngineeringValue elevation, StandardProvenance provenance)
        {
            protectedConstraints.Add(new ProtectedNodeElevationConstraint(nodeId, elevationKind, elevation, provenance));
            return this;
        }

        public GravityVerticalDesignConstraintSetBuilder AddOutfallConstraint(int outfallNodeId, StandardProvenance provenance, SourceEngineeringValue protectedInvert = null, FixedEndpointElevationConstraint connectedEnd = null)
        {
            outfallConstraints.Add(new OutfallVerticalConstraint(outfallNodeId, provenance, protectedInvert, connectedEnd));
            return this;
        }

        public GravityVerticalDesignConstraintSetBuilder AddMatchConstraint(int nodeId, VerticalMatchingMode mode, IEnumerable<GravityConduitEnd> ends, StandardProvenance provenance)
        {
            matchConstraints.Add(new VerticalMatchConstraint(nodeId, mode, ends, provenance));
            return this;
        }

        public GravityVerticalDesignConstraintSetBuilder AddCoverConstraint(GravityConduitEnd conduitEnd, SourceEngineeringValue groundElevation, SourceEngineeringValue minimumCover, StandardProvenance provenance, VerticalCoverReference reference = VerticalCoverReference.GroundToCrown)
        {
            coverConstraints.Add(new VerticalCoverConstraint(conduitEnd, groundElevation, minimumCover, provenance, reference));
            return this;
        }

        public GravityVerticalDesignConstraintSetBuilder AddSlopeConstraint(int conduitId, double? minimumSlope, double? maximumSlope, StandardProvenance provenance)
        {
            slopeConstraints.Add(new VerticalSlopeConstraint(conduitId, minimumSlope, maximumSlope, provenance));
            return this;
        }

        public GravityVerticalDesignConstraintSet Build(string networkFingerprint, string designBasisId = null, string designBasisVersion = null)
        {
            return new GravityVerticalDesignConstraintSet(
                networkFingerprint,
                designBasisId,
                designBasisVersion,
                orientations,
                fixedConstraints,
                protectedConstraints,
                outfallConstraints,
                matchConstraints,
                coverConstraints,
                slopeConstraints);
        }
    }
}
