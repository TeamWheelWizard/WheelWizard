using Avalonia.Controls;

namespace WheelWizard.Views.Shell.Couch;

/// <summary>Page-owned focus entry and inner Back behavior; shell navigation remains the fallback.</summary>
public interface ICouchNavigationPage
{
    Control? CouchFocusTarget { get; }
    bool TryGoBack();
}
