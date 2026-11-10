namespace WheelWizard.Shared.Processes;

public static class ShellQuoting
{
    public static string QuoteArgument(string value, bool isWindows) => isWindows ? $"\"{value}\"" : QuoteUnixArgument(value);

    public static string QuoteUnixArgument(string value) => $"'{value.Replace("'", "'\\''")}'";

    // Decode only the format emitted by QuoteUnixArgument.
    public static bool TryUnquoteUnixArgument(string value, out string argument)
    {
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            var decoded = value[1..^1].Replace("'\\''", "'");
            if (QuoteUnixArgument(decoded) == value)
            {
                argument = decoded;
                return true;
            }
        }

        argument = value;
        return false;
    }

    public static string QuotePowerShellArgument(string value) => $"'{value.Replace("'", "''")}'";
}
