using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using WheelWizard.Views.Components;
using WheelWizard.Views.Pages.KitchenSink;

namespace WheelWizard.Views.Pages;

public partial class KitchenSinkPage : UserControlBase
{
    private readonly SectionDefinition[] _sections =
    [
        SectionDefinition.Create<KitchenSinkTextStylesPage>(),
        SectionDefinition.Create<KitchenSinkToggleButtonsPage>(),
        SectionDefinition.Create<KitchenSinkInputFieldsPage>(),
        SectionDefinition.Create<KitchenSinkDropdownsPage>(),
        SectionDefinition.Create<KitchenSinkButtonsPage>(),
        SectionDefinition.Create<KitchenSinkIconLabelsPage>(),
        SectionDefinition.Create<KitchenSinkStateBoxesPage>(),
        SectionDefinition.Create<KitchenSinkIconsPage>(),
    ];

    // Add more configurable section groups here.
    private readonly SectionCollectionDefinition[] _sectionCollections =
    [
        SectionCollectionDefinition.Create(
            "Basic Components",
            typeof(KitchenSinkTextStylesPage),
            typeof(KitchenSinkToggleButtonsPage),
            typeof(KitchenSinkInputFieldsPage),
            typeof(KitchenSinkDropdownsPage),
            typeof(KitchenSinkButtonsPage),
            typeof(KitchenSinkIconLabelsPage),
            typeof(KitchenSinkStateBoxesPage),
            typeof(KitchenSinkIconsPage)
        ),
    ];

    private readonly Dictionary<string, Border> _allSectionContainersById = [];
    private readonly List<Border> _allSectionBorders = [];
    private Border? _singleSectionBorder;
    private bool _isInitializing;
    private bool _useNeutral900Blocks = true;

    public KitchenSinkPage()
    {
        InitializeComponent();
        BuildAllSections();
        PopulateSections();
        ApplyBlockBackgroundMode();
    }

    private void BuildAllSections()
    {
        _allSectionContainersById.Clear();
        _allSectionBorders.Clear();
        AllSectionsContainer.Children.Clear();

        foreach (var section in _sections)
        {
            var sectionView = section.CreatePage();
            var sectionContainer = CreateSectionContainer(section.Label, section.Tooltip, sectionView, out var sectionBorder);
            _allSectionContainersById[section.Id] = sectionContainer;
            _allSectionBorders.Add(sectionBorder);
            AllSectionsContainer.Children.Add(sectionContainer);
        }
    }

    private static Border CreateSectionContainer(
        string sectionName,
        string? sectionTooltip,
        Control sectionContent,
        out Border sectionBorder
    )
    {
        var header = new FormFieldLabel { Text = sectionName, TipText = sectionTooltip ?? string.Empty };
        var divider = new Border();
        divider.Classes.Add("KitchenSinkSectionDivider");
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(header);
        body.Children.Add(divider);
        body.Children.Add(sectionContent);

        sectionBorder = new Border { Child = body };
        sectionBorder.Classes.Add("KitchenSinkSectionBlock");
        sectionBorder.VerticalAlignment = VerticalAlignment.Top;
        sectionBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
        return sectionBorder;
    }

    private void PopulateSections()
    {
        _isInitializing = true;
        SectionDropdown.Items.Clear();

        foreach (var collection in _sectionCollections)
            SectionDropdown.Items.Add(SectionDropdownItem.ForCollection(collection));

        foreach (var section in _sections)
            SectionDropdown.Items.Add(SectionDropdownItem.ForSection(section));

        SectionDropdown.SelectedIndex = SectionDropdown.Items.Count > 0 ? 0 : -1;
        _isInitializing = false;

        ApplySelectedSection();
    }

    private void SectionDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing)
            return;

        ApplySelectedSection();
    }

    private void BackgroundSwitch_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        _useNeutral900Blocks = BackgroundSwitch.IsChecked == true;
        ApplyBlockBackgroundMode();
    }

    private void ApplySelectedSection()
    {
        var selectedItem = SectionDropdown.SelectedItem as SectionDropdownItem;
        if (selectedItem == null)
        {
            AllSectionsScrollViewer.IsVisible = false;
            SectionContent.IsVisible = false;
            SectionContent.Content = null;
            _singleSectionBorder = null;
            return;
        }

        if (selectedItem.Collection is { } collection)
        {
            ShowCollection(collection);
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedItem.SectionId))
            return;

        ShowSingleSection(selectedItem.SectionId);
    }

    private void ShowCollection(SectionCollectionDefinition collection)
    {
        foreach (var section in _sections)
        {
            if (_allSectionContainersById.TryGetValue(section.Id, out var sectionContainer))
                sectionContainer.IsVisible = collection.Contains(section.Id);
        }

        AllSectionsScrollViewer.IsVisible = true;
        SectionContent.IsVisible = false;
        _singleSectionBorder = null;
        SectionContent.Content = null;

        ApplyBlockBackgroundMode();
    }

    private void ShowSingleSection(string sectionId)
    {
        var sectionIndex = Array.FindIndex(_sections, x => x.Id == sectionId);
        if (sectionIndex < 0)
        {
            SectionContent.Content = null;
            return;
        }

        var section = _sections[sectionIndex];
        var sectionContainer = CreateSectionContainer(section.Label, section.Tooltip, section.CreatePage(), out var sectionBorder);
        _singleSectionBorder = sectionBorder;
        SectionContent.Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Content = sectionContainer,
        };

        AllSectionsScrollViewer.IsVisible = false;
        SectionContent.IsVisible = true;

        ApplyBlockBackgroundMode();
    }

    private void ApplyBlockBackgroundMode()
    {
        foreach (var border in _allSectionBorders)
            ApplyBackgroundClass(border);

        if (_singleSectionBorder != null)
            ApplyBackgroundClass(_singleSectionBorder);
    }

    private void ApplyBackgroundClass(Border border)
    {
        border.Classes.Set("BlockBackground900", _useNeutral900Blocks);
    }

    private readonly record struct SectionDefinition(string Id, string Label, string? Tooltip, Func<KitchenSinkSectionPageBase> CreatePage)
    {
        public static SectionDefinition Create<T>()
            where T : KitchenSinkSectionPageBase, new()
        {
            var metadataInstance = new T();
            return new(typeof(T).Name, metadataInstance.SectionName, metadataInstance.SectionTooltip, () => new T());
        }
    }

    private readonly record struct SectionCollectionDefinition(string Label, HashSet<string> SectionIds)
    {
        public bool Contains(string sectionId) => SectionIds.Contains(sectionId);

        public static SectionCollectionDefinition Create(string label, params Type[] sectionTypes)
        {
            var sectionIds = sectionTypes.Select(static sectionType => sectionType.Name).ToHashSet(StringComparer.Ordinal);
            return new(label, sectionIds);
        }
    }

    private sealed record SectionDropdownItem(string Label, string? SectionId, SectionCollectionDefinition? Collection)
    {
        public static SectionDropdownItem ForCollection(SectionCollectionDefinition collection) => new(collection.Label, null, collection);

        public static SectionDropdownItem ForSection(SectionDefinition section) => new(section.Label, section.Id, null);

        public override string ToString() => Label;
    }
}
