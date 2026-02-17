using Avalonia.Interactivity;
using WheelWizard.Models;
using WheelWizard.Views.Popups.Base;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Popups;

public partial class AddFriendConfirmationWindow : PopupContent
{
    private TaskCompletionSource<bool> _tcs = new();

    public AddFriendConfirmationWindow()
        : base(true, false, true, "Add Friend")
    {
        InitializeComponent();
    }

    public AddFriendConfirmationWindow(PlayerProfileResponse profile, Mii friendMii)
        : this()
    {
        FriendName = string.IsNullOrWhiteSpace(profile.Name) ? "Unknown player" : profile.Name;
        FriendCode = profile.FriendCode;
        FriendVr = profile.Vr;
        FriendMii = friendMii;

        DataContext = this;
    }

    public string FriendName { get; private set; } = "Unknown player";
    public string FriendCode { get; private set; } = string.Empty;
    public int FriendVr { get; private set; }
    public Mii? FriendMii { get; private set; }

    public Task<bool> AwaitAnswer()
    {
        _tcs = new();
        Show();
        return _tcs.Task;
    }

    private void AddFriend_OnClick(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(true);
        Close();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(false);
        Close();
    }

    protected override void BeforeClose()
    {
        _tcs.TrySetResult(false);
    }
}
