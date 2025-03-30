using Avalonia.Controls;

namespace WheelWizard.Views;

public abstract class BaseWindow : Window
{
    private static readonly List<WindowLayer> WindowLayers = [];
    
    private int _disableCount;
    private WindowLayer? _currentLayer;
    
    protected abstract Control InteractionOverlay { get; } // Just the visual part of the interaction
    protected abstract Control InteractionContent { get; } // The content that will be disabled when the overlay is shown
    
    protected bool _allowParentInteraction = false;

    protected void AddLayer()
    {
        if (!_allowParentInteraction || WindowLayers.Count == 0)
        {
            _currentLayer = new(this);
            if(WindowLayers.Count != 0)
                WindowLayers.Last().SetInteractable(false);
            WindowLayers.Add(_currentLayer);
          
            return;
        }
        
        WindowLayers.First().SubsequentWindows.Add(this);
        _currentLayer = WindowLayers.Last();
    }
    
    protected void RemoveLayer()
    {
        if (_currentLayer?.Owner == this)
        {
            WindowLayers.Remove(_currentLayer);

            foreach (var bw in _currentLayer.SubsequentWindows)
            {
                bw.Close();
            }
            if(WindowLayers.Count != 0)
                WindowLayers.Last().SetInteractable(true);
            return;
        }
        
        _currentLayer?.SubsequentWindows.Remove(this);
    }
    
    public void SetInteractable(bool value)
    {
        if (!value)
            _disableCount++;
        else if (_disableCount > 0)
            _disableCount--;
        
        if (_disableCount != 0 && value) return;
        
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
