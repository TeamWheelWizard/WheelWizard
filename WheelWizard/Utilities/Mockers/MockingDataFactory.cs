namespace WheelWizard.Utilities.Mockers;

public abstract class MockingDataFactory<T, U> where U: MockingDataFactory<T,U>, new()
{
    public static T StaticSingle => Instance.Create();
    
    public static U Instance { get; } = new U();
    public abstract T Create(int? seed = null);

    protected virtual string DictionaryKeyGenerator(T value) => value.ToString();

    public T[] CreateMultiple(int count = 5, int? seed = null)
    {
        var random = seed != null ? new Random(seed.Value) : null;   
        var result = new T[count];
        for (var i = 0; i < count; i++)
        {
            int? localSeed = null;
            if (random != null)
                localSeed = random.Next();
            result[i] = Create(localSeed);
        }
        return result;
    }
    
    public Dictionary<string, T> CreateAsDictionary(int count = 5, int? seed = null)
    {
        var result = new Dictionary<string, T>();
        var list = CreateMultiple(count);
        foreach (var item in list)
        {
            result.Add(DictionaryKeyGenerator(item), item);
        }
        return result;
    }

    protected Random Rand(int? seed = null) => seed == null ? new Random() : new Random(seed.Value);
}
