using Avalonia.Controls;
using WheelWizard.Views.Shell;

namespace WheelWizard.UI.Test;

internal sealed class UiTestHost : BaseWindow, IDisposable
{
    public Border Body { get; } = new();
    protected override Control InteractionContent => Body;

    public UiTestHost()
    {
        Width = 656;
        Height = 876;
        Content = new Grid { Children = { Body } };
    }

    public void Dispose() => Close();
}
