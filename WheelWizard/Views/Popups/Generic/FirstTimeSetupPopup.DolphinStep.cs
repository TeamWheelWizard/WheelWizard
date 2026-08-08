using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using WheelWizard.DolphinManagement.Abstractions;
using WheelWizard.Services;
using WheelWizard.Shared.DependencyInjection;

namespace WheelWizard.Views.Popups.Generic;

public partial class FirstTimeSetupPopup
{
    private string? _selectedDolphinTarget;

    [Inject]
    private IDolphinLocator DolphinLocator { get; set; } = null!;

    [Inject]
    private IDolphinInstaller DolphinInstaller { get; set; } = null!;

    private bool IsDolphinStepComplete => !string.IsNullOrWhiteSpace(_selectedDolphinTarget);

    private void InitializeDolphinStep()
    {
        PlatformTextBlock.Text = $"Detected platform: {GetPlatformName()}";
        //BrowseButton.IsEnabled = EnvHelper.IsFlatpakSandboxed(); Not sure how to check for flatpak installation because this doesnt really work
        PopulateDetectedLocations();
    }

    private void SaveDolphinStep() => SettingsService.Set(SettingsService.DOLPHIN_LOCATION, _selectedDolphinTarget!);

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

    private Grid CreateCandidateRow(DolphinInstallation candidate)
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

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(radio);

        if (!candidate.Found && candidate.DisplayName.Contains("flatpak", StringComparison.OrdinalIgnoreCase))
        {
            var installButton = new Components.Button
            {
                Variant = Components.Button.ButtonsVariantType.Default,
                Text = "Install",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            installButton.Click += InstallFlatpakButton_OnClick;
            Grid.SetColumn(installButton, 1);
            row.Children.Add(installButton);
        }

        return row;
    }

    private async void InstallFlatpakButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var progressWindow = new ProgressWindow()
            .SetGoal(t("progress.installing_dolphin"))
            .SetExtraText(t("progress.this_may_take_a_while"));
        progressWindow.Show();
        var progress = new Progress<int>(progressWindow.UpdateProgress);
        var installResult = await DolphinInstaller.InstallDolphin(progress);
        progressWindow.Close();
        if (installResult.IsFailure)
        {
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Failed to install Dolphin")
                .SetInfoText(installResult.Error.Message)
                .ShowDialog();
            return;
        }

        // Reload all radio buttons
        // TODO: Also reload the subtext of every radio button after install
        var candidates = DolphinLocator.DetectInstallations();
        foreach (var radio in DetectedLocationsPanel.Children.SelectMany(GetRadioButtons))
        {
            if (radio?.Tag is null)
                continue;
            var match = candidates.FirstOrDefault(c => radio.Tag.Equals(c.LaunchTarget));
            if (match != null)
            {
                radio.IsEnabled = match.Found;
            }
        }
    }

    private static IEnumerable<RadioButton> GetRadioButtons(Control row) => row is Panel panel ? panel.Children.OfType<RadioButton>() : [];

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
            var path = await OpenDolphinFilePickerAsync(this);
            Logger.LogInformation(path);
            if (string.IsNullOrWhiteSpace(path))
                return;

            // A manual pick wins: clear any detected radio selection.
            foreach (var radio in DetectedLocationsPanel.Children.SelectMany(GetRadioButtons))
                radio.IsChecked = false;

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
        UpdateContinueState();
    }

    private static async Task<string?> OpenDolphinFilePickerAsync(Visual owner)
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
        var filePath = await FilePickerHelper.OpenSingleFileAsync("Select the Dolphin executable", [executableFileType], owner);
        return filePath;
    }
}
