using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using WheelWizard.Views.Components;
using WheelWizard.Views.Pages.KitchenSink;

namespace WheelWizard.Views.Pages;

public partial class KitchenSinkPage : UserControlBase
{
    private readonly SectionDefinition[] _sections =
    [
        SectionDefinition.Create<KitchenSinkTextStylesPage>(),
        SectionDefinition.Create<KitchenSinkToggleButtonsPage>(),
        SectionDefinition.Create<KitchenSinkButtonsPage>(),
    ];

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
        _allSectionBorders.Clear();
        AllSectionsContainer.Children.Clear();

        foreach (var section in _sections)
        {
            var sectionView = section.CreatePage();
            var sectionContainer = CreateSectionContainer(section.Label, section.Tooltip, sectionView, out var sectionBorder);
            _allSectionBorders.Add(sectionBorder);
            AllSectionsContainer.Children.Add(sectionContainer);
        }
    }

    private static StackPanel CreateSectionContainer(
        string sectionName,
        string? sectionTooltip,
        Control sectionContent,
        out Border sectionBorder
    )
    {
        var header = new FormFieldLabel { Text = sectionName, TipText = sectionTooltip ?? string.Empty };

        sectionBorder = new Border { Child = sectionContent };
        sectionBorder.Classes.Add("KitchenSinkSectionBlock");

        var sectionContainer = new StackPanel();
        sectionContainer.Children.Add(header);
        sectionContainer.Children.Add(sectionBorder);
        return sectionContainer;
    }

    private void PopulateSections()
    {
        _isInitializing = true;
        SectionDropdown.Items.Clear();
        SectionDropdown.Items.Add("All");

        foreach (var section in _sections)
            SectionDropdown.Items.Add(section.Label);

        SectionDropdown.SelectedIndex = 0;
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
        var selectedLabel = SectionDropdown.SelectedItem as string ?? "All";
        var showAll = selectedLabel == "All";

        AllSectionsScrollViewer.IsVisible = showAll;
        SectionContent.IsVisible = !showAll;
        _singleSectionBorder = null;

        if (showAll)
        {
            SectionContent.Content = null;
            return;
        }

        var sectionIndex = Array.FindIndex(_sections, x => x.Label == selectedLabel);
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
            Content = sectionContainer,
        };

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

    private readonly record struct SectionDefinition(string Label, string? Tooltip, Func<KitchenSinkSectionPageBase> CreatePage)
    {
        public static SectionDefinition Create<T>()
            where T : KitchenSinkSectionPageBase, new()
        {
            var metadataInstance = new T();
            return new(metadataInstance.SectionName, metadataInstance.SectionTooltip, () => new T());
        }
    }
}
