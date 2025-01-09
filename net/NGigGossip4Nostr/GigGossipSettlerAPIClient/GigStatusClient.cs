using System;
using Microsoft.AspNetCore.SignalR.Client;

namespace GigGossipSettlerAPIClient
{
    public interface IGigStatusClient
    {
        Uri Uri { get; }

        Task ConnectAsync(string authToken, CancellationToken cancellationToken);
        IAsyncEnumerable<GigStatusKey> StreamAsync(string authToken, CancellationToken cancellationToken);
        Task DisposeAsync();
    }

    public class GigStatusClient : IGigStatusClient
    {
        ISettlerAPI swaggerClient;
        HubConnection Connection;

        internal GigStatusClient(ISettlerAPI swaggerClient)
        {
            this.swaggerClient = swaggerClient;
        }

        public Uri Uri => new Uri(swaggerClient?.BaseUrl);

        public async Task ConnectAsync(string authToken, CancellationToken cancellationToken)
		{
            var builder = new HubConnectionBuilder();
            builder.WithUrl(swaggerClient.BaseUrl + "gigstatus?authtoken=" + Uri.EscapeDataString(authToken));
            if (swaggerClient.RetryPolicy != null)
                builder.WithAutomaticReconnect(swaggerClient.RetryPolicy);
            Connection = builder.Build();
            await Connection.StartAsync(cancellationToken);
        }

        public IAsyncEnumerable<GigStatusKey> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            return Connection.StreamAsync<GigStatusKey>("StreamAsync", authToken, cancellationToken);
        }

        public async Task DisposeAsync()
        {
            await Connection.DisposeAsync();
        }
    }
}

