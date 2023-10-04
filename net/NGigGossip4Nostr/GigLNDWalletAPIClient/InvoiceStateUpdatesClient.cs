using System;
using Microsoft.AspNetCore.SignalR.Client;
namespace GigLNDWalletAPIClient
{
	public class InvoiceStateUpdatesClient
	{
        swaggerClient swaggerClient;
        HubConnection connection;

        public InvoiceStateUpdatesClient(swaggerClient swaggerClient)
		{
            this.swaggerClient = swaggerClient;
        }

		public void Connect(string authToken)
		{
            connection = new HubConnectionBuilder()
                .WithUrl(swaggerClient.BaseUrl + "invoicestateupdates?authtoken=" + Uri.EscapeDataString(authToken))
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

