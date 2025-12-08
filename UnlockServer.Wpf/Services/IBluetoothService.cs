using System;
using System.Collections.Generic;
using UnlockServer.Models;

namespace UnlockServer.Services
{
    /// <summary>
    /// 蓝牙服务接口
    /// </summary>
    public interface IBluetoothService
    {
        /// <summary>
        /// 是否正在扫描
        /// </summary>
        bool IsScanning { get; }

        /// <summary>
        /// 蓝牙类型（1=Classic, 2=BLE）
        /// </summary>
        int BluetoothType { get; set; }

        /// <summary>
        /// 开始扫描
        /// </summary>
        void StartScan();

        /// <summary>
        /// 停止扫描
        /// </summary>
        void StopScan();

        /// <summary>
        /// 获取所有发现的设备
        /// </summary>
        List<BluetoothDeviceModel> GetDevices();

        /// <summary>
        /// 根据地址获取设备
        /// </summary>
        BluetoothDeviceModel GetDeviceByAddress(string address);

        /// <summary>
        /// 设备发现事件
        /// </summary>
        event EventHandler<BluetoothDeviceModel> DeviceDiscovered;

        /// <summary>
        /// 设备更新事件
        /// </summary>
        event EventHandler<BluetoothDeviceModel> DeviceUpdated;

        /// <summary>
        /// 设备丢失事件
        /// </summary>
        event EventHandler<string> DeviceLost;
    }
}

