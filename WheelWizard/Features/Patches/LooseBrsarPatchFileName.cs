namespace WheelWizard.Features.Patches;

public static class LooseBrsarPatchFileName
{
    public static bool TryGetNormalizedFileName(string fileName, out string normalizedFileName)
    {
        normalizedFileName = string.Empty;

        if (string.IsNullOrWhiteSpace(fileName) || fileName[0] != '[')
            return false;

        var closeBracketIndex = fileName.IndexOf(']');
        if (closeBracketIndex <= 1)
            return false;

        var idText = fileName[1..closeBracketIndex];
        if (!idText.All(char.IsDigit) || !int.TryParse(idText, out var fileId))
            return false;

        var extension = Path.GetExtension(fileName);
        if (
            !extension.Equals(".brwsd", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".brbnk", StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        normalizedFileName = $"{fileId}{extension.ToLowerInvariant()}";
        return true;
    }
}
