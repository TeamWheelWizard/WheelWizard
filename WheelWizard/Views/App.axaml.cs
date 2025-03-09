using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using WheelWizard.Services.Installation.AutoUpdater;
using WheelWizard.Services.UrlProtocol;

namespace WheelWizard.Views;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AutoUpdater.CheckForUpdatesAsync();
            desktop.MainWindow = new Layout();
        }

        base.OnFrameworkInitializationCompleted();
    }

 

}
