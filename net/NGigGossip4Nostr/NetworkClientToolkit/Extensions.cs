using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http.Connections.Client;

namespace NetworkClientToolkit;

public static class Extensions
{
    public static bool CanRetry(this Exception ex)
    {
        return ex is HubException ||
                ex is TimeoutException ||
                ex is WebSocketException ||
                ex is IOException ||
                ex is HttpRequestException ||
                ex is SocketException ||
                ex is InvalidOperationException ||
                ex is TransportFailedException ||
                ex is NoTransportSupportedException;
    }

    public static async Task WithRetryPolicy(this IRetryPolicy retryPolicy, Func<Task> func)
    {
        var retryContext = new RetryContext();
        while (true)
        {
            try
            {
                await func();
                return;
            }
            catch (Exception ex) when (ex.CanRetry()) 
            {
                var ts = retryPolicy.NextRetryDelay(retryContext);
                if (ts == null)
                    throw;
                retryContext.ElapsedTime += ts.Value;
                retryContext.PreviousRetryCount++;
                retryContext.RetryReason = ex;
                Thread.Sleep(ts.Value);
            }
        }
    }

    public static async Task<T> WithRetryPolicy<T>(this IRetryPolicy retryPolicy, Func<Task<T>> func)
    {
        var retryContext = new RetryContext();
        while (true)
        {
            try
            {
                return await func();
            }
            catch (Exception ex) when (ex.CanRetry())
            {
                var ts = retryPolicy.NextRetryDelay(retryContext);
                if (ts == null)
                    throw;
                retryContext.ElapsedTime += ts.Value;
                retryContext.PreviousRetryCount++;
                retryContext.RetryReason = ex;
                Thread.Sleep(ts.Value);
            }
        }
    }

    /// <summary>
    /// Adds a key/value pair to the <see cref="ConcurrentDictionary{TKey, TValue}"/> by using the specified function 
    /// if the key does not already exist. Returns the new value, or the existing value if the key exists.
    /// </summary>
    public static async Task<TResult> GetOrAddAsync<TKey, TResult>(
        this ConcurrentDictionary<TKey, TResult> dict,
        TKey key, Func<TKey, Task<TResult>> asyncValueFactory) where TKey : notnull
    {
        if (dict.TryGetValue(key, out TResult resultingValue))
        {
            return resultingValue;
        }
        var newValue = await asyncValueFactory(key);
        return dict.GetOrAdd(key, newValue);
    }



}

