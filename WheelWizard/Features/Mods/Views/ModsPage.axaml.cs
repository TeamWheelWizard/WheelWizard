using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using WheelWizard.Features.Patches;
using WheelWizard.Models.Mods;
using WheelWizard.Mods;
using WheelWizard.Mods.ViewModels;
using WheelWizard.Mods.Views.Dialogs;
using WheelWizard.Settings;
using WheelWizard.Shared.Desktop.Storage;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Navigation;

namespace WheelWizard.Mods.Views;

public partial class ModsPage : UserControl, INotifyPropertyChanged
{
    // #todo: move list state and mod workflows into a view model, and split drag-and-drop visuals into their own behavior.
    private IModalFactory Popups { get; }
    private readonly INavigationService _navigation;

    private Func<int, ModPreviewViewModel> CreatePreview { get; }
    private readonly Dictionary<int, ModPreviewViewModel> _previews = [];

    private ModPreviewViewModel GetPreview(int id)
    {
        if (!_previews.TryGetValue(id, out var preview))
            _previews[id] = preview = CreatePreview(id);
        return preview;
    }

    private IModOperationPresentation ModPresentation { get; }

    private IFilePickerService FilePicker { get; }

    private ISettingsManager SettingsService { get; }

    private IModPatchConversionService ModPatchConversionService { get; }

    private IModManager ModManagerService { get; }

    public IModManager ModManager => ModManagerService;

    public ObservableCollection<ModListItem> Mods { get; } = [];

    public string StoragePageTitle => t("page_title.patches");

    private bool _hasMods;

    public bool HasMods
    {
        get => _hasMods;
        set
        {
            if (_hasMods == value)
                return;

            _hasMods = value;
            OnPropertyChanged(nameof(HasMods));
        }
    }

    // Drag-and-drop state
    private bool _isDragPending;
    private bool _isDragging;
    private Point _dragStartPoint;
    private ModListItem? _draggedItem;
    private int _dragStartIndex;
    private int _previewIndex;
    private Control? _draggedContainer;
    private Point _dragOffset;
    private Border? _dragAdorner;
    private IPointer? _capturedPointer;
    private bool _viewInitialized;
    private const double DragThreshold = 5.0;

    public ModsPage(
        IModalFactory popups,
        INavigationService navigation,
        Func<int, ModPreviewViewModel> createPreview,
        IModOperationPresentation modPresentation,
        IFilePickerService filePicker,
        ISettingsManager settingsService,
        IModPatchConversionService modPatchConversionService,
        IModManager modManagerService
    )
    {
        Popups = popups;
        _navigation = navigation;
        CreatePreview = createPreview;
        ModPresentation = modPresentation;
        FilePicker = filePicker;
        SettingsService = settingsService;
        ModPatchConversionService = modPatchConversionService;
        ModManagerService = modManagerService;
        InitializeComponent();
        GridViewOption.IconData = GridViewOption.IconData!.GetWidenedGeometry(
            new Pen(Brushes.White, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
        );
        ListViewOption.IconData = ListViewOption.IconData!.GetWidenedGeometry(
            new Pen(Brushes.White, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
        );
        DataContext = this;
        Focusable = true;
        ModManager.PropertyChanged += OnModsChanged;
        OnModsChanged();
        _ = ReloadModsAndShowErrorsAsync();
        SetModsViewVariant();
        _viewInitialized = true;

        // Apply priority edits as soon as the user clicks anywhere outside the textbox.
        AddHandler(PointerPressedEvent, OnPagePointerPressed, RoutingStrategies.Tunnel, true);

        // Wire up drag-and-drop pointer tracking
        PointerMoved += OnDragPointerMoved;
        PointerReleased += OnDragPointerReleased;
        PointerCaptureLost += OnDragPointerCaptureLost;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ModManager.PropertyChanged -= OnModsChanged;
        ModManager.PropertyChanged += OnModsChanged;
        OnModsChanged(refreshPatches: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelDrag();
        ModManager.PropertyChanged -= OnModsChanged;
        foreach (var preview in _previews.Values)
            preview.Dispose();
        _previews.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnModsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModManager.Mods))
            OnModsChanged();
        else if (e.PropertyName == nameof(Mod.IsEnabled))
            UpdateEnableAllCheckboxState();
    }

    private void OnModsChanged(bool refreshPatches = false)
    {
        if (_isDragging)
            return; // Keep the preview order until the drag ends.

        ListItemCount.Text = ModManager.Mods.Count.ToString();
        HasMods = ModManager.Mods.Count > 0;
        UpdateModLayout(() =>
        {
            var lowest = ModManager.GetLowestActivePriority();
            var highest = ModManager.GetHighestActivePriority();
            for (var i = Mods.Count - 1; i >= 0; i--)
                if (!ModManager.Mods.Contains(Mods[i].Mod))
                    Mods.RemoveAt(i);
            for (var i = 0; i < ModManager.Mods.Count; i++)
            {
                var mod = ModManager.Mods[i];
                var item = Mods.FirstOrDefault(item => ReferenceEquals(item.Mod, mod));
                var preview = GetPreview(mod.ModID);
                var hasPatches = item != null && !refreshPatches ? item.HasPatches : ModPatchConversionService.HasPatches(mod);
                if (item == null)
                    Mods.Insert(i, new(mod, mod.Priority == lowest, mod.Priority == highest, preview, hasPatches));
                else
                {
                    var index = Mods.IndexOf(item);
                    if (index != i)
                        Mods.Move(index, i);
                    if (
                        item.IsLowest != (mod.Priority == lowest)
                        || item.IsHighest != (mod.Priority == highest)
                        || item.Preview != preview
                        || item.HasPatches != hasPatches
                    )
                        Mods[i] = new(mod, mod.Priority == lowest, mod.Priority == highest, preview, hasPatches);
                }
            }
        });
        UpdateEnableAllCheckboxState();
    }

    private void UpdateModLayout(Action update)
    {
        var duration = this.FindResource(WindowAppearance.ControlAnimationDurationResourceKey) is TimeSpan value ? value : TimeSpan.Zero;
        var before = new Dictionary<Mod, Point>();
        foreach (var control in GetDragItems())
        {
            if (
                duration > TimeSpan.Zero
                && control.DataContext is ModListItem item
                && control.TranslatePoint(default, this) is { } position
            )
                before[item.Mod] = position;
            control.RenderTransform = null;
        }
        update();
        if (TopLevel.GetTopLevel(this) == null)
            return;
        UpdateLayout();
        foreach (var control in GetDragItems())
        {
            if (
                control.DataContext is not ModListItem { IsPlaceholder: false } item
                || !before.TryGetValue(item.Mod, out var oldPosition)
                || control.TranslatePoint(default, this) is not { } position
            )
                continue;
            var delta = oldPosition - position;
            if (Math.Abs(delta.X) < 0.5 && Math.Abs(delta.Y) < 0.5)
                continue;
            var offset = new TranslateTransform(delta.X, delta.Y);
            control.RenderTransform = offset;
            offset.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = TranslateTransform.XProperty,
                    Duration = duration,
                    Easing = new CubicEaseOut(),
                },
                new DoubleTransition
                {
                    Property = TranslateTransform.YProperty,
                    Duration = duration,
                    Easing = new CubicEaseOut(),
                },
            };
            offset.X = 0;
            offset.Y = 0;
        }
    }

    private void UpdateEnableAllCheckboxState()
    {
        EnableAllCheckbox.IsChecked = !ModManager.Mods.Select(mod => mod.IsEnabled).Contains(false);
    }

    private void BrowseMod_Click(object sender, RoutedEventArgs e)
    {
        _navigation.NavigateTo<ModBrowserPage>();
    }

    private async Task ReloadModsAndShowErrorsAsync()
    {
        var reloadResult = await ModManager.ReloadAsync();
        if (reloadResult.IsFailure)
            MessageTranslationHelper.ShowMessage(reloadResult.Error);
    }

    private async void ImportMod_Click(object sender, RoutedEventArgs e)
    {
        var selectedFiles = await FilePicker.OpenFilePickerAsync(
            FilePickerFilters.All,
            allowMultiple: true,
            title: t("file_picker.select_mod_file")
        );
        if (selectedFiles.Count == 0)
            return;

        var modName = await new TextInputWindow()
            .SetMainText(t("question.enter_mod_name.title"))
            .SetPlaceholderText(t("placeholder.enter_mod_name"))
            .SetValidation(ModManager.ValidateModName)
            .ShowDialog();
        if (string.IsNullOrWhiteSpace(modName))
            return;

        var importResult = await ModPresentation.RunAsync(
            (progress, _) => ModManager.ImportModFilesAsync(selectedFiles.ToArray(), modName, progress)
        );
        if (importResult.IsFailure)
        {
            MessageTranslationHelper.ShowMessage(importResult.Error);
            return;
        }

        new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Message)
            .SetTitleText(t("message_success.mod_installed.title"))
            .SetInfoText(t("message_success.mod_installed.extra", new { modName = modName })!)
            .Show();
    }

    private async void RenameMod_Click(object sender, RoutedEventArgs e)
    {
        var selectedMod = GetContextModListItem(sender);
        if (selectedMod == null)
            return;

        var oldTitle = selectedMod.Mod.Title;
        var newTitle = await new TextInputWindow()
            .SetMainText(t("question.enter_mod_name.title"))
            .SetInitialText(oldTitle)
            .SetRequireChange()
            .SetExtraText(t("question.enter_new_name.extra", new { name = oldTitle })!)
            .SetPlaceholderText(t("placeholder.enter_mod_name"))
            .SetValidation(ModManager.ValidateRenameModName)
            .ShowDialog();

        if (newTitle == null || oldTitle == newTitle)
            return;

        var renameResult = await ModManager.RenameModAsync(selectedMod.Mod, newTitle);
        if (renameResult.IsFailure)
            MessageTranslationHelper.ShowMessage(renameResult.Error);
    }

    private async void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        var selectedMod = GetContextModListItem(sender);
        if (selectedMod == null)
            return;

        var areTheySure = await new YesNoWindow()
            .SetMainText(t("question.sure_delete.title", new { name = selectedMod.Mod.Title })!)
            .AwaitAnswer();
        if (!areTheySure)
            return;

        var deleteResult = await ModManager.DeleteModAsync(selectedMod.Mod);
        if (deleteResult.IsFailure)
            MessageTranslationHelper.ShowMessage(deleteResult.Error);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var selectedMod = GetContextModListItem(sender);
        if (selectedMod == null)
            return;

        var openResult = ModManager.OpenModFolder(selectedMod.Mod);
        if (openResult.IsFailure)
            MessageTranslationHelper.ShowMessage(openResult.Error);
    }

    private async void ConvertToPatches_Click(object sender, RoutedEventArgs e)
    {
        var selectedMod = GetContextModListItem(sender);
        if (selectedMod == null)
            return;
        if (!selectedMod.Mod.HasIncompatibleFiles)
            return;

        var result = await ModPresentation.RunAsync(
            (progress, cancellation) => ModPatchConversionService.ConvertToPatchesAsync(selectedMod.Mod, cancellation, progress),
            canCancel: true
        );
        OnModsChanged(refreshPatches: true);

        if (result.IsSuccess)
        {
            var conversion = result.Value;
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Message)
                .SetTitleText(t("message_success.mod_converted_to_patches.title"))
                .SetInfoText(BuildPatchConversionResultMessage(conversion))
                .Show();
            return;
        }

        new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Warning)
            .SetTitleText(t("message_warning.could_not_convert_mod.title"))
            .SetInfoText(result.Error.Message)
            .Show();
    }

    private static string BuildPatchConversionResultMessage(ModPatchConversionResult conversion)
    {
        var message = t(
            "message_success.patch_conversion_result",
            new { fileCount = conversion.ConvertedFileCount, patchCount = conversion.WrittenPatchCount }
        );

        if (conversion.Skipped.Count > 0)
        {
            message +=
                $"{Environment.NewLine}{Environment.NewLine}"
                + t("message_success.patch_conversion_skipped", new { amount = conversion.Skipped.Count })!
                + $"{Environment.NewLine}{Environment.NewLine}"
                + string.Join(Environment.NewLine, conversion.Skipped.Take(8));
        }

        if (conversion.Warnings.Count > 0)
        {
            message +=
                $"{Environment.NewLine}{Environment.NewLine}"
                + t("message_success.patch_conversion_notes", new { amount = conversion.Warnings.Count })!
                + $"{Environment.NewLine}{Environment.NewLine}"
                + string.Join(Environment.NewLine, conversion.Warnings.Take(8));
        }

        return message;
    }

    private void ViewMod_Click(object sender, RoutedEventArgs e)
    {
        var selectedMod = GetContextModListItem(sender);
        if (selectedMod == null)
        {
            // You actually never see this error, however, if for some unknown reason it happens, we don't want to disregard it
            MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_CantViewMod_SomethingWrong);
            return;
        }

        if (selectedMod.Mod.ModID == -1)
        {
            MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_CantViewMod_NotFromBrowser);
            return;
        }

        var modPopup = Popups.Create<ModIndependentWindow>();
        modPopup.SetModalOwner(this);
        modPopup.Show();
        _ = modPopup.LoadModAsync(selectedMod.Mod.ModID);
    }

    /// <summary>
    /// Resolves the ModListItem from either a grid context menu (DataContext) or ListBox selection.
    /// </summary>
    private ModListItem? GetContextModListItem(object? sender)
    {
        if (sender is Control { DataContext: ModListItem item })
            return item;
        return ModsListBox.SelectedItem as ModListItem;
    }

    private async void ToggleButton_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        var toggleResult = await ModManager.ToggleAllModsAsync(EnableAllCheckbox.IsChecked == true);
        if (toggleResult.IsFailure)
            MessageTranslationHelper.ShowMessage(toggleResult.Error);
    }

    private async void PriorityText_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        var mod = GetParentsMod(e);
        if (mod == null || e.Source is not InputField textBox)
            return;

        textBox.ErrorText = null;
        if (int.TryParse(textBox.Text, out var newPriority))
        {
            var priorityResult = await ModManager.SetPriorityAsync(mod, newPriority);
            if (priorityResult.IsFailure)
                MessageTranslationHelper.ShowMessage(priorityResult.Error);
        }
        else
        {
            textBox.Text = mod.Priority.ToString();
        }
    }

    private void PriorityText_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        var mod = GetParentsMod(e);
        if (mod == null || e.Source is not InputField textBox)
            return;

        textBox.ErrorText = int.TryParse(textBox.Text, out _)
            ? null
            : WheelWizard.Localization.TranslationFunctions.t("component.input.invalid_number");
    }

    private Mod? GetParentsMod(RoutedEventArgs eventArgs)
    {
        var parent = ViewUtils.FindParent<ListBoxItem>(eventArgs.Source);
        if (parent?.Content is ModListItem mod)
            return mod.Mod;
        return null;
    }

    private async void ButtonUp_OnClick(object? sender, RoutedEventArgs e)
    {
        var mod = GetParentsMod(e);
        if (mod == null)
            return;

        var priorityResult = await ModManager.DecreasePriorityAsync(mod);
        if (priorityResult.IsFailure)
            MessageTranslationHelper.ShowMessage(priorityResult.Error);
    }

    private async void ButtonDown_OnClick(object? sender, RoutedEventArgs e)
    {
        var mod = GetParentsMod(e);
        if (mod == null)
            return;

        var priorityResult = await ModManager.IncreasePriorityAsync(mod);
        if (priorityResult.IsFailure)
            MessageTranslationHelper.ShowMessage(priorityResult.Error);
    }

    private void ModsView_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_viewInitialized || ModsListBox is null || ViewModeToggle.SelectedIndex < 0)
            return;
        var gridView = ViewModeToggle.SelectedIndex == 0;
        if (SettingsService.Get<bool>(SettingsService.PREFERS_MODS_ROW_VIEW) == gridView)
            return;
        SettingsService.Set(SettingsService.PREFERS_MODS_ROW_VIEW, gridView);
        SetModsViewVariant();
    }

    private void SetModsViewVariant()
    {
        CancelDrag();
        var grid = SettingsService.Get<bool>(SettingsService.PREFERS_MODS_ROW_VIEW);
        ModsListBox.IsVisible = !grid;
        ModsGridView.IsVisible = grid;
        ViewModeToggle.SelectedIndex = grid ? 0 : 1;
    }

    private void PriorityText_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox)
            return;
        ViewUtils.FindParent<ListBoxItem>(e.Source)?.Focus();
    }

    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is TextBox || ViewUtils.FindParent<TextBox>(e.Source) != null)
            return;

        var clickedControl = e.Source as Control ?? ViewUtils.FindParent<Control>(e.Source);
        if (clickedControl?.Focusable == true)
            clickedControl.Focus(NavigationMethod.Pointer, e.KeyModifiers);
        else
            Focus(NavigationMethod.Pointer, e.KeyModifiers);
    }

    #region Drag and Drop

    private void DragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var container = sender as GridModPanel ?? (Control?)ViewUtils.FindParent<ListBoxItem>(sender);
        if (container?.DataContext is not ModListItem modItem || Mods.Count <= 1)
            return;

        if (container is ListBoxItem)
            ModsListBox.SelectedItem = modItem;

        _isDragPending = true;
        _isDragging = false;
        _dragStartPoint = e.GetPosition(this);
        _draggedItem = Mods.First(item => ReferenceEquals(item.Mod, modItem.Mod));
        _draggedContainer = container;
        _dragOffset = e.GetPosition(container);

        _capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragPending && !_isDragging)
            return;

        var currentPos = e.GetPosition(this);

        if (_isDragPending && !_isDragging)
        {
            var delta = currentPos - _dragStartPoint;
            if (Math.Abs(delta.Y) < DragThreshold && Math.Abs(delta.X) < DragThreshold)
                return;

            StartDrag(currentPos);
        }

        if (_isDragging)
            UpdateDrag(e, currentPos);
    }

    private async void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            await EndDragAsync();
            e.Handled = true;
        }
        else if (_isDragPending)
        {
            CancelDrag();
        }
    }

    private void OnDragPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDragging || _isDragPending)
            CancelDrag();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && (_isDragging || _isDragPending))
        {
            CancelDrag();
            e.Handled = true;
        }
    }

    private void StartDrag(Point currentPos)
    {
        _isDragPending = false;
        _isDragging = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);

        _dragStartIndex = GetModIndex(_draggedItem!);
        _previewIndex = _dragStartIndex;

        CreateDragAdorner(currentPos);
        _draggedItem!.IsPlaceholder = true;
    }

    private void CreateDragAdorner(Point pos)
    {
        var shadow = new BoxShadows(
            new BoxShadow
            {
                Blur = 20,
                Color = Color.Parse("#70000000"),
                OffsetY = 6,
            }
        );
        if (ModsGridView.IsVisible)
        {
            var card = new GridModPanel
            {
                DataContext = new ModListItem(
                    _draggedItem!.Mod,
                    _draggedItem.IsLowest,
                    _draggedItem.IsHighest,
                    _draggedItem.Preview,
                    _draggedItem.HasPatches
                ),
                IsHitTestVisible = false,
            };
            var border = card.FindControl<Border>("RootCardBorder")!;
            border.BoxShadow = shadow;
            border.BorderBrush = new SolidColorBrush((Color)this.FindResource("Brand400")!);
            _dragAdorner = new Border { Width = _draggedContainer!.Bounds.Width, Child = card };
        }
        else
        {
            _dragAdorner = new Border
            {
                Width = 250,
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(8),
                BoxShadow = shadow,
                Background = new SolidColorBrush((Color)this.FindResource("Neutral900")!),
                BorderBrush = new SolidColorBrush((Color)this.FindResource("Brand400")!),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new PathIcon
                        {
                            Data = (Geometry)this.FindResource("Grip")!,
                            Width = 12,
                            Height = 12,
                            Foreground = new SolidColorBrush((Color)this.FindResource("Neutral400")!),
                        },
                        new TextBlock
                        {
                            Text = _draggedItem?.Mod.Title,
                            FontSize = 13,
                            FontWeight = FontWeight.SemiBold,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            MaxWidth = 200,
                            Foreground = new SolidColorBrush((Color)this.FindResource("Neutral100")!),
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    },
                },
            };
        }
        _dragAdorner.ZIndex = 1;
        DragCanvas.Children.Add(_dragAdorner);
        DragCanvas.IsVisible = true;
        PositionDragAdorner(pos);
    }

    private void PositionDragAdorner(Point pos)
    {
        if (_dragAdorner == null)
            return;
        var grid = ModsGridView.IsVisible;
        Canvas.SetLeft(_dragAdorner, pos.X - (grid ? _dragOffset.X : 30));
        Canvas.SetTop(_dragAdorner, pos.Y - (grid ? _dragOffset.Y : Math.Min(_dragOffset.Y, 25)));
    }

    private void UpdateDrag(PointerEventArgs e, Point currentPos)
    {
        PositionDragAdorner(currentPos);
        HandleAutoScroll(e);
        var pos = e.GetPosition(this);
        // Compare layout slots, not animated positions, so moving items cannot make the target oscillate.
        var nearest = GetDragItems()
            .MinBy(control =>
            {
                var origin = control.GetVisualParent()?.TranslatePoint(control.Bounds.Position, this) ?? default;
                var dx = ModsGridView.IsVisible ? pos.X - origin.X - control.Bounds.Width / 2 : 0;
                var dy = pos.Y - origin.Y - control.Bounds.Height / 2;
                return dx * dx + dy * dy;
            });
        if (nearest?.DataContext is not ModListItem target)
            return;
        var index = Mods.IndexOf(target);
        if (index < 0 || index == _previewIndex)
            return;
        UpdateModLayout(() => Mods.Move(_previewIndex, index));
        _previewIndex = index;
    }

    private void HandleAutoScroll(PointerEventArgs e)
    {
        var scrollViewer = ModsGridView.IsVisible
            ? ModsGridView
            : ModsListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer == null)
            return;

        var pos = e.GetPosition(scrollViewer);
        const double scrollZone = 40.0;
        const double scrollSpeed = 8.0;

        if (pos.Y < scrollZone && scrollViewer.Offset.Y > 0)
        {
            var factor = 1.0 - pos.Y / scrollZone;
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, Math.Max(0, scrollViewer.Offset.Y - scrollSpeed * factor));
        }
        else if (pos.Y > scrollViewer.Viewport.Height - scrollZone)
        {
            var maxScroll = scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
            if (scrollViewer.Offset.Y < maxScroll)
            {
                var factor = 1.0 - (scrollViewer.Viewport.Height - pos.Y) / scrollZone;
                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, Math.Min(maxScroll, scrollViewer.Offset.Y + scrollSpeed * factor));
            }
        }
    }

    private IEnumerable<Control> GetDragItems() =>
        ModsGridView.IsVisible ? ModsGridItems.GetVisualDescendants().OfType<GridModPanel>() : ModsListBox.GetRealizedContainers();

    private int GetModIndex(ModListItem modItem) => ModManager.Mods.IndexOf(modItem.Mod);

    private async Task EndDragAsync()
    {
        var modToMove = _draggedItem?.Mod;
        var targetIndex = _previewIndex;
        var sourceIndex = _dragStartIndex;
        CleanupDrag();
        if (modToMove != null && targetIndex != sourceIndex)
        {
            var gap = targetIndex > sourceIndex ? targetIndex + 1 : targetIndex;
            var moveResult = await ModManager.MoveModToIndexAsync(modToMove, gap);
            if (moveResult.IsFailure)
                MessageTranslationHelper.ShowMessage(moveResult.Error);
        }
        OnModsChanged();
    }

    private void CancelDrag()
    {
        if (!_isDragging && !_isDragPending)
            return;
        CleanupDrag();
        OnModsChanged();
    }

    private void CleanupDrag()
    {
        _isDragging = false;
        _isDragPending = false;
        if (_draggedItem != null)
            _draggedItem.IsPlaceholder = false;
        _draggedItem = null;
        _draggedContainer = null;
        _dragAdorner = null;
        ClearValue(CursorProperty);

        DragCanvas.Children.Clear();
        DragCanvas.IsVisible = false;

        _capturedPointer?.Capture(null);
        _capturedPointer = null;
    }

    #endregion

    #region PropertyChanged

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
