using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Windows.Devices.Enumeration;
using System.Text.RegularExpressions;

namespace UnlockServer
{
    public class BluetoothDiscover
    {

    // 放在 BluetoothDiscover 内部的 Helpers 区域
    private static readonly Regex MacRegex =
    new Regex(@"([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}", RegexOptions.Compiled);

    // 从任意输入中提取“最后一个”形如 AA:BB:CC:DD:EE:FF 的 MAC
    private static string NormalizeAddress(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;

        // 先找所有 MAC 片段，返回最后一个（对 "adapter-mac - peripheral-mac" 很稳）
        var matches = MacRegex.Matches(s);
        if (matches.Count > 0)
            return matches[matches.Count - 1].Value.ToUpperInvariant();

        // 次优兜底：如果有 '-'，取最后一段
        var idx = s.LastIndexOf('-', (int)StringComparison.Ordinal);
        if (idx >= 0 && idx + 1 < s.Length)
            return s.Substring(idx + 1).Trim().ToUpperInvariant();

        // 再兜底：直接返回原串（尽量不返回 null）
        return s.Trim().ToUpperInvariant();
    }

    // 修改这两个方法，让返回值都走 NormalizeAddress：

    private static string GetAddressFromAddEvent(IReadOnlyDictionary<string, object> props, string idFallback)
    {
        string raw = null;
        if (props != null && props.TryGetValue(DeviceAddressProperty, out var addrObj))
            raw = addrObj?.ToString();

        // 不管来自 props 还是 id，都做一次 Normalize
        var norm = NormalizeAddress(raw);
        if (!string.IsNullOrEmpty(norm)) return norm;

        return NormalizeAddress(idFallback);
    }

    // 属性名常量
    private static readonly string[] requestedProperties = new[]
        {
            "System.Devices.Aep.DeviceAddress",
            "System.Devices.Aep.IsConnected",
            "System.Devices.Aep.SignalStrength",
            "System.ItemNameDisplay"
        };

        private const string SignalStrengthProperty = "System.Devices.Aep.SignalStrength";
        private const string DeviceAddressProperty = "System.Devices.Aep.DeviceAddress";
        private const string ItemNameDisplay = "System.ItemNameDisplay";

        public const string BluetoothId = "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\")";
        public const string BluetoothLEId = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";

        private DeviceWatcher _classicWatcher;
        private DeviceWatcher _bleWatcher;

        // 地址 -> 设备
        private readonly ConcurrentDictionary<string, MybluetoothDevice> _devices =
            new ConcurrentDictionary<string, MybluetoothDevice>(StringComparer.OrdinalIgnoreCase);

        // RSSI 更新阈值（绝对变化小于此值则忽略），避免 UI 频繁刷新
        private const int RssiDeltaThreshold = 2;

        // 只在构造时决定扫描协议：1 = Classic，2 = BLE
        private readonly int _bleType;

        public BluetoothDiscover(int bletype = 1)
        {
            _bleType = bletype == 2 ? 2 : 1;
            if (_bleType == 1)
            {
                _classicWatcher = DeviceInformation.CreateWatcher(
                    BluetoothId, requestedProperties, DeviceInformationKind.AssociationEndpoint);
            }
            else
            {
                _bleWatcher = DeviceInformation.CreateWatcher(
                    BluetoothLEId, requestedProperties, DeviceInformationKind.AssociationEndpoint);
            }
        }

        public void StartDiscover()
        {
            // 避免重复绑定
            StopDiscover();

            if (_bleType == 1 && _classicWatcher != null)
            {
                HookClassic(_classicWatcher);
                _classicWatcher.Start();
            }
            else if (_bleType == 2 && _bleWatcher != null)
            {
                HookBle(_bleWatcher);
                _bleWatcher.Start();
            }
        }

        public void StopDiscover()
        {
            if (_classicWatcher != null)
            {
                UnhookClassic(_classicWatcher);
                if (_classicWatcher.Status == DeviceWatcherStatus.Started ||
                    _classicWatcher.Status == DeviceWatcherStatus.EnumerationCompleted)
                {
                    _classicWatcher.Stop();
                }
            }

            if (_bleWatcher != null)
            {
                UnhookBle(_bleWatcher);
                if (_bleWatcher.Status == DeviceWatcherStatus.Started ||
                    _bleWatcher.Status == DeviceWatcherStatus.EnumerationCompleted)
                {
                    _bleWatcher.Stop();
                }
            }

            _devices.Clear();
        }

        public List<MybluetoothDevice> getAllDevice()
        {
            // 返回快照，避免外部遍历时与内部并发修改冲突
            return _devices.Values.ToList();
        }

        #region Classic hooks
        private void HookClassic(DeviceWatcher watcher)
        {
            watcher.Added += Classic_Added;
            watcher.Updated += Classic_Updated;
            watcher.Removed += Classic_Removed;
            watcher.EnumerationCompleted += Watcher_EnumerationCompleted;
            watcher.Stopped += Watcher_Stopped;
        }

        private void UnhookClassic(DeviceWatcher watcher)
        {
            watcher.Added -= Classic_Added;
            watcher.Updated -= Classic_Updated;
            watcher.Removed -= Classic_Removed;
            watcher.EnumerationCompleted -= Watcher_EnumerationCompleted;
            watcher.Stopped -= Watcher_Stopped;
        }

        private void Classic_Added(DeviceWatcher watcher, DeviceInformation deviceInfo)
        {
            if (string.IsNullOrEmpty(deviceInfo?.Name)) return;
            if (!TryGetRssi(deviceInfo.Properties, out short rssi)) return;

            var address = GetAddressFromAddEvent(deviceInfo.Properties, deviceInfo.Id);
            if (string.IsNullOrEmpty(address)) return;

            var device = new MybluetoothDevice
            {
                Name = deviceInfo.Name,
                Address = address,
                Rssi = rssi,
                Type = "Classic"
            };

            UpsertDevice(address, device, rssi);
        }

        private void Classic_Updated(DeviceWatcher watcher, DeviceInformationUpdate update)
        {
            if (!TryGetRssi(update.Properties, out short rssi)) return;

            var address = ExtractAddressFromId(update.Id);
            if (string.IsNullOrEmpty(address)) return;

            UpdateRssiIfChanged(address, rssi);
        }

        private void Classic_Removed(DeviceWatcher watcher, DeviceInformationUpdate update)
        {
            var address = ExtractAddressFromId(update.Id);
            if (string.IsNullOrEmpty(address)) return;

            _devices.TryRemove(address, out _);
        }
        #endregion

        #region BLE hooks
        private void HookBle(DeviceWatcher watcher)
        {
            watcher.Added += Ble_Added;
            watcher.Updated += Ble_Updated;
            watcher.Removed += Ble_Removed;
            watcher.EnumerationCompleted += Watcher_EnumerationCompleted;
            watcher.Stopped += Watcher_Stopped;
        }

        private void UnhookBle(DeviceWatcher watcher)
        {
            watcher.Added -= Ble_Added;
            watcher.Updated -= Ble_Updated;
            watcher.Removed -= Ble_Removed;
            watcher.EnumerationCompleted -= Watcher_EnumerationCompleted;
            watcher.Stopped -= Watcher_Stopped;
        }

        private void Ble_Added(DeviceWatcher watcher, DeviceInformation deviceInfo)
        {
            if (string.IsNullOrEmpty(deviceInfo?.Name)) return;
            if (!TryGetRssi(deviceInfo.Properties, out short rssi)) return;

            var address = GetAddressFromAddEvent(deviceInfo.Properties, deviceInfo.Id);
            if (string.IsNullOrEmpty(address)) return;

            var device = new MybluetoothDevice
            {
                Name = deviceInfo.Name,
                Address = address,
                Rssi = rssi,
                Type = "BLE"
            };

            UpsertDevice(address, device, rssi);
        }


        private void Ble_Updated(DeviceWatcher watcher, DeviceInformationUpdate update)
        {
            if (!TryGetRssi(update.Properties, out short rssi)) return;

            var address = ExtractAddressFromId(update.Id);
            if (string.IsNullOrEmpty(address)) return;

            UpdateRssiIfChanged(address, rssi);
        }

        private void Ble_Removed(DeviceWatcher watcher, DeviceInformationUpdate update)
        {
            var address = ExtractAddressFromId(update.Id);
            if (string.IsNullOrEmpty(address)) return;

            _devices.TryRemove(address, out _);
        }
        #endregion

        #region Common watcher handlers
        private void Watcher_EnumerationCompleted(DeviceWatcher watcher, object _)
        {
            // 可在此处触发一次批量 UI 刷新（如果你有 UI 层）
        }

        private void Watcher_Stopped(DeviceWatcher watcher, object _)
        {
            // 停止时的清理已在 StopDiscover 做了
        }
        #endregion

        #region Helpers
        private static bool TryGetRssi(IReadOnlyDictionary<string, object> props, out short rssi)
        {
            rssi = 0;
            if (props == null) return false;
            if (!props.TryGetValue(SignalStrengthProperty, out var val) || val == null) return false;

            // 有些平台类型为 int/long；尽量稳健转换
            try
            {
                var n = Convert.ToInt32(val);
                if (n < short.MinValue) n = short.MinValue;
                if (n > short.MaxValue) n = short.MaxValue;
                rssi = (short)n;
                return true;
            }
            catch
            {
                return false;
            }
        } 
        private static string ExtractAddressFromId(string id)
        {
            // 直接用 NormalizeAddress 统一处理
            return NormalizeAddress(id);
        }

        private void UpsertDevice(string address, MybluetoothDevice newDevice, short rssi)
        {
            _devices.AddOrUpdate(
                address,
                addValue: newDevice,
                updateValueFactory: (_, existing) =>
                {
                    // 名称可能为空或改变：尽量填更有意义的名字
                    if (!string.IsNullOrEmpty(newDevice.Name) && !string.Equals(existing.Name, newDevice.Name, StringComparison.Ordinal))
                        existing.Name = newDevice.Name;

                    // RSSI 变化过滤
                    if (Math.Abs(existing.Rssi - rssi) >= RssiDeltaThreshold)
                        existing.Rssi = rssi;

                    // 协议类型以首次为准（也可按需更新）
                    return existing;
                });
        }

        private void UpdateRssiIfChanged(string address, short rssi)
        {
            _devices.AddOrUpdate(
                address,
                // 如果未见过该设备，建一个“未知设备”占位
                addValue: new MybluetoothDevice
                {
                    Name = "未知设备",
                    Address = address,
                    Rssi = rssi,
                    Type = _bleType == 1 ? "Classic" : "BLE"
                },
                updateValueFactory: (_, existing) =>
                {
                    if (Math.Abs(existing.Rssi - rssi) >= RssiDeltaThreshold)
                        existing.Rssi = rssi;
                    return existing;
                });
        }
        #endregion
    }
}
