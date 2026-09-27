using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using BluetoothManager.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace BluetoothManager;

public partial class MainWindow : Window
{
    private readonly BluetoothService _bluetoothService = new();

    public ObservableCollection<BluetoothDeviceInfo> Devices { get; } = new();

    private bool _isScanning;

    public MainWindow()
    {
        InitializeComponent();

        DeviceList.ItemsSource = Devices;

        _bluetoothService.DeviceAdded += BluetoothService_DeviceAdded;
        _bluetoothService.DeviceUpdated += BluetoothService_DeviceUpdated;
        _bluetoothService.DeviceRemoved += BluetoothService_DeviceRemoved;
        _bluetoothService.ScanCompleted += BluetoothService_ScanCompleted;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(
        object? sender,
        RoutedEventArgs e)
    {
        await RefreshBluetooth();
    }

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _bluetoothService.StopScan();
    }

    private async void BluetoothToggle_Click(
        object? sender,
        RoutedEventArgs e)
    {
        BluetoothToggle.IsEnabled = false;

        try
        {
            bool currentState =
                await _bluetoothService.IsBluetoothOnAsync();

            bool newState = !currentState;

            bool changed =
                await _bluetoothService.SetBluetoothAsync(newState);

            if (!changed)
            {
                BluetoothStatus.Text = "Permission denied";
                return;
            }

            await Task.Delay(400);

            await RefreshBluetooth();
        }
        catch (Exception ex)
        {
            BluetoothStatus.Text = "Bluetooth error";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            BluetoothToggle.IsEnabled = true;
        }
    }

    private void RefreshButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        StartBluetoothScan();
    }

    private void PairButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not BluetoothDeviceInfo device)
            return;

        _ = PairDeviceAsync(device);
    }

    private void UnpairButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not BluetoothDeviceInfo device)
            return;

        _ = UnpairDeviceAsync(device);
    }

    private async Task PairDeviceAsync(
        BluetoothDeviceInfo device)
    {
        EmptyMessage.Text =
            $"Pairing with {device.Name}...";

        EmptyMessage.IsVisible = true;

        bool success =
            await _bluetoothService.PairAsync(device.Id);

        if (success)
        {
            EmptyMessage.Text =
                $"{device.Name} paired successfully.";

            await Task.Delay(800);

            StartBluetoothScan();
        }
        else
        {
            EmptyMessage.Text =
                $"Could not pair with {device.Name}.";
        }
    }

    private async Task UnpairDeviceAsync(
        BluetoothDeviceInfo device)
    {
        EmptyMessage.Text =
            $"Removing {device.Name}...";

        EmptyMessage.IsVisible = true;

        bool success =
            await _bluetoothService.UnpairAsync(device.Id);

        if (success)
        {
            EmptyMessage.Text =
                $"{device.Name} removed.";

            await Task.Delay(600);

            StartBluetoothScan();
        }
        else
        {
            EmptyMessage.Text =
                $"Could not remove {device.Name}.";
        }
    }

    private async Task RefreshBluetooth()
    {
        try
        {
            bool bluetoothOn =
                await _bluetoothService.IsBluetoothOnAsync();

            UpdateBluetoothUI(bluetoothOn);

            Devices.Clear();

            if (!bluetoothOn)
            {
                _bluetoothService.StopScan();

                _isScanning = false;

                ScanStatus.Text = "Bluetooth is off";

                EmptyMessage.Text =
                    "Bluetooth is turned off";

                EmptyMessage.IsVisible = true;

                return;
            }

            StartBluetoothScan();
        }
        catch (Exception ex)
        {
            BluetoothStatus.Text = "Bluetooth error";

            EmptyMessage.Text =
                "Could not start Bluetooth scanner.";

            EmptyMessage.IsVisible = true;

            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void StartBluetoothScan()
    {
        Devices.Clear();

        _isScanning = true;

        ScanStatus.Text =
            "Scanning for nearby devices...";

        EmptyMessage.Text =
            "Searching for Bluetooth devices...";

        EmptyMessage.IsVisible = true;

        _bluetoothService.StartScan();
    }

    private void BluetoothService_DeviceAdded(
        BluetoothDeviceInfo device)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var existing in Devices)
            {
                if (existing.Id == device.Id)
                    return;
            }

            Devices.Add(device);

            EmptyMessage.IsVisible = false;
        });
    }

    private void BluetoothService_DeviceUpdated(
        BluetoothDeviceInfo device)
    {
        Dispatcher.UIThread.Post(() =>
        {
            for (int i = 0; i < Devices.Count; i++)
            {
                if (Devices[i].Id != device.Id)
                    continue;

                Devices[i] = device;
                break;
            }
        });
    }

    private void BluetoothService_DeviceRemoved(
        string id)
    {
        Dispatcher.UIThread.Post(() =>
        {
            for (int i = Devices.Count - 1; i >= 0; i--)
            {
                if (Devices[i].Id == id)
                {
                    Devices.RemoveAt(i);
                    break;
                }
            }

            if (Devices.Count == 0 &&
                _isScanning)
            {
                EmptyMessage.Text =
                    "No nearby Bluetooth devices found";

                EmptyMessage.IsVisible = true;
            }
        });
    }

    private void BluetoothService_ScanCompleted()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isScanning = false;

            ScanStatus.Text =
                Devices.Count == 0
                    ? "No devices found"
                    : $"{Devices.Count} device(s) found";

            if (Devices.Count == 0)
            {
                EmptyMessage.Text =
                    "No nearby Bluetooth devices found";

                EmptyMessage.IsVisible = true;
            }
            else
            {
                EmptyMessage.IsVisible = false;
            }
        });
    }

    private void UpdateBluetoothUI(bool enabled)
    {
        var transform =
            BluetoothToggleThumb.RenderTransform
                as TranslateTransform;

        if (enabled)
        {
            BluetoothStatus.Text = "Bluetooth ON";

            BluetoothStatus.Foreground =
                new SolidColorBrush(
                    Color.Parse("#A8FFD0"));

            BluetoothStatusDot.Fill =
                new SolidColorBrush(
                    Color.Parse("#67F59A"));

            BluetoothToggle.Background =
                new SolidColorBrush(
                    Color.Parse("#3478D8FF"));

            if (transform != null)
                transform.X = 26;
        }
        else
        {
            BluetoothStatus.Text = "Bluetooth OFF";

            BluetoothStatus.Foreground =
                new SolidColorBrush(
                    Color.Parse("#FFB0B0"));

            BluetoothStatusDot.Fill =
                new SolidColorBrush(
                    Color.Parse("#FF6464"));

            BluetoothToggle.Background =
                new SolidColorBrush(
                    Color.Parse("#25FFFFFF"));

            if (transform != null)
                transform.X = 0;
        }
    }
}