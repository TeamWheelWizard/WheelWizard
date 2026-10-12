using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WheelWizard.Views.Components;

// Pointer-following decoration; render transforms never participate in layout.
public class HoverGlow : Border
{
    private readonly TranslateTransform _position = new();
    private Control? _owner;

    public HoverGlow()
    {
        IsHitTestVisible = false;
        RenderTransform = _position;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _owner = TemplatedParent as Control;
        if (_owner is null)
            return;
        _owner.PointerMoved += UpdatePosition;
        _owner.PointerEntered += UpdatePosition;
    }

    private void UpdatePosition(object? sender, PointerEventArgs e)
    {
        if (this.GetVisualParent() is not { } parent)
            return;
        var position = e.GetPosition(parent);
        _position.X = position.X - Width / 2;
        _position.Y = position.Y - Height / 2;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_owner is not null)
        {
            _owner.PointerMoved -= UpdatePosition;
            _owner.PointerEntered -= UpdatePosition;
            _owner = null;
        }
        base.OnDetachedFromVisualTree(e);
    }
}
