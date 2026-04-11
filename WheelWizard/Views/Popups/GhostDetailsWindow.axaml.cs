using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Models;

namespace WheelWizard.Views.Popups;

public partial class GhostDetailsWindow : Window
{
    public GhostDetailsWindow()
    {
        InitializeComponent();
    }
    
    public GhostDetailsWindow(GhostSubmission submission, GhostTrackInfo? trackInfo = null) : this()
    {
        DataContext = submission;
        
        PopulateLapTimes(submission, trackInfo);
    }
    
    private void PopulateLapTimes(GhostSubmission submission, GhostTrackInfo? trackInfo)
    {
        LapTimesContainer.Children.Clear();
        
        var expectedLaps = trackInfo?.Laps ?? 3;
        
        var actualLaps = submission.LapSplitsDisplay.Take(expectedLaps).ToList();
        
        for (int i = 0; i < actualLaps.Count; i++)
        {
            var grid = CreateLapRow($"Lap {i + 1}:", actualLaps[i]);
            LapTimesContainer.Children.Add(grid);
        }

        var averageMs = submission.LapSplitsMs.Take(expectedLaps).DefaultIfEmpty(0).Average();
        var avgGrid = CreateLapRow("Average Lap:", FormatTime((int)averageMs));
        LapTimesContainer.Children.Add(avgGrid);

        var fastestGrid = CreateLapRow("Fastest Lap:", submission.FastestLapDisplay, isBold: true, bottomMargin: 0);
        LapTimesContainer.Children.Add(fastestGrid);
    }

    private static Grid CreateLapRow(string label, string time, bool isBold = false, int bottomMargin = 4)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Margin = new Avalonia.Thickness(0, 0, 0, bottomMargin)
        };

        var labelText = new TextBlock
        {
            Text = label,
            Classes = { "BodyText" },
            Margin = new Avalonia.Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(labelText, 0);

        var timeText = new TextBlock
        {
            Text = time,
            Classes = { "BodyText" },
            FontFamily = "DejaVu Sans Mono,Adwaita Mono,Consolas,Monaco,monospace",
            FontWeight = isBold ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(timeText, 1);

        grid.Children.Add(labelText);
        grid.Children.Add(timeText);

        return grid;
    }

    private static string FormatTime(int milliseconds)
    {
        var totalSeconds = milliseconds / 1000.0;
        var minutes = (int)totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return minutes > 0 ? $"{minutes}:{seconds:00.000}" : $"{seconds:0.000}";
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}