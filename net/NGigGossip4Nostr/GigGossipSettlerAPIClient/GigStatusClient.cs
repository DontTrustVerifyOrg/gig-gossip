using System;
using Microsoft.AspNetCore.SignalR.Client;

namespace GigGossipSettlerAPIClient
{
    public interface IGigStatusClient
    {
        Task ConnectAsync(string authToken, CancellationToken cancellationToken);
        Task MonitorAsync(string authToken, Guid gigId, Guid replierCertificateId, CancellationToken cancellationToken);
        IAsyncEnumerable<string> StreamAsync(string authToken, CancellationToken cancellationToken);
        Task DisposeAsync();
    }

    public class GigStatusClient : IGigStatusClient
    {
        ISettlerAPI swaggerClient;
        public HubConnection Connection;

        internal GigStatusClient(ISettlerAPI swaggerClient)
		{
            this.swaggerClient = swaggerClient;
        }

		public async Task ConnectAsync(string authToken, CancellationToken cancellationToken)
		{
            Connection = new HubConnectionBuilder()
                .WithUrl(swaggerClient.BaseUrl + "gigstatus?authtoken=" + Uri.EscapeDataString(authToken))
                .Build();
            await Connection.StartAsync(cancellationToken);
        }

        public async Task MonitorAsync(string authToken, Guid gigId, Guid replierCertificateId, CancellationToken cancellationToken)
        {
            await Connection.SendAsync("Monitor", authToken, gigId, replierCertificateId, cancellationToken);
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

