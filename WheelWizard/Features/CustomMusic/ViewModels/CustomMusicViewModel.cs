using System.ComponentModel;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WheelWizard.CustomMusic.Audio;
using WheelWizard.CustomMusic.Catalog;
using WheelWizard.CustomMusic.Library;
using WheelWizard.CustomMusic.Playback;

namespace WheelWizard.CustomMusic.ViewModels;

public sealed class SortOption(MusicSortMode mode, string label)
{
    public MusicSortMode Mode { get; } = mode;

    public override string ToString() => label;
}

public sealed class FinalLapOption(FinalLapStyle style, string label)
{
    public FinalLapStyle Style { get; } = style;

    public override string ToString() => label;
}

public sealed class VolumeOption(double decibels, string label)
{
    public double Decibels { get; } = decibels;

    public override string ToString() => label;
}

/// <summary>What happened when a song was made louder or quieter.</summary>
public enum VolumeChange
{
    /// <summary>Nothing to adjust (no custom song), or a newer adjustment replaced this one.</summary>
    None,
    Changed,

    /// <summary>The song was asked to get louder but its peaks already hold it back; it would only distort.</summary>
    AtLimit,
}

/// <summary>
/// State and workflow of the Custom Music page: the slot list, conversions, previews and saving.
/// The page calls <see cref="ActivateAsync"/> when it is shown and <see cref="Deactivate"/> when it goes away.
/// (Not <see cref="IDisposable"/> on purpose: the container would keep every transient disposable alive.)
/// </summary>
public sealed class CustomMusicViewModel(
    ICustomMusicService service,
    IMusicPreviewService preview,
    IMusicPreviewPlayer player,
    IFileSystem fileSystem
) : INotifyPropertyChanged
{
    /// <summary>A conversion in progress: the song and the volume it is being converted at.</summary>
    private sealed record ConversionRequest(CancellationTokenSource Cancellation, string Path, double VolumeOffsetDb);

    /// <summary>One step of the "Louder" / "Quieter" row actions.</summary>
    public const double VolumeStepDb = 2;

    private const double MaxVolumeOffsetDb = 18;
    private const double MinVolumeOffsetDb = -12;

    // Conversions are CPU and memory heavy; a couple at a time keeps the app responsive.
    private readonly SemaphoreSlim _conversionSlots = new(2, 2);
    private CancellationTokenSource _lifetime = new();
    private readonly List<MusicSlotItem> _items = [];
    private readonly Dictionary<MusicSlotItem, ConversionRequest> _conversions = [];
    private bool _loaded;
    private MusicCatalog _catalog = MusicCatalog.Empty;
    private List<CustomMusicEntry> _orphanedEntries = [];
    private string _searchText = string.Empty;
    private SortOption _selectedSort = null!;
    private FinalLapOption _selectedFinalLap = null!;
    private VolumeOption _selectedVolume = null!;
    private MusicSlotItem? _previewItem;
    private bool _previewIsCustom;
    private int _previewRequest;
    private bool _playerHooked;
    private bool _isLoading = true;
    private bool _isSaving;
    private CustomMusicModState _modState;
    private string? _conflictText;
    private bool _preferencesReady;

    public BulkObservableCollection<object> Rows { get; } = [];

    public IReadOnlyList<SortOption> SortOptions { get; } =
    [
        new(MusicSortMode.Default, t("custom_music.sort.default")),
        new(MusicSortMode.ByType, t("custom_music.sort.by_type")),
        new(MusicSortMode.CustomFirst, t("custom_music.sort.custom_first")),
        new(MusicSortMode.Alphabetical, t("custom_music.sort.alphabetical")),
    ];

    public IReadOnlyList<FinalLapOption> FinalLapOptions { get; } =
    [
        new(FinalLapStyle.Faster, t("custom_music.final_lap.faster")),
        new(FinalLapStyle.FasterKeepPitch, t("custom_music.final_lap.faster_keep_pitch")),
        new(FinalLapStyle.Unchanged, t("custom_music.final_lap.unchanged")),
    ];

    public IReadOnlyList<VolumeOption> VolumeOptions { get; } =
    [
        new(-3, t("custom_music.volume.quieter")),
        new(0, t("custom_music.volume.same")),
        new(3, t("custom_music.volume.louder")),
        new(6, t("custom_music.volume.much_louder")),
        new(9, t("custom_music.volume.loudest")),
    ];

    public SortOption SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (value == null || ReferenceEquals(_selectedSort, value))
                return;
            _selectedSort = value;
            Notify();
            RefreshRows();
            PersistPreferences();
        }
    }

    public FinalLapOption SelectedFinalLap
    {
        get => _selectedFinalLap;
        set
        {
            if (value == null || ReferenceEquals(_selectedFinalLap, value))
                return;
            _selectedFinalLap = value;
            Notify();
            PersistPreferences();
        }
    }

    public VolumeOption SelectedVolume
    {
        get => _selectedVolume;
        set
        {
            if (value == null || ReferenceEquals(_selectedVolume, value))
                return;
            _selectedVolume = value;
            Notify();
            PersistPreferences();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
                return;
            _searchText = value ?? string.Empty;
            Notify();
            RefreshRows();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            _isLoading = value;
            Notify();
            NotifyState();
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            _isSaving = value;
            Notify();
            NotifyState();
        }
    }

    public int CustomCount => _items.Count(item => item.IsCustom);
    public bool HasCustomMusic => CustomCount > 0;
    public int PendingCount => _items.Count(item => item.IsPending);
    public bool HasPendingChanges => PendingCount > 0;
    public bool IsConverting => _items.Any(item => item.IsProcessing);
    public bool CanSave => HasPendingChanges && !IsConverting && !IsSaving && !IsLoading;
    public string PendingText => t("custom_music.pending", PendingCount);
    public bool HasNoResults => !IsLoading && Rows.Count == 0;

    public bool IsRetroRewindMissing => !IsLoading && _catalog.RetroRewind == RetroRewindCatalogState.NotInstalled;
    public bool IsRetroRewindUnreadable => !IsLoading && _catalog.RetroRewind == RetroRewindCatalogState.Unreadable;
    public bool IsModDisabled => _modState == CustomMusicModState.Disabled;
    public string? ConflictText
    {
        get => _conflictText;
        private set
        {
            _conflictText = value;
            Notify();
            Notify(nameof(HasConflicts));
        }
    }
    public bool HasConflicts => ConflictText != null;

    public string Summary =>
        IsLoading ? t("custom_music.loading")
        : _catalog.RetroRewind == RetroRewindCatalogState.Loaded ? t("custom_music.summary", new { tracks = _items.Count(i => i.Slot.Category is MusicCategory.RetroRewindTrack or MusicCategory.RetroRewindArena), total = _items.Count })
        : t("custom_music.summary_game_only", new { total = _items.Count });

    /// <summary>Loads the list the first time the page is shown (and again after <see cref="Deactivate"/>).</summary>
    public Task ActivateAsync()
    {
        if (_loaded)
            return Task.CompletedTask;
        _loaded = true;
        if (_lifetime.IsCancellationRequested)
        {
            _lifetime.Dispose();
            _lifetime = new CancellationTokenSource();
        }

        return LoadAsync();
    }

    /// <summary>The page went away: stop the preview and any conversions, and let go of cover images.</summary>
    public void Deactivate()
    {
        _loaded = false;
        if (_playerHooked)
            player.Changed -= Player_Changed;
        _playerHooked = false;
        _previewRequest++;
        player.Stop();
        if (_previewItem != null)
            _previewItem.PreviewState = ItemPreviewState.None;
        _previewItem = null;

        _lifetime.Cancel();
        foreach (var running in _conversions.Values)
            running.Cancellation.Cancel();
        _conversions.Clear();
        foreach (var item in _items)
        {
            item.PropertyChanged -= Item_PropertyChanged;
            item.IsProcessing = false;
            item.Cover = null;
        }
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var library = await Task.Run(service.LoadLibrary);
            _catalog = await service.LoadCatalogAsync(_lifetime.Token);

            _preferencesReady = false;
            SelectedSort = SortOptions.FirstOrDefault(option => option.Mode == library.SortMode) ?? SortOptions[0];
            SelectedFinalLap = FinalLapOptions.FirstOrDefault(option => option.Style == library.FinalLapStyle) ?? FinalLapOptions[0];
            SelectedVolume = VolumeOptions.OrderBy(option => Math.Abs(option.Decibels - library.VolumeOffsetDb)).First();
            _preferencesReady = true;

            foreach (var item in _items)
            {
                item.PropertyChanged -= Item_PropertyChanged;
                item.Cover = null;
            }
            _items.Clear();
            var entries = library.Entries.ToDictionary(entry => entry.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var slot in _catalog.Slots)
            {
                var item = new MusicSlotItem(slot) { CanPreviewOriginal = preview.HasOriginal(slot) };
                if (entries.Remove(slot.Key, out var entry))
                {
                    item.Saved = entry;
                    item.Current = entry;
                    item.Cover = LoadCover(entry);
                }

                item.PropertyChanged += Item_PropertyChanged;
                _items.Add(item);
            }

            AssignFamilies();
            await RefreshPacksAsync();

            // Songs for tracks Retro Rewind no longer has are kept, so they come back if the track returns.
            _orphanedEntries = entries.Values.ToList();
            RefreshModState();
        }
        catch (OperationCanceledException)
        {
            // Page closed while loading.
        }
        finally
        {
            IsLoading = false;
            RefreshRows();
        }
    }

    /// <summary>
    /// Converts <paramref name="path"/> for <paramref name="item"/>; it stays pending until saved.
    /// <paramref name="volumeOffsetDb"/> defaults to the volume chosen in the options.
    /// </summary>
    public async Task<OperationResult> AssignAsync(MusicSlotItem item, string path, double? volumeOffsetDb = null)
    {
        if (!AudioFileDecoder.IsSupportedExtension(path))
        {
            var message = t("custom_music.error.unsupported", new { extension = PathText.Extension(path) });
            item.Error = message;
            return Fail(message);
        }

        CancelConversion(item);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var volume = volumeOffsetDb ?? SelectedVolume.Decibels;
        var request = new ConversionRequest(cancellation, path, volume);
        _conversions[item] = request;
        item.TargetVolumeOffsetDb = volume;
        item.Error = null;
        item.Progress = 0;
        item.IsProcessing = true;

        try
        {
            await _conversionSlots.WaitAsync(cancellation.Token);
            try
            {
                var progress = new Progress<double>(value => item.Progress = value);
                var result = await service.ConvertAsync(item.Slot, path, SelectedFinalLap.Style, volume, progress, cancellation.Token);
                if (cancellation.IsCancellationRequested)
                    return Fail(t("custom_music.error.cancelled"));
                if (result.IsFailure)
                {
                    item.Error = t("custom_music.error.prefix", new { message = result.Error.Message });
                    return result.Error;
                }

                StopPreviewOf(item, customOnly: true);
                item.Current = result.Value;
                item.Cover = LoadCover(result.Value);
                return Ok();
            }
            finally
            {
                _conversionSlots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return Fail(t("custom_music.error.cancelled"));
        }
        finally
        {
            if (_conversions.TryGetValue(item, out var current) && ReferenceEquals(current, request))
            {
                _conversions.Remove(item);
                item.IsProcessing = false;
                item.TargetVolumeOffsetDb = null;
            }

            cancellation.Dispose();
            RefreshRowsIfSortedByChanges();
        }
    }

    /// <summary>Matches dropped files to slots by name (e.g. an existing "n_Circuit32_n.brstm" pack).</summary>
    public IReadOnlyList<(MusicSlotItem Item, string Path)> MatchByFileName(IEnumerable<string> paths)
    {
        var matches = new List<(MusicSlotItem, string)>();
        foreach (var path in paths)
        {
            var name = PathText.FileName(path);
            var item = _items.FirstOrDefault(i => i.Slot.FileNames.Any(file => file.Equals(name, StringComparison.OrdinalIgnoreCase)));
            if (item != null && matches.All(match => match.Item1 != item))
                matches.Add((item, path));
        }

        return matches;
    }

    /// <summary>
    /// Makes the song of <paramref name="item"/> louder or quieter by converting its source file again.
    /// Clicks during a conversion add up: the running conversion restarts with the combined adjustment.
    /// </summary>
    public async Task<OperationResult<VolumeChange>> AdjustVolumeAsync(MusicSlotItem item, double deltaDb)
    {
        // A conversion already running (a new song, or an earlier click) is the one to adjust.
        string? path;
        double currentOffset;
        if (_conversions.TryGetValue(item, out var running))
        {
            path = running.Path;
            currentOffset = running.VolumeOffsetDb;
        }
        else if (item.Current is { } entry)
        {
            path = entry.SourcePath;
            currentOffset = entry.VolumeOffsetDb;
        }
        else
        {
            return VolumeChange.None;
        }

        if (!fileSystem.File.Exists(path))
        {
            var message = t("custom_music.error.file_missing");
            item.Error = message;
            return new OperationError { Message = message };
        }

        var offset = Math.Clamp(currentOffset + deltaDb, MinVolumeOffsetDb, MaxVolumeOffsetDb);
        if (Math.Abs(offset - currentOffset) < 0.01)
            return deltaDb > 0 ? VolumeChange.AtLimit : VolumeChange.None;

        var before = item.Current?.LoudnessVersusOriginalDb;
        var result = await AssignAsync(item, path, offset);
        // A newer click restarted the conversion; that call reports the outcome.
        if (_conversions.ContainsKey(item))
            return VolumeChange.None;
        if (result.IsFailure)
            return result.Error;

        // The peaks may already be at the ceiling; then "louder" only squashes the song.
        var after = item.Current?.LoudnessVersusOriginalDb;
        if (deltaDb > 0 && before is { } was && after is { } now && now - was < 0.5)
            return VolumeChange.AtLimit;
        return VolumeChange.Changed;
    }

    /// <summary>
    /// Converts one song for every row in <paramref name="item"/>'s family (all the Rainbow Roads, say). Each track
    /// is matched to its own original's loudness.
    /// </summary>
    public async Task<OperationResult> AssignToFamilyAsync(MusicSlotItem item, string path, double? volumeOffsetDb = null)
    {
        var results = await Task.WhenAll(item.Family.Select(member => AssignAsync(member, path, volumeOffsetDb)));
        var failures = results.Count(result => result.IsFailure);
        return failures == 0 ? Ok() : Fail(t("custom_music.error.family", new { failed = failures, amount = results.Length }));
    }

    /// <summary>
    /// Turns dropped files and folders into songs to match by file name: folders are searched, and music packs
    /// (.zip) are extracted into the library first.
    /// </summary>
    public async Task<OperationResult<IReadOnlyList<string>>> ExpandDropAsync(IEnumerable<string> paths)
    {
        var songs = new List<string>();
        foreach (var path in paths)
        {
            if (fileSystem.Directory.Exists(path))
            {
                songs.AddRange(
                    await Task.Run(() => fileSystem.Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(AudioFileDecoder.IsSupportedExtension).ToList())
                );
            }
            else if (PathText.Extension(path) == ".zip")
            {
                var extracted = await service.ExtractPackAsync(path, _lifetime.Token);
                if (extracted.IsFailure)
                    return extracted.Error;
                songs.AddRange(extracted.Value);
            }
            else
            {
                songs.Add(path);
            }
        }

        return songs;
    }

    /// <summary>Puts every row back to its original music (pending until saved).</summary>
    public void ResetAll()
    {
        foreach (var item in _items.Where(item => item.IsCustom || item.IsProcessing).ToList())
            ResetToOriginal(item);
        NotifyState();
    }

    /// <summary>Back to the original game / Retro Rewind music (after saving).</summary>
    public void ResetToOriginal(MusicSlotItem item)
    {
        StopPreviewOf(item, customOnly: true);
        CancelConversion(item);
        item.Error = null;
        item.Current = null;
        item.Cover = null;
        RefreshRowsIfSortedByChanges();
    }

    /// <summary>Forgets an unsaved change to this slot.</summary>
    public void UndoChange(MusicSlotItem item)
    {
        StopPreviewOf(item, customOnly: true);
        CancelConversion(item);
        item.Error = null;
        item.Current = item.Saved;
        item.Cover = item.Saved == null ? null : LoadCover(item.Saved);
        RefreshRowsIfSortedByChanges();
    }

    public async Task<OperationResult> SaveAsync()
    {
        if (!CanSave)
            return Ok();

        IsSaving = true;
        try
        {
            var entries = _items.Where(item => item.Current != null).Select(item => item.Current!).Concat(_orphanedEntries).ToList();
            var result = await service.SaveAsync(entries, _catalog.Slots.ToList(), _lifetime.Token);
            if (result.IsFailure)
                return result;

            foreach (var item in _items)
            {
                item.Saved = item.Current;
                item.Error = null;
            }

            RefreshModState();
            return Ok();
        }
        finally
        {
            IsSaving = false;
            NotifyState();
        }
    }

    public void DiscardChanges()
    {
        foreach (var item in _items.ToList())
        {
            if (item.IsPending || item.IsProcessing || item.Error != null)
                UndoChange(item);
        }

        service.DiscardUnsaved();
        NotifyState();
    }

    public async Task<OperationResult> EnableModAsync()
    {
        var result = await service.EnableModAsync();
        RefreshModState();
        return result;
    }

    #region Preview

    public bool IsPreviewAvailable => player.IsAvailable;
    public bool IsPlayerVisible => _previewItem != null;
    public bool IsPlaying => player.State == PreviewState.Playing;
    public bool IsPreviewLoading => _previewItem?.PreviewState == ItemPreviewState.Loading;
    public string NowPlayingTitle => _previewItem?.Name ?? string.Empty;

    public string NowPlayingKind =>
        _previewItem == null ? string.Empty
        : IsPreviewLoading ? t("custom_music.preview.loading")
        : _previewIsCustom ? t("custom_music.preview.your_song")
        : _previewItem.Pack is { } pack ? t("custom_music.preview.pack", new { pack = pack.ModTitle })
        : t("custom_music.preview.original");

    public double PlaybackFraction => player.Duration > TimeSpan.Zero ? Math.Clamp(player.Position / player.Duration, 0, 1) : 0;
    public string PlaybackTime => player.Duration > TimeSpan.Zero ? $"{Format(player.Position)} / {Format(player.Duration)}" : string.Empty;

    /// <summary>
    /// Plays the original music of <paramref name="item"/>, or the song converted for it (<paramref name="custom"/>).
    /// Playing something else, or the same thing again, replaces what is playing.
    /// </summary>
    public async Task<OperationResult> PreviewAsync(MusicSlotItem item, bool custom)
    {
        if (!player.IsAvailable)
            return Fail(t("custom_music.preview.unavailable"));
        if (custom && item.Current == null)
            return Ok();

        HookPlayer();
        ClearPreview();
        var request = ++_previewRequest;
        _previewItem = item;
        _previewIsCustom = custom;
        item.PreviewState = ItemPreviewState.Loading;
        NotifyPlayback();

        try
        {
            var loaded = custom ? await preview.LoadCustomAsync(item.Current!, _lifetime.Token)
                : item.Pack is { } pack ? await preview.LoadFileAsync(pack.Path, _lifetime.Token)
                : await preview.LoadOriginalAsync(item.Slot, _lifetime.Token);
            if (request != _previewRequest)
                return Ok(); // something else was started meanwhile
            if (loaded.IsFailure)
            {
                ClearPreview();
                return loaded.Error;
            }

            player.Play(loaded.Value);
            item.PreviewState = ItemPreviewState.Playing;
            NotifyPlayback();
            return Ok();
        }
        catch (OperationCanceledException)
        {
            return Ok(); // page closed
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if (request == _previewRequest)
                ClearPreview();
            return new OperationError { Message = t("custom_music.preview.failed", new { message = exception.Message }), Exception = exception };
        }
    }

    public void StopPreview() => ClearPreview();

    public void TogglePause()
    {
        if (_previewItem == null)
            return;
        if (player.State == PreviewState.Playing)
            player.Pause();
        else if (player.State == PreviewState.Paused)
            player.Resume();
    }

    /// <summary>Jumps to a position in the song; <paramref name="fraction"/> is 0 (start) to 1 (end).</summary>
    public void SeekTo(double fraction)
    {
        if (_previewItem != null && player.Duration > TimeSpan.Zero)
            player.Seek(player.Duration * Math.Clamp(fraction, 0, 1));
    }

    private void StopPreviewOf(MusicSlotItem item, bool customOnly)
    {
        if (ReferenceEquals(_previewItem, item) && (!customOnly || _previewIsCustom))
            ClearPreview();
    }

    private void ClearPreview()
    {
        _previewRequest++;
        player.Stop();
        var item = _previewItem;
        _previewItem = null;
        if (item != null)
            item.PreviewState = ItemPreviewState.None;
        NotifyPlayback();
    }

    private void HookPlayer()
    {
        if (_playerHooked)
            return;
        _playerHooked = true;
        player.Changed += Player_Changed;
    }

    private void Player_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(OnPlayerChanged);

    private void OnPlayerChanged()
    {
        if (_previewItem == null)
            return;

        // The song reached its end.
        if (player.State == PreviewState.Stopped && !IsPreviewLoading)
        {
            ClearPreview();
            return;
        }

        if (!IsPreviewLoading)
        {
            _previewItem.PreviewState = player.State == PreviewState.Paused ? ItemPreviewState.Paused : ItemPreviewState.Playing;
        }

        NotifyPlayback();
    }

    private void NotifyPlayback()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(NotifyPlayback);
            return;
        }

        Notify(nameof(IsPlayerVisible));
        Notify(nameof(IsPlaying));
        Notify(nameof(IsPreviewLoading));
        Notify(nameof(NowPlayingTitle));
        Notify(nameof(NowPlayingKind));
        Notify(nameof(PlaybackFraction));
        Notify(nameof(PlaybackTime));
    }

    private static string Format(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    #endregion

    private void RefreshModState()
    {
        _modState = service.GetModState();
        var conflicts = service.FindConflicts();
        ConflictText =
            conflicts.Count == 0
                ? null
                : t(
                    "custom_music.conflicts",
                    new { mods = string.Join(", ", conflicts.Select(conflict => conflict.ModTitle).Distinct()), amount = conflicts.Count }
                );
        Notify(nameof(IsModDisabled));
    }

    private void CancelConversion(MusicSlotItem item)
    {
        if (_conversions.Remove(item, out var running))
        {
            running.Cancellation.Cancel();
            item.IsProcessing = false;
            item.TargetVolumeOffsetDb = null;
        }
    }

    public bool SongFileExists(string path) => fileSystem.File.Exists(path);

    /// <summary>The folder holding the song file of <paramref name="item"/>, or null when it is gone.</summary>
    public string? SourceFolderOf(MusicSlotItem item)
    {
        if (item.Current is not { } entry)
            return null;
        var folder = fileSystem.Path.GetDirectoryName(entry.SourcePath);
        return !string.IsNullOrEmpty(folder) && fileSystem.Directory.Exists(folder) ? folder : null;
    }

    /// <summary>Links rows that are the same course on different consoles, so one song can go to all of them.</summary>
    private void AssignFamilies()
    {
        var families = _items
            .Where(item => item.Slot.Category is MusicCategory.RetroRewindTrack or MusicCategory.Course or MusicCategory.RetroRewindArena or MusicCategory.Arena)
            .GroupBy(item => TrackNames.FamilyKey(item.Slot.Name) ?? item.Slot.Key)
            .Where(group => group.Count() > 1);
        foreach (var family in families)
        {
            var members = family.OrderBy(item => item.Slot.Order).ToList();
            var name = TrackNames.DisplayFamily(members[0].Slot.Name) ?? members[0].Name;
            foreach (var member in members)
            {
                member.Family = members;
                member.FamilyName = name;
            }
        }
    }

    /// <summary>Notes which rows get their music from a music pack installed on the Patches page.</summary>
    private async Task RefreshPacksAsync()
    {
        var packs = await Task.Run(service.FindPackMusic);
        foreach (var item in _items)
            item.Pack = item.Slot.FileNames.Select(name => packs.GetValueOrDefault(name)).FirstOrDefault(pack => pack != null);
    }

    private Bitmap? LoadCover(CustomMusicEntry entry)
    {
        var path = service.GetCoverPath(entry);
        if (path == null)
            return null;
        try
        {
            using var stream = fileSystem.File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, 96);
        }
        catch (Exception)
        {
            // Unusual or broken artwork just falls back to the music note.
            return null;
        }
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Changes to Current, Saved or IsProcessing refresh every row property at once, Current included;
        // reacting to that one name avoids recomputing the page state three times per change.
        if (e.PropertyName == nameof(MusicSlotItem.Current))
            NotifyState();
    }

    private void RefreshRowsIfSortedByChanges()
    {
        if (SelectedSort?.Mode == MusicSortMode.CustomFirst)
            RefreshRows();
    }

    public void RefreshRows()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshRows);
            return;
        }

        var visible = _items.Where(item => item.Matches(SearchText)).ToList();
        var mode = SelectedSort?.Mode ?? MusicSortMode.Default;
        var rows = new List<object>(visible.Count + 64);

        switch (mode)
        {
            case MusicSortMode.Alphabetical:
                rows.AddRange(visible.OrderBy(item => item.SortName, StringComparer.CurrentCultureIgnoreCase));
                break;
            case MusicSortMode.CustomFirst:
            {
                var custom = visible.Where(item => item.IsCustom || item.IsProcessing).OrderBy(item => item.SortName, StringComparer.CurrentCultureIgnoreCase).ToList();
                var original = visible.Where(item => !item.IsCustom && !item.IsProcessing).OrderBy(item => item.Slot.Order).ToList();
                if (custom.Count > 0)
                {
                    rows.Add(new MusicGroupHeader(t("custom_music.group.customised", custom.Count), true));
                    rows.AddRange(custom);
                }

                if (original.Count > 0)
                {
                    rows.Add(new MusicGroupHeader(t("custom_music.group.original"), rows.Count == 0));
                    rows.AddRange(original);
                }

                break;
            }
            case MusicSortMode.ByType:
            {
                // Menu and lobby music first: it is a handful of rows that otherwise sit below every race track.
                var groups = visible
                    .GroupBy(item => TypeGroup(item.Slot.Category))
                    .OrderBy(group => group.Key)
                    .Select(group => (Title: TypeGroupTitle(group.Key), Items: group.OrderBy(item => item.Slot.Order).ToList()));
                foreach (var (title, items) in groups)
                {
                    rows.Add(new MusicGroupHeader(t(title, items.Count), rows.Count == 0));
                    rows.AddRange(items);
                }

                break;
            }
            default:
            {
                string? lastHeader = null;
                foreach (var item in visible.OrderBy(item => item.Slot.Order))
                {
                    var header = GroupTitle(item.Slot);
                    if (header != lastHeader)
                    {
                        rows.Add(new MusicGroupHeader(header, rows.Count == 0));
                        lastHeader = header;
                    }

                    rows.Add(item);
                }

                break;
            }
        }

        // One reset instead of hundreds of single adds keeps typing in the search box smooth.
        Rows.ReplaceAll(rows);
        Notify(nameof(HasNoResults));
    }

    private enum MusicTypeGroup
    {
        Menus,
        Races,
        Arenas,
        Jingles,
    }

    private static MusicTypeGroup TypeGroup(MusicCategory category) =>
        category switch
        {
            MusicCategory.Menu => MusicTypeGroup.Menus,
            MusicCategory.RetroRewindTrack or MusicCategory.Course => MusicTypeGroup.Races,
            MusicCategory.RetroRewindArena or MusicCategory.Arena => MusicTypeGroup.Arenas,
            _ => MusicTypeGroup.Jingles,
        };

    private static string TypeGroupTitle(MusicTypeGroup group) =>
        group switch
        {
            MusicTypeGroup.Menus => "custom_music.group.menus",
            MusicTypeGroup.Races => "custom_music.group.races",
            MusicTypeGroup.Arenas => "custom_music.group.arenas",
            _ => "custom_music.group.jingles",
        };

    private static string GroupTitle(MusicSlot slot)
    {
        var category = slot.Category switch
        {
            MusicCategory.RetroRewindTrack => t("custom_music.category.retro_rewind"),
            MusicCategory.RetroRewindArena => t("custom_music.category.retro_rewind_arenas"),
            MusicCategory.Course => t("custom_music.category.original_courses"),
            MusicCategory.Arena => t("custom_music.category.original_arenas"),
            MusicCategory.Menu => t("custom_music.category.menus"),
            _ => t("custom_music.category.jingles"),
        };
        return slot.Category is MusicCategory.Menu ? category : $"{category} · {slot.Group}";
    }

    private void PersistPreferences()
    {
        if (!_preferencesReady || _selectedSort == null || _selectedFinalLap == null || _selectedVolume == null)
            return;
        var sort = _selectedSort.Mode;
        var finalLap = _selectedFinalLap.Style;
        var volume = _selectedVolume.Decibels;
        _ = Task.Run(() => service.SavePreferences(sort, finalLap, volume));
    }

    private void NotifyState()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(NotifyState);
            return;
        }

        Notify(nameof(CustomCount));
        Notify(nameof(HasCustomMusic));
        Notify(nameof(PendingCount));
        Notify(nameof(HasPendingChanges));
        Notify(nameof(IsConverting));
        Notify(nameof(CanSave));
        Notify(nameof(PendingText));
        Notify(nameof(Summary));
        Notify(nameof(IsRetroRewindMissing));
        Notify(nameof(IsRetroRewindUnreadable));
        Notify(nameof(HasNoResults));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
