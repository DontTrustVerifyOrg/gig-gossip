using System;
using Microsoft.AspNetCore.SignalR.Client;
namespace GigGossipSettlerAPIClient
{
	public class SymmetricKeyRevealClient
	{
        swaggerClient swaggerClient;
        HubConnection connection;

        public SymmetricKeyRevealClient(swaggerClient swaggerClient)
		{
            this.swaggerClient = swaggerClient;
        }

		public async Task ConnectAsync(string authToken)
		{
            connection = new HubConnectionBuilder()
                .WithUrl(swaggerClient.BaseUrl + "symmetrickeyreveal?authtoken=" + Uri.EscapeDataString(authToken))
                .WithAutomaticReconnect()
                .Build();
            await connection.StartAsync();
        }

        public async Task MonitorAsync(string authToken, Guid gigId, string replierPublicKey)
        {
            await connection.SendAsync("Monitor", authToken, gigId, replierPublicKey);
        }

        public IAsyncEnumerable<string> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            return connection.StreamAsync<string>("StreamAsync", authToken, cancellationToken);
        }
    }
}

