using System;
using Microsoft.AspNetCore.SignalR.Client;

namespace NetworkClientToolkit;

public class HubMonitor : ServerListener
{
    bool AppExiting = false;
    bool ClientConnected = false;
    object ClientLock = new();
    Thread monitorThread;

    public event EventHandler<ServerConnectionStateEventArgs> OnServerConnectionState;

    public void WaitForClientConnected()
    {
        lock (ClientLock)
        {
            while (!ClientConnected)
            {
                Monitor.Wait(ClientLock);
                if (AppExiting)
                    throw new OperationCanceledException();
            }
        }
    }

    void NotifyClientIsConnected()
    {
        lock (ClientLock)
        {
            ClientConnected = true;
            Monitor.PulseAll(ClientLock);
        }
    }

    void NotifyAppClosing()
    {
        lock (ClientLock)
        {
            AppExiting = true;
            Monitor.PulseAll(ClientLock);
        }
    }

    public async Task StartAsync(Func<Task> connect, Func<Task> func, Uri uri, IRetryPolicy retryPolicy, CancellationToken cancellationToken)
    {
        monitorThread = new Thread(async () =>
            {
                await LoopAsync(async () =>
                {
                    OnServerConnectionState?.Invoke(this, new ServerConnectionStateEventArgs() { State = ServerConnectionState.Connecting, Uri = uri });
                    await connect();
                    OnServerConnectionState?.Invoke(this, new ServerConnectionStateEventArgs() { State = ServerConnectionState.Open, Uri = uri });
                    NotifyClientIsConnected();
                    await func();
                },
                async (retryContext) =>
                {
                    OnServerConnectionState?.Invoke(this, new ServerConnectionStateEventArgs() { State = ServerConnectionState.Closed, Uri = uri });
                }, retryPolicy, cancellationToken
                );
            });
        monitorThread.Start();
    }


    public void Stop(CancellationTokenSource cancellationTokenSource)
    {
        cancellationTokenSource.Cancel();
        monitorThread.Join();
        NotifyAppClosing();
    }

}



