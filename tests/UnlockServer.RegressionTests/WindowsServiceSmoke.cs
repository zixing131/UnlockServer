// Compiled separately by scripts/test-windows-service.ps1 against the real WPF
// assembly. Does not invoke lock/unlock or install the Credential Provider.
using System;
using System.Reflection;
using UnlockServer.Models;
using UnlockServer.Services;
using UnlockServer.ViewModels;

class WindowsServiceSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Address = "AA:BB:CC:DD:EE:FF";
    static void Set(object value, string name, object field) => value.GetType().GetField(name, Private).SetValue(value, field);
    static object Get(object value, string name) => value.GetType().GetField(name, Private).GetValue(value);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Private).Invoke(value, args);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }

    [STAThread]
    static void Main()
    {
        var service = BluetoothService.Shared;
        try
        {
            // Seed a running all-types owner without starting real OS scans/timers.
            service.BluetoothType = 0;
            Set(service, "<IsScanning>k__BackingField", true);
            Set(service, "_scanUsers", 1);
            var vm = new DeviceListViewModel(0);
            vm.StartScanning(); vm.StartScanning();
            Check((int)Get(service, "_scanUsers") == 2, "one discovery reference per dialog");
            vm.StopScanning(); vm.Dispose(); vm.Dispose();
            Check((int)Get(service, "_scanUsers") == 1 && service.IsScanning, "closing dialog preserves monitor scan");

            Call(service, "Upsert", Address, "Watch", (short)-50, "BLE", false, true);
            var before = service.GetDeviceByAddress(Address);
            Call(service, "Upsert", Address, null, (short)-75, "BLE", false, true);
            Check(before.Rssi == -50 && service.GetDeviceByAddress(Address).Rssi == -75, "published cache snapshots are immutable");
            var seen = service.GetDeviceByAddress(Address).LastSeen;
            Call(service, "Upsert", Address, "Cached metadata", (short)-100, "BLE", true, false);
            Check(service.GetDeviceByAddress(Address).LastSeen == seen, "metadata never refreshes proximity age");
            service.AddManualDevice(Address, type: 1);
            Check(service.GetDeviceByAddress(Address).Type == "Classic", "manual type agrees with cached type");

            Set(service, "_lastAdvertisement", DateTime.Now.AddMinutes(-2));
            Set(service, "_startedAt", DateTime.Now.AddMinutes(-3));
            Set(service, "_stallSince", DateTime.MinValue);
            Check(service.LooksLikeScanStall(), "silent scan initially has recovery grace");
            Set(service, "_stallSince", DateTime.Now.AddSeconds(-13));
            Check(!service.LooksLikeScanStall(), "silence cannot keep absent devices present indefinitely");

            // Actual WinRT watcher creation on Windows; no paired connections or
            // credentials. A VM without Bluetooth may reject Start, which is caught.
            Set(service, "_lastAdvRestart", DateTime.Now.AddMinutes(-2));
            Call(service, "RestartAdvertisementWatcher", "regression smoke");
            var attempted = (DateTime)Get(service, "_lastAdvRestart");
            Check((DateTime.Now - attempted).TotalSeconds < 5, "recovery still attempted after grace expired");
            Call(service, "RestartAdvertisementWatcher", "cooldown smoke");
            Check((DateTime)Get(service, "_lastAdvRestart") == attempted, "recovery is rate limited");
            Set(service, "_lastAdvRestart", DateTime.Now.AddMinutes(-2));
            Call(service, "RestartAdvertisementWatcher", "retry smoke");
            Check((DateTime.Now - (DateTime)Get(service, "_lastAdvRestart")).TotalSeconds < 5, "recovery retries instead of permanently giving up");
            Call(service, "SetConnected", Address, true);
            service.StopScan();
            Check(!service.GetDeviceByAddress(Address).IsConnected && !service.IsAddressConnected(Address), "stop clears cached connection state");
            Console.WriteLine("Windows service smoke passed; real Bluetooth reception still needs hardware testing.");
        }
        finally { service.Dispose(); }
    }
}
