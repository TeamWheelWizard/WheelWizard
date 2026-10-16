using System.Collections.ObjectModel;
using System.ComponentModel;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;

namespace WheelWizard.Mods.ViewModels;

public sealed record ModBrowserItem(GameBananaModPreview Mod, ModPreviewViewModel Preview) : INotifyPropertyChanged
{
    public bool IsInstalled { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateInstalled(bool installed)
    {
        if (IsInstalled == installed)
            return;
        IsInstalled = installed;
        PropertyChanged?.Invoke(this, new(nameof(IsInstalled)));
    }
}

public sealed class ModBrowserViewModel : INotifyPropertyChanged
{
    private CancellationTokenSource? _search;
    private string _query = "";
    private bool _patchesOnly;
    private int _page;
    private bool _hasMore;
    private readonly Queue<GameBananaModPreview> _pending = new();
    private readonly HashSet<int> _seen = [];
    private readonly IGameBananaSingletonService api;
    private readonly IGameBananaMediaService media;
    private readonly IModManager _mods;
    private GameBananaModType[] _types = [],
        _skins = [],
        _ui = [];
    private int? _categoryId;

    public ModBrowserViewModel(IGameBananaSingletonService api, IGameBananaMediaService media, IModManager mods)
    {
        this.api = api;
        this.media = media;
        _mods = mods;
        DraftModTypes.CollectionChanged += (_, _) =>
        {
            if (!ShowSkinFilters)
                DraftSkinTypes.Clear();
            if (!ShowUiFilters)
                DraftUiTypes.Clear();
            Notify();
        };
    }

    public IReadOnlyList<GameBananaModType> ModTypes => GameBananaModCategories.Types;
    public IReadOnlyList<GameBananaModType> SkinTypes => GameBananaModCategories.Skins;
    public IReadOnlyList<GameBananaModType> UiTypes => GameBananaModCategories.Ui;
    public ObservableCollection<GameBananaModType> DraftModTypes { get; } = [];
    public ObservableCollection<GameBananaModType> DraftSkinTypes { get; } = [];
    public ObservableCollection<GameBananaModType> DraftUiTypes { get; } = [];
    public bool DraftPatchesOnly { get; set; }
    public bool ShowSkinFilters => DraftModTypes.Any(type => type.Id == 10080);
    public bool ShowUiFilters => DraftModTypes.Any(type => type.Id == 2083);
    public int ActiveFilterCount => (PatchesOnly ? 1 : 0) + _types.Length + _skins.Length + _ui.Length;
    public bool HasFilters => ActiveFilterCount > 0;

    public ObservableCollection<ModBrowserItem> Results { get; } = [];
    public string SearchText { get; set; } = "";
    public bool PatchesOnly { get; set; }
    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public bool HasError => Error != null;
    public bool IsEmpty => !IsLoading && !HasError && Results.Count == 0;
    public bool CanLoadMore => (_hasMore || _pending.Count > 0) && !IsLoading && !HasError;
    public bool ShowInitialLoading => IsLoading && Results.Count == 0;
    public event PropertyChangedEventHandler? PropertyChanged;

    public Task RetryAsync() => _search == null ? SearchAsync() : LoadMoreAsync();

    public void PrepareFilters()
    {
        DraftPatchesOnly = PatchesOnly;
        DraftModTypes.Clear();
        foreach (var type in _types)
            DraftModTypes.Add(type);
        DraftSkinTypes.Clear();
        foreach (var type in _skins)
            DraftSkinTypes.Add(type);
        DraftUiTypes.Clear();
        foreach (var type in _ui)
            DraftUiTypes.Add(type);
        Notify();
    }

    public Task ApplyFiltersAsync()
    {
        PatchesOnly = DraftPatchesOnly;
        _types = DraftModTypes.ToArray();
        _skins = DraftSkinTypes.ToArray();
        _ui = DraftUiTypes.ToArray();
        return SearchAsync();
    }

    private bool MatchesCategories(GameBananaModPreview mod)
    {
        if (_types.Length == 0)
            return true;
        return _types.Any(type =>
            type.Url == mod.RootCategory.ProfileUrl
            && (
                type.Id switch
                {
                    10080 => _skins.Length == 0 || _skins.Any(skin => skin.Url == mod.SubCategory?.ProfileUrl),
                    2083 => _ui.Length == 0 || _ui.Any(ui => ui.Url == mod.SubCategory?.ProfileUrl),
                    _ => true,
                }
            )
        );
    }

    public async Task SearchAsync()
    {
        Deactivate();
        Error = null;
        _page = 0;
        _query = SearchText.Trim();
        _patchesOnly = PatchesOnly;
        // The API accepts one category: narrow to a parent for several children, or filter the full feed for several parents.
        _categoryId =
            _types.Length != 1
                ? null
                : _types[0].Id switch
                {
                    10080 when _skins.Length == 1 => _skins[0].Id,
                    2083 when _ui.Length == 1 => _ui[0].Id,
                    _ => _types[0].Id,
                };
        _hasMore = true;
        if (_query.Length == 1)
        {
            Error = t("mod_browser.short_search");
            _hasMore = false;
            Notify();
            return;
        }
        if (_patchesOnly && _query.Length == 0)
            _query = "Patches";
        _mods.PropertyChanged += InstalledModsChanged;
        _search = new();
        await LoadMoreAsync();
    }

    public async Task LoadMoreAsync()
    {
        if (_search == null || IsLoading || (!_hasMore && _pending.Count == 0))
            return;
        var search = _search;
        IsLoading = true;
        Error = null;
        Notify();
        try
        {
            // API pages include excluded entries; buffer matching results into even batches.
            while (_pending.Count < 12 && _hasMore)
            {
                var response = await api.GetModSearchResults(_query, _page + 1, _categoryId).WaitAsync(search.Token);
                if (search != _search)
                    return;
                if (response.IsFailure)
                {
                    Error = t("mod_browser.load_error");
                    return;
                }
                _page++;
                _hasMore = !response.Value.MetaData.IsComplete && response.Value.Records.Count > 0;
                foreach (var mod in response.Value.Records)
                {
                    if (
                        mod.ModelName != "Mod"
                        || mod.HasContentRatings
                        || (_patchesOnly && !mod.UsesPatches)
                        || !MatchesCategories(mod)
                        || !_seen.Add(mod.Id)
                    )
                        continue;
                    _pending.Enqueue(mod);
                }
            }
            for (var i = 0; i < 12 && _pending.TryDequeue(out var mod); i++)
            {
                var image = mod.PreviewMedia?.Images?.FirstOrDefault();
                var url = image == null ? "" : $"{image.BaseUrl}/{image.File530 ?? image.File220 ?? image.File}";
                var item = new ModBrowserItem(mod, new ModPreviewViewModel(mod.Id, api, media, url));
                item.UpdateInstalled(_mods.IsModInstalled(mod.Id));
                Results.Add(item);
            }
        }
        catch (OperationCanceledException) when (search.IsCancellationRequested) { }
        finally
        {
            if (search == _search)
            {
                IsLoading = false;
                Notify();
            }
        }
    }

    private void InstalledModsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(IModManager.Mods))
            foreach (var item in Results)
                item.UpdateInstalled(_mods.IsModInstalled(item.Mod.Id));
    }

    public void Deactivate()
    {
        _mods.PropertyChanged -= InstalledModsChanged;
        var previous = _search;
        _search = null;
        previous?.Cancel();
        previous?.Dispose();
        foreach (var item in Results)
            item.Preview.Dispose();
        Results.Clear();
        _pending.Clear();
        _seen.Clear();
        IsLoading = false;
        _hasMore = false;
    }

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
