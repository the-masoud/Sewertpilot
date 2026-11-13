using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    // One bounded real Bentley P12 read, followed by purely offline model-derived P13/P14 draft assessment.
    // TEST/PILOT ONLY: no source mutation, no solver, no implicit design approval.
    internal static class FarzaneganDesignIntakeLiveRunner
    {
        private static void Save(string folder, string name, object value)
        {
            File.WriteAllText(Path.Combine(folder, name), GravityExecutionPlanBuilder.Json(value), new UTF8Encoding(false));
        }
        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        internal static int Run(string modelPath, string evidenceDir)
        {
            string[] files = null;
            string[] before = null;
            string phase = "PREFLIGHT";
            try
            {
                modelPath = Path.GetFullPath(modelPath);
                evidenceDir = Path.GetFullPath(evidenceDir);
                if (!File.Exists(modelPath) || !modelPath.EndsWith(".stsw", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("EXACT_STSW_SOURCE_REQUIRED");
                WorkingCopyIntegrity.RejectLinks(modelPath);
                files = P16FarzaneganNativePilotHost.Family.Select(n => Path.Combine(Path.GetDirectoryName(modelPath), n)).ToArray();
                if (files.Any(f => !File.Exists(f))) throw new FileNotFoundException("SEVEN_FILE_MODEL_FAMILY_INCOMPLETE");
                before = files.Select(f => Path.GetFileName(f) + "|" + new FileInfo(f).Length + "|" + Hash(f)).ToArray();
                if (Directory.Exists(evidenceDir)) throw new InvalidOperationException("EVIDENCE_DIRECTORY_ALREADY_EXISTS");
                Directory.CreateDirectory(evidenceDir);
                Save(evidenceDir, "protected-source-before.json", before);
                string modelFingerprint = Hash(modelPath);
                Console.WriteLine("P12_REAL_BENTLEY_READ_ONCE");
                phase = "P12_NATIVE_READ";
                ModelReadResult result;
                using (var reader = new OpenFlowsModelReader())
                    result = reader.ReadWithEvidence(modelPath);
                var ev = result.Evidence;
                var diag = ev.Diagnostics ?? new Dictionary<string, string>();
                Save(evidenceDir, "native-read-status.json", new {
                    ev.Accepted, ev.FailureCode, ev.SourceIntegrity, ev.WorkingIntegrity,
                    ev.CloseCompleted, ev.WorkingCopyPath, SaveCalls = ev.SaveWriteInvocations.Count,
                    ModelFingerprint = modelFingerprint,
                    RuntimeHostExitCode = diag.ContainsKey("RuntimeHostExitCode") ? diag["RuntimeHostExitCode"] : null
                });
                if (!ev.Accepted || ev.SaveWriteInvocations.Count != 0 || !ev.SourceIntegrity || !ev.WorkingIntegrity || !ev.CloseCompleted)
                    throw new InvalidDataException("NATIVE_READ_NOT_ACCEPTED:" + ev.FailureCode);
                if (!diag.TryGetValue("P12ANetworkReadJson", out string rawJson) || string.IsNullOrWhiteSpace(rawJson))
                    throw new InvalidDataException("P12_NETWORK_JSON_NOT_EXPORTED");
                File.WriteAllText(Path.Combine(evidenceDir, "p12-native-read.json"), rawJson, new UTF8Encoding(false));
                var networkRead = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(rawJson);
                if (networkRead.Conduits.Count != 71 || networkRead.Structures.Count != 73 ||
                    networkRead.ScenarioContext.ActiveScenarioId != 1)
                    throw new InvalidDataException("FARZANEGAN_NETWORK_IDENTITY_MISMATCH");
                var design = (networkRead.SourceData?.Parameters ?? new List<NativeSourceParameter>())
                    .Where(p => p.Category == "DesignModelSettings").ToArray();
                Save(evidenceDir, "native-design-observations.json", new {
                    Authority = "NATIVE_CONFIGURATION_ONLY_NO_ENGINEERING_POLICY",
                    Total = design.Length, Proven = design.Count(p => p.SourceClassification == "PROVEN"),
                    Unresolved = design.Count(p => p.SourceClassification != "PROVEN"),
                    PerField = design.GroupBy(p => p.ParameterIdentity).OrderBy(g => g.Key).Select(g => new {
                        Field = g.Key, Count = g.Count(), Proven = g.Count(p => p.SourceClassification == "PROVEN"),
                        AlternativeIds = g.Select(p => p.AlternativeId).Distinct().OrderBy(x => x).ToArray(),
                        Units = g.Where(p => p.Unit != null).Select(p => p.Unit.Symbol).Distinct().ToArray(),
                        UnresolvedReasons = g.Where(p => p.SourceClassification != "PROVEN").Select(p => p.Reason).Distinct().ToArray()
                    }).ToArray(), Parameters = design
                });
                Console.WriteLine("P12_ACCEPTED=TRUE CONDUITS=" + networkRead.Conduits.Count +
                    " STRUCTURES=" + networkRead.Structures.Count + " NATIVE_DESIGN=" + design.Length +
                    " PROVEN=" + design.Count(p => p.SourceClassification == "PROVEN"));
                phase = "MODEL_DERIVED_DRAFT";
                var workspace = new EngineeringDesignWorkspace("FARZANEGAN_REAL_MODEL_READ_ONLY_DRAFT", modelFingerprint, networkRead);
                workspace.NewDraft("MODEL_OBSERVED_DRAFT_20261028_01");
                var context = workspace.CurrentAutomaticProposalContext(GravityAutomaticPreparationService.PolicyId,
                    GravityAutomaticPreparationService.PolicyVersion);
                var preparation = workspace.PrepareAutomaticDesign(GravityAutomaticPreparationService.PolicyId,
                    GravityAutomaticPreparationService.PolicyVersion, context);
                var snapshot = new GravityNetworkIngestionService().Ingest(networkRead, modelFingerprint, schemaVersion: 2);
                Save(evidenceDir, "draft-orientation-and-outfall-proposals.json", new {
                    Status = "MODEL_DERIVED_DRAFT_NOT_APPROVED",
                    Policy = GravityAutomaticPreparationService.PolicyId,
                    PolicyVersion = GravityAutomaticPreparationService.PolicyVersion,
                    NetworkFingerprint = snapshot.NetworkFingerprint,
                    Components = preparation.Components,
                    DerivedOrientationCount = preparation.DerivedOrientations.Count,
                    AppliedDraftOrientationCount = workspace.Constraints.Orientations.Count,
                    SourceOutfalls = snapshot.Nodes.Where(n => n.Kind == GravityNodeKind.Outfall).Select(n => new {
                        Id = n.SourceElementId, n.Label,
                        ObservedInvert = n.ExistingElevationState.InvertElevation,
                        Unit = n.ExistingElevationState.InvertElevationUnit,
                        Authority = "SOURCE_OBSERVATION_ONLY_BOUNDARY_DECISION_REQUIRED"
                    }).ToArray(),
                    ExplicitOutfallConstraints = workspace.Constraints.OutfallConstraints.Count,
                    PreparationOutfalls = preparation.Outfalls
                });
                Save(evidenceDir, "model-derived-draft.json", new {
                    Status = "DRAFT_ONLY_NOT_REVIEWED_NOT_AUTHORIZED",
                    workspace.Inputs.ProjectId, workspace.Inputs.ModelFingerprint,
                    NetworkFingerprint = snapshot.NetworkFingerprint,
                    DesignBasis = workspace.Basis, Constraints = workspace.Constraints,
                    NativeDesignSource = "native-design-observations.json",
                    P12Source = "p12-native-read.json"
                });
                Console.WriteLine("DRAFT_ORIENTATIONS=" + workspace.Constraints.Orientations.Count +
                    " EXPLICIT_OUTFALL_BOUNDARIES=" + workspace.Constraints.OutfallConstraints.Count);
                phase = "P13_REAL_NETWORK_DRAFT_ASSESSMENT";
                var feasibility = workspace.Assess();
                Save(evidenceDir, "p13-real-model-feasibility.json", feasibility);
                Console.WriteLine("P13_STATUS=" + feasibility.Status + " DECISION_REQUESTS=" +
                    feasibility.DecisionRequests.Count + " CONFLICTS=" + feasibility.Conflicts.Count);
                phase = "P14_GATED_PREVIEW";
                if (workspace.CanGenerate)
                {
                    var proposalContext = workspace.CurrentAutomaticProposalContext(
                        GravityVerticalDesignProposalResolver.ObjectiveId, GravityVerticalDesignProposalResolver.ObjectiveVersion);
                    var preview = workspace.PreviewAutomaticProposal(GravityVerticalDesignProposalResolver.ObjectiveId,
                        GravityVerticalDesignProposalResolver.ObjectiveVersion, proposalContext);
                    Save(evidenceDir, "p14-real-model-preview.json", new {
                        preview.Status, preview.ProposalFingerprint, preview.CandidateFingerprint,
                        Authority = "PREVIEW_ONLY_NOT_SELECTED_NOT_APPROVED_NO_VENDOR_WRITE"
                    });
                    Console.WriteLine("P14_PREVIEW_STATUS=" + preview.Status);
                }
                else
                {
                    Save(evidenceDir, "p14-real-model-preview.json", new {
                        Status = "BLOCKED_BY_REAL_P13",
                        P13Status = feasibility.Status.ToString(),
                        MissingOutfallBoundaryDecisions = workspace.Constraints.OutfallConstraints.Count == 0,
                        Authority = "NO_CANDIDATE_GENERATION_NO_SELECTION_NO_APPROVAL_NO_VENDOR_WRITE"
                    });
                    Console.WriteLine("P14_PREVIEW_STATUS=BLOCKED_BY_REAL_P13");
                }
                phase = "COMPLETE";
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("LIVE_DESIGN_INTAKE_BLOCKED_PHASE=" + phase +
                    " ERROR=" + ex.GetType().Name + ":" + ex.Message);
                if (Directory.Exists(evidenceDir))
                    Save(evidenceDir, "blocked.json", new { Phase = phase, ErrorType = ex.GetType().FullName,
                        Error = ex.Message, Status = "NOT_ACCEPTED_NO_RETRY" });
                return 2;
            }
            finally
            {
                if (files != null && before != null)
                {
                    var after = files.Select(f => Path.GetFileName(f) + "|" + new FileInfo(f).Length + "|" + Hash(f)).ToArray();
                    if (Directory.Exists(evidenceDir))
                    {
                        Save(evidenceDir, "protected-source-after.json", after);
                        Save(evidenceDir, "source-integrity.json", new { Unchanged = before.SequenceEqual(after),
                            SourceFamilyFileCount = files.Length, ReadOnly = true, SolverCalls = 0, SaveCalls = 0 });
                    }
                    Console.WriteLine("PROTECTED_SOURCE_UNCHANGED=" + before.SequenceEqual(after));
                }
            }
        }
    }
}
