using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Serilog;
using WheelWizard.ApplicationLifecycle;
using WheelWizard.Views.Behaviors;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Startup;

namespace WheelWizard.Views;

public class App : Application
{
    private readonly Func<Task<IDesktopStartup>>? _createStartup;

    /// <summary>Loads visual resources for the Avalonia previewer and headless UI tests.</summary>
    public App() { }

    public App(Func<Task<IDesktopStartup>> createStartup) => _createStartup = createStartup;

    public override void Initialize()
    {
        // macOS reads the name before deferred visual resources load for the splash startup.
        Name = "Wheel Wizard";
        if (_createStartup is null)
            LoadVisualResources();
        InstallMacApplicationMenu();
    }

    internal void LoadVisualResources()
    {
        AvaloniaXamlLoader.Load(this);
        ToolTipBubbleBehavior.Initialize();
    }

    private void InstallMacApplicationMenu()
    {
        // Register before Avalonia exports the application menu; later replacement leaves its fallback active.
        if (OperatingSystem.IsMacOS())
        {
            var menu = new NativeMenu();
            var appItems = new List<NativeMenuItem>();
            AddItem("About Wheel Wizard…", window => window.ShowAppInfo());
            AddItem("Settings…", window => window.ShowSettings(), new KeyGesture(Key.OemComma, KeyModifiers.Meta));
            menu.Add(new NativeMenuItemSeparator());
            AddItem("GitHub", window => window.OpenCommunityLink("github"));
            AddItem("Discord", window => window.OpenCommunityLink("discord"));
            AddItem("Support Wheel Wizard", window => window.OpenCommunityLink("support"));
            menu.NeedsUpdate += (_, _) =>
            {
                var enabled =
                    ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: Layout window }
                    && window.IsVisible
                    && window.CompleteContentEnabled;
                foreach (var item in appItems)
                {
                    // Only our items are gated; the system adds Hide, Services and Quit afterward.
                    item.IsEnabled = enabled;
                }
            };
            NativeMenu.SetMenu(this, menu);

            void AddItem(string header, Action<Layout> action, KeyGesture? gesture = null)
            {
                var item = new NativeMenuItem(header) { Gesture = gesture };
                item.Click += (_, _) =>
                {
                    if (
                        ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: Layout window }
                        && window.IsVisible
                        && window.CompleteContentEnabled
                    )
                    {
                        window.Activate();
                        action(window);
                    }
                };
                appItems.Add(item);
                menu.Add(item);
            }
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (_createStartup is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            // Activate from the dispatcher after the native event loop has started.
            desktop.MainWindow = new SplashWindow { ShowActivated = false };
            desktop.MainWindow.Show();
            var shutdown = new CancellationTokenSource();
            var cancellationToken = shutdown.Token;
            desktop.Exit += (_, _) =>
            {
                shutdown.Cancel();
                shutdown.Dispose();
            };
            Dispatcher.UIThread.Post(
                async () =>
                {
                    try
                    {
                        desktop.MainWindow?.Activate();
                        var startup = await _createStartup();
                        cancellationToken.ThrowIfCancellationRequested();
                        await startup.StartAsync(desktop, StartupOptions.Parse(desktop.Args ?? []), cancellationToken);
                    }
                    catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
                    catch (Exception exception)
                    {
                        Log.Error(exception, "Application start failed");
                        desktop.Shutdown(1);
                    }
                },
                DispatcherPriority.Background
            );
        }
        base.OnFrameworkInitializationCompleted();
    }
}
