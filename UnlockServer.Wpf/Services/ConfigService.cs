using System;
using UnlockServer.Models;

namespace UnlockServer.Services
{
    /// <summary>
    /// 配置服务 - 管理应用设置的读写
    /// </summary>
    public class ConfigService
    {
        private static ConfigService _instance;
        public static ConfigService Instance => _instance ?? (_instance = new ConfigService());

        private ConfigService() { }

        /// <summary>
        /// 加载所有配置
        /// </summary>
        public AppSettings LoadSettings()
        {
            var settings = new AppSettings();

            try
            {
                settings.ServerIp = OperateIniFile.ReadSafeString("setting", "ip", "127.0.0.1");
                
                int.TryParse(OperateIniFile.ReadSafeString("setting", "pt", "2084"), out int port);
                settings.ServerPort = port > 0 ? port : 2084;

                settings.Username = OperateIniFile.ReadSafeString("setting", "us", "");
                settings.Password = OperateIniFile.ReadSafeString("setting", "pd", "");

                int.TryParse(OperateIniFile.ReadIni("setting", "rssi", "-90"), out int rssi);
                settings.RssiThreshold = rssi;

                settings.DeviceAddress = OperateIniFile.ReadSafeString("setting", "address", "");
                settings.BluetoothType = OperateIniFile.ReadIniInt("setting", "type", 1);

                settings.AutoLock = OperateIniFile.ReadIniInt("setting", "autolock", 0) == 1;
                settings.AutoUnlock = OperateIniFile.ReadIniInt("setting", "autounlock", 0) == 1;
                settings.ManualLock = OperateIniFile.ReadIniInt("setting", "manuallock", 1) == 1;
                settings.ManualUnlock = OperateIniFile.ReadIniInt("setting", "manualunlock", 0) == 1;

                // 读取延迟设置
                settings.LockDelay = OperateIniFile.ReadIniInt("setting", "lockdelay", 20);
                settings.UnlockDelay = OperateIniFile.ReadIniInt("setting", "unlockdelay", 10);

                settings.AutoStart = AutoStartHelper.IsExists();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"加载配置失败: {ex.Message}");
            }

            return settings;
        }

        /// <summary>
        /// 保存所有配置
        /// </summary>
        public bool SaveSettings(AppSettings settings)
        {
            try
            {
                OperateIniFile.WriteSafeString("setting", "ip", settings.ServerIp);
                OperateIniFile.WriteSafeString("setting", "pt", settings.ServerPort.ToString());
                OperateIniFile.WriteSafeString("setting", "us", settings.Username);
                OperateIniFile.WriteSafeString("setting", "pd", settings.Password);
                OperateIniFile.WriteIniString("setting", "rssi", settings.RssiThreshold.ToString());
                OperateIniFile.WriteSafeString("setting", "address", settings.DeviceAddress);
                OperateIniFile.WriteIniInt("setting", "type", settings.BluetoothType);
                OperateIniFile.WriteIniInt("setting", "autolock", settings.AutoLock ? 1 : 0);
                OperateIniFile.WriteIniInt("setting", "autounlock", settings.AutoUnlock ? 1 : 0);
                OperateIniFile.WriteIniInt("setting", "manuallock", settings.ManualLock ? 1 : 0);
                OperateIniFile.WriteIniInt("setting", "manualunlock", settings.ManualUnlock ? 1 : 0);

                // 保存延迟设置
                OperateIniFile.WriteIniInt("setting", "lockdelay", settings.LockDelay);
                OperateIniFile.WriteIniInt("setting", "unlockdelay", settings.UnlockDelay);

                // 处理开机自启
                if (settings.AutoStart)
                {
                    if (!AutoStartHelper.IsExists())
                    {
                        AutoStartHelper.AddStart();
                    }
                }
                else
                {
                    if (AutoStartHelper.IsExists())
                    {
                        AutoStartHelper.RemoveStart();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"保存配置失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 保存单个设置
        /// </summary>
        public void SaveSetting(string key, string value)
        {
            OperateIniFile.WriteSafeString("setting", key, value);
        }

        /// <summary>
        /// 保存设备地址
        /// </summary>
        public void SaveDeviceAddress(string address)
        {
            OperateIniFile.WriteSafeString("setting", "address", address);
        }

        /// <summary>
        /// 保存蓝牙类型
        /// </summary>
        public void SaveBluetoothType(int type)
        {
            OperateIniFile.WriteIniInt("setting", "type", type);
        }
    }
}

