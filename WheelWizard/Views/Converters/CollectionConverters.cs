using System.Collections;
using Avalonia.Data.Converters;

namespace WheelWizard.Views.Converters;

// Note that this is static, which means you don't have to add it as a converter
public static class CollectionConverters
{
    public static readonly IValueConverter First = new FuncValueConverter<IEnumerable?, object?>(x =>
        x?.OfType<object?>().ElementAtOrDefault(0)
    );
    public static readonly IValueConverter FirstIsNull = new FuncValueConverter<IEnumerable?, bool>(x =>
        x?.OfType<object?>().ElementAtOrDefault(0) == null
    );

    public static readonly IValueConverter Second = new FuncValueConverter<IEnumerable?, object?>(x =>
        x?.OfType<object?>().ElementAtOrDefault(1)
    );
    public static readonly IValueConverter SecondIsNull = new FuncValueConverter<IEnumerable?, bool>(x =>
        x?.OfType<object?>().ElementAtOrDefault(1) == null
    );
    public static readonly IValueConverter SecondIsNotNull = new FuncValueConverter<IEnumerable?, bool>(x =>
        x?.OfType<object?>().ElementAtOrDefault(1) != null
    );
}
