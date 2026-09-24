using System;
using System.Collections.Generic;
using System.Threading;
using UnlockServer.Models;

// This project deliberately replaces OS/UI side effects, not the production
// UnlockManager / DeviceListViewModel logic under test.
namespace System.Windows
{
    public class Application
    {
        public static Application Current { get; } = new Application();
        public Threading.Dispatcher Dispatcher { get; } = new Threading.Dispatcher();
    }
}
namespace System.Windows.Threading
{
    public class Dispatcher
    {
        private readonly Queue<Action> queue = new Queue<Action>();
        public void BeginInvoke(Action action) { lock (queue) queue.Enqueue(action); }
        public bool CheckAccess() => true;
        public void Drain()
        {
            while (true)
            {
                Action action;
                lock (queue) { if (queue.Count == 0) return; action = queue.Dequeue(); }
                action();
            }
        }
    }
    public class DispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler Tick;
        public void Start() { }
        public void Stop() { }
    }
}
namespace System.Windows.Input
{
    public static class CommandManager
    {
        public static event EventHandler RequerySuggested;
        public static void InvalidateRequerySuggested() { }
    }
}
namespace InTheHand.Net.Bluetooth
{
    public class BluetoothRadio { public static BluetoothRadio Default { get; } = new BluetoothRadio(); }
}
namespace UnlockServer
{
    public static class BluetoothDiscover { public static string NormalizeAddress(string address) => address; }
    public static class LogHelper { public static void WriteLine(string message) { } }
    public class SessionSwitchClass
    {
        public bool IsLocked, isUnlockBySoft, isLockBySoft, dolocking, dounlocking;
        public Action SessionLockAction, SessionUnlockAction;
        public void Close() { }
    }
    public static class WanClient
    {
        public static int Locks, Unlocks, LockThread;
        public static bool IsSessionLocked() => false;
        public static void LockPc() { Locks++; LockThread = Environment.CurrentManagedThreadId; }
        public static bool UnlockPc() { Unlocks++; return true; }
        public static bool isConfigVal() => true;
    }
    public static class LocalUnlock
    {
        public static bool ClickUnlock;
        public static bool CanUnlock() => false;
        public static bool RequestUnlock() => true;
        public static bool ConsumeUnlockNow() { var result = ClickUnlock; ClickUnlock = false; return result; }
        public static bool ConsumeUnlockCancel() => false;
        public static void ClearUnlockHint() { }
        public static void WriteUnlockHint(string hint) { }
        public static void ClearUnlockWarn() { }
        public static void WriteUnlockWarn(int seconds) { }
    }
}
namespace UnlockServer.Views
{
    public static class MessageDialog
    {
        public static void ShowError(string text) => throw new Exception(text);
        public static void ShowWarning(string text) => throw new Exception(text);
    }
    public class ActionToast
    {
        public Action Cancel, Timeout;
        public bool Dismissed;
        public void Dismiss() { Dismissed = true; }
    }
    public static class ToastService
    {
        public static ActionToast Last;
        public static void Show(string text) { }
        public static ActionToast ShowAction(string title, string hint, int seconds, Action cancel, Action timeout)
        {
            return Last = new ActionToast { Cancel = cancel, Timeout = timeout };
        }
    }
}
namespace UnlockServer.Services
{
    public class BluetoothService
    {
        public static BluetoothService Shared { get; } = new BluetoothService();
        public readonly List<BluetoothDeviceModel> Devices = new List<BluetoothDeviceModel>();
        public int Users, BluetoothType, ScanSilenceSeconds;
        public bool Stall, IsRefreshing;
        public bool IsScanning => Users > 0;
        public void AddScanUser(bool discovery = false) { Users++; }
        public void RemoveScanUser(bool discovery = false) { Users--; }
        public void PinAddresses(IEnumerable<string> addresses) { }
        public bool IsAddressConnected(string address) => false;
        public bool LooksLikeScanStall() => Stall;
        public List<BluetoothDeviceModel> GetDevices() => new List<BluetoothDeviceModel>(Devices);
        public void RestartWatchers(string reason) { }
        public void NotifySessionLocked() { }
        public void NotifySessionUnlocked() { }
        public BluetoothDeviceModel AddManualDevice(string address, string name = "", int? type = null)
        {
            var device = new BluetoothDeviceModel { Address = address, Name = name, Type = type == 1 ? "Classic" : "BLE" };
            Devices.Add(device);
            return device;
        }
    }
}
