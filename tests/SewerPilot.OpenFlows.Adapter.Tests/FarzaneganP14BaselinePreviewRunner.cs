using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    // OFFLINE PILOT: real previously accepted P12 readback, no Bentley runtime, no engineering authorization.
    // A source-observed existing profile is replayed as a TEST_ONLY exact constraint scenario.
    internal static class FarzaneganP14BaselinePreviewRunner
    {
        private static readonly SourceUnitIdentity Feet = new SourceUnitIdentity("length", "Feet", "feet", "ft");
        private static string Hash(string path)
        {
            using (var f = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
        }
        private static void Save(string dir, string name, object value) =>
            File.WriteAllText(Path.Combine(dir, name), GravityExecutionPlanBuilder.Json(value), new UTF8Encoding(false));
        private static void Require(bool ok, string code)
        {
            if (!ok) throw new InvalidDataException(code);
        }
        private static StandardProvenance Observed(string key) => new StandardProvenance
        {
            SourceLayer = StandardSourceLayer.ApplicationDefault,
            StandardId = "TEST_ONLY_OBSERVED_PROFILE_REPLAY",
            StandardName = "Unreviewed as-is physical geometry replay, NOT a design standard",
            StandardVersion = "1",
            ParameterKey = "test_only.p14." + key,
            DocumentReference = "20261028-live-design-p13-p14/p12-native-read.json",
            Notes = "SOURCE_OBSERVATION_NOT_ENGINEERING_DECISION;NO_APPROVAL;NO_PRODUCTION_WRITE"
        };
        internal static int Run(string acceptedEvidenceDir, string outputDir)
        {
            string phase = "PREFLIGHT", source = null, before = null;
            try
            {
                acceptedEvidenceDir = Path.GetFullPath(acceptedEvidenceDir);
                outputDir = Path.GetFullPath(outputDir);
                if (Directory.Exists(outputDir)) throw new InvalidOperationException("OUTPUT_ALREADY_EXISTS");
                source = Path.Combine(acceptedEvidenceDir, "p12-native-read.json");
                var statusPath = Path.Combine(acceptedEvidenceDir, "native-read-status.json");
                var integrityPath = Path.Combine(acceptedEvidenceDir, "source-integrity.json");
                Require(File.Exists(source) && File.Exists(statusPath) && File.Exists(integrityPath),
                    "ACCEPTED_NATIVE_P12_EVIDENCE_REQUIRED");
                var status = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(statusPath));
                var integrity = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(integrityPath));
                Require((bool)status["Accepted"] && (bool)status["SourceIntegrity"] &&
                    (bool)status["WorkingIntegrity"] && (bool)status["CloseCompleted"] &&
                    Convert.ToInt32(status["SaveCalls"]) == 0 && (bool)integrity["Unchanged"],
                    "PREVIOUS_READ_ACCEPTANCE_INVALID");
                string fingerprint = Convert.ToString(status["ModelFingerprint"]);
                Require(fingerprint == "CCF07288020064F25F9125224CF6CEF0FBD3085D87E85DE173FB3D2BBC652E5B",
                    "FARZANEGAN_SOURCE_IDENTITY_MISMATCH");
                before = Hash(source);
                var raw = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(File.ReadAllText(source));
                Require(raw.Conduits.Count == 71 && raw.Structures.Count == 73 &&
                    raw.ScenarioContext.ActiveScenarioId == 1, "P12_NETWORK_IDENTITY_MISMATCH");
                Require(raw.Conduits.All(c => c.StartInvert.HasValue && c.StopInvert.HasValue &&
                    Feet.Equals(c.StartInvertUnit) && Feet.Equals(c.StopInvertUnit) &&
                    c.PropertyClassifications.TryGetValue("StartInvert", out string start) && start == "PROVEN" &&
                    c.PropertyClassifications.TryGetValue("StopInvert", out string stop) && stop == "PROVEN"),
                    "EXACT_SOURCE_INVERT_PROVENANCE_REQUIRED");
                var outfalls = raw.Structures.Where(n => n.RuntimeCategory == "Outfall").OrderBy(n => n.Id).ToArray();
                Require(outfalls.Select(n => n.Id).SequenceEqual(new[] { 91, 163 }) &&
                    outfalls.All(n => n.InvertElevation.HasValue && Feet.Equals(n.InvertElevationUnit) &&
                    n.PropertyClassifications.TryGetValue("InvertElevation", out string cls) && cls == "PROVEN"),
                    "OUTFALL_SOURCE_INVERT_PROVENANCE_REQUIRED");
                Directory.CreateDirectory(outputDir);
                phase = "REAL_NETWORK_P13_GATE";
                var network = new GravityNetworkIngestionService().Ingest(raw, fingerprint, schemaVersion: 2);
                var empty = new GravityVerticalDesignConstraintSetBuilder().Build(network.NetworkFingerprint);
                var routingContext = new GravityAutomaticPreparationContext(fingerprint, network.NetworkFingerprint,
                    network.ScenarioContext, GravityAutomaticPreparationService.PolicyId,
                    GravityAutomaticPreparationService.PolicyVersion, empty.VerticalConstraintFingerprint);
                var routing = new GravityAutomaticPreparationService().Prepare(network, empty,
                    GravityAutomaticPreparationService.PolicyId, GravityAutomaticPreparationService.PolicyVersion, routingContext);
                Require(routing.ContextFailures.Count == 0 && routing.Components.All(c => c.Eligible) &&
                    routing.DerivedOrientations.Count == 71, "MODEL_DERIVED_ORIENTATION_INVALID");
                var builder = new GravityVerticalDesignConstraintSetBuilder();
                foreach (var o in routing.DerivedOrientations)
                    builder.SetOrientation(o.ConduitId, o.FromNodeId, o.ToNodeId, o.Provenance);
                var authenticConstraints = builder.Build(network.NetworkFingerprint);
                var authenticP13 = new GravityVerticalFeasibilityEngine().Analyze(network, authenticConstraints);
                var authenticP14 = new GravityCandidateGenerator().Generate(network, authenticConstraints,
                    authenticP13, new GravityCandidateGenerationRequest(authenticConstraints.VerticalConstraintFingerprint,
                        new GravityCandidateRecipe[0]));
                Require(authenticP13.Status == VerticalFeasibilityStatus.EngineeringDecisionRequired &&
                    authenticP13.DecisionRequests.Count == 2 && authenticP14.Candidates.Count == 0,
                    "REAL_P13_P14_SAFETY_GATE_REGRESSION");
                Save(outputDir, "authentic-p13-p14-gate.json", new {
                    P13Status = authenticP13.Status.ToString(),
                    DecisionRequests = authenticP13.DecisionRequests,
                    P14Candidates = authenticP14.Candidates.Count,
                    Authority = "REAL_PROJECT_UNREVIEWED_NOT_AUTHORIZED"
                });
                phase = "OUTFALL_ONLY_WHATIF";
                foreach (var o in outfalls)
                    builder.AddOutfallConstraint(o.Id, Observed("outfall." + o.Id),
                        new SourceEngineeringValue(o.InvertElevation, o.InvertElevationUnit));
                var boundaryOnly = builder.Build(network.NetworkFingerprint);
                var boundaryP13 = new GravityVerticalFeasibilityEngine().Analyze(network, boundaryOnly);
                var boundaryP14 = new GravityCandidateGenerator().Generate(network, boundaryOnly,
                    boundaryP13, new GravityCandidateGenerationRequest(boundaryOnly.VerticalConstraintFingerprint,
                        new GravityCandidateRecipe[0]));
                Save(outputDir, "outfall-only-whatif.json", new {
                    P13Status = boundaryP13.Status.ToString(),
                    Decisions = boundaryP13.DecisionRequests.Count, Conflicts = boundaryP13.Conflicts.Count,
                    P14Candidates = boundaryP14.Candidates.Count,
                    Authority = "HYPOTHETICAL_SOURCE_OBSERVED_BOUNDARY_NOT_APPROVED"
                });
                phase = "EXACT_EXISTING_PROFILE_P14_REPLAY";
                foreach (var c in raw.Conduits.OrderBy(c => c.Id))
                {
                    builder.AddFixedConstraint(new GravityConduitEnd(c.Id, ConduitEndKind.Start, c.StartNodeId),
                        new SourceEngineeringValue(c.StartInvert, c.StartInvertUnit),
                        Observed("conduit." + c.Id + ".start"), "TEST_ONLY existing geometry replay");
                    builder.AddFixedConstraint(new GravityConduitEnd(c.Id, ConduitEndKind.Stop, c.StopNodeId),
                        new SourceEngineeringValue(c.StopInvert, c.StopInvertUnit),
                        Observed("conduit." + c.Id + ".stop"), "TEST_ONLY existing geometry replay");
                }
                var exact = builder.Build(network.NetworkFingerprint);
                var p13 = new GravityVerticalFeasibilityEngine().Analyze(network, exact);
                Save(outputDir, "p13-test-only-exact-baseline.json", new {
                    Status = p13.Status.ToString(), Conflicts = p13.Conflicts, Decisions = p13.DecisionRequests,
                    EndpointCount = p13.EndpointRanges.Count, ConstraintFingerprint = exact.VerticalConstraintFingerprint,
                    Authority = "HYPOTHETICAL_FIXED_ALL_EXISTING_ENDPOINTS_NOT_APPROVED"
                });
                Require(p13.Status == VerticalFeasibilityStatus.Feasible &&
                    p13.EndpointRanges.Count == 142, "TEST_ONLY_EXACT_BASELINE_P13_NOT_FEASIBLE");
                var p14 = new GravityCandidateGenerator().Generate(network, exact, p13,
                    new GravityCandidateGenerationRequest(exact.VerticalConstraintFingerprint,
                        new GravityCandidateRecipe[0]));
                Require(p14.Candidates.Count == 1 && p14.RecipeResults.Count == 1 &&
                    p14.RecipeResults[0].Status == CandidateGenerationStatus.Generated,
                    "P14_REAL_MODEL_EXACT_GENERATION_FAILED");
                var candidate = p14.Candidates.Single();
                GravityCandidateExecutor.ValidateLineage(candidate, network.NetworkFingerprint,
                    exact.VerticalConstraintFingerprint);
                Require(candidate.EndpointDesigns.Count == 142 && candidate.DerivedConduits.Count == 71 &&
                    candidate.EndpointDesigns.All(e => e.ExistingInvert.HasValue &&
                        Math.Abs(e.ProposedInvert - e.ExistingInvert.Value) < 1e-9),
                    "EXACT_REPLAY_DIVERGED_FROM_REAL_MODEL");
                Save(outputDir, "p14-test-only-candidate.json", new {
                    candidate.CandidateId, candidate.CandidateFingerprint, candidate.RecipeId,
                    candidate.SourceNetworkFingerprint, candidate.VerticalConstraintFingerprint,
                    candidate.EndpointDesigns, candidate.DerivedConduits,
                    Authority = "TEST_ONLY_EXISTING_GEOMETRY_REPLAY_NO_NEW_DESIGN_NO_APPROVAL"
                });
                Save(outputDir, "verdict.json", new {
                    Status = "P14_REAL_MODEL_TEST_ONLY_EXACT_BASELINE_GENERATED",
                    AuthenticP13 = authenticP13.Status.ToString(), AuthenticP14Candidates = 0,
                    OutfallOnlyP13 = boundaryP13.Status.ToString(), OutfallOnlyP14Candidates = boundaryP14.Candidates.Count,
                    TestOnlyP13 = p13.Status.ToString(), TestOnlyP14Candidates = 1,
                    Conduits = 71, Endpoints = 142, ModifiedEndpoints = 0, OutfallCount = 2,
                    SourceP12Sha256 = before, ModelSha256 = fingerprint,
                    Selected = false, Approved = false, SolverInvocations = 0, ModelSaveInvocations = 0,
                    ProductionP14Accepted = false
                });
                Console.WriteLine("AUTHENTIC_P13=" + authenticP13.Status + " AUTHENTIC_P14=0");
                Console.WriteLine("OUTFALL_ONLY_P13=" + boundaryP13.Status + " P14=" + boundaryP14.Candidates.Count);
                Console.WriteLine("TEST_ONLY_P13=" + p13.Status + " P14_CANDIDATES=1 ENDPOINTS=142 CHANGES=0");
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("P14_BLOCKED_PHASE=" + phase + " " + e.GetType().Name + ":" + e.Message);
                if (Directory.Exists(outputDir))
                    Save(outputDir, "blocked.json", new { Phase = phase, Error = e.ToString(), Status = "NOT_ACCEPTED" });
                return 2;
            }
            finally
            {
                if (source != null && before != null && File.Exists(source))
                {
                    bool unchanged = before == Hash(source);
                    if (Directory.Exists(outputDir))
                        Save(outputDir, "p12-input-integrity.json", new { Unchanged = unchanged, SHA256 = before });
                    Console.WriteLine("P12_EVIDENCE_UNCHANGED=" + unchanged);
                }
            }
        }
    }
}
