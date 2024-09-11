using System;
using Microsoft.AspNetCore.SignalR.Client;
namespace GigGossipSettlerAPIClient
{
    public interface IPreimageRevealClient
    {
        Uri Uri { get; }

        Task ConnectAsync(string authToken, CancellationToken cancellationToken);
        Task MonitorAsync(string authToken, string paymentHash, CancellationToken cancellationToken);
        IAsyncEnumerable<PreimageReveal> StreamAsync(string authToken, CancellationToken cancellationToken);
        Task DisposeAsync();
    }

    public class PreimageRevealClient : IPreimageRevealClient
    {
        ISettlerAPI swaggerClient;
        HubConnection Connection;

        internal PreimageRevealClient(ISettlerAPI swaggerClient)
        {
            this.swaggerClient = swaggerClient;
        }

        public Uri Uri => new Uri(swaggerClient?.BaseUrl);

        public async Task ConnectAsync(string authToken, CancellationToken cancellationToken)
        {
            var builder = new HubConnectionBuilder();
            builder.WithUrl(swaggerClient.BaseUrl + "preimagereveal?authtoken=" + Uri.EscapeDataString(authToken));
            if (swaggerClient.RetryPolicy != null)
                builder.WithAutomaticReconnect(swaggerClient.RetryPolicy);
            Connection = builder.Build();
            await Connection.StartAsync(cancellationToken);
        }

        public async Task MonitorAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
        {
            await Connection.SendAsync("Monitor", authToken, paymentHash, cancellationToken);
        }

        public IAsyncEnumerable<PreimageReveal> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            return Connection.StreamAsync<PreimageReveal>("StreamAsync", authToken, cancellationToken);
        }

        public async Task DisposeAsync()
        {
            await Connection.DisposeAsync();
        }
    }
}
