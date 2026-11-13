using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    // Explicit laboratory route. No engineering plan/approval or production Execute authority.
    internal static class P14FarzaneganTestOnlyHost
    {
        internal const string Operation = "P14FarzaneganTestOnlyNative";
        internal const string LaunchSwitch = "--p14-farzanegan-test-only-native";
        internal static bool IsPilot(string operation) => operation == Operation || operation == P16FarzaneganNativePilotHost.Operation || operation==P14CatalogPointerPersistenceHost.ScopedComputeOperation || operation==P16AnalysisRemediationHost.ComputeOperation || operation == FarzaneganHydraulicRemediationHost.Operation || P14CatalogPointerPersistenceHost.IsOperation(operation);
        internal static readonly int[] Ids = {144,154,160,242};
        internal static readonly double[] Original = {340.31409411175559,338.35147963844088,336.58552257325778,337.52269918735277};
        internal static readonly double[] Proposed = {340.31648539772328,338.35689748389285,336.58953434309507,337.52976089141407};
        internal sealed class Endpoint
        {
            public int Id { get; set; }
            public double Start { get; set; }
            public double Stop { get; set; }
            public bool SetStartToNode { get; set; }
            public bool SetStopToNode { get; set; }
        }
        internal static object Manager(object dataset)
        {
            int type = GravityNetworkReadMapper.ResolveElementTypeId(GravityNetworkReadMapper.ResolveDomainElementType(), "ConduitElementManager");
            if(type < 0) throw new InvalidDataException("Conduit capability missing.");
            return dataset.GetType().GetMethod("DomainElementManager", new[]{typeof(int)}).Invoke(dataset,new object[]{type});
        }
        internal static Endpoint[] Snapshot(object dataset)
        {
            object manager = Manager(dataset);
            object ids = manager.GetType().GetMethod("ElementIDs", Type.EmptyTypes).Invoke(manager,null);
            var index = WaterObjectsLifecycleHost.Indexer(ids.GetType());
            int count = ScenarioAlternativeCapabilityMapper.GetCollectionCount(ids);
            if(count != 71) throw new InvalidDataException("Expected exactly 71 conduits.");
            var rows = new List<Endpoint>();
            for(int i=0;i<count;i++)
            {
                int id = Convert.ToInt32(index.GetValue(ids,new object[]{i}),CultureInfo.InvariantCulture);
                object c = manager.GetType().GetMethod("Element",new[]{typeof(int)}).Invoke(manager,new object[]{id});
                rows.Add(new Endpoint {Id=id,
                    Start=(double)c.GetType().GetProperty("Physical_UpstreamInvert").GetValue(c,null),
                    Stop=(double)c.GetType().GetProperty("Physical_DownstreamInvert").GetValue(c,null),
                    SetStartToNode=GravityWriteAndSolverHost.ReadInheritanceFlag(c,"Physical_SetInvertToUpstream"),
                    SetStopToNode=GravityWriteAndSolverHost.ReadInheritanceFlag(c,"Physical_SetInvertToDownstream")});
            }
            return rows.OrderBy(r=>r.Id).ToArray();
        }
        internal static void Apply(object dataset, object project, HostRequest request, ModelReadEvidence evidence, object scenario)
        {
            P16FarzaneganNativePilotHost.ValidateRequest(request);
            if(request.Operation != Operation && request.Operation != FarzaneganHydraulicRemediationHost.Operation &&
                request.Operation != P14CatalogPointerPersistenceHost.PrepareOperation) throw new InvalidDataException("TEST_ONLY operation required.");
            string open = project.GetType().GetProperty("FullPath").GetValue(project,null) as string;
            if(!P16SaveQuestionPolicy.SamePath(open,request.WorkingModelPath)) throw new InvalidDataException("Native write target mismatch.");
            int physicalType = ScenarioAlternativeIsolationHost.ResolvePhysicalAlternativeTypeId(dataset);
            int physical = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(scenario,physicalType);
            if(physical != 6) throw new InvalidDataException("Expected observed Physical alternative 6.");
            object manager = Manager(dataset);
            object field = GravityNetworkReadMapper.ResolveDomainElementField(manager,"Physical_UpstreamInvert");
            object down = GravityNetworkReadMapper.ResolveDomainElementField(manager,"Physical_DownstreamInvert");
            foreach(object f in new[]{field,down})
                foreach(string member in new[]{"WorkingUnitIndex","StorageUnitIndex"})
                    if(Convert.ToString(f.GetType().GetProperty(member).GetValue(f,null),CultureInfo.InvariantCulture) != "Feet")
                        throw new InvalidDataException("Native invert units must be Feet.");
            var before = Snapshot(dataset);
            evidence.Diagnostics["P14NativeBeforeJson"] = RuntimeHostProtocol.Serializer().Serialize(before);
            evidence.Diagnostics["P14PhysicalAlternative"] = physical.ToString();
            evidence.Diagnostics["P14InvertWorkingAndStorageUnits"] = "Feet";
            for(int i=0;i<Ids.Length;i++)
            {
                var row=before.Single(r=>r.Id==Ids[i]);
                if(Math.Abs(row.Start-Original[i])>1e-10 || row.SetStartToNode || row.SetStopToNode)
                    throw new InvalidDataException("Original native endpoint or inheritance mismatch: "+Ids[i]);
            }
            evidence.Diagnostics["PropertySetCount"]="0";
            for(int i=0;i<Ids.Length;i++)
            {
                evidence.Diagnostics["PropertySetCount"]=(i+1).ToString();
                AdapterWriteBinding.ApplyTestOnlyConduitStart(field,Ids[i],Proposed[i]);
            }
            var after=Snapshot(dataset);
            foreach(var row in after)
            {
                var prior=before.Single(r=>r.Id==row.Id);
                int i=Array.IndexOf(Ids,row.Id);
                if(Math.Abs(row.Start-(i<0?prior.Start:Proposed[i]))>1e-10 || row.Stop!=prior.Stop ||
                    row.SetStartToNode!=prior.SetStartToNode || row.SetStopToNode!=prior.SetStopToNode)
                    throw new InvalidDataException("Exact four-change native readback failed: "+row.Id);
            }
            evidence.Diagnostics["P14NativeAfterJson"]=RuntimeHostProtocol.Serializer().Serialize(after);
            evidence.Diagnostics["P14NativeWriteReadbackVerified"]="true";
        }
    }
}
