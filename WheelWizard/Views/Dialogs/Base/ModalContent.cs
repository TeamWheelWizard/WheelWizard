using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using WheelWizard.Views.Shell;

namespace WheelWizard.Views.Dialogs.Base;

public abstract class ModalContent : UserControl
{
    public static readonly StyledProperty<string> ModalTitleProperty = AvaloniaProperty.Register<ModalContent, string>(
        nameof(ModalTitle),
        ""
    );
    public static readonly StyledProperty<bool> IsDismissibleProperty = AvaloniaProperty.Register<ModalContent, bool>(
        nameof(IsDismissible)
    );

    public static readonly StyledProperty<bool> IsCancelableProperty = AvaloniaProperty.Register<ModalContent, bool>(nameof(IsCancelable));

    private ModalHost? _host;
    private BaseWindow? _owner;
    private bool _disableOpening;
    private TaskCompletionSource? _completion;
    private double? _preferredContentWidth;
    private double? _preferredMinHeight;

    public string ModalTitle
    {
        get => GetValue(ModalTitleProperty);
        set => SetValue(ModalTitleProperty, value);
    }

    /// <summary>Allows backdrop, Escape and close-icon dismissal, independently of explicit cancellation.</summary>
    public bool IsDismissible
    {
        get => GetValue(IsDismissibleProperty);
        set => SetValue(IsDismissibleProperty, value);
    }

    /// <summary>Enables explicit cancellation through the header X, independently of easy dismissal.</summary>
    public bool IsCancelable
    {
        get => GetValue(IsCancelableProperty);
        set => SetValue(IsCancelableProperty, value);
    }

    public Action? CancelAction { get; set; }

    /// <summary>Runs the same outcome as the modal's explicit Cancel/Close action. Defaults to Close.</summary>
    public Action? DismissAction { get; set; }

    protected ModalContent(string title = "") => ModalTitle = title;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Content is not Control root)
            return base.MeasureOverride(availableSize);
        if (_preferredContentWidth == null && double.IsFinite(root.Width))
            _preferredContentWidth = root.Width;
        if (_preferredContentWidth != null)
        {
            // Preserve the preferred width as data arrives, but allow small windows to constrain it.
            root.Width = Math.Min(availableSize.Width, _preferredContentWidth.Value);
        }
        _preferredMinHeight ??= root.MinHeight;
        root.MinHeight = Math.Min(availableSize.Height, _preferredMinHeight.Value);
        return base.MeasureOverride(availableSize);
    }

    protected virtual void BeforeClose() { }

    protected virtual void BeforeOpen() { }

    /// <summary>Choose the originating window explicitly for work that continues asynchronously.</summary>
    public ModalContent SetModalOwner(Control origin)
    {
        _owner =
            TopLevel.GetTopLevel(origin) as BaseWindow
            ?? throw new InvalidOperationException("The modal origin must be attached to an application window.");
        return this;
    }

    public void Show()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disableOpening)
        {
            BeforeClose();
            return;
        }
        if (_host != null)
        {
            Focus();
            return;
        }
        _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _host = (_owner ?? BaseWindow.GetModalOwner()).Modals;
        try
        {
            _host.Show(this);
            BeforeOpen();
        }
        catch
        {
            CloseImmediately();
            throw;
        }
    }

    public Task ShowDialog()
    {
        Show();
        return _completion?.Task ?? Task.CompletedTask;
    }

    public void Close() => CloseCore(animate: true);

    internal void CloseImmediately() => CloseCore(animate: false);

    private void CloseCore(bool animate)
    {
        Dispatcher.UIThread.VerifyAccess();
        var host = _host;
        if (host == null)
            return;
        host.Close(
            this,
            () =>
            {
                _host = null;
                CompleteClose();
            },
            animate
        );
    }

    private void CompleteClose()
    {
        try
        {
            BeforeClose();
        }
        finally
        {
            _completion?.TrySetResult();
        }
    }

    internal void Dismiss()
    {
        if (IsDismissible && _host?.TopModal == this)
            (DismissAction ?? Close)();
    }

    internal void RequestHeaderClose()
    {
        if (_host?.TopModal != this)
            return;
        if (IsCancelable)
            CancelAction?.Invoke();
        else
            Dismiss();
    }

    public void Focus() => _host?.FocusTopModal();

    protected void DisableOpen(bool value)
    {
        _disableOpening = value;
        if (value)
            Close();
    }
}
