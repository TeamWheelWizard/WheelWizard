using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
        _changingCouchLayout = false;
    }

    private void RebuildCouchPartActions(MiiPartDefinition? definition, Mii mii)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Button;
        var focusedLabel = focused?.Parent == CouchPartActions ? focused.Content as string : null;
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
                    () => TogglePresence(definition.Part)
                );
            if (definition.Variants is not null)
            {
                AddCouchPartAction(t("hover.mii_editor.previous")!, true, () => Cycle(-1));
                AddCouchPartAction(t("hover.mii_editor.next")!, true, () => Cycle(1));
            }
            if (definition.IsPresent(mii))
            {
                AddMovement(definition.DragVertical, t("couch.move_up"), t("couch.move_down"));
                AddMovement(definition.DragHorizontal, t("couch.move_inward"), t("couch.move_outward"));
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
                    () => TogglePresence(part)
                );
        }
        if (focusedLabel is not null)
        {
            var buttons = CouchPartActions.Children.OfType<Button>();
            var target =
                buttons.FirstOrDefault(button => button.IsEnabled && Equals(button.Content, focusedLabel))
                ?? buttons.FirstOrDefault(button => button.IsEnabled);
            target?.Focus(Avalonia.Input.NavigationMethod.Directional);
        }

        void AddMovement(MiiDragAxis? axis, string decrease, string increase)
        {
            if (axis is null)
                return;
            AddCouchPartAction(decrease, axis.Stepper.CanStep(mii, -1), () => StepValue(axis.Stepper, -1));
            AddCouchPartAction(increase, axis.Stepper.CanStep(mii, 1), () => StepValue(axis.Stepper, 1));
        }
    }

    private void AddCouchPartAction(string label, bool enabled, Action action)
    {
        var button = new Button
        {
            Content = label,
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
