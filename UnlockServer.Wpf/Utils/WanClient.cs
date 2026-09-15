using System;
using System.Runtime.InteropServices;

namespace UnlockServer
{
    /// <summary>
    /// 远程解锁客户端
    /// </summary>
    internal class WanClient
    {
        private static string ip = "127.0.0.1";
        private static int port = -1;
        private static string username = "";
        private static string pd = "";

        #region Win32 API

        [DllImport("user32.dll")]
        private static extern void LockWorkStation();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, char[] pvInfo, int nLength, out int lpnLengthNeeded);

        [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode)]
        public static extern bool WTSQuerySessionInformationW(IntPtr hServer, uint SessionId, WTS_INFO_CLASS WTSInfoClass, ref IntPtr ppBuffer, ref uint pBytesReturned);

        [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode)]
        public static extern void WTSFreeMemory(IntPtr pMemory);

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern uint WTSGetActiveConsoleSessionId();

        #endregion

        #region 枚举和结构体

        public enum WTS_INFO_CLASS
        {
            WTSInitialProgram,
            WTSApplicationName,
            WTSWorkingDirectory,
            WTSOEMId,
            WTSSessionId,
            WTSUserName,
            WTSWinStationName,
            WTSDomainName,
            WTSConnectState,
            WTSClientBuildNumber,
            WTSClientName,
            WTSClientDirectory,
            WTSClientProductId,
            WTSClientHardwareId,
            WTSClientAddress,
            WTSClientDisplay,
            WTSClientProtocolType,
            WTSIdleTime,
            WTSLogonTime,
            WTSIncomingBytes,
            WTSOutgoingBytes,
            WTSIncomingFrames,
            WTSOutgoingFrames,
            WTSClientInfo,
            WTSSessionInfo,
            WTSSessionInfoEx,
            WTSConfigInfo,
            WTSValidationInfo,
            WTSSessionAddressV4,
            WTSIsRemoteSession
        }

        public enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive,
            WTSConnected,
            WTSConnectQuery,
            WTSShadow,
            WTSDisconnected,
            WTSIdle,
            WTSListen,
            WTSReset,
            WTSDown,
            WTSInit,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WTSINFOEXW
        {
            public int Level;
            public WTSINFOEX_LEVEL_W Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WTSINFOEX_LEVEL_W
        {
            public WTSINFOEX_LEVEL1_W WTSInfoExLevel1;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WTSINFOEX_LEVEL1_W
        {
            public int SessionId;
            public WTS_CONNECTSTATE_CLASS SessionState;
            public int SessionFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
            public string WinStationName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public string UserName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 18)]
            public string DomainName;
            public LARGE_INTEGER LogonTime;
            public LARGE_INTEGER ConnectTime;
            public LARGE_INTEGER DisconnectTime;
            public LARGE_INTEGER LastInputTime;
            public LARGE_INTEGER CurrentTime;
            public uint IncomingBytes;
            public uint OutgoingBytes;
            public uint IncomingFrames;
            public uint OutgoingFrames;
            public uint IncomingCompressedBytes;
            public uint OutgoingCompressedBytes;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct LARGE_INTEGER
        {
            [FieldOffset(0)]
            uint LowPart;
            [FieldOffset(4)]
            int HighPart;
            [FieldOffset(0)]
            long QuadPart;
        }

        #endregion

        /// <summary>
        /// 检查配置是否有效
        /// </summary>
        public static bool isConfigVal()
        {
            return !string.IsNullOrEmpty(ip) && port > 0 && !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(pd);
        }

        /// <summary>
        /// 仅探测解锁服务端口是否可连，不发送凭据。
        /// </summary>
        public static bool TestServer(out string message)
        {
            try
            {
                reloadConfig();
                if (string.IsNullOrEmpty(ip) || port <= 0)
                {
                    message = "请先填写服务器地址和端口";
                    return false;
                }

                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var ar = client.BeginConnect(ip, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(2000))
                    {
                        message = $"无法连接 {ip}:{port}，请先启动远程解锁服务";
                        return false;
                    }
                    client.EndConnect(ar);
                }

                message = $"已连接到 {ip}:{port}";
                return true;
            }
            catch (Exception ex)
            {
                message = $"连接失败: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 重新加载配置
        /// </summary>
        public static void reloadConfig()
        {
            try
            {
                ip = OperateIniFile.ReadSafeString("setting", "ip", ip);
                port = int.Parse(OperateIniFile.ReadSafeString("setting", "pt", "2084"));
                username = OperateIniFile.ReadSafeString("setting", "us", "");
                pd = OperateIniFile.ReadSafeString("setting", "pd", "");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"重新加载配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 解锁电脑
        /// </summary>
        public static bool UnlockPc()
        {
            try
            {
                if (string.IsNullOrEmpty(ip) || port == -1 ||
                    string.IsNullOrEmpty(username) || string.IsNullOrEmpty(pd))
                {
                    return false;
                }
                return UnlockPc(ip, port, username, pd);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"解锁电脑失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 解锁电脑（带参数）
        /// </summary>
        public static bool UnlockPc(string ip, int port, string username, string pwd)
        {
            try
            {
                var data = "{\"oriMac\":\"\",\"username\":\"" + username + "\",\"passwd\":\"" + pwd + "\"}";
                return SslTcpClient.RunClient(ip, port, data);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"解锁电脑失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 锁定电脑
        /// </summary>
        public static void LockPc()
        {
#if DEBUG
            LogHelper.WriteLine("调用锁定设备功能");
#endif
            LockWorkStation();
        }

        /// <summary>
        /// 检查会话是否已锁定
        /// </summary>
        public static bool IsSessionLocked()
        {
            try
            {
                if (TryGetInputDesktopName(out var desktopName))
                {
                    if (desktopName.Equals("Winlogon", StringComparison.OrdinalIgnoreCase) ||
                        desktopName.Equals("Disconnect", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (desktopName.Equals("Default", StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            catch { }

            try
            {
                uint dwSessionID = WTSGetActiveConsoleSessionId();
                uint dwBytesReturned = 0;
                IntPtr pInfo = IntPtr.Zero;

                if (!WTSQuerySessionInformationW(IntPtr.Zero, dwSessionID, WTS_INFO_CLASS.WTSSessionInfoEx, ref pInfo, ref dwBytesReturned) ||
                    pInfo == IntPtr.Zero)
                    return false;

                try
                {
                    var info = Marshal.PtrToStructure<WTSINFOEXW>(pInfo);
                    if (info.Level == 1)
                    {
                        var flags = info.Data.WTSInfoExLevel1.SessionFlags;
                        if (flags == 0) return true;
                        if (flags == 1) return false;
                    }
                }
                finally
                {
                    WTSFreeMemory(pInfo);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"检查会话锁定状态失败: {ex.Message}");
            }
            return false;
        }

        private static bool TryGetInputDesktopName(out string name)
        {
            name = null;
            const uint DESKTOP_READOBJECTS = 0x0001;
            const int UOI_NAME = 2;
            var h = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
            if (h == IntPtr.Zero) return false;
            try
            {
                var buffer = new char[256];
                if (!GetUserObjectInformation(h, UOI_NAME, buffer, buffer.Length * 2, out _))
                    return false;
                name = new string(buffer).TrimEnd('\0');
                return !string.IsNullOrEmpty(name);
            }
            finally
            {
                CloseDesktop(h);
            }
        }
    }
}

