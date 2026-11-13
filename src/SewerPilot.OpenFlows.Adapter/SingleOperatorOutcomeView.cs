using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    // Read-only projection of committed controller/host evidence. Unknown values never become successful claims.
    internal sealed class SingleOperatorOutcomeView
    {
        internal string Classification { get; private set; }
        internal string Text { get; private set; }
        internal string ExistingEvidenceDirectory { get; private set; }
        internal GravityHydraulicResultPresentation HydraulicPresentation { get; private set; }
        private static string Value(object value) => value == null ? "Unknown / not evidenced" : Convert.ToString(value,CultureInfo.InvariantCulture);
        internal static SingleOperatorOutcomeView Create(GravityProductionExecutionOutcome outcome, SingleOperatorPreview preview,
            HostResult result = null, Dictionary<string,object> watchdog = null, string evidenceError = null)
        {
            var plan=preview.Materialize(); var d=result?.Diagnostics;
            Func<string,string> diagnostic=k=>d!=null&&d.TryGetValue(k,out string v)?v:null;
            string classification=outcome.Status=="PASS"?"PASS":outcome.NoChanges?"NOT_EXECUTED":
                (outcome.Status??"").StartsWith("FAIL",StringComparison.Ordinal)?"FAIL":"BLOCKED";
            int? attempted=result!=null?(int?)result.MutationInvocationCount:outcome.Status=="PASS"||outcome.NoChanges?(int?)outcome.FieldWrites:null;
            string verified=diagnostic("ImmediateReadbackVerified")=="true"?diagnostic("EndpointInvertPropertySetCount"):outcome.NoChanges?"0":null;
            string saved=diagnostic("SaveCurrentProjectSucceeded"), reopened=diagnostic("ReopenVerificationSuccess");
            string cleanup=result!=null?Value(result.VendorCleanupCompleted&&result.StaCompleted):null;
            string termination=watchdog!=null&&watchdog.TryGetValue("remaining_runtime_processes",out object remaining)?Value(remaining):null;

            GravityHydraulicResultPresentation hydraulicPresentation = null;
            string hydraulicError = null;
            if (outcome.Status == "PASS" && outcome.ContentAuthority != null && outcome.HydraulicResults != null)
            {
                try
                {
                    List<HydraulicVerificationCriterion> criteria = null;
                    if (plan.Inputs?.DesignBasis != null)
                    {
                        try
                        {
                            criteria = HydraulicVerificationCriteria.ReadFromDesignBasis(plan.Inputs.DesignBasis);
                        }
                        catch (DesignBasisValidationException)
                        {
                            // If DesignBasis.HydraulicCriteria contains non-structured entries or general settings,
                            // criteria remains null so genuine results are still presented with truthful missing-criteria notice.
                            criteria = null;
                        }
                    }
                    hydraulicPresentation = GravityHydraulicVerificationWorkflow.VerifyProductionResult(
                        plan, outcome.ContentAuthority, outcome.HydraulicResults, criteria);
                }
                catch (Exception ex)
                {
                    hydraulicError = ex.Message;
                    hydraulicPresentation = GravityHydraulicVerificationWorkflow.Unavailable(plan, "Production result verification failed: " + ex.Message);
                }
            }
            else
            {
                hydraulicPresentation = GravityHydraulicVerificationWorkflow.Unavailable(plan,
                    "No accepted result-access runtime evidence establishes genuine hydraulic results.");
            }

            string hydraulicTable = FormatHydraulicTable(outcome.HydraulicResults, hydraulicPresentation);
            var view=new SingleOperatorOutcomeView {Classification=classification,
                ExistingEvidenceDirectory=Directory.Exists(outcome.EvidenceDirectory)?Path.GetFullPath(outcome.EvidenceDirectory):null,
                HydraulicPresentation=hydraulicPresentation};
            view.Text="Classification: "+classification+"\r\nExecution status: "+Value(outcome.Status)+"\r\nBlocker/error: "+(outcome.Blocker??"None reported")+
                "\r\nProject: "+plan.Inputs.ProjectId+"\r\nSource model: "+preview.ModelPath+"\r\nSource model fingerprint: "+plan.Inputs.ModelFingerprint+
                "\r\nWorking model: "+(outcome.WorkingModelPath??"Not evidenced")+
                "\r\nSource scenario / Physical alternative: "+plan.SourceScenarioId+" / "+plan.SourcePhysicalAlternativeId+
                "\r\nIsolated scenario / Physical alternative: "+Value(diagnostic("CreatedScenarioID"))+" / "+Value(diagnostic("CreatedPhysicalAlternativeID"))+
                "\r\nAttempted field writes: "+Value(attempted)+"\r\nReadback-verified endpoint writes: "+Value(verified)+
                "\r\nLocalization readback: "+Value(diagnostic("LocalizationReadbackVerified"))+
                "\r\nSave invocation count: "+Value(result!=null?(int?)result.SaveInvocationCount:outcome.Status=="PASS"||outcome.NoChanges?(int?)outcome.Saves:null)+
                "\r\nSave succeeded / fresh-session reopen verified / source isolation: "+Value(saved)+" / "+Value(reopened)+" / "+Value(diagnostic("SourceIsolationVerified"))+
                "\r\nSolver invocation count: "+Value(result!=null?(int?)result.SolverRunInvocationCount:outcome.Status=="PASS"||outcome.NoChanges?(int?)outcome.SolverRuns:null)+
                "\r\nCompute status: "+Value(diagnostic("ScenarioComputeStatus")??outcome.ComputeStatus)+"\r\nCompute status text: "+Value(diagnostic("ScenarioStatusText"))+
                "\r\nController-reported runtime invocations / PID / duration seconds: "+outcome.RuntimeInvocations+" / "+Value(outcome.RuntimePid)+" / "+Value(outcome.RuntimeSeconds)+
                "\r\nVendor/STA cleanup / remaining runtime processes: "+Value(cleanup)+" / "+Value(termination)+
                "\r\nBaseline integrity verified: "+Value(outcome.BaselineIntegrityVerified)+
                "\r\nEvidence: "+(view.ExistingEvidenceDirectory??"No existing evidence directory")+
                (evidenceError==null?"":"\r\nEvidence projection warning: "+evidenceError)+
                (hydraulicError==null?"":"\r\nHydraulic verification warning: "+hydraulicError)+
                "\r\n"+hydraulicPresentation.Describe().TrimEnd()+
                (string.IsNullOrEmpty(hydraulicTable) ? "" : "\r\n" + hydraulicTable) +
                "\r\nA successful calculation does not grant engineering design approval.";
            return view;
        }

        private static string FormatHydraulicTable(HydraulicResultSet results, GravityHydraulicResultPresentation presentation)
        {
            if (results == null || results.Elements == null || results.Elements.Count == 0) return null;
            var sb = new StringBuilder();
            sb.AppendLine("--- HYDRAULIC OBSERVATIONS (" + results.Elements.Count + " elements) ---");
            var conduits = results.Elements.Where(e => e.ElementKind == "Conduit").OrderBy(e => e.ElementId).ToList();
            if (conduits.Count > 0)
            {
                sb.AppendLine("Conduits (" + conduits.Count + "):");
                foreach (var c in conduits)
                {
                    string flow = MetricStr(c.FindMetric(HydraulicMetricKind.Flow));
                    string cap = MetricStr(c.FindMetric(HydraulicMetricKind.Capacity));
                    string vel = MetricStr(c.FindMetric(HydraulicMetricKind.Velocity));
                    string depth = MetricStr(c.FindMetric(HydraulicMetricKind.Depth));
                    string slope = MetricStr(c.FindMetric(HydraulicMetricKind.Slope));
                    string cover = MetricStr(c.FindMetric(HydraulicMetricKind.Cover));
                    string sur = c.FindMetric(HydraulicMetricKind.Surcharge)?.BooleanValue?.ToString() ?? "N/A";
                    sb.AppendLine("  Conduit " + c.ElementId + ": Flow=" + flow + ", Cap=" + cap + ", Vel=" + vel + ", Depth=" + depth + ", Slope=" + slope + ", Cover=" + cover + ", Surcharge=" + sur);
                }
            }
            var structures = results.Elements.Where(e => e.ElementKind != "Conduit").OrderBy(e => e.ElementId).ToList();
            if (structures.Count > 0)
            {
                sb.AppendLine("Structures (" + structures.Count + "):");
                foreach (var s in structures)
                {
                    string hgl = MetricStr(s.FindMetric(HydraulicMetricKind.Hgl));
                    sb.AppendLine("  " + s.ElementKind + " " + s.ElementId + ": HGL=" + hgl);
                }
            }
            if (presentation?.Report != null && presentation.Report.Findings.Count > 0)
            {
                sb.AppendLine("--- HYDRAULIC VERIFICATION FINDINGS (" + presentation.Report.Findings.Count + " findings) ---");
                foreach (var f in presentation.Report.Findings)
                {
                    string actual = f.ActualValue.HasValue ? f.ActualValue.Value.ToString("F4", CultureInfo.InvariantCulture) + " " + (f.ActualUnit ?? "") : "NotAvailable";
                    string threshold = f.RequiredThreshold.HasValue ? f.RequiredThreshold.Value.ToString("F4", CultureInfo.InvariantCulture) : "N/A";
                    sb.AppendLine("  [" + f.Classification + "] Element " + f.ElementId + " (" + (f.ElementKind ?? "") + ") " + f.CriterionKey + ": actual=" + actual.Trim() + ", required=" + f.RequiredOperator + " " + threshold + " " + f.RequiredUnit + (string.IsNullOrEmpty(f.Reason) ? "" : " (" + f.Reason + ")"));
                }
            }
            return sb.ToString().TrimEnd();
        }

        private static string MetricStr(HydraulicMetricResult m)
        {
            if (m == null || !m.HasUsableValue || !m.NumericValue.HasValue) return "NotAvailable";
            return m.NumericValue.Value.ToString("F4", CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(m.Unit) ? "" : " " + m.Unit);
        }

        internal static SingleOperatorOutcomeView Load(GravityProductionExecutionOutcome outcome, SingleOperatorPreview preview)
        {
            HostResult result=null; Dictionary<string,object> watchdog=null; string warning=null;
            try
            {
                if(Directory.Exists(outcome.EvidenceDirectory))
                {
                    var root=outcome.EvidenceDirectory; WorkingCopyIntegrity.RejectLinks(root);
                    string requestPath=Path.Combine(root,"request.json"),resultPath=Path.Combine(root,"result.json"),executionPath=Path.Combine(root,"execution.json");
                    if(File.Exists(requestPath)&&File.Exists(resultPath))
                    {
                        var request=RuntimeHostProtocol.Serializer().Deserialize<HostRequest>(ReadBounded(requestPath));
                        GravityExecutionPlanBuilder.Require(GravityExecutionPlanBuilder.Json(request.EngineeringPlan)==preview.FrozenPlan,"OUTCOME_PLAN_MISMATCH");
                        var parsed=RuntimeHostProtocol.ParseResult(resultPath,request);
                        GravityExecutionPlanBuilder.Require(outcome.RuntimePid.HasValue&&RuntimeHostClient.CommittedMarker(Path.Combine(root,"stages.jsonl"),outcome.RuntimePid.Value),"OUTCOME_COMMIT_MARKER");
                        result=parsed;
                        if (outcome.ContentAuthority == null && request.EngineeringPlan != null && request.EngineeringPlan.Calculate &&
                            result.Diagnostics.TryGetValue("ProductionReadbackVerified", out string prv) && prv == "true")
                        {
                            outcome.ContentAuthority = P16ResultContentAuthority.CaptureCompletedProductionResult(request, result);
                            if (outcome.HydraulicResults == null && result.Diagnostics.TryGetValue("HydraulicResultSetJson", out string json) && !string.IsNullOrEmpty(json))
                            {
                                outcome.HydraulicResults = outcome.ContentAuthority.CreateResultSet(
                                    request.WorkingCopyId,
                                    request.EngineeringPlan.NetworkFingerprint,
                                    request.EngineeringPlan.PlanFingerprint);
                            }
                        }
                    }
                    if(File.Exists(executionPath))
                    {
                        var parsed=RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string,object>>(ReadBounded(executionPath));
                        GravityExecutionPlanBuilder.Require(outcome.RuntimePid.HasValue&&parsed.TryGetValue("pid",out object pid)&&Convert.ToInt32(pid)==outcome.RuntimePid.Value,"OUTCOME_RUNTIME_MISMATCH");
                        watchdog=parsed;
                    }
                }
            }
            catch(Exception error){warning=error.Message;}
            return Create(outcome,preview,result,watchdog,warning);
        }
        private static string ReadBounded(string path)
        {
            WorkingCopyIntegrity.RejectLinks(path);
            GravityExecutionPlanBuilder.Require(new FileInfo(path).Length<=8*1024*1024,"OUTCOME_EVIDENCE_SIZE");
            return File.ReadAllText(path);
        }
    }
    internal sealed class SingleOperatorCheckpointObserver : IDisposable
    {
        private readonly object gate=new object();
        private readonly IProgress<string> progress;
        private FileSystemWatcher watcher;
        private string path;
        private int lastNumber;
        private bool disposed;
        internal SingleOperatorCheckpointObserver(IProgress<string> progress){this.progress=progress;}
        internal void Attach(string directory)
        {
            lock(gate)
            {
                if(disposed)return;
                try
                {
                    WorkingCopyIntegrity.RejectLinks(directory);path=Path.Combine(directory,"stages.jsonl");
                    watcher=new FileSystemWatcher(directory,"stages.jsonl"){NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size};
                    watcher.Created+=(s,e)=>Capture();watcher.Changed+=(s,e)=>Capture();
                    watcher.Error+=(s,e)=>progress?.Report("Checkpoint observation unavailable: "+e.GetException().Message);
                    watcher.EnableRaisingEvents=true;Capture();
                }
                catch(Exception error){progress?.Report("Checkpoint observation unavailable: "+error.Message);}
            }
        }
        internal void Capture()
        {
            lock(gate)
            {
                if(disposed||path==null||!File.Exists(path))return;
                try
                {
                    WorkingCopyIntegrity.RejectLinks(path);
                    if(new FileInfo(path).Length>2*1024*1024){progress?.Report("Checkpoint journal exceeds observation bound.");return;}
                    using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
                    using(var reader=new StreamReader(file))
                    {
                        string line;int lines=0;
                        while((line=reader.ReadLine())!=null&&++lines<=2000)
                        {
                            Dictionary<string,object> entry;
                            try {entry=RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string,object>>(line);}catch(ArgumentException){continue;}
                            if(entry==null||!entry.TryGetValue("stage_number",out object raw)||!entry.ContainsKey("stage_name")||!entry.ContainsKey("state")||!entry.ContainsKey("utc"))continue;
                            int number=Convert.ToInt32(raw);if(number<=lastNumber)continue;lastNumber=number;
                            progress?.Report("Runtime checkpoint: "+entry["stage_name"]+" / "+entry["state"]+" / "+entry["utc"]);
                        }
                    }
                }
                catch(Exception error){progress?.Report("Checkpoint observation unavailable: "+error.Message);}
            }
        }
        public void Dispose()
        {
            FileSystemWatcher w = null;
            lock(gate)
            {
                if(disposed) return;
                disposed = true;
                w = watcher;
                watcher = null;
            }
            if (w != null)
            {
                try { w.EnableRaisingEvents = false; } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in SingleOperatorOutcomeView.cs: " + suppressedEx.Message); }
                w.Dispose();
            }
        }
    }
}
