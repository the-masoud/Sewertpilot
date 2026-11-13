using System;
using System.Linq;
using System.Reflection;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        // Reuse accepted AUTO-03 fixtures. No new numerical fixtures, review, approval, GUI or model reader.
        private static EngineeringDesignWorkspace W14Draft(bool exact = false)
        {
            var d = P3Draft();
            if (exact) { P3Fixed(d, 201, ConduitEndKind.Start, 101, 95); P3Fixed(d, 201, ConduitEndKind.Stop, 102, 94);
                P3Fixed(d, 202, ConduitEndKind.Start, 102, 94); P3Fixed(d, 202, ConduitEndKind.Stop, 103, 90); }
            d.Assess(); d.MarkSaved(); return d;
        }
        private static GravityVerticalDesignProposalResolver.Result W14Preview(EngineeringDesignWorkspace d, string revision = "1",
            GravityAutomaticPreparationContext context = null) => d.PreviewAutomaticProposal(GravityVerticalDesignProposalResolver.ObjectiveId, revision,
                context ?? AutoContext(AutoLive(d), d.Constraints, GravityVerticalDesignProposalResolver.ObjectiveId, revision));
        private static string W14State(EngineeringDesignWorkspace d) => GravityExecutionPlanBuilder.Json(new {
            d.Inputs, d.Recipes, d.RecipeConstraintFingerprint, d.Feasibility, d.Candidates, d.Dirty, d.SourceChanged });
        private static void W14RejectUnchanged(EngineeringDesignWorkspace d, Action operation, string reason)
        {
            var before = W14State(d); int notifications = 0; Action observed = () => notifications++; d.Changed += observed;
            try { Ui02Reject(operation, reason); }
            finally { d.Changed -= observed; }
            Check(before == W14State(d) && notifications == 0, "Rejected stage publishes nothing and preserves operator work: " + reason);
        }
        private static GravityCandidateGenerationRequest W14Request(EngineeringDesignWorkspace d) => GravityEngineeringWire.Read<GravityCandidateGenerationRequest>(
            RuntimeHostProtocol.Serializer().DeserializeObject(d.Inputs.GenerationRequestJson));
        private static void W14Invalidated(EngineeringDesignWorkspace d, Action change)
        {
            var p = W14Preview(d); Check(p.Status == "PROPOSED", "Valid preview before explicit lifecycle change.");
            change(); W14RejectUnchanged(d, () => d.StageAutomaticProposal(p.ProposalFingerprint), "AUTOMATIC_PROPOSAL_PREVIEW_REQUIRED");
        }

        // 1. Native exact case must stage zero recipes despite AUTO-03 exposing a proposal recipe.
        private static void P14Auto01_ExactPreviewAndStaging()
        {
            var d = W14Draft(true); var feasibility = d.Feasibility; var stored = d.Inputs.FeasibilityJson;
            var basis = GravityExecutionPlanBuilder.Json(d.Basis); var constraints = d.Inputs.ConstraintsJson;
            var p = W14Preview(d); int notifications = 0;
            d.Changed += () => { notifications++; Check(d.Candidates != null && d.Inputs.GenerationRequestJson != null && d.Recipes.Count == 0 &&
                d.Inputs.CandidateJson == null && d.Inputs.Approval == null && d.Dirty && ReferenceEquals(d.Feasibility, feasibility), "Changed observes the complete unselected exact publication."); };
            var staged = d.StageAutomaticProposal(p.ProposalFingerprint); var request = W14Request(d);
            Check(p.Status == "PROPOSED" && p.Recipe != null && feasibility.Status == VerticalFeasibilityStatus.Feasible, "Accepted exact proposal preview.");
            Check(staged.Candidates.Count == 1 && staged.RecipeResults.Single().Status == CandidateGenerationStatus.Generated &&
                staged.Candidates.Single().RecipeId == "EXACT_CONSTRAINT_PROFILE", "Native P14 exact profile staged once.");
            Check(request.Recipes.Count == 0 && d.Recipes.Count == 0 && d.RecipeConstraintFingerprint == d.Constraints.VerticalConstraintFingerprint, "Exact staging persists native zero-recipe request.");
            Check(staged.Candidates.Single().CandidateFingerprint == p.CandidateFingerprint &&
                GravityExecutionPlanBuilder.Json(staged.Candidates.Single().EndpointDesigns) == GravityExecutionPlanBuilder.Json(p.Endpoints), "Exact staged fingerprint and complete endpoint values match preview.");
            Check(d.Inputs.CandidateJson == null && d.Inputs.Approval == null && notifications == 1, "Stage emits one change and never selects/approves.");
            Check(ReferenceEquals(d.Feasibility, feasibility) && stored == d.Inputs.FeasibilityJson && basis == GravityExecutionPlanBuilder.Json(d.Basis) && constraints == d.Inputs.ConstraintsJson,
                "Valid assessed feasibility, basis and constraints survive publication unchanged.");
            GravityExecutionPlanBuilder.ValidateEngineeringGeneration(request, d.Constraints, d.Feasibility);
            GravityCandidateExecutor.ValidateLineage(staged.Candidates.Single(), d.NetworkFingerprint, d.Constraints.VerticalConstraintFingerprint);
        }

        // 2. Multiple-solution staging carries exactly the validated recipe and objective provenance.
        private static void P14Auto01_MultiplePreviewAndStaging()
        {
            var d = W14Draft(); var p = W14Preview(d); var feasibility = d.Feasibility; int notifications = 0; d.Changed += () => notifications++;
            var staged = d.StageAutomaticProposal(p.ProposalFingerprint); var request = W14Request(d); var candidate = staged.Candidates.Single();
            Check(p.Status == "PROPOSED" && feasibility.Status == VerticalFeasibilityStatus.FeasibleMultipleSolutions, "Multiple profile preview accepted through existing resolver.");
            Check(request.Recipes.Count == 1 && d.Recipes.Count == 1 && request.Recipes.Single().RecipeId == p.Recipe.RecipeId &&
                GravityExecutionPlanBuilder.Json(d.Recipes.Single()) == GravityExecutionPlanBuilder.Json(p.Recipe), "Only validated objective recipe staged.");
            Check(request.Recipes.Single().Provenance.StandardId == p.Objective && request.Recipes.Single().Provenance.StandardVersion == p.ObjectiveRevision &&
                request.Recipes.Single().Provenance.Notes.Contains("NON_AUTHORITATIVE_PROPOSAL"), "Objective provenance retained without approval.");
            Check(candidate.CandidateFingerprint == p.CandidateFingerprint && candidate.SourceNetworkFingerprint == d.NetworkFingerprint &&
                candidate.VerticalConstraintFingerprint == d.Constraints.VerticalConstraintFingerprint &&
                GravityExecutionPlanBuilder.Json(candidate.EndpointDesigns) == GravityExecutionPlanBuilder.Json(p.Endpoints), "Full staged values and native network/constraint lineage match preview.");
            Check(ReferenceEquals(feasibility, d.Feasibility) && d.Inputs.CandidateJson == null && d.Inputs.Approval == null && d.Dirty && notifications == 1,
                "Single unselected publication preserves feasibility, no authorization.");
            Console.WriteLine("P14_AUTO01_EXAMPLE=" + GravityExecutionPlanBuilder.Json(new { p.ProposalFingerprint, p.CandidateFingerprint,
                p.NetworkFingerprint, p.DesignBasisContentFingerprint, p.ConstraintFingerprint, p.FeasibilityIdentity,
                CandidateId = candidate.CandidateId, RecipeId = candidate.RecipeId, RecipeCount = request.Recipes.Count,
                Selected = d.Inputs.CandidateJson != null, Approved = d.Inputs.Approval != null, TestOnly = true }));
        }

        // 3. Preview and diagnostic preview are transient, including when operator generation work exists.
        private static void P14Auto01_PreviewIsReadOnly()
        {
            var d = W14Draft(); int notifications = 0; d.Changed += () => notifications++; var before = W14State(d);
            var p = W14Preview(d);
            Check(p.Status == "PROPOSED" && p.P13InvocationCount == 1 && p.P14InvocationCount == 1, "One existing resolver call per preview.");
            Check(before == W14State(d) && !d.Dirty && notifications == 0 && d.Candidates == null && d.Recipes.Count == 0 &&
                d.Inputs.CandidateJson == null && d.Inputs.GenerationRequestJson == null && d.Inputs.Approval == null, "Preview persists/selects nothing and does not notify.");
            var cache = typeof(EngineeringDesignWorkspace).GetField("automaticProposal", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(d);
            Check(cache != null && cache.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic).All(f => f.FieldType == typeof(string)), "Transient cache retains identities only, no result/candidate/recipe objects.");
            var diagnostic = W14Preview(d, "2");
            Check(diagnostic.Status == "OBJECTIVE_UNSUPPORTED" && before == W14State(d) && notifications == 0, "Original resolver diagnostic returned without persistent mutation.");
            W14RejectUnchanged(d, () => d.StageAutomaticProposal(p.ProposalFingerprint), "AUTOMATIC_PROPOSAL_PREVIEW_REQUIRED");
            var operatorRecipe = p.Recipe; d.SaveRecipe(operatorRecipe); d.Assess(); d.MarkSaved(); before = W14State(d); int prior = notifications;
            Check(W14Preview(d).Status == "PROPOSED" && before == W14State(d) && notifications == prior, "Preview preserves existing operator recipe and all state.");
            var notAssessed = P3Draft(); W14RejectUnchanged(notAssessed, () => W14Preview(notAssessed), "AUTOMATIC_PROPOSAL_ASSESSED_CONTEXT_REQUIRED");
            var noBoundary = P3Draft(boundary: 2); noBoundary.Assess(); noBoundary.MarkSaved(); before = W14State(noBoundary);
            var rejected = W14Preview(noBoundary); Check(rejected.Status == "MISSING_BOUNDARY_AUTHORITY" && before == W14State(noBoundary), "Unsuccessful feasible preview returns resolver's boundary diagnostic, no candidate.");
        }

        // 4. Stage/Select are separate; legacy one-candidate generation still selects explicitly through its old path.
        private static void P14Auto01_ExplicitSelectionAndAuthority()
        {
            var d = W14Draft(); var p = W14Preview(d); var staged = d.StageAutomaticProposal(p.ProposalFingerprint); var beforeBasis = GravityExecutionPlanBuilder.Json(d.Basis);
            Check(d.Inputs.CandidateJson == null, "Stage stops before selection.");
            d.SelectCandidate(staged.Candidates.Single().CandidateId);
            Check(d.Inputs.CandidateJson == GravityExecutionPlanBuilder.Json(staged.Candidates.Single()) && d.Inputs.Approval == null,
                "Existing explicit SelectCandidate chooses the exact staged candidate; no approval.");
            Check(beforeBasis == GravityExecutionPlanBuilder.Json(d.Basis) && d.Basis.CommonDesignBasis.ReviewState == DesignBasisReviewState.Draft, "Selection grants no review authority.");
            Ui02Reject(() => GravityExecutionPlanBuilder.ValidateReviewedBasis(d.Inputs), "DESIGN_BASIS");
            W14RejectUnchanged(d, () => d.StageAutomaticProposal(p.ProposalFingerprint), "AUTOMATIC_PROPOSAL_PREVIEW_REQUIRED");
            var legacy = W14Draft(true); var legacySet = legacy.GenerateCandidates();
            Check(legacy.Inputs.CandidateJson == GravityExecutionPlanBuilder.Json(legacySet.Candidates.Single()), "Legacy GenerateCandidates single-candidate selection behavior preserved.");
            Check(d.Inputs.SourceRead.WriteInvocationCounts.All(c => c.Value == 0) && legacy.Inputs.SourceRead.WriteInvocationCounts.All(c => c.Value == 0), "No fixture runtime/mutation counters changed.");
        }

        // 5. Lifecycle invalidation and direct exposed-input drift fail closed, including standards after reload.
        private static void P14Auto01_StaleContextsAndLifecycle()
        {
            var source = W14Draft(); W14Invalidated(source, () => source.BindNetwork(source.Inputs.SourceRead, source.Inputs.ModelFingerprint));
            var changedSource = W14Draft(); W14Invalidated(changedSource, () => { var raw = changedSource.Inputs.SourceRead; raw.Conduits[0].Length += 1; changedSource.BindNetwork(raw, changedSource.Inputs.ModelFingerprint); });
            var basis = W14Draft(); W14Invalidated(basis, () => basis.SetIdentity(basis.Basis.CommonDesignBasis.DesignBasisId, "TEST_ONLY_BASIS_CHANGED"));
            var constraint = W14Draft(); W14Invalidated(constraint, () => P3Fixed(constraint, 201, ConduitEndKind.Start, 101, 95));
            var policy = W14Draft(); W14Invalidated(policy, () => policy.ChangeAutomaticPreparationPolicy("TEST_ONLY_UNSUPPORTED_POLICY", "2"));
            var assess = W14Draft(); W14Invalidated(assess, () => assess.Assess());
            var revision = W14Draft(); W14Invalidated(revision, () => revision.NewDraft("TEST_ONLY_NEW_DRAFT"));
            var recipe = W14Draft(); var recipePreview = W14Preview(recipe); recipe.SaveRecipe(recipePreview.Recipe);
            W14RejectUnchanged(recipe, () => recipe.StageAutomaticProposal(recipePreview.ProposalFingerprint), "AUTOMATIC_PROPOSAL_PREVIEW_REQUIRED");
            var import = W14Draft(); W14Invalidated(import, () => import.Import(import.Inputs));
            var review = W14Draft(); W14Invalidated(review, () => review.Transition(DesignBasisReviewState.ReadyForReview, "TEST_ONLY actual local operator review-state transition"));

            var objective = W14Draft(); var old = W14Preview(objective); Check(W14Preview(objective, "2").Status == "OBJECTIVE_UNSUPPORTED", "Changed objective revision rejected, previous preview cleared.");
            W14RejectUnchanged(objective, () => objective.StageAutomaticProposal(old.ProposalFingerprint), "AUTOMATIC_PROPOSAL_PREVIEW_REQUIRED");
            var current = W14Draft(); old = W14Preview(current); var context = AutoContext(AutoLive(current), current.Constraints, GravityVerticalDesignProposalResolver.ObjectiveId);
            var wrong = new GravityAutomaticPreparationContext("TEST_ONLY_WRONG_SOURCE", context.NetworkFingerprint, context.Scenario, context.ActivePolicyId, context.ActivePolicyVersion, context.ConstraintFingerprint);
            Check(W14Preview(current, context: wrong).Status == "SOURCE_CONTEXT_MISMATCH", "Resolver rejects wrong current source, no fallback.");
            W14RejectUnchanged(current, () => current.StageAutomaticProposal(old.ProposalFingerprint), "AUTOMATIC_PROPOSAL_PREVIEW_REQUIRED");
            var direct = W14Draft(); old = W14Preview(direct); direct.Basis.CommonDesignBasis.DesignBasisVersion += "_TEST_ONLY_CHANGED";
            W14RejectUnchanged(direct, () => direct.StageAutomaticProposal(old.ProposalFingerprint), "AUTOMATIC_PROPOSAL_STALE");
            var directSource = W14Draft(); old = W14Preview(directSource); directSource.Inputs.SourceRead.Conduits[0].Length += 1;
            W14RejectUnchanged(directSource, () => directSource.StageAutomaticProposal(old.ProposalFingerprint), "AUTOMATIC_PROPOSAL_SOURCE_IDENTITY");
            var stored = W14Draft(); old = W14Preview(stored); stored.Inputs.FeasibilityJson += " ";
            W14RejectUnchanged(stored, () => stored.StageAutomaticProposal(old.ProposalFingerprint), "AUTOMATIC_PROPOSAL_STORED_INPUT_MISMATCH");
            var provenance = W14Draft(); old = W14Preview(provenance); provenance.Constraints.SlopeConstraints[0].Provenance.Notes = "TEST_ONLY_CHANGED_CONSTRAINT";
            W14RejectUnchanged(provenance, () => provenance.StageAutomaticProposal(old.ProposalFingerprint), "AUTOMATIC_PROPOSAL_STORED_INPUT_MISMATCH");

            var standards = V2Draft(); V2Accept(standards); standards.PutConstraint(new OutfallVerticalConstraint(103, Ui02Provenance("TEST_ONLY.workflow.outfall"), CreateFeetValue(90)));
            standards.Assess(); standards.MarkSaved(); old = W14Preview(standards);
            standards.PrepareAutomaticVerticalConstraints(V2Request(standards));
            W14RejectUnchanged(standards, () => standards.StageAutomaticProposal(old.ProposalFingerprint), "AUTOMATIC_PROPOSAL_PREVIEW_REQUIRED");
            standards.Assess(); old = W14Preview(standards);
            var changedStandard = V2Standard(min: .02); V2Criteria(standards, changedStandard);
            W14RejectUnchanged(standards, () => standards.StageAutomaticProposal(old.ProposalFingerprint), "AUTOMATIC_PROPOSAL_PREVIEW_REQUIRED");
            var reload = new EngineeringDesignWorkspace(standards.Inputs.ProjectId, standards.Inputs.ModelFingerprint, standards.Inputs.SourceRead, standards.Inputs);
            W14RejectUnchanged(reload, () => reload.StageAutomaticProposal(old.ProposalFingerprint), "AUTOMATIC_PROPOSAL_PREVIEW_REQUIRED");
            Ui02Reject(() => reload.Assess(), "VERTICAL_STANDARD_CONTEXT_REQUIRED");
            Check(reload.Feasibility == null && reload.Candidates == null && reload.Inputs.CandidateJson == null, "Reload keeps existing fail-closed standards-context requirement.");
        }

        // 6. Idempotence, wrong fingerprints and protected operator-owned work; no synthetic approval created.
        private static void P14Auto01_IdempotenceAndOperatorWork()
        {
            foreach (bool exact in new[] { true, false })
            {
                var d = W14Draft(exact); var p = W14Preview(d); var staged = d.StageAutomaticProposal(p.ProposalFingerprint);
                d.MarkSaved(); var before = W14State(d); int notifications = 0; d.Changed += () => notifications++;
                Check(ReferenceEquals(staged, d.StageAutomaticProposal(p.ProposalFingerprint)) && before == W14State(d) && notifications == 0 && !d.Dirty,
                    "Identical unselected stage is idempotent without dirty change/notification: " + exact);
                var again = W14Preview(d); Check(again.ProposalFingerprint == p.ProposalFingerprint && before == W14State(d), "Re-preview of unchanged staged work remains read-only.");
                Check(ReferenceEquals(staged, d.StageAutomaticProposal(again.ProposalFingerprint)) && notifications == 0 && before == W14State(d), "Re-preview retains valid idempotent staging ownership.");
                W14RejectUnchanged(d, () => d.StageAutomaticProposal("TEST_ONLY_WRONG_FINGERPRINT"), "AUTOMATIC_PROPOSAL_PREVIEW_REQUIRED");
            }
            var noPreview = W14Draft(); W14RejectUnchanged(noPreview, () => noPreview.StageAutomaticProposal("TEST_ONLY_NO_PREVIEW"), "AUTOMATIC_PROPOSAL_PREVIEW_REQUIRED");
            var recipe = W14Draft(); var r = W14Preview(recipe); recipe.SaveRecipe(r.Recipe); recipe.Assess(); recipe.MarkSaved(); r = W14Preview(recipe);
            W14RejectUnchanged(recipe, () => recipe.StageAutomaticProposal(r.ProposalFingerprint), "AUTOMATIC_STAGE_OPERATOR_WORK");
            var generation = W14Draft(); r = W14Preview(generation); generation.Inputs.GenerationRequestJson = GravityExecutionPlanBuilder.Json(new GravityCandidateGenerationRequest(generation.Constraints.VerticalConstraintFingerprint, new[] { r.Recipe }));
            r = W14Preview(generation); W14RejectUnchanged(generation, () => generation.StageAutomaticProposal(r.ProposalFingerprint), "AUTOMATIC_STAGE_OPERATOR_WORK");
            var candidate = W14Draft(true); candidate.GenerateCandidates(); candidate.MarkSaved(); r = W14Preview(candidate);
            W14RejectUnchanged(candidate, () => candidate.StageAutomaticProposal(r.ProposalFingerprint), "AUTOMATIC_STAGE_OPERATOR_WORK");
            var amended = W14Draft(); r = W14Preview(amended); amended.StageAutomaticProposal(r.ProposalFingerprint); amended.Recipes[0].Provenance.Notes = "TEST_ONLY_OPERATOR_AMENDMENT";
            W14RejectUnchanged(amended, () => amended.StageAutomaticProposal(r.ProposalFingerprint), "AUTOMATIC_STAGE_OPERATOR_WORK");
            Check(recipe.Inputs.Approval == null && generation.Inputs.Approval == null && candidate.Inputs.Approval == null && amended.Inputs.Approval == null,
                "No synthetic approval or new authorization path in conflict tests.");
        }
    }
}
