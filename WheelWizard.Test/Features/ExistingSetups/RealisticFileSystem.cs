using System.IO.Abstractions;
using System.Reflection;

namespace WheelWizard.Test.Features.ExistingSetups;

/// <summary>
/// The in-memory file system throws for an empty path where the real one answers "does not exist"; the
/// settings validators ask about paths that were never set, so the app sees this wrapper instead.
/// </summary>
public class RealisticFileSystem : DispatchProxy
{
    private IFileSystem _inner = null!;
    private object? _directory;
    private object? _file;

    public static IFileSystem Wrap(IFileSystem inner)
    {
        var proxy = Create<IFileSystem, RealisticFileSystem>();
        var self = (RealisticFileSystem)(object)proxy;
        self._inner = inner;
        self._directory = Existence<IDirectory>.Create(inner.Directory);
        self._file = Existence<IFile>.Create(inner.File);
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == "get_Directory")
            return _directory;
        if (targetMethod.Name == "get_File")
            return _file;
        return Forward(targetMethod, _inner, args);
    }

    private static object? Forward(MethodInfo method, object target, object?[]? args)
    {
        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    public class Existence<T> : DispatchProxy
        where T : class
    {
        private T _inner = null!;

        public static T Create(T inner)
        {
            var proxy = DispatchProxy.Create<T, Existence<T>>();
            ((Existence<T>)(object)proxy)._inner = inner;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "Exists" && args is { Length: 1 } && args[0] is null or "")
                return false;
            return Forward(targetMethod, _inner, args);
        }
    }
}
