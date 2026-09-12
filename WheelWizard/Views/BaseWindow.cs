using Avalonia.Controls;

namespace WheelWizard.Views;

public abstract class BaseWindow : Window
{
    private List<WindowLayer> _windowLayers = [];

    private int _disableCount = 0;
    private WindowLayer? _currentLayer;

    protected abstract Control InteractionOverlay { get; } // Just the visual part of the interaction
    protected abstract Control InteractionContent { get; } // The content that will be disabled when the overlay is shown

    protected bool AllowParentInteraction = false;

    protected override void OnOpened(EventArgs e)
    {
        if (Owner is BaseWindow owner)
            _windowLayers = owner._windowLayers;
        AddLayer();
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
