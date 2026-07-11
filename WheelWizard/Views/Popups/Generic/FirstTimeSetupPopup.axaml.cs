using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using WheelWizard.DolphinManagent.Abstractions;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Popups.Base;

namespace WheelWizard.Views.Popups.Generic;

public partial class FirstTimeSetupPopup : PopupContent
{
    private sealed record DolphinCandidate(string DisplayName, string? Path, bool Found);

    private readonly TaskCompletionSource<bool> _completionSource = new();
    private bool _setupCompleted;

    private string? _selectedDolphinTarget;

    [Inject]
    private ILogger<FirstTimeSetupPopup> Logger { get; set; } = null!;

    [Inject]
    private IDolphinLocator DolphinLocator { get; set; } = null!;

    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    public FirstTimeSetupPopup()
        : base(true, false, true, "Wheel Wizard")
    {
        InitializeComponent();

        PlatformTextBlock.Text = $"Detected platform: {GetPlatformName()}";
        PopulateDetectedLocations();
    }

    public Task<bool> ShowAndAwaitCompletionAsync()
    {
        Show();
        return _completionSource.Task;
    }

    private static string GetPlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "macOS";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "Linux";
        return "Unknown";
    }

    private void PopulateDetectedLocations()
    {
        DetectedLocationsPanel.Children.Clear();

        var candidates = DolphinLocator.DetectInstallations();
        if (candidates.Count == 0)
        {
            DetectedLocationsPanel.Children.Add(
                new TextBlock
                {
                    Classes = { "BodyText" },
                    Opacity = 0.75,
                    TextWrapping = TextWrapping.Wrap,
                    Text = "No Dolphin installations were detected automatically. Please select one manually below.",
                }
            );
            return;
        }

        foreach (var candidate in candidates)
            DetectedLocationsPanel.Children.Add(CreateCandidateRow(candidate));
    }

    private RadioButton CreateCandidateRow(DolphinInstallation candidate)
    {
        var subtitle = candidate.Found ? candidate.LaunchTarget ?? string.Empty : "Not found on this system";

        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(
            new TextBlock
            {
                Classes = { "BodyText" },
                FontWeight = FontWeight.SemiBold,
                Text = candidate.DisplayName,
            }
        );
        content.Children.Add(
            new TextBlock
            {
                Classes = { "BodyText" },
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                Text = subtitle,
            }
        );

        var radio = new RadioButton
        {
            GroupName = "DolphinLocation",
            Content = content,
            IsEnabled = candidate.Found,
            Tag = candidate.LaunchTarget,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        radio.IsCheckedChanged += DetectedLocation_OnChecked;
        return radio;
    }

    private void DetectedLocation_OnChecked(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true } radio)
            return;

        ManualPathTextBox.Text = string.Empty;
        SetSelectedTarget(radio.Tag as string);
    }

    private async void BrowseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = await OpenDolphinFilePickerAsync();
            Logger.LogInformation(path);
            if (string.IsNullOrWhiteSpace(path))
                return;

            // A manual pick wins: clear any detected radio selection.
            foreach (var child in DetectedLocationsPanel.Children)
            {
                if (child is RadioButton radio)
                    radio.IsChecked = false;
            }

            ManualPathTextBox.Text = path;
            SetSelectedTarget(path);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to pick a Dolphin location.");
            ShowError("Something went wrong while selecting the file.");
        }
    }

    private void SetSelectedTarget(string? target)
    {
        _selectedDolphinTarget = string.IsNullOrWhiteSpace(target) ? null : target;
        ContinueButton.IsEnabled = _selectedDolphinTarget != null;
        ErrorTextBlock.IsVisible = false;
    }

    private void ContinueButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedDolphinTarget))
        {
            ShowError("Please select a Dolphin installation to continue.");
            return;
        }

        SettingsService.Set(SettingsService.DOLPHIN_LOCATION, _selectedDolphinTarget);

        _setupCompleted = true;
        _completionSource.TrySetResult(true);
        Close();
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close();

    protected override void BeforeClose() => _completionSource.TrySetResult(_setupCompleted);

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.IsVisible = true;
    }

    private static async Task<string?> OpenDolphinFilePickerAsync()
    {
        var executableFileType = new FilePickerFileType("Executable files")
        {
            Patterns = Environment.OSVersion.Platform switch
            {
                PlatformID.Win32NT => new[] { "*.exe" },
                PlatformID.Unix => new[] { "*", "*.sh" },
                PlatformID.MacOSX => new[] { "*", "*.app" },
                _ => new[] { "*" }, // Fallback
            },
        };
        var filePath = await FilePickerHelper.OpenSingleFileAsync("Select the Dolphin executable", [executableFileType]);
        return filePath;
    }
}
