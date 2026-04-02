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
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                Margin = new Avalonia.Thickness(0, 0, 0, 4)
            };
            
            var lapLabel = new TextBlock
            {
                Text = $"Lap {i + 1}:",
                Classes = { "BodyText" },
                Margin = new Avalonia.Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(lapLabel, 0);
            
            var lapTime = new TextBlock
            {
                Text = actualLaps[i],
                Classes = { "BodyText" },
                FontFamily = "DejaVu Sans Mono,Adwaita Mono,Consolas,Monaco,monospace",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Avalonia.Thickness(10, 0, 0, 0)
            };
            Grid.SetColumn(lapTime, 1);
            
            grid.Children.Add(lapLabel);
            grid.Children.Add(lapTime);
            LapTimesContainer.Children.Add(grid);
        }
        
        var avgGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Margin = new Avalonia.Thickness(0, 0, 0, 4)
        };
        
        var avgLabel = new TextBlock
        {
            Text = "Average Lap:",
            Classes = { "BodyText" },
            Margin = new Avalonia.Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(avgLabel, 0);
        
        var averageMs = submission.LapSplitsMs.Take(expectedLaps).Average();
        var avgTime = new TextBlock
        {
            Text = FormatTime((int)averageMs),
            Classes = { "BodyText" },
            FontFamily = "DejaVu Sans Mono,Adwaita Mono,Consolas,Monaco,monospace",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(avgTime, 1);
        
        avgGrid.Children.Add(avgLabel);
        avgGrid.Children.Add(avgTime);
        LapTimesContainer.Children.Add(avgGrid);
        
        var fastestGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Margin = new Avalonia.Thickness(0, 0, 0, 0)
        };
        
        var fastestLabel = new TextBlock
        {
            Text = "Fastest Lap:",
            Classes = { "BodyText" },
            Margin = new Avalonia.Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(fastestLabel, 0);
        
        var fastestTime = new TextBlock
        {
            Text = submission.FastestLapDisplay,
            Classes = { "BodyText" },
            FontFamily = "DejaVu Sans Mono,Adwaita Mono,Consolas,Monaco,monospace",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(fastestTime, 1);
        
        fastestGrid.Children.Add(fastestLabel);
        fastestGrid.Children.Add(fastestTime);
        LapTimesContainer.Children.Add(fastestGrid);
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