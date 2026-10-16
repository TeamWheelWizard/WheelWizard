using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using WheelWizard.Views.Components;
using WheelWizard.Views.Shell.Couch;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement.MiiManagement.Views.Editor;

public partial class MiiEditorPage
{
    private bool _couchLayout;
    private bool _changingCouchLayout;
    private int[]? _desktopPanelIndices;
    private Control[]? _movablePanels;

    public Control? CouchFocusTarget
    {
        get
        {
            if (!_couchLayout || _saving)
                return null;
            if (_level == Level.Picker)
                return CouchPickerActions.Children.OfType<Button>().FirstOrDefault(IsCouchAction);
            if (_colorOpen)
                return SwatchItems.SelectedItem is SegmentOption selected && IsCouchAction(selected)
                    ? selected
                    : SwatchItems.Items.OfType<SegmentOption>().FirstOrDefault(IsCouchAction);
            return SidebarItems.Children.OfType<ToggleButton>().FirstOrDefault(button => button.IsChecked == true && IsCouchAction(button))
                ?? SidebarItems
                    .Children.OfType<Button>()
                    .FirstOrDefault(button => !Equals(button.Tag, "action.back") && IsCouchAction(button));
        }
    }

    public bool TryGoBack()
    {
        if (_saving)
            return false;
        if (_colorOpen)
        {
            CloseColors(animate: true);
            return true;
        }
        if (_level is not (Level.HeadParts or Level.HeadGroups) && (_level != Level.Overview || _section is null))
            return false;
        StepOut();
        return true;
    }

    private bool IsCouchAction(Control control) =>
        control is Button or TextBox or Slider or SegmentOption
        && control.Focusable
        && control.IsEffectivelyVisible
        && control.IsEffectivelyEnabled
        && this.IsVisualAncestorOf(control)
        && !control
            .GetVisualAncestors()
            .OfType<Control>()
            .Any(ancestor => ancestor.Opacity == 0 || !ancestor.IsHitTestVisible || ancestor.Classes.Contains("hidden"));

    private (Control Container, object? Key, int Index)? CaptureCouchFocus()
    {
        if (!_couchLayout || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Control focused)
            return null;
        if (focused is SegmentOption option && option.FindAncestorOfType<SegmentedControl>() is { } selection)
            return (selection, focused.Tag, selection.Items.IndexOf(focused));
        if (focused.Parent is not Panel panel || (panel != SidebarItems && panel != ToolItems && panel != CouchPartActions))
            return null;
        return (panel, focused.Tag, panel.Children.IndexOf(focused));
    }

    private void RestoreCouchFocus((Control Container, object? Key, int Index)? previous)
    {
        if (previous is not { } position)
            return;
        var count = position.Container is Panel panel ? panel.Children.Count : ((SegmentedControl)position.Container).Items.Count;
        Control? target = null;
        for (var index = 0; index < count && target is null; index++)
        {
            var child = ChildAt(index);
            if (child is not null && Equals(child.Tag, position.Key) && IsCouchAction(child))
                target = child;
        }
        if (target is null && position.Container == SidebarItems)
            target = CouchFocusTarget;
        if (target is null)
        {
            // At a bound or after removing an optional part, choose the closest enabled sibling.
            var origin = Math.Clamp(position.Index, 0, Math.Max(0, count - 1));
            for (var distance = 0; distance < count && target is null; distance++)
            {
                var after = origin + distance;
                var before = origin - distance;
                if (after < count && ChildAt(after) is { } next && IsCouchAction(next))
                    target = next;
                else if (before >= 0 && ChildAt(before) is { } prior && IsCouchAction(prior))
                    target = prior;
            }
        }
        Control? ChildAt(int index) =>
            position.Container is Panel owner ? owner.Children[index] : ((SegmentedControl)position.Container).Items[index] as Control;
        target?.Focus(NavigationMethod.Directional);
    }

    private void RecoverCouchFocus()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Control focused && IsCouchAction(focused))
            return;
        CouchFocusTarget?.Focus(NavigationMethod.Directional);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CouchMode.IsEnabledProperty && _scene is not null)
            ApplyCouchLayout();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyCouchLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // A presentation change detaches this page, not its editing session or its owned scene callbacks.
        _scene.EndNudge();
        _dragStart = null;
        _session?.EndMerge();
        EndPreview();
        base.OnDetachedFromVisualTree(e);
    }

    private void ApplyCouchLayout()
    {
        var couch = CouchMode.GetIsEnabled(this);
        if (_changingCouchLayout || couch == _couchLayout)
            return;
        _changingCouchLayout = true;
        _couchLayout = couch;
        Classes.Set("couch", couch);
        Focusable = !couch;

        var panels = _movablePanels ??= [SidebarPanel, ToolPanel, BodyPanel, InfoCard, BottomBar];
        if (couch)
        {
            _desktopPanelIndices ??= new int[panels.Length];
            for (var i = 0; i < panels.Length; i++)
                _desktopPanelIndices[i] = Root.Children.IndexOf(panels[i]);
            Root.Children.Remove(StageHost);
            CouchStage.Children.Add(StageHost);
            foreach (var panel in panels)
            {
                Root.Children.Remove(panel);
                CouchPanels.Children.Add(panel);
            }
        }
        else
        {
            CouchStage.Children.Remove(StageHost);
            Root.Children.Insert(0, StageHost);
            for (var i = 0; i < panels.Length; i++)
            {
                CouchPanels.Children.Remove(panels[i]);
                Root.Children.Insert(_desktopPanelIndices![i], panels[i]);
            }
        }

        CouchLayout.IsVisible = couch;
        EditorHeader.IsVisible = !couch;
        EditorHeaderDivider.IsVisible = !couch;
        EditorBottomDivider.IsVisible = !couch;
        BackButton.IsVisible = !couch;
        Overlay.IsVisible = !couch;
        PickerHint.IsVisible = !couch && _picking;
        DiceButton.IsVisible = !couch && _picking;
        CouchPickerActions.IsVisible = couch && _level == Level.Picker;
        CouchEditorActions.IsVisible = _level != Level.Picker;
        SidebarPanel.IsVisible = !couch || _level != Level.Picker;
        BottomBar.IsVisible = !couch || _level != Level.Picker;

        for (var i = 0; i < 4; i++)
        {
            var panel = panels[i];
            panel.Margin = couch ? default : new Thickness(0, 40, 0, 0);
            panel.HorizontalAlignment =
                couch ? HorizontalAlignment.Stretch
                : panel == SidebarPanel ? HorizontalAlignment.Left
                : HorizontalAlignment.Right;
            panel.VerticalAlignment = couch ? VerticalAlignment.Top : VerticalAlignment.Center;
        }
        InfoCard.Width = couch ? double.NaN : 236;
        SidebarItems.Orientation = couch ? Orientation.Horizontal : Orientation.Vertical;
        SidebarScroller.HorizontalScrollBarVisibility = couch
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        SidebarScroller.VerticalScrollBarVisibility = couch
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        ToolItems.Orientation = Orientation.Vertical;
        VariantButton.Focusable = couch;
        // Rebuild only presentation controls; Mii, history, selection and field contents remain untouched.
        foreach (var variant in VariantItems.Items.OfType<SegmentOption>())
            variant.Focusable = couch;
        foreach (var swatch in SwatchItems.Items.OfType<SegmentOption>())
            swatch.Focusable = couch;
        Refresh();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (couch)
                RecoverCouchFocus();
        });
        _changingCouchLayout = false;
    }

    private void RebuildCouchPartActions(MiiPartDefinition? definition, Mii mii)
    {
        CouchPartActions.Children.Clear();
        if (!_couchLayout)
            return;
        if (definition is not null)
        {
            CouchPartActions.Children.Add(
                new TextBlock
                {
                    Text = t(definition.TitleKey),
                    Margin = new Thickness(6, 12),
                    VerticalAlignment = VerticalAlignment.Center,
                }
            );
            if (definition.Presence is not null)
                AddCouchPartAction(
                    t(
                        definition.IsPresent(mii) ? "hover.mii_editor.remove" : "hover.mii_editor.add",
                        new { part = t(definition.TitleKey) }
                    )!,
                    true,
                    () => TogglePresence(definition.Part),
                    definition.Part
                );
            if (definition.Variants is not null)
            {
                AddCouchPartAction(t("hover.mii_editor.previous")!, true, () => Cycle(-1));
                AddCouchPartAction(t("hover.mii_editor.next")!, true, () => Cycle(1));
            }
            if (definition.IsPresent(mii))
            {
                AddMovement(definition.DragVertical, "vertical", t("couch.move_up"), t("couch.move_down"));
                AddMovement(definition.DragHorizontal, "horizontal", t("couch.move_inward"), t("couch.move_outward"));
            }
        }
        // Optional parts can also be added without a realtime part hit target.
        foreach (var part in _presenceButtons.Keys)
        {
            var optional = MiiEditorParts.Get(part);
            if (optional.Group == _group && optional.Part != definition?.Part)
                AddCouchPartAction(
                    t(optional.IsPresent(mii) ? "hover.mii_editor.remove" : "hover.mii_editor.add", new { part = t(optional.TitleKey) })!,
                    true,
                    () => TogglePresence(part),
                    part
                );
        }

        void AddMovement(MiiDragAxis? axis, string key, string decrease, string increase)
        {
            if (axis is null)
                return;
            AddCouchPartAction(decrease, axis.Stepper.CanStep(mii, -1), () => StepValue(axis.Stepper, -1), key + "-decrease");
            AddCouchPartAction(increase, axis.Stepper.CanStep(mii, 1), () => StepValue(axis.Stepper, 1), key + "-increase");
        }
    }

    private void AddCouchPartAction(string label, bool enabled, Action action, object? key = null)
    {
        var button = new ActionButton
        {
            Text = label,
            Tag = key ?? label,
            Tone = ActionButtonTone.Secondary,
            Variant = ActionButtonVariant.Ghost,
            IsEnabled = enabled,
            MinHeight = 44,
            Margin = new Thickness(3),
        };
        button.Click += (_, _) => action();
        CouchPartActions.Children.Add(button);
    }

    private async void PickBoy_OnClick(object? sender, RoutedEventArgs e) => await PickAsync(false);

    private async void PickGirl_OnClick(object? sender, RoutedEventArgs e) => await PickAsync(true);
}
