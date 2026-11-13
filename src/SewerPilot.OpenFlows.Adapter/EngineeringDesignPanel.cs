using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    // One embedded workspace and one reusable native criterion editor. Events delegate to offline domain services.
    internal sealed class EngineeringDesignPanel : UserControl
    {
        internal readonly TabControl Sections = new TabControl { Name="EngineeringSections", AccessibleName="Engineering sections", Dock=DockStyle.Fill };
        private readonly TextBox basisId=Text("DesignBasisId",190), designBasisVersionInput=Text("DesignBasisVersion",85), draftVersion=Text("NewDraftVersion",85), note=Text("ReviewNote",250);
        private readonly Label constraintHelp=new Label {AutoSize=true};
        private readonly Label identity=new Label {Name="DesignReviewIdentity",AutoSize=true};
        private readonly ComboBox basisSection=Choice("DesignBasisSection",Enum.GetNames(typeof(GravityFlowSectionIdentifier)),160);
        private readonly ComboBox valueKind=Choice("CriterionValueKind",Enum.GetNames(typeof(StandardValueKind)),100);
        private readonly ListBox criteria=new ListBox {Name="CriterionList",AccessibleName="Criteria",Dock=DockStyle.Fill,DisplayMember="Key"};
        private readonly PropertyGrid criterion=new PropertyGrid {Name="CriterionEditor",AccessibleName="Typed criterion editor",Dock=DockStyle.Fill,ToolbarVisible=false};
        private readonly TextBox validation=Output("DesignValidation");
        private readonly DataGridView orientations=Table("OrientationTable","Conduit","Bentley Start","Bentley Stop","Direction","Validation");
        private readonly ComboBox direction=Choice("DesignDirection",new[]{"Undecided","Start → Stop","Stop → Start"},160);
        private readonly PropertyGrid orientationProvenance=Provenance("OrientationProvenance");
        private readonly DataGridView constraintTable=Table("ConstraintTable","Type","Identity / decision","Provenance key");
        private readonly ComboBox constraintType=Choice("ConstraintType",new[]{"OutfallVerticalConstraint","FixedEndpointElevationConstraint","ProtectedNodeElevationConstraint","VerticalMatchConstraint","VerticalCoverConstraint","VerticalSlopeConstraint"},230);
        private readonly ComboBox conduit=Choice("ConstraintConduit",new string[0],80), node=Choice("ConstraintNode",new string[0],80);
        private readonly ComboBox end=Choice("ConstraintEnd",Enum.GetNames(typeof(ConduitEndKind)),75);
        private readonly ComboBox mode=Choice("ConstraintMode",new[]{"FixedInvert","FreeOutfall","ConnectedEndpointConstraint","InvertMatch","CrownMatch","Invert","Ground","Rim"},200);
        private readonly TextBox first=Text("ConstraintFirstValue",110), second=Text("ConstraintSecondValue",110), units=Text("ConstraintUnit",70), reason=Text("ConstraintReason",180);
        private readonly CheckedListBox matchEnds=new CheckedListBox {Name="MatchEndpoints",AccessibleName="Match participating endpoints",Height=80,Width=250,Dock=DockStyle.Top};
        private readonly PropertyGrid constraintProvenance=Provenance("ConstraintProvenance");
        private readonly TextBox feasibility=Output("FeasibilityResult");
        private readonly TextBox recipeId=Text("RecipeId",160), recipeName=Text("RecipeName",140);
        private readonly PropertyGrid recipeProvenance=Provenance("RecipeProvenance");
        private readonly DataGridView recipeEnds=Table("RecipeEndpoints","Conduit","End","Node","Selection","Explicit elevation (ft)");
        private readonly ListBox candidates=new ListBox {Name="CandidateList",AccessibleName="Generated candidates",Height=70,Dock=DockStyle.Bottom,DisplayMember="CandidateId"};
        private readonly TextBox candidateOutput=Output("CandidateDetails");
        private readonly Button generate=Button("GenerateCandidates","Generate P14"), select=Button("SelectCandidate","Select candidate");
        private readonly Button previewAutomatic=Button("PreviewAutomaticProposal","Preview Automatic Proposal"), stageAutomatic=Button("StageAutomaticProposal","Stage Automatic Proposal");
        private readonly Button applyCriterion=Button("ApplyCriterion","Apply criterion"), applyIdentity=Button("ApplyDesignIdentity","Apply identity/version");
        private readonly Button save=Button("SaveEngineering","Save engineering"), reload=Button("ReloadEngineering","Reopen engineering state");
        private ProjectWorkspace project;
        internal EngineeringDesignWorkspace Design {get;private set;}
        internal event Action Changed;
        internal event Action Saved;
        private GravityDesignCriterion editing;
        private DraftMetadata editingMetadata;
        private sealed class DraftMetadata
        {
            public List<DesignBasisStandardReference> StandardReferences {get;set;}
            public List<DesignBasisAssumption> Assumptions {get;set;}
            public List<DesignBasisOverride> Overrides {get;set;}
        }
        private string editingKey;
        private string automaticProposalFingerprint, automaticProposalDetails;
        private bool pendingCriterion, presenting, previewing;

        internal EngineeringDesignPanel()
        {
            Name="EngineeringDesignWorkspace";AccessibleName="Engineering Design Workspace";Dock=DockStyle.Fill;
            foreach(var type in new[]{typeof(StandardValue),typeof(StandardQuantity),typeof(StandardProvenance)})
                TypeDescriptor.AddAttributes(type,new TypeConverterAttribute(typeof(ExpandableObjectConverter)));
            var top=Bar(save,reload,new Label {Text="Offline design assessment; no runtime authorization, Bentley saves or solver.",AutoSize=true});
            Controls.Add(Sections);Controls.Add(top);
            BuildBasis();BuildOrientation();BuildConstraints();BuildFeasibility();BuildCandidates();
            foreach(var input in new[]{basisId,designBasisVersionInput,draftVersion,note,first,second,units,reason,recipeId,recipeName})
                input.TextChanged+=(s,e)=>{if(!presenting&&!previewing)Changed?.Invoke();};
            foreach(var input in new[]{direction,conduit,node,end,mode,valueKind})
                input.SelectedIndexChanged+=(s,e)=>{if(!presenting&&!previewing)Changed?.Invoke();};
            foreach(var editor in new[]{orientationProvenance,constraintProvenance,recipeProvenance})
                editor.PropertyValueChanged+=(s,e)=>Changed?.Invoke();
            recipeEnds.CellValueChanged+=(s,e)=>{if(!presenting&&!previewing)Changed?.Invoke();};
            save.Click+=(s,e)=>Guard(()=>PersistEngineeringState());
            reload.Click+=(s,e)=>Guard(()=>{EngineeringDesignWorkspace.Require(Design!=null&&!Design.Dirty&&!pendingCriterion,"SAVE_EDITS_BEFORE_REOPEN"); Bind(project,true); validation.Text="Saved engineering state reopened and content identity verified. No executable plan restored.";Saved?.Invoke();});
            basisSection.SelectedIndexChanged+=(s,e)=>{if(pendingCriterion)Guard(()=>ApplyCriterion());ShowCriteria();}; criteria.SelectedIndexChanged+=(s,e)=>{if(!pendingCriterion)EditSelected();};
            criterion.PropertyValueChanged+=(s,e)=>{pendingCriterion=true;project?.MarkEngineeringDirty();Changed?.Invoke();};
            orientations.SelectionChanged+=(s,e)=>ShowOrientation(); constraintType.SelectedIndexChanged+=(s,e)=>ConstraintHelp();
            candidates.SelectedIndexChanged+=(s,e)=>{var c=candidates.SelectedItem as GravityDesignCandidate;if(!presenting&&c!=null){automaticProposalDetails=null;candidateOutput.Text=PresentCandidate(c);}};
            RefreshView();
        }
        private static TextBox Text(string name,int width)=>new TextBox{Name=name,AccessibleName=name,Width=width};
        private static TextBox Output(string name)=>new TextBox{Name=name,AccessibleName=name,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill};
        private static Button Button(string name,string text)=>new Button{Name=name,AccessibleName=text,Text=text,AutoSize=true,Height=28};
        private static ComboBox Choice(string name,string[] values,int width)
        {var c=new ComboBox{Name=name,AccessibleName=name,DropDownStyle=ComboBoxStyle.DropDownList,Width=width};c.Items.AddRange(values);if(values.Length>0)c.SelectedIndex=0;return c;}
        private static PropertyGrid Provenance(string name)=>new PropertyGrid{Name=name,AccessibleName=name,SelectedObject=new StandardProvenance(),ToolbarVisible=false,Dock=DockStyle.Fill};
        private static FlowLayoutPanel Bar(params Control[] controls)
        {var p=new FlowLayoutPanel{Dock=DockStyle.Top,Height=37,AutoScroll=true};p.Controls.AddRange(controls);return p;}
        private static DataGridView Table(string name,params string[] headers)
        {
            var g=new DataGridView{Name=name,AccessibleName=name,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,MultiSelect=false,
                SelectionMode=DataGridViewSelectionMode.FullRowSelect,RowHeadersVisible=false,Dock=DockStyle.Fill,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
            foreach(var h in headers)g.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=h,Name=h.Replace(" ",""),SortMode=DataGridViewColumnSortMode.NotSortable});return g;
        }
        private TabPage Page(string name){var p=new TabPage(name){Name=name.Replace(" ","")};Sections.TabPages.Add(p);return p;}
        private void Guard(Action action)
        {
            try{action();RefreshView();}
            catch(Exception e){ClearAutomaticProposalPresentation();validation.Text="BLOCKED: "+e.GetBaseException().Message;feasibility.Text="BLOCKED: "+e.GetBaseException().Message;candidateOutput.Text="BLOCKED: "+e.GetBaseException().Message;generate.Enabled=Design?.CanGenerate==true;}
        }
        private void BuildBasis()
        {
            var page=Page("Design Basis");
            var create=Button("CreateDesignDraft","Create new draft/version");create.Click+=(s,e)=>Guard(()=>{Design.NewDraft(Design.Basis==null?designBasisVersionInput.Text:draftVersion.Text);validation.Text="Draft created. Required identity/criteria/provenance remain unresolved until explicitly supplied.";});
            applyIdentity.Click+=(s,e)=>Guard(()=>{Design.SetIdentity(basisId.Text,designBasisVersionInput.Text);validation.Text="Draft identity applied.";});
            var add=Button("NewCriterion","New criterion");add.Click+=(s,e)=>Guard(()=>{
                EngineeringDesignWorkspace.Require(Design.Basis?.CommonDesignBasis.ReviewState==DesignBasisReviewState.Draft,"DRAFT_REQUIRED");
                EngineeringDesignWorkspace.Require(!pendingCriterion,"APPLY_CURRENT_EDITOR_FIRST");editingMetadata=null;
                editing=new GravityDesignCriterion{Section=(GravityFlowSectionIdentifier)Enum.Parse(typeof(GravityFlowSectionIdentifier),(string)basisSection.SelectedItem),Provenance=new StandardProvenance()};editingKey=null;criterion.SelectedObject=editing;pendingCriterion=true;project.MarkEngineeringDirty();Changed?.Invoke();});
            var metadata=Button("EditCommonMetadata","Common metadata");metadata.Click+=(s,e)=>Guard(()=>{
                EngineeringDesignWorkspace.Require(Design.Basis?.CommonDesignBasis.ReviewState==DesignBasisReviewState.Draft&&!pendingCriterion,"APPLY_CURRENT_DRAFT_EDITOR_FIRST");
                var b=GravityFlowDesignBasisSerializer.Deserialize(GravityFlowDesignBasisSerializer.Serialize(Design.Basis)).CommonDesignBasis;
                editingMetadata=new DraftMetadata{StandardReferences=b.StandardReferences,Assumptions=b.Assumptions,Overrides=b.Overrides};editing=null;criterion.SelectedObject=editingMetadata;pendingCriterion=true;project.MarkEngineeringDirty();Changed?.Invoke();});
            applyCriterion.Click+=(s,e)=>Guard(()=>ApplyCriterion());
            var discard=Button("DiscardCriterionEditor","Discard editor changes");discard.Click+=(s,e)=>{pendingCriterion=false;EditSelected();validation.Text="Unapplied editor changes discarded; saved engineering content retained.";};
            var makeValue=Button("CreateTypedValue","Create typed value");makeValue.Click+=(s,e)=>{if(editing==null)return;editing.Value=new StandardValue{Kind=(StandardValueKind)Enum.Parse(typeof(StandardValueKind),(string)valueKind.SelectedItem)};
                if(editing.Value.Kind==StandardValueKind.Quantity)editing.Value.QuantityValue=new StandardQuantity{Value=double.NaN,Unit=""};criterion.Refresh();pendingCriterion=true;project?.MarkEngineeringDirty();Changed?.Invoke();};
            var check=Button("ValidateDesignBasis","Validate Design Basis");check.Click+=(s,e)=>Guard(()=>{var v=Design.ValidateBasis();validation.Text=v.IsValid?"Design Basis validation passed; review and production readiness remain separate.":string.Join(Environment.NewLine,v.Issues);});
            var transition=Choice("ReviewTransition",Enum.GetNames(typeof(DesignBasisReviewState)),140);
            var review=Button("ApplyReviewTransition","Apply review transition");review.Click+=(s,e)=>Guard(()=>{Design.Transition((DesignBasisReviewState)Enum.Parse(typeof(DesignBasisReviewState),(string)transition.SelectedItem),note.Text);validation.Text="SingleOperator review transition recorded. No independent reviewer or execution authority implied.";});
            var left=new Panel{Dock=DockStyle.Left,Width=190};left.Controls.Add(criteria);left.Controls.Add(Bar(basisSection));
            var editor=new Panel{Dock=DockStyle.Fill};editor.Controls.Add(criterion);editor.Controls.Add(Bar(add,valueKind,makeValue,applyCriterion,metadata));editor.Controls.Add(Bar(discard));
            var result=new Panel{Dock=DockStyle.Bottom,Height=115};result.Controls.Add(validation);
            page.Controls.Add(editor);page.Controls.Add(left);page.Controls.Add(result);
            page.Controls.Add(Bar(check,transition,note,review));page.Controls.Add(Bar(identity));
            page.Controls.Add(Bar(new Label{Text="ID",AutoSize=true},basisId,new Label{Text="Version",AutoSize=true},designBasisVersionInput,applyIdentity));
            page.Controls.Add(Bar(new Label{Text="New revision version",AutoSize=true},draftVersion,create));
        }
        private void BuildOrientation()
        {
            var page=Page("Flow Orientation");var apply=Button("ApplyOrientation","Apply selected orientation");
            apply.Click+=(s,e)=>Guard(()=>{EngineeringDesignWorkspace.Require(orientations.CurrentRow!=null,"SELECT_CONDUIT");Design.SetOrientation((int)orientations.CurrentRow.Tag,(string)direction.SelectedItem,(StandardProvenance)orientationProvenance.SelectedObject);});
            var autoPrep=Button("PrepareAutomaticRouting","Evaluate automatic routing & overflow");
            autoPrep.Click+=(s,e)=>Guard(()=>{
                EngineeringDesignWorkspace.Require(Design!=null,"OPEN_PROJECT_FIRST");
                var prep=Design.TryPrepareAutomaticDesign();
                var overflow=Design.EvaluateControlledOverflowProposal();
                string msg="Automatic routing evaluation complete.";
                if(prep!=null&&prep.Components.Any(c=>!c.Eligible))
                    msg+=" Ineligible component: "+string.Join("; ",prep.Components.SelectMany(c=>c.UnresolvedReasons));
                if(overflow!=null&&overflow.IsSupported)
                    msg+=Environment.NewLine+overflow.Detail;
                validation.Text=msg;
            });
            var right=new Panel{Dock=DockStyle.Right,Width=360};right.Controls.Add(orientationProvenance);right.Controls.Add(Bar(direction,apply,autoPrep));
            page.Controls.Add(orientations);page.Controls.Add(right);page.Controls.Add(Bar(new Label{Text="Topology is not design direction. Undecided creates no record. Provenance requires a source layer, parameter key and document/standard reference.",AutoSize=true}));
        }
        private void BuildConstraints()
        {
            var page=Page("Outfalls & Constraints");
            var input=new Panel{Dock=DockStyle.Right,Width=430};input.Controls.Add(constraintProvenance);
            var fields=new FlowLayoutPanel{Dock=DockStyle.Top,Height=185,AutoScroll=true};
            foreach(var row in new Control[][]{
                new Control[]{constraintType},new Control[]{new Label{Text="Conduit / end / node",AutoSize=true},conduit,end,node},
                new Control[]{new Label{Text="Mode / elevation kind",AutoSize=true},mode},
                new Control[]{new Label{Text="Value / second bound / unit",AutoSize=true},first,second,units},
                new Control[]{new Label{Text="Reason (optional)",AutoSize=true},reason},new Control[]{constraintHelp}})
                fields.Controls.AddRange(row);
            var apply=Button("ApplyConstraint","Apply typed constraint");apply.Click+=(s,e)=>Guard(()=>ApplyConstraint());
            input.Controls.Add(Bar(apply));input.Controls.Add(matchEnds);input.Controls.Add(fields);
            page.Controls.Add(constraintTable);page.Controls.Add(input);
            page.Controls.Add(Bar(new Label{Text="Missing outfall decisions are explicit. FreeOutfall may satisfy P13 intent; production still requires ProtectedInvert or ConnectedEndpointConstraint. Elevation/cover: Feet only; slopes are native ratios, no % conversion.",AutoSize=true}));
        }
        private void BuildFeasibility()
        {
            var page=Page("Feasibility");var assess=Button("AssessP13","Run P13 feasibility");assess.Click+=(s,e)=>Guard(()=>{Design.Assess();feasibility.Text=PresentFeasibility(Design.Feasibility)+Environment.NewLine+PresentReadiness(Design.Readiness());});
            page.Controls.Add(feasibility);page.Controls.Add(Bar(assess,new Label{Text="Mathematical feasibility and production readiness are distinct. Assessment requests no solver intent.",AutoSize=true}));
        }
        private void BuildCandidates()
        {
            var page=Page("Candidates");var recipeArea=new Panel{Dock=DockStyle.Top,Height=230};
            recipeEnds.ReadOnly=false;foreach(DataGridViewColumn c in recipeEnds.Columns)c.ReadOnly=c.Index<3;
            var selection=new DataGridViewComboBoxColumn{Name="Selection",HeaderText="Selection",FlatStyle=FlatStyle.Flat};selection.Items.Add("Undecided");selection.Items.AddRange(Enum.GetNames(typeof(EndpointSelectionMode)));recipeEnds.Columns.RemoveAt(3);recipeEnds.Columns.Insert(3,selection);
            var provenance=new Panel{Dock=DockStyle.Right,Width=300};provenance.Controls.Add(recipeProvenance);recipeArea.Controls.Add(recipeEnds);recipeArea.Controls.Add(provenance);
            var add=Button("SaveRecipe","Save explicit recipe");add.Click+=(s,e)=>Guard(()=>{
                recipeEnds.EndEdit();var p=(StandardProvenance)recipeProvenance.SelectedObject;var selections=new List<CandidateEndpointSelection>();
                foreach(DataGridViewRow row in recipeEnds.Rows)
                {
                    string choice=Convert.ToString(row.Cells[3].Value,CultureInfo.InvariantCulture);if(string.IsNullOrWhiteSpace(choice)||choice=="Undecided")continue;
                var m=(EndpointSelectionMode)Enum.Parse(typeof(EndpointSelectionMode),choice);
                    var original=row.Tag as CandidateEndpointSelection;
                    var endpoint=original?.ConduitEnd??(GravityConduitEnd)row.Tag;
                    selections.Add(new CandidateEndpointSelection(endpoint,m,original?.Provenance??p,m==EndpointSelectionMode.ExplicitValue?EngineeringDesignWorkspace.Feet(Convert.ToString(row.Cells[4].Value,CultureInfo.InvariantCulture),"ft"):null));
                }
                Design.SaveRecipe(new GravityCandidateRecipe(recipeId.Text,selections,p,recipeName.Text));candidateOutput.Text="Explicit recipe saved against current constraint fingerprint. Run P13 again before generating.";
            });
            var existing=Choice("ExistingRecipes",new string[0],190);var editRecipe=Button("EditRecipe","Load saved recipe");editRecipe.Click+=(s,e)=>Guard(()=>{
                var r=Design.Recipes.SingleOrDefault(x=>x.RecipeId==Convert.ToString(existing.SelectedItem));EngineeringDesignWorkspace.Require(r!=null,"SELECT_RECIPE");
                recipeId.Text=r.RecipeId;recipeName.Text=r.Name;recipeProvenance.SelectedObject=RuntimeHostProtocol.Serializer().Deserialize<StandardProvenance>(GravityExecutionPlanBuilder.Json(r.Provenance));RefreshView();});
            existing.DropDown+=(s,e)=>{existing.Items.Clear();existing.Items.AddRange(Design?.Recipes.Select(r=>(object)r.RecipeId).ToArray()??new object[0]);};
            generate.Click+=(s,e)=>Guard(()=>{var c=Design.GenerateCandidates();candidates.DataSource=c.Candidates.ToArray();candidateOutput.Text=PresentCandidates(c)+(c.Candidates.Count==1?"\r\nSingle valid candidate follows existing intake selection rule.":"\r\nSelect an exact candidate explicitly; no ranking applied.");});
            select.Click+=(s,e)=>Guard(()=>{var c=candidates.SelectedItem as GravityDesignCandidate;EngineeringDesignWorkspace.Require(c!=null,"SELECT_CANDIDATE");Design.SelectCandidate(c.CandidateId);candidateOutput.Text=PresentCandidate(c)+"\r\nSELECTED — NOT APPROVED";});
            previewAutomatic.Click+=(s,e)=>
            {
                ClearAutomaticProposalPresentation();
                // Guard refreshes controls after success. Its representational input changes are not engineering edits.
                previewing=true;
                try{Guard(()=>
                {
                    EngineeringDesignWorkspace.Require(Design!=null,"OPEN_PROJECT_FIRST");RequireAppliedInputs();
                    EngineeringDesignWorkspace.Require(Design.Feasibility!=null,"P13_ASSESSMENT_REQUIRED");
                    var context=Design.CurrentAutomaticProposalContext(GravityVerticalDesignProposalResolver.ObjectiveId,GravityVerticalDesignProposalResolver.ObjectiveVersion);
                    var proposal=Design.PreviewAutomaticProposal(GravityVerticalDesignProposalResolver.ObjectiveId,GravityVerticalDesignProposalResolver.ObjectiveVersion,context);
                    automaticProposalDetails=PresentAutomaticProposal(proposal);
                    automaticProposalFingerprint=proposal.Status=="PROPOSED"?proposal.ProposalFingerprint:null;
                    candidateOutput.Text=automaticProposalDetails;
                });}finally{previewing=false;}
            };
            stageAutomatic.Click+=(s,e)=>
            {
                var fingerprint=automaticProposalFingerprint;ClearAutomaticProposalPresentation();
                Guard(()=>
                {
                    EngineeringDesignWorkspace.Require(Design!=null,"OPEN_PROJECT_FIRST");RequireAppliedInputs();
                    EngineeringDesignWorkspace.Require(!string.IsNullOrWhiteSpace(fingerprint),"AUTOMATIC_PROPOSAL_PREVIEW_REQUIRED");
                    var staged=Design.StageAutomaticProposal(fingerprint);
                    automaticProposalDetails=PresentCandidates(staged)+"\r\nSTAGED — NOT SELECTED";
                    candidateOutput.Text=automaticProposalDetails;
                });
            };
            page.Controls.Add(candidateOutput);page.Controls.Add(candidates);page.Controls.Add(Bar(generate,select,previewAutomatic,stageAutomatic));page.Controls.Add(recipeArea);
            page.Controls.Add(Bar(new Label{Text="Recipe ID / name",AutoSize=true},recipeId,recipeName,add,existing,editRecipe));
            page.Controls.Add(Bar(new Label{Text="Shallowest feasible invert profile: explicit preview, stage, then select. Exact feasible profiles need no recipe. Multiple solutions require explicit supported endpoint choices. Incomplete/decision-required/infeasible P13 blocks generation.",AutoSize=true}));
            recipeEnds.DataError+=(s,e)=>{e.ThrowException=false;candidateOutput.Text="Invalid selection: choose a supported endpoint mode.";};
        }
        internal void Bind(ProjectWorkspace workspace,bool reopen=false)
        {
            if(workspace==null){if(Design!=null)Design.Changed-=DesignChanged;ClearAutomaticProposalPresentation();project=null;Design=null;RefreshView();return;}
            if(reopen||project!=workspace||Design==null)
            {
                if(Design!=null)Design.Changed-=DesignChanged;ClearAutomaticProposalPresentation();
                project=workspace;Design=EngineeringDesignStorage.OpenWorkspace(workspace);
                Design.Changed+=DesignChanged;
                FeasibilityCleared();
            }
            else if(workspace.Network!=null){ClearAutomaticProposalPresentation();Design.BindNetwork(workspace.Network,workspace.State.Model.ProtectedBaselineFingerprint);}
            RefreshView();
        }
        private void ClearAutomaticProposalPresentation(){automaticProposalFingerprint=null;automaticProposalDetails=null;stageAutomatic.Enabled=false;}
        private void DesignChanged()
        {
            ClearAutomaticProposalPresentation();
            if(Design?.Candidates==null)candidates.DataSource=null;
            project?.MarkEngineeringDirty();Changed?.Invoke();
        }
        private void FeasibilityCleared(){feasibility.Text="Not assessed in this session. Stored engineering content is historical until a verified live P12 network is loaded.";candidates.DataSource=null;candidateOutput.Text="No executable preview/confirmation restored.";}
        internal void PersistEngineeringState()
        {
            EngineeringDesignWorkspace.Require(Design?.Basis!=null,"CREATE_DRAFT_FIRST");
            if(pendingCriterion)ApplyCriterion();
            if(Design.Basis.CommonDesignBasis.ReviewState==DesignBasisReviewState.Draft &&
                (basisId.Text!=Design.Basis.CommonDesignBasis.DesignBasisId||designBasisVersionInput.Text!=Design.Basis.CommonDesignBasis.DesignBasisVersion))Design.SetIdentity(basisId.Text,designBasisVersionInput.Text);
            EngineeringDesignStorage.PersistEngineeringState(project,Design);validation.Text="Engineering state saved through ProjectStore. Immutable content reference verified; no Bentley save.";Saved?.Invoke();RefreshView();
        }
        internal void Import(GravityEngineeringInputs inputs){EngineeringDesignWorkspace.Require(Design!=null,"OPEN_PROJECT_FIRST");Design.Import(inputs);RefreshView();}
        internal void ShowCurrentEngineeringState(){RefreshView();}
        internal bool Dirty=>Design?.Dirty==true||pendingCriterion;
        internal void RequireAppliedInputs()
        {
            EngineeringDesignWorkspace.Require(Design?.Basis!=null&&!pendingCriterion&&!recipeEnds.IsCurrentCellDirty,"APPLY_ENGINEERING_EDITOR_BEFORE_PREVIEW");
            EngineeringDesignWorkspace.Require(basisId.Text==Design.Basis.CommonDesignBasis.DesignBasisId&&
                designBasisVersionInput.Text==Design.Basis.CommonDesignBasis.DesignBasisVersion,"APPLY_IDENTITY_BEFORE_PREVIEW");
        }
        internal void SetBusy(bool busy){Enabled=!busy;}
        private void ApplyCriterion()
        {
            if(editingMetadata!=null){Design.SetCommonMetadata(editingMetadata.StandardReferences,editingMetadata.Assumptions,editingMetadata.Overrides);pendingCriterion=false;editingMetadata=null;validation.Text="Native common metadata applied.";return;}
            EngineeringDesignWorkspace.Require(editing!=null,"SELECT_OR_CREATE_CRITERION");
            Design.PutCriterion(editing.Section,editing,editingKey);pendingCriterion=false;editingKey=editing.Key;validation.Text="Typed criterion applied. Undefined required criteria remain unresolved.";
        }
        private void ShowCriteria()
        {
            if(presenting)return;criteria.DataSource=Design?.Basis?.GetSection((GravityFlowSectionIdentifier)Enum.Parse(typeof(GravityFlowSectionIdentifier),(string)basisSection.SelectedItem)).Criteria.ToArray();EditSelected();
        }
        private void EditSelected()
        {
            var c=criteria.SelectedItem as GravityDesignCriterion;editingMetadata=null;editing=c?.Clone();editingKey=c?.Key;criterion.SelectedObject=editing;pendingCriterion=false;
        }
        private void RefreshView()
        {
            presenting=true;
            bool has=Design!=null;save.Enabled=reload.Enabled=has;Sections.Enabled=has;
            var b=Design?.Basis?.CommonDesignBasis;basisId.Text=b?.DesignBasisId??"";designBasisVersionInput.Text=b?.DesignBasisVersion??"";
            identity.Text=b==null?"No Design Basis. Create a draft; no criteria or engineering decisions supplied.":
                "Project: "+b.ProjectId+" | "+b.ReviewState+" | actor: "+(b.ReviewMetadata?.ActorId??"Not reviewed")+" | "+(b.ReviewMetadata?.ReviewStateChangedUtc??"");
            bool editable=b?.ReviewState==DesignBasisReviewState.Draft&&Design?.SourceChanged!=true;
            criterion.Enabled=applyCriterion.Enabled=applyIdentity.Enabled=editable;basisId.ReadOnly=designBasisVersionInput.ReadOnly=b!=null&&!editable;
            orientations.Rows.Clear();constraintTable.Rows.Clear();recipeEnds.Rows.Clear();conduit.Items.Clear();node.Items.Clear();matchEnds.Items.Clear();
            if(project?.Network!=null)
            {
                foreach(var c in project.Network.Conduits.OrderBy(c=>c.Id))
                {
                    var o=Design.Constraints?.Orientations.Values.FirstOrDefault(x=>x.ConduitId==c.Id);
                    int i=orientations.Rows.Add(c.Id,c.StartNodeId,c.StopNodeId,o==null?"Undecided":o.IsStartToStop(c.StartNodeId,c.StopNodeId)?"Start → Stop":"Stop → Start",o==null?"Missing explicit decision":"Explicit provenance");orientations.Rows[i].Tag=c.Id;
                    conduit.Items.Add(c.Id);
                    foreach(var kind in new[]{ConduitEndKind.Start,ConduitEndKind.Stop})
                    {
                        var e=new GravityConduitEnd(c.Id,kind,kind==ConduitEndKind.Start?c.StartNodeId:c.StopNodeId);matchEnds.Items.Add(e);
                        var selected=Design.Recipes.FirstOrDefault(r=>r.RecipeId==recipeId.Text)?.EndpointSelections.FirstOrDefault(s=>s.ConduitEnd.Equals(e));
                        int row=recipeEnds.Rows.Add(c.Id,kind,e.NodeId,selected?.Mode.ToString()??"Undecided",selected?.ExplicitElevation?.NumericValue?.ToString("R",CultureInfo.InvariantCulture)??"");recipeEnds.Rows[row].Tag=(object)selected??e;
                    }
                }
                foreach(var n in project.Network.Structures.OrderBy(n=>n.Id))node.Items.Add(n.Id);
            }
            if(conduit.Items.Count>0)conduit.SelectedIndex=0;if(node.Items.Count>0)node.SelectedIndex=0;
            if(Design?.Constraints!=null)
                foreach(var d in GravityExecutionPlanBuilder.ConstraintDecisions(Design.Constraints).Where(d=>!(d is GravityDesignOrientation)))
                {var p=(StandardProvenance)d.GetType().GetProperty("Provenance").GetValue(d,null);constraintTable.Rows.Add(d.GetType().Name,DescribeDecision(d),p?.ParameterKey);}
            if(project?.Network!=null)
                foreach(var n in project.Network.Structures.Where(n=>n.RuntimeCategory=="Outfall"))
                    if(Design.Constraints==null||!Design.Constraints.OutfallConstraints.Any(o=>o.OutfallNodeId==n.Id))constraintTable.Rows.Add("Outfall decision MISSING","Node "+n.Id+": explicit FixedInvert / FreeOutfall / ConnectedEndpointConstraint required","Unresolved");
            generate.Enabled=Design?.CanGenerate==true;select.Enabled=Design?.Candidates?.Candidates.Count>0;
            previewAutomatic.Enabled=has;stageAutomatic.Enabled=has&&!string.IsNullOrWhiteSpace(automaticProposalFingerprint);
            if(Design?.Feasibility==null)feasibility.Text="Not assessed for current engineering content. Run P13; production readiness remains separate.";
            var currentCandidates=Design?.Candidates?.Candidates;
            var displayedCandidates=candidates.DataSource as GravityDesignCandidate[];
            if(currentCandidates==null)candidates.DataSource=null;
            else if(displayedCandidates==null||!displayedCandidates.SequenceEqual(currentCandidates))candidates.DataSource=currentCandidates.ToArray();
            if(automaticProposalDetails!=null)candidateOutput.Text=automaticProposalDetails;
            else if(currentCandidates==null)candidateOutput.Text="No current candidates. P13 and explicit recipe lineage are required; no executable plan restored.";
            presenting=false;if(!pendingCriterion)ShowCriteria();ShowOrientation();ConstraintHelp();
        }
        private void ShowOrientation()
        {
            if(presenting||orientations.CurrentRow==null)return;int id=(int)orientations.CurrentRow.Tag;
            var o=Design?.Constraints?.Orientations.Values.FirstOrDefault(x=>x.ConduitId==id);direction.SelectedItem=orientations.CurrentRow.Cells[3].Value;
            orientationProvenance.SelectedObject=o==null?new StandardProvenance():RuntimeHostProtocol.Serializer().Deserialize<StandardProvenance>(GravityExecutionPlanBuilder.Json(o.Provenance));
        }
        private void ConstraintHelp()
        {
            string type=(string)constraintType.SelectedItem;
            var values=type=="OutfallVerticalConstraint"?new[]{"FixedInvert","FreeOutfall","ConnectedEndpointConstraint"}:
                type=="VerticalMatchConstraint"?new[]{"InvertMatch","CrownMatch"}:
                type=="ProtectedNodeElevationConstraint"?new[]{"Invert","Ground","Rim"}:new string[0];
            string prior=Convert.ToString(mode.SelectedItem);mode.Items.Clear();mode.Items.AddRange(values);mode.Enabled=values.Length>0;
            if(values.Length>0)mode.SelectedItem=values.Contains(prior)?prior:values[0];
            matchEnds.Visible=type=="VerticalMatchConstraint";
            constraintHelp.Text=type=="VerticalSlopeConstraint"?"Minimum / maximum native slope ratio; blank bound is unbounded.":
                type=="VerticalCoverConstraint"?"Ground elevation / minimum cover, Feet.":"Elevation in Feet; FreeOutfall has no fabricated elevation.";
        }
        private static int SelectedId(ComboBox c){EngineeringDesignWorkspace.Require(c.SelectedItem is int,"ACTUAL_ELEMENT_ID_REQUIRED");return (int)c.SelectedItem;}
        private void ApplyConstraint()
        {
            var p=(StandardProvenance)constraintProvenance.SelectedObject;string t=(string)constraintType.SelectedItem;
            Func<GravityConduitEnd> endpoint=()=>Design.End(SelectedId(conduit),(ConduitEndKind)Enum.Parse(typeof(ConduitEndKind),(string)end.SelectedItem));
            Func<SourceEngineeringValue> value=()=>EngineeringDesignWorkspace.Feet(first.Text,units.Text);
            object d;
            if(t=="OutfallVerticalConstraint")
            {
                string m=(string)mode.SelectedItem;EngineeringDesignWorkspace.Require(m=="FixedInvert"||m=="FreeOutfall"||m=="ConnectedEndpointConstraint","OUTFALL_MODE_REQUIRED");
                d=new OutfallVerticalConstraint(SelectedId(node),p,m=="FixedInvert"?value():null,m=="ConnectedEndpointConstraint"?new FixedEndpointElevationConstraint(endpoint(),value(),p,reason.Text):null);
            }
            else if(t=="FixedEndpointElevationConstraint")d=new FixedEndpointElevationConstraint(endpoint(),value(),p,reason.Text);
            else if(t=="ProtectedNodeElevationConstraint")d=new ProtectedNodeElevationConstraint(SelectedId(node),(string)mode.SelectedItem,value(),p);
            else if(t=="VerticalMatchConstraint")d=new VerticalMatchConstraint(SelectedId(node),(VerticalMatchingMode)Enum.Parse(typeof(VerticalMatchingMode),(string)mode.SelectedItem),matchEnds.CheckedItems.Cast<GravityConduitEnd>(),p);
            else if(t=="VerticalCoverConstraint")d=new VerticalCoverConstraint(endpoint(),value(),EngineeringDesignWorkspace.Feet(second.Text,units.Text),p);
            else d=new VerticalSlopeConstraint(SelectedId(conduit),string.IsNullOrWhiteSpace(first.Text)?(double?)null:EngineeringDesignWorkspace.Finite(first.Text),string.IsNullOrWhiteSpace(second.Text)?(double?)null:EngineeringDesignWorkspace.Finite(second.Text),p);
            Design.PutConstraint(d);
        }
        private static string DescribeDecision(object d)
        {
            if(d is OutfallVerticalConstraint o)return "Node "+o.OutfallNodeId+": "+(o.ProtectedInvert!=null?"FixedInvert "+o.ProtectedInvert:o.ConnectedEndpointConstraint!=null?"ConnectedEndpointConstraint "+o.ConnectedEndpointConstraint.ConduitEnd:"FreeOutfall; production boundary unresolved");
            if(d is FixedEndpointElevationConstraint f)return f.ConduitEnd+" = "+f.InvertElevation;
            if(d is ProtectedNodeElevationConstraint p)return "Node "+p.NodeId+" "+p.ElevationKind+" = "+p.Elevation;
            if(d is VerticalMatchConstraint m)return "Node "+m.NodeId+" "+m.Mode;
            if(d is VerticalCoverConstraint c)return c.ConduitEnd+" ground "+c.GroundElevation+" / cover "+c.MinimumCover;
            if(d is VerticalSlopeConstraint s)return "Conduit "+s.ConduitId+" native slope ["+s.MinimumSlope+", "+s.MaximumSlope+"]";
            return d.GetType().Name;
        }
        internal static string PresentFeasibility(VerticalFeasibilityResult f)=>f==null?"Not assessed":
            "P13: "+f.Status+"\r\nConstraint fingerprint: "+f.ConstraintFingerprint+"\r\n"+
            string.Join("\r\n",f.EndpointRanges.Values.Select(r=>r.ConduitEnd+": ["+r.MinimumAllowedInvert+", "+r.MaximumAllowedInvert+"] exact="+r.ExactInvert+" "+r.Unit.Symbol))+
            "\r\nConflicts: "+string.Join("; ",f.Conflicts.Select(c=>c.ConflictCode+": "+c.Explanation))+
            "\r\nDecisions: "+string.Join("; ",f.DecisionRequests.Select(d=>d.Decision))+
            "\r\nIncomplete: "+string.Join("; ",f.IncompleteItems);
        internal static string PresentReadiness(GravityEngineeringReadiness r)=>"\r\nProduction readiness: "+(r.ProductionExecutionEligible?"Eligible":"NOT AUTHORIZED")+
            " | Design Basis: "+r.DesignBasisReviewStatus+"\r\nMissing flow orientations: "+string.Join(", ",r.MissingFlowOrientationConduitIds)+
            "\r\nMissing outfall boundaries: "+string.Join(", ",r.MissingOutfallDecisionNodeIds)+"\r\n"+string.Join("\r\n",r.BlockingReasons.Select(b=>b.Code+": "+b.Detail));
        internal static string PresentAutomaticProposal(GravityVerticalDesignProposalResolver.Result result)
        {
            if(result==null)return "No automatic proposal. Preview required.";
            if(result.Status!="PROPOSED")return "Automatic proposal status: "+result.Status+"\r\nReasons:\r\n"+string.Join("\r\n",result.Reasons);
            var output=new StringBuilder("PROPOSED — NOT APPROVED\r\nShallowest feasible invert profile\r\n");
            output.Append("Objective ID/version: ").Append(result.Objective).Append(" / ").Append(result.ObjectiveRevision)
                .Append("\r\nProposal fingerprint: ").Append(result.ProposalFingerprint)
                .Append("\r\nCandidate fingerprint: ").Append(result.CandidateFingerprint)
                .Append("\r\nNetwork fingerprint: ").Append(result.NetworkFingerprint)
                .Append("\r\nConstraint fingerprint: ").Append(result.ConstraintFingerprint)
                .Append("\r\nDesign Basis content fingerprint: ").Append(result.DesignBasisContentFingerprint);
            Func<double?,string> number=value=>value.HasValue&&!double.IsNaN(value.Value)&&!double.IsInfinity(value.Value)?value.Value.ToString("R",CultureInfo.InvariantCulture):"Unavailable";
            foreach(var endpoints in result.Endpoints.GroupBy(e=>e.ConduitId).OrderBy(e=>e.Key))
            {
                var start=endpoints.SingleOrDefault(e=>e.End==ConduitEndKind.Start);var stop=endpoints.SingleOrDefault(e=>e.End==ConduitEndKind.Stop);
                var derived=result.DerivedConduits.SingleOrDefault(c=>c.ConduitId==endpoints.Key);
                output.Append("\r\n\r\nConduit ").Append(endpoints.Key.ToString(CultureInfo.InvariantCulture))
                    .Append("\r\nBentley Start node: ").Append(start?.NodeId.ToString(CultureInfo.InvariantCulture)??"Unavailable")
                    .Append("\r\nBentley Stop node: ").Append(stop?.NodeId.ToString(CultureInfo.InvariantCulture)??"Unavailable")
                    .Append("\r\nProposed Start invert (ft): ").Append(number(start?.ProposedInvert))
                    .Append("\r\nProposed Stop invert (ft): ").Append(number(stop?.ProposedInvert))
                    .Append("\r\nDesign From node: ").Append(derived?.FromNodeId.ToString(CultureInfo.InvariantCulture)??"Unavailable")
                    .Append("\r\nDesign To node: ").Append(derived?.ToNodeId.ToString(CultureInfo.InvariantCulture)??"Unavailable")
                    .Append("\r\nSlope ratio: ").Append(number(derived?.Slope))
                    .Append("\r\nAvailable Start cover (ft): ").Append(number(derived?.StartCover))
                    .Append("\r\nAvailable Stop cover (ft): ").Append(number(derived?.StopCover))
                    .Append("\r\nSource observed Start invert (ft): ").Append(number(start?.ExistingInvert))
                    .Append("\r\nSource observed Stop invert (ft): ").Append(number(stop?.ExistingInvert));
            }
            output.Append("\r\n\r\nProposal provenance: ").Append(result.Provenance==null?"Unavailable":GravityExecutionPlanBuilder.Json(result.Provenance))
                .Append("\r\nResolver evidence / Constraint explanation:\r\n").Append(string.Join("\r\n",result.Evidence));
            return output.ToString();
        }
        internal static string PresentCandidate(GravityDesignCandidate c)=>"Candidate: "+c.CandidateId+"\r\nFingerprint: "+c.CandidateFingerprint+"\r\nRecipe: "+c.RecipeId+
            "\r\nNetwork: "+c.SourceNetworkFingerprint+"\r\nConstraints: "+c.VerticalConstraintFingerprint+"\r\nAffected conduits: "+string.Join(", ",c.EndpointDesigns.Select(e=>e.ConduitId).Distinct())+
            "\r\n"+string.Join("\r\n",c.EndpointDesigns.Select(e=>e.ToString()));
        internal static string PresentCandidates(GravityDesignCandidateSet c)=>string.Join("\r\n",c.RecipeResults.Select(r=>r.RecipeId+": "+r.Status+" "+string.Join("; ",r.Issues)))+
            "\r\n"+string.Join("\r\n\r\n",c.Candidates.Select(PresentCandidate));
    }
}
