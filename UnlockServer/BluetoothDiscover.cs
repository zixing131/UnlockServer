using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace UnlockServer
{
    public class BluetoothDiscover
    {

        public static object locker = new object();
        static string[] requestedProperties = new string[] { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected", "System.Devices.Aep.SignalStrength", "System.ItemNameDisplay" };

        private const string SignalStrengthProperty = "System.Devices.Aep.SignalStrength"; 
        private const string DeviceAddress = "System.Devices.Aep.DeviceAddress"; 
        private const string ItemNameDisplay = "System.ItemNameDisplay";

        public const string BluetoothId = "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\")";
        public const string BluetoothLEId = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";
        private DeviceWatcher BluetoothDeviceWatcher = null;
        private DeviceWatcher BluetoothLEDeviceWatcher = null;

        public BluetoothDiscover(int bletype = 1)
        {
            if(bletype==1)
            {  
                BluetoothDeviceWatcher = DeviceInformation.CreateWatcher(BluetoothId, requestedProperties, DeviceInformationKind.AssociationEndpoint);
                 
                BluetoothDeviceWatcher.Added += new TypedEventHandler<DeviceWatcher, DeviceInformation>(async (watcher, deviceInfo) =>
                { 
                    if (deviceInfo.Name != "")
                    {
#if DEBUG
                        if(deviceInfo.Name.Contains("HUAWEI"))
                        {

                        }
#endif
                        if (deviceInfo.Properties.ContainsKey(SignalStrengthProperty) == false)
                        {
                            return;
                        }
                        var prop = deviceInfo.Properties[SignalStrengthProperty];
                        if (prop == null)
                        {
                            return;
                        }
                        var rssi = short.Parse(prop.ToString());
                        var address = deviceInfo.Properties[DeviceAddress].ToString();
                        MybluetoothDevice mybluetooth = new MybluetoothDevice()
                        {
                            Name = deviceInfo.Name,
                            Address = address,
                            Rssi = rssi,
                            Type = "Classic"
                        };
                        lock (locker)
                        {
                            var r = mybluetoothDevices.FirstOrDefault(p => address.Contains(p.Address));
                            if (r == null)
                            {
                                mybluetoothDevices.Add(mybluetooth);
                            }
                            else
                            {
                                r.Rssi = rssi;
                            }
                        }

                    }
                });
                 
                BluetoothDeviceWatcher.Updated += new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
                {
                    if (deviceInfoUpdate.Properties.ContainsKey(SignalStrengthProperty) == false)
                    {
                        return;
                    }
                    var prop = deviceInfoUpdate.Properties[SignalStrengthProperty];
                    if (prop == null)
                    {
                        return;
                    }
                    var rssi = short.Parse(prop.ToString());

                    var address = deviceInfoUpdate.Id;
                    lock (locker)
                    {
                        bool con = false;

                        foreach (var device in mybluetoothDevices)
                        {
                            if (address.Contains(device.Address))
                            {
                                device.Rssi = rssi;
                                con = true;
                            }
                        }
                        if (con == false)
                        {
                            var name = "未知设备";
                            address = address.Split('-')[1];
                            MybluetoothDevice mybluetooth = new MybluetoothDevice()
                            {
                                Name = name,
                                Address = address,
                                Rssi = rssi,
                                Type = "Classic"
                            };
                            mybluetoothDevices.Add(mybluetooth);
                        } 
                    }
                });
                 
                BluetoothDeviceWatcher.EnumerationCompleted += new TypedEventHandler<DeviceWatcher, Object>(async (watcher, obj) =>
                {

                });
                 
                BluetoothDeviceWatcher.Removed += new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
                {
                    var address = deviceInfoUpdate.Id;
                    lock (locker)
                    {
                        var r = mybluetoothDevices.FirstOrDefault(p => address.Contains(p.Address));
                        if (r != null)
                        {
                            mybluetoothDevices.Remove(r);
                        }
                    }
                });

                 
                BluetoothDeviceWatcher.Stopped += new TypedEventHandler<DeviceWatcher, Object>(async (watcher, obj) =>
                {

                });

            }
            else
            {
                BluetoothLEDeviceWatcher = DeviceInformation.CreateWatcher(BluetoothLEId,
                                                                requestedProperties,
                                                                DeviceInformationKind.AssociationEndpoint);

                //接続可能なデバイス候補が出現した際に呼び出されるイベントハンドラ
                BluetoothLEDeviceWatcher.Added += new TypedEventHandler<DeviceWatcher, DeviceInformation>(async (watcher, deviceInfo) =>
                {

                    // Make sure device name isn't blank
                    if (deviceInfo.Name != "")
                    {
#if DEBUG
                        if(deviceInfo.Name.Contains("HUAWEI"))
                        {

                        }
#endif
                        if (deviceInfo.Properties.ContainsKey(SignalStrengthProperty) == false)
                        {
                            return;
                        }
                        var prop = deviceInfo.Properties[SignalStrengthProperty];
                        if (prop == null)
                        {
                            return;
                        }
                        var rssi = short.Parse(prop.ToString());
                        var address = deviceInfo.Properties[DeviceAddress].ToString();
                        MybluetoothDevice mybluetooth = new MybluetoothDevice()
                        {
                            Name = deviceInfo.Name,
                            Address = address,
                            Rssi = rssi,
                            Type = "BLE"
                        };
                        lock (locker)
                        {
                            var r = mybluetoothDevices.FirstOrDefault(p => address.Contains(p.Address));
                            if (r == null)
                            {
                                mybluetoothDevices.Add(mybluetooth);
                            }
                            else
                            {
                                r.Rssi = rssi;
                            }
                        }
                    }
                }); 

                BluetoothLEDeviceWatcher.Updated += new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
                {
                    if (deviceInfoUpdate.Properties.ContainsKey(SignalStrengthProperty) == false)
                    {
                        return;
                    }
                    var prop = deviceInfoUpdate.Properties[SignalStrengthProperty];
                    if (prop == null)
                    {
                        return;
                    }

                    var address = deviceInfoUpdate.Id;
                    var rssi = short.Parse(prop.ToString());
                    lock (locker)
                    {
                        bool con = false;

                        foreach (var device in mybluetoothDevices)
                        {
                            if (address.Contains(device.Address))
                            {
#if DEBUG
                        if(deviceInfo.Name.Contains("HUAWEI"))
                        {

                        }
#endif
                                device.Rssi = rssi;
                                con = true;
                            } 
                        }
                        if(con == false)
                        {
                            var name = "未知设备";
                            address = address.Split('-')[1];
                            MybluetoothDevice mybluetooth = new MybluetoothDevice()
                            {
                                Name = name,
                                Address = address,
                                Rssi = rssi,
                                Type = "BLE"
                            };
                            mybluetoothDevices.Add(mybluetooth);
                        }

                    }
                });
                 
                BluetoothLEDeviceWatcher.EnumerationCompleted += new TypedEventHandler<DeviceWatcher, Object>(async (watcher, obj) =>
                {

                });
                 
                BluetoothLEDeviceWatcher.Removed += new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
                {
                    var address = deviceInfoUpdate.Id;
                    lock (locker)
                    {
                        var r = mybluetoothDevices.FirstOrDefault(p => address.Contains(p.Address));
                        if (r != null)
                        {
#if DEBUG
                        if(deviceInfo.Name.Contains("HUAWEI"))
                        {

                        }
#endif
                            mybluetoothDevices.Remove(r);
                        }
                    }
                });
                 
                BluetoothLEDeviceWatcher.Stopped += new TypedEventHandler<DeviceWatcher, Object>(async (watcher, obj) =>
                {

                });

            }
        }

        private List<MybluetoothDevice> mybluetoothDevices = new List<MybluetoothDevice>();

        public void StartDiscover()
        {
            BluetoothDeviceWatcher?.Start();
            BluetoothLEDeviceWatcher?.Start();
        }
        public void StopDiscover()
        {
            BluetoothDeviceWatcher?.Stop();
            BluetoothLEDeviceWatcher?.Stop();
            lock (locker)
            {
                mybluetoothDevices.Clear();
            } 
        }


        public List<MybluetoothDevice> getAllDevice()
        {
            lock(locker)
            { 
                return mybluetoothDevices;
            }
        }
    }
}
