using System;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static class RemediationNativeSafetyTests
    {
        public sealed class DiameterFieldProbe
        {
            public string Name { get; set; } = "ConduitDiameter";
            public string WorkingUnitIndex { get; set; } = "Feet";
            public string StorageUnitIndex { get; set; } = "Feet";
            public int Calls; public int Id; public bool Local; public object Value;
            public void SetValue(int id, bool local, object value) {Id=id;Local=local;Value=value;Calls++;}
        }
        // Stands in for the qualified alternative-scoped catalog-size field.
        public sealed class CatalogSizeFieldProbe
        {
            public string Name { get; set; } = FarzaneganHydraulicRemediationHost.CatalogSizeField;
            public int Calls; public int Id; public bool Local; public object Value;
            public void SetValue(int id, bool local, object value) {Id=id;Local=local;Value=value;Calls++;}
        }
        // Stands in for the native ConduitElement that carries the effective section diameter.
        public sealed class ConduitElementProbe
        {
            public double ConduitDiameter { get; set; }
        }
        internal static int Run()
        {
            if(P14NativeSafetyTests.Run()!=0) return 2;
            // The remediation route is no longer blocked by the catalog binding itself, but it must
            // still be unreachable without the internal launch and its exact bounded request context.
            bool blocked=false;
            try { RuntimeHostProtocol.ValidateRequest(new HostRequest {Operation=FarzaneganHydraulicRemediationHost.Operation,SchemaVersion=1}); }
            catch(System.IO.InvalidDataException) {blocked=true;}
            if(!blocked) throw new Exception("Remediation requires internal launch and controlled request context.");
            blocked=false;
            try { P16FarzaneganNativePilotHost.ValidateRequest(new HostRequest {Operation=FarzaneganHydraulicRemediationHost.Operation}); }
            catch(System.IO.InvalidDataException) {blocked=true;}
            if(!blocked) throw new Exception("Remediation requires its exact bounded native request.");
            if(FarzaneganHydraulicRemediationHost.ExpectedWrites(P16FarzaneganNativePilotHost.Operation)!=0 ||
                FarzaneganHydraulicRemediationHost.ExpectedWrites(P14FarzaneganTestOnlyHost.Operation)!=4 ||
                FarzaneganHydraulicRemediationHost.ExpectedWrites(FarzaneganHydraulicRemediationHost.Operation)!=15)
                throw new Exception("Operation write budgets changed.");
            if(FarzaneganHydraulicRemediationHost.Targets.Length!=11 ||
                FarzaneganHydraulicRemediationHost.Targets.Select(t=>t.Id).Distinct().Count()!=11)
                throw new Exception("The eleven-conduit allowlist changed.");
            foreach(var t in FarzaneganHydraulicRemediationHost.Targets)
                if(t.TargetFt<=t.SourceFt ||
                    !FarzaneganHydraulicRemediationHost.IsAllowlistedDiameter(t.Id,t.TargetFt) ||
                    FarzaneganHydraulicRemediationHost.IsAllowlistedDiameter(t.Id,t.SourceFt))
                    throw new Exception("Allowlist entry is not a strict, gated enlargement: "+t.Id);

            // The element-local diameter setter must accept only the allowlisted (id, diameter) pairs.
            var element=new ConduitElementProbe();
            var first=FarzaneganHydraulicRemediationHost.Targets[0];
            AdapterWriteBinding.ApplyTestOnlyConduitElementDiameter(element,first.Id,first.TargetFt);
            if(element.ConduitDiameter!=first.TargetFt)
                throw new Exception("Allowlisted element-local diameter write incorrect.");
            Action<Action> refuseElement=action=> {try {action();} catch(InvalidOperationException) {return;} throw new Exception("Unsafe element-local diameter write accepted.");};
            refuseElement(()=>AdapterWriteBinding.ApplyTestOnlyConduitElementDiameter(element,154,first.TargetFt));
            refuseElement(()=>AdapterWriteBinding.ApplyTestOnlyConduitElementDiameter(element,first.Id,first.SourceFt));
            refuseElement(()=>AdapterWriteBinding.ApplyTestOnlyConduitElementDiameter(element,first.Id,first.TargetFt+0.01));
            refuseElement(()=>AdapterWriteBinding.ApplyTestOnlyConduitElementDiameter(null,first.Id,first.TargetFt));
            if(element.ConduitDiameter!=first.TargetFt)
                throw new Exception("Rejected element-local diameter write reached the native property.");
            if(FarzaneganHydraulicRemediationHost.CatalogSizeField!="CatalogPipeType_CatalogPipeSize" ||
                FarzaneganHydraulicRemediationHost.TwelveInchCatalogSizeGuid!="61634a43-f8e2-4469-a1eb-c47fedbf8c6a" ||
                FarzaneganHydraulicRemediationHost.TenInchCatalogSizeGuid!="54919863-b228-4f75-b7e2-4e5e6a82a7f4")
                throw new Exception("Qualified catalog selector identity changed.");

            var size=new CatalogSizeFieldProbe();
            string id=FarzaneganHydraulicRemediationHost.TwelveInchCatalogSizeGuid;
            AdapterWriteBinding.ApplyTestOnlyCatalogSize(size,150,id,typeof(string));
            if(size.Id!=150 || !size.Local || size.Calls!=1 || (string)size.Value!=id)
                throw new Exception("Qualified catalog selection incorrect.");
            Action<Action> refuseSize=action=> {try {action();} catch(InvalidOperationException) {return;} throw new Exception("Unsafe catalog selection accepted.");};
            refuseSize(()=>AdapterWriteBinding.ApplyTestOnlyCatalogSize(size,154,id,typeof(string)));
            refuseSize(()=>AdapterWriteBinding.ApplyTestOnlyCatalogSize(size,150,
                FarzaneganHydraulicRemediationHost.TenInchCatalogSizeGuid,typeof(string)));
            refuseSize(()=>AdapterWriteBinding.ApplyTestOnlyCatalogSize(size,150,"00000000-0000-0000-0000-000000000000",typeof(string)));
            refuseSize(()=>AdapterWriteBinding.ApplyTestOnlyCatalogSize(size,150,id,typeof(Guid)));
            refuseSize(()=>AdapterWriteBinding.ApplyTestOnlyCatalogSize(size,150,id,typeof(double)));
            size.Name="ConduitDiameter";
            refuseSize(()=>AdapterWriteBinding.ApplyTestOnlyCatalogSize(size,150,id,typeof(string)));
            if(size.Calls!=1) throw new Exception("Rejected catalog selection reached native setter.");

            var diameter=new DiameterFieldProbe();
            AdapterWriteBinding.ApplyTestOnlyConduitDiameter(diameter,150,1.0);
            if(diameter.Calls!=1 || diameter.Id!=150 || !diameter.Local || (double)diameter.Value!=1.0)
                throw new Exception("Element-local diameter setter incorrect.");
            Action<Action> refuse=action=> {try {action();} catch(InvalidOperationException) {return;} throw new Exception("Unsafe diameter write accepted.");};
            refuse(()=>AdapterWriteBinding.ApplyTestOnlyConduitDiameter(diameter,154,1.0));
            refuse(()=>AdapterWriteBinding.ApplyTestOnlyConduitDiameter(diameter,150,1.5));
            diameter.WorkingUnitIndex="Meters";
            refuse(()=>AdapterWriteBinding.ApplyTestOnlyConduitDiameter(diameter,150,1.0));
            diameter.WorkingUnitIndex="Feet";diameter.StorageUnitIndex="Meters";
            refuse(()=>AdapterWriteBinding.ApplyTestOnlyConduitDiameter(diameter,150,1.0));
            diameter.StorageUnitIndex="Feet";diameter.Name="CatalogConduitDiameter";
            refuse(()=>AdapterWriteBinding.ApplyTestOnlyConduitDiameter(diameter,150,1.0));
            if(diameter.Calls!=1) throw new Exception("Rejected write reached native setter.");
            Console.WriteLine("REMEDIATION_NATIVE_SAFETY=PASS; controlled launch, operation budgets, eleven-conduit allowlist, qualified catalog selector, wrong element/value/type/field rejection");
            return 0;
        }
    }
}
