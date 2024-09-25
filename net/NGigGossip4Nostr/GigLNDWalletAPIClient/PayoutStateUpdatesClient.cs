using System;
using Microsoft.AspNetCore.SignalR.Client;
namespace GigLNDWalletAPIClient
{
    public interface IPayoutStateUpdatesClient
    {
        Uri Uri { get; }
        Task ConnectAsync(string authToken, CancellationToken cancellationToken);
        IAsyncEnumerable<PayoutStateChanged> StreamAsync(string authToken, CancellationToken cancellationToken);
    }

    public class PayoutStateUpdatesClient : IPayoutStateUpdatesClient
    {
        IWalletAPI swaggerClient;
        HubConnection connection;
        SemaphoreSlim slimLock = new(1, 1);

        public Uri Uri => new Uri(swaggerClient?.BaseUrl);

        internal PayoutStateUpdatesClient(IWalletAPI swaggerClient)
        {
            this.swaggerClient = swaggerClient;
        }

        public async Task ConnectAsync(string authToken, CancellationToken cancellationToken)
        {
            await slimLock.WaitAsync();
            try
            {
                var builder = new HubConnectionBuilder();
                builder.WithUrl(swaggerClient.BaseUrl + "payoutstateupdates?authtoken=" + Uri.EscapeDataString(authToken));
                if(swaggerClient.RetryPolicy != null)
                    builder.WithAutomaticReconnect(swaggerClient.RetryPolicy);
                connection = builder.Build();
                await connection.StartAsync(cancellationToken);
            }
            finally
            {
                slimLock.Release();
            }
        }

        public IAsyncEnumerable<PayoutStateChanged> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            slimLock.Wait();
            try
            {
                return connection.StreamAsync<PayoutStateChanged>("StreamAsync", authToken, cancellationToken);
            }
            finally
            {
                slimLock.Release();
            }
        }
    }
}
