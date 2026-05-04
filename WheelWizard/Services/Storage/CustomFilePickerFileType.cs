using Avalonia.Platform.Storage;
using WheelWizard.Resources.Languages;

namespace WheelWizard.Services;

public static class CustomFilePickerFileType
{
    public static FilePickerFileType All { get; } =
        new(Common.Attribute_All)
        {
            Patterns = ["*"],
            AppleUniformTypeIdentifiers = ["public.data"],
            MimeTypes = ["application/octet-stream"],
        };
    public static FilePickerFileType Miis { get; } =
        new("Miis")
        {
            Patterns = ["*.mii", "*.miigx", "*.mae"],
            AppleUniformTypeIdentifiers = ["com.wheelwizard.miis"],
            MimeTypes = ["application/miis"],
        };
}
