using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace WheelWizard.Views;

public abstract class BaseWindow : Window
{
    private List<WindowLayer> _windowLayers = [];

    private int _disableCount = 0;
    private WindowLayer? _currentLayer;

    protected abstract Control InteractionOverlay { get; } // Just the visual part of the interaction
    protected abstract Control InteractionContent { get; } // The content that will be disabled when the overlay is shown

    protected bool AllowParentInteraction = false;
    protected virtual bool CanUserClose => true;

    private void InstallMacWindowMenu()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var actions = new NativeMenu();
        var minimize = new NativeMenuItem("Minimize") { Gesture = new KeyGesture(Key.M, KeyModifiers.Meta) };
        minimize.Click += (_, _) =>
        {
            if (CanMinimize && InteractionContent.IsEnabled)
                WindowState = WindowState.Minimized;
        };
        var positions = new List<NativeMenuItem>();
        AddPosition("Center", 0.5, 0.5);
        AddPosition("Move to Left Side of Screen", 0, 0.5);
        AddPosition("Move to Right Side of Screen", 1, 0.5);
        AddPosition("Move to Top of Screen", 0.5, 0);
        AddPosition("Move to Bottom of Screen", 0.5, 1);

        void AddPosition(string title, double horizontal, double vertical)
        {
            var item = new NativeMenuItem(title);
            item.Click += (_, _) => MoveWithinScreen(horizontal, vertical);
            positions.Add(item);
        }

        var close = new NativeMenuItem("Close") { Gesture = new KeyGesture(Key.W, KeyModifiers.Meta) };
        close.Click += (_, _) =>
        {
            if (CanUserClose && InteractionContent.IsEnabled)
                Close();
        };
        actions.Add(minimize);
        actions.Add(new NativeMenuItemSeparator());
        foreach (var position in positions)
            actions.Add(position);
        actions.Add(new NativeMenuItemSeparator());
        actions.Add(close);
        actions.NeedsUpdate += (_, _) =>
        {
            minimize.IsEnabled = CanMinimize && InteractionContent.IsEnabled;
            foreach (var position in positions)
                position.IsEnabled = WindowState == WindowState.Normal && InteractionContent.IsEnabled;
            close.IsEnabled = CanUserClose && InteractionContent.IsEnabled;
        };
        var menu = new NativeMenu();
        menu.Add(new NativeMenuItem("Window") { Menu = actions });
        NativeMenu.SetMenu(this, menu);
    }

    private void MoveWithinScreen(double horizontal, double vertical)
    {
        if (!InteractionContent.IsEnabled || WindowState != WindowState.Normal)
            return;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;
        var area = screen.WorkingArea;
        var remainingWidth = Math.Max(0, area.Width - (int)Math.Round(Bounds.Width * RenderScaling));
        var remainingHeight = Math.Max(0, area.Height - (int)Math.Round(Bounds.Height * RenderScaling));
        Position = new PixelPoint(
            area.X + (int)Math.Round(remainingWidth * horizontal),
            area.Y + (int)Math.Round(remainingHeight * vertical));
    }

    protected override void OnOpened(EventArgs e)
    {
        if (Owner is BaseWindow owner)
            _windowLayers = owner._windowLayers;
        AddLayer();
        InstallMacWindowMenu();
        base.OnOpened(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        RemoveLayer();
        base.OnClosed(e);
    }

    private void AddLayer()
    {
        if (_currentLayer is not null)
            return;
        if (!AllowParentInteraction || _windowLayers.Count == 0)
        {
            _currentLayer = new(this);
            if (_windowLayers.Count != 0)
                _windowLayers.Last().SetInteractable(false);
            _windowLayers.Add(_currentLayer);

            return;
        }

        _windowLayers.Last().SubsequentWindows.Add(this);
        _currentLayer = _windowLayers.Last();
    }

    private void RemoveLayer()
    {
        var layer = _currentLayer;
        _currentLayer = null;
        if (layer?.Owner == this)
        {
            _windowLayers.Remove(layer);

            foreach (var bw in layer.SubsequentWindows.ToArray())
            {
                bw.Close();
            }

            if (_windowLayers.Count != 0)
                _windowLayers.Last().SetInteractable(true);
            return;
        }

        layer?.SubsequentWindows.Remove(this);
    }

    public void SetInteractable(bool value)
    {
        if (!value)
            _disableCount++;
        else if (_disableCount > 0)
            _disableCount--;

        if (_disableCount != 0 && value)
            return;

        InteractionOverlay.IsVisible = !value;
        InteractionContent.IsEnabled = value;
    }

    protected class WindowLayer(BaseWindow owner)
    {
        public BaseWindow Owner { get; set; } = owner;
        public readonly List<BaseWindow> SubsequentWindows = [];

        public void SetInteractable(bool active)
        {
            Owner.SetInteractable(active);
            foreach (var subsequentWindow in SubsequentWindows)
            {
                subsequentWindow.SetInteractable(active);
            }
        }
    }
}
