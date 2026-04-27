using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using WheelWizard.Features.Patches;
using WheelWizard.Helpers;
using WheelWizard.Models.Mods;
using WheelWizard.Mods;
using WheelWizard.Resources.Languages;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.Views.Popups.ModManagement;

namespace WheelWizard.Views.Pages;

public record ModListItem(Mod Mod, bool IsLowest, bool IsHighest);

public partial class ModsPage : UserControlBase, INotifyPropertyChanged
{
    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    [Inject]
    private IModPatchConversionService ModPatchConversionService { get; set; } = null!;

    [Inject]
    private IModManager ModManagerService { get; set; } = null!;

    public IModManager ModManager => ModManagerService;

    public ObservableCollection<ModListItem> Mods =>
        new(
            ModManager.Mods.Select(mod => new ModListItem(
                mod,
                mod.Priority == ModManager.GetLowestActivePriority(),
                mod.Priority == ModManager.GetHighestActivePriority()
            ))
        );

    public string StoragePageTitle => "Patches";

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
    private int _currentDropIndex;
    private ListBoxItem? _draggedListBoxItem;
    private double _dragOffsetY;
    private Border? _dragAdorner;
    private Border? _dropIndicatorLine;
    private IPointer? _capturedPointer;
    private const double DragThreshold = 5.0;

    public ModsPage()
    {
        InitializeComponent();
        DataContext = this;
        Focusable = true;
        ModManager.PropertyChanged += OnModsChanged;
        _ = ReloadModsAndShowErrorsAsync();
        SetModsViewVariant();

        // Apply priority edits as soon as the user clicks anywhere outside the textbox.
        AddHandler(PointerPressedEvent, OnPagePointerPressed, RoutingStrategies.Tunnel, true);

        // Wire up drag-and-drop pointer tracking
        PointerMoved += OnDragPointerMoved;
        PointerReleased += OnDragPointerReleased;
        PointerCaptureLost += OnDragPointerCaptureLost;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ModManager.PropertyChanged -= OnModsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnModsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModManager.Mods))
            OnModsChanged();
        else if (e.PropertyName == nameof(Mod.IsEnabled))
            UpdateEnableAllCheckboxState();
    }

    private void OnModsChanged()
    {
        if (_isDragging)
            return; // Suppress UI updates during drag to prevent stale container references

        ListItemCount.Text = ModManager.Mods.Count.ToString();
        OnPropertyChanged(nameof(Mods));
        HasMods = ModManager.Mods.Count > 0;
        UpdateEnableAllCheckboxState();
    }

    private void UpdateEnableAllCheckboxState()
    {
        EnableAllCheckbox.IsChecked = !ModManager.Mods.Select(mod => mod.IsEnabled).Contains(false);
    }

    private void BrowseMod_Click(object sender, RoutedEventArgs e)
    {
        var modPopup = new ModBrowserWindow();
        modPopup.Show();
    }

    private async Task ReloadModsAndShowErrorsAsync()
    {
        var reloadResult = await ModManager.ReloadAsync();
        if (reloadResult.IsFailure)
            MessageTranslationHelper.ShowMessage(reloadResult.Error);
    }

    private async void ImportMod_Click(object sender, RoutedEventArgs e)
    {
        var selectedFiles = await FilePickerHelper.OpenFilePickerAsync(
            CustomFilePickerFileType.All,
            allowMultiple: true,
            title: "Select Mod File"
        );
        if (selectedFiles.Count == 0)
            return;

        var modName = await new TextInputWindow()
            .SetMainText("Mod name:")
            .SetPlaceholderText("Enter mod name...")
            .SetValidation(ModManager.ValidateModName)
            .ShowDialog();
        if (string.IsNullOrWhiteSpace(modName))
            return;

        var importResult = await ModManager.ImportModFilesAsync(selectedFiles.ToArray(), modName);
        if (importResult.IsFailure)
        {
            MessageTranslationHelper.ShowMessage(importResult.Error);
            return;
        }

        new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Message)
            .SetTitleText("Successfully installed mod!")
            .SetInfoText($"Mod '{modName}' installed successfully.")
            .Show();
    }

    private async void RenameMod_Click(object sender, RoutedEventArgs e)
    {
        var selectedMod = GetContextModListItem(sender);
        if (selectedMod == null)
            return;

        var oldTitle = selectedMod.Mod.Title;
        var newTitle = await new TextInputWindow()
            .SetMainText("Mod Name")
            .SetInitialText(oldTitle)
            .SetExtraText($"Changing name from: {oldTitle}")
            .SetPlaceholderText("Enter mod name...")
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
            .SetMainText(Humanizer.ReplaceDynamic(Phrases.Question_SureDelete_Title, selectedMod.Mod.Title)!)
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

        var result = await ModPatchConversionService.ConvertToPatchesAsync(selectedMod.Mod, CancellationToken.None);
        OnModsChanged();

        if (result.IsSuccess)
        {
            var conversion = result.Value;
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Message)
                .SetTitleText("Converted to patches")
                .SetInfoText(
                    $"Converted {conversion.ConvertedFileCount} archive file(s) into {conversion.WrittenPatchCount} patch file(s)."
                )
                .Show();
            return;
        }

        new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Warning)
            .SetTitleText("Could not convert mod")
            .SetInfoText(result.Error.Message)
            .Show();
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

        var modPopup = new ModIndependentWindow();
        _ = modPopup.LoadModAsync(selectedMod.Mod.ModID);
        modPopup.ShowDialog();
    }

    /// <summary>
    /// Resolves the ModListItem from either a grid context menu (DataContext) or ListBox selection.
    /// </summary>
    private ModListItem? GetContextModListItem(object? sender)
    {
        if (sender is MenuItem { DataContext: ModListItem item })
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
        if (mod == null || e.Source is not TextBox textBox)
            return;

        textBox.Classes.Remove("error"); // In case this class has been added, then we remove it again
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
        if (mod == null || e.Source is not TextBox textBox)
            return;

        // We intentionally don't use the FeedbackTextBox here since that component is a bit to big for this use case.
        if (int.TryParse(textBox.Text, out _))
            textBox.Classes.Remove("error");
        else if (!textBox.Classes.Contains("error"))
            textBox.Classes.Add("error");
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

    private void ToggleModsPageView_OnClick(object? sender, RoutedEventArgs e)
    {
        var current = SettingsService.Get<bool>(SettingsService.PREFERS_MODS_ROW_VIEW);
        SettingsService.Set(SettingsService.PREFERS_MODS_ROW_VIEW, !current);
        SetModsViewVariant();
    }

    private void SetModsViewVariant()
    {
        Control[] elementsToSwapClasses = [ToggleButton, ModsListBox];
        var asRows = SettingsService.Get<bool>(SettingsService.PREFERS_MODS_ROW_VIEW);

        foreach (var elementToSwapClass in elementsToSwapClasses)
        {
            if (asRows)
                elementToSwapClass.Classes.Remove("Blocks");
            else
                elementToSwapClass.Classes.Add("Blocks");

            if (asRows)
                elementToSwapClass.Classes.Add("Rows");
            else
                elementToSwapClass.Classes.Remove("Rows");
        }

        // Toggle between list view (Blocks/arrows mode) and grid view (Rows/priority text mode)
        ModsListBox.IsVisible = !asRows;
        ModsGridView.IsVisible = asRows;
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

        var listBoxItem = ViewUtils.FindParent<ListBoxItem>(sender);
        if (listBoxItem?.Content is not ModListItem modItem)
            return;

        if (Mods.Count <= 1)
            return;

        ModsListBox.SelectedItem = modItem;

        _isDragPending = true;
        _isDragging = false;
        _dragStartPoint = e.GetPosition(this);
        _draggedItem = modItem;
        _draggedListBoxItem = listBoxItem;
        _dragOffsetY = e.GetPosition(listBoxItem).Y;

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
            await EndDragAsync(commit: true);
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

        _dragStartIndex = GetModIndex(_draggedItem!);
        _currentDropIndex = _dragStartIndex;

        if (_draggedListBoxItem != null)
            _draggedListBoxItem.Opacity = 0.3;

        CreateDragAdorner(currentPos);
        CreateDropIndicator();
    }

    private void CreateDragAdorner(Point pos)
    {
        var title = _draggedItem?.Mod.Title ?? "Mod";

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 10,
        };

        try
        {
            if (this.FindResource("Grip") is Geometry gripData)
            {
                content.Children.Add(
                    new PathIcon
                    {
                        Data = gripData,
                        Width = 12,
                        Height = 12,
                        Foreground = new SolidColorBrush(Color.Parse("#6C7389")),
                    }
                );
            }
        }
        catch
        {
            // Resource not found, skip grip icon
        }

        content.Children.Add(
            new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
            }
        );

        _dragAdorner = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#474B5D")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(15, 0),
            Height = 50,
            MinWidth = 250,
            RenderTransform = new RotateTransform(1.5),
            Opacity = 0.95,
            BoxShadow = new BoxShadows(
                new BoxShadow
                {
                    Blur = 15,
                    Color = Color.Parse("#80000000"),
                    OffsetX = 0,
                    OffsetY = 4,
                }
            ),
            Child = content,
        };

        Canvas.SetLeft(_dragAdorner, pos.X - 30);
        Canvas.SetTop(_dragAdorner, pos.Y - _dragOffsetY);

        DragCanvas.Children.Add(_dragAdorner);
        DragCanvas.IsVisible = true;
    }

    private void CreateDropIndicator()
    {
        _dropIndicatorLine = new Border
        {
            Height = 3,
            Background = new SolidColorBrush(Color.Parse("#34EAC5")),
            CornerRadius = new CornerRadius(2),
            IsVisible = false,
        };

        DragCanvas.Children.Add(_dropIndicatorLine);
    }

    private void UpdateDrag(PointerEventArgs e, Point currentPos)
    {
        if (_dragAdorner != null)
        {
            Canvas.SetLeft(_dragAdorner, currentPos.X - 30);
            Canvas.SetTop(_dragAdorner, currentPos.Y - _dragOffsetY);
        }

        var dropIndex = CalculateDropIndex(e);
        _currentDropIndex = dropIndex;
        UpdateDropIndicator(dropIndex);
        HandleAutoScroll(e);
    }

    private int CalculateDropIndex(PointerEventArgs e)
    {
        var posInListBox = e.GetPosition(ModsListBox);
        var items = GetListBoxItems();

        if (items.Count == 0)
            return 0;

        for (var i = 0; i < items.Count; i++)
        {
            var itemPos = items[i].TranslatePoint(new Point(0, 0), ModsListBox);
            if (itemPos == null)
                continue;

            var midY = itemPos.Value.Y + items[i].Bounds.Height / 2;
            if (posInListBox.Y < midY)
                return i;
        }

        return items.Count;
    }

    private void UpdateDropIndicator(int gapIndex)
    {
        if (_dropIndicatorLine == null)
            return;

        var items = GetListBoxItems();
        if (items.Count == 0)
            return;

        // Hide indicator when hovering over the no-op zone (same position)
        if (gapIndex == _dragStartIndex || gapIndex == _dragStartIndex + 1)
        {
            _dropIndicatorLine.IsVisible = false;
            return;
        }

        _dropIndicatorLine.IsVisible = true;

        if (gapIndex < items.Count)
        {
            var item = items[gapIndex];
            var itemPos = item.TranslatePoint(new Point(0, -2), DragCanvas);
            if (itemPos != null)
            {
                Canvas.SetLeft(_dropIndicatorLine, itemPos.Value.X);
                Canvas.SetTop(_dropIndicatorLine, itemPos.Value.Y);
                _dropIndicatorLine.Width = item.Bounds.Width;
            }
        }
        else
        {
            var lastItem = items[^1];
            var itemPos = lastItem.TranslatePoint(new Point(0, lastItem.Bounds.Height + 2), DragCanvas);
            if (itemPos != null)
            {
                Canvas.SetLeft(_dropIndicatorLine, itemPos.Value.X);
                Canvas.SetTop(_dropIndicatorLine, itemPos.Value.Y);
                _dropIndicatorLine.Width = lastItem.Bounds.Width;
            }
        }
    }

    private void HandleAutoScroll(PointerEventArgs e)
    {
        var scrollViewer = ModsListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
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

    private List<ListBoxItem> GetListBoxItems()
    {
        var items = new List<ListBoxItem>();
        for (var i = 0; i < Mods.Count; i++)
        {
            if (ModsListBox.ContainerFromIndex(i) is ListBoxItem lbi)
                items.Add(lbi);
        }
        return items;
    }

    private int GetModIndex(ModListItem modItem)
    {
        for (var i = 0; i < Mods.Count; i++)
        {
            if (Mods[i].Mod == modItem.Mod)
                return i;
        }
        return -1;
    }

    private async Task EndDragAsync(bool commit)
    {
        if (_draggedListBoxItem != null)
            _draggedListBoxItem.Opacity = 1.0;

        var modToMove = _draggedItem?.Mod;
        var targetGapIndex = _currentDropIndex;
        var sourceIndex = _dragStartIndex;
        var shouldCommit = commit && modToMove != null && targetGapIndex != sourceIndex && targetGapIndex != sourceIndex + 1;

        CleanupDrag();

        if (shouldCommit)
        {
            var moveResult = await ModManager.MoveModToIndexAsync(modToMove!, targetGapIndex);
            if (moveResult.IsFailure)
                MessageTranslationHelper.ShowMessage(moveResult.Error);
        }
    }

    private void CancelDrag()
    {
        if (!_isDragging && !_isDragPending)
            return;

        if (_draggedListBoxItem != null)
            _draggedListBoxItem.Opacity = 1.0;

        CleanupDrag();
    }

    private void CleanupDrag()
    {
        _isDragging = false;
        _isDragPending = false;
        _draggedItem = null;
        _draggedListBoxItem = null;
        _dragAdorner = null;
        _dropIndicatorLine = null;

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
