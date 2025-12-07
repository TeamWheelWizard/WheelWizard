using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using WheelWizard.Resources.Languages;
using WheelWizard.Views.Components;
using WheelWizard.Views.Popups.Base;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups.MiiManagement;

public partial class MiiSelectorWindow : PopupContent
{
    public Mii? Result { get; private set; } = null;
    private TaskCompletionSource<Mii?> _tcs;

    public MiiSelectorWindow()
        : base(true, false, false, Common.PopupTitle_MiiSelector)
    {
        InitializeComponent();
        SaveButton.Text = Common.Action_Save;
        CancelButton.Text = Common.Action_Cancel;
    }

    public MiiSelectorWindow SetButtonText(string saveText, string cancelText)
    {
        SaveButton.Text = saveText;
        CancelButton.Text = cancelText;

        // It really depends on the text length what looks best
        ButtonContainer.HorizontalAlignment =
            (saveText.Length + cancelText.Length) > 12 ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        return this;
    }

    public MiiSelectorWindow SetMiiOptions(List<Mii> miis, int selectedIndex) => SetMiiOptions(miis, miis[selectedIndex]);

    public MiiSelectorWindow SetMiiOptions(List<Mii> miis, Mii? selected)
    {
        MiiList.Children.Clear();
        foreach (var mii in miis.OrderByDescending(m => m.IsFavorite))
        {
            var miiBlock = new MiiBlock
            {
                Mii = mii,
                Width = 90,
                Height = 90,
                Margin = new(8, 10),
                IsChecked = mii.IsTheSameAs(selected),
            };
            miiBlock.Click += ChangeMii_Click;
            MiiList.Children.Add(miiBlock);
        }
        return this;
    }

    private void ChangeMii_Click(object? sender, RoutedEventArgs e)
    {
        var selected = MiiList.Children.OfType<MiiBlock>().FirstOrDefault(block => block.IsChecked == true)?.Mii;
        SaveButton.IsEnabled = selected != null;
    }

    private void yesButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MiiList.Children.OfType<MiiBlock>().FirstOrDefault(block => block.IsChecked == true)?.Mii;
        _tcs.TrySetResult(Result); // Signal that the task is complete
        Close();
    }

    private void noButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void BeforeClose()
    {
        // If you want to return something different, then to the TrySetResult before you close it
        _tcs.TrySetResult(null);
    }

    public async Task<Mii?> AwaitAnswer()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(AwaitAnswer);
        }
        _tcs = new();
        Show(); // Or ShowDialog(parentWindow) if you need it to be modal
        return await _tcs.Task;
    }
}
