using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WheelWizard.Services.LiveData;
using WheelWizard.Utilities.RepeatedTasks;

namespace WheelWizard.Views;

public static class ViewUtils
{
    public enum SnackbarType
    {
        Success,
        Warning,
        Danger,
    }

    public static void OpenLink(string link)
    {
        Process.Start(new ProcessStartInfo { FileName = link, UseShellExecute = true });
        ShowSnackbar("Opened link");
    }

    public static void ShowSnackbar(string message, SnackbarType type = SnackbarType.Success) => GetLayout().ShowSnackbar(message, type);

    public static Layout GetLayout() => Layout.Instance;

    public static void RefreshWindow()
    {
        // Refresh window  opens in the start page again, that is nessesairy
        // we would prefer opening up where we left off, however, that does not work since the translations
        // are still in the context of the layout before, and so the dropdowns will break

        var oldWindow = GetLayout();
        // Creating a new one will also set re-assign `Layout.Instance` right away, and this `GetLayout()`
        Layout newWindow = new();
        newWindow.Position = oldWindow.Position;
        if (oldWindow is IRepeatedTaskListener oldListener)
        {
            // Unsubscribing is not really necessary. But i guess it prevents memory leaks when
            // someone is refreshing the window a lot (happens when changing the language e.g.
            // So they would have to change the language like 1000 of times in a row)
            WhWzStatusManager.Instance.Unsubscribe(oldListener);
            RRLiveRooms.Instance.Unsubscribe(oldListener);
        }

        newWindow.Show();
        oldWindow.Close();
        newWindow.UpdatePlayerAndRoomCount(RRLiveRooms.Instance);
    }

    public static T? FindParent<T>(object? child, int maxSearchDepth = 10)
    {
        StyledElement? currentParent = null;
        if (child is StyledElement childElement)
            currentParent = childElement.Parent;
        if (currentParent == null)
            return default;

        var currentDepth = 1;
        while (currentDepth < maxSearchDepth)
        {
            if (currentParent is T parentElement)
                return parentElement;
            if (currentParent?.Parent != null)
                currentParent = currentParent.Parent;
            currentDepth++;
        }

        return default;
    }

    #region Colors

    public static class Colors
    {
        public static Color Warning50 = GetColor("Warning50");
        public static Color Warning100 = GetColor("Warning100");
        public static Color Warning200 = GetColor("Warning200");
        public static Color Warning300 = GetColor("Warning300");
        public static Color Warning400 = GetColor("Warning400");
        public static Color Warning500 = GetColor("Warning500");
        public static Color Warning600 = GetColor("Warning600");
        public static Color Warning700 = GetColor("Warning700");
        public static Color Warning800 = GetColor("Warning800");
        public static Color Warning900 = GetColor("Warning900");
        public static Color Warning950 = GetColor("Warning950");

        public static Color Danger50 = GetColor("Danger50");
        public static Color Danger100 = GetColor("Danger100");
        public static Color Danger200 = GetColor("Danger200");
        public static Color Danger300 = GetColor("Danger300");
        public static Color Danger400 = GetColor("Danger400");
        public static Color Danger500 = GetColor("Danger500");
        public static Color Danger600 = GetColor("Danger600");
        public static Color Danger700 = GetColor("Danger700");
        public static Color Danger800 = GetColor("Danger800");
        public static Color Danger900 = GetColor("Danger900");
        public static Color Danger950 = GetColor("Danger950");

        public static Color Primary50 = GetColor("Primary50");
        public static Color Primary100 = GetColor("Primary100");
        public static Color Primary200 = GetColor("Primary200");
        public static Color Primary300 = GetColor("Primary300");
        public static Color Primary400 = GetColor("Primary400");
        public static Color Primary500 = GetColor("Primary500");
        public static Color Primary600 = GetColor("Primary600");
        public static Color Primary700 = GetColor("Primary700");
        public static Color Primary800 = GetColor("Primary800");
        public static Color Primary900 = GetColor("Primary900");
        public static Color Primary950 = GetColor("Primary950");

        public static Color Neutral50 = GetColor("Neutral50");
        public static Color Neutral100 = GetColor("Neutral100");
        public static Color Neutral200 = GetColor("Neutral200");
        public static Color Neutral300 = GetColor("Neutral300");
        public static Color Neutral400 = GetColor("Neutral400");
        public static Color Neutral500 = GetColor("Neutral500");
        public static Color Neutral600 = GetColor("Neutral600");
        public static Color Neutral700 = GetColor("Neutral700");
        public static Color Neutral800 = GetColor("Neutral800");
        public static Color Neutral900 = GetColor("Neutral900");
        public static Color Neutral950 = GetColor("Neutral950");

        public static Color Black = GetColor("Black");

        private static Color GetColor(string name) => (Color)Application.Current!.FindResource(name)!;
    }

    #endregion
}
