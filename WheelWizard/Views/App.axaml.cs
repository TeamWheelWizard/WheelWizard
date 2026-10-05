using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using WheelWizard.ApplicationLifecycle;
using WheelWizard.Views.Behaviors;
using WheelWizard.Views.Startup;

namespace WheelWizard.Views;

public class App : Application
{
    private readonly IDesktopStartup? _startup;

    /// <summary>Loads visual resources for the Avalonia previewer and headless UI tests.</summary>
    public App() { }

    public App(IDesktopStartup startup) => _startup = startup;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ToolTipBubbleBehavior.Initialize();
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
        if (_startup is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shutdown = new CancellationTokenSource();
            desktop.Exit += (_, _) =>
            {
                shutdown.Cancel();
                shutdown.Dispose();
            };
            _ = _startup.StartAsync(desktop, StartupOptions.Parse(desktop.Args ?? []), shutdown.Token);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
