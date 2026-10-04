using Avalonia.Platform.Storage;

namespace WheelWizard.Views.Storage;

public static class FilePickerFilters
{
    public static FilePickerFileType All =>
        new(t("attribute.all"))
        {
            Patterns = ["*"],
            AppleUniformTypeIdentifiers = ["public.data"],
            MimeTypes = ["application/octet-stream"],
        };
    public static FilePickerFileType Miis =>
        new("Miis")
        {
            Patterns = ["*.mii", "*.miigx", "*.mae"],
            AppleUniformTypeIdentifiers = ["com.wheelwizard.miis"],
            MimeTypes = ["application/miis"],
        };
}
