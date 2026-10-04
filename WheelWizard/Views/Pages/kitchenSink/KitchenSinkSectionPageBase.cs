using Avalonia.Controls;

namespace WheelWizard.Views.Pages.KitchenSink;

public interface IKitchenSinkSection
{
    string SectionName { get; }
    string? SectionTooltip { get; }
}

public abstract class KitchenSinkSectionPageBase : UserControl, IKitchenSinkSection
{
    public abstract string SectionName { get; }
    public virtual string? SectionTooltip => null;
}
