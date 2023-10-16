using System;
using Microsoft.AspNetCore.SignalR.Client;
namespace GigGossipSettlerAPIClient
{
	public class PreimageRevealClient
	{
        swaggerClient swaggerClient;
        HubConnection connection;

        public PreimageRevealClient(swaggerClient swaggerClient)
		{
            this.swaggerClient = swaggerClient;
        }

		public async Task ConnectAsync(string authToken)
		{
            connection = new HubConnectionBuilder()
                .WithUrl(swaggerClient.BaseUrl + "preimagereveal?authtoken=" + Uri.EscapeDataString(authToken))
                .WithAutomaticReconnect()
                .Build();
            await connection.StartAsync();
        }

        public async void MonitorAsync(string authToken, string paymentHash)
        {
            await connection.SendAsync("Monitor", authToken, paymentHash);
        }

        public IAsyncEnumerable<string> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            return connection.StreamAsync<string>("StreamAsync", authToken, cancellationToken);
        }
    }
}

