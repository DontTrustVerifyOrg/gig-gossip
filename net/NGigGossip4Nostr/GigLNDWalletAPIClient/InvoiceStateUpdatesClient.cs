using System;
using Microsoft.AspNetCore.SignalR.Client;
namespace GigLNDWalletAPIClient
{
    public interface IInvoiceStateUpdatesClient
    {
        Uri Uri { get; }
        Task ConnectAsync(string authToken, CancellationToken cancellationToken);
        Task MonitorAsync(string authToken, string paymentHash, CancellationToken cancellationToken);
        Task StopMonitoringAsync(string authToken, string paymentHash, CancellationToken cancellationToken);
        IAsyncEnumerable<InvoiceStateChange> StreamAsync(string authToken, CancellationToken cancellationToken);
    }

    public class InvoiceStateUpdatesClient : IInvoiceStateUpdatesClient
    {
        IWalletAPI swaggerClient;
        HubConnection connection;
        SemaphoreSlim slimLock = new(1, 1);

        public Uri Uri => new Uri(swaggerClient?.BaseUrl);

        internal InvoiceStateUpdatesClient(IWalletAPI swaggerClient)
        {
            this.swaggerClient = swaggerClient;
        }

        public async Task ConnectAsync(string authToken, CancellationToken cancellationToken)
        {
            if (!await slimLock.WaitAsync(1000)) throw new TimeoutException();
            try
            {
                var builder = new HubConnectionBuilder();
                builder.WithUrl(swaggerClient.BaseUrl + "invoicestateupdates?authtoken=" + Uri.EscapeDataString(authToken));
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

        public async Task MonitorAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
        {
            if (!await slimLock.WaitAsync(1000)) throw new TimeoutException();
            try
            {
                await connection.SendAsync("Monitor", authToken, paymentHash, cancellationToken);
            }
            finally
            {
                slimLock.Release();
            }
        }

        public async Task StopMonitoringAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
        {
            if (!await slimLock.WaitAsync(1000)) throw new TimeoutException();
            try
            {
                await connection.SendAsync("StopMonitoring", authToken, paymentHash, cancellationToken);
            }
            finally
            {
                slimLock.Release();
            }
        }

        public IAsyncEnumerable<InvoiceStateChange> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            if(!slimLock.Wait(1000)) throw new TimeoutException();
            try
            {
                return connection.StreamAsync<InvoiceStateChange>("StreamAsync", authToken, cancellationToken);
            }
            finally
            {
                slimLock.Release();
            }
        }
    }
}
