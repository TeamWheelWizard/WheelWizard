using Avalonia.Controls;
using WheelWizard.Views.Shell;

namespace WheelWizard.UI.Test;

internal sealed class UiTestHost : BaseWindow, IDisposable
{
    public Border Body { get; } = new();
    private readonly Border _overlay = new();
    protected override Control InteractionContent => Body;
    protected override Control InteractionOverlay => _overlay;

    public UiTestHost()
    {
        Width = 656;
        Height = 876;
        Content = new Grid { Children = { Body, _overlay } };
    }

    public void Dispose() => Close();
}
