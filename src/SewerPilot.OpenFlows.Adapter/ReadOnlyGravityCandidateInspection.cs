using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SewerPilot.OpenFlows.Adapter
{
    // Additive projection boundary for GravityFlow vertical feasibility and candidate preview.
    // Strictly read-only, non-mutating calculation. Never approves, saves, or modifies models.
    public static class ReadOnlyGravityCandidateInspection
    {
        public static StoredDesignArtifactReference Locate(ProjectState state)
        {
            return ReadOnlyDesignBasisInspection.Locate(state, "GravityFlow");
        }

        public static StoredVerticalFeasibilitySummary AssessFeasibility(string directory, ProjectState state)
        {
            var artifactReference = Locate(state);
            if (artifactReference == null)
            {
                return new StoredVerticalFeasibilitySummary
                {
                    ProjectId = state != null ? state.ProjectId : null,
                    StorageStatus = "NOT_SAVED",
                    FeasibilityStatus = "NOT_AVAILABLE",
                    Conflicts = new List<VerticalConstraintConflictSummary>(),
                    DecisionRequests = new List<EngineeringDecisionRequestSummary>(),
                    EndpointRanges = new List<EndpointElevationRangeSummary>(),
                    IncompleteItems = new List<string>()
                };
            }

            var artifact = EngineeringDesignStorage.Load(directory, state);
            if (artifact == null)
            {
                return new StoredVerticalFeasibilitySummary
                {
                    ProjectId = state.ProjectId,
                    StorageStatus = "NOT_SAVED",
                    FeasibilityStatus = "NOT_AVAILABLE",
                    Conflicts = new List<VerticalConstraintConflictSummary>(),
                    DecisionRequests = new List<EngineeringDecisionRequestSummary>(),
                    EndpointRanges = new List<EndpointElevationRangeSummary>(),
                    IncompleteItems = new List<string>()
                };
            }

            if (artifact.Inputs == null || artifact.Inputs.SourceRead == null)
            {
                return new StoredVerticalFeasibilitySummary
                {
                    ProjectId = state.ProjectId,
                    StorageStatus = "STORED",
                    FeasibilityStatus = "Incomplete",
                    ConstraintFingerprint = artifact.ConstraintFingerprint,
                    IncompleteItems = new List<string> { "SOURCE_READ_UNAVAILABLE" },
                    Conflicts = new List<VerticalConstraintConflictSummary>(),
                    DecisionRequests = new List<EngineeringDecisionRequestSummary>(),
                    EndpointRanges = new List<EndpointElevationRangeSummary>()
                };
            }

            var network = new GravityNetworkIngestionService().Ingest(artifact.Inputs.SourceRead, artifact.Inputs.ModelFingerprint, schemaVersion: 2);
            var constraints = GravityExecutionPlanBuilder.ValidateEngineeringConstraints(artifact.Inputs, network);
            var feasibility = new GravityVerticalFeasibilityEngine().Analyze(network, constraints);

            var conflictSummaries = new List<VerticalConstraintConflictSummary>();
            if (feasibility.Conflicts != null)
            {
                foreach (var c in feasibility.Conflicts)
                {
                    conflictSummaries.Add(new VerticalConstraintConflictSummary
                    {
                        ConflictCode = c.ConflictCode,
                        AffectedElements = c.AffectedElements != null ? c.AffectedElements.ToList() : new List<string>(),
                        ConstraintIds = c.ConstraintIds != null ? c.ConstraintIds.ToList() : new List<string>(),
                        KnownValues = c.KnownValues != null ? new Dictionary<string, double>(c.KnownValues) : new Dictionary<string, double>(),
                        Explanation = c.Explanation
                    });
                }
            }

            var decisionSummaries = new List<EngineeringDecisionRequestSummary>();
            if (feasibility.DecisionRequests != null)
            {
                foreach (var d in feasibility.DecisionRequests)
                {
                    decisionSummaries.Add(new EngineeringDecisionRequestSummary
                    {
                        Decision = d.Decision,
                        KnownFacts = d.KnownFacts != null ? new Dictionary<string, string>(d.KnownFacts) : new Dictionary<string, string>(),
                        AvailableChoices = d.AvailableChoices != null ? d.AvailableChoices.ToList() : new List<string>(),
                        Consequences = d.Consequences != null ? new Dictionary<string, string>(d.Consequences) : new Dictionary<string, string>(),
                        IndependentWorkThatCanContinue = d.IndependentWorkThatCanContinue != null ? d.IndependentWorkThatCanContinue.ToList() : new List<string>()
                    });
                }
            }

            var rangeSummaries = new List<EndpointElevationRangeSummary>();
            if (feasibility.EndpointRanges != null)
            {
                foreach (var kvp in feasibility.EndpointRanges)
                {
                    var range = kvp.Value;
                    rangeSummaries.Add(new EndpointElevationRangeSummary
                    {
                        ConduitId = range.ConduitEnd.ConduitId,
                        End = range.ConduitEnd.End.ToString(),
                        NodeId = range.ConduitEnd.NodeId,
                        MinimumAllowedInvert = range.MinimumAllowedInvert,
                        MaximumAllowedInvert = range.MaximumAllowedInvert,
                        ExactInvert = range.ExactInvert,
                        Unit = range.Unit != null ? range.Unit.Symbol : "ft"
                    });
                }
            }

            return new StoredVerticalFeasibilitySummary
            {
                ProjectId = state.ProjectId,
                StorageStatus = "STORED",
                FeasibilityStatus = feasibility.Status.ToString(),
                ConstraintFingerprint = feasibility.ConstraintFingerprint,
                PropagationPasses = feasibility.PropagationPasses,
                ConduitCount = network.Conduits.Count,
                ConflictCount = feasibility.Conflicts != null ? feasibility.Conflicts.Count : 0,
                DecisionRequestCount = feasibility.DecisionRequests != null ? feasibility.DecisionRequests.Count : 0,
                IncompleteItemCount = feasibility.IncompleteItems != null ? feasibility.IncompleteItems.Count : 0,
                Conflicts = conflictSummaries,
                DecisionRequests = decisionSummaries,
                EndpointRanges = rangeSummaries,
                IncompleteItems = feasibility.IncompleteItems != null ? feasibility.IncompleteItems.ToList() : new List<string>()
            };
        }

        public static StoredGravityCandidatePreviewSummary PreviewCandidates(string directory, ProjectState state, string candidateId = null, string recipeId = null)
        {
            var artifactReference = Locate(state);
            if (artifactReference == null)
            {
                return new StoredGravityCandidatePreviewSummary
                {
                    ProjectId = state != null ? state.ProjectId : null,
                    StorageStatus = "NOT_SAVED",
                    FeasibilityStatus = "NOT_AVAILABLE",
                    CandidateCount = 0,
                    Candidates = new List<CandidateProfileSummary>(),
                    RecipeResults = new List<RecipeResultSummary>()
                };
            }

            var artifact = EngineeringDesignStorage.Load(directory, state);
            if (artifact == null)
            {
                return new StoredGravityCandidatePreviewSummary
                {
                    ProjectId = state.ProjectId,
                    StorageStatus = "NOT_SAVED",
                    FeasibilityStatus = "NOT_AVAILABLE",
                    CandidateCount = 0,
                    Candidates = new List<CandidateProfileSummary>(),
                    RecipeResults = new List<RecipeResultSummary>()
                };
            }

            if (artifact.Inputs == null || artifact.Inputs.SourceRead == null)
            {
                return new StoredGravityCandidatePreviewSummary
                {
                    ProjectId = state.ProjectId,
                    StorageStatus = "STORED",
                    FeasibilityStatus = "Incomplete",
                    CandidateCount = 0,
                    Candidates = new List<CandidateProfileSummary>(),
                    RecipeResults = new List<RecipeResultSummary>()
                };
            }

            var network = new GravityNetworkIngestionService().Ingest(artifact.Inputs.SourceRead, artifact.Inputs.ModelFingerprint, schemaVersion: 2);
            var constraints = GravityExecutionPlanBuilder.ValidateEngineeringConstraints(artifact.Inputs, network);
            var feasibility = new GravityVerticalFeasibilityEngine().Analyze(network, constraints);

            GravityCandidateGenerationRequest generation;
            if (!string.IsNullOrWhiteSpace(artifact.Inputs.GenerationRequestJson))
            {
                generation = GravityEngineeringWire.Read<GravityCandidateGenerationRequest>(RuntimeHostProtocol.Serializer().DeserializeObject(artifact.Inputs.GenerationRequestJson));
            }
            else
            {
                var recipes = EngineeringDesignStorage.ReadRecipes(artifact.RecipesJson);
                generation = new GravityCandidateGenerationRequest(constraints.VerticalConstraintFingerprint, recipes);
            }

            if (generation.Recipes != null && generation.Recipes.Count > 0)
            {
                foreach (var r in generation.Recipes)
                {
                    GravityExecutionPlanBuilder.ValidateEngineeringProvenance(r.Provenance);
                    foreach (var s in r.EndpointSelections)
                    {
                        GravityExecutionPlanBuilder.ValidateEngineeringProvenance(s.Provenance);
                    }
                }
            }

            var candidateSet = new GravityCandidateGenerator().Generate(network, constraints, feasibility, generation);

            string selectedCandidateId = null;
            if (!string.IsNullOrWhiteSpace(artifact.Inputs.CandidateJson))
            {
                try
                {
                    var selected = RuntimeHostProtocol.Serializer().Deserialize<GravityDesignCandidate>(artifact.Inputs.CandidateJson);
                    selectedCandidateId = selected != null ? selected.CandidateId : null;
                }
                catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in ReadOnlyGravityCandidateInspection.cs: " + suppressedEx.Message); }
            }

            var candidateSummaries = new List<CandidateProfileSummary>();
            var candidatesToEnumerate = candidateSet.Candidates.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(candidateId))
            {
                candidatesToEnumerate = candidatesToEnumerate.Where(c => string.Equals(c.CandidateId, candidateId, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(recipeId))
            {
                candidatesToEnumerate = candidatesToEnumerate.Where(c => string.Equals(c.RecipeId, recipeId, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var cand in candidatesToEnumerate)
            {
                var endpointSummaries = new List<CandidateEndpointSummary>();
                foreach (var ep in cand.EndpointDesigns)
                {
                    endpointSummaries.Add(new CandidateEndpointSummary
                    {
                        ConduitId = ep.ConduitId,
                        End = ep.End.ToString(),
                        NodeId = ep.NodeId,
                        ExistingInvert = ep.ExistingInvert,
                        ProposedInvert = ep.ProposedInvert,
                        Unit = ep.Unit != null ? ep.Unit.Symbol : "ft",
                        SelectionSource = ep.SelectionSource.ToString()
                    });
                }

                var conduitSummaries = new List<CandidateConduitSummary>();
                foreach (var cd in cand.DerivedConduits)
                {
                    conduitSummaries.Add(new CandidateConduitSummary
                    {
                        ConduitId = cd.ConduitId,
                        FromNodeId = cd.FromNodeId,
                        ToNodeId = cd.ToNodeId,
                        FromInvert = cd.FromInvert,
                        ToInvert = cd.ToInvert,
                        Length = cd.Length,
                        Slope = cd.Slope,
                        StartCrown = cd.StartCrown,
                        StopCrown = cd.StopCrown,
                        StartCover = cd.StartCover,
                        StopCover = cd.StopCover,
                        Unit = cd.Unit != null ? cd.Unit.Symbol : "ft"
                    });
                }

                candidateSummaries.Add(new CandidateProfileSummary
                {
                    CandidateId = cand.CandidateId,
                    RecipeId = cand.RecipeId,
                    CandidateFingerprint = cand.CandidateFingerprint,
                    VerticalConstraintFingerprint = cand.VerticalConstraintFingerprint,
                    EndpointCount = cand.EndpointDesigns.Count,
                    ConduitCount = cand.DerivedConduits.Count,
                    Endpoints = endpointSummaries,
                    Conduits = conduitSummaries
                });
            }

            var recipeResults = new List<RecipeResultSummary>();
            if (candidateSet.RecipeResults != null)
            {
                foreach (var rr in candidateSet.RecipeResults)
                {
                    recipeResults.Add(new RecipeResultSummary
                    {
                        RecipeId = rr.RecipeId,
                        Status = rr.Status.ToString(),
                        RejectionReasons = rr.Issues != null ? rr.Issues.ToList() : new List<string>()
                    });
                }
            }

            return new StoredGravityCandidatePreviewSummary
            {
                ProjectId = state.ProjectId,
                StorageStatus = "STORED",
                FeasibilityStatus = feasibility.Status.ToString(),
                SelectedCandidateId = selectedCandidateId,
                CandidateCount = candidateSummaries.Count,
                Candidates = candidateSummaries,
                RecipeResults = recipeResults
            };
        }
    }

    public sealed class StoredVerticalFeasibilitySummary
    {
        public string Kind { get { return "STORED_VERTICAL_FEASIBILITY_SUMMARY"; } }
        public string ProjectId { get; set; }
        public string StorageStatus { get; set; }
        public string FeasibilityStatus { get; set; }
        public string ConstraintFingerprint { get; set; }
        public int PropagationPasses { get; set; }
        public int ConduitCount { get; set; }
        public int ConflictCount { get; set; }
        public int DecisionRequestCount { get; set; }
        public int IncompleteItemCount { get; set; }
        public List<VerticalConstraintConflictSummary> Conflicts { get; set; }
        public List<EngineeringDecisionRequestSummary> DecisionRequests { get; set; }
        public List<EndpointElevationRangeSummary> EndpointRanges { get; set; }
        public List<string> IncompleteItems { get; set; }
    }

    public sealed class VerticalConstraintConflictSummary
    {
        public string ConflictCode { get; set; }
        public List<string> AffectedElements { get; set; }
        public List<string> ConstraintIds { get; set; }
        public Dictionary<string, double> KnownValues { get; set; }
        public string Explanation { get; set; }
    }

    public sealed class EngineeringDecisionRequestSummary
    {
        public string Decision { get; set; }
        public Dictionary<string, string> KnownFacts { get; set; }
        public List<string> AvailableChoices { get; set; }
        public Dictionary<string, string> Consequences { get; set; }
        public List<string> IndependentWorkThatCanContinue { get; set; }
    }

    public sealed class EndpointElevationRangeSummary
    {
        public int ConduitId { get; set; }
        public string End { get; set; }
        public int NodeId { get; set; }
        public double? MinimumAllowedInvert { get; set; }
        public double? MaximumAllowedInvert { get; set; }
        public double? ExactInvert { get; set; }
        public string Unit { get; set; }
    }

    public sealed class StoredGravityCandidatePreviewSummary
    {
        public string Kind { get { return "STORED_GRAVITY_CANDIDATE_PREVIEW"; } }
        public string ProjectId { get; set; }
        public string StorageStatus { get; set; }
        public string FeasibilityStatus { get; set; }
        public string SelectedCandidateId { get; set; }
        public int CandidateCount { get; set; }
        public List<CandidateProfileSummary> Candidates { get; set; }
        public List<RecipeResultSummary> RecipeResults { get; set; }
    }

    public sealed class CandidateProfileSummary
    {
        public string CandidateId { get; set; }
        public string RecipeId { get; set; }
        public string CandidateFingerprint { get; set; }
        public string VerticalConstraintFingerprint { get; set; }
        public int EndpointCount { get; set; }
        public int ConduitCount { get; set; }
        public List<CandidateEndpointSummary> Endpoints { get; set; }
        public List<CandidateConduitSummary> Conduits { get; set; }
    }

    public sealed class CandidateEndpointSummary
    {
        public int ConduitId { get; set; }
        public string End { get; set; }
        public int NodeId { get; set; }
        public double? ExistingInvert { get; set; }
        public double ProposedInvert { get; set; }
        public string Unit { get; set; }
        public string SelectionSource { get; set; }
    }

    public sealed class CandidateConduitSummary
    {
        public int ConduitId { get; set; }
        public int FromNodeId { get; set; }
        public int ToNodeId { get; set; }
        public double FromInvert { get; set; }
        public double ToInvert { get; set; }
        public double Length { get; set; }
        public double Slope { get; set; }
        public double? StartCrown { get; set; }
        public double? StopCrown { get; set; }
        public double? StartCover { get; set; }
        public double? StopCover { get; set; }
        public string Unit { get; set; }
    }

    public sealed class RecipeResultSummary
    {
        public string RecipeId { get; set; }
        public string Status { get; set; }
        public List<string> RejectionReasons { get; set; }
    }
}
