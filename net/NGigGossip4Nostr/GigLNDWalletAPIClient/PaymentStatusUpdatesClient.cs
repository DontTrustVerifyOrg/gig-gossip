using System;
using Microsoft.AspNetCore.SignalR.Client;
namespace GigLNDWalletAPIClient
{
	public class PaymentStatusUpdatesClient
	{
        swaggerClient swaggerClient;
        HubConnection connection;

        public PaymentStatusUpdatesClient(swaggerClient swaggerClient)
		{
            this.swaggerClient = swaggerClient;
        }

		public void Connect(string authToken)
		{
            connection = new HubConnectionBuilder().WithUrl(swaggerClient.BaseUrl + "paymentstatusupdates?authtoken=" + Uri.EscapeDataString(authToken)).Build();
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

