using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace DshTray
{
    internal static class PortGuard
    {
        private const int AfInet = 2;
        private const int AfInet6 = 23;
        private const int TcpTableOwnerPidListener = 3;
        private const uint MibTcpStateListen = 2;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen,
            bool sort, int ipVersion, int tblClass, int reserved);

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint State;
            public uint LocalAddr;
            public byte LocalPort1;
            public byte LocalPort2;
            public byte LocalPort3;
            public byte LocalPort4;
            public uint RemoteAddr;
            public byte RemotePort1;
            public byte RemotePort2;
            public byte RemotePort3;
            public byte RemotePort4;
            public uint OwningPid;
        }

        public static int GetListenerPid(int port)
        {
            if (port <= 0 || port > 65535) return 0;

            int pid = Scan(port, AfInet);
            if (pid != 0) return pid;
            return Scan(port, AfInet6);
        }

        public static bool IsListening(int port)
        {
            return GetListenerPid(port) != 0;
        }

        private static int Scan(int port, int family)
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int size = 0;
                GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TcpTableOwnerPidListener, 0);
                if (size <= 0) return 0;

                buffer = Marshal.AllocHGlobal(size);
                uint rc = GetExtendedTcpTable(buffer, ref size, false, family, TcpTableOwnerPidListener, 0);
                if (rc != 0) return 0;

                int count = Marshal.ReadInt32(buffer);
                int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                IntPtr cursor = new IntPtr(buffer.ToInt64() + 4);

                for (int i = 0; i < count; i++)
                {
                    var row = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(cursor, typeof(MIB_TCPROW_OWNER_PID));
                    cursor = new IntPtr(cursor.ToInt64() + rowSize);

                    if (row.State != MibTcpStateListen) continue;

                    int localPort = (row.LocalPort1 << 8) + row.LocalPort2;
                    if (localPort == port) return (int)row.OwningPid;
                }
            }
            catch
            {
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }

            return 0;
        }

        public static string DescribeProcess(int pid)
        {
            if (pid <= 0) return "未知进程";
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    return p.ProcessName + ".exe (PID " + pid + ")";
                }
            }
            catch
            {
                return "PID " + pid;
            }
        }

        private const string DshMarker = "@deepseek-ai";

        public static List<int> FindDshWebProcesses(string dshScript)
        {
            var hits = new List<int>();
            ManagementObjectSearcher searcher = null;
            ManagementObjectCollection results = null;

            try
            {
                searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'node.exe'");
                results = searcher.Get();

                foreach (ManagementObject mo in results)
                {
                    try
                    {
                        object rawCmd = mo["CommandLine"];
                        object rawPid = mo["ProcessId"];
                        if (rawCmd == null || rawPid == null) continue;

                        if (!LooksLikeDshWeb(rawCmd.ToString(), dshScript)) continue;
                        hits.Add(Convert.ToInt32(rawPid));
                    }
                    catch
                    {
                    }
                    finally
                    {
                        try { mo.Dispose(); } catch { }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (results != null) { try { results.Dispose(); } catch { } }
                if (searcher != null) { try { searcher.Dispose(); } catch { } }
            }

            return hits;
        }

        private static bool LooksLikeDshWeb(string commandLine, string dshScript)
        {
            if (string.IsNullOrEmpty(commandLine)) return false;

            var lower = commandLine.ToLowerInvariant();
            if (lower.IndexOf(" web", StringComparison.Ordinal) < 0) return false;

            if (!string.IsNullOrEmpty(dshScript)
                && lower.IndexOf(dshScript.ToLowerInvariant(), StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            return lower.IndexOf(DshMarker, StringComparison.Ordinal) >= 0;
        }
    }
}
