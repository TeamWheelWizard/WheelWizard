using Avalonia.Interactivity;
using WheelWizard.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Popups.Base;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Popups.Generic;

public partial class MessageBoxWindow : PopupContent
{
    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    public enum MessageType
    {
        Error,
        Warning,
        Message,
    }

    private MessageType messageType = MessageType.Message;
    private string? _doNotShowAgainKey;

    public MessageBoxWindow()
        : base(true, false, true, t("attribute.message"))
    {
        InitializeComponent();
        SetMessageType(messageType);
    }

    public MessageBoxWindow SetMessageType(MessageType newType)
    {
        messageType = newType;
        CancelButton.Variant = messageType == MessageType.Message ? Button.ButtonsVariantType.Primary : Button.ButtonsVariantType.Default;

        Window.WindowTitle = messageType.ToString();
        TitleBorder.Classes.Add(messageType.ToString());
        return this;
    }

    public MessageBoxWindow SetTitleText(string mainText)
    {
        MessageTitleBlock.Text = mainText;
        return this;
    }

    public MessageBoxWindow SetInfoText(string extraText)
    {
        MessageInformationBlock.Text = extraText;
        return this;
    }

    public MessageBoxWindow SetTag(string extraText)
    {
        MessageTag.Text = extraText;
        MessageTagBlock.IsVisible = true;
        return this;
    }

    public MessageBoxWindow SetDoNotShowAgain(string warningKey)
    {
        if (string.IsNullOrWhiteSpace(warningKey))
            throw new ArgumentException("A warning key is required.", nameof(warningKey));

        _doNotShowAgainKey = warningKey;
        DoNotShowAgainCheckBox.IsVisible = true;
        DisableOpen(SettingsService.Get<string[]>(SettingsService.DO_NOT_SHOW_AGAIN).Contains(warningKey, StringComparer.Ordinal));
        return this;
    }

    protected override void BeforeClose()
    {
        if (_doNotShowAgainKey == null || DoNotShowAgainCheckBox.IsChecked != true)
            return;

        var hiddenWarnings = SettingsService.Get<string[]>(SettingsService.DO_NOT_SHOW_AGAIN);
        if (hiddenWarnings.Contains(_doNotShowAgainKey, StringComparer.Ordinal))
            return;

        SettingsService.Set(SettingsService.DO_NOT_SHOW_AGAIN, hiddenWarnings.Append(_doNotShowAgainKey).ToArray());
    }

    protected override void BeforeOpen() => PlaySound(messageType);

    private static void PlaySound(MessageType messageType)
    {
        switch (messageType)
        {
            //todo: fix sounds for all platforms
            case MessageType.Error:
                // SystemSounds.Hand.Play();
                break;
            case MessageType.Warning:
                // SystemSounds.Exclamation.Play();
                break;
            case MessageType.Message
            or _:
                // SystemSounds.Hand.Play();
                break;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => Close();
}
