using WheelWizard.Helpers;
using WheelWizard.Resources.Languages;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Shared.MessageTranslations;

// MAKE SURE EVERY ENUM VALUE HAS A VALUE
// 0xxx = Successes
// 1xxx = Warnings (without tag)
// 2xxx = Warnings (with tag)
// 3xxx = Errors
// ANY OTHER VALUE BELOW 1000 OR ABOVE 3999 is not valid

// When determining if something is an error or a warning, keep in mind the following questions:
// - Is it the user's fault?                                        (e.g. the user entering a wrong path, that's their fault)
// - Is this something that we as WhWz can do anything about?       (e.g. we can't do anything about internet related issues, but a missing file that we should have created is our fault)
// If any of the questions above can be answered with "yes", then it is a warning, otherwise it is an error.

// Note that you can safely re-organize the actual enum numbers. They might be serialized in the future, or screenshots might be taken from the numbers. So those will then be outdated, but thats not a problem
// as longs as wheel wizard it-self never reads and converts the numbers back to the enum values.
public enum MessageTranslation
{
    #region Successes

    Success_StanderdSuccess = 0000,
    Success_PathSettingsSaved = 0001,

    #endregion

    #region Warnings

    // Warning starting with 1 have NO error code displayed, once starting with 2 DO HAVE an error code displayed, like the tag
    // If warning makes sense on its own what the user did wrong, then no tag is needed (1xxx)
    Warning_StanderdWarning = 2000,
    Warning_InvalidPathSettings = 1001,
    Warning_UnkownRendererSelected = 2002,
    Warning_CouldNotFindRoom = 2003,
    Warning_CantDeleteFavMii = 1004,
    Warning_CantViewMod_NotFromBrowser = 1005,
    Warning_CantViewMod_SomethingWrong = 2006,
    Warning_NoMiisFound = 1007,
    Warning_DolphinNotFound = 2008,
    Warning_ModNameCantEmpty = 1009,
    Warning_ModNameInvalid = 1010,
    Warning_UnableToDownloadMod_Files = 1011,

    #endregion

    #region Errors

    // 30xx = random errors
    // 31xx = Mii (related) Errors
    // - 310x = Mii Repository/DB Error
    // - 311x = Mii Serializer Error
    // - 312x = Mii Editor
    // 32xx = External program Errors
    // - 320x = Dolphin related Errors
    Error_StanderdError = 3000,
    Error_ModDownloadFailed = 3001,

    Error_MiiDBAlreadyExists = 3100,
    Error_UpdateMiiDb_InvalidClId = 3101,
    Error_UpdateMiiDb_BlockSizeInvalid = 3102,
    Error_UpdateMiiDb_NoBlockFound = 3103,
    Error_UpdateMiiDb_MiiNotFound = 3104,
    Error_UpdateMiiDb_InvalidMac = 3105,
    Error_UpdateMiiDb_RFLdbNotFound = 3106,
    Error_UpdateMiiDb_CorruptDb = 3107,

    Error_MiiSerializer_MiiNotNull = 3110,
    Error_MiiSerializer_MiiId0 = 3111,
    Error_MiiSerializer_MiiDataLength = 3112,
    Error_MiiSerializer_MiiDataEmpty = 3113,
    Error_MiiSerializer_InvalidMiiData = 3114,
    Error_MiiEditor_CantOpenEditor = 3120,

    Error_FailedCopyMii = 3200,
    Error_FailedInstallDolphin = 3201,

    #endregion
}

public static class MessageTranslationHelper
{
    /// <summary>
    /// Returns the translation related to this message enum
    /// </summary>
    /// <returns>(Title, additional information)</returns>
    public static (string, string?) GetTranslationText(MessageTranslation msg)
    {
        return msg switch
        {
            #region Successes

            MessageTranslation.Success_StanderdSuccess => ("Success", "Completed successfully!"),
            MessageTranslation.Success_PathSettingsSaved => (
                Phrases.MessageSuccess_SettingsSaved_Title,
                Phrases.MessageSuccess_SettingsSaved_Title
            ),

            #endregion

            #region Warnings

            MessageTranslation.Warning_StanderdWarning => ("Warning", "Something went wrong!"),
            MessageTranslation.Warning_InvalidPathSettings => (
                Phrases.MessageWarning_InvalidPaths_Title,
                Phrases.MessageWarning_InvalidPaths_Extra
            ),
            MessageTranslation.Warning_UnkownRendererSelected => ("Unknown renderer selected", "Unknown renderer selected: {$1}"),
            MessageTranslation.Warning_CouldNotFindRoom => (
                "Couldn't find the room",
                "Whoops, could not find the room that this player is supposedly playing in"
            ),
            MessageTranslation.Warning_CantDeleteFavMii => (
                Phrases.MessageWarning_CannotDeleteFavMii_Title,
                Phrases.MessageWarning_CannotDeleteFavMii_Extra
            ),

            MessageTranslation.Warning_CantViewMod_SomethingWrong => (
                Phrases.MessageWarning_CantViewMod_Title,
                Phrases.MessageWarning_CantViewMod_Extra_SomethingElse
            ),
            MessageTranslation.Warning_CantViewMod_NotFromBrowser => (
                Phrases.MessageWarning_CantViewMod_Title,
                Phrases.MessageWarning_CantViewMod_Extra_NotFromBrowser
            ),
            MessageTranslation.Warning_NoMiisFound => ("No Miis Found", "There are no other Miis available to select."),
            MessageTranslation.Warning_DolphinNotFound => (
                Phrases.MessageWarning_DolphinNotFound_Title,
                Phrases.MessageWarning_DolphinNotFound_Extra
            ),
            MessageTranslation.Warning_ModNameCantEmpty => (
                Phrases.MessageWarning_ModNameEmpty_Title,
                Phrases.MessageWarning_ModNameEmpty_Extra
            ),
            MessageTranslation.Warning_ModNameInvalid => (
                Phrases.MessageWarning_ModNameInvalid_Title,
                Phrases.MessageWarning_ModNameInvalid_Extra
            ),
            MessageTranslation.Warning_UnableToDownloadMod_Files => (
                Phrases.MessageWarning_UnableDownloadMod_Title,
                Phrases.MessageWarning_UnableDownloadMod_Extra
            ),

            #endregion

            #region Errors

            MessageTranslation.Error_StanderdError => ("Standard Error", "Something went wrong!"),
            MessageTranslation.Error_ModDownloadFailed => (
                Phrases.MessageError_ModDownloadFail_Title,
                Phrases.MessageError_ModDownloadFail_Extra
            ),
            MessageTranslation.Error_FailedCopyMii => ("Failed to copy Mii", "{$1}"),
            MessageTranslation.Error_MiiDBAlreadyExists => (Phrases.MessageError_FailedCreateMiiDb_Title, "Database already exists."),
            MessageTranslation.Error_UpdateMiiDb_InvalidClId => ("Invalid Client ID.", "The client ID attached to this Mii is invalid."),
            MessageTranslation.Error_UpdateMiiDb_BlockSizeInvalid => ("Mii block size invalid.", null),
            MessageTranslation.Error_UpdateMiiDb_NoBlockFound => ("Mii block not found.", null),
            MessageTranslation.Error_UpdateMiiDb_MiiNotFound => ("Mii not found", null),
            MessageTranslation.Error_UpdateMiiDb_InvalidMac => ("Invalid MAC Address", "The MAC attached to this Mii is invalid."),
            MessageTranslation.Error_UpdateMiiDb_RFLdbNotFound => ("RFL_DB.dat not found", "The RFL_DB.dat file could not be found."),
            MessageTranslation.Error_UpdateMiiDb_CorruptDb => (
                "Corrupt Mii Database",
                "Corrupt Mii database (bad CRC 0x{$1}, expected 0x{$2})."
            ),
            MessageTranslation.Error_MiiSerializer_MiiNotNull => ("Mii cannot be null", null),
            MessageTranslation.Error_MiiSerializer_MiiId0 => ("Mii ID cannot be 0", null),
            MessageTranslation.Error_MiiSerializer_MiiDataLength => ("Invalid Mii data length.", null),
            MessageTranslation.Error_MiiSerializer_MiiDataEmpty => ("Mii data is empty.", null),
            MessageTranslation.Error_MiiSerializer_InvalidMiiData => ("Invalid Mii data", "The Mii '{$1}' is invalid."),

            MessageTranslation.Error_MiiEditor_CantOpenEditor => ("Cant open Mii Editor", "{$1}"),

            MessageTranslation.Error_FailedInstallDolphin => (
                Phrases.MessageError_FailedInstallDolphin_Title,
                Phrases.MessageError_FailedInstallDolphin_Extra
            ),

            #endregion

            _ => ("Message", $"Unknown translation for: {msg.ToString()}"),
        };
    }

    #region Base Stuff

    /// <summary>
    ///  Shows a message box with the given message enum.
    /// </summary>
    public static void ShowMessage(MessageTranslation msg, object[]? titleReplacements = null, object[]? extraReplacements = null) =>
        CreateMessageBox(msg, titleReplacements, extraReplacements).Show();

    public static void ShowMessage(OperationError error) => CreateMessageBox(error).Show();

    public static Task AwaitMessageAsync(MessageTranslation msg, object[]? titleReplacements = null, object[]? extraReplacements = null) =>
        CreateMessageBox(msg, titleReplacements, extraReplacements).ShowDialog();

    public static Task AwaitMessageAsync(OperationError error) => CreateMessageBox(error).ShowDialog();

    private static MessageBoxWindow CreateMessageBox(
        MessageTranslation msg,
        object[]? titleReplacements = null,
        object[]? extraReplacements = null
    )
    {
        var (title, extraText) = GetTranslationText(msg);
        var type =
            (int)msg < 1000 ? MessageBoxWindow.MessageType.Message
            : (int)msg < 3000 ? MessageBoxWindow.MessageType.Warning
            : MessageBoxWindow.MessageType.Error;
        var box = new MessageBoxWindow()
            .SetMessageType(type)
            .SetTitleText(Humanizer.ReplaceDynamic(title, titleReplacements ?? []) ?? title);
        if (extraText == null)
            box.SetInfoText(Humanizer.ReplaceDynamic(title, titleReplacements ?? []) ?? title);
        else
            box.SetInfoText(Humanizer.ReplaceDynamic(extraText, extraReplacements ?? []) ?? extraText);

        if ((int)msg >= 2000)
            box.SetTag($"{(int)msg}");

        return box;
    }

    private static MessageBoxWindow CreateMessageBox(OperationError error)
    {
        if (error.MessageTranslation != null)
            return CreateMessageBox((MessageTranslation)error.MessageTranslation, error.TitleReplacements, error.ExtraReplacements);

        return new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Error)
            .SetTag("Unk")
            .SetTitleText(Phrases.MessageError_GenericError_Title)
            .SetInfoText(error.Message);
    }

    #endregion
}
