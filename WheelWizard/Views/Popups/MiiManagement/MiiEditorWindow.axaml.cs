using System.ComponentModel;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.Views.Popups.Base;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.Views.Popups.MiiManagement.MiiEditor;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups.MiiManagement;

public partial class MiiEditorWindow : PopupContent, INotifyPropertyChanged
{
    // whether you want to save the Mii
    public bool Result { get; private set; } = false;
    private TaskCompletionSource<bool> _tcs;

    private Mii _mii;
    public Mii Mii
    {
        get => _mii;
        private set
        {
            if (_mii != value)
            {
                _mii = value;
                OnPropertyChanged(nameof(Mii));
            }
        }
    }

    private VisualizationType selectedVisualization = VisualizationType.Face;

    public MiiEditorWindow()
        : base(true, false, false, "Mii Editor")
    {
        InitializeComponent();
        DataContext = this;

        Window.BetaFlag = true;
    }

    protected override void BeforeOpen()
    {
        base.BeforeOpen();
        SetEditorPage(typeof(EditorStartPage));
    }

    public void SetEditorPage(Type pageType)
    {
        EditorPresenter.Content = Activator.CreateInstance(pageType, this)!;
        Window.WindowTitle = $"Mii Editor - {Mii.Name}";
    }

    public MiiEditorWindow SetMii(Mii miiToEdit)
    {
        Window.WindowTitle = $"Mii Editor - {miiToEdit.Name}";
        var miiResult = miiToEdit.Clone();
        if (miiResult.IsFailure)
        {
            DisableOpen(true);
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Cant open Mii Editor")
                .SetInfoText(miiResult.Error.Message)
                .Show();
            return this;
        }

        Mii = miiResult.Value;
        return this;
    }

    public void SignalSaveMii()
    {
        Result = true;
        _tcs.TrySetResult(true);
        Close();
    }

    protected override void BeforeClose()
    {
        // If you want to return something different, then to the TrySetResult before you close it
        _tcs.TrySetResult(false);
    }

    public async Task<bool> AwaitAnswer()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => AwaitAnswer());
        }
        _tcs = new();
        Show(); // Or ShowDialog(parentWindow) if you need it to be modal
        return await _tcs.Task;
    }

    public void RefreshImage()
    {
        if (selectedVisualization == VisualizationType.Carousel)
            MiiCarousel.Mii = Mii;
        else if (selectedVisualization == VisualizationType.Face)
            MiiFaceImage.Mii = Mii;
    }

    #region PropertyChanged

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion


    private void SetVisualization(VisualizationType type)
    {
        if (!IsInitialized)
            return;
        VisualizationFace.IsVisible = type == VisualizationType.Face;
        VisualizationCarousel.IsVisible = type == VisualizationType.Carousel;
        selectedVisualization = type;
        RefreshImage();
    }

    private void MiiFaceToggle_OnChecked(object? sender, RoutedEventArgs e) => SetVisualization(VisualizationType.Face);

    private void MiiCarouselToggle_OnChecked(object? sender, RoutedEventArgs e) => SetVisualization(VisualizationType.Carousel);

    private enum VisualizationType
    {
        Face,
        Carousel,
    }
}
