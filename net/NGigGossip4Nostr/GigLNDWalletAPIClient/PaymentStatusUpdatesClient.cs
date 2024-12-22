using System;
using Microsoft.AspNetCore.SignalR.Client;
namespace GigLNDWalletAPIClient
{
    public interface IPaymentStatusUpdatesClient
    {
        Uri Uri { get; }

        Task ConnectAsync(string authToken, CancellationToken cancellationToken);
        IAsyncEnumerable<PaymentStatusChanged> StreamAsync(string authToken, CancellationToken cancellationToken);
    }

    public class PaymentStatusUpdatesClient : IPaymentStatusUpdatesClient
    {
        IWalletAPI swaggerClient;
        HubConnection connection;
        SemaphoreSlim slimLock = new(1, 1);

        internal PaymentStatusUpdatesClient(IWalletAPI swaggerClient)
        {
            this.swaggerClient = swaggerClient;
        }

        public Uri Uri => new Uri(swaggerClient?.BaseUrl);

        public async Task ConnectAsync(string authToken, CancellationToken cancellationToken)
        {
            if (!await slimLock.WaitAsync(1000)) throw new TimeoutException();
            try
            {
                var builder = new HubConnectionBuilder();
                builder.WithUrl(swaggerClient.BaseUrl + "paymentstatusupdates?authtoken=" + Uri.EscapeDataString(authToken));
                if (swaggerClient.RetryPolicy != null)
                    builder.WithAutomaticReconnect(swaggerClient.RetryPolicy);
                connection = builder.Build();
                await connection.StartAsync(cancellationToken);
            }
            finally
            {
                slimLock.Release();
            }
        }

        public IAsyncEnumerable<PaymentStatusChanged> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            if (!slimLock.Wait(10000)) throw new TimeoutException();
            try
            {
                return connection.StreamAsync<PaymentStatusChanged>("StreamAsync", authToken, cancellationToken);
            }
            finally
            {
                slimLock.Release();
            }
        }
    }
}

