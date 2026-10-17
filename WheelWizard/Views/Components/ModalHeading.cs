using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using WheelWizard.Views.Dialogs.Base;

namespace WheelWizard.Views.Components;

/// <summary>The dialog's own title and explicit close/cancel action, with no window chrome.</summary>
public sealed class ModalHeading : ContentControl
{
    public ModalHeading()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Template = new FuncControlTemplate<ModalHeading>(
            (heading, _) =>
            {
                var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
                var title = new ContentPresenter
                {
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                title.Bind(ContentPresenter.ContentProperty, new Binding(nameof(Content)) { Source = heading });
                row.Children.Add(title);
                var close = new ActionButton
                {
                    Name = "PART_CloseButton",
                    Text = "",
                    IconData = heading.FindResource("XMark") as Geometry,
                    Tone = ActionButtonTone.Secondary,
                    Variant = ActionButtonVariant.Ghost,
                    Size = ButtonSize.Compact,
                    Width = 26,
                    Height = 26,
                    MinHeight = 0,
                    Padding = new Thickness(5),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                };
                AutomationProperties.SetName(close, "Close");
                close.AttachedToVisualTree += (_, _) =>
                {
                    var modal = heading.FindAncestorOfType<ModalContent>();
                    if (modal == null)
                        return;
                    close.Bind(
                        IsVisibleProperty,
                        new MultiBinding
                        {
                            Priority = BindingPriority.Style,
                            Converter = BoolConverters.Or,
                            Bindings =
                            {
                                new Binding(nameof(ModalContent.IsDismissible)) { Source = modal },
                                new Binding(nameof(ModalContent.IsCancelable)) { Source = modal },
                            },
                        }
                    );
                };
                close.Click += (_, _) => heading.FindAncestorOfType<ModalContent>()?.RequestHeaderClose();
                Grid.SetColumn(close, 1);
                row.Children.Add(close);
                return row;
            }
        );
    }
}
