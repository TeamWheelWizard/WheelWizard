using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using WheelWizard.Models.MiiImages;
using WheelWizard.Services.Settings;
using WheelWizard.Views.Components.MiiImages;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.Domain.Mii;
using MiiCreatorWindow = WheelWizard.Views.Popups.MiiCreatorTabs.MiiCreatorWindow;

namespace WheelWizard.Views.Components;

public class DetailedProfileBox : TemplatedControl, INotifyPropertyChanged
{
    public static readonly StyledProperty<Mii?> MiiProperty = AvaloniaProperty.Register<DetailedProfileBox, Mii?>(nameof(Mii));

    public Mii? Mii
    {
        get => GetValue(MiiProperty);
        set
        {
            SetValue(MiiProperty, value);
            OnPropertyChanged(nameof(Mii));
        }
    }

    public static readonly StyledProperty<Bitmap?> MiiImageProperty = AvaloniaProperty.Register<DetailedProfileBox, Bitmap?>(
        nameof(MiiImage)
    );

    public Bitmap? MiiImage
    {
        get => GetValue(MiiImageProperty);
        set => SetValue(MiiImageProperty, value);
    }

    public static readonly StyledProperty<bool> IsOnlineProperty = AvaloniaProperty.Register<DetailedProfileBox, bool>(nameof(IsOnline));

    public bool IsOnline
    {
        get => GetValue(IsOnlineProperty);
        set => SetValue(IsOnlineProperty, value);
    }

    public static readonly StyledProperty<string> TotalWonProperty = AvaloniaProperty.Register<DetailedProfileBox, string>(
        nameof(TotalWon)
    );

    public string TotalWon
    {
        get => GetValue(TotalWonProperty);
        set => SetValue(TotalWonProperty, value);
    }

    public static readonly StyledProperty<string> TotalRacesProperty = AvaloniaProperty.Register<DetailedProfileBox, string>(
        nameof(TotalRaces)
    );

    public string TotalRaces
    {
        get => GetValue(TotalRacesProperty);
        set => SetValue(TotalRacesProperty, value);
    }

    public static readonly StyledProperty<string> VrProperty = AvaloniaProperty.Register<DetailedProfileBox, string>(nameof(Vr));

    public string Vr
    {
        get => GetValue(VrProperty);
        set => SetValue(VrProperty, value);
    }

    public static readonly StyledProperty<string> BrProperty = AvaloniaProperty.Register<DetailedProfileBox, string>(nameof(Br));

    public string Br
    {
        get => GetValue(BrProperty);
        set => SetValue(BrProperty, value);
    }

    public static readonly StyledProperty<string> FriendCodeProperty = AvaloniaProperty.Register<DetailedProfileBox, string>(
        nameof(FriendCode)
    );

    public string FriendCode
    {
        get => GetValue(FriendCodeProperty);
        set => SetValue(FriendCodeProperty, value);
    }

    public static readonly StyledProperty<string> UserNameProperty = AvaloniaProperty.Register<DetailedProfileBox, string>(
        nameof(UserName)
    );

    public string UserName
    {
        get => GetValue(UserNameProperty);
        set => SetValue(UserNameProperty, value);
    }

    public static readonly StyledProperty<bool> IsCheckedProperty = AvaloniaProperty.Register<DetailedProfileBox, bool>(nameof(IsChecked));

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public static readonly StyledProperty<EventHandler<RoutedEventArgs>?> OnCheckedProperty = AvaloniaProperty.Register<
        DetailedProfileBox,
        EventHandler<RoutedEventArgs>?
    >(nameof(OnChecked));

    public EventHandler<RoutedEventArgs>? OnChecked
    {
        get => GetValue(OnCheckedProperty);
        set => SetValue(OnCheckedProperty, value);
    }

    public static readonly StyledProperty<EventHandler?> OnRenameProperty = AvaloniaProperty.Register<DetailedProfileBox, EventHandler?>(
        nameof(OnRename)
    );

    public EventHandler? OnRename
    {
        get => GetValue(OnRenameProperty);
        set => SetValue(OnRenameProperty, value);
    }

    public static readonly StyledProperty<Action<string>?> ViewRoomActionProperty = AvaloniaProperty.Register<
        FriendsListItem,
        Action<string>?
    >(nameof(ViewRoomAction));

    public Action<string>? ViewRoomAction
    {
        get => GetValue(ViewRoomActionProperty);
        set => SetValue(ViewRoomActionProperty, value);
    }

    public void ViewRoom(object? sender, RoutedEventArgs e)
    {
        ViewRoomAction.Invoke(FriendCode);
    }

    private void CopyFriendCode(object? obj, EventArgs e)
    {
        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(FriendCode);
    }

    private async void OpenMiiEditor_Click(object? sender, RoutedEventArgs e)
    {
        var miiDbService = App.Services.GetRequiredService<IMiiDbService>();
        var miiCreatorWindow = new MiiCreatorWindow(miiDbService, Mii);
        var updatedMii = await miiCreatorWindow.ShowDialogAsync();
        if (updatedMii != null)
        {
            // User saved changes.
            // The Mii object instance might have been updated directly by the popup,
            // but setting the property ensures Avalonia's binding system updates.
            this.Mii = updatedMii; // Re-assign to trigger property change notifications

            // Optional: Force UI update if needed, although setting Mii should suffice
            // this.InvalidateVisual();
            Console.WriteLine($"Mii '{updatedMii.Name}' updated successfully.");
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var checkBox = e.NameScope.Find<RadioButton>("CheckBox");
        if (checkBox != null)
            checkBox.Checked += OnChecked;

        var viewRoomButton = e.NameScope.Find<Button>("ViewRoomButton");
        if (viewRoomButton != null)
            viewRoomButton.Click += ViewRoom;

        var miiButton = e.NameScope.Find<Button>("PART_MiiButton");
        if (miiButton != null)
            miiButton.Click += OpenMiiEditor_Click;

        var changeNameButton = e.NameScope.Find<IconLabelButton>("EditMiiName");
        if (changeNameButton != null)
            changeNameButton.Click += OnRename;

        var copyFcButton = e.NameScope.Find<IconLabelButton>("CopyFcButton");
        if (copyFcButton != null)
            copyFcButton.Click += CopyFriendCode;

        var miiImageLoader = e.NameScope.Find<MiiImageLoader>("MiiFaceImageLoader");
        var animationsEnabled = (bool)SettingsManager.ENABLE_ANIMATIONS.Get();
        if (miiImageLoader != null && animationsEnabled)
        {
            // We set them all at least one, just to make sure the request is being send.
            // sometimes this still works goofy though, for some reason
            miiImageLoader.ImageVariant = MiiImageVariants.Variant.SLIGHT_SIDE_PROFILE_HOVER;
            miiImageLoader.ImageVariant = MiiImageVariants.Variant.SLIGHT_SIDE_PROFILE_INTERACT;
            miiImageLoader.ImageVariant = MiiImageVariants.Variant.SLIGHT_SIDE_PROFILE_DEFAULT;

            miiImageLoader.PointerEntered += (_, _) => miiImageLoader.ImageVariant = MiiImageVariants.Variant.SLIGHT_SIDE_PROFILE_HOVER;
            miiImageLoader.PointerExited += (_, _) => miiImageLoader.ImageVariant = MiiImageVariants.Variant.SLIGHT_SIDE_PROFILE_DEFAULT;
            miiImageLoader.PointerPressed += (_, _) => miiImageLoader.ImageVariant = MiiImageVariants.Variant.SLIGHT_SIDE_PROFILE_INTERACT;
            miiImageLoader.PointerReleased += (_, _) => miiImageLoader.ImageVariant = MiiImageVariants.Variant.SLIGHT_SIDE_PROFILE_HOVER;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}
