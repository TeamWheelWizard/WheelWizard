namespace WheelWizard.ApplicationLifecycle;

public sealed record StartupOptions(string? ProtocolArgument, bool LaunchRetroRewind)
{
    public static StartupOptions Parse(IReadOnlyList<string> arguments)
    {
        var protocol = arguments.FirstOrDefault(argument => argument.StartsWith("wheelwizard://", StringComparison.OrdinalIgnoreCase));
        var launch = false;
        for (var index = 0; index < arguments.Count; ++index)
        {
            var argument = arguments[index];
            string? target = null;
            if (
                argument.Equals("--launch", StringComparison.OrdinalIgnoreCase) || argument.Equals("-l", StringComparison.OrdinalIgnoreCase)
            )
            {
                if (index + 1 < arguments.Count)
                    target = arguments[++index];
            }
            else if (argument.StartsWith("--launch=", StringComparison.OrdinalIgnoreCase))
                target = argument["--launch=".Length..].Trim();

            launch |= target?.ToLowerInvariant() is "rr" or "retrorewind" or "retro-rewind";
        }
        return new StartupOptions(protocol, launch);
    }
}
