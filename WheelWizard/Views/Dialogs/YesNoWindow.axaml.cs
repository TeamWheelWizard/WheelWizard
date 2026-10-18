using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs.Base;

namespace WheelWizard.Views.Dialogs;

public partial class YesNoWindow : ModalContent
{
    public bool Result { get; private set; } = false;

    /// <summary>
    /// Whether the user actually picked "no", as opposed to dismissing the window. Both leave
    /// <see cref="Result"/> false, but a caller that treats "no" as a deliberate choice needs to tell them apart.
    /// </summary>
    public bool NoButtonClicked { get; private set; } = false;

    private TaskCompletionSource<bool>? _tcs;

    public YesNoWindow()
        : base("Wheel Wizard")
    {
        InitializeComponent();
        IsDismissible = true;
        YesButton.Text = t("action.yes");
        NoButton.Text = t("action.no");
    }

    public YesNoWindow SetMainText(string mainText)
    {
        MainTextBlock.Text = mainText;
        return this;
    }

    public YesNoWindow SetExtraText(string extraText)
    {
        ExtraTextBlock.Text = extraText;
        return this;
    }

    public YesNoWindow SetButtonText(string yesText, string noText)
    {
        YesButton.Text = yesText;
        NoButton.Text = noText;

        // It really depends on the text length what looks best
        ButtonContainer.HorizontalAlignment =
            (yesText.Length + noText.Length) > 12 ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        return this;
    }

    public YesNoWindow SetButtonTones(ActionButtonTone yesVariant, ActionButtonTone noVariant)
    {
        YesButton.Tone = yesVariant;
        NoButton.Tone = noVariant;
        return this;
    }

    private void yesButton_Click(object sender, RoutedEventArgs e)
    {
        Result = true;
        _tcs?.TrySetResult(true);
        Close();
    }

    private void noButton_Click(object sender, RoutedEventArgs e)
    {
        NoButtonClicked = true;
        Close();
    }

    protected override void BeforeClose()
    {
        _tcs?.TrySetResult(false);
    }

    public async Task<bool> AwaitAnswer()
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
