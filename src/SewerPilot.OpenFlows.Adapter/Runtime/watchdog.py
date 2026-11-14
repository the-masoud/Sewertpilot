"""One-shot Windows watchdog. No retries or polling; runtime begins only after job assignment."""
import ctypes
from ctypes import wintypes as W
import datetime as dt
import json
import msvcrt
import os
from pathlib import Path
import subprocess
import sys
import time
import _winapi

ROOT = Path(__file__).resolve().parent
K = ctypes.WinDLL('kernel32', use_last_error=True)
SIZE_T = ctypes.c_size_t
ULONG_PTR = ctypes.c_size_t

class BASIC_LIMIT(ctypes.Structure):
    _fields_ = [('PerProcessUserTimeLimit', ctypes.c_longlong), ('PerJobUserTimeLimit', ctypes.c_longlong),
                ('LimitFlags', W.DWORD), ('MinimumWorkingSetSize', SIZE_T), ('MaximumWorkingSetSize', SIZE_T),
                ('ActiveProcessLimit', W.DWORD), ('Affinity', ULONG_PTR), ('PriorityClass', W.DWORD),
                ('SchedulingClass', W.DWORD)]

class IO_COUNTERS(ctypes.Structure):
    _fields_ = [(name, ctypes.c_ulonglong) for name in
                ('ReadOperationCount', 'WriteOperationCount', 'OtherOperationCount',
                 'ReadTransferCount', 'WriteTransferCount', 'OtherTransferCount')]

class EXTENDED_LIMIT(ctypes.Structure):
    _fields_ = [('BasicLimitInformation', BASIC_LIMIT), ('IoInfo', IO_COUNTERS),
                ('ProcessMemoryLimit', SIZE_T), ('JobMemoryLimit', SIZE_T),
                ('PeakProcessMemoryUsed', SIZE_T), ('PeakJobMemoryUsed', SIZE_T)]

class ACCOUNTING(ctypes.Structure):
    _fields_ = [('TotalUserTime', ctypes.c_longlong), ('TotalKernelTime', ctypes.c_longlong),
                ('ThisPeriodTotalUserTime', ctypes.c_longlong), ('ThisPeriodTotalKernelTime', ctypes.c_longlong),
                ('TotalPageFaultCount', W.DWORD), ('TotalProcesses', W.DWORD),
                ('ActiveProcesses', W.DWORD), ('TotalTerminatedProcesses', W.DWORD)]

class PID_LIST(ctypes.Structure):
    _fields_ = [('NumberOfAssignedProcesses', W.DWORD), ('NumberOfProcessIdsInList', W.DWORD),
                ('ProcessIdList', ULONG_PTR * 256)]

K.CreateJobObjectW.argtypes = [ctypes.c_void_p, W.LPCWSTR]
K.CreateJobObjectW.restype = W.HANDLE
K.SetInformationJobObject.argtypes = [W.HANDLE, ctypes.c_int, ctypes.c_void_p, W.DWORD]
K.SetInformationJobObject.restype = W.BOOL
K.AssignProcessToJobObject.argtypes = [W.HANDLE, W.HANDLE]
K.AssignProcessToJobObject.restype = W.BOOL
K.IsProcessInJob.argtypes = [W.HANDLE, W.HANDLE, ctypes.POINTER(W.BOOL)]
K.IsProcessInJob.restype = W.BOOL
K.QueryInformationJobObject.argtypes = [W.HANDLE, ctypes.c_int, ctypes.c_void_p, W.DWORD, ctypes.c_void_p]
K.QueryInformationJobObject.restype = W.BOOL
K.TerminateJobObject.argtypes = [W.HANDLE, W.UINT]
K.TerminateJobObject.restype = W.BOOL
K.WaitForSingleObject.argtypes = [W.HANDLE, W.DWORD]
K.WaitForSingleObject.restype = W.DWORD
K.GetExitCodeProcess.argtypes = [W.HANDLE, ctypes.POINTER(W.DWORD)]
K.GetExitCodeProcess.restype = W.BOOL
K.ResumeThread.argtypes = [W.HANDLE]
K.ResumeThread.restype = W.DWORD
K.TerminateProcess.argtypes = [W.HANDLE, W.UINT]
K.TerminateProcess.restype = W.BOOL
K.CloseHandle.argtypes = [W.HANDLE]
K.CloseHandle.restype = W.BOOL

def checked(ok):
    if not ok:
        raise ctypes.WinError(ctypes.get_last_error())
    return ok

def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()

def persist(path, value):
    with path.open('w', encoding='utf-8', newline='\n') as stream:
        json.dump(value, stream, indent=2)
        stream.write('\n')
        stream.flush()
        os.fsync(stream.fileno())

def inventory(job):
    account = ACCOUNTING()
    pids = PID_LIST()
    checked(K.QueryInformationJobObject(job, 1, ctypes.byref(account), ctypes.sizeof(account), None))
    checked(K.QueryInformationJobObject(job, 3, ctypes.byref(pids), ctypes.sizeof(pids), None))
    if pids.NumberOfProcessIdsInList > 256 or pids.NumberOfAssignedProcesses > 256:
        raise RuntimeError('Job PID evidence exceeds fixed bounded capacity')
    return {'active_processes': account.ActiveProcesses, 'total_processes': account.TotalProcesses,
            'terminated_processes': account.TotalTerminatedProcesses,
            'verified_job_pids': list(pids.ProcessIdList[:pids.NumberOfProcessIdsInList])}

def main():
    # Preflight validates Windows structures and job kill-on-close before creating a runtime process.
    if sys.platform != 'win32' or ctypes.sizeof(ctypes.c_void_p) != 8:
        raise RuntimeError('RUNTIME_WATCHDOG_UNAVAILABLE: Windows x64 controller required')
    if ctypes.sizeof(EXTENDED_LIMIT) != 144 or ctypes.sizeof(ACCOUNTING) != 48:
        raise RuntimeError('RUNTIME_WATCHDOG_UNAVAILABLE: native structure layout mismatch')
    result = {'controller_pid': os.getpid(), 'runtime_host_invocations': 0, 'timeout_seconds': 180,
              'timeout_triggered': False, 'post_termination_wait_seconds': 10,
              'termination_scope': 'unnamed Windows Job Object; root assigned while suspended; descendants cannot break away',
              'stdout_path': str(ROOT / 'stdout.txt'), 'stderr_path': str(ROOT / 'stderr.txt')}
    job = process_handle = thread_handle = None
    started = None
    resumed = False
    try:
        job = checked(K.CreateJobObjectW(None, None))
        limits = EXTENDED_LIMIT()
        limits.BasicLimitInformation.LimitFlags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        checked(K.SetInformationJobObject(job, 9, ctypes.byref(limits), ctypes.sizeof(limits)))
        result['watchdog_preflight'] = inventory(job)
        config = json.loads((ROOT / 'watchdog-config.json').read_text(encoding='utf-8-sig'))
        exe = config['executable']
        args = [exe, *config['arguments']]
        si = subprocess.STARTUPINFO()
        si.dwFlags = subprocess.STARTF_USESTDHANDLES | subprocess.STARTF_USESHOWWINDOW
        si.wShowWindow = 0
        environment = dict(os.environ)
        with open(os.devnull, 'rb') as stdin, (ROOT / 'stdout.txt').open('wb') as stdout, (ROOT / 'stderr.txt').open('wb') as stderr:
            handles = [msvcrt.get_osfhandle(stream.fileno()) for stream in (stdin, stdout, stderr)]
            for handle in handles:
                os.set_handle_inheritable(handle, True)
            si.hStdInput, si.hStdOutput, si.hStdError = handles
            si.lpAttributeList = {'handle_list': handles}
            result['command'] = args
            result['working_directory'] = os.getcwd()
            process_handle, thread_handle, child_pid, thread_id = _winapi.CreateProcess(
                exe, subprocess.list2cmdline(args), None, None, True,
                0x00000004 | 0x08000000 | 0x00000400,  # suspended, no visible window, Unicode environment
                environment, os.getcwd(), si)
            result.update(pid=child_pid, native_primary_thread_id=thread_id, created_at_utc=now())
            checked(K.AssignProcessToJobObject(job, process_handle))
            membership = W.BOOL()
            checked(K.IsProcessInJob(process_handle, job, ctypes.byref(membership)))
            if not membership.value:
                raise RuntimeError('RUNTIME_WATCHDOG_UNAVAILABLE: suspended child job membership not verified')
            result['pre_execution_verified_tree'] = inventory(job)
            persist(ROOT / 'launch.json', result)
            print(json.dumps({'event': 'RUNTIME_LAUNCH', 'pid': child_pid, 'controller_pid': os.getpid(),
                              'deadline_seconds': 180, 'evidence_directory': str(ROOT)}), flush=True)
            started = time.monotonic()
            result['start_time_utc'] = now()
            prior_suspend_count = K.ResumeThread(thread_handle)
            if prior_suspend_count != 1:
                raise RuntimeError('Unexpected primary-thread suspend count: ' + str(prior_suspend_count))
            resumed = True
            result['runtime_host_invocations'] = 1
            # Exactly one native bounded wait for the runtime: no polling, no communicate(), no output pipes.
            wait_result = K.WaitForSingleObject(process_handle, 180000)
            result['elapsed_until_wait_return_seconds'] = round(time.monotonic() - started, 6)
            result['wait_result'] = wait_result
            result['tree_at_wait_return'] = inventory(job)
            result['timeout_triggered'] = wait_result == 0x102
            terminate_tree = wait_result != 0 or result['tree_at_wait_return']['active_processes'] != 0
            if terminate_tree:
                # Membership is established before resume; no unrelated process can enter this unnamed job.
                checked(K.TerminateJobObject(job, 124))
                result['job_termination_requested'] = True
                result['post_termination_wait_result'] = K.WaitForSingleObject(process_handle, 10000)
            else:
                result['job_termination_requested'] = False
            # Exactly one bounded post-exit/termination inventory, no retry.
            result['post_termination_check'] = inventory(job)
            result['remaining_runtime_processes'] = result['post_termination_check']['active_processes']
            result['process_tree_terminated'] = terminate_tree and result['remaining_runtime_processes'] == 0
            exit_code = W.DWORD()
            checked(K.GetExitCodeProcess(process_handle, ctypes.byref(exit_code)))
            result['exit_code'] = exit_code.value
            result['end_time_utc'] = now()
            result['elapsed_seconds'] = round(time.monotonic() - started, 6)
            result['status'] = ('RUNTIME_TIMEOUT' if result['timeout_triggered'] else
                                'RUNTIME_COMPLETED' if wait_result == 0 and exit_code.value == 0 and result['remaining_runtime_processes'] == 0
                                else 'RUNTIME_BLOCKED')
    except BaseException as error:
        result['status'] = 'RUNTIME_BLOCKED'
        result['error_type'] = type(error).__name__
        result['error_message'] = str(error)
        if process_handle:
            # Suspended preflight failures execute zero runtime instructions. Handles identify only this child.
            if job:
                K.TerminateJobObject(job, 125)
            K.TerminateProcess(process_handle, 125)
            result['emergency_bounded_wait_result'] = K.WaitForSingleObject(process_handle, 10000)
        result['runtime_host_invocations'] = int(resumed)
    finally:
        if thread_handle:
            K.CloseHandle(thread_handle)
        if process_handle:
            K.CloseHandle(process_handle)
        if job:
            K.CloseHandle(job)
        persist(ROOT / 'execution.json', result)
    print(json.dumps(result), flush=True)
    return 0 if result.get('remaining_runtime_processes', 0) == 0 else 3

if __name__ == '__main__':
    sys.exit(main())
