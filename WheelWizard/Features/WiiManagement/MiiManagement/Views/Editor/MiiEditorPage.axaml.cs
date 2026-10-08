using System.Numerics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Testably.Abstractions;
using WheelWizard.MiiAnimations.Editor;
using WheelWizard.MiiAnimations.Library;
using WheelWizard.MiiImages.Views;
using WheelWizard.MiiRendering.Realtime;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Settings;
using WheelWizard.Shared.Calendar;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;
using Button = Avalonia.Controls.Button;

namespace WheelWizard.WiiManagement.MiiManagement.Views.Editor;

/// <summary>What to edit: an existing Mii, or null to make a new one (starting with the picker).</summary>
public sealed record MiiEditorRequest(Mii? Mii);

/// <summary>
/// The Mii editor, as a page. A new Mii starts with two Miis falling from the sky to pick from; then the picked Mii
/// fills the page and you edit it through a floating sidebar (Head → its groups → their parts, Body, Info) or by
/// clicking the Mii itself: the head opens the head menu, and in the close-up every part lights up under the mouse,
/// can be clicked to select it, and dragged to move it. The selected part cycles with the floating arrows (or the
/// strip of all its variants at the bottom) and has its colour behind the button in the corner.
/// </summary>
public partial class MiiEditorPage : UserControl, INavigationGuard, IFullWidthPage
{
    /// <summary>Leave anyway if the save animation never reaches its "saved" marker.</summary>
    private static readonly TimeSpan SaveAnimationTimeout = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan ColorPanelDuration = TimeSpan.FromMilliseconds(260);
    private const double ColorPanelClosedWidth = 62;
    private const double FloatingGap = 10;

    private readonly INavigationService _navigation;
    private readonly IMiiDbService _miiDb;
    private readonly ISettingsManager _settings;
    private readonly IRandomSystem _random;
    private readonly MiiEditorScene _scene;
    private readonly bool _animate;

    private MiiEditorSession? _session;
    private Mii? _pickBoy;
    private Mii? _pickGirl;
    private bool _picking;
    private bool _saving;

    private Level _level = Level.Overview;
    private Section? _section;
    private MiiHeadGroup? _group;
    private MiiEditPart? _part;

    private Mii? _dragStart;
    private bool _colorOpen;
    private bool _updatingFields;
    private Mii? _preview;

    private (MiiEditPart Part, Size Size, Rect Rect)? _arrowAnchor;
    private bool _arrowAnchorDirty;

    private readonly Button _previousButton;
    private readonly Button _nextButton;
    private readonly Dictionary<MiiEditPart, Button> _presenceButtons = new();

    private enum Level
    {
        Picker,

        /// <summary>The whole Mii: Head / Body / Info.</summary>
        Overview,

        /// <summary>Head close-up with its groups (Head, Hair, Eyes, ...).</summary>
        HeadGroups,

        /// <summary>Head close-up with the parts of one group (e.g. Eyes, Brows, Glasses).</summary>
        HeadParts,
    }

    private enum Section
    {
        Body,
        Info,
    }

    public MiiEditorPage(
        INavigationService navigation,
        IMiiDbService miiDb,
        ISettingsManager settings,
        IMiiNativeRenderer renderer,
        IMiiAnimationLibrary animations,
        ISeasonalCalendar calendar,
        IRandomSystem random,
        MiiEditorRequest request
    )
    {
        _navigation = navigation;
        _miiDb = miiDb;
        _settings = settings;
        _random = random;
        _animate = settings.ENABLE_ANIMATIONS.Get();
        InitializeComponent();

        _scene = new MiiEditorScene(renderer, animations, calendar, random.Random.Shared, _animate);
        StageHost.Children.Add(_scene);
        _scene.Picked += girl => _ = PickAsync(girl);
        _scene.BodyClicked += part =>
        {
            if (part == MiiBodyPart.Head)
                OpenHead();
            else
                OpenSection(Section.Body);
        };
        _scene.PartClicked += SelectPart;
        _scene.PartDragStarted += OnPartDragStarted;
        _scene.PartDragged += OnPartDragged;
        _scene.PartDragEnded += _ =>
        {
            _scene.EndNudge();
            _session?.EndMerge();
            _arrowAnchorDirty = true;
        };
        _scene.FrameDrawn += PositionOverlay;
        _scene.FellBackToImages += () => PositionOverlay();
        _scene.PointerWheelChanged += Scene_OnPointerWheelChanged;

        _previousButton = RoundButton("MiiEditorChevronLeft", "hover.mii_editor.previous", "Arrow", (_, _) => Cycle(-1));
        _nextButton = RoundButton("MiiEditorChevronRight", "hover.mii_editor.next", "Arrow", (_, _) => Cycle(1));
        Overlay.Children.Add(_previousButton);
        Overlay.Children.Add(_nextButton);
        foreach (var part in new[] { MiiEditPart.Glasses, MiiEditPart.Mole, MiiEditPart.Beard, MiiEditPart.Mustache })
        {
            var captured = part;
            var button = RoundButton("PlusMark", null, "Add", (_, _) => TogglePresence(captured));
            _presenceButtons[part] = button;
            Overlay.Children.Add(button);
        }

        // Hidden until a drawn frame says where they go.
        foreach (var child in Overlay.Children)
            Hide(child);

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel);

        if (request.Mii is { } existing && MiiEditorSession.Copy(existing) is { } copy)
        {
            _session = new MiiEditorSession(copy, isNew: false);
            _session.Changed += OnSessionChanged;
            _scene.ShowEditor(copy);
            EnterEditor();
        }
        else
        {
            StartPicker();
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Focus();
    }

    #region Picker

    private void StartPicker()
    {
        _level = Level.Picker;
        _picking = true;
        _pickBoy = MiiFactory.CreateDefaultMale();
        _pickGirl = MiiFactory.CreateDefaultFemale();
        _scene.ShowPicker(_pickBoy, _pickGirl);
        PickerHint.IsVisible = true;
        DiceButton.IsVisible = true;
        SetEditorChromeVisible(false);
    }

    private async void DiceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_picking)
            return;
        _picking = false;
        DiceButton.IsEnabled = false;
        var random = _random.Random.Shared;
        _pickBoy = RandomMii(girl: false);
        _pickGirl = RandomMii(girl: true);
        await _scene.ShuffleAsync(_pickBoy, _pickGirl);
        // Let the new looks sink in before the dice decides.
        await Task.Delay(_animate ? 450 : 0);
        _picking = true;
        await PickAsync(random.Next(2) == 1);
    }

    private Mii RandomMii(bool girl)
    {
        var mii = MiiFactory.CreateRandomMii(_random.Random.Shared);
        mii.IsGirl = girl;
        return mii;
    }

    private async Task PickAsync(bool girl)
    {
        if (!_picking || (girl ? _pickGirl : _pickBoy) is not { } picked)
            return;
        _picking = false;
        DiceButton.IsVisible = false;
        PickerHint.IsVisible = false;

        _session = new MiiEditorSession(picked, isNew: true);
        _session.Changed += OnSessionChanged;
        if (_scene.IsRealtime)
            await _scene.ChooseAsync(girl);
        else
            _scene.ShowEditorImage(picked);
        EnterEditor();
    }

    #endregion

    #region Levels and the floating sidebar

    private void EnterEditor()
    {
        _level = Level.Overview;
        _section = null;
        SetEditorChromeVisible(true);
        LoadFields();
        Refresh();
    }

    private void SetEditorChromeVisible(bool visible)
    {
        foreach (var control in new Control[] { SidebarPanel, TopActions })
        {
            control.Opacity = visible ? 1 : 0;
            control.IsHitTestVisible = visible;
        }
    }

    private void OpenHead()
    {
        _level = Level.HeadGroups;
        _section = null;
        _group = null;
        _part = null;
        _scene.SetFraming(MiiEditorFraming.Head);
        Refresh();
    }

    private void OpenSection(Section section)
    {
        _level = Level.Overview;
        _section = section;
        _group = null;
        _part = null;
        _scene.SetFraming(section == Section.Info ? MiiEditorFraming.Info : MiiEditorFraming.Body);
        Refresh();
    }

    private void OpenGroup(MiiHeadGroup group)
    {
        var parts = MiiEditorParts.PartsOf(group);
        SelectPart(parts[0].Part);
    }

    private void SelectPart(MiiEditPart part)
    {
        var definition = MiiEditorParts.Get(part);
        _group = definition.Group;
        _part = part;
        _section = null;
        _level = MiiEditorParts.PartsOf(definition.Group).Count > 1 ? Level.HeadParts : Level.HeadGroups;
        if (_scene.Framing != MiiEditorFraming.Head)
            _scene.SetFraming(MiiEditorFraming.Head);
        Refresh();
    }

    /// <summary>One level up: a group's parts → the head groups → the whole Mii.</summary>
    private void StepOut()
    {
        switch (_level)
        {
            case Level.HeadParts:
                _level = Level.HeadGroups;
                _part = null;
                _group = null;
                Refresh();
                break;
            case Level.HeadGroups:
                ZoomOut();
                break;
            case Level.Overview when _section is not null:
                _section = null;
                _scene.SetFraming(MiiEditorFraming.Body);
                Refresh();
                break;
        }
    }

    private void ZoomOut()
    {
        _level = Level.Overview;
        _section = null;
        _group = null;
        _part = null;
        _scene.SetFraming(MiiEditorFraming.Body);
        Refresh();
    }

    private void RebuildSidebar()
    {
        SidebarItems.Children.Clear();
        if (_session is null)
            return;
        var mii = _session.Mii;

        switch (_level)
        {
            case Level.Overview:
                SidebarItems.Children.Add(
                    SideButton(MiiEditorParts.Get(MiiEditPart.FaceShape), "attribute.mii_section.head", false, (_, _) => OpenHead())
                );
                SidebarItems.Children.Add(
                    SideGeometryButton("Shirt", "attribute.mii_section.body", _section == Section.Body, (_, _) => OpenSection(Section.Body))
                );
                SidebarItems.Children.Add(
                    SideGeometryButton(
                        "InfoTip",
                        "attribute.mii_section.info",
                        _section == Section.Info,
                        (_, _) => OpenSection(Section.Info)
                    )
                );
                if (_section == Section.Body)
                {
                    SidebarItems.Children.Add(Divider());
                    SidebarItems.Children.Add(
                        SideGeometryButton("PersonMale", "attribute.mii.gender_male", !mii.IsGirl, (_, _) => SetGender(false))
                    );
                    SidebarItems.Children.Add(
                        SideGeometryButton("PersonFemale", "attribute.mii.gender_female", mii.IsGirl, (_, _) => SetGender(true))
                    );
                }

                break;
            case Level.HeadGroups:
                SidebarItems.Children.Add(SideGeometryButton("ArrowLeft", "action.back", false, (_, _) => StepOut()));
                SidebarItems.Children.Add(Divider());
                foreach (var group in MiiEditorParts.Groups)
                {
                    var captured = group;
                    var definition = MiiEditorParts.Get(MiiEditorParts.GroupIconPart(group));
                    SidebarItems.Children.Add(
                        SideButton(definition, MiiEditorParts.GroupTitleKey(group), _group == group, (_, _) => OpenGroup(captured))
                    );
                }

                break;
            case Level.HeadParts:
                SidebarItems.Children.Add(SideGeometryButton("ArrowLeft", "action.back", false, (_, _) => StepOut()));
                SidebarItems.Children.Add(Divider());
                foreach (var definition in MiiEditorParts.PartsOf(_group!.Value))
                {
                    var captured = definition.Part;
                    SidebarItems.Children.Add(
                        SideButton(definition, definition.TitleKey, _part == definition.Part, (_, _) => SelectPart(captured))
                    );
                }

                break;
        }
    }

    private MultiIconRadioButton SideButton(
        MiiPartDefinition definition,
        string titleKey,
        bool isChecked,
        EventHandler<RoutedEventArgs> click
    )
    {
        if (definition.Icon.StartsWith("MiiEditor"))
            return SideGeometryButton(definition.Icon, titleKey, isChecked, click);
        var button = new MultiIconRadioButton
        {
            Classes = { "Side" },
            Focusable = false,
            IconData = (DrawingImage)Application.Current!.FindResource(definition.Icon)!,
            IsChecked = isChecked,
            GroupName = "MiiEditorSidebar" + GetHashCode(),
        };
        definition.StyleIcon?.Invoke(button);
        ToolTip.SetTip(button, t(titleKey));
        ToolTip.SetPlacement(button, PlacementMode.Right);
        button.Click += click;
        return button;
    }

    private MultiIconRadioButton SideGeometryButton(string geometry, string titleKey, bool isChecked, EventHandler<RoutedEventArgs> click)
    {
        var button = new MultiIconRadioButton
        {
            Classes = { "Side" },
            Focusable = false,
            IconGeo = FindGeometry(geometry),
            Color1 = new SolidColorBrush(ViewUtils.Colors.Neutral300),
            HoverColor1 = new SolidColorBrush(ViewUtils.Colors.Neutral50),
            SelectedColor1 = new SolidColorBrush(ViewUtils.Colors.Primary300),
            IsChecked = isChecked,
            GroupName = "MiiEditorSidebar" + GetHashCode(),
            Padding = new Thickness(6),
        };
        ToolTip.SetTip(button, t(titleKey));
        ToolTip.SetPlacement(button, PlacementMode.Right);
        button.Click += click;
        return button;
    }

    private static Border Divider() =>
        new()
        {
            Height = 1,
            Margin = new Thickness(6, 3),
            Background = new SolidColorBrush(ViewUtils.Colors.Neutral700),
        };

    private Geometry FindGeometry(string key) =>
        this.TryFindResource(key, out var resource) && resource is Geometry geometry
            ? geometry
            : (Geometry)Application.Current!.FindResource(key)!;

    #endregion

    #region Refreshing the panels

    /// <summary>Brings every panel in line with the level, selection and Mii.</summary>
    private void Refresh()
    {
        if (_session is null)
            return;
        var mii = _session.Mii;
        RebuildSidebar();

        UndoButton.IsEnabled = _session.CanUndo;
        RedoButton.IsEnabled = _session.CanRedo;

        var definition = _part is { } part ? MiiEditorParts.Get(part) : null;
        RebuildTools(definition, mii);
        RebuildVariants(definition, mii);

        var colors = _section == Section.Body ? MiiEditorParts.FavoriteColors : definition?.Colors;
        SetColors(colors, mii);

        SetShown(BodyPanel, _section == Section.Body);
        SetShown(InfoCard, _section == Section.Info);
        PositionOverlay();

        // The button that had focus may just have been rebuilt away; keep the keyboard shortcuts working.
        Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focused || !this.IsVisualAncestorOf(focused))
                Focus();
        });
    }

    private static void SetShown(Control control, bool shown) => control.Classes.Set("hidden", !shown);

    private void RebuildTools(MiiPartDefinition? definition, Mii mii)
    {
        ToolItems.Children.Clear();
        if (definition is not null && definition.IsPresent(mii))
        {
            if (definition.Size is { } size)
            {
                ToolItems.Children.Add(
                    Tool("MiiEditorBiggerIcon", "hover.mii_editor.bigger", size.CanStep(mii, 1), () => StepValue(size, 1))
                );
                ToolItems.Children.Add(
                    Tool("MiiEditorSmallerIcon", "hover.mii_editor.smaller", size.CanStep(mii, -1), () => StepValue(size, -1))
                );
            }

            if (definition.Rotation is { } rotation)
            {
                // Higher values turn the right side of the part up, which reads as turning left.
                ToolItems.Children.Add(
                    Tool("RotateLeft", "hover.mii_editor.rotate_left", rotation.CanStep(mii, 1), () => StepValue(rotation, 1))
                );
                ToolItems.Children.Add(
                    Tool("RotateRight", "hover.mii_editor.rotate_right", rotation.CanStep(mii, -1), () => StepValue(rotation, -1))
                );
            }

            if (definition.Flip is { } flip && definition.IsFlipped is { } isFlipped)
                ToolItems.Children.Add(Tool("ArrowSwap", "hover.mii_editor.flip", true, () => FlipPart(definition, flip, isFlipped)));
        }

        SetShown(ToolPanel, ToolItems.Children.Count > 0);
    }

    private Button Tool(string geometry, string tipKey, bool enabled, Action click)
    {
        var button = RoundButton(geometry, tipKey, null, (_, _) => click());
        button.IsEnabled = enabled;
        ToolTip.SetPlacement(button, PlacementMode.Left);
        return button;
    }

    private Button RoundButton(string geometry, string? tipKey, string? extraClass, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button
        {
            Classes = { "Round" },
            Focusable = false,
            Content = new PathIcon { Data = FindGeometry(geometry) },
        };
        if (extraClass is not null)
            button.Classes.Add(extraClass);
        if (tipKey is not null)
            ToolTip.SetTip(button, t(tipKey));
        button.Click += click;
        return button;
    }

    private MiiEditPart? _variantsBuiltFor;

    private void RebuildVariants(MiiPartDefinition? definition, Mii mii)
    {
        if (definition?.Variants is not { } variants)
        {
            _variantsBuiltFor = null;
            VariantItems.Children.Clear();
            SetShown(VariantPanel, false);
            return;
        }

        var current = variants.Get(mii);
        if (_variantsBuiltFor != definition.Part)
        {
            _variantsBuiltFor = definition.Part;
            VariantItems.Children.Clear();
            for (var i = variants.First; i < variants.Count; i++)
            {
                var index = i;
                Control content;
                ToggleButton button;
                if (variants.IconPrefix is { } prefix)
                {
                    var icon = new MultiIconRadioButton
                    {
                        Classes = { "Variant" },
                        Focusable = false,
                        IconData = (DrawingImage)Application.Current!.FindResource($"{prefix}{index:00}")!,
                        GroupName = "MiiEditorVariants" + GetHashCode(),
                    };
                    variants.StyleIcon?.Invoke(icon);
                    button = icon;
                }
                else
                {
                    content = new TextBlock
                    {
                        Text = variants.Label?.Invoke(index) ?? index.ToString(),
                        Classes = { "BodyText" },
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    };
                    button = new ToggleButton
                    {
                        Focusable = false,
                        Content = content,
                        Height = 46,
                        Padding = new Thickness(12, 0),
                        Margin = new Thickness(2, 0),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        CornerRadius = new CornerRadius(10),
                    };
                }

                button.Tag = index;
                button.Click += (_, _) => PickVariant(index);
                VariantItems.Children.Add(button);
            }
        }

        foreach (var child in VariantItems.Children.OfType<ToggleButton>())
            child.IsChecked = child.Tag is int index && index == current && definition.IsPresent(mii);
        SetShown(VariantPanel, true);
        // Keep the selected one in view (after layout, so new buttons have their place).
        Dispatcher.UIThread.Post(
            () =>
            {
                if (VariantItems.Children.OfType<ToggleButton>().FirstOrDefault(b => b.IsChecked == true) is { } selected)
                    selected.BringIntoView();
            },
            DispatcherPriority.Background
        );
    }

    #endregion

    #region Colours

    private MiiColors? _colors;

    private void SetColors(MiiColors? colors, Mii mii)
    {
        if (!ReferenceEquals(colors, _colors))
        {
            _colors = colors;
            if (_colorOpen)
                CloseColors(animate: false);
            SwatchItems.Children.Clear();
            if (colors is not null)
            {
                for (var i = 0; i < colors.Swatches.Count; i++)
                {
                    var index = i;
                    var swatch = new Button
                    {
                        Classes = { "Swatch" },
                        Focusable = false,
                        Background = new SolidColorBrush(colors.Swatches[i]),
                        Tag = index,
                    };
                    swatch.Click += (_, _) => PickColor(index);
                    swatch.PointerEntered += (_, _) => PreviewColor(index);
                    swatch.PointerExited += (_, _) => EndPreview();
                    SwatchItems.Children.Add(swatch);
                }
            }
        }

        if (colors is null)
        {
            SetShown(ColorPanel, false);
            return;
        }

        var current = colors.Get(mii);
        ColorDot.Background = new SolidColorBrush(colors.Swatches[Math.Clamp(current, 0, colors.Swatches.Count - 1)]);
        foreach (var swatch in SwatchItems.Children.OfType<Button>())
            swatch.Classes.Set("selected", swatch.Tag is int index && index == current);
        SetShown(ColorPanel, true);
    }

    private void ColorButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_colorOpen)
            CloseColors(animate: true);
        else
            OpenColors();
    }

    /// <summary>The colour button grows to the left over the variants and shows every colour.</summary>
    private void OpenColors()
    {
        if (_colors is null)
            return;
        _colorOpen = true;
        // Closed it's just the round button; open it's a card holding every colour.
        ColorPanel.Classes.Remove("Bare");
        var full = Math.Max(ColorPanelClosedWidth, BottomBar.Bounds.Width);
        AnimateWidth(ColorPanel, ColorPanel.Bounds.Width, full);
        SwatchScroller.Opacity = 1;
        VariantPanel.Opacity = 0;
        VariantPanel.IsHitTestVisible = false;
    }

    private void CloseColors(bool animate)
    {
        _colorOpen = false;
        ColorPanel.Classes.Add("Bare");
        EndPreview();
        if (animate)
            AnimateWidth(ColorPanel, ColorPanel.Bounds.Width, ColorPanelClosedWidth);
        else
            ColorPanel.Width = ColorPanelClosedWidth;
        SwatchScroller.Opacity = 0;
        VariantPanel.ClearValue(OpacityProperty);
        VariantPanel.ClearValue(IsHitTestVisibleProperty);
    }

    private void AnimateWidth(Control control, double from, double to)
    {
        control.Width = to;
        if (!_animate)
            return;
        var animation = new Animation
        {
            Duration = ColorPanelDuration,
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(WidthProperty, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(WidthProperty, to) } },
            },
        };
        _ = animation.RunAsync(control);
    }

    private void PickColor(int index)
    {
        _preview = null;
        var colors = _colors;
        CloseColors(animate: true);
        if (colors is null || _session is null)
            return;
        var reaction = _section == Section.Body ? MiiEditorReaction.FavoriteColor : (MiiEditorReaction?)null;
        if (_session.Change(m => colors.Set(m, index)))
            _scene.Present(_session.Mii, reaction: reaction);
        else
            _scene.Present(_session.Mii);
        Refresh();
    }

    /// <summary>Hovering a colour shows it on the Mii without changing anything yet.</summary>
    private void PreviewColor(int index)
    {
        if (_colors is not { } colors || _session is null || MiiEditorSession.Copy(_session.Mii) is not { } copy)
            return;
        if (!colors.Set(copy, index))
            return;
        _preview = copy;
        _scene.Present(copy);
    }

    private void EndPreview()
    {
        if (_preview is null || _session is null)
            return;
        _preview = null;
        _scene.Present(_session.Mii);
    }

    #endregion

    #region Editing

    private void OnSessionChanged()
    {
        if (_session is null)
            return;
        UndoButton.IsEnabled = _session.CanUndo;
        RedoButton.IsEnabled = _session.CanRedo;
    }

    /// <summary>Applies an edit to the Mii and shows it, optionally sliding the changed part.</summary>
    private bool Edit(Func<Mii, bool> edit, HeadPartChange? change = null, string? mergeKey = null, MiiEditorReaction? reaction = null)
    {
        if (_session is null || !_session.Change(edit, mergeKey))
            return false;
        _scene.Present(_session.Mii, change, reaction);
        Refresh();
        return true;
    }

    private void Cycle(int direction)
    {
        if (_part is not { } part || MiiEditorParts.Get(part) is not { Variants: { } variants } definition || _session is null)
            return;
        var next = variants.Cycle(_session.Mii, direction);
        Edit(m => variants.Set(m, next), new HeadPartChange(definition.RenderPart, direction));
    }

    private void PickVariant(int index)
    {
        if (_part is not { } part || MiiEditorParts.Get(part) is not { Variants: { } variants } definition || _session is null)
            return;
        var current = variants.Get(_session.Mii);
        var direction = definition.IsPresent(_session.Mii) ? Math.Sign(index - current) : 0;
        if (!Edit(m => variants.Set(m, index), new HeadPartChange(definition.RenderPart, direction)))
            Refresh();
    }

    private void StepValue(MiiStepper stepper, int change) => Edit(m => stepper.Step(m, change));

    private void FlipPart(MiiPartDefinition definition, Func<Mii, bool, bool> flip, Func<Mii, bool> isFlipped) =>
        Edit(m => flip(m, !isFlipped(m)), new HeadPartChange(definition.RenderPart, 0));

    private void TogglePresence(MiiEditPart part)
    {
        var definition = MiiEditorParts.Get(part);
        if (definition.Presence is not { } presence || _session is null)
            return;
        var present = presence.IsPresent(_session.Mii);
        if (Edit(m => presence.Set(m, !present), new HeadPartChange(definition.RenderPart, 0)) && !present)
            SelectPart(part);
    }

    private void SetGender(bool girl)
    {
        if (_session is null || _session.Mii.IsGirl == girl)
        {
            Refresh();
            return;
        }

        if (
            !_session.Change(m =>
            {
                m.IsGirl = girl;
                return true;
            })
        )
            return;
        // The new body shows mid-twirl, at the swap_gender marker.
        _scene.Present(
            _session.Mii,
            reaction: girl ? MiiEditorReaction.BecomeGirl : MiiEditorReaction.BecomeBoy,
            showAtCue: MiiEditorCues.SwapGender
        );
        Refresh();
    }

    private void OnPartDragStarted(MiiEditPart part)
    {
        if (_part != part)
            SelectPart(part);
        _session?.EndMerge();
        _dragStart = _session is { } session ? MiiEditorSession.Copy(session.Mii) : null;
        // Freezes the head on screen; the part then moves with the mouse (a head build per step would lag behind).
        _scene.BeginNudge(part);
    }

    private void OnPartDragged(MiiEditPart part, int down, int across)
    {
        if (_dragStart is not { } start)
            return;
        var definition = MiiEditorParts.Get(part);
        if (!definition.IsPresent(start))
            return;
        Edit(
            m =>
            {
                var changed = false;
                if (definition.DragVertical is { } vertical)
                    changed |= vertical.Stepper.SetClamped(m, vertical.Stepper.Get(start) + down);
                if (definition.DragHorizontal is { } horizontal)
                    changed |= horizontal.Stepper.SetClamped(m, horizontal.Stepper.Get(start) + across);
                return changed;
            },
            mergeKey: "drag"
        );
        if (_session is { } session)
        {
            static int Moved(MiiDragAxis? axis, Mii from, Mii to) => axis is null ? 0 : axis.Stepper.Get(to) - axis.Stepper.Get(from);
            _scene.NudgePart(
                part,
                Moved(definition.DragVertical, start, session.Mii),
                Moved(definition.DragHorizontal, start, session.Mii)
            );
        }
    }

    private void UndoButton_OnClick(object? sender, RoutedEventArgs e) => UndoRedo(undo: true);

    private void RedoButton_OnClick(object? sender, RoutedEventArgs e) => UndoRedo(undo: false);

    private void UndoRedo(bool undo)
    {
        if (_session is null || !(undo ? _session.Undo() : _session.Redo()))
            return;
        _scene.Present(_session.Mii);
        _arrowAnchorDirty = true;
        LoadFields();
        Refresh();
    }

    #endregion

    #region Body and info

    /// <summary>Puts the Mii's values in the sliders and text fields (without counting that as an edit).</summary>
    private void LoadFields()
    {
        if (_session is null)
            return;
        var mii = _session.Mii;
        _updatingFields = true;
        HeightSlider.Value = mii.Height.Value;
        WeightSlider.Value = mii.Weight.Value;
        NameField.Text = mii.Name.ToString();
        CreatorField.Text = mii.CreatorName.ToString();
        FavoriteButton.Classes.Set("favorite", mii.IsFavorite);
        _updatingFields = false;
    }

    private void HeightSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingFields)
            return;
        var value = (byte)Math.Clamp((int)HeightSlider.Value, 0, 127);
        Edit(
            m => MiiScale.Create(value) is { IsSuccess: true } scale && (m.Height = scale.Value) is not null,
            mergeKey: "height",
            reaction: MiiEditorReaction.BodyShape
        );
    }

    private void WeightSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingFields)
            return;
        var value = (byte)Math.Clamp((int)WeightSlider.Value, 0, 127);
        Edit(
            m => MiiScale.Create(value) is { IsSuccess: true } scale && (m.Weight = scale.Value) is not null,
            mergeKey: "weight",
            reaction: MiiEditorReaction.BodyShape
        );
    }

    private void NameField_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingFields || _session is null)
            return;
        var name = NameField.Text?.Trim() ?? string.Empty;
        var valid = ValidateName(name);
        NameField.ErrorText = valid.IsFailure ? valid.Error.Message : string.Empty;
        if (valid.IsFailure || name == _session.Mii.Name.ToString())
            return;
        if (_session.Change(m => MiiName.Create(name) is { IsSuccess: true } result && (m.Name = result.Value) is not null, "name"))
            _scene.Director?.React(MiiEditorReaction.Name);
    }

    private void CreatorField_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingFields || _session is null)
            return;
        var name = CreatorField.Text?.Trim() ?? string.Empty;
        var valid = name.Length > 10 ? Fail(t("helper_note.creator_name_less11")) : Ok();
        CreatorField.ErrorText = valid.IsFailure ? valid.Error.Message : string.Empty;
        if (valid.IsFailure || name == _session.Mii.CreatorName.ToString())
            return;
        _session.Change(m => MiiName.Create(name) is { IsSuccess: true } result && (m.CreatorName = result.Value) is not null, "creator");
    }

    private static OperationResult ValidateName(string name) =>
        name.Length is > 10 or < 3 ? Fail(t("helper_note.name_must_between")) : Ok();

    private void FavoriteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_session is null)
            return;
        var favorite = !_session.Mii.IsFavorite;
        _session.Change(m =>
        {
            m.IsFavorite = favorite;
            return true;
        });
        FavoriteButton.Classes.Set("favorite", favorite);
        _scene.Director?.React(favorite ? MiiEditorReaction.Favorite : MiiEditorReaction.Unfavorite);
    }

    #endregion

    #region Buttons floating next to the Mii

    /// <summary>Moves the floating arrows and add/remove buttons next to their parts (every frame).</summary>
    private void PositionOverlay()
    {
        var mii = _session?.Mii;
        var inHead =
            mii is not null
            && _scene.IsRealtime
            && _scene.Framing == MiiEditorFraming.Head
            && _level is Level.HeadGroups or Level.HeadParts;

        // Arrows on both sides of the head, at the height of the selected part.
        var definition = _part is { } part ? MiiEditorParts.Get(part) : null;
        Rect? rect = inHead && definition is { Variants: not null } && definition.IsPresent(mii!) ? ArrowAnchor(definition.Part) : null;
        if (rect is { } r)
        {
            Place(_previousButton, new Point(r.Left - FloatingGap - _previousButton.Width, r.Center.Y - _previousButton.Height / 2));
            Place(_nextButton, new Point(r.Right + FloatingGap, r.Center.Y - _nextButton.Height / 2));
        }
        else
        {
            Hide(_previousButton);
            Hide(_nextButton);
        }

        // Add / remove buttons for the optional parts of the open group.
        foreach (var (optional, button) in _presenceButtons)
        {
            var optionalDefinition = MiiEditorParts.Get(optional);
            if (!inHead || optionalDefinition.Group != _group)
            {
                Hide(button);
                continue;
            }

            var present = optionalDefinition.IsPresent(mii!);
            button.Classes.Set("Add", !present);
            button.Classes.Set("Remove", present);
            if (button.Content is PathIcon icon)
                icon.Data = FindGeometry(present ? "MinMark" : "PlusMark");
            ToolTip.SetTip(
                button,
                t(present ? "hover.mii_editor.remove" : "hover.mii_editor.add", new { part = t(optionalDefinition.TitleKey) })
            );
            if (PresenceAnchor(optional, present) is { } anchor)
                Place(button, new Point(anchor.X - button.Width / 2, anchor.Y - button.Height / 2));
            else
                Hide(button);
        }
    }

    /// <summary>
    /// The row the arrows sit on: as wide as the face, at the height of the part. It's measured once when the part
    /// is selected and then stays put, so the arrows don't follow the breathing or jump when the next variant is a
    /// different size (or, like bald hair, has nothing to measure). Only camera moves, resizes and moving the part
    /// itself (dragging, undo) measure it again.
    /// </summary>
    private Rect? ArrowAnchor(MiiEditPart part)
    {
        var size = Overlay.Bounds.Size;
        if (!_arrowAnchorDirty && !_scene.IsCameraMoving && _arrowAnchor is { } kept && kept.Part == part && kept.Size == size)
            return kept.Rect;
        if (_scene.FaceRect() is not { } face)
            return _arrowAnchor is { } old && old.Part == part ? old.Rect : null;

        var y = part switch
        {
            MiiEditPart.Hair => face.Top + face.Height * 0.08,
            MiiEditPart.FaceShape or MiiEditPart.FaceFeature => face.Center.Y,
            MiiEditPart.Beard => face.Bottom - face.Height * 0.15,
            MiiEditPart.Glasses => (_scene.PartRect(part) ?? _scene.PartRect(MiiEditPart.Eyes))?.Center.Y,
            _ => _scene.PartRect(part)?.Center.Y,
        };
        // A part that's just been added may not be drawn yet: try again next frame.
        if (y is not { } row)
            return _arrowAnchor is { } old && old.Part == part ? old.Rect : null;

        var rect = new Rect(face.Left, row, face.Width, 0);
        _arrowAnchor = (part, size, rect);
        _arrowAnchorDirty = false;
        return rect;
    }

    /// <summary>Where the add/remove button of an optional part goes: on the part, or where it would appear.</summary>
    private Point? PresenceAnchor(MiiEditPart part, bool present)
    {
        if (present && _scene.PartRect(part) is { } own)
            return part switch
            {
                MiiEditPart.Beard => new Point(own.Right + 4, own.Bottom - 6),
                MiiEditPart.Mole => new Point(own.Right + 14, own.Top - 10),
                _ => new Point(own.Right + 6, own.Top - 6),
            };

        return part switch
        {
            MiiEditPart.Glasses => _scene.PartRect(MiiEditPart.Eyes) is { } eyes ? new Point(eyes.Right + 8, eyes.Top - 12) : null,
            MiiEditPart.Mole => _scene.FacePoint(new Vector2(0.72f, 0.62f)),
            MiiEditPart.Beard => _scene.FaceRect() is { } face ? new Point(face.Center.X, face.Bottom - face.Height * 0.06) : null,
            MiiEditPart.Mustache => _scene.PartRect(MiiEditPart.Mouth) is { } mouth ? new Point(mouth.Center.X, mouth.Top - 14) : null,
            _ => null,
        };
    }

    private void Place(Control control, Point position)
    {
        var width = double.IsNaN(control.Width) ? 38 : control.Width;
        var height = double.IsNaN(control.Height) ? 38 : control.Height;
        var x = Math.Clamp(position.X, 0, Math.Max(0, Overlay.Bounds.Width - width));
        var y = Math.Clamp(position.Y, 0, Math.Max(0, Overlay.Bounds.Height - height));
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        control.Opacity = 1;
        control.IsHitTestVisible = true;
    }

    private static void Hide(Control control)
    {
        control.Opacity = 0;
        control.IsHitTestVisible = false;
    }

    #endregion

    #region Keyboard and mouse shortcuts

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_level == Level.Picker || _session is null)
            return;
        // Typing a name: let the text box have its keys.
        if (e.Source is TextBox)
            return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        switch (e.Key)
        {
            case Key.Z when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
            case Key.Y when ctrl:
                UndoRedo(undo: false);
                e.Handled = true;
                break;
            case Key.Z when ctrl:
                UndoRedo(undo: true);
                e.Handled = true;
                break;
            case Key.Left when _part is not null:
                Cycle(-1);
                e.Handled = true;
                break;
            case Key.Right when _part is not null:
                Cycle(1);
                e.Handled = true;
                break;
            case Key.Escape:
                if (_colorOpen)
                    CloseColors(animate: true);
                else
                    StepOut();
                e.Handled = true;
                break;
        }
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsXButton1Pressed && _level is not Level.Picker)
        {
            StepOut();
            e.Handled = true;
            return;
        }

        // Clicking anywhere outside the open colour panel closes it.
        if (_colorOpen && e.Source is Visual source && !ColorPanel.IsVisualAncestorOf(source))
            CloseColors(animate: true);
        // Keep the keyboard shortcuts working after clicking around, but don't pull focus out of the text boxes.
        if ((e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is null)
            Focus();
    }

    /// <summary>Scrolling out of the head close-up goes back to the whole Mii.</summary>
    private void Scene_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y < 0 && _level is Level.HeadGroups or Level.HeadParts)
        {
            ZoomOut();
            e.Handled = true;
        }
    }

    #endregion

    #region Saving and leaving

    public bool HasUnsavedWork => !_saving && _session?.HasChanges == true;

    public async Task<bool> ConfirmLeaveAsync() =>
        await new YesNoWindow()
            .SetMainText(t("question.leave_mii_editor.title"))
            .SetExtraText(t("question.leave_mii_editor.extra"))
            .SetButtonText(t("action.leave"), t("action.keep_editing"))
            .SetButtonVariants(ButtonVariant.Danger, ButtonVariant.Default)
            .AwaitAnswer();

    private void BackButton_OnClick(object? sender, RoutedEventArgs e) => _navigation.NavigateTo<MiiListPage>();

    private async void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_session is null || _saving)
            return;
        var name = _session.Mii.Name.ToString().Trim();
        if (ValidateName(name).IsFailure)
        {
            OpenSection(Section.Info);
            NameField.ErrorText = t("helper_note.name_must_between");
            NameField.Focus();
            return;
        }

        _saving = true;
        IsHitTestVisible = false;
        await PlaySaveAnimationAsync();

        var result = _session.IsNew
            ? _miiDb.AddToDatabase(_session.Mii, _settings.Get<string>(_settings.MACADDRESS))
            : _miiDb.Update(_session.Mii);
        if (result.IsFailure)
        {
            _saving = false;
            IsHitTestVisible = true;
            var key = _session.IsNew ? "snackbar_error.mii_failure_create" : "snackbar_error.mii_failure_update";
            ViewUtils.ShowSnackbar(t(key, new { error = result.Error.Message })!, ViewUtils.SnackbarType.Danger);
            return;
        }

        _navigation.NavigateAway(typeof(MiiListPage));
    }

    /// <summary>The Mii takes a bow (back in the whole view) before the page closes.</summary>
    private async Task PlaySaveAnimationAsync()
    {
        CloseColors(animate: false);
        if (_level != Level.Overview || _section is not null)
            ZoomOut();
        if (!_animate || _scene.Director is not { } director || !director.React(MiiEditorReaction.Save))
            return;

        var done = new TaskCompletionSource();
        void OnCue(MiiEditorReaction reaction, string cue)
        {
            if (reaction == MiiEditorReaction.Save && cue == MiiEditorCues.Saved)
                done.TrySetResult();
        }

        void OnEnded(MiiEditorReaction reaction)
        {
            if (reaction == MiiEditorReaction.Save)
                done.TrySetResult();
        }

        director.Cue += OnCue;
        director.ReactionEnded += OnEnded;
        await Task.WhenAny(done.Task, Task.Delay(SaveAnimationTimeout));
        director.Cue -= OnCue;
        director.ReactionEnded -= OnEnded;
    }

    #endregion
}
