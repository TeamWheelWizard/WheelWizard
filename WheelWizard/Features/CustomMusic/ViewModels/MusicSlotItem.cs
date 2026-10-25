using System.ComponentModel;
using Avalonia.Media.Imaging;
using WheelWizard.CustomMusic.Audio;
using WheelWizard.CustomMusic.Catalog;
using WheelWizard.CustomMusic.Library;

namespace WheelWizard.CustomMusic.ViewModels;

/// <summary>A section heading in the music list (a cup, "Menus", ...).</summary>
public sealed class MusicGroupHeader(string title, bool isFirst)
{
    public string Title { get; } = title;

    /// <summary>The first heading sits flush with the top of the list.</summary>
    public bool IsFirst { get; } = isFirst;
}

public enum ItemPreviewState
{
    None,

    /// <summary>The music is being read (the original may come out of the disc image).</summary>
    Loading,
    Playing,
    Paused,
}

/// <summary>One row in the custom music list.</summary>
public sealed class MusicSlotItem(MusicSlot slot) : INotifyPropertyChanged
{
    private CustomMusicEntry? _saved;
    private CustomMusicEntry? _current;
    private bool _isProcessing;
    private double _progress;
    private string? _error;
    private Bitmap? _cover;
    private bool _isDropTarget;
    private ItemPreviewState _previewState;
    private double? _targetVolumeOffsetDb;
    private PackMusic? _pack;
    private bool _canPreviewOriginal;

    public MusicSlot Slot { get; private set; } = slot;
    public string Name => Slot.Name;

    /// <summary>What is installed in the game right now.</summary>
    public CustomMusicEntry? Saved
    {
        get => _saved;
        set => Set(ref _saved, value);
    }

    /// <summary>What will be installed after saving (null = the original music).</summary>
    public CustomMusicEntry? Current
    {
        get => _current;
        set => Set(ref _current, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => Set(ref _isProcessing, value);
    }

    public double Progress
    {
        get => _progress;
        set
        {
            // Raised many times per conversion: only the properties that show it change.
            if (_progress.Equals(value))
                return;
            _progress = value;
            Raise(nameof(Progress), nameof(Subtitle));
        }
    }

    public string? Error
    {
        get => _error;
        set => Set(ref _error, value);
    }

    public Bitmap? Cover
    {
        get => _cover;
        set
        {
            if (ReferenceEquals(_cover, value))
                return;
            _cover?.Dispose();
            _cover = value;
            Changed();
        }
    }

    /// <summary>Highlighted while a file is dragged over this row.</summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (_isDropTarget == value)
                return;
            _isDropTarget = value;
            Raise(nameof(IsDropTarget));
        }
    }

    /// <summary>Other rows for the same course on other consoles ("SNES Rainbow Road", "Rainbow Road", ...), this one included.</summary>
    public IReadOnlyList<MusicSlotItem> Family { get; set; } = [];

    /// <summary>The course name shared by <see cref="Family"/> ("Rainbow Road").</summary>
    public string FamilyName { get; set; } = string.Empty;

    public bool HasFamily => Family.Count > 1;

    public string FamilyMenuText => HasFamily ? t("custom_music.menu.use_for_family", new { amount = Family.Count, name = FamilyName }) : string.Empty;

    /// <summary>The music pack (another enabled patch) that provides this music when no custom song is set.</summary>
    public PackMusic? Pack
    {
        get => _pack;
        set => Set(ref _pack, value);
    }

    /// <summary>The volume adjustment a running conversion uses (shown while converting), or null.</summary>
    public double? TargetVolumeOffsetDb
    {
        get => _targetVolumeOffsetDb;
        set => Set(ref _targetVolumeOffsetDb, value);
    }

    /// <summary>Whether this row is playing in the preview player (either the original or the custom song).</summary>
    public ItemPreviewState PreviewState
    {
        get => _previewState;
        set
        {
            if (_previewState == value)
                return;
            _previewState = value;
            Raise(nameof(PreviewState), nameof(IsPreviewing), nameof(ShowPlayButton));
        }
    }

    /// <summary>Whether the original music of this row is a file that can be previewed.</summary>
    public bool CanPreviewOriginal
    {
        get => _canPreviewOriginal;
        set => Set(ref _canPreviewOriginal, value);
    }

    public bool IsPreviewing => PreviewState != ItemPreviewState.None;
    public bool ShowPlayButton => (CanPreviewOriginal || Pack != null) && !IsPreviewing;

    public bool IsCustom => Current != null;
    public bool IsPending => Saved?.StorageId != Current?.StorageId;
    public bool HasError => Error != null && !IsProcessing;
    public bool HasCover => Cover != null && !IsProcessing;

    /// <summary>The default music-note tile: the original music, or a custom song without artwork.</summary>
    public bool ShowNoteIcon => !IsProcessing && Cover == null;

    public string Subtitle
    {
        get
        {
            if (IsProcessing)
            {
                var converting = t("custom_music.converting", new { percent = (int)Math.Round(Progress * 100) });
                return TargetVolumeOffsetDb is { } target && Math.Abs(target) >= 0.05 ? $"{converting} · {FormatDb(target)}" : converting;
            }
            if (Error != null)
                return Error;
            if (Current is { } entry)
            {
                var artist = string.IsNullOrWhiteSpace(entry.Artist) ? null : entry.Artist;
                var text = artist == null ? $"♪ {entry.DisplayTitle}" : $"♪ {entry.DisplayTitle} — {artist}";
                if (Math.Abs(entry.VolumeOffsetDb) >= 0.05)
                    text = $"{text} · {FormatDb(entry.VolumeOffsetDb)}";
                // The song's peaks held it below the level asked for: say so on the row, not only in the tooltip.
                if (entry.LoudnessVersusOriginalDb is { } reached && entry.VolumeOffsetDb - reached > 1.5)
                    text = $"{text} · {t("custom_music.volume_short", new { amount = (entry.VolumeOffsetDb - reached).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) })}";
                // The volume could not be matched to the original, so it is a guess; say so.
                return entry.ReferenceNote == null ? text : $"{text} · {t("custom_music.volume_estimated")}";
            }

            if (Pack != null)
                return t("custom_music.from_pack", new { pack = Pack.ModTitle });
            return Slot.Detail ?? Slot.FileNames.FirstOrDefault() ?? string.Empty;
        }
    }

    /// <summary>Hover details: which files are written and how loud the song was made.</summary>
    public string Details
    {
        get
        {
            var lines = new List<string> { string.Join(", ", Slot.FileNames.Take(3)) + (Slot.FileNames.Count > 3 ? ", …" : string.Empty) };
            if (Slot.HasFinalLap)
                lines.Add(t("custom_music.details.final_lap", new { file = Slot.FinalLapFileNames[0] }));
            if (Current is { } entry)
            {
                lines.Add(t("custom_music.details.source", new { file = PathText.FileName(entry.SourcePath) }));
                var reference = entry.ReferenceSource switch
                {
                    MusicReferenceSource.RetroRewind => t("custom_music.reference.retro_rewind"),
                    MusicReferenceSource.GameDisc => t("custom_music.reference.game_disc"),
                    _ => t("custom_music.reference.estimate"),
                };
                lines.Add(
                    t(
                        "custom_music.details.loudness",
                        new
                        {
                            source = entry.SourceLufs.ToString("0.0"),
                            target = (entry.OutputLufs ?? entry.TargetLufs).ToString("0.0"),
                            original = (entry.ReferenceLufs ?? entry.TargetLufs - entry.VolumeOffsetDb).ToString("0.0"),
                            reference,
                        }
                    )
                );
                if (entry.PulsarVolume > 0)
                    lines.Add(t("custom_music.details.volume_byte", new { volume = entry.PulsarVolume, original = entry.ReferenceVolume }));
                if (Math.Abs(entry.VolumeOffsetDb) >= 0.05)
                {
                    lines.Add(t("custom_music.details.volume_offset", new { amount = FormatDb(entry.VolumeOffsetDb) }));
                    // Louder than the peaks allow is not possible without distortion; show what was reached.
                    if (entry.LoudnessVersusOriginalDb is { } reached && entry.VolumeOffsetDb - reached > 1.0)
                        lines.Add(t("custom_music.details.volume_limited", new { amount = FormatDb(reached) }));
                }
                if (entry.ReferenceNote != null)
                    lines.Add(t("custom_music.details.reference_problem", new { message = entry.ReferenceNote }));
                var loop = entry.Loop switch
                {
                    LoopKind.FromFile => "custom_music.details.loop_file",
                    LoopKind.Found => "custom_music.details.loop_found",
                    LoopKind.Crossfade => "custom_music.details.loop_crossfade",
                    _ => null,
                };
                if (loop != null)
                    lines.Add(t(loop, new { start = FormatTime(entry.LoopStartSeconds), end = FormatTime(entry.LoopEndSeconds) }));
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public string SortName => Name;

    private static string FormatTime(double seconds) => TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    private static string FormatDb(double decibels) => decibels.ToString("+0.#;-0.#", System.Globalization.CultureInfo.CurrentCulture) + " dB";

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;
        return Contains(Name) || Contains(Slot.Group) || Contains(Slot.SearchText) || Contains(Current?.Title) || Contains(Current?.Artist);

        bool Contains(string? text) => text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    }

    public void UpdateSlot(MusicSlot slot)
    {
        Slot = slot;
        Changed();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        Changed();
    }

    private static readonly string[] AllProperties =
    [
        nameof(Slot),
        nameof(Name),
        nameof(Saved),
        nameof(Current),
        nameof(IsProcessing),
        nameof(Progress),
        nameof(Error),
        nameof(Cover),
        nameof(IsDropTarget),
        nameof(TargetVolumeOffsetDb),
        nameof(Pack),
        nameof(PreviewState),
        nameof(CanPreviewOriginal),
        nameof(IsPreviewing),
        nameof(ShowPlayButton),
        nameof(IsCustom),
        nameof(IsPending),
        nameof(HasError),
        nameof(HasCover),
        nameof(ShowNoteIcon),
        nameof(Subtitle),
        nameof(Details),
    ];

    private void Raise(params string[] properties)
    {
        var handler = PropertyChanged;
        if (handler == null)
            return;
        foreach (var property in properties)
            handler(this, new PropertyChangedEventArgs(property));
    }

    /// <summary>Most properties derive from each other; refreshing them all keeps the bindings simple.</summary>
    private void Changed()
    {
        var handler = PropertyChanged;
        if (handler == null)
            return;
        foreach (var property in AllProperties)
            handler(this, new PropertyChangedEventArgs(property));
    }
}
