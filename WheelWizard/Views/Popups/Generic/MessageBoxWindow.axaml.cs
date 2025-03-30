using Avalonia.Interactivity;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Popups.Generic;

public partial class MessageBoxWindow : PopupContent
{
    public enum MessageType
    {
        Error,
        Warning,
        Message
    }
    private MessageType _messageType = MessageType.Message;
    
    public MessageBoxWindow() : base(true, false, true, "Message", new(400, 230))
    {
        InitializeComponent();
        SetMessageType(_messageType);
    }
    
    public MessageBoxWindow SetMessageType(MessageType newType)
    {
        _messageType = newType;
        CancelButton.Variant = _messageType == MessageType.Message ? 
            Button.ButtonsVariantType.Primary: 
            Button.ButtonsVariantType.Default;
        
        Window.WindowTitle = _messageType.ToString();
        TitleBorder.Classes.Add(_messageType.ToString());
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

    protected override void BeforeOpen() => PlaySound(_messageType);
    
    private static void PlaySound(MessageType messageType)
    {
        switch (messageType)
        {
            //todo: fix sounds for all platforms
            case MessageType.Error :
                // SystemSounds.Hand.Play();
                break;
            case MessageType.Warning:
                // SystemSounds.Exclamation.Play();
                break;
        }
    }
    
    private void OkButton_Click(object sender, RoutedEventArgs e) => Close();
}

