namespace WheelWizard.CustomCharacters;

public interface ICustomCharactersService
{
    /// <summary>
    /// Gets the custom characters.
    /// </summary>
    List<char> GetCustomCharacters();

    /// <summary>
    /// To clear the given string from all the custom characters, and map them to their ascii closest representation.
    /// </summary>
    string NormalizeToAscii(string str);
}

public class CustomCharactersService : ICustomCharactersService
{
    public List<char> GetCustomCharacters()
    {
        var charRanges = new List<(char, char)>
        {
            ((char)0x2460, (char)0x246e),
            ((char)0xe000, (char)0xe01c),
            ((char)0xf061, (char)0xf06d),
            ((char)0xf074, (char)0xf07c),
            ((char)0xf107, (char)0xf10f), // it actually goes up to 0xf12f, but for some reason it repeats the same 8 icons 6 times over
        };

        var chars = new List<char>();
        foreach (var (start, end) in charRanges)
        {
            for (var i = start; i <= end; i++)
            {
                chars.Add(i);
            }
        }

        // All the left-over chars that we cant make easy groups out of
        chars.AddRange(
            [
                (char)0xe028,
                (char)0xe068,
                (char)0xe067,
                (char)0xe06a,
                (char)0xe06b,
                (char)0xf030,
                (char)0xf031,
                (char)0xf034,
                (char)0xf035,
                (char)0xf038,
                (char)0xf039,
                (char)0xf041,
                (char)0xf043,
                (char)0xf044,
                (char)0xf047,
                (char)0xf050,
                (char)0xf058,
                (char)0xf05e,
                (char)0xf05f,
                (char)0xf103,
            ]
        );

        return chars;
    }

    public string NormalizeToAscii(string str)
    {
        var charRanges = new List<(char, char, string[])>
        {
            ((char)0x2460, (char)0x246e, SArr("0123456789:,/-+")),
            (
                (char)0xe000,
                (char)0xe01c,
                [
                    "(A)",
                    "(B)",
                    "(X)",
                    "(Y)",
                    "[L]",
                    "[R]",
                    "⁜",
                    "◷",
                    ":)",
                    ">:(",
                    "<:o",
                    ":|",
                    "⁕",
                    "@",
                    "◜|◝",
                    "{}",
                    "[!]",
                    "[?]",
                    "[v]",
                    "[x]",
                    "[+]",
                    "\u2660",
                    "\u2666",
                    "\u2665",
                    "\u2663",
                    "▶",
                    "◀",
                    "▲",
                    "▼",
                ]
            ),
            ((char)0xf061, (char)0xf06d, SArr("⁎⁑⁂⨁⨁⨁⨁⨀⩉ŲŲŲ₩")),
            ((char)0xf074, (char)0xf07c, SArr("⨁⨁⨁⨁ABCDE")),
            (
                (char)0xf107,
                (char)0xf12f,
                [
                    "[⁇]",
                    "▢▣▣▣",
                    "▣▢▣▣",
                    "▣▣▢▣",
                    "▣▣▣▢",
                    "○●●●",
                    "●○●●",
                    "●●○●",
                    "●●●○",
                    "▢▣▣▣",
                    "▣▢▣▣",
                    "▣▣▢▣",
                    "▣▣▣▢",
                    "○●●●",
                    "●○●●",
                    "●●○●",
                    "●●●○",
                    "▢▣▣▣",
                    "▣▢▣▣",
                    "▣▣▢▣",
                    "▣▣▣▢",
                    "○●●●",
                    "●○●●",
                    "●●○●",
                    "●●●○",
                    "▢▣▣▣",
                    "▣▢▣▣",
                    "▣▣▢▣",
                    "▣▣▣▢",
                    "○●●●",
                    "●○●●",
                    "●●○●",
                    "●●●○",
                    "▢▣▣▣",
                    "▣▢▣▣",
                    "▣▣▢▣",
                    "▣▣▣▢",
                    "○●●●",
                    "●○●●",
                    "●●○●",
                    "●●●○",
                    "▢▣▣▣",
                    "▣▢▣▣",
                    "▣▣▢▣",
                    "▣▣▣▢",
                    "○●●●",
                    "●○●●",
                    "●●○●",
                    "●●●○",
                ]
            ),
        };

        foreach (var (start, end, replacements) in charRanges)
        {
            for (var i = start; i <= end; i++)
            {
                var replacementIndex = i - start;
                if (replacements.Length < replacementIndex)
                    continue; // If its here we did not account for 1 of the replacements
                str = str.Replace($"{i}", replacements[replacementIndex]);
            }
        }

        (char, string)[] individualReplacements =
        [
            ((char)0xe028, "✕"),
            ((char)0xe068, "er"),
            ((char)0xe067, "re"),
            ((char)0xe06a, "e"),
            ((char)0xe06b, "[?]"),
            ((char)0xf030, "②"),
            ((char)0xf031, "②"),
            ((char)0xf034, "(A)"),
            ((char)0xf035, "(A)"),
            ((char)0xf038, "(a)"),
            ((char)0xf039, "(a)"),
            ((char)0xf041, "[B]"),
            ((char)0xf043, "(1)"),
            ((char)0xf044, "(+)"),
            ((char)0xf047, "(+)"),
            ((char)0xf050, "(b)"),
            ((char)0xf058, "(B)"),
            ((char)0xf05e, "(S)"),
            ((char)0xf05f, "(s)"),
            ((char)0xf103, "▣▣▣▣"),
            // The once below are also  not in teh actual app but should still be exported correctly
            ((char)0xf03c, "(A)"),
            ((char)0xf03d, "(A)"),
            ((char)0xf102, " "),
            ((char)0xf060, " "),
        ];

        foreach (var (c, replacement) in individualReplacements)
        {
            str = str.Replace($"{c}", replacement);
        }

        return str;
    }

    private static string[] SArr(string input) => input.ToCharArray().Select(c => $"{c}").ToArray();
}
