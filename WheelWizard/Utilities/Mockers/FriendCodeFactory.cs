namespace WheelWizard.Utilities.Mockers;

public class FriendCodeFactory : MockingDataFactory<string, FriendCodeFactory>
{
    public override string Create(int? seed = null)
    {
        var random = Rand(seed);
        var subStrings = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var subString = string.Empty;
            for (var j = 0; j < 4; j++)
            {
                var randomNumber = (int)(new Random().NextDouble() * 10);
                subString += randomNumber.ToString();
            }
            subStrings.Add(subString);
        }
        
        return string.Join("-",subStrings);
    }
}
