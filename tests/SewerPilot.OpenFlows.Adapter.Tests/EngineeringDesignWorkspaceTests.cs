using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static void Ui02Reject(Action action,string code)
        {
            try {action();throw new Exception("Expected rejection "+code);}
            catch(InvalidOperationException e){Check(e.Message.Contains(code),"Exact offline rejection: "+code);}
        }
        private static EngineeringDesignWorkspace Ui02Draft()
        {
            var f=ProductionFixture();var d=new EngineeringDesignWorkspace(f.Input.ProjectId,f.Input.ModelFingerprint,f.Input.SourceRead);
            d.NewDraft("TEST_ONLY_DRAFT");return d;
        }
        private static StandardProvenance Ui02Provenance(string key)=>CreateTestProvenance("TEST_ONLY_NON_ENGINEERING",key);
        private static void MvpUi02_TypedBasisAndReview()
        {
            var d=Ui02Draft();var values=new[]{StandardValue.FromNumber(1.25),StandardValue.FromInteger(7),StandardValue.FromBoolean(true),StandardValue.FromText("TEST_ONLY"),StandardValue.FromQuantity(2.5,"ft")};
            int i=0;foreach(GravityFlowSectionIdentifier section in Enum.GetValues(typeof(GravityFlowSectionIdentifier)))
            {
                string key="TEST_ONLY."+section;
                d.PutCriterion(section,new GravityDesignCriterion {Key=key,Section=section,Requirement=GravityCriterionRequirement.Required,
                    Status=GravityCriterionStatus.Defined,Value=values[i++%values.Length],Provenance=Ui02Provenance(key),Rationale="TEST_ONLY_NON_ENGINEERING"});
            }
            var round=GravityFlowDesignBasisSerializer.Deserialize(GravityFlowDesignBasisSerializer.Serialize(d.Basis));
            Check(round.EnumerateSectionsInCanonicalOrder().Count()==7,"All seven native sections editable.");
            foreach(var section in round.EnumerateSectionsInCanonicalOrder())Check(section.Criteria.Count==1&&section.Criteria[0].Rationale=="TEST_ONLY_NON_ENGINEERING"&&section.Criteria[0].Value.Kind==d.Basis.GetSection(section.SectionId).Criteria[0].Value.Kind,"Typed value/provenance/rationale roundtrip "+section.SectionId);
            d.Transition(DesignBasisReviewState.ReadyForReview,"TEST_ONLY operator review");d.Transition(DesignBasisReviewState.NeedsRevision,"TEST_ONLY revision request");d.Transition(DesignBasisReviewState.Draft,"TEST_ONLY revision action");
            d.Transition(DesignBasisReviewState.ReadyForReview,"TEST_ONLY ready");d.Transition(DesignBasisReviewState.Reviewed,"TEST_ONLY operator reviewed");
            Check(d.Basis.CommonDesignBasis.ReviewMetadata.ActorId.Contains(System.Security.Principal.WindowsIdentity.GetCurrent().User.Value)&&!string.IsNullOrWhiteSpace(d.Basis.CommonDesignBasis.ReviewMetadata.ReviewStateChangedUtc),"Actual local operator SID/time recorded through native transition.");
            Ui02Reject(()=>d.SetIdentity("forged","2"),"DRAFT_REQUIRED");
            Ui02Reject(()=>d.NewDraft("TEST_ONLY_DRAFT"),"NEW_VERSION_REQUIRED");d.MarkSaved();d.NewDraft("TEST_ONLY_VERSION_2");
            Check(d.Basis.CommonDesignBasis.ReviewState==DesignBasisReviewState.Draft&&d.Basis.CommonDesignBasis.ReviewMetadata.ActorId==null&&d.Basis.EnumerateSectionsInCanonicalOrder().All(s=>s.Criteria.Count==1),"Explicit new version preserves content and resets review via native draft default.");
        }
        private static void MvpUi02_ValidationAndUnits()
        {
            var d=Ui02Draft();d.PutCriterion(GravityFlowSectionIdentifier.Loading,new GravityDesignCriterion{Key="TEST_ONLY_REQUIRED",Section=GravityFlowSectionIdentifier.Loading,Requirement=GravityCriterionRequirement.Required});
            Check(d.ValidateBasis().Issues.Any(x=>x.Code=="REQUIRED_CRITERION_UNRESOLVED"),"Undefined required criterion remains visibly unresolved.");
            Ui02Reject(()=>d.Transition(DesignBasisReviewState.ReadyForReview,"TEST_ONLY"),"REVIEW_VALIDATION");
            Ui02Reject(()=>d.PutCriterion(GravityFlowSectionIdentifier.Project,new GravityDesignCriterion{Key="TEST_ONLY_DEFINED",Section=GravityFlowSectionIdentifier.Project,Status=GravityCriterionStatus.Defined,Value=StandardValue.FromNumber(1),Provenance=new StandardProvenance()}),"PROVENANCE");
            Ui02Reject(()=>EngineeringDesignWorkspace.Feet("1","m"),"UNSUPPORTED_UNIT");Ui02Reject(()=>EngineeringDesignWorkspace.Feet("NaN","ft"),"FINITE_NUMBER_REQUIRED");
            Ui02Reject(()=>EngineeringDesignWorkspace.Finite("Infinity"),"FINITE_NUMBER_REQUIRED");
            Check(EngineeringDesignWorkspace.Feet("1.234567890123","Feet").NumericValue==1.234567890123,"Native Feet magnitude preserved exactly; no conversion.");
            var c=d.Basis.Loading.Criteria.Single().Clone();c.Requirement=GravityCriterionRequirement.Optional;
            Ui02Reject(()=>d.PutCriterion(GravityFlowSectionIdentifier.Loading,c,c.Key),"EXISTING_CRITERION_IDENTITY_REQUIRED");
        }
        private static void MvpUi02_OrientationAndConstraintMapping()
        {
            var d=Ui02Draft();var raw=d.Inputs.SourceRead;var c=raw.Conduits.First();
            Check(d.Constraints.Orientations.Count==0,"No topology-derived orientation records.");
            d.SetOrientation(c.Id,"Start → Stop",Ui02Provenance("TEST_ONLY.orientation"));var o=d.Constraints.Orientations[c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            Check(o.FromNodeId==c.StartNodeId&&o.ToNodeId==c.StopNodeId,"Explicit Start/Stop mapped to actual endpoints.");
            d.SetOrientation(c.Id,"Stop → Start",Ui02Provenance("TEST_ONLY.orientation.revised"));
            Check(d.Constraints.Orientations.Count==1&&d.Constraints.Orientations[c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)].FromNodeId==c.StopNodeId,"Direction reversal replaces record and prior mirrored criterion.");
            d.SetOrientation(c.Id,"Undecided",null);Check(d.Constraints.Orientations.Count==0&&!d.Basis.VerticalDesign.Criteria.Any(),"Undecided removes decision and its owned binding.");
            Ui02Reject(()=>d.SetOrientation(999999,"Start → Stop",Ui02Provenance("TEST_ONLY.bad")),"ACTUAL_CONDUIT_ENDPOINT_REQUIRED");
            var end=d.End(c.Id,ConduitEndKind.Start);var outfall=raw.Structures.First(s=>s.RuntimeCategory=="Outfall");
            d.PutConstraint(new FixedEndpointElevationConstraint(end,CreateFeetValue(90),Ui02Provenance("TEST_ONLY.fixed"),"TEST_ONLY reason"));
            d.PutConstraint(new ProtectedNodeElevationConstraint(end.NodeId,"Rim",CreateFeetValue(100),Ui02Provenance("TEST_ONLY.protected")));
            d.PutConstraint(new VerticalCoverConstraint(end,CreateFeetValue(100),CreateFeetValue(2),Ui02Provenance("TEST_ONLY.cover")));
            d.PutConstraint(new VerticalSlopeConstraint(c.Id,0.001,0.03,Ui02Provenance("TEST_ONLY.slope")));
            var joint=raw.Structures.Select(n=>new {n.Id,Ends=raw.Conduits.SelectMany(x=>new[]{d.End(x.Id,ConduitEndKind.Start),d.End(x.Id,ConduitEndKind.Stop)}).Where(e=>e.NodeId==n.Id).ToArray()}).First(x=>x.Ends.Length>=2);
            d.PutConstraint(new VerticalMatchConstraint(joint.Id,VerticalMatchingMode.InvertMatch,joint.Ends,Ui02Provenance("TEST_ONLY.match")));
            d.PutConstraint(new OutfallVerticalConstraint(outfall.Id,Ui02Provenance("TEST_ONLY.outfall")));
            Check(d.Constraints.FixedConstraints.Count==1&&d.Constraints.ProtectedConstraints.Count==1&&d.Constraints.CoverConstraints.Count==1&&d.Constraints.SlopeConstraints.Count==1&&d.Constraints.MatchConstraints.Count==1&&d.Constraints.OutfallConstraints.Count==1,"All six existing typed constraint contracts reused.");
            Check(d.Readiness().MissingOutfallDecisionNodeIds.Contains(outfall.Id),"FreeOutfall never satisfies protected/connected production boundary.");
            Check(d.Basis.VerticalDesign.Criteria.Count==6&&d.Basis.VerticalDesign.Criteria.All(x=>x.Value.Kind==StandardValueKind.Text),"Native structured-decision bindings are generated by typed controls.");
            GravityExecutionPlanBuilder.ValidateEngineeringConstraints(d.Inputs,new GravityNetworkIngestionService().Ingest(raw,d.Inputs.ModelFingerprint,schemaVersion:2));
            Check(true,"Accepted P15 lineage validator accepts exact native mirrored decisions.");
        }
        private static void MvpUi02_P13PresentationAndGenerationGate()
        {
            var d=Ui02Draft();var f=d.Assess();
            Check(f.Status==VerticalFeasibilityStatus.EngineeringDecisionRequired||f.Status==VerticalFeasibilityStatus.Incomplete,"Native P13 reports unresolved intent.");
            string text=EngineeringDesignPanel.PresentFeasibility(f)+EngineeringDesignPanel.PresentReadiness(d.Readiness());
            Check(text.Contains(f.Status.ToString())&&text.Contains(f.ConstraintFingerprint)&&text.Contains("Missing flow orientations")&&text.Contains("NOT AUTHORIZED"),"Exact native result/lineage/blockers presented separately from production readiness.");
            Ui02Reject(()=>d.GenerateCandidates(),"P13_GENERATION_GATE");
            Check(!d.CanGenerate&&d.Inputs.Approval==null&&d.Inputs.CandidateJson==null,"Incomplete assessment creates no approval, candidate or runtime intent.");
        }
        private static void MvpUi02_P14ExactAndRecipeSelection()
        {
            var f=ProductionFixture();var d=new EngineeringDesignWorkspace(f.Input.ProjectId,f.Input.ModelFingerprint,f.Input.SourceRead,f.Input);
            var exact=d.Assess();Check(exact.Status==VerticalFeasibilityStatus.Feasible,"Existing TEST_ONLY exact fixture maps to feasible UI state.");
            var set=d.GenerateCandidates();Check(set.Candidates.Count==1&&set.Candidates.Single().RecipeId=="EXACT_CONSTRAINT_PROFILE"&&d.Inputs.CandidateJson!=null,"Native exact-profile generation needs no artificial recipe.");
            Check(EngineeringDesignPanel.PresentCandidate(set.Candidates.Single()).Contains(set.Candidates.Single().CandidateFingerprint),"Positive candidate/UI projection includes exact fingerprint.");
            var many=ProductionFixture(free:true);var free=new EngineeringDesignWorkspace(many.Input.ProjectId,many.Input.ModelFingerprint,many.Input.SourceRead,many.Input);
            free.Assess();Check(free.Feasibility.Status==VerticalFeasibilityStatus.FeasibleMultipleSolutions,"Existing free fixture projects multiple solutions.");Ui02Reject(()=>free.GenerateCandidates(),"RECIPE_REQUIRED");
            free.MarkSaved();free.NewDraft("TEST_ONLY_2");free.SaveRecipe(many.Generation.Recipes.Single());free.Assess();free.GenerateCandidates();
            Check(free.Inputs.CandidateJson!=null&&free.Candidates.Candidates.Count==1,"Explicit existing recipe follows native single-candidate intake rule.");
            Ui02Reject(()=>free.SelectCandidate("forged"),"EXACT_CANDIDATE_SELECTION_REQUIRED");
            free.SetOrientation(many.Input.SourceRead.Conduits.First().Id,"Start → Stop",Ui02Provenance("TEST_ONLY.orientation.change"));
            Check(free.Feasibility==null&&free.Candidates==null&&free.Inputs.CandidateJson==null&&free.Inputs.GenerationRequestJson==null,"Orientation change invalidates feasibility, candidates, selection and intent.");
            free.Assess();Ui02Reject(()=>free.GenerateCandidates(),"RECIPE_STALE");
        }
        private static (ProjectWorkspace Project,EngineeringDesignWorkspace Design) Ui02StoredFixture(bool changed=true)
        {
            string root=Ui01Directory();Directory.CreateDirectory(root);string file=Path.Combine(root,"TEST_ONLY.stsw");File.WriteAllText(file,"TEST_ONLY_NOT_A_BENTLEY_MODEL");
            var f=ProductionFixture(changed);var project=ProjectWorkspace.Create(Path.Combine(root,"project"),"TEST_ONLY_UI02");project.AttachModel(ProtectedBaseline.Register(file),f.Input.SourceRead);project.SaveProjectState();
            var d=EngineeringDesignStorage.OpenWorkspace(project);d.NewDraft("TEST_ONLY_1");
            foreach(var decision in GravityExecutionPlanBuilder.ConstraintDecisions(f.Constraints))
                if(decision is GravityDesignOrientation o)d.SetOrientation(o.ConduitId,o.FromNodeId==d.End(o.ConduitId,ConduitEndKind.Start).NodeId?"Start → Stop":"Stop → Start",o.Provenance);else d.PutConstraint(decision);
            d.Assess();d.GenerateCandidates();EngineeringDesignStorage.PersistEngineeringState(project,d);return(project,d);
        }
        private static void MvpUi02_AtomicPersistenceAndReopen()
        {
            var f=Ui02StoredFixture();var p=f.Project;string reference=p.State.EngineeringDecisionReferenceId;
            Check(EngineeringDesignStorage.IsOwned(reference)&&p.State.DesignBasisReference.DesignBasisVersion=="TEST_ONLY_1"&&!p.Dirty,"Native project references only verified durable engineering artifact.");
            var reopened=ProjectWorkspace.Open(p.DirectoryPath);var d=EngineeringDesignStorage.OpenWorkspace(reopened);
            Check(!d.HasLiveNetwork&&d.Feasibility==null&&d.Candidates==null&&d.Inputs.Approval==null,"Application reopen restores historical engineering content without executable plan or live-network claim.");
            Check(d.Basis.CreateSnapshot().GravityContentFingerprint==f.Design.Basis.CreateSnapshot().GravityContentFingerprint&&d.Constraints.VerticalConstraintFingerprint==f.Design.Constraints.VerticalConstraintFingerprint&&d.Inputs.CandidateJson==f.Design.Inputs.CandidateJson,"Basis/constraints/selection identity persist exactly.");
            d.BindNetwork(p.Network,p.State.Model.ProtectedBaselineFingerprint);d.MarkSaved();d.NewDraft("TEST_ONLY_2");EngineeringDesignStorage.PersistEngineeringState(reopened,d);
            Check(File.Exists(Path.Combine(p.DirectoryPath,"engineering",reference+".json"))&&reopened.State.EngineeringDecisionReferenceId!=reference,"Explicit revision preserves prior immutable engineering information.");
            Ui01Reject(()=>EngineeringDesignStorage.PersistEngineeringState(p,f.Design),"PROJECT_STATE_CONFLICT");
            string latest=reopened.State.EngineeringDecisionReferenceId;string path=Path.Combine(p.DirectoryPath,"engineering",latest+".json");string original=File.ReadAllText(path);
            File.WriteAllText(path,original+" ");Ui01Reject(()=>ProjectWorkspace.Open(p.DirectoryPath),"ENGINEERING_CONTENT_IDENTITY_MISMATCH");File.WriteAllText(path,original);
            File.Move(path,path+".TEST_ONLY_REMOVED");Ui01Reject(()=>ProjectWorkspace.Open(p.DirectoryPath),"ENGINEERING_ARTIFACT_MISSING");
        }
        private static void MvpUi02_StaleSourceAndMalformedReferences()
        {
            var f=Ui02StoredFixture();var d=f.Design;var changed=RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(GravityExecutionPlanBuilder.Json(f.Project.Network));changed.Conduits[0].StartInvert+=0.5;
            d.BindNetwork(changed,f.Project.State.Model.ProtectedBaselineFingerprint);
            Check(d.SourceChanged&&d.Inputs.CandidateJson==null&&d.Feasibility==null,"Actual network-content change invalidates all dependent state.");
            Ui02Reject(()=>d.Assess(),"CURRENT_NETWORK_AND_DRAFT_REQUIRED");d.NewDraft("TEST_ONLY_SOURCE_2");
            Check(!d.SourceChanged&&d.Constraints.Orientations.Count==0&&d.Constraints.FixedConstraints.Count==0,"Explicit new source draft keeps engineering decisions unresolved; no assumed remapping.");
            var copy=ProjectStore.LoadProject(f.Project.DirectoryPath);copy.EngineeringDecisionReferenceId="ui02-../escape";Ui01Reject(()=>EngineeringDesignStorage.Load(f.Project.DirectoryPath,copy),"ENGINEERING_REFERENCE_INVALID");
            copy=ProjectStore.LoadProject(f.Project.DirectoryPath);copy.DesignBasisReference.DesignBasisVersion="stale";Ui01Reject(()=>EngineeringDesignStorage.Load(f.Project.DirectoryPath,copy),"ENGINEERING_REFERENCE_MISMATCH");
        }
        private static void MvpUi02_ControlProjectionWithoutForeground()
        {
            var f=Ui02StoredFixture();Exception failure=null;var thread=new Thread(()=>{try {
                using(var form=new ProjectWorkspaceForm(GravityOperatingMode.SingleOperator))
                {
                    form.DesignWorkspace.Bind(f.Project);Check(form.DesignWorkspace.Sections.TabPages.Count==5,"One embedded design workspace with five practical tabs.");
                    var table=(DataGridView)form.Controls.Find("OrientationTable",true).Single();Check(table.Rows.Count==f.Project.Network.Conduits.Count,"UI projection uses actual source conduits only.");
                    Check(!((Button)form.Controls.Find("GenerateCandidates",true).Single()).Enabled,"Reopened/cached engineering input never restores P14 generation availability.");
                    form.DesignWorkspace.Design.Assess();Check(form.DesignWorkspace.Design.CanGenerate,"Positive native service state available to UI gate without solver intent.");
                    form.SetBusy(true);Check(!form.DesignWorkspace.Enabled&&!form.SelectModel.Enabled,"Read supervision disables engineering editing and duplicate model load.");
                    Check(!form.Execute.Enabled&&form.DesignWorkspace.Design.Inputs.Approval==null,"UI02 projection does not authorize P15 execution.");
                }
            } catch(Exception e){failure=e;} });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure!=null)throw failure;
        }
    }
}
