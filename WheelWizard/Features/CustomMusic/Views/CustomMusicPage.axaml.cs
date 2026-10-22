using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WheelWizard.CustomMusic.Audio;
using WheelWizard.CustomMusic.ViewModels;
using WheelWizard.Shared.Desktop.Storage;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Navigation;

namespace WheelWizard.CustomMusic.Views;

public partial class CustomMusicPage : UserControl, INavigationGuard
{
    private readonly CustomMusicViewModel _model;
    private readonly IFilePickerService _filePicker;
    private MusicSlotItem? _dropTarget;

    private static FilePickerFileType AudioFiles =>
        new(t("custom_music.file_type"))
        {
            Patterns = AudioFileDecoder.AllExtensions.Select(extension => "*" + extension).ToArray(),
            AppleUniformTypeIdentifiers = ["public.audio"],
            MimeTypes = ["audio/*"],
        };

    public CustomMusicPage(CustomMusicViewModel model, IFilePickerService filePicker)
    {
        _model = model;
        _filePicker = filePicker;
        InitializeComponent();
        DataContext = model;

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        try
        {
            await _model.ActivateAsync();
        }
        catch (Exception exception)
        {
            // async void: an escaping exception would take the whole app down.
            ViewUtils.ShowSnackbar(t("custom_music.error.load_failed", new { message = exception.Message }), ViewUtils.SnackbarType.Warning);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        EndDrag();
        _model.Deactivate();
        base.OnDetachedFromVisualTree(e);
    }

    #region Navigation guard

    public bool HasUnsavedWork => _model.HasPendingChanges || _model.IsConverting;

    public async Task<bool> ConfirmLeaveAsync()
    {
        var leave = await new YesNoWindow()
            .SetMainText(t("custom_music.leave.title"))
            .SetExtraText(t("custom_music.leave.extra"))
            .SetButtonText(t("custom_music.leave.discard"), t("custom_music.leave.stay"))
            .SetButtonTones(ActionButtonTone.Danger, ActionButtonTone.Secondary)
            .AwaitAnswer();
        if (leave)
            _model.DiscardChanges();
        return leave;
    }

    #endregion

    #region Header actions

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        var result = await _model.SaveAsync();
        if (result.IsFailure)
        {
            MessageTranslationHelper.ShowMessage(result.Error);
            return;
        }

        ViewUtils.ShowSnackbar(t("custom_music.saved"));
    }

    private void Discard_Click(object? sender, RoutedEventArgs e) => _model.DiscardChanges();

    private async void EnableMod_Click(object? sender, RoutedEventArgs e)
    {
        var result = await _model.EnableModAsync();
        if (result.IsFailure)
            MessageTranslationHelper.ShowMessage(result.Error);
    }

    #endregion

    #region Row actions

    private static MusicSlotItem? ItemFrom(object? sender) => (sender as StyledElement)?.DataContext as MusicSlotItem;

    private async void ChooseSong_Click(object? sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is not { } item)
            return;

        var files = await _filePicker.OpenFilePickerAsync(AudioFiles, allowMultiple: false, title: t("custom_music.pick_title", new { name = item.Name }));
        if (files.Count == 0)
            return;

        await AssignAsync(item, files[0]);
    }

    /// <summary>One song for every console's version of a course: the row's own song if it has one, otherwise a new pick.</summary>
    private async void UseForFamily_Click(object? sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is not { HasFamily: true } item)
            return;

        var path = item.Current?.SourcePath;
        if (path == null || !_model.SongFileExists(path))
        {
            var title = t("custom_music.pick_title", new { name = item.FamilyMenuText });
            var files = await _filePicker.OpenFilePickerAsync(AudioFiles, allowMultiple: false, title: title);
            if (files.Count == 0)
                return;
            path = files[0];
        }

        var result = await _model.AssignToFamilyAsync(item, path, item.Current?.SourcePath == path ? item.Current.VolumeOffsetDb : null);
        if (result.IsFailure)
            ViewUtils.ShowSnackbar(result.Error.Message, ViewUtils.SnackbarType.Warning);
    }

    private async void ResetAll_Click(object? sender, RoutedEventArgs e)
    {
        var confirmed = await new YesNoWindow()
            .SetMainText(t("custom_music.reset_all.title"))
            .SetExtraText(t("custom_music.reset_all.extra", new { amount = _model.CustomCount }))
            .SetButtonText(t("custom_music.reset_all.confirm"), t("custom_music.leave.stay"))
            .SetButtonTones(ActionButtonTone.Danger, ActionButtonTone.Secondary)
            .AwaitAnswer();
        if (confirmed)
            _model.ResetAll();
    }

    private void UndoChange_Click(object? sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is { } item)
            _model.UndoChange(item);
    }

    private void Reset_Click(object? sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is { } item)
            _model.ResetToOriginal(item);
    }

    private void ShowSource_Click(object? sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is not { IsCustom: true } item)
            return;

        if (_model.SourceFolderOf(item) is { } folder)
            _filePicker.OpenFolderInFileManager(folder);
        else
            ViewUtils.ShowSnackbar(t("custom_music.error.file_missing"), ViewUtils.SnackbarType.Warning);
    }

    /// <summary>The "..." button opens the same menu as right-clicking the row.</summary>
    private void RowMenu_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var row = ViewUtils.FindParent<Border>(sender);
        while (row != null && !row.Classes.Contains("MusicRow"))
            row = ViewUtils.FindParent<Border>(row);
        row?.ContextMenu?.Open();
    }

    private async void PlayOriginal_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ItemFrom(sender) is { } item)
            await ShowPreviewResultAsync(item, custom: false);
    }

    private async void PlayCustom_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ItemFrom(sender) is { } item)
            await ShowPreviewResultAsync(item, custom: true);
    }

    private async Task ShowPreviewResultAsync(MusicSlotItem item, bool custom)
    {
        var result = await _model.PreviewAsync(item, custom);
        if (result.IsFailure)
            ViewUtils.ShowSnackbar(result.Error.Message, ViewUtils.SnackbarType.Warning);
    }

    private void StopPreview_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _model.StopPreview();
    }

    private void PlayPause_Click(object? sender, RoutedEventArgs e) => _model.TogglePause();

    private void Seek_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control bar && bar.Bounds.Width > 0)
            _model.SeekTo(e.GetPosition(bar).X / bar.Bounds.Width);
    }

    private async void Louder_Click(object? sender, RoutedEventArgs e) => await AdjustVolumeAsync(sender, CustomMusicViewModel.VolumeStepDb);

    private async void Quieter_Click(object? sender, RoutedEventArgs e) => await AdjustVolumeAsync(sender, -CustomMusicViewModel.VolumeStepDb);

    private async Task AdjustVolumeAsync(object? sender, double deltaDb)
    {
        if (ItemFrom(sender) is not { } item)
            return;
        var result = await _model.AdjustVolumeAsync(item, deltaDb);
        if (result.IsFailure)
        {
            if (item.Error == null)
                ViewUtils.ShowSnackbar(result.Error.Message, ViewUtils.SnackbarType.Warning);
        }
        else if (result.Value == VolumeChange.AtLimit)
        {
            ViewUtils.ShowSnackbar(t("custom_music.volume.at_limit", new { name = item.Name }), ViewUtils.SnackbarType.Warning);
        }
    }

    private async Task AssignAsync(MusicSlotItem item, string path)
    {
        var result = await _model.AssignAsync(item, path);
        if (result.IsFailure && item.Error == null)
            ViewUtils.ShowSnackbar(result.Error.Message, ViewUtils.SnackbarType.Warning);
    }

    #endregion

    #region Drag and drop

    private static string[] GetDroppedFiles(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).Where(path => !string.IsNullOrEmpty(path)).Select(path => path!).ToArray()
        ?? [];

    private static bool HasFiles(DragEventArgs e) => e.DataTransfer.Contains(DataFormat.File);

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (!HasFiles(e))
            return;
        DropHint.IsVisible = true;
        UpdateDropTarget(e);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!HasFiles(e))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        DropHint.IsVisible = true;
        UpdateDropTarget(e);
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        // Leave events bubble up from every child; only react when the pointer really left the page.
        var position = e.GetPosition(this);
        if (new Rect(Bounds.Size).Contains(position))
            return;
        EndDrag();
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var target = TargetFrom(e);
        EndDrag();
        var dropped = GetDroppedFiles(e);
        if (dropped.Length == 0)
            return;
        e.Handled = true;

        // A single song on a row replaces that row; folders and music packs (.zip) are matched by file name.
        var isSingleSong = dropped.Length == 1 && AudioFileDecoder.IsSupportedExtension(dropped[0]);
        if (target != null && isSingleSong)
        {
            await AssignAsync(target, dropped[0]);
            return;
        }

        var expanded = await _model.ExpandDropAsync(dropped);
        if (expanded.IsFailure)
        {
            ViewUtils.ShowSnackbar(expanded.Error.Message, ViewUtils.SnackbarType.Warning);
            return;
        }

        var files = expanded.Value.ToArray();
        if (files.Length == 0)
            return;

        // Files named after game music (n_Circuit32_n.brstm, as music packs ship them) find their track by name.
        var matches = _model.MatchByFileName(files);
        if (matches.Count == 0)
        {
            if (target != null)
            {
                if (files.Length > 1)
                    ViewUtils.ShowSnackbar(t("custom_music.drop_first_only"), ViewUtils.SnackbarType.Warning);
                await AssignAsync(target, files[0]);
                return;
            }

            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Message)
                .SetTitleText(t("custom_music.drop_on_row.title"))
                .SetInfoText(t("custom_music.drop_on_row.extra"))
                .Show();
            return;
        }

        ViewUtils.ShowSnackbar(t("custom_music.matched_files", new { matched = matches.Count, total = files.Length }));
        await Task.WhenAll(matches.Select(match => AssignAsync(match.Item, match.Path)));
    }

    private MusicSlotItem? TargetFrom(DragEventArgs e) => (e.Source as StyledElement)?.DataContext as MusicSlotItem;

    private void UpdateDropTarget(DragEventArgs e)
    {
        var target = TargetFrom(e);
        if (ReferenceEquals(target, _dropTarget))
            return;
        if (_dropTarget != null)
            _dropTarget.IsDropTarget = false;
        _dropTarget = target;
        if (_dropTarget != null)
            _dropTarget.IsDropTarget = true;
        DropHintText.Text = target == null ? t("custom_music.drop_hint") : t("custom_music.drop_on", new { name = target.Name });
    }

    private void EndDrag()
    {
        if (_dropTarget != null)
            _dropTarget.IsDropTarget = false;
        _dropTarget = null;
        DropHint.IsVisible = false;
    }

    #endregion
}
