using System;
using Microsoft.AspNetCore.SignalR.Client;
namespace GigLNDWalletAPIClient
{
	public class InvoiceStateUpdatesClient
	{
        swaggerClient swaggerClient;
        private readonly HttpMessageHandler? httpMessageHandler;
        HubConnection connection;

        public InvoiceStateUpdatesClient(swaggerClient swaggerClient, HttpMessageHandler? httpMessageHandler = null)
		{
            this.swaggerClient = swaggerClient;
            this.httpMessageHandler = httpMessageHandler;
        }

		public async Task ConnectAsync(string authToken)
		{
            var builder = new HubConnectionBuilder()
                .WithAutomaticReconnect();

            if (httpMessageHandler != null)
                builder.WithUrl(swaggerClient.BaseUrl + "invoicestateupdates?authtoken=" + Uri.EscapeDataString(authToken), (options) => { options.HttpMessageHandlerFactory = (messageHndl) => { return httpMessageHandler; }; });
            else
                builder.WithUrl(swaggerClient.BaseUrl + "invoicestateupdates?authtoken=" + Uri.EscapeDataString(authToken));

            connection = builder.Build();
            await connection.StartAsync();
        }

        public async Task MonitorAsync(string authToken, string paymentHash)
        {
            await connection.SendAsync("Monitor", authToken, paymentHash);
        }

        public IAsyncEnumerable<string> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            return connection.StreamAsync<string>("StreamAsync", authToken, cancellationToken);
        }
    }
}

