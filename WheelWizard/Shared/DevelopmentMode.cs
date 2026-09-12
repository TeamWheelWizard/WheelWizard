namespace WheelWizard.Shared;

public static class DevelopmentMode
{
#if DEBUG
    public static bool IsEnabled { get; private set; } = true;

    public static void Hide() => IsEnabled = false;
#else
    public static bool IsEnabled => false;

    public static void Hide() { }
#endif
}
