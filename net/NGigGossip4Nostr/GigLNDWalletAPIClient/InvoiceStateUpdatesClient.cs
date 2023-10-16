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

		public async Task ConnectAsync(string authToken)
		{
            connection = new HubConnectionBuilder()
                .WithUrl(swaggerClient.BaseUrl + "invoicestateupdates?authtoken=" + Uri.EscapeDataString(authToken))
                .WithAutomaticReconnect()
                .Build();
            await connection.StartAsync();
        }

        public async Task MonitorAsync(string authToken, string paymentHash)
        {
            await connection.SendAsync("Monitor", authToken, paymentHash);
        }

        public IAsyncEnumerable<string> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            return connection.StreamAsync<string>("Streaming", authToken, cancellationToken);
        }
    }
}

