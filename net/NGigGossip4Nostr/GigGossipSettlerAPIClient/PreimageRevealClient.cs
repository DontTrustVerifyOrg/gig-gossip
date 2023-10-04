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

		public void Connect(string authToken)
		{
            connection = new HubConnectionBuilder()
                .WithUrl(swaggerClient.BaseUrl + "preimagereveal?authtoken=" + Uri.EscapeDataString(authToken))
                .WithAutomaticReconnect()
                .Build();
            connection.StartAsync().Wait();
        }

        public void Monitor(string authToken, string paymentHash)
        {
            connection.SendAsync("Monitor", authToken, paymentHash).Wait();
        }

        public IAsyncEnumerable<string> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            return connection.StreamAsync<string>("Streaming", authToken, cancellationToken);
        }
    }
}

