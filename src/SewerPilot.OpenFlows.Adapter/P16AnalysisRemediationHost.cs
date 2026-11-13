using System;
using System.Globalization;
using System.IO;
using System.Linq;
namespace SewerPilot.OpenFlows.Adapter
{
    // Fixed TEST_ONLY plan from the proven Analysis baseline. No production design authority.
    internal static class P16AnalysisRemediationHost
    {
        internal const string PrepareOperation="P16FarzaneganAnalysisRemediationPrepare";
        internal const string ComputeOperation="P16FarzaneganAnalysisRemediationCompute";
        internal const string InputDigest="7D34D76E135209399A064FAFBB645662134AE751675DB6653CD33D1FA96E1865";
        internal const int ExpectedWrites=30;
        internal sealed class Target
        {
            public int Id;public double SourceFt,TargetFt;public string SourceGuid,TargetGuid,TargetLabel;
        }
        internal static readonly Target[] Targets={
            new Target {Id=52,SourceFt=0.6666666666666667,TargetFt=0.833333333333333,SourceGuid="5791999b-5fb5-45fd-9448-3e53a9d1ab77",TargetGuid="54919863-b228-4f75-b7e2-4e5e6a82a7f4",TargetLabel="10 inch"},
            new Target {Id=56,SourceFt=1,TargetFt=1.166666666662,SourceGuid="61634a43-f8e2-4469-a1eb-c47fedbf8c6a",TargetGuid="4495c0c2-91ca-4394-935e-19d55493e9b4",TargetLabel="14 inch"},
            new Target {Id=63,SourceFt=0.6666666666666667,TargetFt=0.833333333333333,SourceGuid="5791999b-5fb5-45fd-9448-3e53a9d1ab77",TargetGuid="54919863-b228-4f75-b7e2-4e5e6a82a7f4",TargetLabel="10 inch"},
            new Target {Id=66,SourceFt=0.8333333333333335,TargetFt=1.0,SourceGuid="54919863-b228-4f75-b7e2-4e5e6a82a7f4",TargetGuid="61634a43-f8e2-4469-a1eb-c47fedbf8c6a",TargetLabel="12 inch"},
            new Target {Id=70,SourceFt=1.1666666666620003,TargetFt=1.333333333328,SourceGuid="4495c0c2-91ca-4394-935e-19d55493e9b4",TargetGuid="0ae18b6f-eb4a-4cf0-8e46-6c54fbfd8568",TargetLabel="16 inch"},
            new Target {Id=74,SourceFt=1.5000000000000002,TargetFt=1.66666666666,SourceGuid="7c47ab83-71fe-446b-bdee-f95b905367d8",TargetGuid="8ce9bc94-e13a-4321-b735-8909f7a48f76",TargetLabel="20 inch"},
            new Target {Id=99,SourceFt=0.6666666666666667,TargetFt=0.833333333333333,SourceGuid="5791999b-5fb5-45fd-9448-3e53a9d1ab77",TargetGuid="54919863-b228-4f75-b7e2-4e5e6a82a7f4",TargetLabel="10 inch"},
            new Target {Id=150,SourceFt=1,TargetFt=1.166666666662,SourceGuid="61634a43-f8e2-4469-a1eb-c47fedbf8c6a",TargetGuid="4495c0c2-91ca-4394-935e-19d55493e9b4",TargetLabel="14 inch"},
            new Target {Id=234,SourceFt=1.66666666666,TargetFt=1.833333333326,SourceGuid="8ce9bc94-e13a-4321-b735-8909f7a48f76",TargetGuid="7864a056-9aa4-448c-b5fd-c1b86667e9a2",TargetLabel="22 inch"},
            new Target {Id=268,SourceFt=0.6666666666666667,TargetFt=0.833333333333333,SourceGuid="5791999b-5fb5-45fd-9448-3e53a9d1ab77",TargetGuid="54919863-b228-4f75-b7e2-4e5e6a82a7f4",TargetLabel="10 inch"},
        };
        internal static Target Find(int id)=>Targets.SingleOrDefault(x=>x.Id==id);
        internal static bool MatchSize(int id,string guid)=>Find(id)!=null && string.Equals(Find(id).TargetGuid,guid,StringComparison.OrdinalIgnoreCase);
        internal static bool MatchDiameter(int id,double ft)=>Find(id)!=null && Math.Abs(Find(id).TargetFt-ft)<1e-12;
        private static string Json(object x)=>RuntimeHostProtocol.Serializer().Serialize(x);
        private static P14FarzaneganTestOnlyHost.Endpoint[] Endpoints(object dataset)
        {
            if(P14CatalogPointerPersistenceHost.GvfMode(dataset)!="Analysis")throw new InvalidDataException("Remediation requires fixed-candidate Analysis mode.");
            var ends=P14FarzaneganTestOnlyHost.Snapshot(dataset);
            for(int n=0;n<P14FarzaneganTestOnlyHost.Ids.Length;n++)
            {
                var x=ends.Single(e=>e.Id==P14FarzaneganTestOnlyHost.Ids[n]);
                if(Math.Abs(x.Start-P14FarzaneganTestOnlyHost.Proposed[n])>1e-10 || x.SetStartToNode || x.SetStopToNode)
                    throw new InvalidDataException("Four P14 starts/inheritance must remain verified.");
            }
            return ends;
        }
        private static void Section(FarzaneganHydraulicRemediationHost.Section row,Target target,bool after)
        {
            if(Math.Abs(row.DiameterFt-(after?target.TargetFt:target.SourceFt))>1e-10 ||
                !string.Equals(row.CatalogSizeGuid,after?target.TargetGuid:target.SourceGuid,StringComparison.OrdinalIgnoreCase) ||
                row.CatalogId!="125" || row.ConduitTypeValue!=1 || row.Shape!="CircularPipe" || row.Material!="GRVE" ||
                Math.Abs(row.ManningsN-.01)>1e-12 || row.UseLocalShapeLabel || string.IsNullOrWhiteSpace(row.ShapeLabel))
                throw new InvalidDataException("Exact remediation section precondition/readback failed: "+row.Id);
        }
        internal static void Prepare(object dataset,object project,object scenario,ModelReadEvidence evidence)
        {
            var ends=Endpoints(dataset);var before=FarzaneganHydraulicRemediationHost.PersistedSections(dataset,scenario);
            var manager=P14FarzaneganTestOnlyHost.Manager(dataset);
            var diameterField=P14NativeConduitLabelFormatter.PhysicalField(manager,"ConduitDiameter");
            foreach(string unit in new[]{"WorkingUnitIndex","StorageUnitIndex"})
                if(Convert.ToString(diameterField.GetType().GetProperty(unit).GetValue(diameterField,null),CultureInfo.InvariantCulture)!="Feet")
                    throw new InvalidDataException("Remediation diameter must use native Feet.");
            object size=FarzaneganHydraulicRemediationHost.CatalogSizeFieldHandle(dataset,scenario);
            var sizes=FarzaneganHydraulicRemediationHost.CatalogSizes(dataset);
            foreach(var t in Targets)
            {
                Section(before.Single(x=>x.Id==t.Id),t,false);
                if(!sizes.TryGetValue(t.TargetGuid,out string[] c) || c[0]!=t.TargetLabel ||
                    Math.Abs(double.Parse(c[1],CultureInfo.InvariantCulture)-t.TargetFt)>1e-10 || Math.Abs(double.Parse(c[2],CultureInfo.InvariantCulture)-.01)>1e-12)
                    throw new InvalidDataException("Target catalogue entry mismatch: "+t.Id);
            }
            evidence.Diagnostics["P16RemediationEndpointsBeforeJson"]=Json(ends);
            evidence.Diagnostics["P16RemediationSectionsBeforeJson"]=Json(before);
            int count=0;
            foreach(var t in Targets)
            {
                object conduit=manager.GetType().GetMethod("Element",new[]{typeof(int)}).Invoke(manager,new object[]{t.Id});
                AdapterWriteBinding.ApplyP16RemediationSize(size,t.Id,t.TargetGuid);evidence.Diagnostics["PropertySetCount"]=(++count).ToString();
                AdapterWriteBinding.ApplyP16RemediationDiameter(conduit,t.Id,t.TargetFt);evidence.Diagnostics["PropertySetCount"]=(++count).ToString();
                var generated=P14NativeConduitLabelFormatter.Generate(dataset,project,manager,t.Id);
                AdapterWriteBinding.ApplyP16RemediationLabel(P14NativeConduitLabelFormatter.PhysicalField(manager,"Physical_ConduitShapeLabel"),t.Id,generated.GeneratedLabel);
                evidence.Diagnostics["PropertySetCount"]=(++count).ToString();
                evidence.Diagnostics["P16NativeFormatter"+t.Id]=Json(generated);
            }
            var after=FarzaneganHydraulicRemediationHost.PersistedSections(dataset,scenario);
            foreach(var x in after)
            {
                var t=Find(x.Id);var prior=before.Single(y=>y.Id==x.Id);
                if(t==null){if(Json(x)!=Json(prior))throw new InvalidDataException("Non-target section changed: "+x.Id);}
                else
                {
                    Section(x,t,true);
                    if(x.ConduitType!=prior.ConduitType || x.Shape!=prior.Shape || x.Material!=prior.Material || x.ManningsN!=prior.ManningsN || x.CatalogId!=prior.CatalogId)
                        throw new InvalidDataException("Non-allowlisted section attribute changed: "+x.Id);
                }
            }
            if(Json(Endpoints(dataset))!=Json(ends))throw new InvalidDataException("Remediation changed inverts/inheritance.");
            if(after.Any(x=>Math.Abs(x.DiameterFt-double.Parse(x.CatalogSizeInsideDiameterFt,CultureInfo.InvariantCulture))>1e-10))
                throw new InvalidDataException("Remediation catalogue invariant failed.");
            evidence.Diagnostics["P16RemediationSectionsAfterJson"]=Json(after);
            evidence.Diagnostics["P16RemediationEndpointsAfterJson"]=Json(ends);
            evidence.Diagnostics["P14NativeWriteReadbackVerified"]="true";
        }
        internal static void VerifyCompute(object dataset,object scenario,ModelReadEvidence evidence,string stage)
        {
            var ends=Endpoints(dataset);var sections=FarzaneganHydraulicRemediationHost.PersistedSections(dataset,scenario);
            foreach(var t in Targets)Section(sections.Single(x=>x.Id==t.Id),t,true);
            if(sections.Any(x=>Math.Abs(x.DiameterFt-double.Parse(x.CatalogSizeInsideDiameterFt,CultureInfo.InvariantCulture))>1e-10))throw new InvalidDataException("Catalogue invariant failed at "+stage);
            string e=Json(ends),s=Json(sections);
            if(stage!="BeforeBinding" && (e!=evidence.Diagnostics["P16RemediationBeforeBindingEndpointsJson"] || s!=evidence.Diagnostics["P16RemediationBeforeBindingSectionsJson"]))
                throw new InvalidDataException("Fixed-candidate geometry changed at "+stage);
            evidence.Diagnostics["P16Remediation"+stage+"EndpointsJson"]=e;
            evidence.Diagnostics["P16Remediation"+stage+"SectionsJson"]=s;
            evidence.Diagnostics["P16Remediation"+stage+"Mode"]="Analysis";
        }
    }
}
