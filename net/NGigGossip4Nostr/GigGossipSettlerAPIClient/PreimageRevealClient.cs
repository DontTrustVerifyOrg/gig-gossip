using System;
using Microsoft.AspNetCore.SignalR.Client;
namespace GigGossipSettlerAPIClient
{
	public class PreimageRevealClient
	{
        swaggerClient swaggerClient;
        public HubConnection Connection;

        public PreimageRevealClient(swaggerClient swaggerClient)
		{
            this.swaggerClient = swaggerClient;
        }

		public async Task ConnectAsync(string authToken)
		{
            Connection = new HubConnectionBuilder()
                .WithUrl(swaggerClient.BaseUrl + "preimagereveal?authtoken=" + Uri.EscapeDataString(authToken))
                .Build();
            await Connection.StartAsync();
        }

        public async Task MonitorAsync(string authToken, string paymentHash)
        {
            await Connection.SendAsync("Monitor", authToken, paymentHash);
        }

        public IAsyncEnumerable<string> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            return Connection.StreamAsync<string>("StreamAsync", authToken, cancellationToken);
        }
    }
}

