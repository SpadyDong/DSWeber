using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DshTray
{
    internal enum ServiceState
    {
        Stopped,
        Starting,
        Running,
        Crashed,
        PortConflict
    }

    internal sealed class DshSupervisor : IDisposable
    {
        private readonly AppConfig _config;
        private readonly object _gate = new object();
        private Process _process;
        private IntPtr _jobHandle = IntPtr.Zero;
        private bool _stopRequested;
        private int _restartCount;
        private DateTime _startedAt;

        public event Action StateChanged;

        public ServiceState State { get; private set; }
        public string LastError { get; private set; }
        public int ConflictOwnerPid { get; private set; }
        public int RestartCount { get { return _restartCount; } }
        public DateTime StartedAt { get { return _startedAt; } }

        public int ProcessId
        {
            get
            {
                lock (_gate)
                {
                    try
                    {
                        return _process != null && !_process.HasExited ? _process.Id : 0;
                    }
                    catch
                    {
                        return 0;
                    }
                }
            }
        }

        public DshSupervisor(AppConfig config)
        {
            _config = config;
            State = ServiceState.Stopped;
            CreateJobObject();
        }

        public string BuildArgumentLine()
        {
            var sb = new StringBuilder();
            sb.Append('"').Append(_config.DshScript).Append('"');
            sb.Append(" web");
            if (!string.IsNullOrWhiteSpace(_config.Host))
            {
                sb.Append(" --host ").Append(_config.Host);
            }
            sb.Append(" --port ").Append(_config.Port);
            if (_config.NoOpen)
            {
                sb.Append(" --no-open");
            }
            return sb.ToString();
        }

        public string DisplayCommandLine()
        {
            return "\"" + _config.NodePath + "\" " + BuildArgumentLine();
        }

        public string WebUrl
        {
            get
            {
                var host = string.IsNullOrWhiteSpace(_config.Host) ? "127.0.0.1" : _config.Host;
                if (host == "0.0.0.0" || host == "::") host = "127.0.0.1";
                return "http://" + host + ":" + _config.Port + "/";
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_process != null && !SafeHasExited(_process))
                {
                    return;
                }

                _stopRequested = false;
                LastError = null;

                int owner = PortGuard.GetListenerPid(_config.Port);
                if (owner != 0)
                {
                    ConflictOwnerPid = owner;
                    LastError = "端口 " + _config.Port + " 已被 "
                        + PortGuard.DescribeProcess(owner) + " 占用";
                    SetState(ServiceState.PortConflict);
                    return;
                }

                ConflictOwnerPid = 0;
                SetState(ServiceState.Starting);

                var psi = new ProcessStartInfo
                {
                    FileName = _config.NodePath,
                    Arguments = BuildArgumentLine(),
                    WorkingDirectory = _config.WorkingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                try
                {
                    _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    _process.Exited += OnProcessExited;
                    _process.Start();
                    AssignToJob(_process);
                    _startedAt = DateTime.Now;
                    SetState(ServiceState.Running);
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    _process = null;
                    SetState(ServiceState.Crashed);
                }
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                _stopRequested = true;
                KillTree();
                ConflictOwnerPid = 0;
                SetState(ServiceState.Stopped);
            }
        }

        public void Restart()
        {
            Stop();
            System.Threading.Thread.Sleep(300);
            _restartCount = 0;
            Start();
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            bool shouldRestart = false;

            lock (_gate)
            {
                if (_stopRequested)
                {
                    return;
                }

                int exitCode = -1;
                try { exitCode = _process.ExitCode; } catch { }

                int owner = PortGuard.GetListenerPid(_config.Port);
                if (owner != 0)
                {
                    ConflictOwnerPid = owner;
                    LastError = "dsh web 启动失败：端口 " + _config.Port + " 已被 "
                        + PortGuard.DescribeProcess(owner) + " 占用";
                    SetState(ServiceState.PortConflict);
                    return;
                }

                LastError = "dsh web 进程已退出，退出码 " + exitCode;
                SetState(ServiceState.Crashed);

                if (_config.AutoRestart && _restartCount < 5)
                {
                    _restartCount++;
                    shouldRestart = true;
                }
            }

            if (shouldRestart)
            {
                System.Threading.Thread.Sleep(1500);
                Start();
            }
        }

        private void SetState(ServiceState state)
        {
            State = state;
            var handler = StateChanged;
            if (handler != null)
            {
                handler();
            }
        }

        private static bool SafeHasExited(Process p)
        {
            try { return p.HasExited; }
            catch { return true; }
        }

        private void KillTree()
        {
            if (_process == null) return;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Exited -= OnProcessExited;
                    _process.Kill();
                    _process.WaitForExit(4000);
                }
            }
            catch
            {
            }
            finally
            {
                try { _process.Dispose(); } catch { }
                _process = null;
            }
        }

        #region Job Object

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JobObjectLimitKillOnJobClose = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        private void CreateJobObject()
        {
            try
            {
                _jobHandle = CreateJobObject(IntPtr.Zero, null);
                if (_jobHandle == IntPtr.Zero) return;

                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

                int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr ptr = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation, ptr, (uint)length);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            catch
            {
                _jobHandle = IntPtr.Zero;
            }
        }

        private void AssignToJob(Process p)
        {
            if (_jobHandle == IntPtr.Zero) return;
            try { AssignProcessToJobObject(_jobHandle, p.Handle); }
            catch { }
        }

        #endregion

        public void Dispose()
        {
            Stop();
            if (_jobHandle != IntPtr.Zero)
            {
                CloseHandle(_jobHandle);
                _jobHandle = IntPtr.Zero;
            }
        }
    }
}