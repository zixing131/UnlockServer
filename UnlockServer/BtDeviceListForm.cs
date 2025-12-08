using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace UnlockServer
{
    public partial class BtDeviceListForm : Form
    {
        public string address = "";
        public int bletype = 1;

        private BluetoothDiscover bluetoothDiscover;
        private System.Windows.Forms.Timer refreshTimer;
        private readonly Dictionary<string, MybluetoothDevice> devicesByAddress =
            new Dictionary<string, MybluetoothDevice>(StringComparer.OrdinalIgnoreCase);

        // 记录当前选中的地址（用于刷新后保持选中）
        private string selectedAddress = null;

        // ===== 新增：防“乱跳”所需的平滑与限频字段 =====
        // 平滑 RSSI 用（地址 -> 平滑值）
        private readonly Dictionary<string, double> rssiSmooth = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private const double RssiAlpha = 0.25; // 指数平滑系数，越小越稳，0.2~0.3 可选

        // 限制重排频率与名次变化阈值
        private DateTime lastReorderTime = DateTime.MinValue;
        private static readonly TimeSpan MinReorderInterval = TimeSpan.FromSeconds(3); // 最短重排间隔
        private const int ReorderHysteresis = 4; // 仅当名次变化>=4位才提前强制重排

        public BtDeviceListForm(int bletype)
        {
            InitializeComponent();
            this.bletype = bletype;
            if(bletype == 1)
            {
                rdbclassic.Checked = true;
            }
            else
            {
                rdbble.Checked = true;  
            }
            this.Load += BtDeviceListForm_Load;
            this.FormClosing += BtDeviceListForm_FormClosing;
        }

        private void BtDeviceListForm_Load(object sender, EventArgs e)
        {
            selectedAddress = OperateIniFile.ReadSafeString("setting", "address", "");
            StartDiscoverAndUiRefresh();
        }

        private void BtDeviceListForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopDiscoverAndUiRefresh();
        }

        private void StartDiscoverAndUiRefresh()
        {
            // 停止旧实例
            StopDiscoverAndUiRefresh();

            // 启动新的扫描
            bluetoothDiscover = new BluetoothDiscover(bletype);
            bluetoothDiscover.StartDiscover();

            // UI 定时刷新（在 UI 线程执行，不需要 Invoke）
            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 500; // 500ms 刷新一次
            refreshTimer.Tick += (s, e) => RefreshDeviceList();
            refreshTimer.Start();
        }

        private void StopDiscoverAndUiRefresh()
        {
            try
            {
                if (refreshTimer != null)
                {
                    refreshTimer.Stop();
                    refreshTimer.Dispose();
                    refreshTimer = null;
                }
            }
            catch { /* ignore */ }

            try
            {
                if (bluetoothDiscover != null)
                {
                    bluetoothDiscover.StopDiscover();
                    bluetoothDiscover = null;
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// 将设备转为展示文本
        /// </summary>
        private static string BuildDisplay(MybluetoothDevice d)
        {
            // 例：HUAWEI (BLE) -55dBm [AA:BB:CC:DD:EE:FF]
            return $"{(string.IsNullOrEmpty(d.Name) ? "未知设备" : d.Name)} ({d.Type}) {d.Rssi}dBm [{d.Address}]";
        }

        /// <summary>
        /// 更新并获取平滑后的 RSSI
        /// </summary>
        private double UpdateSmoothedRssi(string addr, int rssiRaw)
        {
            if (!rssiSmooth.TryGetValue(addr, out var smooth))
            {
                smooth = rssiRaw; // 第一次就用原值
            }
            else
            {
                smooth = RssiAlpha * rssiRaw + (1 - RssiAlpha) * smooth;
            }
            rssiSmooth[addr] = smooth;
            return smooth;
        }

        /// <summary>
        /// 每次 Timer Tick 刷新列表；使用平滑 + 限频重排，避免“乱跳”
        /// </summary>
        private void RefreshDeviceList()
        {
            if (bluetoothDiscover == null) return;

            var snapshot = bluetoothDiscover.getAllDevice();
            if (snapshot == null) return;

            // 构建 address->device 的最新快照（防止同地址重复）
            var latest = new Dictionary<string, MybluetoothDevice>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in snapshot)
            {
                if (string.IsNullOrWhiteSpace(d?.Address)) continue;
                latest[d.Address.ToLower()] = d;
            }

            // --- 判断是否有变化（数量变化或任一项属性变化）---
            bool changed = false;

            if (latest.Count != devicesByAddress.Count)
            {
                changed = true;
            }
            else
            {
                foreach (var kv in latest)
                {
                    if (!devicesByAddress.TryGetValue(kv.Key, out var old))
                    {
                        changed = true; break;
                    }
                    var nw = kv.Value;
                    if (!StringEquals(old.Name, nw.Name) ||
                        !StringEquals(old.Type, nw.Type) ||
                        old.Rssi != nw.Rssi)
                    {
                        changed = true; break;
                    }
                }
            }

            if (!changed) return;

            // --- 更新内部缓存 ---
            devicesByAddress.Clear();
            foreach (var kv in latest) devicesByAddress[kv.Key] = kv.Value;

            // --- 更新平滑 RSSI 并准备排序键 ---
            var listWithKey = new List<(string addr, MybluetoothDevice dev, double key)>();
            foreach (var d in devicesByAddress.Values)
            {
                var smooth = UpdateSmoothedRssi(d.Address, d.Rssi);
                // 排序键：优先平滑后的 RSSI，其次地址稳定排序，避免相等时抖动
                listWithKey.Add((d.Address, d, smooth));
            }

            // --- 判断是否需要“重排”（名次大变或超过最小重排间隔）---
            bool needReorder = (DateTime.Now - lastReorderTime) >= MinReorderInterval;

            // 期望顺序（按平滑 RSSI 排序，不立即重排，仅用于比较）
            var desiredOrder = listWithKey
                .OrderByDescending(t => t.key)
                .ThenBy(t => t.addr, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.addr)
                .ToList();

            // 如果还没到重排时间，可根据名次变化阈值决定是否提前重排
            if (!needReorder && lstbldevice.Items.Count > 0)
            {
                // 现有顺序（按当前 ListBox）
                var currentOrder = lstbldevice.Items.Cast<object>()
                    .Select(it => (it as DeviceListItem)?.Address)
                    .Where(a => !string.IsNullOrEmpty(a))
                    .ToList();

                // 计算每个地址的名次差
                var indexMap = desiredOrder
                    .Select((a, idx) => (a, idx))
                    .ToDictionary(x => x.a, x => x.idx, StringComparer.OrdinalIgnoreCase);

                int maxRankShift = 0;
                for (int i = 0; i < currentOrder.Count; i++)
                {
                    var addr = currentOrder[i];
                    if (addr != null && indexMap.TryGetValue(addr, out var newIdx))
                    {
                        maxRankShift = Math.Max(maxRankShift, Math.Abs(newIdx - i));
                    }
                }

                // 只有当名次变化很大时才强制提前重排
                if (maxRankShift >= ReorderHysteresis)
                    needReorder = true;
            }

            // --- 如果不需要重排，仅更新文本显示并返回（避免“跳”）---
            if (!needReorder)
            {
                lstbldevice.BeginUpdate();
                try
                {
                    // 用地址映射快速更新显示文本（名称/类型/RSSI）
                    var map = devicesByAddress;
                    for (int i = 0; i < lstbldevice.Items.Count; i++)
                    {
                        if (lstbldevice.Items[i] is DeviceListItem it &&
                            map.TryGetValue(it.Address, out var dNow))
                        {
                            it.Display = BuildDisplay(dNow); // 更新显示文本
                            it.Name = dNow.Name;
                            // 赋回去触发重绘
                            lstbldevice.Items[i] = it;
                        }
                    }
                }
                finally
                {
                    lstbldevice.EndUpdate();
                }
                return;
            }

            // --- 真正执行重排（到达间隔 或 变化很大）---
            lastReorderTime = DateTime.Now;

            // 记住滚动位置与选择
            int topIndex = 0;
            try { topIndex = lstbldevice.TopIndex; } catch { /* 某些情况下会抛例外，忽略 */ }

            string preferred = selectedAddress;
            if (string.IsNullOrEmpty(preferred))
            {
                // 尝试用 INI 里的地址
                var iniAddr = OperateIniFile.ReadSafeString("setting", "address", "");
                if (!string.IsNullOrEmpty(iniAddr)) preferred = iniAddr;
            }

            lstbldevice.BeginUpdate();
            try
            {
                lstbldevice.Items.Clear();

                foreach (var t in listWithKey
                    .OrderByDescending(x => x.key)
                    .ThenBy(x => x.addr, StringComparer.OrdinalIgnoreCase))
                {
                    lstbldevice.Items.Add(new DeviceListItem
                    {
                        Address = t.addr,
                        Display = BuildDisplay(t.dev),
                        Name = t.dev.Name
                    });
                }

                // 恢复选择
                if (!string.IsNullOrEmpty(preferred))
                {
                    for (int idx = 0; idx < lstbldevice.Items.Count; idx++)
                    {
                        if (lstbldevice.Items[idx] is DeviceListItem it &&
                            it.Address.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                        {
                            lstbldevice.SelectedIndex = idx;
                            break;
                        }
                    }
                }
                else if (lstbldevice.Items.Count > 0)
                {
                    lstbldevice.SelectedIndex = 0;
                }

                // 恢复滚动位置，避免“跳”到列表顶部
                try { lstbldevice.TopIndex = Math.Min(topIndex, lstbldevice.Items.Count - 1); } catch { }
            }
            finally
            {
                lstbldevice.EndUpdate();
            }
        }

        private static bool StringEquals(string a, string b) =>
            string.Equals(a ?? "", b ?? "", StringComparison.Ordinal);

        private void rdbclassic_CheckedChanged(object sender, EventArgs e)
        {
            setType();
        }

        private void rdbble_CheckedChanged(object sender, EventArgs e)
        {
            setType();
        }

        private void setType()
        {
            int type = 0;
            if (rdbclassic.Checked) type = 1;
            if (rdbble.Checked) type = 2;

            if (type == 0 || type == bletype) return; // 未改变则不重启

            bletype = type;
            try { OperateIniFile.WriteIniInt("setting", "type", type); } catch { }

            // 切换协议后，重启扫描与刷新
            StartDiscoverAndUiRefresh();
        }

        private void lstbldevice_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstbldevice.SelectedItem is DeviceListItem it)
            {
                selectedAddress = it.Address;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (lstbldevice.Items.Count <= 0 || lstbldevice.SelectedIndex == -1)
            {
                MessageBox.Show("请选择一个项目！");
                return;
            }

            if (lstbldevice.SelectedItem is DeviceListItem it)
            {
                address = it.Name + "[" + it.Address+ "]"; // 返回纯地址
                //address = it.Address; // 返回纯地址
                try { OperateIniFile.WriteSafeString("setting", "address", address); } catch { }
                DialogResult = DialogResult.OK;
            }
            else
            {
                MessageBox.Show("所选项目无效！");
            }
        }

        /// <summary>
        /// ListBox 用的展示项：ToString() 返回展示文本，包含地址用于选择恢复
        /// </summary>
        private sealed class DeviceListItem
        {
            public string Address { get; set; }
            public string Display { get; set; }
            public string Name { get; set; }
            public override string ToString() => Display ?? Address ?? "";
        }
    }

    // ====== 说明 ======
    // 1) 本文件依赖以下外部类型/控件：
    //    - BluetoothDiscover：提供 StartDiscover()/StopDiscover()/getAllDevice()
    //    - MybluetoothDevice：含 Name、Type、Rssi、Address 属性
    //    - OperateIniFile：ReadSafeString/WriteSafeString/WriteIniInt
    //    - 窗体控件：lstbldevice(ListBox)、rdbclassic(RadioButton)、rdbble(RadioButton)、button1(Button)
    //
    // 2) 可调参数：
    //    - RssiAlpha：0~1，越小越稳，默认 0.25
    //    - MinReorderInterval：最短重排间隔，默认 3 秒
    //    - ReorderHysteresis：名次变化阈值，默认 4 位
}
