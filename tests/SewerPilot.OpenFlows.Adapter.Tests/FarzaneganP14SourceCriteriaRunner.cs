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
    // OFFLINE, TEST_ONLY: source SQLite design settings are observations, NOT native-authorized design policy.
    // All engineering decisions are isolated hypothetical constraints. Never execute or save a Bentley model.
    internal static class FarzaneganP14SourceCriteriaRunner
    {
        private const string ModelSha = "CCF07288020064F25F9125224CF6CEF0FBD3085D87E85DE173FB3D2BBC652E5B";
        private const string SqliteSha = "A8D7963461B7A16F8EE9FD9EC42A1F412257BDC6A220510840FB095A5B5BDB0A";
        private const string P12Sha = "74FB2CA66A9E2249EB0683E878272B5CFA272EF29C18BCAF065148D4C8A95C7F";
        private static readonly SourceUnitIdentity Feet = new SourceUnitIdentity("length", "Feet", "feet", "ft");
        private sealed class Local
        {
            public int Id, StartNode, StopNode, FromNode, ToNode, DesignFlag, LocalEnabled;
            public double Length, Start, Stop, Diameter, MinSlope, MaxSlope, MinCover, MaxCover, MinVelocity, MaxVelocity;
        }
        private static void Check(bool pass, string code) { if (!pass) throw new InvalidDataException(code); }
        private static double D(string value) => double.Parse(value, CultureInfo.InvariantCulture);
        private static int I(string value) => int.Parse(value, CultureInfo.InvariantCulture);
        private static SourceEngineeringValue F(double value) => new SourceEngineeringValue(value, Feet);
        private static StandardProvenance SourceObservation(string key) => new StandardProvenance {
            SourceLayer = StandardSourceLayer.ApplicationDefault,
            StandardId = "TEST_ONLY_SQLITE_MODEL_DESIGN_OBSERVATION",
            StandardName = "Source SQLite local settings; native DesignModelSettings 0/852 PROVEN",
            StandardVersion = "1",
            ParameterKey = "test_only.p14.source." + key,
            DocumentReference = "20261028-design-intake/active-conduits-model-derived-proposals.csv",
            Notes = "HYPOTHETICAL_NOT_ENGINEERING_REVIEWED;SOURCE_SQLITE_NOT_NATIVE_POLICY;NO_EXECUTION_AUTHORITY"
        };
        private static string Sha(string path)
        {
            using (var f = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
        }
        private static void Save(string dir, string name, object value) =>
            File.WriteAllText(Path.Combine(dir, name), GravityExecutionPlanBuilder.Json(value), new UTF8Encoding(false));
        private static Local[] Csv(string path)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            Check(lines.Length == 72, "EXPECTED_71_ACTIVE_CONDUITS_IN_CSV");
            var columns = lines[0].Split(',').Select((name, index) => new { name, index })
                .ToDictionary(x => x.name.Trim().TrimStart('\uFEFF'), x => x.index, StringComparer.Ordinal);
            Func<string[], string, string> get = (row, key) => row[columns[key]].Trim().Trim('"');
            var list = new List<Local>();
            foreach (var line in lines.Skip(1))
            {
                var row = line.Split(',');
                Check(row.Length == columns.Count, "UNEXPECTED_CSV_SCHEMA_OR_QUOTING");
                list.Add(new Local {
                    Id = I(get(row, "conduit_id")), StartNode = I(get(row, "start_node")),
                    StopNode = I(get(row, "stop_node")), FromNode = I(get(row, "proposal_from_node")),
                    ToNode = I(get(row, "proposal_to_node")), Length = D(get(row, "source_length_ft")),
                    Start = D(get(row, "source_start_invert_ft")), Stop = D(get(row, "source_stop_invert_ft")),
                    Diameter = D(get(row, "source_diameter_ft")),
                    MinSlope = D(get(row, "model_local_min_slope")), MaxSlope = D(get(row, "model_local_max_slope")),
                    MinCover = D(get(row, "model_local_min_cover_ft")), MaxCover = D(get(row, "model_local_max_cover_ft")),
                    MinVelocity = D(get(row, "model_local_min_velocity_ft_per_s")),
                    MaxVelocity = D(get(row, "model_local_max_velocity_ft_per_s")),
                    DesignFlag = I(get(row, "model_design_conduit_flag")),
                    LocalEnabled = I(get(row, "model_local_constraint_enabled"))
                });
            }
            Check(list.Select(x => x.Id).Distinct().Count() == 71 && list.All(x =>
                x.LocalEnabled == 1 && x.Length > 0 && x.Diameter > 0 && x.MinSlope > 0 &&
                x.MinSlope <= x.MaxSlope && x.MinCover > 0 && x.MinCover <= x.MaxCover &&
                x.MinVelocity > 0 && x.MinVelocity <= x.MaxVelocity), "LOCAL_DESIGN_DATA_INVALID");
            return list.OrderBy(x => x.Id).ToArray();
        }
        internal static int Run(string acceptedDir, string intakeDir, string outputDir)
        {
            string phase = "PREFLIGHT", p12 = null, sqlite = null, p12Before = null, sqliteBefore = null;
            try
            {
                acceptedDir = Path.GetFullPath(acceptedDir); intakeDir = Path.GetFullPath(intakeDir);
                outputDir = Path.GetFullPath(outputDir);
                Check(!Directory.Exists(outputDir), "OUTPUT_ALREADY_EXISTS");
                p12 = Path.Combine(acceptedDir, "p12-native-read.json");
                sqlite = Path.GetFullPath(Path.Combine(intakeDir, "..", "..", "..", "engineering",
                    "customer-data", "Farzanegan", "SourceCopy_20261027", "Model-SewerGEM",
                    "FEP-AOC-030826.stsw.sqlite"));
                // Resolve relative to repository root instead: intakeDir is <repo>/docs/evidence/farzanegan/<phase>.
                var repo = Directory.GetParent(Directory.GetParent(Directory.GetParent(
                    Directory.GetParent(intakeDir).FullName).FullName).FullName).FullName;
                sqlite = Path.Combine(repo, "engineering", "customer-data", "Farzanegan",
                    "SourceCopy_20261027", "Model-SewerGEM", "FEP-AOC-030826.stsw.sqlite");
                var csv = Path.Combine(intakeDir, "active-conduits-model-derived-proposals.csv");
                var intake = Path.Combine(intakeDir, "model-design-intake.json");
                var statusPath = Path.Combine(acceptedDir, "native-read-status.json");
                Check(File.Exists(p12) && File.Exists(sqlite) && File.Exists(csv) &&
                    File.Exists(intake) && File.Exists(statusPath), "SOURCE_AND_P12_EVIDENCE_REQUIRED");
                p12Before = Sha(p12); sqliteBefore = Sha(sqlite);
                Check(p12Before == P12Sha && sqliteBefore == SqliteSha &&
                    File.ReadAllText(intake).Contains(SqliteSha), "SOURCE_HASH_OR_INTAKE_IDENTITY_MISMATCH");
                var status = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(statusPath));
                Check((bool)status["Accepted"] && (bool)status["SourceIntegrity"] &&
                    (bool)status["WorkingIntegrity"] && (bool)status["CloseCompleted"] &&
                    Convert.ToInt32(status["SaveCalls"]) == 0 && Convert.ToString(status["ModelFingerprint"]) == ModelSha,
                    "NATIVE_READ_ACCEPTANCE_INVALID");
                var raw = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(File.ReadAllText(p12));
                var local = Csv(csv);
                Check(raw.Conduits.Count == 71 && raw.Structures.Count == 73 &&
                    raw.ScenarioContext.ActiveScenarioId == 1, "REAL_MODEL_INVENTORY_INVALID");
                var nodes = raw.Structures.ToDictionary(x => x.Id);
                foreach (var row in local)
                {
                    var native = raw.Conduits.Single(x => x.Id == row.Id);
                    Check(native.StartNodeId == row.StartNode && native.StopNodeId == row.StopNode &&
                        native.Length.HasValue && Math.Abs(native.Length.Value - row.Length) < 1e-8 &&
                        Math.Abs(native.StartInvert.Value - row.Start) < 1e-8 &&
                        Math.Abs(native.StopInvert.Value - row.Stop) < 1e-8 &&
                        Math.Abs(native.EffectiveSectionHeight.Value - row.Diameter) < 1e-8 &&
                        Feet.Equals(native.LengthUnit) && Feet.Equals(native.StartInvertUnit) &&
                        Feet.Equals(native.StopInvertUnit) &&
                        native.PropertyClassifications["StartInvert"] == "PROVEN" &&
                        native.PropertyClassifications["StopInvert"] == "PROVEN",
                        "SQLITE_LOCAL_ROW_NATIVE_GEOMETRY_MISMATCH_" + row.Id);
                }
                var network = new GravityNetworkIngestionService().Ingest(raw, ModelSha, schemaVersion: 2);
                var empty = new GravityVerticalDesignConstraintSetBuilder().Build(network.NetworkFingerprint);
                var policy = new GravityAutomaticPreparationService();
                var route = policy.Prepare(network, empty, GravityAutomaticPreparationService.PolicyId,
                    GravityAutomaticPreparationService.PolicyVersion,
                    new GravityAutomaticPreparationContext(ModelSha, network.NetworkFingerprint,
                        network.ScenarioContext, GravityAutomaticPreparationService.PolicyId,
                        GravityAutomaticPreparationService.PolicyVersion, empty.VerticalConstraintFingerprint));
                Check(route.ContextFailures.Count == 0 && route.Components.All(x => x.Eligible) &&
                    route.DerivedOrientations.Count == 71, "SOURCE_ROUTING_NOT_ELIGIBLE");
                var orientations = route.DerivedOrientations.ToDictionary(x => x.ConduitId);
                foreach (var row in local)
                    Check(orientations[row.Id].FromNodeId == row.FromNode &&
                        orientations[row.Id].ToNodeId == row.ToNode, "SQLITE_NATIVE_ROUTING_MISMATCH_" + row.Id);
                phase = "P13_SOURCE_CRITERIA_HYPOTHESIS";
                var builder = new GravityVerticalDesignConstraintSetBuilder();
                foreach (var o in route.DerivedOrientations)
                    builder.SetOrientation(o.ConduitId, o.FromNodeId, o.ToNodeId, o.Provenance);
                var outfalls = raw.Structures.Where(x => x.RuntimeCategory == "Outfall").OrderBy(x => x.Id).ToArray();
                Check(outfalls.Select(x => x.Id).SequenceEqual(new[] { 91, 163 }), "OUTFALL_ID_MISMATCH");
                foreach (var outfall in outfalls)
                {
                    Check(outfall.InvertElevation.HasValue && Feet.Equals(outfall.InvertElevationUnit) &&
                        outfall.PropertyClassifications["InvertElevation"] == "PROVEN", "OUTFALL_NATIVE_ELEVATION_NOT_PROVEN");
                    var conduit = raw.Conduits.Single(c => c.StartNodeId == outfall.Id || c.StopNodeId == outfall.Id);
                    bool atStart = conduit.StartNodeId == outfall.Id;
                    var end = new GravityConduitEnd(conduit.Id, atStart ? ConduitEndKind.Start : ConduitEndKind.Stop, outfall.Id);
                    var nativeInvert = atStart ? conduit.StartInvert.Value : conduit.StopInvert.Value;
                    Check(Math.Abs(nativeInvert - outfall.InvertElevation.Value) < 1e-8, "OUTFALL_PIPE_END_MISMATCH");
                    var fixedEnd = new FixedEndpointElevationConstraint(end, F(nativeInvert),
                        SourceObservation("outfall.endpoint." + outfall.Id), "TEST_ONLY source observed outlet");
                    builder.AddOutfallConstraint(outfall.Id, SourceObservation("outfall." + outfall.Id),
                        F(outfall.InvertElevation.Value), connectedEnd: fixedEnd);
                }
                var originalCoverViolations = new List<string>();
                var changes = new List<object>();
                var proposals = new Dictionary<string, double>(StringComparer.Ordinal);
                Func<int, ConduitEndKind, string> key = (id, end) => id + ":" + end;
                foreach (var row in local)
                {
                    var n1 = nodes[row.StartNode]; var n2 = nodes[row.StopNode];
                    Check(n1.GroundElevation.HasValue && n2.GroundElevation.HasValue &&
                        Feet.Equals(n1.GroundElevationUnit) && Feet.Equals(n2.GroundElevationUnit),
                        "NATIVE_GROUND_ELEVATION_MISSING_" + row.Id);
                    builder.AddSlopeConstraint(row.Id, row.MinSlope, row.MaxSlope, SourceObservation("slope." + row.Id));
                    foreach (var e in new[] { ConduitEndKind.Start, ConduitEndKind.Stop })
                    {
                        int node = e == ConduitEndKind.Start ? row.StartNode : row.StopNode;
                        var native = nodes[node];
                        var end = new GravityConduitEnd(row.Id, e, node);
                        builder.AddCoverConstraint(end, F(native.GroundElevation.Value), F(row.MinCover),
                            SourceObservation("cover." + row.Id + "." + e));
                        double original = e == ConduitEndKind.Start ? row.Start : row.Stop;
                        double cover = native.GroundElevation.Value - original - row.Diameter;
                        if (cover < row.MinCover - 1e-4 || cover > row.MaxCover + 1e-4)
                            originalCoverViolations.Add(row.Id + ":" + e);
                        proposals.Add(key(row.Id, e), original);
                    }
                }
                Check(originalCoverViolations.Count == 0, "ORIGINAL_MODEL_COVER_CONFLICT");
                var constraints = builder.Build(network.NetworkFingerprint);
                var p13 = new GravityVerticalFeasibilityEngine().Analyze(network, constraints);
                Directory.CreateDirectory(outputDir);
                Save(outputDir, "p13-local-criteria.json", new {
                    Status = p13.Status.ToString(), p13.Conflicts, p13.DecisionRequests,
                    p13.IncompleteItems, ConstraintFingerprint = constraints.VerticalConstraintFingerprint,
                    SlopeConstraints = constraints.SlopeConstraints.Count, CoverConstraints = constraints.CoverConstraints.Count,
                    OutfallConstraints = constraints.OutfallConstraints.Count,
                    Authority = "HYPOTHETICAL_SQLITE_SOURCE_SETTINGS_NOT_REVIEWED"
                });
                Check(p13.Status == VerticalFeasibilityStatus.FeasibleMultipleSolutions &&
                    p13.Conflicts.Count == 0 && p13.DecisionRequests.Count == 0,
                    "P13_LOCAL_CRITERIA_NOT_FEASIBLE");
                phase = "P14_CHANGED_PROFILE";
                foreach (var row in local)
                {
                    bool fromStart = row.FromNode == row.StartNode;
                    var fromEnd = fromStart ? ConduitEndKind.Start : ConduitEndKind.Stop;
                    var toEnd = fromStart ? ConduitEndKind.Stop : ConduitEndKind.Start;
                    double existingFrom = proposals[key(row.Id, fromEnd)];
                    double existingTo = proposals[key(row.Id, toEnd)];
                    double deficit = existingTo + row.MinSlope * row.Length - existingFrom;
                    if (deficit > 1e-8)
                    {
                        double proposed = existingTo + row.MinSlope * row.Length + 1e-7;
                        proposals[key(row.Id, fromEnd)] = proposed;
                        changes.Add(new {
                            ConduitId = row.Id, End = fromEnd.ToString(), NodeId = row.FromNode,
                            OriginalInvertFt = existingFrom, ProposedInvertFt = proposed,
                            ChangeFt = proposed - existingFrom, MinSlope = row.MinSlope,
                            OriginalSlope = (existingFrom - existingTo) / row.Length,
                            ProposedSlope = (proposed - existingTo) / row.Length,
                            Source = "SOURCE_SQLITE_LOCAL_SLOPE_HYPOTHESIS_NOT_ENGINEERING_APPROVED"
                        });
                    }
                }
                var selections = new List<CandidateEndpointSelection>();
                foreach (var row in local)
                    foreach (var end in new[] { ConduitEndKind.Start, ConduitEndKind.Stop })
                    {
                        int node = end == ConduitEndKind.Start ? row.StartNode : row.StopNode;
                        selections.Add(new CandidateEndpointSelection(new GravityConduitEnd(row.Id, end, node),
                            EndpointSelectionMode.ExplicitValue, SourceObservation("recipe." + row.Id + "." + end),
                            F(proposals[key(row.Id, end)])));
                    }
                var recipe = new GravityCandidateRecipe("TEST_ONLY_FARZANEGAN_LOCAL_SLOPE_MINIMAL_UPSTREAM",
                    selections, SourceObservation("recipe.minimal_upstream"), "Non-authoritative local slope what-if");
                var result = new GravityCandidateGenerator().Generate(network, constraints, p13,
                    new GravityCandidateGenerationRequest(constraints.VerticalConstraintFingerprint, new[] { recipe }));
                Save(outputDir, "p14-recipe-results.json", new {
                    Results = result.RecipeResults.Select(x => new { x.RecipeId, Status = x.Status.ToString(), x.Issues }),
                    CandidateCount = result.Candidates.Count
                });
                Check(result.Candidates.Count == 1 && result.RecipeResults.Count == 1 &&
                    result.RecipeResults[0].Status == CandidateGenerationStatus.Generated,
                    "P14_REJECTED_LOCAL_SLOPE_PROFILE");
                var candidate = result.Candidates.Single();
                GravityCandidateExecutor.ValidateLineage(candidate, network.NetworkFingerprint,
                    constraints.VerticalConstraintFingerprint);
                Check(candidate.EndpointDesigns.Count == 142 && candidate.DerivedConduits.Count == 71 &&
                    candidate.EndpointDesigns.All(e => Math.Abs(e.ProposedInvert - proposals[key(e.ConduitId, e.End)]) < 1e-8),
                    "P14_GENERATED_ENDPOINTS_MISMATCH");
                var violations = new List<string>();
                foreach (var row in local)
                {
                    bool fromStart = row.FromNode == row.StartNode;
                    double from = proposals[key(row.Id, fromStart ? ConduitEndKind.Start : ConduitEndKind.Stop)];
                    double to = proposals[key(row.Id, fromStart ? ConduitEndKind.Stop : ConduitEndKind.Start)];
                    double slope = (from - to) / row.Length;
                    if (slope < row.MinSlope - 1e-9 || slope > row.MaxSlope + 1e-9)
                        violations.Add("SLOPE:" + row.Id);
                    foreach (var end in new[] { ConduitEndKind.Start, ConduitEndKind.Stop })
                    {
                        var node = nodes[end == ConduitEndKind.Start ? row.StartNode : row.StopNode];
                        double cover = node.GroundElevation.Value - proposals[key(row.Id, end)] - row.Diameter;
                        if (cover < row.MinCover - 1e-6 || cover > row.MaxCover + 1e-6)
                            violations.Add("COVER:" + row.Id + ":" + end);
                    }
                }
                Check(violations.Count == 0, "P14_INDEPENDENT_SLOPE_COVER_VALIDATION_FAILED");
                Check(changes.Count == 4 && candidate.EndpointDesigns.Count(e =>
                    Math.Abs(e.ProposedInvert - e.ExistingInvert.Value) > 1e-8) == 4,
                    "UNEXPECTED_MODIFIED_ENDPOINT_SET");
                Save(outputDir, "source-local-criteria.json", new {
                    SourceSqliteSha256 = sqliteBefore, SourceP12Sha256 = p12Before,
                    LocalSlopeCount = local.Length, LocalCoverEndpointCount = 142,
                    SourceSystemMinSlope = 0.005, LocalMinSlope = 0.003,
                    SourceLocalSlopeMin = local.Min(x => x.MinSlope),
                    SourceLocalSlopeMax = local.Max(x => x.MinSlope),
                    SourceLocalCoverMinFt = local.Min(x => x.MinCover),
                    SourceLocalCoverMaxFt = local.Max(x => x.MinCover),
                    DesignFlaggedConduits = local.Count(x => x.DesignFlag == 1),
                    NativeDesignSettingsProven = 0, NativeDesignSettingsUnavailable = 852,
                    NativeGeometryCompared = 71, NativeGeometryMismatches = 0,
                    OriginalCoverViolations = originalCoverViolations.Count,
                    Authority = "SQLITE_SOURCE_CONFIGURATION_ONLY;NOT_NATIVE_DESIGN_FIELD_PROOF;NOT_ADOPTED_STANDARD"
                });
                Save(outputDir, "changed-endpoints.json", changes);
                Save(outputDir, "p14-modified-test-only-candidate.json", new {
                    candidate.CandidateId, candidate.RecipeId, candidate.CandidateFingerprint,
                    candidate.SourceNetworkFingerprint, candidate.VerticalConstraintFingerprint,
                    candidate.EndpointDesigns, candidate.DerivedConduits,
                    Authority = "TEST_ONLY_UNREVIEWED_SOURCE_CRITERIA_NOT_FOR_EXECUTION"
                });
                Save(outputDir, "verdict.json", new {
                    Status = "P14_REAL_MODEL_MODIFIED_HYPOTHETICAL_CANDIDATE_GENERATED",
                    P13Status = p13.Status.ToString(), P14CandidateCount = 1,
                    ChangedConduitCount = changes.Count, ChangedEndpointCount = 4,
                    ChangedConduitIds = new[] { 144, 154, 160, 242 },
                    NativeGeometryReconciled = 71, SlopeConstraints = 71, CoverConstraints = 142,
                    SourceModelSha256 = ModelSha, SourceSqliteSha256 = sqliteBefore,
                    NativeDesignSettingsProven = 0, SourceGroundQualityUnreviewed = true,
                    ProductionP14Accepted = false, DesignBasisApproved = false, OutfallDecisionApproved = false,
                    CandidateSelected = false, SolverInvocations = 0, ModelSaveInvocations = 0
                });
                Console.WriteLine("P13=" + p13.Status + " P14=1 CHANGED_ENDPOINTS=4 CONDUITS=144,154,160,242");
                Console.WriteLine("LOCAL_SLOPE=0.003 MIN_COVER=SOURCE_LOCAL P12_GEOMETRY_RECONCILED=71");
                Console.WriteLine("PRODUCTION_P14=NOT_ACCEPTED; NATIVE_DESIGN_SETTINGS_PROVEN=0");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("P14_SOURCE_CRITERIA_BLOCKED_PHASE=" + phase + " " + ex.GetType().Name + ":" + ex.Message);
                if (Directory.Exists(outputDir))
                    Save(outputDir, "blocked.json", new { Phase = phase, Error = ex.ToString(), Status = "NOT_ACCEPTED" });
                return 2;
            }
            finally
            {
                if (Directory.Exists(outputDir))
                    Save(outputDir, "source-integrity.json", new {
                        P12Unchanged = p12Before != null && File.Exists(p12) && Sha(p12) == p12Before,
                        SqliteUnchanged = sqliteBefore != null && File.Exists(sqlite) && Sha(sqlite) == sqliteBefore,
                        SourceP12Sha256 = p12Before, SourceSqliteSha256 = sqliteBefore
                    });
            }
        }
    }
}
