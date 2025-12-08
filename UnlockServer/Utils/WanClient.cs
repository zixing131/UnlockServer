using System;
using System.Collections;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;

namespace UnlockServer
{
    internal class WanClient
    {
        public static bool UnlockPc()
        {
            try
            {
                if (string.IsNullOrEmpty(ip))
                {
                    return false;
                }
                if (port == -1)
                {
                    return false;
                }
                if (string.IsNullOrEmpty(username))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(pd))
                {
                    return false;
                }
                return UnlockPc(ip,port, username, pd);
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public static bool isConfigVal()
        {
            return !string.IsNullOrEmpty(ip) && port > 0 && !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(pd);
        }

        static string ip = "127.0.0.1";

        static int port = -1;
        static string username = "";
        static string pd = "";
        public static void reloadConfig()
        {
            try { 
              ip = OperateIniFile.ReadSafeString("setting", "ip", ip); 
              port = int.Parse(OperateIniFile.ReadSafeString("setting", "pt", "2084"));
            
              username = OperateIniFile.ReadSafeString("setting", "us", "");
           
              pd = OperateIniFile.ReadSafeString("setting", "pd", "");
            }catch(Exception ex)
            {

            }
        }
         

        public static bool UnlockPc(string ip,int port,string username,string pwd)
        {
            try
            {
                var data = "{\"oriMac\":\"\",\"username\":\"" + username + "\",\"passwd\":\"" + pwd + "\"}";
                return SslTcpClient.RunClient(ip, port, data);
            }
            catch(Exception ex)
            {
                return false;
            }
        }

        // 锁定计算机.    
        [DllImport("user32.dll")]
        private static extern void LockWorkStation();//须写extern
        public static void LockPc()
        {
#if DEBUG
            LogHelper.WriteLine("调用锁定设备功能");
            //return;
#endif
            LockWorkStation();
        }
        [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode)]
        public static extern bool WTSQuerySessionInformationW(IntPtr hServer, uint SessionId, WTS_INFO_CLASS WTSInfoClass, ref IntPtr ppBuffer, ref uint pBytesReturned);
        [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode)]
        public static extern void WTSFreeMemory(IntPtr pMemory);
        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern uint WTSGetActiveConsoleSessionId();

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
            WTSValidationInfo,   // Info Class value used to fetch Validation Information through the WTSQuerySessionInformation
            WTSSessionAddressV4,
            WTSIsRemoteSession
        }
        public enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive,              // User logged on to WinStation
            WTSConnected,           // WinStation connected to client
            WTSConnectQuery,        // In the process of connecting to client
            WTSShadow,              // Shadowing another WinStation
            WTSDisconnected,        // WinStation logged on without client
            WTSIdle,                // Waiting for client to connect
            WTSListen,              // WinStation is listening for connection
            WTSReset,               // WinStation is being reset
            WTSDown,                // WinStation is down due to error
            WTSInit,                // WinStation in initialization
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
        public struct LARGE_INTEGER //此结构体在C++中使用的为union结构，在C#中需要使用FieldOffset设置相关的内存起始地址
        {
            [FieldOffset(0)]
            uint LowPart;
            [FieldOffset(4)]
            int HighPart;
            [FieldOffset(0)]
            long QuadPart;
        }


        public static bool IsSessionLocked()
        {
            try
            {
                uint dwSessionID = WTSGetActiveConsoleSessionId();
                uint dwBytesReturned = 0;
                int dwFlags = 0;
                IntPtr pInfo = IntPtr.Zero;
                WTSQuerySessionInformationW(IntPtr.Zero, dwSessionID, WTS_INFO_CLASS.WTSSessionInfoEx, ref pInfo, ref dwBytesReturned);
                var shit = Marshal.PtrToStructure<WTSINFOEXW>(pInfo);

                if (shit.Level == 1)
                {
                    dwFlags = shit.Data.WTSInfoExLevel1.SessionFlags;
                }
                switch (dwFlags)
                {
                    case 0: return true;
                    case 1: return false;
                    default: return false;
                }
            }
            catch(Exception ex)
            {
                return false;
            }
        }
    }
}
