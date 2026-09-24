using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnlockServer;
using UnlockServer.Models;
using UnlockServer.Services;
using UnlockServer.ViewModels;
using UnlockServer.Views;
using System.Windows;

class Program
{
    const string Address = "AA:BB:CC:DD:EE:FF";
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
    static object Get(object obj, string name) => obj.GetType().GetField(name, Private).GetValue(obj);
    static void Call(object obj, string name) => obj.GetType().GetMethod(name, Private).Invoke(obj, null);
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Tick(UnlockManager manager) => Call(manager, "Tick");
    static void Expire(UnlockManager manager) => Set(manager, "_pendingUntil", DateTime.Now.AddSeconds(-1));
    static void Reset()
    {
        Application.Current.Dispatcher.Drain();
        BluetoothService.Shared.Devices.Clear();
        BluetoothService.Shared.Stall = false;
        BluetoothService.Shared.Users = 0;
        BluetoothService.Shared.BluetoothType = 2;
        WanClient.Locks = WanClient.Unlocks = 0;
        ToastService.Last = null;
    }
    static UnlockManager Manager()
    {
        var manager = new UnlockManager { isautolock = true, lockOnlyWhenIdle = false, actionWarnSeconds = 10,
            sessionSwitchClass = new SessionSwitchClass(), manuallock = false };
        manager.SetBoundDevices(new[] { new BoundDevice { Address = Address } });
        Set(manager, "_bluetooth", BluetoothService.Shared);
        Set(manager, "_deviceLeftTime", (DateTime?)DateTime.Now.AddMinutes(-4));
        return manager;
    }
    static void Nearby()
    {
        BluetoothService.Shared.Devices.Add(new BluetoothDeviceModel { Address = Address, Name = "Watch",
            Type = "BLE", Rssi = -50, LastSeen = DateTime.Now });
    }
    static void Test(string name, Action test)
    {
        Reset(); test(); Console.WriteLine("PASS " + name);
    }
    static void Main()
    {
        Test("manual unlock grace expires with no Bluetooth return", () =>
        {
            var manager = Manager();
            Set(manager, "_lastSoftwareLockTime", DateTime.Now);
            Call(manager, "HandleUserUnlockAfterSoftwareLock");
            Tick(manager);
            Assert(Get(manager, "_pending").ToString() == "None", "must suppress lock during grace");
            Set(manager, "_suppressLockUntil", DateTime.Now.AddSeconds(-1));
            Tick(manager); Application.Current.Dispatcher.Drain();
            Assert(ToastService.Last != null, "must show countdown after grace");
            Expire(manager);
            ToastService.Last.Timeout();
            Assert(WanClient.Locks == 0, "UI timeout must not execute OS actions");
            var uiThread = Environment.CurrentManagedThreadId;
            Task.Run(() => Tick(manager)).GetAwaiter().GetResult();
            Assert(WanClient.Locks == 1 && WanClient.LockThread != uiThread, "monitor must lock exactly once off UI thread");
            Application.Current.Dispatcher.Drain();
            Assert(ToastService.Last.Dismissed, "countdown must be dismissed after execution");
        });
        Test("device returns at deadline cancels locking", () =>
        {
            var manager = Manager(); Tick(manager); Expire(manager); Nearby(); Tick(manager);
            Assert(WanClient.Locks == 0 && Get(manager, "_pending").ToString() == "None", "revalidate presence before locking");
        });
        Test("cancel countdown needs no manager lock on UI", () =>
        {
            var manager = Manager(); Tick(manager); Application.Current.Dispatcher.Drain();
            var gate = Get(manager, "lockLock");
            using (var held = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var worker = Task.Run(() => { lock (gate) { held.Set(); release.Wait(); } });
                held.Wait();
                var cancel = Task.Run(() => ToastService.Last.Cancel());
                bool completed = cancel.Wait(1000);
                release.Set(); worker.Wait(); cancel.Wait();
                Assert(completed, "cancel callback blocked on monitor lock");
            }
            Expire(manager); Tick(manager);
            Assert(WanClient.Locks == 0, "cancel must win over expired countdown");
        });
        Test("stale countdown cannot cancel a newer countdown", () =>
        {
            var manager = Manager(); Tick(manager); Application.Current.Dispatcher.Drain();
            var old = ToastService.Last; Nearby(); Tick(manager);
            BluetoothService.Shared.Devices.Clear();
            Set(manager, "_deviceLeftTime", (DateTime?)DateTime.Now.AddMinutes(-1));
            Tick(manager); Application.Current.Dispatcher.Drain(); old.Cancel(); Expire(manager); Tick(manager);
            Assert(WanClient.Locks == 1, "old cancellation affected new request");
        });
        Test("silence grace ends and departure can lock", () =>
        {
            var manager = Manager(); BluetoothService.Shared.Stall = true; Tick(manager);
            Assert(Get(manager, "_pending").ToString() == "None", "must hold during bounded recovery");
            BluetoothService.Shared.Stall = false; Tick(manager); Expire(manager); Tick(manager);
            Assert(WanClient.Locks == 1, "silence must not suppress lock forever");
        });
        Test("locked session cannot unlock from stale RSSI", () =>
        {
            var manager = Manager(); manager.isautounlock = true; manager.sessionSwitchClass.IsLocked = true;
            Nearby(); Tick(manager);
            BluetoothService.Shared.Devices[0].LastSeen = DateTime.Now.AddMinutes(-1);
            Set(manager, "_deviceEnteredTime", (DateTime?)DateTime.Now.AddMinutes(-1));
            BluetoothService.Shared.Stall = true; LocalUnlock.ClickUnlock = true; Tick(manager);
            Assert(WanClient.Unlocks == 0, "stale RSSI authorized click unlock");
        });
        Test("rapid stop/start cannot resurrect delayed monitor", () =>
        {
            var manager = Manager(); manager.Start(); manager.Stop(); manager.Start(); manager.Stop();
            Thread.Sleep(2100);
            Assert(BluetoothService.Shared.Users == 0 && WanClient.Locks == 0, "stopped monitor resurrected");
        });
        Test("device dialog owns one scan reference and isolated models", () =>
        {
            BluetoothService.Shared.Users = 1; Nearby();
            BluetoothService.Shared.Devices.Add(new BluetoothDeviceModel { Address = "11:22:33:44:55:66", Name = "Headset", Type = "Classic", Rssi = -65 });
            var vm = new DeviceListViewModel(0); vm.StartScanning(); vm.StartScanning();
            Call(vm, "RefreshDeviceList");
            Assert(BluetoothService.Shared.Users == 2 && vm.Devices.Count == 2, "unbalanced acquire or missing list");
            Assert(!ReferenceEquals(vm.Devices[0], BluetoothService.Shared.Devices[0]), "UI shares scanner model");
            vm.BluetoothType = 1;
            Assert(vm.Devices.Count == 1 && vm.Devices[0].Type == "Classic", "classic filter failed");
            vm.BluetoothType = 2; vm.SearchText = "watch";
            Assert(vm.Devices.Count == 1 && vm.Devices[0].Type == "BLE", "BLE/name filters failed");
            Assert(BluetoothService.Shared.BluetoothType == 2, "dialog overwrote monitor scan type");
            BluetoothService.Shared.Devices[0].Rssi = -75;
            Assert(vm.Devices[0].Rssi == -50, "scanner directly mutated UI");
            Call(vm, "RefreshDeviceList"); Assert(vm.Devices[0].Rssi == -75, "snapshot not refreshed");
            vm.StopScanning(); vm.Dispose(); vm.Dispose();
            Assert(BluetoothService.Shared.Users == 1, "closing dialog stopped main monitor");
        });
        Console.WriteLine("All regression scenarios passed (OS/Bluetooth side effects simulated).");
    }
}
