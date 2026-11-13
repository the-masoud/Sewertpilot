using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    // The caller supplies the current source AND active policy; constructing a request never selects a policy.
    public sealed class GravityAutomaticPreparationContext
    {
        public string ModelFingerprint { get; }
        public string NetworkFingerprint { get; }
        public GravityScenarioContext Scenario { get; }
        public string ActivePolicyId { get; }
        public string ActivePolicyVersion { get; }
        public string ConstraintFingerprint { get; }
        public GravityAutomaticPreparationContext(string modelFingerprint, string networkFingerprint,
            GravityScenarioContext scenario, string activePolicyId, string activePolicyVersion, string constraintFingerprint)
        {
            ModelFingerprint = modelFingerprint; NetworkFingerprint = networkFingerprint; Scenario = scenario?.Clone();
            ActivePolicyId = activePolicyId; ActivePolicyVersion = activePolicyVersion; ConstraintFingerprint = constraintFingerprint;
        }
    }

    public sealed class GravityPreparationComponent
    {
        public ReadOnlyCollection<int> NodeIds { get; }
        public ReadOnlyCollection<int> ConduitIds { get; }
        public ReadOnlyCollection<string> UnresolvedReasons { get; }
        public int? RootOutfallId { get; }
        public bool Eligible => UnresolvedReasons.Count == 0;
        internal GravityPreparationComponent(IEnumerable<int> nodes, IEnumerable<int> conduits, IEnumerable<string> reasons, int? root)
        {
            NodeIds = Array.AsReadOnly(nodes.OrderBy(i => i).ToArray());
            ConduitIds = Array.AsReadOnly(conduits.OrderBy(i => i).ToArray());
            UnresolvedReasons = Array.AsReadOnly(reasons.Distinct().OrderBy(r => r, StringComparer.Ordinal).ToArray());
            RootOutfallId = root;
        }
    }

    public sealed class GravityOutfallPreparationEvidence
    {
        public int NodeId { get; }
        public GravityElevationState SourceElevations { get; }
        public IReadOnlyDictionary<string, string> PropertyClassifications { get; }
        public ReadOnlyCollection<GravityConduitEnd> ConnectedEndpoints { get; }
        public ReadOnlyCollection<OutfallVerticalConstraint> ExplicitBoundaries { get; }
        public ReadOnlyCollection<string> Classifications { get; }
        public ReadOnlyCollection<string> IncompleteItems { get; }
        public ReadOnlyCollection<string> Conflicts { get; }
        internal GravityOutfallPreparationEvidence(GravityNetworkNode node, IEnumerable<GravityConduitEnd> ends,
            IEnumerable<OutfallVerticalConstraint> boundaries, IEnumerable<string> classes, IEnumerable<string> missing, IEnumerable<string> conflicts)
        {
            NodeId = node.SourceElementId; SourceElevations = node.ExistingElevationState.Clone();
            PropertyClassifications = node.PropertyClassifications;
            ConnectedEndpoints = Array.AsReadOnly(ends.ToArray()); ExplicitBoundaries = Array.AsReadOnly(boundaries.ToArray());
            Classifications = Array.AsReadOnly(classes.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray());
            IncompleteItems = Array.AsReadOnly(missing.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Conflicts = Array.AsReadOnly(conflicts.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }
    }

    public sealed class GravityAutomaticPreparationResult
    {
        public string SourceNetworkFingerprint { get; }
        public string PolicyId { get; }
        public string PolicyVersion { get; }
        public ReadOnlyCollection<string> ContextFailures { get; }
        public ReadOnlyCollection<GravityPreparationComponent> Components { get; }
        // Proposals only: existing matching explicit decisions are retained, never replaced.
        public ReadOnlyCollection<GravityDesignOrientation> DerivedOrientations { get; }
        public ReadOnlyCollection<GravityOutfallPreparationEvidence> Outfalls { get; }
        internal GravityAutomaticPreparationResult(GravityNetworkSnapshot network, string policyId, string version,
            IEnumerable<string> failures, IEnumerable<GravityPreparationComponent> components,
            IEnumerable<GravityDesignOrientation> orientations, IEnumerable<GravityOutfallPreparationEvidence> outfalls)
        {
            SourceNetworkFingerprint = network.NetworkFingerprint; PolicyId = policyId; PolicyVersion = version;
            ContextFailures = Array.AsReadOnly(failures.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Components = Array.AsReadOnly(components.ToArray());
            DerivedOrientations = Array.AsReadOnly(orientations.OrderBy(o => o.ConduitId).ToArray()); Outfalls = Array.AsReadOnly(outfalls.ToArray());
        }
    }

    /// <summary>
    /// Pure drainage-design preparation. Policy v1 routes an undirected tree toward its sole identified
    /// outfall. Elevations and Bentley Start/Stop order supply no routing authority. No hydraulic flow,
    /// boundary elevation, engineering review or execution authorization is inferred.
    /// </summary>
    public sealed class GravityAutomaticPreparationService
    {
        public const string PolicyId = "GravityDrainTowardUniqueOutfall";
        public const string PolicyVersion = "1";
        private const string Derivation = "DERIVED_DRAINAGE_ROUTING";
        public GravityAutomaticPreparationResult Prepare(GravityNetworkSnapshot network, GravityVerticalDesignConstraintSet constraints,
            string selectedPolicyId, string selectedPolicyVersion, GravityAutomaticPreparationContext current)
        {
            if (network == null) throw new ArgumentNullException(nameof(network));
            var failures = ContextFailures(network, constraints, selectedPolicyId, selectedPolicyVersion, current);
            var nodes = network.Nodes.GroupBy(n => n.SourceElementId).ToDictionary(g => g.Key, g => g.ToArray());
            var conduitCounts = network.Conduits.GroupBy(c => c.SourceElementId).ToDictionary(g => g.Key, g => g.Count());
            // Reconstruct adjacency from actual endpoints, including unresolved IDs. Never trust a cached adjacency table.
            var incident = nodes.Keys.ToDictionary(id => id, id => new List<GravityNetworkConduit>());
            foreach (var c in network.Conduits)
                foreach (int id in new[] { c.StartNodeId, c.StopNodeId }.Distinct())
                {
                    if (!incident.ContainsKey(id)) incident[id] = new List<GravityNetworkConduit>();
                    incident[id].Add(c);
                }
            var visited = new HashSet<int>(); var components = new List<GravityPreparationComponent>();
            var derived = new List<GravityDesignOrientation>();
            foreach (int seed in incident.Keys.OrderBy(id => id))
            {
                if (!visited.Add(seed)) continue;
                var queue = new Queue<int>(); queue.Enqueue(seed);
                var componentNodes = new List<int>(); var edges = new HashSet<GravityNetworkConduit>();
                while (queue.Count > 0)
                {
                    int id = queue.Dequeue(); componentNodes.Add(id);
                    foreach (var c in incident[id].OrderBy(c => c.SourceElementId))
                    {
                        edges.Add(c); int other = c.StartNodeId == id ? c.StopNodeId : c.StartNodeId;
                        if (visited.Add(other)) queue.Enqueue(other);
                    }
                }
                var reasons = new List<string>(failures);
                foreach (int id in componentNodes)
                    if (id <= 0 || !nodes.ContainsKey(id) || nodes[id].Length != 1)
                        reasons.Add("INVALID_OR_UNRESOLVED_NODE:" + N(id));
                foreach (var c in edges)
                    if (c.SourceElementId <= 0 || conduitCounts[c.SourceElementId] != 1)
                        reasons.Add("INVALID_OR_DUPLICATE_CONDUIT:" + N(c.SourceElementId));
                var roots = componentNodes.Where(id => nodes.ContainsKey(id) && nodes[id].Any(n => n.Kind == GravityNodeKind.Outfall)).ToArray();
                if (roots.Length == 0) reasons.Add("NO_OUTFALL");
                if (roots.Length > 1) reasons.Add("MULTIPLE_OUTFALLS:" + string.Join(",", roots.OrderBy(id => id).Select(N)));
                if (edges.Count != componentNodes.Count - 1) reasons.Add("CYCLIC_TOPOLOGY");
                var proposals = new List<GravityDesignOrientation>();
                if (reasons.Count == 0)
                {
                    int root = roots.Single(); var parent = new Dictionary<int, int>(); parent[root] = root;
                    queue.Enqueue(root);
                    while (queue.Count > 0)
                    {
                        int id = queue.Dequeue();
                        foreach (var c in incident[id].OrderBy(c => c.SourceElementId))
                        {
                            int other = c.StartNodeId == id ? c.StopNodeId : c.StartNodeId;
                            if (parent.ContainsKey(other)) continue;
                            parent[other] = id; queue.Enqueue(other);
                            var p = Provenance(network, c, root);
                            if (constraints != null && constraints.TryGetOrientation(c.SourceElementId, out var explicitDecision))
                            {
                                if (explicitDecision.FromNodeId != other || explicitDecision.ToNodeId != id)
                                    reasons.Add("EXPLICIT_ORIENTATION_CONFLICT:" + N(c.SourceElementId));
                                if (IsDerived(explicitDecision) && (explicitDecision.Provenance.StandardVersion != PolicyVersion ||
                                    explicitDecision.Provenance.Notes != p.Notes)) reasons.Add("DERIVED_CONTEXT_STALE:" + N(c.SourceElementId));
                            }
                            else proposals.Add(new GravityDesignOrientation(c.SourceElementId, other, id, p));
                        }
                    }
                }
                if (reasons.Count == 0) derived.AddRange(proposals);
                components.Add(new GravityPreparationComponent(componentNodes, edges.Select(c => c.SourceElementId), reasons,
                    roots.Length == 1 ? (int?)roots[0] : null));
            }
            return new GravityAutomaticPreparationResult(network, selectedPolicyId, selectedPolicyVersion, failures, components, derived,
                network.Nodes.Where(n => n.Kind == GravityNodeKind.Outfall).OrderBy(n => n.SourceElementId)
                    .Select(n => OutfallEvidence(network, constraints, n, failures)));
        }

        private static List<string> ContextFailures(GravityNetworkSnapshot network, GravityVerticalDesignConstraintSet constraints,
            string id, string policyRevision, GravityAutomaticPreparationContext current)
        {
            var failures = new List<string>();
            if (id != PolicyId || policyRevision != PolicyVersion) failures.Add("EXPLICIT_SUPPORTED_POLICY_REQUIRED");
            if (current == null || current.ActivePolicyId != id || current.ActivePolicyVersion != policyRevision) failures.Add("POLICY_CONTEXT_STALE");
            if (current == null || string.IsNullOrWhiteSpace(network.SourceModelFingerprint) || string.IsNullOrWhiteSpace(network.NetworkFingerprint) ||
                current.ModelFingerprint != network.SourceModelFingerprint || current.NetworkFingerprint != network.NetworkFingerprint ||
                !SameScenario(current.Scenario, network.ScenarioContext) || network.NetworkFingerprint != GravityNetworkSnapshot.ComputeFingerprint(
                    network.SchemaVersion, network.SourceModelFingerprint, network.ScenarioContext, network.Nodes, network.Conduits)) failures.Add("SOURCE_CONTEXT_STALE");
            if (constraints != null && constraints.SourceNetworkFingerprint != network.NetworkFingerprint) failures.Add("CONSTRAINT_SOURCE_STALE");
            if (current == null || current.ConstraintFingerprint != constraints?.VerticalConstraintFingerprint) failures.Add("CONSTRAINT_CONTEXT_STALE");
            return failures;
        }
        private static bool SameScenario(GravityScenarioContext a, GravityScenarioContext b) => a != null && b != null &&
            a.ScenarioId == b.ScenarioId && a.ScenarioLabel == b.ScenarioLabel && a.TopologyAlternativeId == b.TopologyAlternativeId &&
            a.PhysicalAlternativeId == b.PhysicalAlternativeId && a.ActiveTopologyAlternativeId == b.ActiveTopologyAlternativeId;
        private static string N(int id) => id.ToString(CultureInfo.InvariantCulture);
        internal static bool IsDerived(GravityDesignOrientation o) => o?.Provenance?.SourceLayer == StandardSourceLayer.ApplicationDefault &&
            o.Provenance.StandardId == PolicyId && o.Provenance.ParameterKey == PolicyId + ".orientation." + N(o.ConduitId);
        private static StandardProvenance Provenance(GravityNetworkSnapshot net, GravityNetworkConduit c, int root)
        {
            var s = net.ScenarioContext;
            return new StandardProvenance { SourceLayer = StandardSourceLayer.ApplicationDefault, StandardId = PolicyId,
                StandardName = "Application drainage-design policy (proposed routing; not calculated hydraulic flow)", StandardVersion = PolicyVersion,
                ParameterKey = PolicyId + ".orientation." + N(c.SourceElementId),
                Notes = Derivation + ";Model=" + net.SourceModelFingerprint + ";Network=" + net.NetworkFingerprint +
                    ";Scenario=" + N(s.ScenarioId) + ";Topology=" + N(s.TopologyAlternativeId) + ";ActiveTopology=" + N(s.ActiveTopologyAlternativeId) +
                    ";Physical=" + N(s.PhysicalAlternativeId) + ";Conduit=" + N(c.SourceElementId) + ";Start=" + N(c.StartNodeId) +
                    ";Stop=" + N(c.StopNodeId) + ";RootOutfall=" + N(root) + ";Policy=" + PolicyId + ";Version=" + PolicyVersion };
        }
        private static bool Finite(double? value) => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
        private static bool ValidBoundaryQuantity(SourceEngineeringValue v) => v != null && Finite(v.NumericValue) &&
            new SourceUnitIdentity("length", "Feet", "feet", "ft").Equals(v.Unit);
        private static bool ValidProvenance(StandardProvenance p) => p != null && Enum.IsDefined(typeof(StandardSourceLayer), p.SourceLayer) &&
            !string.IsNullOrWhiteSpace(p.ParameterKey) && (!string.IsNullOrWhiteSpace(p.StandardId) || !string.IsNullOrWhiteSpace(p.DocumentReference));
        private static GravityOutfallPreparationEvidence OutfallEvidence(GravityNetworkSnapshot network,
            GravityVerticalDesignConstraintSet constraints, GravityNetworkNode node, IEnumerable<string> contextFailures)
        {
            var classes = new List<string>(); var missing = new List<string>(); var conflicts = new List<string>(contextFailures);
            var ends = new List<GravityConduitEnd>(); var elevation = node.ExistingElevationState;
            Action<string, double?, SourceUnitIdentity> observe = (name, value, unit) => {
                if (!value.HasValue) missing.Add(name + "_UNAVAILABLE");
                else if (!Finite(value)) conflicts.Add(name + "_NONFINITE");
                else { classes.Add("SOURCE_VALUE_AVAILABLE"); if (unit == null || string.IsNullOrWhiteSpace(unit.UnitId)) missing.Add(name + "_UNIT_UNRESOLVED"); }
            };
            observe("Invert", elevation.InvertElevation, elevation.InvertElevationUnit);
            observe("Ground", elevation.GroundElevation, elevation.GroundElevationUnit);
            observe("Rim", elevation.RimElevation, elevation.RimElevationUnit);
            foreach (var c in network.Conduits.Where(c => c.StartNodeId == node.SourceElementId || c.StopNodeId == node.SourceElementId).OrderBy(c => c.SourceElementId))
                foreach (var kind in new[] { ConduitEndKind.Start, ConduitEndKind.Stop })
                {
                    if ((kind == ConduitEndKind.Start ? c.StartNodeId : c.StopNodeId) != node.SourceElementId) continue;
                    if (c.SourceElementId <= 0 || node.SourceElementId <= 0) { conflicts.Add("INVALID_CONNECTED_ENDPOINT:" + N(c.SourceElementId)); continue; }
                    ends.Add(new GravityConduitEnd(c.SourceElementId, kind, node.SourceElementId));
                    var value = kind == ConduitEndKind.Start ? c.ExistingPhysicalState.StartInvert : c.ExistingPhysicalState.StopInvert;
                    var unit = kind == ConduitEndKind.Start ? c.ExistingPhysicalState.StartInvertUnit : c.ExistingPhysicalState.StopInvertUnit;
                    string label = "Conduit" + N(c.SourceElementId) + "." + kind;
                    observe(label, value, unit);
                    if (Finite(value) && Finite(elevation.InvertElevation))
                    {
                        if (unit != null && elevation.InvertElevationUnit != null && !unit.Equals(elevation.InvertElevationUnit)) conflicts.Add(label + "_SOURCE_UNIT_MISMATCH");
                        else if (unit != null && unit.Equals(elevation.InvertElevationUnit) && value != elevation.InvertElevation) conflicts.Add(label + "_SOURCE_INVERT_MISMATCH");
                    }
                }
            if (ends.Count == 0) missing.Add("CONNECTED_ENDPOINT_UNAVAILABLE");
            if (node.SourceElementId <= 0 || network.Nodes.Count(n => n.SourceElementId == node.SourceElementId) != 1) conflicts.Add("INVALID_OR_DUPLICATE_OUTFALL");
            var boundaries = constraints?.OutfallConstraints.Where(b => b.OutfallNodeId == node.SourceElementId).ToArray() ?? new OutfallVerticalConstraint[0];
            bool confirmed = boundaries.Length == 1 && ValidProvenance(boundaries[0].Provenance) &&
                (boundaries[0].ProtectedInvert != null || boundaries[0].ConnectedEndpointConstraint != null);
            foreach (var b in boundaries)
            {
                bool valid = ValidProvenance(b.Provenance) && (b.ProtectedInvert == null || ValidBoundaryQuantity(b.ProtectedInvert)) &&
                    (b.ConnectedEndpointConstraint == null || (ends.Contains(b.ConnectedEndpointConstraint.ConduitEnd) &&
                        ValidBoundaryQuantity(b.ConnectedEndpointConstraint.InvertElevation) && ValidProvenance(b.ConnectedEndpointConstraint.Provenance)));
                if (b.ProtectedInvert != null && b.ConnectedEndpointConstraint != null &&
                    b.ProtectedInvert.NumericValue != b.ConnectedEndpointConstraint.InvertElevation.NumericValue) valid = false;
                if (!valid) { confirmed = false; conflicts.Add("EXPLICIT_BOUNDARY_INVALID"); }
            }
            if (boundaries.Length > 1) conflicts.Add("MULTIPLE_EXPLICIT_BOUNDARIES");
            if (contextFailures.Any()) confirmed = false;
            if (confirmed) classes.Add("EXPLICIT_BOUNDARY_CONFIRMED");
            else { classes.Add("BOUNDARY_UNRESOLVED"); missing.Add("PROTECTED_OR_CONNECTED_BOUNDARY_AUTHORITY_REQUIRED"); }
            if (conflicts.Count > 0) classes.Add("SOURCE_CONFLICT");
            return new GravityOutfallPreparationEvidence(node, ends, boundaries, classes, missing, conflicts);
        }
    }
}
