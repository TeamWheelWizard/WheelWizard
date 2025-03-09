namespace WheelWizard.Models.GameData;

public class GameData
{
    public List<GameDataUser> Users { get; set; }

    public GameData()
    {
        Users = new List<GameDataUser>(4);
    }
}
