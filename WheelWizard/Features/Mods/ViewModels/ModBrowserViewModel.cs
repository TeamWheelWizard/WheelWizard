using System.Collections.ObjectModel;
using System.ComponentModel;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.Domain;

namespace WheelWizard.Mods.ViewModels;

public sealed record ModBrowserItem(GameBananaModPreview Mod, ModPreviewViewModel Preview);

public sealed class ModBrowserViewModel(IGameBananaSingletonService api, IGameBananaMediaService media) : INotifyPropertyChanged
{
    private CancellationTokenSource? _search;
    private string _query = "";
    private bool _patchesOnly;
    private int _page;
    private bool _hasMore;
    private readonly Queue<GameBananaModPreview> _pending = new();
    private readonly HashSet<int> _seen = [];
    private GameBananaModType? _modType;

    public ObservableCollection<GameBananaModType> ModTypes { get; } =
        [
            new()
            {
                Id = 0,
                Name = t("mod_browser.all_types"),
                Url = "",
            },
        ];
    public GameBananaModType? ModType { get; set; }
    public bool DraftPatchesOnly { get; set; }
    public GameBananaModType? DraftModType { get; set; }
    public bool TypesLoading { get; private set; }
    public string? TypesError { get; private set; }
    public int ActiveFilterCount => (PatchesOnly ? 1 : 0) + (ModType is { Id: > 0 } ? 1 : 0);
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

    public async Task PrepareFiltersAsync()
    {
        DraftPatchesOnly = PatchesOnly;
        DraftModType = ModType ?? ModTypes[0];
        Notify();
        if (ModTypes.Count > 1 || TypesLoading)
            return;
        TypesLoading = true;
        TypesError = null;
        Notify();
        var response = await api.GetModTypes();
        if (response.IsSuccess)
        {
            foreach (var type in response.Value.Where(type => !type.IsObsolete).OrderBy(type => type.Name))
                ModTypes.Add(type);
        }
        else
            TypesError = t("mod_browser.types_error");
        TypesLoading = false;
        Notify();
    }

    public Task ApplyFiltersAsync()
    {
        PatchesOnly = DraftPatchesOnly;
        ModType = DraftModType;
        return SearchAsync();
    }

    public async Task SearchAsync()
    {
        Deactivate();
        Error = null;
        _page = 0;
        _query = SearchText.Trim();
        _patchesOnly = PatchesOnly;
        _modType = ModType is { Id: > 0 } ? ModType : null;
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
                var response = await api.GetModSearchResults(_query, _page + 1, _modType?.Id).WaitAsync(search.Token);
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
                        || (_modType != null && mod.RootCategory.ProfileUrl != _modType.Url)
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
                Results.Add(new(mod, new ModPreviewViewModel(mod.Id, api, media, url)));
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

    public void Deactivate()
    {
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
