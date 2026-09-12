using Avalonia.Controls;
using WheelWizard.Shared.DependencyInjection;

namespace WheelWizard.Views;

public abstract class UserControlBase : UserControl
{
    protected UserControlBase()
    {
        if (ServiceInjector.RequiresInjection(GetType()))
            ServiceInjector.InjectServices(App.Services, this);
    }
}
