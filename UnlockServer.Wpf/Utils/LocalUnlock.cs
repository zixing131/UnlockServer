using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace UnlockServer
{
    /// <summary>
    /// 本机解锁：把 Windows 账号交给已注册的凭据提供程序，锁屏界面自动提交。
    /// </summary>
    public static class LocalUnlock
    {
        public const string ProviderGuid = "{A31E8C27-9B14-4F6D-8E52-1C7A9D04E6B3}";
        public const string EventName = "Global\\UnlockServer.UnlockPulse";
        public const string InstallArg = "--install-local-unlock";
        public const string UninstallArg = "--uninstall-local-unlock";

        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UnlockServer");

        private static EventWaitHandle _pulse;

        public static string CredPath => Path.Combine(DataDir, "local.cred");
        public static string RequestPath => Path.Combine(DataDir, "unlock.req");
        public static string WarnPath => Path.Combine(DataDir, "unlock.warn");
        public static string CancelPath => Path.Combine(DataDir, "unlock.cancel");
        public static string ProviderDllName = "UnlockServer.Provider.dll";

        public static bool IsAdministrator()
        {
            try
            {
                var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsProviderRegistered()
        {
            try
            {
                using (var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = lm.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\" + ProviderGuid))
                {
                    return key != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool HasSavedCredential()
        {
            return File.Exists(CredPath);
        }

        public static bool CanUnlock()
        {
            return IsProviderRegistered() && HasSavedCredential();
        }

        public static string StatusText()
        {
            if (CanUnlock())
                return "本机解锁已启用";
            if (IsProviderRegistered() && !HasSavedCredential())
                return "组件已安装，请填写 Windows 密码";
            return "尚未安装本机解锁组件";
        }

        public static void ResolveIdentity(string usernameOverride, out string domain, out string username, out string sid)
        {
            domain = ".";
            username = usernameOverride ?? "";
            sid = "";
            try
            {
                var id = WindowsIdentity.GetCurrent();
                sid = id.User?.Value ?? "";
                var name = id.Name ?? "";

                if (!string.IsNullOrWhiteSpace(usernameOverride) && usernameOverride.IndexOf('\\') > 0)
                {
                    var i = usernameOverride.IndexOf('\\');
                    domain = usernameOverride.Substring(0, i);
                    username = usernameOverride.Substring(i + 1);
                    return;
                }

                var slash = name.IndexOf('\\');
                if (slash > 0)
                {
                    domain = name.Substring(0, slash);
                    if (string.IsNullOrWhiteSpace(username))
                        username = name.Substring(slash + 1);
                }
                else if (string.IsNullOrWhiteSpace(username))
                {
                    username = Environment.UserName;
                }

                if (string.IsNullOrEmpty(domain))
                    domain = ".";
            }
            catch
            {
                if (string.IsNullOrEmpty(username))
                    username = Environment.UserName;
            }
        }

        public static bool SaveCredential(string username, string password)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                ResolveIdentity(username, out var domain, out var user, out var sid);
                var payload = domain + "\n" + user + "\n" + (password ?? "") + "\n" + sid;
                var bytes = Encoding.UTF8.GetBytes(payload);
                var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine);
                File.WriteAllBytes(CredPath, protectedBytes);
                LogHelper.WriteLine($"已保存本机解锁凭据: {domain}\\{user}");
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"保存本机解锁凭据失败: {ex.Message}");
                return false;
            }
        }

        public static EventWaitHandle EnsurePulseEvent()
        {
            if (_pulse != null) return _pulse;
            try
            {
                var sec = new EventWaitHandleSecurity();
                sec.AddAccessRule(new EventWaitHandleAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    EventWaitHandleRights.FullControl,
                    AccessControlType.Allow));
                bool created;
                _pulse = new EventWaitHandle(false, EventResetMode.AutoReset, EventName, out created, sec);
                LogHelper.WriteLine(created ? "已创建解锁事件" : "已打开解锁事件");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"创建解锁事件失败: {ex.Message}");
            }
            return _pulse;
        }

        private static void Pulse()
        {
            try { EnsurePulseEvent()?.Set(); }
            catch { }
        }

        public static void WriteUnlockWarn(int seconds)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var expiry = DateTime.UtcNow.AddSeconds(seconds > 0 ? seconds : 1).Ticks;
                File.WriteAllText(WarnPath, expiry.ToString(), Encoding.ASCII);
                TryDeleteFile(CancelPath);
                Pulse();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"写入解锁提示失败: {ex.Message}");
            }
        }

        public static void ClearUnlockWarn()
        {
            TryDeleteFile(WarnPath);
            Pulse();
        }

        public static bool ConsumeUnlockCancel()
        {
            if (!File.Exists(CancelPath))
                return false;
            TryDeleteFile(CancelPath);
            TryDeleteFile(WarnPath);
            Pulse();
            return true;
        }

        public static bool RequestUnlock()
        {
            try
            {
                if (!CanUnlock())
                    return false;

                Directory.CreateDirectory(DataDir);
                EnsurePulseEvent();

                string lastToken = null;
                for (int i = 0; i < 40; i++)
                {
                    lastToken = DateTime.UtcNow.Ticks.ToString();
                    File.WriteAllText(RequestPath, lastToken, Encoding.ASCII);
                    Pulse();
                    Thread.Sleep(400);
                    if (WanClient.IsSessionLocked())
                        continue;

                    try
                    {
                        if (!File.Exists(RequestPath))
                            return true;
                        var left = File.ReadAllText(RequestPath).Trim();
                        if (!string.Equals(left, lastToken, StringComparison.Ordinal))
                            return true;
                    }
                    catch
                    {
                        return true;
                    }

                    LogHelper.WriteLine("会话已解锁，但锁屏组件未消费解锁请求（可能是手动登录）");
                    return false;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"请求本机解锁失败: {ex.Message}");
                return false;
            }
        }

        public static bool RelaunchElevatedToInstall()
        {
            if (IsAdministrator())
                return RunElevatedInstall() == 0 && IsProviderRegistered();

            try
            {
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = InstallArg,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(60000);
                return p.ExitCode == 0 && IsProviderRegistered();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"提权安装本机解锁失败: {ex.Message}");
                return false;
            }
        }

        public static void EnsureInstalled()
        {
            try
            {
                EnsurePulseEvent();
                if (!IsAdministrator()) return;
                if (!IsProviderRegistered()) return;

                var dll = FindProviderDll();
                if (string.IsNullOrEmpty(dll) || !File.Exists(dll)) return;

                var dest = GetSecureDllPath();
                var needCopy = !File.Exists(dest) || File.GetLastWriteTimeUtc(dll) > File.GetLastWriteTimeUtc(dest);
                if (needCopy)
                    RunElevatedInstall();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"检查本机解锁组件失败: {ex.Message}");
            }
        }

        public static bool RelaunchElevatedToUninstall()
        {
            if (IsAdministrator())
                return RunElevatedUninstall() == 0 && !IsProviderRegistered();

            try
            {
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = UninstallArg,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(60000);
                return p.ExitCode == 0 && !IsProviderRegistered();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"提权卸载本机解锁失败: {ex.Message}");
                return false;
            }
        }

        public static int RunElevatedUninstall()
        {
            try
            {
                UnregisterProvider();
                RemoveLegacyProviderKey();
                TryDeleteFile(CredPath);
                TryDeleteFile(RequestPath);
                TryDeleteFile(WarnPath);
                TryDeleteFile(CancelPath);
                TryDeleteFile(Path.Combine(DataDir, "provider.log"));

                var dest = GetSecureDllPath();
                TryDeleteFile(dest);
                try
                {
                    var dir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) &&
                        Directory.GetFileSystemEntries(dir).Length == 0)
                    {
                        Directory.Delete(dir);
                    }
                }
                catch { }

                LogHelper.WriteLine("本机解锁组件已卸载");
                return 0;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"卸载本机解锁失败: {ex.Message}");
                return 1;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"删除失败 {path}: {ex.Message}");
            }
        }

        private static void UnregisterProvider()
        {
            try
            {
                using (var classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64))
                    classes.DeleteSubKeyTree(@"CLSID\" + ProviderGuid, false);
            }
            catch { }

            try
            {
                using (var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    lm.DeleteSubKeyTree(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\" + ProviderGuid,
                        false);
            }
            catch { }
        }

        public static int RunElevatedInstall()
        {
            try
            {
                var dll = FindProviderDll();
                if (string.IsNullOrEmpty(dll) || !File.Exists(dll))
                {
                    LogHelper.WriteLine("未找到 UnlockServer.Provider.dll，请先编译凭据提供程序。");
                    return 2;
                }

                Directory.CreateDirectory(DataDir);
                GrantUsersModify(DataDir);

                var dest = GetSecureDllPath();
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(dll, dest, true);
                ProtectDllAcl(dest);

                RegisterProvider(dest);
                RemoveLegacyProviderKey();
                EnsurePulseEvent();
                LogHelper.WriteLine("本机解锁组件已注册: " + dest);
                return 0;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"安装本机解锁失败: {ex.Message}");
                return 1;
            }
        }

        private static string FindProviderDll()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(dir, ProviderDllName),
                Path.Combine(dir, "x64", ProviderDllName),
                Path.Combine(dir, "..", "..", "..", "UnlockServer.Provider", "bin", "Debug", ProviderDllName),
                Path.Combine(dir, "..", "..", "..", "UnlockServer.Provider", "bin", "Release", ProviderDllName)
            };
            foreach (var path in candidates)
            {
                var full = Path.GetFullPath(path);
                if (File.Exists(full)) return full;
            }
            return candidates[0];
        }

        private static void GrantUsersModify(string path)
        {
            var dir = new DirectoryInfo(path);
            var acl = dir.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            dir.SetAccessControl(acl);
        }

        private static string GetSecureDllPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "UnlockServer",
                ProviderDllName);
        }

        private static void ProtectDllAcl(string filePath)
        {
            var file = new FileInfo(filePath);
            var acl = file.GetAccessControl();
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            file.SetAccessControl(acl);

            var dir = file.Directory;
            if (dir != null)
            {
                var dirAcl = dir.GetAccessControl();
                dirAcl.SetAccessRuleProtection(true, false);
                dirAcl.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                dirAcl.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                dirAcl.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    FileSystemRights.ReadAndExecute,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                dir.SetAccessControl(dirAcl);
            }
        }

        private static void RemoveLegacyProviderKey()
        {
            try
            {
                var bare = ProviderGuid.Trim('{', '}');
                using (var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    lm.DeleteSubKeyTree(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\" + bare, false);
            }
            catch { }
        }

        private static void RegisterProvider(string dllPath)
        {
            using (var classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64))
            using (var clsid = classes.CreateSubKey(@"CLSID\" + ProviderGuid))
            {
                clsid.SetValue(null, "UnlockServer Credential Provider");
                using (var inproc = clsid.CreateSubKey("InprocServer32"))
                {
                    inproc.SetValue(null, dllPath);
                    inproc.SetValue("ThreadingModel", "Apartment");
                }
            }

            using (var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var cp = lm.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\" + ProviderGuid))
            {
                cp.SetValue(null, "UnlockServer");
            }
        }
    }
}
