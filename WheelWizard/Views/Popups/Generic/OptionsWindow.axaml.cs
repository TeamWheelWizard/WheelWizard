using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using WheelWizard.Views.Components;
using WheelWizard.Views.Popups.Base;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Popups.Generic;

public partial class OptionsWindow : PopupContent
{
    public string? Result { get; private set; } = null;
    private TaskCompletionSource<string?> _tcs;

    public OptionsWindow()
        : base(true, false, true, "Wheel Wizard")
    {
        InitializeComponent();
    }

    public OptionsWindow SetWindowTitle(string title)
    {
        Window.WindowTitle = title;
        return this;
    }

    public OptionsWindow AddOption(Geometry icon, string title, Action onClick, bool enabled = true)
    {
        var button = new OptionButton()
        {
            IconData = icon,
            Text = title,
            IsEnabled = enabled,
        };
        button.Click += (_, _) =>
        {
            onClick.Invoke();
            Result = title;
            _tcs.TrySetResult(title);
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
        OptionList.Columns = childCount;
        if (childCount <= 4)
            return;

        // with 5, 6 or 9 items, a column width of 3 just looks better
        OptionList.Columns = childCount is 5 or 6 or 9 ? 3 : 4;
    }

    protected override void BeforeClose()
    {
        // If you want to return something different, then to the TrySetResult before you close it
        _tcs.TrySetResult(null);
    }

    public async Task<string?> AwaitAnswer()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => AwaitAnswer());
        }
        _tcs = new();
        Show(); // Or ShowDialog(parentWindow) if you need it to be modal
        return await _tcs.Task;
    }
}
