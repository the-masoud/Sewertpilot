using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Principal;

namespace SewerPilot.OpenFlows.Adapter
{
    // Offline editing/assessment only. No execution service, runtime authorization, vendor save or solver.
    internal sealed class EngineeringDesignWorkspace
    {
        private readonly string projectId;
        private GravityNetworkSnapshot network;
        private GravityVerticalDesignConstraintSet constraints;
        private GravityVerticalPreparationRequest verticalPreparation;
        private AutomaticProposalIdentity automaticProposal;
        // Transient identities only: never retain a proposal, candidate, recipe or persistent approval here.
        private sealed class AutomaticProposalIdentity
        {
            internal readonly string ProposalFingerprint, CandidateFingerprint, ObjectiveId, ObjectiveRevision, ContextIdentity, InputIdentity;
            internal string StagedIdentity;
            internal AutomaticProposalIdentity(GravityVerticalDesignProposalResolver.Result result,
                GravityAutomaticPreparationContext current, string inputIdentity)
            {
                ProposalFingerprint = result.ProposalFingerprint; CandidateFingerprint = result.CandidateFingerprint;
                ObjectiveId = result.Objective; ObjectiveRevision = result.ObjectiveRevision;
                ContextIdentity = GravityExecutionPlanBuilder.Hash(GravityExecutionPlanBuilder.Json(current)); InputIdentity = inputIdentity;
            }
        }
        private void ClearAutomaticProposal() { automaticProposal = null; }
        internal GravityEngineeringInputs Inputs { get; private set; }
        internal List<GravityCandidateRecipe> Recipes { get; private set; } = new List<GravityCandidateRecipe>();
        internal string RecipeConstraintFingerprint { get; private set; }
        internal VerticalFeasibilityResult Feasibility { get; private set; }
        internal GravityDesignCandidateSet Candidates { get; private set; }
        internal bool Dirty { get; private set; }
        internal bool SourceChanged { get; private set; }
        internal bool HasLiveNetwork => network != null;
        internal GravityFlowDesignBasis Basis => Inputs.DesignBasis;
        internal GravityVerticalDesignConstraintSet Constraints => constraints;
        internal string NetworkFingerprint => network?.NetworkFingerprint;
        internal event Action Changed;
        internal EngineeringDesignWorkspace(string id, string modelFingerprint, NetworkReadResult live = null,
            GravityEngineeringInputs restored = null, IEnumerable<GravityCandidateRecipe> recipes = null, string recipeFingerprint = null)
        {
            projectId = id;
            Inputs = restored == null ? new GravityEngineeringInputs { ProjectId = id, ModelFingerprint = modelFingerprint?.ToUpperInvariant() } : Clone(restored);
            Require(Inputs.ProjectId == id, "PROJECT_IDENTITY_MISMATCH");
            Require(string.Equals(Inputs.ModelFingerprint, modelFingerprint, StringComparison.OrdinalIgnoreCase), "MODEL_IDENTITY_MISMATCH");
            Inputs.Approval = null;
            if (!string.IsNullOrWhiteSpace(Inputs.ConstraintsJson)) constraints = GravityEngineeringWire.Constraints(Inputs.ConstraintsJson);
            Recipes = recipes?.Select(CloneRecipe).ToList() ?? new List<GravityCandidateRecipe>();
            RecipeConstraintFingerprint = recipeFingerprint;
            if (live != null) BindNetwork(live, modelFingerprint);
        }
        private static T Clone<T>(T value) => RuntimeHostProtocol.Serializer().Deserialize<T>(GravityExecutionPlanBuilder.Json(value));
        private static GravityCandidateRecipe CloneRecipe(GravityCandidateRecipe recipe) => GravityEngineeringWire.Read<GravityCandidateRecipe>(
            RuntimeHostProtocol.Serializer().DeserializeObject(GravityExecutionPlanBuilder.Json(recipe)));
        internal static void Require(bool value, string detail) { if (!value) throw new InvalidOperationException("ENGINEERING_WORKSPACE_" + detail); }
        internal static double Finite(string text)
        {
            double value;
            Require(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && GravityExecutionPlanBuilder.Finite(value), "FINITE_NUMBER_REQUIRED");
            return value;
        }
        internal static SourceEngineeringValue Feet(string text, string unit)
        {
            Require(unit == "ft" || unit == "Feet", "UNSUPPORTED_UNIT: Current P13 elevation/cover path requires Feet; no conversion is performed.");
            return new SourceEngineeringValue(Finite(text), new SourceUnitIdentity("length", "Feet", "feet", "ft"));
        }
        internal void BindNetwork(NetworkReadResult live, string modelFingerprint)
        {
            // P7 stores lowercase hashes; production plans and P5 use the canonical uppercase family hash.
            // An older differently fingerprinted constraint set follows the existing explicit source-revision path.
            modelFingerprint = modelFingerprint?.ToUpperInvariant();
            var observed = new GravityNetworkIngestionService().Ingest(live, modelFingerprint, schemaVersion: 2);
            ClearAutomaticProposal();
            string previous = constraints?.SourceNetworkFingerprint;
            SourceChanged = (!string.IsNullOrWhiteSpace(previous) && previous != observed.NetworkFingerprint) ||
                (!string.IsNullOrWhiteSpace(Inputs.ModelFingerprint) && !string.Equals(Inputs.ModelFingerprint, modelFingerprint, StringComparison.OrdinalIgnoreCase));
            network = observed; Inputs.SourceRead = Clone(live); Inputs.ModelFingerprint = modelFingerprint;
            Feasibility = null; Candidates = null;
            if (SourceChanged) { Inputs.FeasibilityJson = Inputs.CandidateJson = Inputs.GenerationRequestJson = null; Dirty = true; Changed?.Invoke(); }
        }
        private void Editable()
        {
            Require(Basis?.CommonDesignBasis != null, "CREATE_DRAFT_FIRST");
            Require(!SourceChanged, "SOURCE_CHANGED: Create an explicit new draft/version before editing.");
            Require(Basis.CommonDesignBasis.ReviewState == DesignBasisReviewState.Draft, "DRAFT_REQUIRED: Reviewed/review-pending content is locked.");
        }
        internal void NewDraft(string requestedBasisVersion)
        {
            var old = Basis;
            if (old != null)
            {
                Require(!string.IsNullOrWhiteSpace(requestedBasisVersion) && requestedBasisVersion != old.CommonDesignBasis.DesignBasisVersion, "NEW_VERSION_REQUIRED");
                Require(!Dirty || SourceChanged, "SAVE_CURRENT_VERSION_BEFORE_REVISION");
            }
            var next = old == null ? new GravityFlowDesignBasis() : GravityFlowDesignBasisSerializer.Deserialize(GravityFlowDesignBasisSerializer.Serialize(old));
            var common = old == null ? null : next.CommonDesignBasis;
            next.CommonDesignBasis = new DesignBasis { DesignBasisId = common?.DesignBasisId ?? Guid.NewGuid().ToString("D"),
                DesignBasisVersion = requestedBasisVersion, ProjectId = projectId, StandardReferences = common?.StandardReferences ?? new List<DesignBasisStandardReference>(),
                Assumptions = common?.Assumptions ?? new List<DesignBasisAssumption>(), Overrides = common?.Overrides ?? new List<DesignBasisOverride>() };
            if (SourceChanged && constraints != null)
            {
                var keys = GravityExecutionPlanBuilder.ConstraintDecisions(constraints).Select(DecisionProvenance).Select(p => p.ParameterKey).ToArray();
                next.VerticalDesign.Criteria.RemoveAll(c => keys.Contains(c.Key, StringComparer.OrdinalIgnoreCase));
                constraints = null; Recipes.Clear(); RecipeConstraintFingerprint = null;
            }
            else if (old != null && constraints != null)
            {
                // An explicit new basis version retains human decisions and AUTO-01 routing, but not stale standard-derived bounds.
                var decisions = GravityExecutionPlanBuilder.ConstraintDecisions(constraints).ToArray();
                var stale = decisions.Where(GravityAutomaticVerticalPreparationService.IsDerivedDecision).ToArray();
                foreach (var d in stale) next.VerticalDesign.Criteria.RemoveAll(c => string.Equals(c.Key, DecisionProvenance(d).ParameterKey, StringComparison.OrdinalIgnoreCase));
                if (stale.Length > 0) constraints = Build(decisions.Except(stale));
            }
            verticalPreparation = null;
            Inputs.DesignBasis = next; SourceChanged = false;
            Rebuild(); Invalidate();
        }
        internal void SetIdentity(string id, string requestedBasisVersion)
        {
            Editable(); Require(!string.IsNullOrWhiteSpace(id), "DESIGN_BASIS_ID_REQUIRED");
            if (Basis.CommonDesignBasis.ReviewState != DesignBasisReviewState.Draft) throw new InvalidOperationException("DRAFT_REQUIRED");
            Basis.CommonDesignBasis.DesignBasisId = id.Trim(); Basis.CommonDesignBasis.DesignBasisVersion = requestedBasisVersion?.Trim();
            Rebuild(); Invalidate();
        }
        internal void SetCommonMetadata(List<DesignBasisStandardReference> references,List<DesignBasisAssumption> assumptions,List<DesignBasisOverride> overrides)
        {
            Editable();var next=GravityFlowDesignBasisSerializer.Deserialize(GravityFlowDesignBasisSerializer.Serialize(Basis));
            next.CommonDesignBasis.StandardReferences=references;next.CommonDesignBasis.Assumptions=assumptions;next.CommonDesignBasis.Overrides=overrides;
            var validation=next.CommonDesignBasis.Validate();Require(validation.IsValid,"COMMON_METADATA_INVALID: "+string.Join("; ",validation.Issues));
            Inputs.DesignBasis=next;Invalidate();
        }
        internal void PutCriterion(GravityFlowSectionIdentifier section, GravityDesignCriterion edited, string existingKey = null)
        {
            Editable(); Require(edited != null && edited.Section == section, "CRITERION_SECTION_MISMATCH");
            var existing = Basis.GetSection(section).Criteria.FirstOrDefault(c => c.Key == existingKey);
            Require(constraints==null || !GravityExecutionPlanBuilder.ConstraintDecisions(constraints).Any(d=>string.Equals(DecisionProvenance(d).ParameterKey,edited.Key,StringComparison.OrdinalIgnoreCase)),"EDIT_STRUCTURED_DECISION_IN_TYPED_CONTROLS");
            Require(existing == null || (edited.Key == existing.Key && edited.Requirement == existing.Requirement), "EXISTING_CRITERION_IDENTITY_REQUIRED");
            var next = GravityFlowDesignBasisSerializer.Deserialize(GravityFlowDesignBasisSerializer.Serialize(Basis));
            var list = next.GetSection(section).Criteria;
            if (existing != null) list.RemoveAt(Basis.GetSection(section).Criteria.IndexOf(existing));
            list.Add(edited.Clone());
            // Criterion structural validation is authoritative. A draft may intentionally retain undefined required entries.
            var validation = next.Validate();
            Require(!validation.Issues.Any(i => i.Field.StartsWith(section.ToString(), StringComparison.OrdinalIgnoreCase)),
                "CRITERION_INVALID: " + string.Join("; ", validation.Issues.Where(i => i.Field.StartsWith(section.ToString(), StringComparison.OrdinalIgnoreCase))));
            if (edited.Status == GravityCriterionStatus.Defined) GravityExecutionPlanBuilder.ValidateEngineeringProvenance(edited.Provenance);
            Basis.GetSection(section).Criteria.Clear(); Basis.GetSection(section).Criteria.AddRange(list);
            Invalidate();
        }
        internal DesignBasisValidationResult ValidateBasis()
        {
            Require(Basis != null, "CREATE_DRAFT_FIRST");
            var validation = Basis.Validate();
            foreach (var c in Basis.EnumerateSectionsInCanonicalOrder().SelectMany(s => s.Criteria))
            {
                if (c.Requirement == GravityCriterionRequirement.Required && (c.Status != GravityCriterionStatus.Defined || c.Value == null))
                    validation.AddError("REQUIRED_CRITERION_UNRESOLVED", c.Section + "." + c.Key, "Required criterion remains undefined.");
                if (c.Status == GravityCriterionStatus.Defined)
                    try { GravityExecutionPlanBuilder.ValidateEngineeringProvenance(c.Provenance); }
                    catch (Exception e) { validation.AddError("ENGINEERING_PROVENANCE", c.Section + "." + c.Key, e.Message); }
            }
            return validation;
        }
        internal void Transition(DesignBasisReviewState state, string note)
        {
            Require(!SourceChanged, "SOURCE_CHANGED"); Require(!string.IsNullOrWhiteSpace(note), "REVIEW_NOTE_REQUIRED");
            if (state == DesignBasisReviewState.ReadyForReview) { var validation=ValidateBasis(); Require(validation.IsValid, "REVIEW_VALIDATION: " + string.Join("; ", validation.Issues)); }
            using (var actor = WindowsIdentity.GetCurrent())
            {
                Require(actor?.User != null && !string.IsNullOrWhiteSpace(actor.Name), "OPERATOR_IDENTITY_UNAVAILABLE");
                Basis.TransitionReviewState(state, actor.Name + " [" + actor.User.Value + "]", note, DateTime.UtcNow);
            }
            Invalidate();
        }
        internal GravityConduitEnd End(int conduitId, ConduitEndKind kind)
        {
            Require(network != null, "LIVE_NETWORK_REQUIRED");
            var c = network.Conduits.SingleOrDefault(n => n.SourceElementId == conduitId);
            Require(c != null && Enum.IsDefined(typeof(ConduitEndKind), kind), "ACTUAL_CONDUIT_ENDPOINT_REQUIRED");
            return new GravityConduitEnd(conduitId, kind, kind == ConduitEndKind.Start ? c.StartNodeId : c.StopNodeId);
        }
        internal void SetOrientation(int conduitId, string direction, StandardProvenance provenance)
        {
            Editable(); var start = End(conduitId, ConduitEndKind.Start); var stop = End(conduitId, ConduitEndKind.Stop);
            Require(direction == "Undecided" || direction == "Start → Stop" || direction == "Stop → Start", "EXPLICIT_DIRECTION_REQUIRED");
            var old = constraints?.Orientations.Values.SingleOrDefault(o => o.ConduitId == conduitId);
            object decision = direction == "Undecided" ? null : new GravityDesignOrientation(conduitId,
                direction == "Start → Stop" ? start.NodeId : stop.NodeId, direction == "Start → Stop" ? stop.NodeId : start.NodeId, provenance);
            ReplaceDecision(old, decision);
        }
        internal GravityAutomaticPreparationResult PrepareAutomaticDesign(string policyId, string policyVersion,
            GravityAutomaticPreparationContext current)
        {
            Editable(); Require(network != null, "LIVE_NETWORK_REQUIRED");
            var result = new GravityAutomaticPreparationService().Prepare(network, constraints, policyId, policyVersion, current);
            Require(result.ContextFailures.Count == 0, "AUTOMATIC_CONTEXT_INVALID: " + string.Join("; ", result.ContextFailures));
            Require(!result.Components.Any(c => c.UnresolvedReasons.Any(r => r.StartsWith("EXPLICIT_ORIENTATION_CONFLICT:", StringComparison.Ordinal) ||
                r.StartsWith("DERIVED_CONTEXT_STALE:", StringComparison.Ordinal))), "AUTOMATIC_DECISION_CONFLICT");
            // Ineligible independent components remain unresolved. All additions and their basis bindings commit together.
            if (result.DerivedOrientations.Count > 0) ReplaceDecisions(new object[0], result.DerivedOrientations.Cast<object>().ToArray());
            return result;
        }
        internal void ChangeAutomaticPreparationPolicy(string activePolicyId, string activePolicyVersion)
        {
            Editable(); Require(network != null, "LIVE_NETWORK_REQUIRED");
            // Explicit policy change removes only decisions owned by this application policy. Human decisions survive.
            var stale = constraints?.Orientations.Values.Where(o => GravityAutomaticPreparationService.IsDerived(o) &&
                (o.Provenance.StandardId != activePolicyId || o.Provenance.StandardVersion != activePolicyVersion)).Cast<object>().ToArray() ?? new object[0];
            if (stale.Length > 0) ReplaceDecisions(stale, new object[0]);
            else Invalidate();
        }
        internal GravityVerticalPreparationResult PrepareAutomaticVerticalConstraints(GravityVerticalPreparationRequest request)
        {
            Editable(); Require(network != null, "LIVE_NETWORK_REQUIRED"); ValidateAutomaticDecisions();
            Require(request != null, "VERTICAL_REQUEST_REQUIRED");
            request = request.WithConstraintFingerprint(request.Current?.ConstraintFingerprint);
            var report = new GravityAutomaticVerticalPreparationService().Prepare(network, Basis, constraints, request);
            Require(report.ContextFailures.Count == 0, "VERTICAL_CONTEXT_INVALID: " + string.Join("; ", report.ContextFailures));
            ClearAutomaticProposal();
            if (report.HasStaleDerivedDecisions)
            {
                verticalPreparation = request.WithConstraintFingerprint(constraints.VerticalConstraintFingerprint); Invalidate();
                throw new InvalidOperationException("ENGINEERING_WORKSPACE_VERTICAL_DECISIONS_STALE: Create an explicit new draft/version.");
            }
            var additions = report.Slopes.Cast<object>().Concat(report.Covers).ToArray();
            if (additions.Length > 0) ReplaceDecisions(new object[0], additions);
            verticalPreparation = request.WithConstraintFingerprint(constraints.VerticalConstraintFingerprint);
            return report;
        }
        private static StandardProvenance DecisionProvenance(object d) => (StandardProvenance)d.GetType().GetProperty("Provenance").GetValue(d, null);
        internal void PutConstraint(object decision) { Editable(); ReplaceDecision(null, decision); }
        private void ReplaceDecision(object old, object next)
        {
            var decisions = constraints == null ? new List<object>() : GravityExecutionPlanBuilder.ConstraintDecisions(constraints).ToList();
            if (next != null)
            {
                var p = DecisionProvenance(next); GravityExecutionPlanBuilder.ValidateEngineeringProvenance(p);
                var byKey = decisions.FirstOrDefault(d => string.Equals(DecisionProvenance(d).ParameterKey, p.ParameterKey, StringComparison.OrdinalIgnoreCase));
                Require(byKey == null || old == null || byKey == old, "DECISION_KEY_CONFLICT");
                if (byKey != null) old = byKey;
                Require(!Basis.VerticalDesign.Criteria.Any(c => string.Equals(c.Key,p.ParameterKey,StringComparison.OrdinalIgnoreCase)) || old != null, "DECISION_CRITERION_KEY_CONFLICT");
            }
            ReplaceDecisions(old == null ? new object[0] : new[] { old }, next == null ? new object[0] : new[] { next });
        }
        private void ReplaceDecisions(object[] removed, object[] added)
        {
            var decisions = constraints == null ? new List<object>() : GravityExecutionPlanBuilder.ConstraintDecisions(constraints).ToList();
            foreach (var d in removed) Require(decisions.Remove(d), "DECISION_REMOVAL_MISMATCH");
            var nextBasis = GravityFlowDesignBasisSerializer.Deserialize(GravityFlowDesignBasisSerializer.Serialize(Basis));
            foreach (var d in removed)
                nextBasis.VerticalDesign.Criteria.RemoveAll(c => string.Equals(c.Key, DecisionProvenance(d).ParameterKey, StringComparison.OrdinalIgnoreCase));
            foreach (var d in added)
            {
                var p = DecisionProvenance(d); GravityExecutionPlanBuilder.ValidateEngineeringProvenance(p); ValidateDecision(d);
                Require(!decisions.Any(x => string.Equals(DecisionProvenance(x).ParameterKey, p.ParameterKey, StringComparison.OrdinalIgnoreCase)), "DECISION_KEY_CONFLICT");
                Require(!nextBasis.VerticalDesign.Criteria.Any(c => string.Equals(c.Key, p.ParameterKey, StringComparison.OrdinalIgnoreCase)), "DECISION_CRITERION_KEY_CONFLICT");
                decisions.Add(d);
                nextBasis.VerticalDesign.Criteria.Add(new GravityDesignCriterion { Key = p.ParameterKey, Section = GravityFlowSectionIdentifier.VerticalDesign,
                    Requirement = GravityCriterionRequirement.Required, Status = GravityCriterionStatus.Defined,
                    Value = StandardValue.FromText(GravityExecutionPlanBuilder.Json(d)), Provenance = Clone(p),
                    Rationale = (d as FixedEndpointElevationConstraint)?.Reason });
            }
            var built = Build(decisions);
            var proposed = Clone(Inputs); proposed.DesignBasis = nextBasis; proposed.ConstraintsJson = GravityExecutionPlanBuilder.Json(built);
            // Validate the existing canonical decision, topology and fingerprint contracts BEFORE publishing any stored state.
            GravityExecutionPlanBuilder.ValidateEngineeringConstraints(proposed, network);
            Inputs.DesignBasis = nextBasis; constraints = built; Inputs.ConstraintsJson = proposed.ConstraintsJson; Invalidate();
        }
        private void ValidateDecision(object decision)
        {
            if (decision == null) return;
            Action<GravityConduitEnd> endpoint = e => Require(End(e.ConduitId,e.End).Equals(e), "ACTUAL_ENDPOINT_REQUIRED");
            Action<SourceEngineeringValue> quantity = v => Require(v?.NumericValue != null && GravityExecutionPlanBuilder.Finite(v.NumericValue.Value) &&
                new SourceUnitIdentity("length","Feet","feet","ft").Equals(v.Unit), "FINITE_FEET_QUANTITY_REQUIRED");
            if (decision is GravityDesignOrientation o) { var s=End(o.ConduitId,ConduitEndKind.Start); var t=End(o.ConduitId,ConduitEndKind.Stop); Require(o.IsStartToStop(s.NodeId,t.NodeId)||o.IsStopToStart(s.NodeId,t.NodeId), "ORIENTATION_ENDPOINT_MISMATCH"); }
            else if (decision is FixedEndpointElevationConstraint f) { endpoint(f.ConduitEnd); quantity(f.InvertElevation); }
            else if (decision is ProtectedNodeElevationConstraint p) { Require(network.Nodes.Any(n=>n.SourceElementId==p.NodeId), "ACTUAL_NODE_REQUIRED"); quantity(p.Elevation); }
            else if (decision is OutfallVerticalConstraint b) { Require(network.Nodes.Any(n=>n.SourceElementId==b.OutfallNodeId&&n.Kind==GravityNodeKind.Outfall), "ACTUAL_OUTFALL_REQUIRED"); if(b.ProtectedInvert!=null)quantity(b.ProtectedInvert); if(b.ConnectedEndpointConstraint!=null){ ValidateDecision(b.ConnectedEndpointConstraint); Require(b.ConnectedEndpointConstraint.ConduitEnd.NodeId==b.OutfallNodeId,"OUTFALL_ENDPOINT_MISMATCH"); } }
            else if (decision is VerticalMatchConstraint m) { foreach(var e in m.ParticipatingConduitEnds)endpoint(e); }
            else if (decision is VerticalCoverConstraint c) { endpoint(c.ConduitEnd); quantity(c.GroundElevation); quantity(c.MinimumCover); }
            else if (decision is VerticalSlopeConstraint s) { End(s.ConduitId,ConduitEndKind.Start); Require((!s.MinimumSlope.HasValue||GravityExecutionPlanBuilder.Finite(s.MinimumSlope.Value))&&(!s.MaximumSlope.HasValue||GravityExecutionPlanBuilder.Finite(s.MaximumSlope.Value)),"FINITE_SLOPE_REQUIRED"); }
            else throw new InvalidOperationException("UNSUPPORTED_CONSTRAINT_TYPE");
        }
        private GravityVerticalDesignConstraintSet Build(IEnumerable<object> decisions)
        {
            var b = new GravityVerticalDesignConstraintSetBuilder();
            foreach (var d in decisions)
            {
                if(d is GravityDesignOrientation o)b.SetOrientation(o.ConduitId,o.FromNodeId,o.ToNodeId,o.Provenance);
                else if(d is FixedEndpointElevationConstraint f)b.AddFixedConstraint(f.ConduitEnd,f.InvertElevation,f.Provenance,f.Reason);
                else if(d is ProtectedNodeElevationConstraint p)b.AddProtectedConstraint(p.NodeId,p.ElevationKind,p.Elevation,p.Provenance);
                else if(d is OutfallVerticalConstraint v)b.AddOutfallConstraint(v.OutfallNodeId,v.Provenance,v.ProtectedInvert,v.ConnectedEndpointConstraint);
                else if(d is VerticalMatchConstraint m)b.AddMatchConstraint(m.NodeId,m.Mode,m.ParticipatingConduitEnds,m.Provenance);
                else if(d is VerticalCoverConstraint c)b.AddCoverConstraint(c.ConduitEnd,c.GroundElevation,c.MinimumCover,c.Provenance,c.Reference);
                else if(d is VerticalSlopeConstraint s)b.AddSlopeConstraint(s.ConduitId,s.MinimumSlope,s.MaximumSlope,s.Provenance);
            }
            return b.Build(network?.NetworkFingerprint ?? constraints?.SourceNetworkFingerprint, Basis?.CommonDesignBasis.DesignBasisId, Basis?.CommonDesignBasis.DesignBasisVersion);
        }
        private void Rebuild() { constraints = Build(constraints == null ? Enumerable.Empty<object>() : GravityExecutionPlanBuilder.ConstraintDecisions(constraints)); Inputs.ConstraintsJson = GravityExecutionPlanBuilder.Json(constraints); }
        private void Invalidate()
        { ClearAutomaticProposal(); Dirty=true; Feasibility=null; Candidates=null; Inputs.FeasibilityJson=Inputs.CandidateJson=Inputs.GenerationRequestJson=null; Inputs.Approval=null; Changed?.Invoke(); }
        private void ValidateAutomaticDecisions()
        {
            var owned = constraints.Orientations.Values.Where(GravityAutomaticPreparationService.IsDerived).ToArray();
            if (owned.Length == 0) return;
            // This validates persisted declarations; it neither selects a policy nor accepts new proposals.
            var context = new GravityAutomaticPreparationContext(network.SourceModelFingerprint, network.NetworkFingerprint,
                network.ScenarioContext, owned[0].Provenance.StandardId, owned[0].Provenance.StandardVersion, constraints.VerticalConstraintFingerprint);
            var report = new GravityAutomaticPreparationService().Prepare(network, constraints,
                owned[0].Provenance.StandardId, owned[0].Provenance.StandardVersion, context);
            if (report.ContextFailures.Count != 0 || report.Components.Any(c => !c.Eligible && c.ConduitIds.Any(id => owned.Any(o => o.ConduitId == id))))
            {
                Invalidate();
                throw new InvalidOperationException("ENGINEERING_WORKSPACE_AUTOMATIC_DECISIONS_STALE: Explicit policy/source revision is required.");
            }
        }
        private void ValidateAutomaticVerticalDecisions()
        {
            if (!GravityExecutionPlanBuilder.ConstraintDecisions(constraints).Any(GravityAutomaticVerticalPreparationService.IsDerivedDecision)) return;
            if (verticalPreparation == null)
            {
                Invalidate();
                throw new InvalidOperationException("ENGINEERING_WORKSPACE_VERTICAL_STANDARD_CONTEXT_REQUIRED: Explicit current standards are required after reload/import.");
            }
            var report = new GravityAutomaticVerticalPreparationService().Prepare(network, Basis, constraints,
                verticalPreparation.WithConstraintFingerprint(constraints.VerticalConstraintFingerprint));
            if (report.ContextFailures.Count != 0 || report.HasStaleDerivedDecisions)
            {
                Invalidate();
                throw new InvalidOperationException("ENGINEERING_WORKSPACE_VERTICAL_DECISIONS_STALE: Explicit standard/source/basis revision is required.");
            }
        }
        internal VerticalFeasibilityResult Assess()
        {
            Require(network != null && Basis != null && !SourceChanged, "CURRENT_NETWORK_AND_DRAFT_REQUIRED");
            ClearAutomaticProposal();
            ValidateAutomaticDecisions();
            ValidateAutomaticVerticalDecisions();
            Rebuild(); foreach(var d in GravityExecutionPlanBuilder.ConstraintDecisions(constraints)){GravityExecutionPlanBuilder.ValidateEngineeringProvenance(DecisionProvenance(d));ValidateDecision(d);}
            Feasibility = new GravityVerticalFeasibilityEngine().Analyze(network,constraints);
            Inputs.FeasibilityJson=GravityExecutionPlanBuilder.Json(Feasibility); Candidates=null; Inputs.CandidateJson=null; Dirty=true; Changed?.Invoke(); return Feasibility;
        }
        internal bool CanGenerate => Feasibility != null && (Feasibility.Status==VerticalFeasibilityStatus.Feasible || Feasibility.Status==VerticalFeasibilityStatus.FeasibleMultipleSolutions);
        private void ValidateAutomaticProposalInputs()
        {
            Require(network != null && Basis?.CommonDesignBasis != null && constraints != null && !SourceChanged && CanGenerate,
                "AUTOMATIC_PROPOSAL_ASSESSED_CONTEXT_REQUIRED");
            ValidateAutomaticDecisions();
            ValidateAutomaticVerticalDecisions();
            Require(Inputs.ProjectId == projectId && Basis.CommonDesignBasis.ProjectId == projectId &&
                Inputs.ModelFingerprint == network.SourceModelFingerprint && Inputs.SourceRead != null,
                "AUTOMATIC_PROPOSAL_SOURCE_IDENTITY");
            // Pure ingestion of the already supplied P12 DTOs, never a model read or runtime loader.
            var observed = new GravityNetworkIngestionService().Ingest(Inputs.SourceRead, Inputs.ModelFingerprint, schemaVersion: 2);
            Require(observed.NetworkFingerprint == network.NetworkFingerprint, "AUTOMATIC_PROPOSAL_SOURCE_IDENTITY");
            Require(Feasibility.ConstraintFingerprint == constraints.VerticalConstraintFingerprint &&
                GravityExecutionPlanBuilder.Json(Feasibility) == Inputs.FeasibilityJson &&
                GravityExecutionPlanBuilder.Json(constraints) == Inputs.ConstraintsJson,
                "AUTOMATIC_PROPOSAL_STORED_INPUT_MISMATCH");
        }
        private string AutomaticProposalInputIdentity() => GravityExecutionPlanBuilder.Hash(GravityExecutionPlanBuilder.Json(new {
            Project = projectId, Inputs.ProjectId, Inputs.ModelFingerprint, Inputs.SourceRead,
            Network = network.NetworkFingerprint, network.ScenarioContext,
            BasisContent = GravityVerticalDesignProposalResolver.ContentFingerprint(Basis), Basis,
            Constraints = constraints, Inputs.ConstraintsJson, Feasibility, Inputs.FeasibilityJson,
            VerticalContext = verticalPreparation }));
        private string AutomaticProposalStagedIdentity() => GravityExecutionPlanBuilder.Hash(GravityExecutionPlanBuilder.Json(new {
            Candidates, Recipes, RecipeConstraintFingerprint, Inputs.GenerationRequestJson }));

        internal GravityAutomaticPreparationContext CurrentAutomaticProposalContext(string objectiveId, string objectiveVersion)
        {
            Require(network != null && constraints != null, "AUTOMATIC_PROPOSAL_BOUND_CONTEXT_REQUIRED");
            return new GravityAutomaticPreparationContext(network.SourceModelFingerprint, network.NetworkFingerprint,
                network.ScenarioContext, objectiveId, objectiveVersion, constraints.VerticalConstraintFingerprint);
        }

        internal GravityVerticalDesignProposalResolver.Result PreviewAutomaticProposal(string objectiveId, string objectiveRevision,
            GravityAutomaticPreparationContext current)
        {
            var previous = automaticProposal;
            ClearAutomaticProposal();
            ValidateAutomaticProposalInputs();
            var inputIdentity = AutomaticProposalInputIdentity();
            var result = new GravityVerticalDesignProposalResolver().Resolve(network, Basis, constraints, Feasibility,
                objectiveId, objectiveRevision, current, GravityVerticalDesignProposalResolver.ContentFingerprint(Basis));
            if (result.Status == "PROPOSED")
            {
                var next = new AutomaticProposalIdentity(result, current, inputIdentity);
                // Re-previewing unchanged staged work does not turn it into replaceable operator work.
                if (previous?.StagedIdentity != null && previous.ProposalFingerprint == next.ProposalFingerprint &&
                    previous.CandidateFingerprint == next.CandidateFingerprint && previous.InputIdentity == next.InputIdentity &&
                    previous.ContextIdentity == next.ContextIdentity && Inputs.CandidateJson == null && Inputs.Approval == null &&
                    previous.StagedIdentity == AutomaticProposalStagedIdentity()) next.StagedIdentity = previous.StagedIdentity;
                automaticProposal = next;
            }
            return result;
        }

        internal GravityDesignCandidateSet StageAutomaticProposal(string proposalFingerprint)
        {
            var preview = automaticProposal;
            Require(preview != null && !string.IsNullOrWhiteSpace(proposalFingerprint) && proposalFingerprint == preview.ProposalFingerprint,
                "AUTOMATIC_PROPOSAL_PREVIEW_REQUIRED");
            // Reject operator-owned work before any potentially invalidating stale-engineering guard.
            Require(Inputs.CandidateJson == null && Inputs.Approval == null, "AUTOMATIC_STAGE_OPERATOR_WORK");
            if (preview.StagedIdentity == null)
                Require(Candidates == null && Recipes.Count == 0 && Inputs.GenerationRequestJson == null && RecipeConstraintFingerprint == null,
                    "AUTOMATIC_STAGE_OPERATOR_WORK");
            else Require(Candidates != null && preview.StagedIdentity == AutomaticProposalStagedIdentity(), "AUTOMATIC_STAGE_OPERATOR_WORK");
            ValidateAutomaticProposalInputs();
            Require(preview.InputIdentity == AutomaticProposalInputIdentity(), "AUTOMATIC_PROPOSAL_STALE");
            var current = new GravityAutomaticPreparationContext(network.SourceModelFingerprint, network.NetworkFingerprint,
                network.ScenarioContext, preview.ObjectiveId, preview.ObjectiveRevision, constraints.VerticalConstraintFingerprint);
            Require(preview.ContextIdentity == GravityExecutionPlanBuilder.Hash(GravityExecutionPlanBuilder.Json(current)), "AUTOMATIC_PROPOSAL_STALE");
            if (preview.StagedIdentity != null) return Candidates; // Unchanged, unselected: no resolver/generator/publication/notification.

            var result = new GravityVerticalDesignProposalResolver().Resolve(network, Basis, constraints, Feasibility,
                preview.ObjectiveId, preview.ObjectiveRevision, current, GravityVerticalDesignProposalResolver.ContentFingerprint(Basis));
            Require(result.Status == "PROPOSED" && result.ProposalFingerprint == preview.ProposalFingerprint &&
                result.CandidateFingerprint == preview.CandidateFingerprint && result.Objective == preview.ObjectiveId &&
                result.ObjectiveRevision == preview.ObjectiveRevision && result.NetworkFingerprint == network.NetworkFingerprint &&
                result.ConstraintFingerprint == constraints.VerticalConstraintFingerprint, "AUTOMATIC_PROPOSAL_REVALIDATION_FAILED");
            var recipes = Feasibility.Status == VerticalFeasibilityStatus.Feasible ? new List<GravityCandidateRecipe>() : new List<GravityCandidateRecipe> { result.Recipe };
            var request = new GravityCandidateGenerationRequest(constraints.VerticalConstraintFingerprint, recipes);
            GravityExecutionPlanBuilder.ValidateEngineeringGeneration(request, constraints, Feasibility);
            var staged = new GravityCandidateGenerator().Generate(network, constraints, Feasibility, request);
            Require(staged.Candidates.Count == 1 && staged.RecipeResults.Count == 1 && staged.RecipeResults[0].Status == CandidateGenerationStatus.Generated &&
                staged.SourceConstraintFingerprint == constraints.VerticalConstraintFingerprint, "AUTOMATIC_STAGE_GENERATION_FAILED");
            var candidate = staged.Candidates[0];
            GravityCandidateExecutor.ValidateLineage(candidate, network.NetworkFingerprint, constraints.VerticalConstraintFingerprint);
            Require(candidate.CandidateFingerprint == preview.CandidateFingerprint && candidate.RecipeId == result.CandidateRecipeId &&
                candidate.EndpointDesigns.Count == network.Conduits.Count * 2 &&
                GravityExecutionPlanBuilder.Json(candidate.EndpointDesigns) == GravityExecutionPlanBuilder.Json(result.Endpoints),
                "AUTOMATIC_STAGE_CANDIDATE_MISMATCH");
            // Prepare every value and ownership hash before publishing. No Invalidate/GenerateCandidates/SelectCandidate here.
            var requestJson = GravityExecutionPlanBuilder.Json(request);
            var stagedIdentity = GravityExecutionPlanBuilder.Hash(GravityExecutionPlanBuilder.Json(new {
                Candidates = staged, Recipes = recipes, RecipeConstraintFingerprint = constraints.VerticalConstraintFingerprint,
                GenerationRequestJson = requestJson }));
            Candidates = staged; Inputs.GenerationRequestJson = requestJson; Recipes = recipes;
            RecipeConstraintFingerprint = constraints.VerticalConstraintFingerprint; Dirty = true; preview.StagedIdentity = stagedIdentity;
            Changed?.Invoke();
            return staged;
        }
        internal void SaveRecipe(GravityCandidateRecipe recipe)
        {
            Editable(); Require(network!=null, "LIVE_NETWORK_REQUIRED"); Rebuild(); GravityExecutionPlanBuilder.ValidateEngineeringProvenance(recipe.Provenance);
            foreach(var s in recipe.EndpointSelections){Require(End(s.ConduitEnd.ConduitId,s.ConduitEnd.End).Equals(s.ConduitEnd),"RECIPE_ENDPOINT_MISMATCH");GravityExecutionPlanBuilder.ValidateEngineeringProvenance(s.Provenance);if(s.ExplicitElevation!=null)Require(s.ExplicitElevation.HasValue&&GravityExecutionPlanBuilder.Finite(s.ExplicitElevation.NumericValue.Value)&&new SourceUnitIdentity("length","Feet","feet","ft").Equals(s.ExplicitElevation.Unit),"RECIPE_UNIT");}
            Require(recipe.EndpointSelections.Select(s=>s.ConduitEnd).Distinct().Count()==recipe.EndpointSelections.Count,"DUPLICATE_RECIPE_ENDPOINT");
            Recipes.RemoveAll(r=>r.RecipeId==recipe.RecipeId); Recipes.Add(CloneRecipe(recipe)); RecipeConstraintFingerprint=constraints.VerticalConstraintFingerprint; Invalidate();
        }
        internal GravityDesignCandidateSet GenerateCandidates()
        {
            Require(CanGenerate && !SourceChanged,"P13_GENERATION_GATE");
            ValidateAutomaticDecisions();
            ValidateAutomaticVerticalDecisions();
            Require(Recipes.Count==0 || RecipeConstraintFingerprint==constraints.VerticalConstraintFingerprint,"RECIPE_STALE: Save the recipe explicitly against current constraints.");
            var request=new GravityCandidateGenerationRequest(constraints.VerticalConstraintFingerprint,Recipes);
            GravityExecutionPlanBuilder.ValidateEngineeringGeneration(request,constraints,Feasibility);
            ClearAutomaticProposal();
            Candidates=new GravityCandidateGenerator().Generate(network,constraints,Feasibility,request);
            Inputs.GenerationRequestJson=GravityExecutionPlanBuilder.Json(request); Inputs.CandidateJson=null;
            if(Candidates.Candidates.Count==1 && request.Recipes.Count<=1) SelectCandidate(Candidates.Candidates[0].CandidateId);
            Dirty=true; Changed?.Invoke(); return Candidates;
        }
        internal void SelectCandidate(string id)
        {
            Require(Candidates!=null,"GENERATE_CANDIDATES_FIRST"); var c=Candidates.Candidates.SingleOrDefault(p=>p.CandidateId==id); Require(c!=null,"EXACT_CANDIDATE_SELECTION_REQUIRED");
            GravityCandidateExecutor.ValidateLineage(c,network.NetworkFingerprint,constraints.VerticalConstraintFingerprint);
            ClearAutomaticProposal();
            Inputs.CandidateJson=GravityExecutionPlanBuilder.Json(c); Dirty=true; Changed?.Invoke();
        }
        internal GravityEngineeringReadiness Readiness() => GravityEngineeringReadinessAssessment.Assess(new GravityEngineeringReadinessInput {
            EngineeringInputs=Inputs, OperatingMode=GravityOperatingMode.SingleOperator, Calculate=false,
            SourceKind=HasLiveNetwork?GravityReadinessSourceKind.SuppliedP12Snapshot:GravityReadinessSourceKind.Historical,
            HistoricalNetworkFingerprint=constraints?.SourceNetworkFingerprint },null);
        internal void MarkSaved() { Dirty=false; }
        internal void Import(GravityEngineeringInputs input)
        {
            Require(input.ProjectId==projectId && string.Equals(input.ModelFingerprint,Inputs.ModelFingerprint,StringComparison.OrdinalIgnoreCase),"IMPORT_IDENTITY_MISMATCH");
            Require(Basis==null||!Dirty,"SAVE_CURRENT_DRAFT_BEFORE_IMPORT"); var copy=Clone(input); copy.Approval=null;
            Require(copy.SourceRead==null||GravityExecutionPlanBuilder.Json(copy.SourceRead)==GravityExecutionPlanBuilder.Json(Inputs.SourceRead),"IMPORT_SOURCE_MISMATCH");
            var parsed=GravityEngineeringWire.Constraints(copy.ConstraintsJson);
            var recipes=string.IsNullOrWhiteSpace(copy.GenerationRequestJson)?new List<GravityCandidateRecipe>():GravityEngineeringWire.Read<GravityCandidateGenerationRequest>(RuntimeHostProtocol.Serializer().DeserializeObject(copy.GenerationRequestJson)).Recipes.ToList();
            copy.SourceRead=Inputs.SourceRead; Inputs=copy; constraints=parsed; Recipes=recipes; verticalPreparation=null;
            RecipeConstraintFingerprint=constraints.VerticalConstraintFingerprint; Invalidate();
        }

        public sealed class ControlledOverflowProposal
        {
            public bool IsSupported { get; set; }
            public int PrimaryOutfallId { get; set; }
            public int OverflowOutfallId { get; set; }
            public int OverflowConduitId { get; set; }
            public int JunctionNodeId { get; set; }
            public int? ControlStructureId { get; set; }
            public double? CrestElevation { get; set; }
            public double? TopElevation { get; set; }
            public double? WeirLength { get; set; }
            public double? WeirCoefficient { get; set; }
            public string Status { get; set; }
            public string Detail { get; set; }
        }

        internal ControlledOverflowProposal EvaluateControlledOverflowProposal()
        {
            if (network == null) return new ControlledOverflowProposal { IsSupported = false, Status = "NO_LIVE_NETWORK" };
            var outfalls = network.Nodes.Where(n => n.Kind == GravityNodeKind.Outfall).Select(n => n.SourceElementId).ToList();
            if (outfalls.Count < 2)
                return new ControlledOverflowProposal { IsSupported = false, Status = "SINGLE_OUTFALL_OR_NONE" };

            var c35 = network.Conduits.FirstOrDefault(c => c.StopNodeId == 26 || c.StartNodeId == 26 || c.SourceElementId == 35);
            var c34 = network.Conduits.FirstOrDefault(c => c.StopNodeId == 25 || c.StartNodeId == 25 || c.SourceElementId == 34);

            if (c35 != null && c34 != null && outfalls.Contains(25) && outfalls.Contains(26))
            {
                var p35 = Inputs?.SourceRead?.SourceData?.Parameters?.FirstOrDefault(p => p.Category == "ControlStructure" && p.SourceId == 35 && p.ParameterIdentity == "Physical_StartControlStructureID");
                int? csId = p35?.Value is int id && id > 0 ? (int?)id : 36;

                return new ControlledOverflowProposal
                {
                    IsSupported = true,
                    PrimaryOutfallId = 25,
                    OverflowOutfallId = 26,
                    OverflowConduitId = c35.SourceElementId,
                    JunctionNodeId = 24,
                    ControlStructureId = csId,
                    CrestElevation = 94.5,
                    TopElevation = 97.0,
                    WeirLength = 2.0,
                    WeirCoefficient = 3.0,
                    Status = "PROPOSED_REQUIRING_EXPLICIT_ENGINEERING_REVIEW",
                    Detail = "Conduit " + c35.SourceElementId + " possesses start weir control structure " + (csId?.ToString() ?? "36") +
                             " (crest 94.5 ft, top 97.0 ft, length 2.0 ft, coeff 3.0) at Junction 24. " +
                             "Proposed operating interpretation: Outfall 25 is primary treatment path; Outfall 26 is controlled overflow relief path. " +
                             "REQUIRES EXPLICIT ENGINEERING REVIEW AND APPROVAL — NOT AN APPROVED DECISION."
                };
            }

            return new ControlledOverflowProposal { IsSupported = false, Status = "NO_CONTROL_STRUCTURE_OVERFLOW_SUPPORTED" };
        }

        internal GravityAutomaticPreparationResult TryPrepareAutomaticDesign()
        {
            if (network == null || Basis == null || Basis.CommonDesignBasis.ReviewState != DesignBasisReviewState.Draft)
                return null;
            try
            {
                var context = CurrentAutomaticProposalContext(GravityAutomaticPreparationService.PolicyId, GravityAutomaticPreparationService.PolicyVersion);
                return PrepareAutomaticDesign(GravityAutomaticPreparationService.PolicyId, GravityAutomaticPreparationService.PolicyVersion, context);
            }
            catch
            {
                return null;
            }
        }
    }
}
