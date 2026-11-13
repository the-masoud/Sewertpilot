using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    // Pure, non-authoritative proposal selection. No workspace transitions, persistence or runtime calls.
    public sealed class GravityVerticalDesignProposalResolver
    {
        public const string ObjectiveId = "ShallowestFeasibleInvertProfile";
        public const string ObjectiveVersion = "1";
        private const double Tolerance = 1e-8; // Numerical validation only; not an engineering criterion.
        private static readonly SourceUnitIdentity Feet = new SourceUnitIdentity("length", "Feet", "feet", "ft");

        public sealed class Result
        {
            public string Status { get; }
            public string Objective { get; }
            public string ObjectiveRevision { get; }
            public string GeneratorVersion { get; }
            public string NetworkFingerprint { get; }
            public string DesignBasisContentFingerprint { get; }
            public string ConstraintFingerprint { get; }
            public string ConstraintContentFingerprint { get; }
            public string FeasibilityIdentity { get; }
            public string CandidateFingerprint { get; }
            public string ProposalFingerprint { get; }
            public string CandidateRecipeId { get; }
            public int P13InvocationCount { get; }
            public int P14InvocationCount { get; }
            public int RelaxationPasses { get; }
            public ReadOnlyCollection<CandidateEndpointDesign> Endpoints { get; }
            public ReadOnlyCollection<CandidateConduitVerticalState> DerivedConduits { get; }
            public ReadOnlyCollection<string> Evidence { get; }
            public ReadOnlyCollection<string> Reasons { get; }
            private readonly string recipeJson;
            private readonly string provenanceJson;
            // Existing provenance objects are mutable. Return detached copies, never the result's backing data.
            public GravityCandidateRecipe Recipe => recipeJson == null ? null : GravityEngineeringWire.Read<GravityCandidateRecipe>(
                RuntimeHostProtocol.Serializer().DeserializeObject(recipeJson));
            public StandardProvenance Provenance => provenanceJson == null ? null : RuntimeHostProtocol.Serializer().Deserialize<StandardProvenance>(provenanceJson);

            internal Result(State s, string status, string reason = null, GravityDesignCandidate candidate = null,
                GravityCandidateRecipe recipe = null, StandardProvenance provenance = null)
            {
                Status = status; Objective = s.Objective; ObjectiveRevision = s.Revision; GeneratorVersion = s.Generator;
                NetworkFingerprint = s.Network?.NetworkFingerprint; DesignBasisContentFingerprint = s.BasisFingerprint;
                ConstraintFingerprint = s.Constraints?.VerticalConstraintFingerprint; ConstraintContentFingerprint = s.ConstraintContent;
                FeasibilityIdentity = s.FeasibilityIdentity; P13InvocationCount = s.P13Calls; P14InvocationCount = s.P14Calls;
                RelaxationPasses = s.Passes; CandidateFingerprint = candidate?.CandidateFingerprint; CandidateRecipeId = candidate?.RecipeId;
                Endpoints = new ReadOnlyCollection<CandidateEndpointDesign>(candidate?.EndpointDesigns.ToList() ?? new List<CandidateEndpointDesign>());
                DerivedConduits = new ReadOnlyCollection<CandidateConduitVerticalState>(candidate?.DerivedConduits.ToList() ?? new List<CandidateConduitVerticalState>());
                Evidence = new ReadOnlyCollection<string>(s.Evidence.ToList());
                Reasons = new ReadOnlyCollection<string>(reason == null ? new List<string>() : new List<string> { reason });
                recipeJson = recipe == null ? null : Json(recipe); provenanceJson = provenance == null ? null : Json(provenance);
                ProposalFingerprint = candidate == null ? null : Hash(Json(new { InputIdentity = s.InputIdentity,
                    CandidateFingerprint, RecipeId = recipe.RecipeId, Selections = recipe.EndpointSelections }));
            }
        }

        internal sealed class State
        {
            internal GravityNetworkSnapshot Network;
            internal GravityVerticalDesignConstraintSet Constraints;
            internal string Objective, Revision, BasisFingerprint, ConstraintContent, FeasibilityIdentity, InputIdentity;
            internal string Generator = typeof(GravityCandidateGenerator).Assembly.GetName().Version.ToString();
            internal int P13Calls, P14Calls, Passes;
            internal readonly List<string> Evidence = new List<string>();
        }
        private sealed class Failure : Exception
        {
            internal readonly string Classification;
            internal Failure(string classification, string detail) : base(detail) { Classification = classification; }
        }
        private sealed class Edge
        {
            internal int From, To;
            internal double Offset;
            internal string Evidence;
        }
        private static string Json(object value) => GravityExecutionPlanBuilder.Json(value);
        private static string Hash(string value) => GravityExecutionPlanBuilder.Hash(value);
        private static bool Finite(double value) => GravityExecutionPlanBuilder.Finite(value);
        private static void Require(bool condition, string classification, string detail)
        { if (!condition) throw new Failure(classification, detail); }
        private static string SemanticDecision(object decision)
        {
            var match = decision as VerticalMatchConstraint;
            return Json(match == null ? decision : new VerticalMatchConstraint(match.NodeId, match.Mode,
                Ordered(match.ParticipatingConduitEnds), match.Provenance));
        }
        private static IEnumerable<GravityConduitEnd> Ordered(IEnumerable<GravityConduitEnd> ends) =>
            ends.OrderBy(e => e.ConduitId).ThenBy(e => (int)e.End).ThenBy(e => e.NodeId);

        // Engineering content identity, not review authority. Review actors/notes/timestamps are deliberately excluded.
        // Uses existing domain objects and canonical serialization; it defines no persisted schema.
        public static string ContentFingerprint(GravityFlowDesignBasis basis)
        {
            if (basis?.CommonDesignBasis == null) throw new ArgumentException("Design Basis identity is required.", nameof(basis));
            var c = basis.CommonDesignBasis;
            return Hash(Json(new { c.DesignBasisId, c.DesignBasisVersion, c.ProjectId,
                References = (c.StandardReferences ?? new List<DesignBasisStandardReference>()).Select(Json).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                Assumptions = (c.Assumptions ?? new List<DesignBasisAssumption>()).Select(Json).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                Overrides = (c.Overrides ?? new List<DesignBasisOverride>()).Select(Json).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                Sections = basis.EnumerateSectionsInCanonicalOrder().Select(s => s.Criteria.Select(Json).OrderBy(x => x, StringComparer.Ordinal).ToArray()).ToArray() }));
        }
        private static string FeasibilityFingerprint(VerticalFeasibilityResult f) => Hash(Json(new {
            f.Status, f.ConstraintFingerprint,
            Ranges = f.EndpointRanges.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { p.Key, p.Value }).ToArray(),
            Conflicts = f.Conflicts.Select(c => Json(new { c.ConflictCode, c.Explanation,
                Elements = c.AffectedElements.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                Constraints = c.ConstraintIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(), c.KnownValues,
                Provenance = c.Provenance.Select(Json).OrderBy(x => x, StringComparer.Ordinal).ToArray() })).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            Decisions = f.DecisionRequests.Select(d => Json(new { d.Decision, d.KnownFacts, d.Consequences,
                Choices = d.AvailableChoices.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                Independent = d.IndependentWorkThatCanContinue.OrderBy(x => x, StringComparer.Ordinal).ToArray() })).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            Incomplete = f.IncompleteItems.OrderBy(x => x, StringComparer.Ordinal).ToArray()
        })); // PropagationPasses is operational evidence, not engineering-result identity.

        public Result Resolve(GravityNetworkSnapshot network, GravityFlowDesignBasis basis,
            GravityVerticalDesignConstraintSet constraints, VerticalFeasibilityResult suppliedFeasibility,
            string objectiveId, string objectiveRevision, GravityAutomaticPreparationContext current,
            string currentDesignBasisContentFingerprint)
        {
            var s = new State { Network = network, Constraints = constraints, Objective = objectiveId, Revision = objectiveRevision };
            try
            {
                Require(objectiveId == ObjectiveId && objectiveRevision == ObjectiveVersion, "OBJECTIVE_UNSUPPORTED", "Explicit supported objective and revision are required.");
                Require(network != null && constraints != null && suppliedFeasibility != null && current != null && basis?.CommonDesignBasis != null,
                    "INPUT_MISSING", "Current network, constraints, feasibility, Design Basis and context are required.");
                Require(network.SchemaVersion == 2 && !string.IsNullOrWhiteSpace(network.SourceModelFingerprint) && network.Conduits.Count > 0 &&
                    network.NetworkFingerprint == GravityNetworkSnapshot.ComputeFingerprint(network.SchemaVersion, network.SourceModelFingerprint,
                        network.ScenarioContext, network.Nodes, network.Conduits), "SOURCE_INVALID", "Current schema-2 network fingerprint is invalid.");
                var context = network.ScenarioContext;
                Require(context != null && context.ScenarioId > 0 && context.TopologyAlternativeId > 0 && context.PhysicalAlternativeId > 0 &&
                    context.ActiveTopologyAlternativeId == context.TopologyAlternativeId, "SOURCE_CONTEXT_INVALID", "Resolved current scenario/alternatives are required.");
                Require(current.ModelFingerprint == network.SourceModelFingerprint && current.NetworkFingerprint == network.NetworkFingerprint &&
                    Json(current.Scenario) == Json(context), "SOURCE_CONTEXT_MISMATCH", "Model, network or scenario context is stale.");
                Require(current.ActivePolicyId == objectiveId && current.ActivePolicyVersion == objectiveRevision,
                    "OBJECTIVE_CONTEXT_MISMATCH", "Current objective identity/revision is stale.");
                Require(current.ConstraintFingerprint == constraints.VerticalConstraintFingerprint && constraints.SourceNetworkFingerprint == network.NetworkFingerprint,
                    "CONSTRAINT_CONTEXT_MISMATCH", "Constraint or source-network identity is stale.");
                s.BasisFingerprint = ContentFingerprint(basis);
                Require(s.BasisFingerprint == currentDesignBasisContentFingerprint, "DESIGN_BASIS_CONTENT_MISMATCH", "Design Basis content changed.");
                Require(!string.IsNullOrWhiteSpace(basis.CommonDesignBasis.ProjectId) && !string.IsNullOrWhiteSpace(basis.CommonDesignBasis.DesignBasisId) &&
                    !string.IsNullOrWhiteSpace(basis.CommonDesignBasis.DesignBasisVersion), "DESIGN_BASIS_IDENTITY_MISSING", "Project and Design Basis identity are required.");
                // Reuse the existing pure wire/topology/criterion binding guards; this never builds an execution plan or approval.
                GravityExecutionPlanBuilder.ValidateEngineeringConstraints(new GravityEngineeringInputs {
                    DesignBasis = basis, ConstraintsJson = Json(constraints) }, network);
                foreach (var decision in GravityExecutionPlanBuilder.ConstraintDecisions(constraints))
                    GravityExecutionPlanBuilder.ValidateEngineeringProvenance((StandardProvenance)decision.GetType().GetProperty("Provenance").GetValue(decision, null));
                ValidateNetwork(network);
                s.ConstraintContent = Hash(Json(GravityExecutionPlanBuilder.ConstraintDecisions(constraints).Select(SemanticDecision).OrderBy(x => x, StringComparer.Ordinal).ToArray()));
                s.P13Calls++;
                var recomputed = new GravityVerticalFeasibilityEngine().Analyze(network, constraints);
                s.FeasibilityIdentity = FeasibilityFingerprint(recomputed);
                Require(suppliedFeasibility.ConstraintFingerprint == constraints.VerticalConstraintFingerprint &&
                    FeasibilityFingerprint(suppliedFeasibility) == s.FeasibilityIdentity, "FEASIBILITY_MISMATCH", "Supplied P13 result differs from current recomputation.");
                s.Evidence.Add("P13 recomputed: " + recomputed.Status + "; operational passes=" + recomputed.PropagationPasses);
                Require(recomputed.Status == VerticalFeasibilityStatus.Feasible || recomputed.Status == VerticalFeasibilityStatus.FeasibleMultipleSolutions,
                    "P13_" + recomputed.Status.ToString().ToUpperInvariant(), Json(new { recomputed.Conflicts, recomputed.DecisionRequests, recomputed.IncompleteItems }));
                Require(constraints.ProtectedConstraints.Count == 0, "UNSUPPORTED_CONSTRAINT", "ProtectedNodeElevationConstraint has no established conduit-end binding in P13/P14; none may be omitted.");

                var ends = Ordered(network.Conduits.SelectMany(c => new[] { new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Start, c.StartNodeId),
                    new GravityConduitEnd(c.SourceElementId, ConduitEndKind.Stop, c.StopNodeId) })).ToArray();
                var index = ends.Select((e, i) => new { e, i }).ToDictionary(p => p.e, p => p.i);
                var lower = Enumerable.Repeat(double.NegativeInfinity, ends.Length).ToArray();
                var upper = Enumerable.Repeat(double.PositiveInfinity, ends.Length).ToArray();
                var edges = new List<Edge>();
                Action<GravityConduitEnd, double, string> exact = (end, value, reason) => {
                    int i = index[end]; lower[i] = Math.Max(lower[i], value); upper[i] = Math.Min(upper[i], value);
                    s.Evidence.Add(reason + ": " + end + "=" + value.ToString("R", CultureInfo.InvariantCulture) + " ft"); };
                Require(recomputed.EndpointRanges.Count == ends.Length, "P13_COVERAGE_INVALID", "P13 endpoint coverage is incomplete.");
                for (int i = 0; i < ends.Length; i++)
                {
                    Require(recomputed.EndpointRanges.TryGetValue(ends[i].ToString(), out var r) && r.ConduitEnd.Equals(ends[i]) && Feet.Equals(r.Unit),
                        "P13_COVERAGE_INVALID", "Missing or invalid P13 endpoint range: " + ends[i]);
                    if (r.MinimumAllowedInvert.HasValue) { Require(Finite(r.MinimumAllowedInvert.Value), "NONFINITE_BOUND", ends[i].ToString()); lower[i] = r.MinimumAllowedInvert.Value; }
                    if (r.MaximumAllowedInvert.HasValue) { Require(Finite(r.MaximumAllowedInvert.Value), "NONFINITE_BOUND", ends[i].ToString()); upper[i] = r.MaximumAllowedInvert.Value; }
                    if (r.ExactInvert.HasValue) { Require(Finite(r.ExactInvert.Value), "NONFINITE_BOUND", ends[i].ToString()); exact(ends[i], r.ExactInvert.Value, "P13 exact"); }
                }
                foreach (var f in constraints.FixedConstraints) exact(f.ConduitEnd, f.InvertElevation.NumericValue.Value, "Fixed");
                AddOutfalls(network, constraints, ends, exact);
                foreach (var c in constraints.CoverConstraints)
                {
                    double bound = c.GroundElevation.NumericValue.Value - Rise(network, c.ConduitEnd) - c.MinimumCover.NumericValue.Value;
                    Require(Finite(bound), "NONFINITE_BOUND", "Cover arithmetic overflow: " + c.ConduitEnd);
                    int i = index[c.ConduitEnd]; upper[i] = Math.Min(upper[i], bound);
                    s.Evidence.Add("GroundToCrown: " + c.ConduitEnd + " <= " + bound.ToString("R", CultureInfo.InvariantCulture) + " ft; " + Json(c.Provenance));
                }
                Action<GravityConduitEnd, GravityConduitEnd, double, string> add = (from, to, offset, evidence) => {
                    Require(Finite(offset), "NONFINITE_RELATION", evidence); edges.Add(new Edge { From = index[from], To = index[to], Offset = offset, Evidence = evidence }); };
                foreach (var c in constraints.SlopeConstraints)
                {
                    var conduit = network.Conduits.Single(p => p.SourceElementId == c.ConduitId);
                    constraints.TryGetOrientation(c.ConduitId, out var orientation);
                    var from = ends.Single(e => e.ConduitId == c.ConduitId && e.NodeId == orientation.FromNodeId);
                    var to = ends.Single(e => e.ConduitId == c.ConduitId && e.NodeId == orientation.ToNodeId);
                    double length = conduit.ExistingPhysicalState.Length.Value;
                    if (c.MinimumSlope.HasValue) add(from, to, -c.MinimumSlope.Value * length, "MinimumSlope: " + Json(c));
                    if (c.MaximumSlope.HasValue) add(to, from, c.MaximumSlope.Value * length, "MaximumSlope: " + Json(c));
                }
                foreach (var m in constraints.MatchConstraints)
                {
                    Require(m.Mode == VerticalMatchingMode.InvertMatch || m.Mode == VerticalMatchingMode.CrownMatch,
                        "UNSUPPORTED_CONSTRAINT", "Unsupported matching mode: " + m.Mode);
                    var sorted = Ordered(m.ParticipatingConduitEnds).ToArray();
                    for (int j = 1; j < sorted.Length; j++)
                    {
                        double offset = m.Mode == VerticalMatchingMode.CrownMatch ? Rise(network, sorted[0]) - Rise(network, sorted[j]) : 0;
                        add(sorted[0], sorted[j], offset, "Match: " + SemanticDecision(m));
                        add(sorted[j], sorted[0], -offset, "Match: " + SemanticDecision(m));
                    }
                }
                edges = edges.OrderBy(e => e.From).ThenBy(e => e.To).ThenBy(e => e.Offset).ThenBy(e => e.Evidence, StringComparer.Ordinal).ToList();
                foreach (var e in edges) s.Evidence.Add(ends[e.To] + " <= " + ends[e.From] + " + " + e.Offset.ToString("R", CultureInfo.InvariantCulture) + "; " + e.Evidence);
                var ceiling = (double[])upper.Clone(); // Retain every bound when exact values are restored after relaxation.
                bool allExact = recomputed.EndpointRanges.Values.All(r => r.ExactInvert.HasValue);
                Require(recomputed.Status != VerticalFeasibilityStatus.Feasible || allExact, "P13_EXACT_PROFILE_INVALID", "Feasible requires every endpoint exact.");
                if (!allExact)
                {
                    // One edge traversal per pass updates upper distances and zero-origin cycle witnesses together.
                    // The witness detects negative cycles even in components without finite upper anchors.
                    var witness = new double[ends.Length];
                    for (int pass = 0; pass < ends.Length - 1; pass++)
                    {
                        bool changed = false; s.Passes++;
                        foreach (var e in edges)
                        {
                            if (Finite(upper[e.From]))
                            {
                                double bound = upper[e.From] + e.Offset;
                                Require(Finite(bound), "NONFINITE_RELATION", e.Evidence);
                                if (bound < upper[e.To]) { upper[e.To] = bound; changed = true; }
                            }
                            double cycle = witness[e.From] + e.Offset;
                            Require(Finite(cycle), "NONFINITE_RELATION", e.Evidence);
                            if (cycle < witness[e.To]) { witness[e.To] = cycle; changed = true; }
                        }
                        if (!changed) break;
                    }
                    foreach (var e in edges) Require(witness[e.To] <= witness[e.From] + e.Offset + Tolerance &&
                        (!Finite(upper[e.From]) || upper[e.To] <= upper[e.From] + e.Offset + Tolerance),
                        "CONTRADICTION_OR_NEGATIVE_CYCLE", e.Evidence);
                }
                // Preserve P13 exact values byte-for-value; no recipe can override them.
                for (int i = 0; i < ends.Length; i++)
                    if (recomputed.EndpointRanges[ends[i].ToString()].ExactInvert.HasValue)
                        upper[i] = recomputed.EndpointRanges[ends[i].ToString()].ExactInvert.Value;
                ValidateVector(ends, upper, lower, ceiling, edges, recomputed, s.Evidence);
                s.InputIdentity = Hash(Json(new { Objective = objectiveId, ObjectiveRevision = objectiveRevision,
                    Generator = s.Generator, Model = network.SourceModelFingerprint, Scenario = context,
                    Network = network.NetworkFingerprint, Basis = s.BasisFingerprint, Constraint = constraints.VerticalConstraintFingerprint,
                    ConstraintContent = s.ConstraintContent, Feasibility = s.FeasibilityIdentity }));
                var provenance = new StandardProvenance { SourceLayer = StandardSourceLayer.ApplicationDefault,
                    StandardId = ObjectiveId, StandardVersion = ObjectiveVersion, ParameterKey = "proposal.invert.objective",
                    Notes = "NON_AUTHORITATIVE_PROPOSAL; componentwise highest feasible invert; InputIdentity=" + s.InputIdentity };
                var selections = ends.Select((e, i) => new { e, i }).Where(p => !recomputed.EndpointRanges[p.e.ToString()].ExactInvert.HasValue)
                    .Select(p => new CandidateEndpointSelection(p.e, EndpointSelectionMode.ExplicitValue, provenance, new SourceEngineeringValue(upper[p.i], Feet))).ToArray();
                var recipe = new GravityCandidateRecipe("PROPOSAL_" + s.InputIdentity, selections, provenance, ObjectiveId);
                // Exact P13 profiles use P14's established empty-request exact materialization path.
                var request = new GravityCandidateGenerationRequest(constraints.VerticalConstraintFingerprint,
                    allExact ? new GravityCandidateRecipe[0] : new[] { recipe });
                s.P14Calls++;
                var generated = new GravityCandidateGenerator().Generate(network, constraints, recomputed, request);
                Require(generated.Candidates.Count == 1 && generated.RecipeResults.Count == 1 &&
                    generated.RecipeResults[0].Status == CandidateGenerationStatus.Generated,
                    "P14_REJECTED", Json(generated.RecipeResults));
                var candidate = generated.Candidates[0];
                Require(generated.SourceConstraintFingerprint == constraints.VerticalConstraintFingerprint && candidate.SourceNetworkFingerprint == network.NetworkFingerprint &&
                    candidate.VerticalConstraintFingerprint == constraints.VerticalConstraintFingerprint && candidate.RecipeId == (allExact ? "EXACT_CONSTRAINT_PROFILE" : recipe.RecipeId),
                    "P14_LINEAGE_MISMATCH", "P14 returned a mismatched candidate.");
                Require(candidate.EndpointDesigns.Count == ends.Length && candidate.EndpointDesigns.Select(e => e.ConduitEnd).Distinct().Count() == ends.Length,
                    "P14_COVERAGE_INVALID", "Candidate endpoint coverage is invalid.");
                var values = ends.Select((e, i) => {
                    var endpoint = candidate.EndpointDesigns.Single(p => p.ConduitEnd.Equals(e));
                    Require(Feet.Equals(endpoint.Unit) && endpoint.ProposedInvert == upper[i], "P14_VALUES_MISMATCH", e.ToString());
                    return endpoint.ProposedInvert;
                }).ToArray();
                ValidateVector(ends, values, lower, ceiling, edges, recomputed, new List<string>());
                Require(candidate.DerivedConduits.Count == network.Conduits.Count && candidate.DerivedConduits.All(c => Finite(c.Slope) &&
                    (!c.StartCover.HasValue || Finite(c.StartCover.Value)) && (!c.StopCover.HasValue || Finite(c.StopCover.Value))),
                    "P14_DERIVED_INVALID", "Candidate slopes/covers are nonfinite or incomplete.");
                s.Evidence.Add("P14 Generated exactly one matching candidate; bounds/fixed/outfalls/slope/match/cover revalidated; no selection or authorization.");
                return new Result(s, "PROPOSED", candidate: candidate, recipe: recipe, provenance: provenance);
            }
            catch (Failure failure) { return new Result(s, failure.Classification, failure.Message); }
            catch (ArgumentException failure) { return new Result(s, "INPUT_INVALID", failure.Message); }
            catch (InvalidOperationException failure) { return new Result(s, "INPUT_INVALID", failure.Message); }
        }

        private static void ValidateNetwork(GravityNetworkSnapshot network)
        {
            Require(network.Nodes.All(n => n != null && n.SourceElementId > 0 && Enum.IsDefined(typeof(GravityNodeKind), n.Kind)) &&
                network.Nodes.Select(n => n.SourceElementId).Distinct().Count() == network.Nodes.Count &&
                network.Conduits.All(c => c != null && c.SourceElementId > 0) &&
                network.Conduits.Select(c => c.SourceElementId).Distinct().Count() == network.Conduits.Count,
                "SOURCE_INVALID", "Invalid or duplicate network elements.");
            foreach (var c in network.Conduits)
            {
                var p = c.ExistingPhysicalState;
                Require(c.StartNodeId != c.StopNodeId && network.Nodes.Any(n => n.SourceElementId == c.StartNodeId) && network.Nodes.Any(n => n.SourceElementId == c.StopNodeId) &&
                    p?.Length != null && Finite(p.Length.Value) && p.Length.Value > 0 && Feet.Equals(p.LengthUnit), "SOURCE_INVALID", "Invalid conduit endpoints or Feet length: " + c.SourceElementId);
                bool heightValid;
                if (p?.EffectiveSectionHeight.HasValue == true)
                {
                    heightValid = Finite(p.EffectiveSectionHeight.Value) && p.EffectiveSectionHeight.Value > 0 &&
                        Feet.Equals(p.EffectiveSectionHeightUnit ?? p.DiameterUnit ?? p.RiseUnit);
                    if (p.Rise.HasValue && p.Rise.Value > 0)
                    {
                        heightValid = heightValid && Finite(p.Rise.Value) && Feet.Equals(p.RiseUnit) &&
                            Math.Abs(p.Rise.Value - p.EffectiveSectionHeight.Value) <= 1e-4;
                    }
                }
                else
                {
                    heightValid = !p.Rise.HasValue || (Finite(p.Rise.Value) && p.Rise.Value > 0 && Feet.Equals(p.RiseUnit));
                }
                Require(heightValid &&
                    (!p.StartInvert.HasValue || (Finite(p.StartInvert.Value) && Feet.Equals(p.StartInvertUnit))) &&
                    (!p.StopInvert.HasValue || (Finite(p.StopInvert.Value) && Feet.Equals(p.StopInvertUnit))), "UNSUPPORTED_SOURCE_QUANTITY", "Nonfinite or unsupported conduit units: " + c.SourceElementId);
            }
            foreach (var n in network.Nodes)
                Require(n.ExistingElevationState?.GroundElevation == null || (Finite(n.ExistingElevationState.GroundElevation.Value) && Feet.Equals(n.ExistingElevationState.GroundElevationUnit)),
                    "UNSUPPORTED_SOURCE_QUANTITY", "Ground elevation units/quantity: " + n.SourceElementId);
        }
        private static double Rise(GravityNetworkSnapshot network, GravityConduitEnd end)
        {
            var p = network.Conduits.Single(c => c.SourceElementId == end.ConduitId).ExistingPhysicalState;
            if (p?.EffectiveSectionHeight != null)
            {
                Require(Finite(p.EffectiveSectionHeight.Value) && p.EffectiveSectionHeight.Value > 0 &&
                    Feet.Equals(p.EffectiveSectionHeightUnit ?? p.DiameterUnit ?? p.RiseUnit),
                    "UNSUPPORTED_SOURCE_QUANTITY", "Crown/cover requires positive finite effective section height in Feet: " + end);
                return p.EffectiveSectionHeight.Value;
            }
            Require(p?.Rise != null && Finite(p.Rise.Value) && p.Rise.Value > 0 && Feet.Equals(p.RiseUnit),
                "UNSUPPORTED_SOURCE_QUANTITY", "Crown/cover requires positive finite Rise in Feet: " + end);
            return p.Rise.Value;
        }
        private static void AddOutfalls(GravityNetworkSnapshot network, GravityVerticalDesignConstraintSet constraints,
            GravityConduitEnd[] ends, Action<GravityConduitEnd, double, string> exact)
        {
            foreach (var node in network.Nodes.Where(n => n.Kind == GravityNodeKind.Outfall).OrderBy(n => n.SourceElementId))
            {
                var incident = ends.Where(e => e.NodeId == node.SourceElementId).ToArray(); var covered = new HashSet<GravityConduitEnd>();
                Require(incident.Length > 0, "UNSUPPORTED_OUTFALL_BINDING", "Outfall has no connected endpoint: " + node.SourceElementId);
                foreach (var boundary in constraints.OutfallConstraints.Where(b => b.OutfallNodeId == node.SourceElementId))
                {
                    var binding = boundary.ConnectedEndpointConstraint;
                    Require(binding != null || boundary.ProtectedInvert != null, "MISSING_BOUNDARY_AUTHORITY", "Empty/free outfall does not establish an invert boundary: " + node.SourceElementId);
                    Require(binding != null || incident.Length == 1, "UNSUPPORTED_OUTFALL_BINDING", "Protected outfall invert has multiple possible endpoint bindings: " + node.SourceElementId);
                    var end = binding?.ConduitEnd ?? incident[0];
                    Require(incident.Contains(end), "UNSUPPORTED_OUTFALL_BINDING", "Outfall endpoint is not incident: " + node.SourceElementId);
                    if (binding != null) {
                        GravityExecutionPlanBuilder.ValidateEngineeringProvenance(binding.Provenance);
                        exact(end, binding.InvertElevation.NumericValue.Value, "Explicit connected outfall");
                    }
                    if (boundary.ProtectedInvert != null) exact(end, boundary.ProtectedInvert.NumericValue.Value, "Explicit protected outfall");
                    covered.Add(end);
                }
                Require(incident.All(covered.Contains), "MISSING_BOUNDARY_AUTHORITY", "Each connected outfall endpoint needs an explicit unambiguous boundary: " + node.SourceElementId);
            }
        }
        private static void ValidateVector(GravityConduitEnd[] ends, double[] values, double[] lower, double[] ceiling,
            List<Edge> edges, VerticalFeasibilityResult feasibility, List<string> evidence)
        {
            var unbounded = ends.Where((e, i) => double.IsPositiveInfinity(values[i])).ToArray();
            Require(unbounded.Length == 0, "UNBOUNDED_ABOVE", string.Join("; ", unbounded.Select(e => e.ToString())));
            for (int i = 0; i < ends.Length; i++)
            {
                var r = feasibility.EndpointRanges[ends[i].ToString()];
                Require(Finite(values[i]), "NONFINITE_SOLUTION", ends[i].ToString());
                Require(values[i] >= lower[i] - Tolerance && values[i] <= ceiling[i] + Tolerance &&
                    (!r.MaximumAllowedInvert.HasValue || values[i] <= r.MaximumAllowedInvert.Value + Tolerance) &&
                    (!r.ExactInvert.HasValue || values[i] == r.ExactInvert.Value), "BOUND_OR_EXACT_VIOLATION", ends[i].ToString());
                evidence.Add(ends[i] + ": " + (r.ExactInvert.HasValue ? "P13 exact preserved" : "ExplicitValue from coupled greatest vector") + "=" + values[i].ToString("R", CultureInfo.InvariantCulture) + " ft");
            }
            foreach (var e in edges) Require(values[e.To] <= values[e.From] + e.Offset + Tolerance, "COUPLED_CONSTRAINT_VIOLATION", e.Evidence);
        }
    }
}
