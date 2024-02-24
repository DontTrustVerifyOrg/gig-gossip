using System;
using Microsoft.AspNetCore.SignalR.Client;
namespace GigGossipSettlerAPIClient
{
    public interface IPreimageRevealClient
    {
        Task ConnectAsync(string authToken, CancellationToken cancellationToken);
        Task MonitorAsync(string authToken, string paymentHash, CancellationToken cancellationToken);
        IAsyncEnumerable<string> StreamAsync(string authToken, CancellationToken cancellationToken);
        Task DisposeAsync();
    }

    public class PreimageRevealClient : IPreimageRevealClient
    {
        ISettlerAPI swaggerClient;
        public HubConnection Connection;

        internal PreimageRevealClient(ISettlerAPI swaggerClient)
        {
            this.swaggerClient = swaggerClient;
        }

        public async Task ConnectAsync(string authToken, CancellationToken cancellationToken)
        {
            Connection = new HubConnectionBuilder()
                .WithUrl(swaggerClient.BaseUrl + "preimagereveal?authtoken=" + Uri.EscapeDataString(authToken))
                .Build();
            await Connection.StartAsync(cancellationToken);
        }

        public async Task MonitorAsync(string authToken, string paymentHash, CancellationToken cancellationToken)
        {
            await Connection.SendAsync("Monitor", authToken, paymentHash, cancellationToken);
        }

        public IAsyncEnumerable<string> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            return Connection.StreamAsync<string>("StreamAsync", authToken, cancellationToken);
        }

        public async Task DisposeAsync()
        {
            await Connection.DisposeAsync();
        }
    }
}
