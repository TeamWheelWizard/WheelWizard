using System.Linq.Expressions;
using Microsoft.Extensions.Logging;

namespace WheelWizard.Shared.Services;

/// <summary>
/// Singleton service for calling Refit APIs.
/// </summary>
/// <typeparam name="TApi">The type of the Refit API interface.</typeparam>
public interface IApiCaller<TApi>
    where TApi : class
{
    /// <summary>
    /// Calls the specified API method asynchronously.
    /// </summary>
    /// <param name="apiCall">The API method to call. Make sure you name the variable clearly as it is used in the logs.</param>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <returns>An <see cref="OperationResult{TResult}"/> representing the result of the API call.</returns>
    /// <remarks>
    /// Uses the <paramref name="apiCall"/> expression and the name of <typeparamref name="TApi"/> for logging purposes.
    /// </remarks>
    Task<OperationResult<TResult>> CallApiAsync<TResult>(Expression<Func<TApi, Task<TResult>>> apiCall);
}

public class ApiCaller<T>(IServiceScopeFactory scopeFactory, ILogger<ApiCaller<T>> logger) : IApiCaller<T>
    where T : class
{
    public async Task<OperationResult<TResult>> CallApiAsync<TResult>(Expression<Func<T, Task<TResult>>> apiCall)
    {
        // #todo: carry cancellation through the api interfaces to the actual requests, not just the caller's wait.
        var apiCallString = apiCall.Body.ToString();
        var apiCallFunction = apiCall.Compile();
        var apiName = typeof(T).Name[1..];

        using var scope = scopeFactory.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<T>();

        var result = await TryCatch(async () => await apiCallFunction.Invoke(api), errorMessage: $"{apiName} call failed");
        if (!result.IsSuccess)
            logger.LogError(result.Error.Exception, "API method '{ApiCall}' failed: {Message}", apiCallString, result.Error.Message);

        return result;
    }
}
