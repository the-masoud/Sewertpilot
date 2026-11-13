using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        // Pure presentation of accepted TEST_ONLY proposal fixtures. Never construct a panel/control or execute a model reader.
        private static GravityVerticalDesignProposalResolver.Result P14PresentationPreview(EngineeringDesignWorkspace design) =>
            design.PreviewAutomaticProposal(GravityVerticalDesignProposalResolver.ObjectiveId, GravityVerticalDesignProposalResolver.ObjectiveVersion,
                design.CurrentAutomaticProposalContext(GravityVerticalDesignProposalResolver.ObjectiveId, GravityVerticalDesignProposalResolver.ObjectiveVersion));
        private static string P14ConduitText(string text, int id)
        {
            var start = text.IndexOf("\r\n\r\nConduit " + id + "\r\n", StringComparison.Ordinal);
            Check(start >= 0, "Conduit has an individual presentation block: " + id);
            var end = text.IndexOf("\r\n\r\n", start + 4, StringComparison.Ordinal);
            return text.Substring(start, (end < 0 ? text.Length : end) - start);
        }
        private static string P14Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static void P14Auto02_IdentityAndNonAuthority()
        {
            var design = W14Draft(); var before = W14State(design); int changed = 0; design.Changed += () => changed++;
            var context = design.CurrentAutomaticProposalContext(GravityVerticalDesignProposalResolver.ObjectiveId, GravityVerticalDesignProposalResolver.ObjectiveVersion);
            var expected = AutoContext(AutoLive(design), design.Constraints, GravityVerticalDesignProposalResolver.ObjectiveId, GravityVerticalDesignProposalResolver.ObjectiveVersion);
            Check(GravityExecutionPlanBuilder.Json(context) == GravityExecutionPlanBuilder.Json(expected), "Accessor uses actual bound model/network/scenario/constraints and explicit objective.");
            Check(before == W14State(design) && changed == 0, "Context accessor is read-only.");
            var result = P14PresentationPreview(design); var text = EngineeringDesignPanel.PresentAutomaticProposal(result);
            Check(result.Status == "PROPOSED" && text.StartsWith("PROPOSED — NOT APPROVED\r\nShallowest feasible invert profile\r\n", StringComparison.Ordinal), "Successful proposal is clearly non-authoritative.");
            Check(text.Contains("Objective ID/version: " + result.Objective + " / " + result.ObjectiveRevision), "Actual fixed objective/version shown.");
            foreach (var identity in new[] { "Proposal fingerprint: " + result.ProposalFingerprint, "Candidate fingerprint: " + result.CandidateFingerprint,
                "Network fingerprint: " + result.NetworkFingerprint, "Constraint fingerprint: " + result.ConstraintFingerprint,
                "Design Basis content fingerprint: " + result.DesignBasisContentFingerprint })
                Check(text.Contains(identity), "Actual proposal identity shown: " + identity.Split(':')[0]);
            Check(text.Contains("Proposal provenance: " + GravityExecutionPlanBuilder.Json(result.Provenance)), "Actual detached provenance shown without invented standard/approval.");
            Check(text.Contains("Resolver evidence / Constraint explanation:"), "Constraint explanations explicitly identified as resolver evidence.");
            Check(result.Evidence.All(text.Contains), "Every actual resolver evidence item preserved.");
            Check(before == W14State(design) && changed == 0 && design.Candidates == null && design.Inputs.Approval == null,
                "Pure formatting/preview creates no staged candidate, selection, approval or dirty notification.");
            var unbound = new EngineeringDesignWorkspace(design.Inputs.ProjectId, design.Inputs.ModelFingerprint);
            Ui02Reject(() => unbound.CurrentAutomaticProposalContext(result.Objective, result.ObjectiveRevision), "AUTOMATIC_PROPOSAL_BOUND_CONTEXT_REQUIRED");
        }

        // Detach the accepted proposal for formatter-only reversed Bentley-end coverage. Preserve all values by physical node;
        // this TEST_ONLY projection is never passed to staging, feasibility, production authorization or persistence.
        private static GravityVerticalDesignProposalResolver.Result P14ReversedPresentation(EngineeringDesignWorkspace design,
            GravityVerticalDesignProposalResolver.Result original)
        {
            int reversed = original.Endpoints.Min(e => e.ConduitId);
            var endpoints = original.Endpoints.Reverse().Select(e => e.ConduitId != reversed ? e :
                new CandidateEndpointDesign(e.ConduitId, e.End == ConduitEndKind.Start ? ConduitEndKind.Stop : ConduitEndKind.Start,
                    e.NodeId, e.ExistingInvert, e.ProposedInvert, e.Unit, e.SelectionSource));
            var derived = original.DerivedConduits.Reverse().Select(c => c.ConduitId != reversed ? c :
                new CandidateConduitVerticalState(c.ConduitId, c.FromNodeId, c.ToNodeId, c.StopInvert, c.StartInvert,
                    c.FromInvert, c.ToInvert, c.Length, c.Slope, c.StopCrown, c.StartCrown, c.StopCover, c.StartCover, c.Unit));
            var candidate = new GravityDesignCandidate("TEST_ONLY_PRESENTATION", original.CandidateRecipeId, original.NetworkFingerprint,
                original.ConstraintFingerprint, endpoints, derived, null);
            var state = new GravityVerticalDesignProposalResolver.State { Network = AutoLive(design), Constraints = design.Constraints,
                Objective = original.Objective, Revision = original.ObjectiveRevision, BasisFingerprint = original.DesignBasisContentFingerprint,
                ConstraintContent = original.ConstraintContentFingerprint, FeasibilityIdentity = original.FeasibilityIdentity, Generator = original.GeneratorVersion };
            state.Evidence.AddRange(original.Evidence);
            return new GravityVerticalDesignProposalResolver.Result(state, "PROPOSED", candidate: candidate, recipe: original.Recipe, provenance: original.Provenance);
        }

        private static void P14Auto02_ValuesDirectionsAndOrdering()
        {
            var design = W14Draft(); var result = P14PresentationPreview(design); var oldCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var text = EngineeringDesignPanel.PresentAutomaticProposal(result);
                Check(text == EngineeringDesignPanel.PresentAutomaticProposal(result), "Presentation deterministic for identical inputs.");
                Check(text.IndexOf("\r\n\r\nConduit 201\r\n", StringComparison.Ordinal) < text.IndexOf("\r\n\r\nConduit 202\r\n", StringComparison.Ordinal), "Ascending conduit identity ordering.");
                foreach (var derived in result.DerivedConduits)
                {
                    var section = P14ConduitText(text, derived.ConduitId);
                    var start = result.Endpoints.Single(e => e.ConduitId == derived.ConduitId && e.End == ConduitEndKind.Start);
                    var stop = result.Endpoints.Single(e => e.ConduitId == derived.ConduitId && e.End == ConduitEndKind.Stop);
                    Check(section.Contains("Bentley Start node: " + start.NodeId) && section.Contains("Bentley Stop node: " + stop.NodeId), "Actual Bentley node identities shown.");
                    Check(section.Contains("Proposed Start invert (ft): " + P14Number(start.ProposedInvert)) && section.Contains("Proposed Stop invert (ft): " + P14Number(stop.ProposedInvert)), "Actual proposed Feet endpoint values use invariant culture.");
                    Check(section.Contains("Design From node: " + derived.FromNodeId) && section.Contains("Design To node: " + derived.ToNodeId), "Design direction shown independently of Bentley labels.");
                    Check(section.Contains("Slope ratio: " + P14Number(derived.Slope)), "Actual native slope ratio preserved without percent conversion.");
                    Check(section.Contains("Available Start cover (ft): " + (derived.StartCover.HasValue ? P14Number(derived.StartCover.Value) : "Unavailable")) &&
                        section.Contains("Available Stop cover (ft): " + (derived.StopCover.HasValue ? P14Number(derived.StopCover.Value) : "Unavailable")), "Actual optional Feet covers shown.");
                    Check(section.Contains("Source observed Start invert (ft): " + (start.ExistingInvert.HasValue ? P14Number(start.ExistingInvert.Value) : "Unavailable")), "Source observations explicitly distinguished from proposals.");
                }
                var reversed = P14ReversedPresentation(design, result); var reversedText = EngineeringDesignPanel.PresentAutomaticProposal(reversed);
                var reversedSection = P14ConduitText(reversedText, 201); var first = reversed.DerivedConduits.Single(c => c.ConduitId == 201);
                var bentleyStart = reversed.Endpoints.Single(e => e.ConduitId == 201 && e.End == ConduitEndKind.Start);
                Check(bentleyStart.NodeId != first.FromNodeId, "Formatter-only TEST_ONLY projection actually reverses Bentley Start versus gravity From.");
                Check(reversedSection.Contains("Bentley Start node: " + bentleyStart.NodeId) && reversedSection.Contains("Design From node: " + first.FromNodeId), "Reversed physical endpoint cannot be presented as the gravity From endpoint.");
                Check(reversedSection.Contains("Proposed Start invert (ft): " + P14Number(bentleyStart.ProposedInvert)), "Reverse-direction presentation preserves the actual Start-associated value.");
            }
            finally { Thread.CurrentThread.CurrentCulture = oldCulture; }
        }

        private static void P14Auto02_UnavailableCover()
        {
            var result = P14PresentationPreview(W14Draft()); var text = EngineeringDesignPanel.PresentAutomaticProposal(result);
            Check(result.DerivedConduits.Any(c => !c.StartCover.HasValue || !c.StopCover.HasValue), "Accepted fixture has genuinely missing source-ground cover data.");
            foreach (var derived in result.DerivedConduits)
            {
                var section = P14ConduitText(text, derived.ConduitId);
                if (!derived.StartCover.HasValue) Check(section.Contains("Available Start cover (ft): Unavailable") && !section.Contains("Available Start cover (ft): 0"), "Missing Start cover stays unavailable, never zero.");
                if (!derived.StopCover.HasValue) Check(section.Contains("Available Stop cover (ft): Unavailable") && !section.Contains("Available Stop cover (ft): 0"), "Missing Stop cover stays unavailable, never zero.");
            }
            Check(!text.Contains("NaN") && !text.Contains("Infinity"), "No missing/nonfinite value is presented as engineering numeric data.");
        }

        private static void P14Auto02_ActualRejectionReasons()
        {
            var boundary = P3Draft(boundary: 0); var rejected = P3Resolve(boundary);
            var design = W14Draft(); var wrongObjective = design.PreviewAutomaticProposal(GravityVerticalDesignProposalResolver.ObjectiveId, "2",
                design.CurrentAutomaticProposalContext(GravityVerticalDesignProposalResolver.ObjectiveId, "2"));
            foreach (var result in new[] { rejected, wrongObjective })
            {
                Check(result.Status != "PROPOSED" && result.Reasons.Count > 0, "Accepted negative fixture returns actual rejection diagnostics.");
                var text = EngineeringDesignPanel.PresentAutomaticProposal(result);
                Check(text == "Automatic proposal status: " + result.Status + "\r\nReasons:\r\n" + string.Join("\r\n", result.Reasons), "Rejected presentation contains the exact status and unmodified reasons.");
                Check(!text.Contains("Proposed Start invert") && !text.Contains("Candidate fingerprint:") && !text.Contains("Proposal provenance:"), "Rejected proposal invents no engineering values, identity or provenance.");
            }
            Check(EngineeringDesignPanel.PresentAutomaticProposal(null) == "No automatic proposal. Preview required.", "Absent result does not fabricate proposal data.");
        }
    }
}
