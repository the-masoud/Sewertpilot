using System;
using System.Collections.Generic;
using System.IO;
using SewerPilot.OpenFlows.Adapter;
namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static class Co40NativeLabelSafetyTests
    {
        public sealed class LabelField
        {
            public string Name {get;set;}="Physical_ConduitShapeLabel";
            public int Id;public int Calls;public bool Local;public object Value;
            public void SetValue(int id,bool local,object value){Id=id;Local=local;Value=value;Calls++;}
        }
        public enum GVFCalculationType {Analysis=0,Design=1}
        public sealed class OptionField
        {
            public string Name {get;set;}="GvfCalculationType";
            public int Calls;
            public void SetValue(int id,object value){Calls++;}
        }
        internal static int Run()
        {
            var field=new LabelField();
            AdapterWriteBinding.ApplyTestOnlyNativeGeneratedConduitLabel(field,150,"Circle - 304.8 mm");
            if(field.Calls!=1 || field.Id!=150 || !field.Local || (string)field.Value!="Circle - 304.8 mm")
                throw new Exception("Exact local label gateway failed.");
            int rejected=0;
            Action<Action> refuse=action=>{try{action();}catch(InvalidOperationException){rejected++;return;}throw new Exception("Unsafe label write accepted.");};
            refuse(()=>AdapterWriteBinding.ApplyTestOnlyNativeGeneratedConduitLabel(field,154,"Circle - 304.8 mm"));
            refuse(()=>AdapterWriteBinding.ApplyTestOnlyNativeGeneratedConduitLabel(field,150,""));
            field.Name="ConduitDiameter";
            refuse(()=>AdapterWriteBinding.ApplyTestOnlyNativeGeneratedConduitLabel(field,150,"Circle - 304.8 mm"));
            if(field.Calls!=1)throw new Exception("Rejected call reached native setter.");
            if(P14CatalogPointerPersistenceHost.ExpectedMutations(P14CatalogPointerPersistenceHost.Operation)!=2 ||
                P14CatalogPointerPersistenceHost.ExpectedMutations(P14CatalogPointerPersistenceHost.LabelRepairOperation)!=1)
                throw new Exception("Persistence operation budgets changed.");
            var request=new HostRequest {SchemaVersion=1,Operation=P14CatalogPointerPersistenceHost.LabelRepairOperation,
                WorkingCopyId="0123456789abcdef0123456789abcdef",Diagnostics=new Dictionary<string,string>{
                    ["FarzaneganPilotCopyId"]="0123456789abcdef0123456789abcdef",["FarzaneganPilotScenario"]="1",
                    ["FarzaneganPilotOptions"]="29",["FarzaneganPilotEngine"]="DelawareProductEngine",
                    ["FarzaneganPilotInputSHA256"]=new string('A',64)}};
            bool invalid=false;try{P16FarzaneganNativePilotHost.ValidateRequest(request);}catch(InvalidDataException){invalid=true;}
            if(!invalid)throw new Exception("Unqualified input accepted.");
            invalid=false;try{RuntimeHostProtocol.ValidateRequest(request);}catch(InvalidDataException){invalid=true;}
            if(!invalid)throw new Exception("Unqualified launch accepted.");
            if(P14CatalogPointerPersistenceHost.ExpectedMutations(P14CatalogPointerPersistenceHost.PrepareOperation)!=4 ||
                !P14CatalogPointerPersistenceHost.IsOperation(P14CatalogPointerPersistenceHost.PrepareOperation))
                throw new Exception("Prepare-only must require four writes with the save-only zero-solver budget.");
            request.Operation=P14CatalogPointerPersistenceHost.PrepareOperation;
            invalid=false;try{P16FarzaneganNativePilotHost.ValidateRequest(request);}catch(InvalidDataException){invalid=true;}
            if(!invalid)throw new Exception("Prepare-only admitted an unqualified input fingerprint.");
            invalid=false;try{RuntimeHostProtocol.ValidateRequest(request);}catch(InvalidDataException){invalid=true;}
            if(!invalid)throw new Exception("Prepare-only admitted a generic external launch.");
            if(!P14CatalogPointerPersistenceHost.RegistryReportsSize(new[]{"hasSizeField=True"}) ||
                !P14CatalogPointerPersistenceHost.RegistryReportsSize(new[]{"hasSizeField=true"}) ||
                P14CatalogPointerPersistenceHost.RegistryReportsSize(new[]{"hasSizeField=False"}))
                throw new Exception("Registry Boolean case regression.");
            if(P14CatalogPointerPersistenceHost.ExpectedMutations(P14CatalogPointerPersistenceHost.ScopedRepairOperation)!=3 ||
                P14CatalogPointerPersistenceHost.IsOperation(P14CatalogPointerPersistenceHost.ScopedComputeOperation) ||
                !P14FarzaneganTestOnlyHost.IsPilot(P14CatalogPointerPersistenceHost.ScopedComputeOperation) ||
                FarzaneganHydraulicRemediationHost.ExpectedWrites(P14CatalogPointerPersistenceHost.ScopedComputeOperation)!=0)
                throw new Exception("Scoped repair/compute budget isolation failed.");
            request.Operation=P14CatalogPointerPersistenceHost.ScopedRepairOperation;
            invalid=false;try{P16FarzaneganNativePilotHost.ValidateRequest(request);}catch(InvalidDataException){invalid=true;}
            if(!invalid)throw new Exception("Scoped repair admitted wrong input digest.");
            invalid=false;try{RuntimeHostProtocol.ValidateRequest(request);}catch(InvalidDataException){invalid=true;}
            if(!invalid)throw new Exception("Scoped repair admitted external launch.");
            var option=new OptionField();
            AdapterWriteBinding.ApplyTestOnlyGvfAnalysisMode(option,29,GVFCalculationType.Analysis);
            refuse(()=>AdapterWriteBinding.ApplyTestOnlyGvfAnalysisMode(option,28,GVFCalculationType.Analysis));
            refuse(()=>AdapterWriteBinding.ApplyTestOnlyGvfAnalysisMode(option,29,GVFCalculationType.Design));
            option.Name="Other";refuse(()=>AdapterWriteBinding.ApplyTestOnlyGvfAnalysisMode(option,29,GVFCalculationType.Analysis));
            if(option.Calls!=1 || P14CatalogPointerPersistenceHost.ExpectedMutations(P14CatalogPointerPersistenceHost.AnalysisPrepareOperation)!=1)
                throw new Exception("Analysis mode scope/budget failed.");
            Console.WriteLine("CO40_NATIVE_LABEL_SAFETY=PASS; local label write; wrong ID/field/empty text rejected="+rejected+"; input/launch gates PASS; budgets 1/2");
            return 0;
        }
    }
}
