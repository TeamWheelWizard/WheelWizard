namespace WheelWizard.GameBanana.Domain;

public sealed record GameBananaModType(int Id, string Name)
{
    public string Url => $"https://gamebanana.com/mods/cats/{Id}";

    public override string ToString() => Name;
}

// Mario Kart Wii's category tree: https://gamebanana.com/games/cattree/5896
public static class GameBananaModCategories
{
    public static IReadOnlyList<GameBananaModType> Types { get; } =
        [new(10080, "Skins"), new(799, "Tracks"), new(2083, "UI"), new(4152, "Other/Misc"), new(35879, "Item")];

    public static IReadOnlyList<GameBananaModType> Skins { get; } =
        [
            new(10101, "Baby Daisy"),
            new(10099, "Baby Luigi"),
            new(10098, "Baby Mario"),
            new(10100, "Baby Peach"),
            new(10087, "Birdo"),
            new(10093, "Bowser"),
            new(10089, "Bowser Jr."),
            new(10085, "Daisy"),
            new(10088, "Diddy Kong"),
            new(10092, "Donkey Kong"),
            new(10105, "Dry Bones"),
            new(10097, "Dry Bowser"),
            new(10096, "Funky Kong"),
            new(10106, "Items"),
            new(10108, "Karts"),
            new(10094, "King Boo"),
            new(10104, "Koopa Troopa"),
            new(10083, "Luigi"),
            new(10110, "Luma"),
            new(10082, "Mario"),
            new(10107, "Mii"),
            new(10081, "Other/Misc"),
            new(10109, "Packs"),
            new(10084, "Peach"),
            new(10095, "Rosalina"),
            new(10102, "Toad"),
            new(10103, "Toadette"),
            new(10091, "Waluigi"),
            new(10090, "Wario"),
            new(10086, "Yoshi"),
        ];

    public static IReadOnlyList<GameBananaModType> Ui { get; } =
        [new(2086, "Driver Icons"), new(2085, "HUD"), new(2084, "Other/Misc"), new(30495, "Title Screen")];
}
