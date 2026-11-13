using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SewerPilot.OpenFlows.Adapter
{
    // A small immutable artifact envelope. Engineering payloads retain existing domain/wire formats.
    internal sealed class EngineeringDesignArtifact
    {
        public int SchemaVersion { get; set; } = 1;
        public string ProjectId { get; set; }
        public string NetworkFingerprint { get; set; }
        public string DesignBasisFingerprint { get; set; }
        public string ConstraintFingerprint { get; set; }
        public GravityEngineeringInputs Inputs { get; set; }
        public string RecipesJson { get; set; }
        public string RecipeConstraintFingerprint { get; set; }
        public string PreviousReference { get; set; }
    }
    internal static class EngineeringDesignStorage
    {
        private const string Prefix = "ui02-";
        internal static bool IsOwned(string reference) => reference?.StartsWith(Prefix, StringComparison.Ordinal) == true;
        private static string PathFor(string directory, string reference)
        {
            if (!Regex.IsMatch(reference ?? "", "^ui02-[a-f0-9]{64}$")) throw new ProjectStorageException("ENGINEERING_REFERENCE_INVALID", "Engineering reference is malformed.");
            var root=Path.GetFullPath(directory); WorkingCopyIntegrity.RejectLinks(root);
            string folder=Path.Combine(root,"engineering"); WorkingCopyIntegrity.RejectLinks(folder);
            string path=Path.Combine(folder,reference+".json"); WorkingCopyIntegrity.RejectLinks(path); return path;
        }
        internal static EngineeringDesignArtifact Load(string directory, ProjectState state)
        {
            if (!IsOwned(state.EngineeringDecisionReferenceId)) return null;
            try
            {
                string path=PathFor(directory,state.EngineeringDecisionReferenceId);
                if(!File.Exists(path))throw new ProjectStorageException("ENGINEERING_ARTIFACT_MISSING","Referenced engineering content is missing.");
                if(new FileInfo(path).Length>16*1024*1024)throw new ProjectStorageException("ENGINEERING_ARTIFACT_INVALID","Engineering artifact exceeds the accepted serializer limit.");
                string json=File.ReadAllText(path,Encoding.UTF8);
                if(Prefix+GravityExecutionPlanBuilder.Hash(json)!=state.EngineeringDecisionReferenceId)
                    throw new ProjectStorageException("ENGINEERING_CONTENT_IDENTITY_MISMATCH","Engineering content does not match its immutable reference.");
                var artifact=RuntimeHostProtocol.Serializer().Deserialize<EngineeringDesignArtifact>(json);
                Validate(artifact,state);
                return artifact;
            }
            catch(ProjectStorageException){throw;}
            catch(Exception e){throw new ProjectStorageException("ENGINEERING_ARTIFACT_INVALID",e.GetBaseException().Message,e);}
        }
        private static void Validate(EngineeringDesignArtifact a, ProjectState state)
        {
            Action<bool,string> require=(valid,reason)=>{if(!valid)throw new ProjectStorageException("ENGINEERING_REFERENCE_MISMATCH",reason);};
            require(a!=null&&a.SchemaVersion==1&&a.ProjectId==state.ProjectId&&a.Inputs?.ProjectId==state.ProjectId,"Engineering project/schema identity mismatch.");
            var input=a.Inputs; var basis=input.DesignBasis;
            require(basis?.CommonDesignBasis!=null&&basis.CommonDesignBasis.ProjectId==state.ProjectId&&basis.Validate().IsValid,"Invalid Design Basis identity/content.");
            require(input.Approval==null,"Engineering workspace artifacts cannot carry execution approval.");
            require(string.Equals(input.ModelFingerprint,state.Model?.ProtectedBaselineFingerprint,StringComparison.OrdinalIgnoreCase),"Referenced model fingerprint changed.");
            // Legacy recovery (pre-separation projects whose shared reference a StormFlow save overwrote):
            // the GravityFlow Design Basis identity is derived from this immutable artifact, whose content
            // hash, project identity and model fingerprint were all verified above, so the reference is
            // recovered unambiguously rather than guessed. A shared reference that is neither this artifact's
            // identity nor a StormFlow reference still fails closed below. Recovery is read-only; the shared
            // reference is rewritten only by the next GravityFlow save.
            var gravityReference=state.DesignBasisReference;
            if(StormFlowDesignBasisWorkspace.IsStormFlowReference(gravityReference?.ReferenceIdentifier))
                gravityReference=basis.CommonDesignBasis.ToProjectReference();
            require(gravityReference?.ReferenceIdentifier==basis.CommonDesignBasis.DesignBasisId&&gravityReference.DesignBasisVersion==basis.CommonDesignBasis.DesignBasisVersion,"Project Design Basis reference mismatch.");
            require(a.DesignBasisFingerprint==basis.CreateSnapshot().GravityContentFingerprint,"Design Basis fingerprint mismatch.");
            if(input.SourceRead==null){require(string.IsNullOrWhiteSpace(a.NetworkFingerprint)&&string.IsNullOrWhiteSpace(a.ConstraintFingerprint),"Network lineage is unavailable.");return;}
            var network=new GravityNetworkIngestionService().Ingest(input.SourceRead,input.ModelFingerprint,schemaVersion:2);
            require(a.NetworkFingerprint==network.NetworkFingerprint,"Source network fingerprint mismatch.");
            var constraints=GravityExecutionPlanBuilder.ValidateEngineeringConstraints(input,network);
            require(a.ConstraintFingerprint==constraints.VerticalConstraintFingerprint,"Constraint fingerprint mismatch.");
            foreach(var recipe in ReadRecipes(a.RecipesJson))
            {
                GravityExecutionPlanBuilder.ValidateEngineeringProvenance(recipe.Provenance);
                foreach(var s in recipe.EndpointSelections) GravityExecutionPlanBuilder.ValidateEngineeringProvenance(s.Provenance);
            }
            if(!string.IsNullOrWhiteSpace(input.CandidateJson))
            {
                var feasibility=new GravityVerticalFeasibilityEngine().Analyze(network,constraints);
                require(GravityExecutionPlanBuilder.Json(feasibility)==input.FeasibilityJson,"Stored P13 result is stale.");
                var generation=GravityEngineeringWire.Read<GravityCandidateGenerationRequest>(RuntimeHostProtocol.Serializer().DeserializeObject(input.GenerationRequestJson));
                GravityExecutionPlanBuilder.ValidateEngineeringGeneration(generation,constraints,feasibility);
                var generated=new GravityCandidateGenerator().Generate(network,constraints,feasibility,generation);
                require(generated.Candidates.Any(c=>GravityExecutionPlanBuilder.Json(c)==input.CandidateJson),"Stored candidate/recipe lineage is stale or mismatched.");
            }
        }
        internal static List<GravityCandidateRecipe> ReadRecipes(string json) => string.IsNullOrWhiteSpace(json)?new List<GravityCandidateRecipe>():
            GravityEngineeringWire.Read<IEnumerable<GravityCandidateRecipe>>(RuntimeHostProtocol.Serializer().DeserializeObject(json)).ToList();
        internal static EngineeringDesignWorkspace OpenWorkspace(ProjectWorkspace project)
        {
            var a=Load(project.DirectoryPath,project.State);
            return new EngineeringDesignWorkspace(project.State.ProjectId,project.State.Model?.ProtectedBaselineFingerprint,project.Network,
                a?.Inputs,a==null?null:ReadRecipes(a.RecipesJson),a?.RecipeConstraintFingerprint);
        }
        internal static void PersistEngineeringState(ProjectWorkspace project, EngineeringDesignWorkspace design)
        {
            EngineeringDesignWorkspace.Require(!project.Reading&&!design.SourceChanged,"CURRENT_SOURCE_REQUIRED");
            project.ModelReferenceStatus();
            var basis=design.Basis;
            EngineeringDesignWorkspace.Require(basis!=null&&basis.Validate().IsValid,"DRAFT_STORAGE_VALIDATION: "+string.Join("; ",basis?.Validate().Issues??new List<DesignBasisValidationIssue>()));
            var copy=RuntimeHostProtocol.Serializer().Deserialize<ProjectState>(GravityExecutionPlanBuilder.Json(project.State));
            copy.DesignBasisReference=basis.CommonDesignBasis.ToProjectReference();
            var artifact=new EngineeringDesignArtifact { ProjectId=copy.ProjectId, Inputs=RuntimeHostProtocol.Serializer().Deserialize<GravityEngineeringInputs>(GravityExecutionPlanBuilder.Json(design.Inputs)),
                NetworkFingerprint=design.Inputs.SourceRead==null?null:new GravityNetworkIngestionService().Ingest(design.Inputs.SourceRead,design.Inputs.ModelFingerprint,schemaVersion:2).NetworkFingerprint,
                DesignBasisFingerprint=basis.CreateSnapshot().GravityContentFingerprint,
                ConstraintFingerprint=design.Inputs.SourceRead==null?null:design.Constraints?.VerticalConstraintFingerprint,
                RecipesJson=GravityExecutionPlanBuilder.Json(design.Recipes),RecipeConstraintFingerprint=design.RecipeConstraintFingerprint,
                PreviousReference=project.State.EngineeringDecisionReferenceId };
            artifact.Inputs.Approval=null;
            string json=GravityExecutionPlanBuilder.Json(artifact); string reference=Prefix+GravityExecutionPlanBuilder.Hash(json);
            copy.EngineeringDecisionReferenceId=reference; copy.SelectedModule="GravityFlow";
            Validate(artifact,copy);
            string path=PathFor(project.DirectoryPath,reference); Directory.CreateDirectory(Path.GetDirectoryName(path));
            string lockPath=Path.Combine(project.DirectoryPath,".engineering-save.lock"); WorkingCopyIntegrity.RejectLinks(lockPath);
            using(var lease=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                var stored=ProjectStore.LoadProject(project.DirectoryPath);
                if(stored.ProjectId!=copy.ProjectId||stored.StateRevision!=copy.StateRevision)
                    throw new ProjectStorageException("PROJECT_STATE_CONFLICT","Engineering save rejected a stale revision or changed project identity.");
                if(File.Exists(path))
                {
                    if(File.ReadAllText(path,Encoding.UTF8)!=json)throw new ProjectStorageException("ENGINEERING_CONTENT_IDENTITY_MISMATCH","Immutable engineering artifact must not be overwritten.");
                }
                else
                {
                    string stage=path+"."+Guid.NewGuid().ToString("N")+".staging";
                    try
                    {
                        byte[] data=new UTF8Encoding(false).GetBytes(json);
                        using(var file=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(data,0,data.Length);file.Flush(true);}
                        File.Move(stage,path);
                    }
                    finally{if(File.Exists(stage))File.Delete(stage);}
                }
                // Artifact is durable and verified before project.json can claim it. Existing atomic revision/audit path is authoritative.
                Load(project.DirectoryPath,copy);
                var saved=ProjectStore.SaveProject(project.DirectoryPath,copy,project.State.StateRevision,new AuditRecord {
                    ActionType=AuditActionType.ProjectStateUpdated,Module="GravityFlow",ModelFingerprint=copy.Model?.ProtectedBaselineFingerprint,
                    ScenarioId=copy.ScenarioAlternativeState?.SelectedScenarioId,
                    Payload=new Dictionary<string,string>{{"EngineeringArtifact",reference},{"DesignBasisVersion",basis.CommonDesignBasis.DesignBasisVersion}} });
                project.AcceptEngineeringSave(saved); design.MarkSaved();
            }
        }
    }
}
