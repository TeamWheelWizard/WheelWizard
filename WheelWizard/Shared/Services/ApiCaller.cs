using System.Linq.Expressions;
using Microsoft.Extensions.Logging;

namespace WheelWizard.Shared.Services;

/// <summary>
/// Singleton service for calling Refit APIs.
/// </summary>
/// <typeparam name="TApi">The type of the Refit API interface.</typeparam>
public interface IApiCaller<TApi> where TApi : class
{
    /// <summary>
    /// Calls the specified API method asynchronously.
    /// </summary>
    /// <param name="apiCall">The API method to call.</param>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <returns>An <see cref="OperationResult{TResult}"/> representing the result of the API call.</returns>
    Task<OperationResult<TResult>> CallApiAsync<TResult>(Expression<Func<TApi, Task<TResult>>> apiCall);
}

public class ApiCaller<T>(IServiceScopeFactory scopeFactory, ILogger<ApiCaller<T>> logger) : IApiCaller<T> where T : class
{
    public async Task<OperationResult<TResult>> CallApiAsync<TResult>(Expression<Func<T, Task<TResult>>> apiCall)
    {
        var apiCallString = apiCall.Body.ToString();

        try
        {
            using var scope = scopeFactory.CreateScope();
            var api = scope.ServiceProvider.GetRequiredService<T>();

            return await apiCall.Compile().Invoke(api);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "API call '{ApiCall}' failed: {Message}", apiCallString, exception.Message);
            return new OperationError
            {
                Message = $"API call '{apiCallString}' failed: {exception.Message}",
                Exception = exception
            };
        }
    }
}
