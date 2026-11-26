using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using DawnPro.Core;
using Microsoft.Win32;

namespace DawnPro.Windows;

public partial class MainWindow : Window
{
    private static readonly string SavedSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DawnPro-GUI", "config.json");

    private readonly CancellationTokenSource _lifetime = new();
    private DawnProState? _current;
    private string? _devicePath;
    private bool _busy;
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();

        GainBox.ItemsSource = Enum.GetValues<GainMode>();
        LedBox.ItemsSource = Enum.GetValues<LedMode>().Select(mode =>
            KeyValuePair.Create(mode, DawnProPresets.Label(mode)));
        FilterBox.ItemsSource = Enum.GetValues<ReconstructionFilter>().Select(filter =>
            KeyValuePair.Create(filter, DawnProPresets.Label(filter)));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await RefreshAsync();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        await RunDeviceOperationAsync("Reading DAC settings…", "Connected to Moondrop Dawn Pro.", async token =>
        {
            var result = await Task.Run(async () =>
            {
                using var transport = DawnProHidTransport.Open();
                DawnProState snapshot = await new DawnProController(transport).ReadAsync(token);
                return (transport.DevicePath, snapshot);
            }, token);

            if (!_closed)
            {
                _current = result.snapshot;
                _devicePath = result.DevicePath;
                ShowDraft(result.snapshot.Settings);
            }
        });
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (_current is not { } expected || _devicePath is not { } path || GetDraft() is not { } draft)
            return;

        await RunDeviceOperationAsync("Applying changes…", "The DAC confirmed the changes.", async token =>
        {
            DawnProState confirmed = await Task.Run(async () =>
            {
                using var transport = DawnProHidTransport.Open(path);
                return await new DawnProController(transport).ApplyAsync(expected, draft, token);
            }, token);

            if (!_closed)
            {
                _current = confirmed;
                ShowDraft(confirmed.Settings);
            }
        });
    }

    private async Task RunDeviceOperationAsync(string pending, string complete, Func<CancellationToken, Task> operation)
    {
        if (_busy)
            return;

        _busy = true;
        SetStatus(pending);
        UpdateControls();
        try
        {
            await operation(_lifetime.Token);
            if (!_closed)
                SetStatus(complete);
        }
        catch (OperationCanceledException) when (_closed)
        {
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException
                                      or UnauthorizedAccessException)
        {
            _current = null;
            _devicePath = null;
            if (!_closed)
                SetStatus(error.Message);
        }
        finally
        {
            _busy = false;
            if (_closed)
                _lifetime.Dispose();
            else
                UpdateControls();
        }
    }

    private void OnDraftChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            UpdateControls();
    }

    private DawnProSettings? GetDraft() =>
        GainBox.SelectedItem is GainMode gain && LedBox.SelectedValue is LedMode led &&
        FilterBox.SelectedValue is ReconstructionFilter filter
            ? new DawnProSettings((int)VolumeSlider.Value, gain, led, filter)
            : null;

    private void ShowDraft(DawnProSettings values)
    {
        VolumeSlider.Value = values.VolumeStep;
        GainBox.SelectedItem = values.Gain;
        LedBox.SelectedValue = values.Led;
        FilterBox.SelectedValue = values.Filter;
        UpdateControls();
    }

    private void UpdateControls()
    {
        bool connected = _current.HasValue;
        bool canEdit = connected && !_busy;
        DawnProSettings? draft = GetDraft();
        VolumeText.Text = connected ? $"{(int)VolumeSlider.Value} / 60" : "—";
        ControlsPanel.IsEnabled = canEdit;
        RefreshButton.IsEnabled = !_busy;
        ApplyButton.IsEnabled = canEdit && draft.HasValue && draft != _current?.Settings;
        SaveButton.IsEnabled = canEdit && draft.HasValue;
        LoadButton.IsEnabled = canEdit && File.Exists(SavedSettingsPath);
        ImportButton.IsEnabled = canEdit;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (GetDraft() is not { } draft)
            return;

        RunFileOperation(() =>
        {
            DawnProPresets.Save(SavedSettingsPath, draft);
            SetStatus("Settings saved on this PC.");
        });
    }

    private void OnLoad(object sender, RoutedEventArgs e) => LoadSettings(SavedSettingsPath);

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Dawn Pro settings",
            Filter = "JSON files (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            LoadSettings(dialog.FileName);
    }

    private void LoadSettings(string path)
    {
        if (_current is not { } current)
            return;

        RunFileOperation(() =>
        {
            ShowDraft(DawnProPresets.Load(path, current.Settings));
            SetStatus("Settings loaded. Select Apply to DAC to use them.");
        });
    }

    private void RunFileOperation(Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
                                      or JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            SetStatus($"Settings file error. {error.Message}");
        }

        UpdateControls();
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        UIElementAutomationPeer.FromElement(StatusText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _lifetime.Cancel();
        if (!_busy)
            _lifetime.Dispose();
        base.OnClosed(e);
    }
}
