using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    // Model-specific P13/P14 application-workspace acceptance. Strictly TEST_ONLY:
    // no review transition, engineering approval, runtime execution, model save or solver.
    internal static class FarzaneganP13P14OperationalRunner
    {
        private const string ModelSha = "CCF07288020064F25F9125224CF6CEF0FBD3085D87E85DE173FB3D2BBC652E5B";
        private const string SqliteSha = "A8D7963461B7A16F8EE9FD9EC42A1F412257BDC6A220510840FB095A5B5BDB0A";
        private const string P12Sha = "74FB2CA66A9E2249EB0683E878272B5CFA272EF29C18BCAF065148D4C8A95C7F";
        private static readonly SourceUnitIdentity Feet = new SourceUnitIdentity("length", "Feet", "feet", "ft");
        private sealed class Row
        {
            public int Id, Start, Stop, From, To;
            public double StartInvert, StopInvert, Length, Diameter, MinSlope, MaxSlope, MinCover, MaxCover;
        }
        private static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidDataException(message);
        }
        private static string Sha(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        private static SourceEngineeringValue Ft(double value) => new SourceEngineeringValue(value, Feet);
        private static StandardProvenance Observation(string key) => new StandardProvenance
        {
            SourceLayer = StandardSourceLayer.ApplicationDefault,
            StandardId = "TEST_ONLY_FARZANEGAN_SOURCE_OBSERVATION",
            StandardName = "Unreviewed Farzanegan model configuration",
            StandardVersion = "1",
            ParameterKey = "test_only.farzanegan.workspace." + key,
            DocumentReference = "docs/evidence/farzanegan/20261028-design-intake/active-conduits-model-derived-proposals.csv",
            Notes = "SOURCE_OBSERVATION_ONLY_NOT_ENGINEERING_APPROVED_NO_EXECUTION_AUTHORITY"
        };
        private static void Save(string directory, string name, object value) =>
            File.WriteAllText(Path.Combine(directory, name), GravityExecutionPlanBuilder.Json(value), new UTF8Encoding(false));
        private static Row[] ReadRows(string path)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            Check(lines.Length == 72, "EXPECTED_71_ROWS");
            var columns = lines[0].Split(',').Select((name, index) => new { Name = name.Trim().TrimStart('\uFEFF'), Index = index })
                .ToDictionary(x => x.Name, x => x.Index, StringComparer.Ordinal);
            Func<string[], string, string> val = (cells, name) => cells[columns[name]].Trim().Trim('"');
            Func<string, int> i = x => int.Parse(x, CultureInfo.InvariantCulture);
            Func<string, double> d = x => double.Parse(x, CultureInfo.InvariantCulture);
            return lines.Skip(1).Select(line =>
            {
                var c = line.Split(',');
                Check(c.Length == columns.Count, "CSV_SCHEMA");
                return new Row {
                    Id = i(val(c, "conduit_id")), Start = i(val(c, "start_node")), Stop = i(val(c, "stop_node")),
                    From = i(val(c, "proposal_from_node")), To = i(val(c, "proposal_to_node")),
                    StartInvert = d(val(c, "source_start_invert_ft")), StopInvert = d(val(c, "source_stop_invert_ft")),
                    Length = d(val(c, "source_length_ft")), Diameter = d(val(c, "source_diameter_ft")),
                    MinSlope = d(val(c, "model_local_min_slope")), MaxSlope = d(val(c, "model_local_max_slope")),
                    MinCover = d(val(c, "model_local_min_cover_ft")), MaxCover = d(val(c, "model_local_max_cover_ft"))
                };
            }).OrderBy(r => r.Id).ToArray();
        }
        private static void ExpectGate(Action operation, string code)
        {
            try { operation(); }
            catch (InvalidOperationException ex) {
                Check(ex.Message.Contains(code), "WRONG_GATE:" + ex.Message);
                return;
            }
            throw new InvalidDataException("GATE_NOT_ENFORCED:" + code);
        }
        internal static int Run(string repo, string outputDir)
        {
            string phase = "PREFLIGHT";
            string[] files = null;
            string[] before = null;
            try
            {
                repo = Path.GetFullPath(repo);
                outputDir = Path.GetFullPath(outputDir);
                Check(!Directory.Exists(outputDir), "EVIDENCE_DIR_EXISTS");
                var source = Path.Combine(repo, "engineering", "customer-data", "Farzanegan",
                    "SourceCopy_20261027", "Model-SewerGEM");
                files = Directory.GetFiles(source).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                Check(files.Length == 7, "SOURCE_FAMILY_COUNT");
                before = files.Select(f => Path.GetFileName(f) + "|" + Sha(f)).ToArray();
                var model = Path.Combine(source, "FEP-AOC-030826.stsw");
                var sqlite = model + ".sqlite";
                var p12 = Path.Combine(repo, "docs", "evidence", "farzanegan",
                    "20261028-live-design-p13-p14", "p12-native-read.json");
                var csv = Path.Combine(repo, "docs", "evidence", "farzanegan",
                    "20261028-design-intake", "active-conduits-model-derived-proposals.csv");
                Check(Sha(model) == ModelSha && Sha(sqlite) == SqliteSha && Sha(p12) == P12Sha, "SOURCE_HASH_MISMATCH");
                var raw = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(File.ReadAllText(p12));
                var rows = ReadRows(csv);
                Check(raw.Conduits.Count == 71 && raw.Structures.Count == 73 &&
                    raw.ScenarioContext.ActiveScenarioId == 1 && rows.Select(x => x.Id).Distinct().Count() == 71,
                    "MODEL_INVENTORY");
                var nodes = raw.Structures.ToDictionary(n => n.Id);
                var pipes = raw.Conduits.ToDictionary(c => c.Id);
                foreach (var row in rows)
                {
                    var c = pipes[row.Id];
                    Check(c.StartNodeId == row.Start && c.StopNodeId == row.Stop &&
                        Math.Abs(c.StartInvert.Value - row.StartInvert) < 1e-8 &&
                        Math.Abs(c.StopInvert.Value - row.StopInvert) < 1e-8 &&
                        Math.Abs(c.Length.Value - row.Length) < 1e-8 &&
                        Math.Abs(c.EffectiveSectionHeight.Value - row.Diameter) < 1e-8, "P12_CSV_MISMATCH:" + row.Id);
                }
                Directory.CreateDirectory(outputDir);
                phase = "WORKSPACE_DRAFT_AND_ROUTING";
                var design = new EngineeringDesignWorkspace("TEST_ONLY_FARZANEGAN_P13_P14_WORKSPACE", ModelSha, raw);
                design.NewDraft("TEST_ONLY_SOURCE_OBSERVED_20261028");
                var routeContext = design.CurrentAutomaticProposalContext(GravityAutomaticPreparationService.PolicyId,
                    GravityAutomaticPreparationService.PolicyVersion);
                var route = design.PrepareAutomaticDesign(GravityAutomaticPreparationService.PolicyId,
                    GravityAutomaticPreparationService.PolicyVersion, routeContext);
                Check(route.DerivedOrientations.Count == 71 && design.Constraints.Orientations.Count == 71, "DRAFT_ROUTING");
                foreach (var row in rows)
                {
                    var o = design.Constraints.Orientations[row.Id.ToString(CultureInfo.InvariantCulture)];
                    Check(o.FromNodeId == row.From && o.ToNodeId == row.To, "ROUTING_MISMATCH:" + row.Id);
                }
                phase = "WORKSPACE_TYPED_CONSTRAINTS";
                foreach (var row in rows)
                {
                    design.PutConstraint(new VerticalSlopeConstraint(row.Id, row.MinSlope, row.MaxSlope,
                        Observation("slope." + row.Id)));
                    foreach (var kind in new[] { ConduitEndKind.Start, ConduitEndKind.Stop })
                    {
                        int nid = kind == ConduitEndKind.Start ? row.Start : row.Stop;
                        Check(nodes[nid].GroundElevation.HasValue && Feet.Equals(nodes[nid].GroundElevationUnit),
                            "NATIVE_GROUND_UNAVAILABLE:" + nid);
                        design.PutConstraint(new VerticalCoverConstraint(design.End(row.Id, kind),
                            Ft(nodes[nid].GroundElevation.Value), Ft(row.MinCover),
                            Observation("cover." + row.Id + "." + kind)));
                    }
                }
                Check(design.Constraints.SlopeConstraints.Count == 71 &&
                    design.Constraints.CoverConstraints.Count == 142, "TYPED_CONSTRAINT_COVERAGE");
                phase = "P13_NEGATIVE_OUTFALL_GATE";
                var missing = design.Assess();
                Check(missing.Status == VerticalFeasibilityStatus.EngineeringDecisionRequired &&
                    missing.DecisionRequests.Count == 2 && !design.CanGenerate, "MISSING_OUTFALL_GATE");
                ExpectGate(() => design.GenerateCandidates(), "P13_GENERATION_GATE");
                Save(outputDir, "p13-missing-outfalls.json", new {
                    Status = missing.Status.ToString(), DecisionRequests = missing.DecisionRequests.Count,
                    GenerationRejected = true
                });
                phase = "P13_CONNECTED_OUTFALL_BOUNDARIES";
                foreach (int id in new[] { 91, 163 })
                {
                    var n = nodes[id];
                    Check(n.RuntimeCategory == "Outfall" && n.InvertElevation.HasValue &&
                        n.PropertyClassifications["InvertElevation"] == "PROVEN", "OUTFALL_NATIVE_INVERT:" + id);
                    var c = raw.Conduits.Single(x => x.StartNodeId == id || x.StopNodeId == id);
                    var kind = c.StartNodeId == id ? ConduitEndKind.Start : ConduitEndKind.Stop;
                    double invert = kind == ConduitEndKind.Start ? c.StartInvert.Value : c.StopInvert.Value;
                    Check(Math.Abs(invert - n.InvertElevation.Value) < 1e-8, "OUTFALL_END_INVERT_MISMATCH:" + id);
                    var endpoint = new FixedEndpointElevationConstraint(design.End(c.Id, kind), Ft(invert),
                        Observation("outfall.endpoint." + id), "TEST_ONLY_CONNECTED_ENDPOINT");
                    design.PutConstraint(new OutfallVerticalConstraint(id, Observation("outfall." + id),
                        protectedInvert: null, connectedEndpointConstraint: endpoint));
                }
                var feasible = design.Assess();
                Check(feasible.Status == VerticalFeasibilityStatus.FeasibleMultipleSolutions &&
                    feasible.Conflicts.Count == 0 && feasible.DecisionRequests.Count == 0 &&
                    feasible.IncompleteItems.Count == 0 && design.CanGenerate, "P13_SOURCE_WHATIF_FEASIBILITY");
                foreach (int id in new[] { 91, 163 })
                {
                    var c = raw.Conduits.Single(x => x.StartNodeId == id || x.StopNodeId == id);
                    var kind = c.StartNodeId == id ? ConduitEndKind.Start : ConduitEndKind.Stop;
                    var end = design.End(c.Id, kind);
                    double invert = kind == ConduitEndKind.Start ? c.StartInvert.Value : c.StopInvert.Value;
                    Check(feasible.EndpointRanges[end.ToString()].ExactInvert.HasValue &&
                        Math.Abs(feasible.EndpointRanges[end.ToString()].ExactInvert.Value - invert) < 1e-8,
                        "P13_CONNECTED_OUTFALL_NUMERIC_NOT_ENFORCED:" + id);
                }
                Save(outputDir, "p13-test-only-feasibility.json", new {
                    Status = feasible.Status.ToString(), ConflictCount = feasible.Conflicts.Count,
                    DecisionCount = feasible.DecisionRequests.Count, IncompleteCount = feasible.IncompleteItems.Count,
                    ConnectedOutfallExactInvertsEnforced = true, SlopeConstraints = design.Constraints.SlopeConstraints.Count,
                    CoverConstraints = design.Constraints.CoverConstraints.Count, OutfallConstraints = design.Constraints.OutfallConstraints.Count,
                    Fingerprint = feasible.ConstraintFingerprint, Authority = "TEST_ONLY_SOURCE_OBSERVATION"
                });
                phase = "P14_EXPLICIT_RECIPE_WORKSPACE";
                var proposed = new Dictionary<string, double>(StringComparer.Ordinal);
                Func<int, ConduitEndKind, string> key = (id, kind) => id + ":" + kind;
                foreach (var row in rows)
                {
                    proposed[key(row.Id, ConduitEndKind.Start)] = row.StartInvert;
                    proposed[key(row.Id, ConduitEndKind.Stop)] = row.StopInvert;
                    bool forward = row.From == row.Start;
                    var from = forward ? ConduitEndKind.Start : ConduitEndKind.Stop;
                    var to = forward ? ConduitEndKind.Stop : ConduitEndKind.Start;
                    double deficit = proposed[key(row.Id, to)] + row.MinSlope * row.Length - proposed[key(row.Id, from)];
                    if (deficit > 1e-8)
                        proposed[key(row.Id, from)] = proposed[key(row.Id, to)] + row.MinSlope * row.Length + 1e-7;
                }
                var selections = new List<CandidateEndpointSelection>();
                foreach (var row in rows)
                    foreach (var kind in new[] { ConduitEndKind.Start, ConduitEndKind.Stop })
                        selections.Add(new CandidateEndpointSelection(design.End(row.Id, kind),
                            EndpointSelectionMode.ExplicitValue, Observation("recipe." + row.Id + "." + kind),
                            Ft(proposed[key(row.Id, kind)])));
                design.SaveRecipe(new GravityCandidateRecipe("TEST_ONLY_FARZANEGAN_SOURCE_LOCAL_SLOPE",
                    selections, Observation("recipe"), "Unreviewed model-derived geometry"));
                Check(design.Feasibility == null && design.Candidates == null, "RECIPE_INVALIDATES_P13");
                feasible = design.Assess();
                var generated = design.GenerateCandidates();
                Check(generated.Candidates.Count == 1 && generated.RecipeResults.Count == 1 &&
                    generated.RecipeResults[0].Status == CandidateGenerationStatus.Generated,
                    "P14_WORKSPACE_GENERATION");
                var candidate = generated.Candidates.Single();
                GravityCandidateExecutor.ValidateLineage(candidate,
                    new GravityNetworkIngestionService().Ingest(raw, ModelSha, schemaVersion: 2).NetworkFingerprint,
                    design.Constraints.VerticalConstraintFingerprint);
                Check(candidate.EndpointDesigns.Count == 142 && candidate.DerivedConduits.Count == 71 &&
                    candidate.EndpointDesigns.All(e => Math.Abs(e.ProposedInvert - proposed[key(e.ConduitId, e.End)]) < 1e-8),
                    "P14_ENDPOINTS");
                var changed = candidate.EndpointDesigns.Where(e =>
                    Math.Abs(e.ProposedInvert - e.ExistingInvert.Value) > 1e-8).ToArray();
                Check(changed.Length == 4 && changed.Select(e => e.ConduitId).OrderBy(x => x)
                    .SequenceEqual(new[] { 144, 154, 160, 242 }), "P14_CHANGED_SET");
                foreach (int id in new[] { 91, 163 })
                {
                    var c = raw.Conduits.Single(x => x.StartNodeId == id || x.StopNodeId == id);
                    var kind = c.StartNodeId == id ? ConduitEndKind.Start : ConduitEndKind.Stop;
                    var e = candidate.EndpointDesigns.Single(x => x.ConduitId == c.Id && x.End == kind);
                    Check(Math.Abs(e.ProposedInvert - nodes[id].InvertElevation.Value) < 1e-8,
                        "P14_OUTFALL_ENDPOINT_CHANGED:" + id);
                }
                phase = "P14_NEGATIVE_BOUNDARY_AND_READINESS";
                var c91 = raw.Conduits.Single(x => x.StartNodeId == 91 || x.StopNodeId == 91);
                var k91 = c91.StartNodeId == 91 ? ConduitEndKind.Start : ConduitEndKind.Stop;
                var illegal = new GravityCandidateRecipe("TEST_ONLY_ILLEGAL_OUTFALL_CHANGE",
                    new[] { new CandidateEndpointSelection(design.End(c91.Id, k91),
                        EndpointSelectionMode.ExplicitValue, Observation("negative.outfall"),
                        Ft(nodes[91].InvertElevation.Value + 0.5)) }, Observation("negative.recipe"));
                var rejected = new GravityCandidateGenerator().Generate(
                    new GravityNetworkIngestionService().Ingest(raw, ModelSha, schemaVersion: 2),
                    design.Constraints, feasible,
                    new GravityCandidateGenerationRequest(design.Constraints.VerticalConstraintFingerprint,
                        new[] { illegal }));
                Check(rejected.Candidates.Count == 0 && rejected.RecipeResults.Count == 1 &&
                    rejected.RecipeResults[0].Status == CandidateGenerationStatus.Rejected,
                    "P14_ILLEGAL_OUTFALL_NOT_REJECTED");
                var readiness = design.Readiness();
                Check(design.Basis.CommonDesignBasis.ReviewState == DesignBasisReviewState.Draft &&
                    !readiness.ProductionExecutionEligible && design.Inputs.Approval == null &&
                    readiness.BlockingReasons.Any(b => b.Code == "DESIGN_BASIS_UNREVIEWED"),
                    "UNREVIEWED_DESIGN_EXECUTION_GATE");
                Save(outputDir, "p14-test-only-workspace-candidate.json", new {
                    candidate.CandidateId, candidate.CandidateFingerprint, candidate.RecipeId,
                    candidate.EndpointDesigns, candidate.DerivedConduits, ChangedConduits = changed.Select(x => x.ConduitId).ToArray(),
                    Authority = "TEST_ONLY_NOT_ENGINEERING_APPROVED_NOT_FOR_EXECUTION"
                });
                Save(outputDir, "readiness.json", new {
                    readiness.DesignBasisReviewStatus, readiness.ProductionExecutionEligible,
                    readiness.P13FeasibilityStatus, readiness.P14CandidateAvailable,
                    Blockers = readiness.BlockingReasons.Select(b => b.Code).ToArray(),
                    IllegalOutfallRecipeRejected = true
                });
                Save(outputDir, "verdict.json", new {
                    Status = "WORKSPACE_P13_P14_FUNCTIONAL_TEST_ONLY_PASS",
                    SourceConduits = 71, SourceNodes = 73, OrientationCount = 71,
                    P13 = feasible.Status.ToString(), P14CandidateCount = generated.Candidates.Count,
                    ChangedConduitIds = changed.Select(x => x.ConduitId).OrderBy(x => x).ToArray(),
                    ConnectedOutfallNumericConstraintsEnforced = true, IllegalOutfallCandidateRejected = true,
                    SourceGroundReviewed = false, NativeDesignSettingsProven = 0,
                    DesignBasisReviewed = false, EngineeringApproved = false,
                    ProductionP13P14Accepted = false, ProductionExecutionEligible = false,
                    SolverCalls = 0, ModelSaves = 0
                });
                Console.WriteLine("WORKSPACE_P13=" + feasible.Status + " P14=1 CHANGED=144,154,160,242");
                Console.WriteLine("CONNECTED_OUTFALL_FIXED=2 ILLEGAL_OUTFALL_REJECTED=TRUE");
                Console.WriteLine("WORKSPACE_FUNCTIONAL_TEST_ONLY_PASS;PRODUCTION_P13_P14_NOT_ACCEPTED");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WORKSPACE_P13_P14_BLOCKED_PHASE=" + phase + " " + ex.GetType().Name + ":" + ex.Message);
                if (Directory.Exists(outputDir))
                    Save(outputDir, "blocked.json", new { Phase = phase, Error = ex.ToString(), Status = "NOT_ACCEPTED" });
                return 2;
            }
            finally
            {
                if (files != null && before != null && Directory.Exists(outputDir))
                {
                    var after = files.Select(f => Path.GetFileName(f) + "|" + Sha(f)).ToArray();
                    Save(outputDir, "source-integrity.json", new {
                        Unchanged = before.SequenceEqual(after), SourceFiles = files.Length,
                        NoModelSave = true, NoSolver = true
                    });
                    Console.WriteLine("SOURCE_FAMILY_UNCHANGED=" + before.SequenceEqual(after));
                }
            }
        }
    }
}
