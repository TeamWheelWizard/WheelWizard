using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using WheelWizard.Views.Patterns;

namespace WheelWizard.Views;

public partial class Layout
{
    public static readonly StyledProperty<double> SidebarWidthProperty = AvaloniaProperty.Register<Layout, double>(
        nameof(SidebarWidth),
        221
    );

    public double SidebarWidth
    {
        get => GetValue(SidebarWidthProperty);
        set => SetValue(SidebarWidthProperty, value);
    }

    private bool _sidebarCollapsed;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SidebarWidthProperty && CompleteGrid != null)
        {
            CompleteGrid.ColumnDefinitions[0].Width = new GridLength(SidebarWidth);
            // Follow the same animated width so neither image jumps when labels are hidden.
            var collapsedProgress = Math.Clamp((221 - SidebarWidth) / (221 - 64), 0, 1);
            SidebarMii.Margin = new Thickness(-7 - 10 * collapsedProgress, 0, 0, 1);
            var logoMargin = TitleLabel.Margin;
            TitleLabel.Margin = new Thickness(10 + 6.5 * collapsedProgress, logoMargin.Top, 0, logoMargin.Bottom);
            LiveStatusBorder.Margin = new Thickness(10 + 8 * collapsedProgress, 0, 0, 12);
        }
    }

    private async void SidebarToggle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!SidebarToggle.IsEnabled)
            return;

        SettingsService.SIDEBAR_COLLAPSED.Set(!_sidebarCollapsed);
        await SetSidebarCollapsedAsync(!_sidebarCollapsed, SettingsService.ENABLE_ANIMATIONS.Get());
    }

    private async Task SetSidebarCollapsedAsync(bool collapsed, bool animate)
    {
        SidebarToggle.IsEnabled = false;
        _sidebarCollapsed = collapsed;
        var duration = TimeSpan.FromMilliseconds(240);
        SidebarInfoContextMenu.Close();
        SidebarBottomBar.IsHitTestVisible = false;
        SupportUsButton.IsHitTestVisible = false;
        await AnimateSidebarDetails(appearing: false, animate);
        SupportUsButton.IsVisible = false;
        VersionTagBorder.IsVisible = !_sidebarCollapsed;
        CollapsedSupportMenuItem.IsVisible = _sidebarCollapsed;

        // Keep labels hidden while the column is narrow to avoid wrapping/reflow on expansion.
        ApplySidebarContents(compact: true);

        var chevron = (RotateTransform)SidebarChevron.RenderTransform!;
        chevron.Transitions = animate
            ? new Transitions
            {
                new DoubleTransition { Property = RotateTransform.AngleProperty, Duration = duration },
            }
            : null;
        chevron.Angle = _sidebarCollapsed ? 180 : 0;
        var startWidth = SidebarWidth;
        var targetWidth = _sidebarCollapsed ? 64d : 221d;
        // Set the underlying value first so completing the animation cannot snap back.
        SidebarWidth = targetWidth;
        if (animate)
        {
            var resize = new Animation
            {
                Duration = duration,
                Easing = new CubicEaseInOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(SidebarWidthProperty, startWidth) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(SidebarWidthProperty, targetWidth) } },
                },
            };
            await resize.RunAsync(this);
        }
        ApplySidebarContents(_sidebarCollapsed);

        SidebarBottomBar.ColumnDefinitions = new ColumnDefinitions(_sidebarCollapsed ? "*" : "28,8,28,8,28,8,*");
        SidebarBottomBar.RowDefinitions = new RowDefinitions(_sidebarCollapsed ? "28,8,28,8,28" : "Auto");
        SidebarBottomBar.Margin = _sidebarCollapsed ? new Thickness(0, 0, 0, 12) : new Thickness(10, 0, 13, 12);
        Grid.SetRowSpan(SidebarBottomBar, _sidebarCollapsed ? 2 : 1);
        Grid.SetRow(SidebarBottomBar, _sidebarCollapsed ? 4 : 5);
        Grid.SetColumn(SidebarInfoButton, _sidebarCollapsed ? 0 : 2);
        Grid.SetRow(SidebarInfoButton, _sidebarCollapsed ? 2 : 0);
        Grid.SetColumn(SidebarSettingsButton, _sidebarCollapsed ? 0 : 4);
        Grid.SetRow(SidebarSettingsButton, 0);
        Grid.SetColumn(VersionTagBorder, _sidebarCollapsed ? 0 : 6);
        SupportUsButton.IsVisible = !_sidebarCollapsed;
        ToolTip.SetPlacement(LiveStatusBorder, _sidebarCollapsed ? PlacementMode.Right : PlacementMode.TopEdgeAlignedLeft);
        ToolTip.SetPlacement(SidebarSettingsButton, _sidebarCollapsed ? PlacementMode.Right : PlacementMode.Top);

        await AnimateSidebarDetails(appearing: true, animate);
        SidebarBottomBar.IsHitTestVisible = true;
        SupportUsButton.IsHitTestVisible = true;
        SidebarToggle.IsEnabled = true;
    }

    private async Task AnimateSidebarDetails(bool appearing, bool animate)
    {
        List<Control> controls = [SidebarBottomBar];
        if (SupportUsButton.IsVisible)
            controls.Add(SupportUsButton);
        // Headings leave with the expanded footer and return only after expanding.
        if (appearing != _sidebarCollapsed)
            controls.AddRange(
                CompleteGrid
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(text =>
                        text.Classes.Contains("SidebarSectionText") && (text.Parent == CompleteGrid || text.Parent == SidePanelButtons)
                    )
            );
        foreach (var control in controls)
            control.Opacity = appearing ? 1 : 0;
        if (!animate)
            return;

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(150),
            Easing = appearing ? new CubicEaseOut() : new CubicEaseIn(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(OpacityProperty, appearing ? 0d : 1d),
                        new Setter(TranslateTransform.YProperty, appearing ? 8d : 0d),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(OpacityProperty, appearing ? 1d : 0d),
                        new Setter(TranslateTransform.YProperty, appearing ? 0d : 8d),
                    },
                },
            },
        };
        await Task.WhenAll(controls.Select(control => animation.RunAsync(control)));
    }

    private void ApplySidebarContents(bool compact)
    {
        TitleLabel.Text = compact ? string.Empty : BrandingService.Branding.DisplayName;
        SidebarCurrentUserProfile.Opacity = compact ? 0 : 1;
        // Keep the same bottom anchor and overflowing portrait in both sidebar sizes.
        SidebarMii.Width = SidebarMii.Height = 80;
        SidebarMii.HorizontalAlignment = HorizontalAlignment.Left;
        SidebarMii.VerticalAlignment = VerticalAlignment.Bottom;

        OtherSectionText.IsVisible = TestingButton.IsVisible;
        foreach (var button in SidePanelButtons.Children.OfType<SidebarRadioButton>())
        {
            button.Classes.Set("compact", compact);
            ToolTip.SetPlacement(button, compact ? PlacementMode.Right : PlacementMode.Top);
        }
    }
}
