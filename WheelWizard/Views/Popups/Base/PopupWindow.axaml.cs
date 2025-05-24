using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using WheelWizard.Services.Settings;

namespace WheelWizard.Views.Popups.Base;

public partial class PopupWindow : BaseWindow, INotifyPropertyChanged
{
    protected override Control InteractionOverlay => DisabledDarkenEffect;
    protected override Control InteractionContent => CompleteGrid;

    private bool _disableOpening = false;

    public PopupWindow()
    {
        // Constructor is never used, however, UI elements must have a constructor with no params
        InitializeComponent();
        DataContext = this;
        Loaded += PopupWindow_Loaded;
    }

    private bool _isTopMost = true;

    public bool IsTopMost
    {
        get => _isTopMost;
        set
        {
            _isTopMost = value;
            Topmost = value;
            OnPropertyChanged(nameof(IsTopMost));
        }
    }

    private bool _canClose = false;

    public bool CanClose
    {
        get => _canClose;
        set
        {
            _canClose = value;
            OnPropertyChanged(nameof(CanClose));
        }
    }

    private bool _betaFlag;

    public bool BetaFlag
    {
        get => _betaFlag;
        set
        {
            _betaFlag = value;
            OnPropertyChanged(nameof(BetaFlag));
        }
    }

    private string _windowTitle = "Wheel Wizard Popup";

    public string WindowTitle
    {
        get => _windowTitle;
        set
        {
            _windowTitle = value;
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public Action BeforeOpen { get; set; } = () => { };
    public Action BeforeClose { get; set; } = () => { };

    public void DisableOpen(bool value)
    {
        _disableOpening = value;
        if (IsLoaded && _disableOpening)
            Close();
    }

    // Most (if not all) of these parameters should be set in the popup you create, and not kept as a parameter for that popup
    public PopupWindow(bool allowClose, bool allowParentInteraction, bool isTopMost, string title = "")
    {
        IsTopMost = isTopMost;
        CanClose = allowClose;
        WindowTitle = title;
        AllowParentInteraction = allowParentInteraction;
        var mainWindow = ViewUtils.GetLayout();
        if (mainWindow.IsVisible)
            Owner = mainWindow;

        InitializeComponent();
        AddLayer();
        DataContext = this;

        Position = mainWindow.Position;
        Loaded += PopupWindow_Loaded;
    }

    private void PopupWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_disableOpening)
        {
            Close();
            return;
        }

        BeforeOpen();
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);

        CompleteBorder.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var desiredSize = CompleteBorder.DesiredSize;
        SetWindowSize(desiredSize);
    }

    public void SetWindowSize(Size size)
    {
        var scaleFactor = (double)SettingsManager.WINDOW_SCALE.Get();
        Width = size.Width * scaleFactor;
        Height = size.Height * scaleFactor;
        CompleteGrid.RenderTransform = new ScaleTransform(scaleFactor, scaleFactor);
        var marginXCorrection = ((scaleFactor * size.Width) - size.Width) / 2f;
        var marginYCorrection = ((scaleFactor * size.Height) - size.Height) / 2f;
        CompleteGrid.Margin = new(marginXCorrection, marginYCorrection);
    }

    protected void TopBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        BeforeClose();
        RemoveLayer();

        base.OnClosed(e);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    public void Restore() => WindowState = WindowState.Normal;

    #region PropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
