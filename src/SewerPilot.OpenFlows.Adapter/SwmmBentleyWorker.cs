using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using SewerPilot.OpenFlows.Compatibility;

namespace SewerPilot.OpenFlows.Adapter
{
    // Each vendor run owns a process: a timeout cannot leave native STA state in the desktop.
    public static class SwmmBentleyWorker
    {
        public const string LaunchSwitch = "--swmm-bentley-worker";
        public const string LifecycleProbeSwitch = "--swmm-bentley-lifecycle-probe";
        public sealed class Request
        {
            public string InputPath { get; set; }
            public string CaseName { get; set; }
            public string WorkDirectory { get; set; }
        }

        internal static string ResolveRuntimeRoot()
        {
            var options = new OpenFlowsDiscoveryOptions(Environment.GetEnvironmentVariable("SEWERPILOT_OPENFLOWS_INSTALL_ROOT"));
            var selection = new OpenFlowsRuntimeSelector().Select(new OpenFlowsRuntimeDiscovery().Discover(options), options);
            if (selection.Status != OpenFlowsRuntimeSelectionStatus.Selected)
                throw new InvalidOperationException("BENTLEY_RUNTIME_UNAVAILABLE: " + selection.Status + ": " + selection.Reason);
            return selection.Candidate.InstallRoot;
        }

        internal static BentleyLiveExecutionResult Execute(string input, string name, string workDirectory, int timeoutMilliseconds = 120000)
        {
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || name == "." || name == "..")
                throw new ArgumentException("Case name must be a file name.");
            string host = Path.Combine(Path.GetDirectoryName(typeof(SwmmBentleyWorker).Assembly.Location), "SewerPilot.OpenFlows.RuntimeHost.exe");
            if (!File.Exists(host)) throw new FileNotFoundException("Bentley process-isolation host missing.", host);
            string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "SwmmBentleyWorker", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string requestPath = Path.Combine(directory, "request.json"), responsePath = Path.Combine(directory, "result.json");
            var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            File.WriteAllText(requestPath, serializer.Serialize(new Request { InputPath = Path.GetFullPath(input), CaseName = name, WorkDirectory = Path.GetFullPath(workDirectory) }));
            var start = new ProcessStartInfo(host, LaunchSwitch + " \"" + requestPath + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(host)
            };
            start.EnvironmentVariables.Remove("ATRIA_API_KEY");
            using (var job = new WorkerJob())
            using (var process = Process.Start(start))
            {
                try
                {
                    job.Assign(process);
                    // Child cannot invoke Bentley until its process belongs to the kill-on-close job.
                    File.WriteAllText(Path.Combine(directory, "assigned"), "assigned");
                    WaitForWorker(process, job, timeoutMilliseconds);
                    if (!File.Exists(responsePath)) throw new InvalidDataException("BENTLEY_WORKER_RESULT_MISSING: exit " + process.ExitCode);
                    var result = serializer.Deserialize<BentleyLiveExecutionResult>(File.ReadAllText(responsePath));
                    if (process.ExitCode != 0 || result == null || !result.Success)
                        throw new InvalidOperationException("BENTLEY_WORKER_FAILED: " + result?.ErrorMessage);
                    // Reparse retained artifacts after process termination; getter-only dictionaries are not IPC DTOs.
                    result.Report = SwmmCrossSolverValidationEngine.ParseBentleySwmmReport(result.EngineReportPath, result.StswPath + ".sqlite");
                    File.WriteAllText(result.EngineReportPath + ".element-map.json", serializer.Serialize(
                        SwmmCrossSolverValidationEngine.ReadElementLabelMapFromSqlite(result.StswPath + ".sqlite")));
                    return result;
                }
                finally
                {
                    if (!process.HasExited) { job.Terminate(); process.WaitForExit(5000); }
                }
            }
        }

        private static void WaitForWorker(Process process, WorkerJob job, int timeoutMilliseconds)
        {
            if (process.WaitForExit(timeoutMilliseconds))
            {
                if (!job.WaitUntilEmpty(5000))
                {
                    job.Terminate();
                    if (!job.WaitUntilEmpty(5000)) throw new InvalidOperationException("BENTLEY_WORKER_TERMINATION_UNCONFIRMED");
                }
                return;
            }
            job.Terminate();
            if (!process.WaitForExit(5000) || !job.WaitUntilEmpty(5000))
                throw new InvalidOperationException("BENTLEY_WORKER_TERMINATION_UNCONFIRMED");
            var timeout = new TimeoutException("BENTLEY_WORKER_TIMEOUT: owned process job terminated.");
            timeout.Data["TerminationConfirmed"] = true;
            throw timeout;
        }

        // The probe has no model input, vendor binding, result output, or successful parity route.
        internal static bool VerifyTimeoutIsolation()
        {
            string host = Path.Combine(Path.GetDirectoryName(typeof(SwmmBentleyWorker).Assembly.Location), "SewerPilot.OpenFlows.RuntimeHost.exe");
            var start = new ProcessStartInfo(host, LifecycleProbeSwitch) { UseShellExecute = false, CreateNoWindow = true };
            start.EnvironmentVariables.Remove("ATRIA_API_KEY");
            using (var job = new WorkerJob())
            using (var process = Process.Start(start))
            {
                job.Assign(process);
                try { WaitForWorker(process, job, 100); }
                catch (TimeoutException timeout) { return process.HasExited && Equals(timeout.Data["TerminationConfirmed"], true); }
                return false;
            }
        }

        public static int RunLifecycleProbe()
        {
            Thread.Sleep(30000);
            return 4;
        }

        public static int Run(string requestPath)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(requestPath));
            var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            BentleyLiveExecutionResult result;
            try
            {
                var wait = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(directory, "assigned")))
                {
                    if (wait.ElapsedMilliseconds > 10000) return 3;
                    Thread.Sleep(20);
                }
                var request = serializer.Deserialize<Request>(File.ReadAllText(requestPath));
                result = SwmmCrossSolverValidationEngine.ExecuteBentleyInWorker(request.InputPath, Path.Combine(directory, "model"), request.CaseName, request.WorkDirectory);
            }
            catch (Exception error) { result = new BentleyLiveExecutionResult { Success = false, ErrorMessage = error.ToString() }; }
            File.WriteAllText(Path.Combine(directory, "result.json"), serializer.Serialize(result));
            return result.Success ? 0 : 1;
        }

        private sealed class WorkerJob : IDisposable
        {
            private IntPtr handle;
            public WorkerJob()
            {
                handle = CreateJobObject(IntPtr.Zero, null);
                if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
                int size = Marshal.SizeOf(limits);
                IntPtr data = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(limits, data, false);
                    if (!SetInformationJobObject(handle, 9, data, (uint)size)) throw new System.ComponentModel.Win32Exception();
                }
                catch { Dispose(); throw; }
                finally { Marshal.FreeHGlobal(data); }
            }
            public void Assign(Process process)
            {
                if (!AssignProcessToJobObject(handle, process.Handle))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                    throw new System.ComponentModel.Win32Exception();
                }
            }
            public void Terminate() { if (handle != IntPtr.Zero && !TerminateJobObject(handle, 1)) throw new System.ComponentModel.Win32Exception(); }
            public bool WaitUntilEmpty(int milliseconds)
            {
                var timer = Stopwatch.StartNew();
                do
                {
                    Accounting counts;
                    if (!QueryInformationJobObject(handle, 1, out counts, (uint)Marshal.SizeOf(typeof(Accounting)), IntPtr.Zero))
                        throw new System.ComponentModel.Win32Exception();
                    if (counts.ActiveProcesses == 0) return true;
                    Thread.Sleep(20);
                } while (timer.ElapsedMilliseconds < milliseconds);
                return false;
            }
            public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
            [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
            {
                public long ProcessTime, JobTime;
                public uint Flags;
                public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint Priority, Scheduling;
            }
            [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
            [StructLayout(LayoutKind.Sequential)] private struct Accounting
            {
                public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
                public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
            }
            [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
            {
                public BasicLimits Basic;
                public IoCounters Io;
                public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
            }
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint length);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(IntPtr job, uint code);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(IntPtr job, int informationClass, out Accounting information, uint length, IntPtr returnedLength);
            [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        }
    }
}
