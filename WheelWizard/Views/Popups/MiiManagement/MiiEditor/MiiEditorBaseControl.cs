using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using WheelWizard.Views.Components;

namespace WheelWizard.Views.Popups.MiiManagement.MiiEditor;

public class MiiEditorBaseControl : UserControlBase
{
    protected MiiEditorWindow Editor { get; init; }

    protected MiiEditorBaseControl(MiiEditorWindow editor) => Editor = editor;

    protected void BackButton_OnClick(object? sender, EventArgs e)
    {
        BeforeBack();
        Editor.SetEditorPage(typeof(EditorStartPage));
    }

    protected void RefreshImage() => Editor.RefreshImage();

    protected virtual void BeforeBack() { }

    protected DrawingImage GetMiiIconData(string name)
    {
        // If exception is being thrown here, it means the resource is not found, either name is invalid,
        // OR you e.g. forgot to add the list of icons to the App.xaml.
        return (DrawingImage)Application.Current!.FindResource(name)!;
    }

    protected void SetButtons(string type, int count, UniformGrid addTo, Action<int, MultiIconRadioButton> modify)
    {
        for (var i = 0; i < count; i++)
        {
            var index = i;

            var indexStr = index < 10 ? $"0{i}" : index.ToString();
            var iconName = $"{type}{indexStr}";
            var button = new MultiIconRadioButton() { Margin = new(6), IconData = GetMiiIconData(iconName) };
            modify(index, button);
            addTo.Children.Add(button);
        }
    }

    protected void SetColorButtons(int count, UniformGrid addTo, Action<int, MultiIconRadioButton> modify)
    {
        var color2 = new SolidColorBrush(ViewUtils.Colors.Neutral950);
        var selectedColor2 = new SolidColorBrush(ViewUtils.Colors.Neutral900);

        for (var i = 0; i < count; i++)
        {
            var index = i;

            var iconData = GetMiiIconData("Multi-PaintBrush");
            var button = new MultiIconRadioButton
            {
                Margin = new(6),
                IconData = iconData,
                Color2 = color2,
                SelectedColor2 = selectedColor2,
            };
            modify(index, button);
            addTo.Children.Add(button);
        }
    }

    // Enum to identify which property is being changed by buttons
    protected enum MiiTransformProperty
    {
        Vertical,
        Horizontal,
        Size,
        Spacing,
        Rotation,
    }
}
