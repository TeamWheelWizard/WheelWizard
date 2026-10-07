using Avalonia.Interactivity;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs.Base;
using WheelWizard.Views.Shell;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Dialogs;

public partial class MessageBoxWindow : PopupContent
{
    public enum MessageType
    {
        Error,
        Warning,
        Message,
    }

    private MessageType messageType = MessageType.Message;

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

    public MessageBoxWindow SetWindowTitle(string title)
    {
        Window.WindowTitle = title;
        return this;
    }

    public MessageBoxWindow SetLink(string url)
    {
        MessageLink.Text = url;
        MessageLink.Tag = url;
        MessageLink.IsVisible = true;
        return this;
    }

    private void MessageLink_OnClick(object? sender, EventArgs e) => ViewUtils.OpenLink((string)MessageLink.Tag!);

    public MessageBoxWindow SetTag(string extraText)
    {
        MessageTag.Text = extraText;
        MessageTagBlock.IsVisible = true;
        return this;
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
