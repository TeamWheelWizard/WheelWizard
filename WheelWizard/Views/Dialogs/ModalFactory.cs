using WheelWizard.Views.Dialogs.Base;

namespace WheelWizard.Views.Dialogs;

public interface IModalFactory
{
    T Create<T>(params object[] arguments)
        where T : ModalContent;
}

/// <summary>Constructs in-app modal content with its services and explicit caller-supplied arguments.</summary>
public sealed class ModalFactory(IServiceProvider services) : IModalFactory
{
    public T Create<T>(params object[] arguments)
        where T : ModalContent => ActivatorUtilities.CreateInstance<T>(services, arguments);
}
