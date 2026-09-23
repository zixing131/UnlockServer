using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
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

                int.TryParse(OperateIniFile.ReadIni("setting", "rssi", "-70"), out int rssi);
                settings.RssiThreshold = rssi;

                settings.DeviceAddress = OperateIniFile.ReadSafeString("setting", "address", "");
                settings.BluetoothType = OperateIniFile.ReadIniInt("setting", "type", 1);

                settings.AutoLock = OperateIniFile.ReadIniInt("setting", "autolock", 1) == 1;
                settings.AutoUnlock = OperateIniFile.ReadIniInt("setting", "autounlock", 1) == 1;
                settings.ManualLock = OperateIniFile.ReadIniInt("setting", "manuallock", 1) == 1;
                settings.ManualUnlock = OperateIniFile.ReadIniInt("setting", "manualunlock", 0) == 1;
                settings.LockWhenSeen = OperateIniFile.ReadIniInt("setting", "lockwhenseen", 0) == 1;

                // 读取延迟设置
                settings.LockDelay = OperateIniFile.ReadIniInt("setting", "lockdelay", 15);
                settings.UnlockDelay = OperateIniFile.ReadIniInt("setting", "unlockdelay", 3);
                settings.ActionWarnSeconds = OperateIniFile.ReadIniInt("setting", "actionwarn", 10);
                settings.LockOnlyWhenIdle = OperateIniFile.ReadIniInt("setting", "lockonlyidle", 1) == 1;
                settings.IdleLockSeconds = OperateIniFile.ReadIniInt("setting", "idlelock", 30);
                settings.HysteresisDb = OperateIniFile.ReadIniInt("setting", "hysteresis", 8);
                settings.PresenceTimeout = OperateIniFile.ReadIniInt("setting", "presencetimeout", 8);
                settings.RequireAllDevices = OperateIniFile.ReadIniInt("setting", "requireall", 0) == 1;
                settings.UseLocalUnlock = OperateIniFile.ReadIniInt("setting", "localunlock", 1) == 1;

                settings.Devices = LoadDevices();
                if (settings.Devices.Count == 0 && !string.IsNullOrWhiteSpace(settings.DeviceAddress))
                {
                    settings.Devices.Add(new BoundDevice
                    {
                        Name = ExtractName(settings.DeviceAddress),
                        Address = ExtractAddress(settings.DeviceAddress),
                        BluetoothType = settings.BluetoothType,
                        Enabled = true
                    });
                }

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
                OperateIniFile.WriteIniInt("setting", "lockwhenseen", settings.LockWhenSeen ? 1 : 0);

                // 保存延迟设置
                OperateIniFile.WriteIniInt("setting", "lockdelay", settings.LockDelay);
                OperateIniFile.WriteIniInt("setting", "unlockdelay", settings.UnlockDelay);
                OperateIniFile.WriteIniInt("setting", "actionwarn", settings.ActionWarnSeconds);
                OperateIniFile.WriteIniInt("setting", "lockonlyidle", settings.LockOnlyWhenIdle ? 1 : 0);
                OperateIniFile.WriteIniInt("setting", "idlelock", settings.IdleLockSeconds);
                OperateIniFile.WriteIniInt("setting", "hysteresis", settings.HysteresisDb);
                OperateIniFile.WriteIniInt("setting", "presencetimeout", settings.PresenceTimeout);
                OperateIniFile.WriteIniInt("setting", "requireall", settings.RequireAllDevices ? 1 : 0);
                OperateIniFile.WriteIniInt("setting", "localunlock", settings.UseLocalUnlock ? 1 : 0);
                SaveDevices(settings.Devices);

                if (settings.Devices != null && settings.Devices.Count > 0)
                {
                    var first = settings.Devices[0];
                    settings.DeviceAddress = string.IsNullOrEmpty(first.Name)
                        ? first.Address
                        : $"{first.Name}[{first.Address}]";
                    settings.BluetoothType = first.BluetoothType;
                    OperateIniFile.WriteSafeString("setting", "address", settings.DeviceAddress);
                    OperateIniFile.WriteIniInt("setting", "type", settings.BluetoothType);
                }

                if (settings.AutoStart)
                {
                    AutoStartHelper.AddStart();
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

        private static string DevicesPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devices.json");

        private static List<BoundDevice> LoadDevices()
        {
            try
            {
                if (!File.Exists(DevicesPath))
                    return new List<BoundDevice>();

                var ser = new DataContractJsonSerializer(typeof(List<BoundDevice>));
                using (var fs = File.OpenRead(DevicesPath))
                {
                    return (ser.ReadObject(fs) as List<BoundDevice>) ?? new List<BoundDevice>();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"加载设备列表失败: {ex.Message}");
                return new List<BoundDevice>();
            }
        }

        private static void SaveDevices(List<BoundDevice> devices)
        {
            try
            {
                var ser = new DataContractJsonSerializer(typeof(List<BoundDevice>));
                using (var fs = File.Create(DevicesPath))
                {
                    ser.WriteObject(fs, devices ?? new List<BoundDevice>());
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"保存设备列表失败: {ex.Message}");
            }
        }

        private static string ExtractName(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var start = input.LastIndexOf('[');
            return start > 0 ? input.Substring(0, start) : "";
        }

        private static string ExtractAddress(string input)
        {
            return BluetoothDiscover.NormalizeAddress(input) ?? input;
        }
    }
}

