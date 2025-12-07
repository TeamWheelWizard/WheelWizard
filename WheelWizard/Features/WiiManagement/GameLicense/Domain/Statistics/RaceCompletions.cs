namespace WheelWizard.WiiManagement.GameLicense.Domain.Statistics;

public class RaceCompletions
{
    public Dictionary<Character, int> CharacterCompletions { get; set; } = new();

    public Character FavoriteCharacter
    {
        get
        {
            var currentFavorite = Character.Mario; // Default to Mario
            var maxCompletions = 0;
            foreach (var kvp in CharacterCompletions)
            {
                if (kvp.Value > maxCompletions)
                {
                    maxCompletions = kvp.Value;
                    currentFavorite = kvp.Key;
                }
            }
            return currentFavorite;
        }
    }

    /// <summary>
    /// Key = Vehicle enum, Value = races completed count.
    /// Uses 36 entries starting at 0x11E.
    /// </summary>
    public Dictionary<Vehicle, int> Vehicle { get; init; } = new();

    /// <summary>
    /// Key = Course enum, Value = races completed count.
    /// Uses 32 entries starting at 0x166.
    /// </summary>
    public Dictionary<Course, int> Course { get; init; } = new();
}
