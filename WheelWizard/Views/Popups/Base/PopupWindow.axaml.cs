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
    public Vector InternalSize { get; set; }

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

    private bool _canClose;

    public bool CanClose
    {
        get => _canClose;
        set
        {
            _canClose = value;
            OnPropertyChanged(nameof(CanClose));
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

    // Most (if not all) of these parameters should be set in the popup you create, and not kept as a parameter for that popup
    public PopupWindow(bool allowClose, bool allowParentInteraction, bool isTopMost, string title = "", Vector? size = null)
    {
        size ??= new(400, 200);
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

        SetWindowSize(size.Value);
        Position = mainWindow.Position;
        Loaded += PopupWindow_Loaded;
    }

    public void SetWindowSize(Vector size)
    {
        InternalSize = size;
        var scaleFactor = (double)SettingsManager.WINDOW_SCALE.Get();
        Width = size.X * scaleFactor;
        Height = size.Y * scaleFactor;
        CompleteGrid.RenderTransform = new ScaleTransform(scaleFactor, scaleFactor);
        var marginXCorrection = ((scaleFactor * size.X) - size.X) / 2f;
        var marginYCorrection = ((scaleFactor * size.Y) - size.Y) / 2f;
        CompleteGrid.Margin = new(marginXCorrection, marginYCorrection);
    }

    private void PopupWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        BeforeOpen();
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

    #region PropertyChanged

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    public void Restore() => WindowState = WindowState.Normal;
}
