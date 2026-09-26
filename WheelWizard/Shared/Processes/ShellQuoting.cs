namespace WheelWizard.Shared.Processes;

public static class ShellQuoting
{
    public static string QuoteArgument(string value, bool isWindows) => isWindows ? $"\"{value}\"" : $"'{value.Replace("'", "'\\''")}'";
}
