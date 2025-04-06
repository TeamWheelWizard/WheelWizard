using Avalonia;

namespace WheelWizard;

/// <summary>
/// This interface is used to define the entry point for the Avalonia designer.
/// </summary>
public interface IDesignerEntryPoint
{
    /// <summary>
    /// This method is used by the Avalonia designer to create the application instance.
    /// </summary>
    static abstract AppBuilder BuildAvaloniaApp();
}
