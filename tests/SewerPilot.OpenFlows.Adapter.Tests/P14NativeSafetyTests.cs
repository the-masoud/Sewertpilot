using System;
using System.Collections.Generic;
using System.IO;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static class P14NativeSafetyTests
    {
        public sealed class FieldProbe
        {
            public int Id; public bool Local; public object Value; public int Calls;
            public void SetValue(int id, bool local, object value) { Id=id; Local=local; Value=value; Calls++; }
        }
        internal static int Run()
        {
            int count=0;
            Action<Action> reject=action=> { try { action(); } catch(InvalidDataException) {count++; return;} throw new Exception("Unsafe request accepted."); };
            reject(()=>P16FarzaneganNativePilotHost.ValidateRequest(null));
            reject(()=>P16FarzaneganNativePilotHost.ValidateRequest(new HostRequest { Operation="GravityWriteAndSolver", SchemaVersion=1 }));
            var r=new HostRequest { Operation=P14FarzaneganTestOnlyHost.Operation,SchemaVersion=1,
                WorkingCopyId="0123456789abcdef0123456789abcdef",Diagnostics=new Dictionary<string,string> {
                    ["FarzaneganPilotCopyId"]="0123456789abcdef0123456789abcdef",["FarzaneganPilotScenario"]="1",
                    ["FarzaneganPilotOptions"]="29",["FarzaneganPilotEngine"]="DelawareProductEngine",
                    ["FarzaneganPilotInputSHA256"]=new string('A',64)}};
            reject(()=>P16FarzaneganNativePilotHost.ValidateRequest(r));
            r.WriteAuthorizationTokenId="production-token";
            reject(()=>P16FarzaneganNativePilotHost.ValidateRequest(r));
            r.WriteAuthorizationTokenId=null;
            reject(()=>RuntimeHostProtocol.ValidateRequest(r)); // Explicit internal launch required.
            var f=new FieldProbe();
            AdapterWriteBinding.ApplyTestOnlyConduitStart(f,144,340.31648539772328);
            if(f.Calls!=1 || f.Id!=144 || !f.Local || (double)f.Value!=340.31648539772328) throw new Exception("Native setter signature incorrect.");
            Console.WriteLine("P14_NATIVE_SAFETY=PASS; rejected_requests="+count+"; localized_setter=PASS");
            return 0;
        }
    }
}
