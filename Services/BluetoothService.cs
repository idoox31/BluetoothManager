using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace BluetoothManager.Services;

public class BluetoothDeviceInfo
{
    public string Name { get; set; } = "Unknown device";
    public string Id { get; set; } = "";

    public bool IsConnected { get; set; }
    public bool IsPaired { get; set; }
    public bool IsPresent { get; set; }

    public string Type { get; set; } = "Bluetooth";

    public int SignalStrength { get; set; } = -100;

    public DateTime LastSeen { get; set; } = DateTime.Now;

    public string Status
    {
        get
        {
            if (IsConnected)
                return "Connected";

            if (IsPaired)
                return "Paired";

            return "Nearby";
        }
    }

    public string SignalText
    {
        get
        {
            if (SignalStrength >= -55)
                return "Excellent signal";

            if (SignalStrength >= -70)
                return "Good signal";

            if (SignalStrength >= -85)
                return "Weak signal";

            return "Very weak signal";
        }
    }
}

public class BluetoothService
{
    private DeviceWatcher? _deviceWatcher;
    private BluetoothLEAdvertisementWatcher? _bleWatcher;

    private readonly Dictionary<string, BluetoothDeviceInfo> _devices =
        new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cleanupCancellation;

    public event Action<BluetoothDeviceInfo>? DeviceAdded;
    public event Action<BluetoothDeviceInfo>? DeviceUpdated;
    public event Action<string>? DeviceRemoved;
    public event Action? ScanCompleted;

    private async Task<Radio?> GetBluetoothRadioAsync()
    {
        var radios = await Radio.GetRadiosAsync();

        foreach (var radio in radios)
        {
            if (radio.Kind == RadioKind.Bluetooth)
                return radio;
        }

        return null;
    }

    public async Task<bool> IsBluetoothOnAsync()
    {
        var radio = await GetBluetoothRadioAsync();

        return radio != null &&
               radio.State == RadioState.On;
    }

    public async Task<bool> SetBluetoothAsync(bool enabled)
    {
        var radio = await GetBluetoothRadioAsync();

        if (radio == null)
            return false;

        var targetState =
            enabled ? RadioState.On : RadioState.Off;

        var result =
            await radio.SetStateAsync(targetState);

        return result == RadioAccessStatus.Allowed &&
               radio.State == targetState;
    }

    public void StartScan()
    {
        StopScan();

        _devices.Clear();

        StartBleWatcher();
        StartDeviceWatcher();

        _cleanupCancellation =
            new CancellationTokenSource();

        _ = CleanupLoopAsync(
            _cleanupCancellation.Token);
    }

    private void StartBleWatcher()
    {
        _bleWatcher =
            new BluetoothLEAdvertisementWatcher
            {
                ScanningMode =
                    BluetoothLEScanningMode.Active
            };

        _bleWatcher.Received += BleWatcher_Received;

        _bleWatcher.Start();
    }

    private void StartDeviceWatcher()
    {
        string selector =
            BluetoothDevice.GetDeviceSelectorFromPairingState(false);

        _deviceWatcher =
            DeviceInformation.CreateWatcher(
                selector,
                new[]
                {
                    "System.Devices.Aep.IsConnected",
                    "System.Devices.Aep.IsPaired",
                    "System.Devices.Aep.IsPresent"
                },
                DeviceInformationKind.AssociationEndpoint);

        _deviceWatcher.Added += DeviceWatcher_Added;
        _deviceWatcher.Updated += DeviceWatcher_Updated;
        _deviceWatcher.Removed += DeviceWatcher_Removed;
        _deviceWatcher.EnumerationCompleted +=
            DeviceWatcher_EnumerationCompleted;

        _deviceWatcher.Start();
    }

    public void StopScan()
    {
        try
        {
            _cleanupCancellation?.Cancel();
        }
        catch
        {
        }

        _cleanupCancellation = null;

        if (_bleWatcher != null)
        {
            try
            {
                _bleWatcher.Stop();
            }
            catch
            {
            }

            _bleWatcher.Received -= BleWatcher_Received;
            _bleWatcher = null;
        }

        if (_deviceWatcher != null)
        {
            try
            {
                _deviceWatcher.Stop();
            }
            catch
            {
            }

            _deviceWatcher.Added -= DeviceWatcher_Added;
            _deviceWatcher.Updated -= DeviceWatcher_Updated;
            _deviceWatcher.Removed -= DeviceWatcher_Removed;
            _deviceWatcher.EnumerationCompleted -=
                DeviceWatcher_EnumerationCompleted;

            _deviceWatcher = null;
        }

        _devices.Clear();
    }

    // ============================================================
    // BLE LIVE DISCOVERY
    // ============================================================

    private void BleWatcher_Received(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        ulong address = args.BluetoothAddress;

        if (address == 0)
            return;

        string id = address.ToString("X12");

        string name =
            args.Advertisement.LocalName;

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Bluetooth device ({id})";
        }

        int rssi =
            args.RawSignalStrengthInDBm;

        if (_devices.TryGetValue(id, out var existing))
        {
            existing.LastSeen = DateTime.Now;
            existing.IsPresent = true;

            if (!string.IsNullOrWhiteSpace(
                    args.Advertisement.LocalName))
            {
                existing.Name =
                    args.Advertisement.LocalName;
            }

            existing.SignalStrength = rssi;

            DeviceUpdated?.Invoke(existing);
            return;
        }

        var device =
            new BluetoothDeviceInfo
            {
                Name = name,
                Id = id,
                IsConnected = false,
                IsPaired = false,
                IsPresent = true,
                Type = "Bluetooth LE",
                SignalStrength = rssi,
                LastSeen = DateTime.Now
            };

        _devices[id] = device;

        DeviceAdded?.Invoke(device);
    }

    // ============================================================
    // WINDOWS DEVICE WATCHER
    // ============================================================

    private void DeviceWatcher_Added(
        DeviceWatcher sender,
        DeviceInformation device)
    {
        if (string.IsNullOrWhiteSpace(device.Name))
            return;

        bool present =
            GetBool(
                device,
                "System.Devices.Aep.IsPresent");

        bool connected =
            GetBool(
                device,
                "System.Devices.Aep.IsConnected");

        bool paired =
            GetBool(
                device,
                "System.Devices.Aep.IsPaired");

        if (!present && !connected)
            return;

        string id = device.Id;

        if (_devices.TryGetValue(id, out var existing))
        {
            existing.IsPresent = present;
            existing.IsConnected = connected;
            existing.IsPaired = paired;
            existing.LastSeen = DateTime.Now;

            DeviceUpdated?.Invoke(existing);
            return;
        }

        var info =
            new BluetoothDeviceInfo
            {
                Name = device.Name,
                Id = id,
                IsConnected = connected,
                IsPaired = paired,
                IsPresent = present,
                Type = "Bluetooth",
                LastSeen = DateTime.Now
            };

        _devices[id] = info;

        DeviceAdded?.Invoke(info);
    }

    private void DeviceWatcher_Updated(
        DeviceWatcher sender,
        DeviceInformationUpdate update)
    {
        if (!_devices.TryGetValue(
                update.Id,
                out var device))
        {
            return;
        }

        device.LastSeen = DateTime.Now;

        DeviceUpdated?.Invoke(device);
    }

    private void DeviceWatcher_Removed(
        DeviceWatcher sender,
        DeviceInformationUpdate update)
    {
        if (_devices.Remove(update.Id))
        {
            DeviceRemoved?.Invoke(update.Id);
        }
    }

    private void DeviceWatcher_EnumerationCompleted(
        DeviceWatcher sender,
        object args)
    {
        ScanCompleted?.Invoke();
    }

    // ============================================================
    // REMOVE BLE DEVICES THAT STOP BROADCASTING
    // ============================================================

    private async Task CleanupLoopAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(8),
                    cancellationToken);
            }
            catch
            {
                break;
            }

            var now = DateTime.Now;
            var expired = new List<string>();

            foreach (var pair in _devices)
            {
                var device = pair.Value;

                if (device.Type == "Bluetooth LE" &&
                    !device.IsConnected &&
                    (now - device.LastSeen).TotalSeconds > 10)
                {
                    expired.Add(pair.Key);
                }
            }

            foreach (var id in expired)
            {
                if (_devices.Remove(id))
                {
                    DeviceRemoved?.Invoke(id);
                }
            }
        }
    }

    // ============================================================
    // PAIR
    // ============================================================

    public async Task<bool> PairAsync(
        string deviceId)
    {
        try
        {
            var device =
                await DeviceInformation.CreateFromIdAsync(
                    deviceId);

            if (device == null)
                return false;

            if (device.Pairing.IsPaired)
                return true;

            var result =
                await device.Pairing.PairAsync();

            return result.Status ==
                   DevicePairingResultStatus.Paired;
        }
        catch
        {
            return false;
        }
    }

    // ============================================================
    // UNPAIR
    // ============================================================

    public async Task<bool> UnpairAsync(
        string deviceId)
    {
        try
        {
            var device =
                await DeviceInformation.CreateFromIdAsync(
                    deviceId);

            if (device == null)
                return false;

            if (!device.Pairing.IsPaired)
                return true;

            var result =
                await device.Pairing.UnpairAsync();

            return result.Status ==
                   DeviceUnpairingResultStatus.Unpaired;
        }
        catch
        {
            return false;
        }
    }

    private static bool GetBool(
        DeviceInformation device,
        string property)
    {
        if (!device.Properties.TryGetValue(
                property,
                out var value))
        {
            return false;
        }

        return value is bool b && b;
    }
}