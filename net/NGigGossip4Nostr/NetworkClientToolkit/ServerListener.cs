using System;
using System.Threading;
using Microsoft.AspNetCore.SignalR.Client;

namespace NetworkClientToolkit;

public class ServerListener
{
    public RetryContext RetryContext = new();

    public async Task LoopAsync(Func<Task> func, Func<RetryContext, Task> retry, IRetryPolicy retryPolicy, CancellationToken cancellationToken)
    {
        TimeSpan? ots = null;
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                RetryContext = new();
                return;
            }

            try
            {
                await func();
                RetryContext = new();
                break;
            }
            catch (OperationCanceledException)
            {
                RetryContext = new();
                return;
            }
            catch (Exception ex) when (ex.CanRetry())
            {
                var ts = retryPolicy.NextRetryDelay(RetryContext);
                if (ts == null)
                    ts = ots;
                else
                    ots = ts;
                RetryContext.ElapsedTime += ts!.Value;
                RetryContext.PreviousRetryCount++;
                RetryContext.RetryReason = ex;
                await retry(RetryContext);
                Thread.Sleep(ts.Value);
            }
        }
    }
}

