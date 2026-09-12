using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace WheelWizard.Views.Storage;

public interface IStorageProviderAccessor
{
    IStorageProvider? Current { get; }
}

public sealed class DesktopStorageProviderAccessor : IStorageProviderAccessor
{
    // Resolve the current window at the presentation boundary, including after a language/window refresh.
    public IStorageProvider? Current =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;
}
