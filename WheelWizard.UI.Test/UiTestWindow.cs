using Avalonia.Controls;

namespace WheelWizard.UI.Test;

internal sealed class UiTestWindow : Window, IDisposable
{
    public void Dispose() => Close();
}
