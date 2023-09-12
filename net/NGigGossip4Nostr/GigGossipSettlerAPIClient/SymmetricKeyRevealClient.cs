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

		public void Connect(string authToken)
		{
            connection = new HubConnectionBuilder().WithUrl(swaggerClient.BaseUrl + "symmetrickeyreveal?authtoken=" + Uri.EscapeDataString(authToken)).Build();
            connection.StartAsync().Wait();
        }

        public void Monitor(string authToken, Guid gigId, string replierPublicKey)
        {
            connection.SendAsync("Monitor", authToken, gigId, replierPublicKey).Wait();
        }

        public IAsyncEnumerable<string> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            return connection.StreamAsync<string>("Streaming", authToken, cancellationToken);
        }
    }
}

