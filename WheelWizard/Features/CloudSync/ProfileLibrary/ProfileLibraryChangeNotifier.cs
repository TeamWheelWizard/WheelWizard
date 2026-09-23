namespace WheelWizard.CloudSync.ProfileLibrary;

/// <summary>Signals that WiiCompiled has changed the local license library.</summary>
public static class ProfileLibraryChangeNotifier
{
    public static event EventHandler? Changed;

    public static void NotifyChanged() => Changed?.Invoke(null, EventArgs.Empty);
}
