using System.Collections.Concurrent;
using System.Reflection;

namespace WheelWizard.Shared.DependencyInjection;

/// <summary>
/// A static class that provides methods for injecting services into properties of a given instance.
/// </summary>
public static class ServiceInjector
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> InjectPropertiesCache = new();

    /// <summary>
    /// Injects services into properties marked with the <see cref="InjectAttribute"/> in the given instance.
    /// </summary>
    public static bool RequiresInjection(Type type) => GetInjectProperties(type).Length > 0;

    public static void InjectServices(IServiceProvider serviceProvider, object instance)
    {
        var type = instance.GetType();

        foreach (var injectProperty in GetInjectProperties(type))
        {
            injectProperty.SetValue(instance, serviceProvider.GetService(injectProperty.PropertyType), null);
        }
    }

    private static PropertyInfo[] GetInjectProperties(Type controlType) =>
        InjectPropertiesCache.GetOrAdd(
            controlType,
            static type =>
                GetAllPropertiesRecursive(type)
                    .Where(prop => prop.CanWrite && prop.GetCustomAttribute<InjectAttribute>() is not null)
                    .ToArray()
        );

    private static IEnumerable<PropertyInfo> GetAllPropertiesRecursive(Type type)
    {
        while (type != null!)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var prop in type.GetProperties(flags))
            {
                yield return prop;
            }

            type = type.BaseType!;
        }
    }
}
