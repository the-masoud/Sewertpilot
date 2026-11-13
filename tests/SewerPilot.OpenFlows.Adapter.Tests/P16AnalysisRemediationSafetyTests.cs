using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;
namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static class P16AnalysisRemediationSafetyTests
    {
        public sealed class Field
        {
            public string Name {get;set;}=FarzaneganHydraulicRemediationHost.CatalogSizeField;
            public int Calls;
            public bool Local;
            public void SetValue(int id,bool local,object value){Calls++;Local=local;}
        }
        public sealed class Conduit {public double ConduitDiameter {get;set;}}
        internal static int Run()
        {
            if(P16AnalysisRemediationHost.Targets.Length!=10 || P16AnalysisRemediationHost.Targets.Select(x=>x.Id).Distinct().Count()!=10 ||
                P14CatalogPointerPersistenceHost.ExpectedMutations(P16AnalysisRemediationHost.PrepareOperation)!=30 ||
                P14CatalogPointerPersistenceHost.IsOperation(P16AnalysisRemediationHost.ComputeOperation) ||
                !P14FarzaneganTestOnlyHost.IsPilot(P16AnalysisRemediationHost.ComputeOperation) ||
                FarzaneganHydraulicRemediationHost.ExpectedWrites(P16AnalysisRemediationHost.ComputeOperation)!=0)
                throw new Exception("Exact plan/budgets invalid.");
            int rejects=0;Action<Action> refuse=f=>{try{f();}catch(InvalidOperationException){rejects++;return;}throw new Exception("Unsafe write admitted.");};
            var target=P16AnalysisRemediationHost.Find(52);var field=new Field();
            AdapterWriteBinding.ApplyP16RemediationSize(field,52,target.TargetGuid);
            if(field.Calls!=1 || !field.Local)throw new Exception("Local catalogue selector not applied.");
            refuse(()=>AdapterWriteBinding.ApplyP16RemediationSize(field,240,target.TargetGuid));
            refuse(()=>AdapterWriteBinding.ApplyP16RemediationSize(field,154,target.TargetGuid));
            refuse(()=>AdapterWriteBinding.ApplyP16RemediationSize(field,56,target.TargetGuid));
            refuse(()=>AdapterWriteBinding.ApplyP16RemediationSize(field,52,"bad-guid"));
            field.Name="Other";refuse(()=>AdapterWriteBinding.ApplyP16RemediationSize(field,52,target.TargetGuid));
            if(field.Calls!=1)throw new Exception("Rejected selector reached native setter.");
            var conduit=new Conduit();AdapterWriteBinding.ApplyP16RemediationDiameter(conduit,52,target.TargetFt);
            refuse(()=>AdapterWriteBinding.ApplyP16RemediationDiameter(conduit,52,target.SourceFt));
            refuse(()=>AdapterWriteBinding.ApplyP16RemediationDiameter(conduit,240,1.0));
            refuse(()=>AdapterWriteBinding.ApplyP16RemediationDiameter(conduit,52,double.NaN));
            if(conduit.ConduitDiameter!=target.TargetFt)throw new Exception("Rejected diameter changed value.");
            field.Name="Physical_ConduitShapeLabel";AdapterWriteBinding.ApplyP16RemediationLabel(field,52,"Native formatter result");
            refuse(()=>AdapterWriteBinding.ApplyP16RemediationLabel(field,52,""));
            refuse(()=>AdapterWriteBinding.ApplyP16RemediationLabel(field,240,"Native formatter result"));
            var request=new HostRequest {SchemaVersion=1,Operation=P16AnalysisRemediationHost.PrepareOperation,
                WorkingCopyId="0123456789abcdef0123456789abcdef",Diagnostics=new Dictionary<string,string>{
                    ["FarzaneganPilotCopyId"]="0123456789abcdef0123456789abcdef",["FarzaneganPilotScenario"]="1",
                    ["FarzaneganPilotOptions"]="29",["FarzaneganPilotEngine"]="DelawareProductEngine",["FarzaneganPilotInputSHA256"]=new string('A',64)}};
            bool blocked=false;try{P16FarzaneganNativePilotHost.ValidateRequest(request);}catch(InvalidDataException){blocked=true;}
            if(!blocked)throw new Exception("Wrong source fingerprint accepted.");
            blocked=false;try{RuntimeHostProtocol.ValidateRequest(request);}catch(InvalidDataException){blocked=true;}
            if(!blocked)throw new Exception("External generic launch accepted.");
            Console.WriteLine("P16_ANALYSIS_REMEDIATION_SAFETY=PASS; exact10targets;30write/0solve prepare;0write/1solve compute; unsafe write rejections="+rejects+"; fingerprint/launch gates PASS");
            return 0;
        }
    }
}
