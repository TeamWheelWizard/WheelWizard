using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs.Base;

namespace WheelWizard.Views.Dialogs;

public partial class OptionsWindow : ModalContent
{
    public string? Result { get; private set; } = null;
    private TaskCompletionSource<string?>? _tcs;

    public OptionsWindow()
        : base("Choose an option")
    {
        InitializeComponent();
        IsDismissible = true;
    }

    public OptionsWindow SetWindowTitle(string title)
    {
        ModalTitle = title;
        return this;
    }

    public OptionsWindow AddOption(Geometry icon, string title, Action onClick, bool enabled = true)
    {
        var button = new ActionButton()
        {
            Tone = ActionButtonTone.Secondary,
            Width = 150,
            MinHeight = 48,
            Margin = new Thickness(6),
            IconData = icon,
            Text = title,
            IsEnabled = enabled,
        };
        button.Click += (_, _) =>
        {
            onClick.Invoke();
            Result = title;
            _tcs?.TrySetResult(title);
            Close();
        };

        OptionList.Children.Add(button);

        OptimizeColumns();
        return this;
    }

    public OptionsWindow AddOption(string iconName, string title, Action onClick, bool enabled = true)
    {
        return AddOption((Geometry)Application.Current!.FindResource(iconName)!, title, onClick, enabled);
    }

    private void OptimizeColumns()
    {
        var childCount = OptionList.Children.Count;
        // Keep the existing preferred arrangement while allowing narrower hosts to wrap.
        var columns =
            childCount <= 4 ? childCount
            : childCount is 5 or 6 or 9 ? 3
            : 4;
        OptionList.MaxWidth = Math.Max(1, columns) * 162;
    }

    protected override void BeforeClose()
    {
        _tcs?.TrySetResult(null);
    }

    public async Task<string?> AwaitAnswer()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => AwaitAnswer());
        }
        _tcs = new();
        Show();
        return await _tcs.Task;
    }
}
