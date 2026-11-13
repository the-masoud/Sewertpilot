using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SewerPilot.Mcp.Server.Tests
{
    // Unprivileged, process-local verification. No WMI subscription or Windows setting changes.
    internal sealed class OwnedProcessFence : IDisposable
    {
        private readonly SafeFileHandle job;
        private const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        private const int JobObjectBasicAccountingInformation = 1;
        private const int JobObjectBasicProcessIdList = 3;
        private const int JobObjectExtendedLimitInformation = 9;
        private readonly int serverPid, harnessPid;
        private readonly long serverCreatedFileTime;
        private readonly string serverImage;
        private readonly Dictionary<uint, VerifiedProcess> verifiedProcesses = new Dictionary<uint, VerifiedProcess>();
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TH32CS_SNAPPROCESS = 0x2;

        internal OwnedProcessFence(Process ownedServer, string expectedServerImage)
        {
            job = CreateJobObject(IntPtr.Zero, null);
            try
            {
                if (job.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new NativeJobException("CreateJobObjectW", error);
                }
                serverPid = ownedServer.Id;
                if (!GetOwnedProcessTimes(ownedServer.Handle, out NativeFileTime created, out NativeFileTime exited,
                    out NativeFileTime kernel, out NativeFileTime user))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new NativeJobException("GetProcessTimes(owned server)", error);
                }
                serverCreatedFileTime = created.Value;
                serverImage = Path.GetFullPath(expectedServerImage);
                using (var harness = Process.GetCurrentProcess())
                {
                    harnessPid = harness.Id;
                }
                var limits = new ExtendedLimits();
                limits.Basic.LimitFlags = JOB_OBJECT_LIMIT_ACTIVE_PROCESS | JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                limits.Basic.ActiveProcessLimit = 2; // Exact owned server plus at most one verified Windows Console Host.
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits))))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new NativeJobException("SetInformationJobObject(JobObjectExtendedLimitInformation)", error);
                }
                if (!AssignProcessToJobObject(job, ownedServer.Handle))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new NativeJobException("AssignProcessToJobObject", error);
                }
            }
            catch (Exception error)
            {
                // Preserve the original native failure before disposing handles or terminating the exact owned process.
                job.Dispose();
                try
                {
                    if (!ownedServer.HasExited) ownedServer.Kill();
                    error.Data["ownedProcessCleanup"] = ownedServer.WaitForExit(5000) ? "PASS" : "TIMEOUT";
                }
                catch (Exception cleanupError)
                {
                    error.Data["ownedProcessCleanup"] = cleanupError.GetType().Name;
                }
                throw;
            }
        }

        internal uint TotalProcesses
        {
            get => ReadAccounting().TotalProcesses;
        }

        private Accounting ReadAccounting()
        {
            if (!QueryAccounting(job, JobObjectBasicAccountingInformation, out Accounting info,
                (uint)Marshal.SizeOf(typeof(Accounting)), IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                throw new NativeJobException("QueryInformationJobObject(JobObjectBasicAccountingInformation)", error);
            }
            return info;
        }

        internal JobSnapshot CaptureSnapshot(int expectedServerPid)
        {
            DateTime started = DateTime.UtcNow;
            Accounting accounting = ReadAccounting();
            // Fixed bounded buffer; failed/truncated queries stop without retry.
            const int capacity = 16;
            int firstPidOffset = Marshal.OffsetOf(typeof(ProcessIdListHeader), nameof(ProcessIdListHeader.FirstProcessId)).ToInt32();
            int bufferBytes = firstPidOffset + capacity * IntPtr.Size;
            IntPtr buffer = Marshal.AllocHGlobal(bufferBytes);
            uint assigned, returned, returnedBytes;
            ulong[] processIds;
            try
            {
                if (!QueryProcessIds(job, JobObjectBasicProcessIdList, buffer, (uint)bufferBytes, out returnedBytes))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new NativeJobException("QueryInformationJobObject(JobObjectBasicProcessIdList)", error);
                }
                assigned = unchecked((uint)Marshal.ReadInt32(buffer, 0));
                returned = unchecked((uint)Marshal.ReadInt32(buffer, 4));
                if (returned > capacity || assigned > returned || returnedBytes < firstPidOffset + returned * IntPtr.Size)
                    throw new InvalidOperationException("JOB_PROCESS_ID_LIST_INCOMPLETE");
                processIds = new ulong[returned];
                for (int index = 0; index < processIds.Length; index++)
                    processIds[index] = unchecked((ulong)Marshal.ReadInt64(buffer, firstPidOffset + index * IntPtr.Size));
            }
            finally { Marshal.FreeHGlobal(buffer); }

            if (!QueryLimits(job, JobObjectExtendedLimitInformation, out ExtendedLimits limits,
                (uint)Marshal.SizeOf(typeof(ExtendedLimits)), IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                throw new NativeJobException("QueryInformationJobObject(JobObjectExtendedLimitInformation)", error);
            }
            return new JobSnapshot
            {
                startedUtc = started, finishedUtc = DateTime.UtcNow, expectedServerPid = expectedServerPid,
                totalProcesses = accounting.TotalProcesses, activeProcesses = accounting.ActiveProcesses,
                totalTerminatedProcesses = accounting.TotalTerminatedProcesses,
                numberOfAssignedProcesses = assigned, numberOfProcessIdsInList = returned,
                processIds = processIds, processIdBufferBytes = bufferBytes, processIdReturnedBytes = returnedBytes,
                limitFlags = "0x" + limits.Basic.LimitFlags.ToString("X8"),
                activeProcessLimit = limits.Basic.ActiveProcessLimit,
                breakawayAllowed = (limits.Basic.LimitFlags & (0x800 | 0x1000)) != 0
            };
        }

        internal void VerifyProcessAllowlist(JobSnapshot snapshot, bool serverMustBeActive, Action<object> recordEvidence)
        {
            if (snapshot.limitFlags != "0x00002008" || snapshot.activeProcessLimit != 2 || snapshot.breakawayAllowed)
                throw new ProcessEvidenceException("JOB_SECURITY_LIMITS_CHANGED");
            if (snapshot.totalProcesses < 1 || snapshot.totalProcesses > 2 || snapshot.activeProcesses > 2 || snapshot.totalTerminatedProcesses != 0)
                throw new ProcessEvidenceException("UNEXPECTED_PROCESS_ASSOCIATION_OR_LIMIT_VIOLATION");
            if (snapshot.activeProcesses != snapshot.processIds.Length || snapshot.numberOfAssignedProcesses != snapshot.numberOfProcessIdsInList)
                throw new ProcessEvidenceException("JOB_PROCESS_MEMBERSHIP_INCONSISTENT");
            if (serverMustBeActive ? !snapshot.processIds.Contains((ulong)serverPid) : snapshot.processIds.Length != 0)
                throw new ProcessEvidenceException("OWNED_PROCESS_LIFECYCLE_MISMATCH");

            uint[] pids = snapshot.processIds.Select(id => checked((uint)id)).ToArray();
            var parents = pids.Length == 0 ? new Dictionary<uint, uint>() : ReadParentPids(pids);
            foreach (uint pid in pids)
            {
                SafeFileHandle handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    throw new NativeJobException("OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION), PID=" + pid, error);
                }
                try
                {
                    long creation = ReadCreationTime(handle, pid);
                    var identity = new ProcessIdentity
                    {
                        ProcessId = pid, ParentProcessId = parents[pid], Image = ReadImage(handle, pid),
                        CreationFileTime = creation, CreationUtc = DateTime.FromFileTimeUtc(creation), ObservedUtc = DateTime.UtcNow
                    };
                    recordEvidence(new { observation = identity });
                    if (creation <= 0 || creation > DateTime.UtcNow.ToFileTimeUtc() || ReadCreationTime(handle, pid) != creation)
                        throw new ProcessEvidenceException("PROCESS_CREATION_IDENTITY_INCONSISTENT");
                    if (pid == serverPid)
                    {
                        if (!SameImage(identity.Image, serverImage) || creation != serverCreatedFileTime || identity.ParentProcessId != harnessPid)
                            throw new ProcessEvidenceException("OWNED_SERVER_IDENTITY_MISMATCH");
                    }
                    else if (!SameImage(identity.Image, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe")) ||
                        identity.ParentProcessId != serverPid || creation < serverCreatedFileTime)
                        throw new ProcessEvidenceException("UNEXPECTED_PROCESS_IDENTITY_REJECTED");

                    if (verifiedProcesses.TryGetValue(pid, out VerifiedProcess prior))
                    {
                        if (prior.Identity.CreationFileTime != creation || !SameImage(prior.Identity.Image, identity.Image) || prior.Identity.ParentProcessId != identity.ParentProcessId)
                            throw new ProcessEvidenceException("PROCESS_IDENTITY_REUSE_REJECTED");
                    }
                    else
                    {
                        if (pid != serverPid && verifiedProcesses.Keys.Any(id => id != serverPid))
                            throw new ProcessEvidenceException("ADDITIONAL_CONSOLE_HOST_REJECTED");
                        verifiedProcesses.Add(pid, new VerifiedProcess { Handle = handle, Identity = identity });
                        handle = null; // Retain the native process object through final accounting and cleanup.
                    }
                }
                finally { handle?.Dispose(); }
            }
            foreach (VerifiedProcess process in verifiedProcesses.Values)
                if (ReadCreationTime(process.Handle, process.Identity.ProcessId) != process.Identity.CreationFileTime)
                    throw new ProcessEvidenceException("RETAINED_PROCESS_IDENTITY_MISMATCH");
            recordEvidence(new { expectedServerPid = serverPid, uniqueVerifiedIdentities = verifiedProcesses.Values.Select(p => p.Identity).ToArray(), cumulativeJobTotal = snapshot.totalProcesses });
            if (!verifiedProcesses.ContainsKey((uint)serverPid) || verifiedProcesses.Count != snapshot.totalProcesses)
                throw new ProcessEvidenceException("UNIDENTIFIED_LIFETIME_JOB_ASSOCIATION");
        }

        private static long ReadCreationTime(SafeFileHandle handle, uint pid)
        {
            if (!GetProcessTimes(handle, out NativeFileTime created, out NativeFileTime exited, out NativeFileTime kernel, out NativeFileTime user))
            {
                int error = Marshal.GetLastWin32Error();
                throw new NativeJobException("GetProcessTimes, PID=" + pid, error);
            }
            return created.Value;
        }

        private static string ReadImage(SafeFileHandle handle, uint pid)
        {
            var image = new StringBuilder(32768);
            uint size = (uint)image.Capacity;
            if (!QueryFullProcessImageName(handle, 0, image, ref size))
            {
                int error = Marshal.GetLastWin32Error();
                throw new NativeJobException("QueryFullProcessImageNameW, PID=" + pid, error);
            }
            return image.ToString();
        }

        private static Dictionary<uint, uint> ReadParentPids(uint[] ownedPids)
        {
            using (SafeFileHandle snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0))
            {
                if (snapshot.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new NativeJobException("CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS)", error);
                }
                var entry = new ProcessEntry { StructSize = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
                if (!Process32First(snapshot, ref entry))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new NativeJobException("Process32FirstW", error);
                }
                var parents = new Dictionary<uint, uint>();
                var timer = Stopwatch.StartNew();
                for (int index = 0; index < 8192; index++)
                {
                    // Only retain parent information for Job members; no unrelated image/identity queries.
                    if (ownedPids.Contains(entry.ProcessId)) parents.Add(entry.ProcessId, entry.ParentProcessId);
                    if (parents.Count == ownedPids.Length) return parents;
                    if (timer.Elapsed > TimeSpan.FromSeconds(2)) throw new ProcessEvidenceException("TOOLHELP_ENUMERATION_TIME_LIMIT");
                    if (!Process32Next(snapshot, ref entry))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == 18) throw new NativeJobException("Process32NextW (required Job member missing)", error);
                        throw new NativeJobException("Process32NextW", error);
                    }
                }
                throw new ProcessEvidenceException("TOOLHELP_ENUMERATION_RECORD_LIMIT");
            }
        }

        private static bool SameImage(string actual, string expected)
            => Path.IsPathRooted(actual) && string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);

        internal static IReadOnlyDictionary<string, int> ValidateNativeLayouts()
        {
            // Windows x64 ABI: DWORD=4; LARGE_INTEGER=8; SIZE_T/ULONG_PTR=8; default alignment=8.
            var expected = new Dictionary<string, int>
            {
                ["PointerBytes"] = 8, ["BasicLimits.Size"] = 64, ["IoCounters.Size"] = 48,
                ["ProcessEntry.Size"] = 568, ["ProcessEntry.DefaultHeapId"] = 16,
                ["ProcessEntry.ParentProcessId"] = 32, ["ProcessEntry.ExeFile"] = 44, ["NativeFileTime.Size"] = 8,
                ["ExtendedLimits.Size"] = 144, ["Accounting.Size"] = 48, ["ProcessIdListHeader.Size"] = 16,
                ["BasicLimits.LimitFlags"] = 16, ["BasicLimits.MinimumWorkingSet"] = 24,
                ["BasicLimits.MaximumWorkingSet"] = 32, ["BasicLimits.ActiveProcessLimit"] = 40,
                ["BasicLimits.Affinity"] = 48, ["BasicLimits.PriorityClass"] = 56, ["BasicLimits.SchedulingClass"] = 60,
                ["ExtendedLimits.Io"] = 64, ["ExtendedLimits.ProcessMemory"] = 112,
                ["ExtendedLimits.JobMemory"] = 120, ["ExtendedLimits.PeakProcessMemory"] = 128,
                ["ExtendedLimits.PeakJobMemory"] = 136, ["Accounting.PageFaults"] = 32,
                ["Accounting.TotalProcesses"] = 36, ["Accounting.ActiveProcesses"] = 40,
                ["Accounting.TotalTerminatedProcesses"] = 44,
                ["ProcessIdListHeader.NumberOfAssignedProcesses"] = 0,
                ["ProcessIdListHeader.NumberOfProcessIdsInList"] = 4, ["ProcessIdListHeader.FirstProcessId"] = 8
            };
            var actual = new Dictionary<string, int> { ["PointerBytes"] = IntPtr.Size };
            foreach (Type type in new[] { typeof(BasicLimits), typeof(IoCounters), typeof(ExtendedLimits), typeof(Accounting), typeof(ProcessIdListHeader), typeof(ProcessEntry), typeof(NativeFileTime) })
            {
                actual[type.Name + ".Size"] = Marshal.SizeOf(type);
                foreach (var field in type.GetFields())
                    actual[type.Name + "." + field.Name] = Marshal.OffsetOf(type, field.Name).ToInt32();
            }
            foreach (var pair in expected)
                if (actual[pair.Key] != pair.Value) throw new InvalidOperationException("NATIVE_LAYOUT_MISMATCH: " + pair.Key);
            return actual;
        }

        public void Dispose()
        {
            job.Dispose();
            foreach (VerifiedProcess process in verifiedProcesses.Values) process.Handle.Dispose();
        }

        private sealed class VerifiedProcess
        {
            internal SafeFileHandle Handle;
            internal ProcessIdentity Identity;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFileTime
        {
            public uint Low, High;
            public long Value => unchecked((long)(((ulong)High << 32) | Low));
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            public uint StructSize, Usage, ProcessId;
            public UIntPtr DefaultHeapId;
            public uint ModuleId, Threads, ParentProcessId;
            public int PriorityClassBase;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long ProcessTime, JobTime;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Accounting
        {
            public long TotalUserTime, TotalKernelTime, PeriodUserTime, PeriodKernelTime;
            public uint PageFaults, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessIdListHeader
        {
            public uint NumberOfAssignedProcesses, NumberOfProcessIdsInList;
            public UIntPtr FirstProcessId;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateJobObject(IntPtr security, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(SafeFileHandle job, int type, ref ExtendedLimits info, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
        [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryAccounting(SafeFileHandle job, int type, out Accounting info, uint size, IntPtr returned);
        [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryLimits(SafeFileHandle job, int type, out ExtendedLimits info, uint size, IntPtr returned);
        [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryProcessIds(SafeFileHandle job, int type, IntPtr info, uint size, out uint returned);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeFileHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
        [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(SafeFileHandle process, uint flags, StringBuilder image, ref uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(SafeFileHandle process, out NativeFileTime created, out NativeFileTime exited, out NativeFileTime kernel, out NativeFileTime user);
        [DllImport("kernel32.dll", EntryPoint = "GetProcessTimes", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOwnedProcessTimes(IntPtr process, out NativeFileTime created, out NativeFileTime exited, out NativeFileTime kernel, out NativeFileTime user);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
    }

    internal sealed class JobSnapshot
    {
        public DateTime startedUtc { get; set; }
        public DateTime finishedUtc { get; set; }
        public int expectedServerPid { get; set; }
        public uint totalProcesses { get; set; }
        public uint activeProcesses { get; set; }
        public uint totalTerminatedProcesses { get; set; }
        public uint numberOfAssignedProcesses { get; set; }
        public uint numberOfProcessIdsInList { get; set; }
        public ulong[] processIds { get; set; }
        public int processIdBufferBytes { get; set; }
        public uint processIdReturnedBytes { get; set; }
        public string limitFlags { get; set; }
        public uint activeProcessLimit { get; set; }
        public bool breakawayAllowed { get; set; }
    }

    internal sealed class ProcessIdentity
    {
        public uint ProcessId { get; set; }
        public string Image { get; set; }
        public uint ParentProcessId { get; set; }
        public long CreationFileTime { get; set; }
        public DateTime CreationUtc { get; set; }
        public DateTime ObservedUtc { get; set; }
    }

    internal sealed class ProcessEvidenceException : Exception
    {
        internal string Classification { get; }
        internal string Operation => "VerifyProcessAllowlist/NativeOwnedJobIdentity";
        internal ProcessEvidenceException(string classification) : base(classification) { Classification = classification; }
    }

    internal sealed class NativeJobException : Exception
    {
        internal string Operation { get; }
        internal int ErrorCode { get; }
        internal string Classification { get; }

        internal NativeJobException(string operation, int errorCode) : base(operation + " failed")
        {
            Operation = operation;
            ErrorCode = errorCode;
            Classification = errorCode == 5 ? "NATIVE_ACCESS_DENIED" :
                errorCode == 87 ? "NATIVE_INVALID_PARAMETER" :
                errorCode == 50 ? "NATIVE_NOT_SUPPORTED" :
                errorCode == 234 ? "NATIVE_MORE_DATA" :
                errorCode == 18 ? "NATIVE_NO_MORE_FILES" :
                errorCode == 122 ? "NATIVE_INSUFFICIENT_BUFFER" : "NATIVE_CALL_FAILURE";
        }
    }
}
