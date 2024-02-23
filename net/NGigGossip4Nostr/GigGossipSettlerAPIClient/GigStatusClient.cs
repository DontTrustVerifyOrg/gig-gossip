using System;
using Microsoft.AspNetCore.SignalR.Client;

namespace GigGossipSettlerAPIClient
{
	public class GigStatusClient
	{
        ISettlerAPI swaggerClient;
        public HubConnection Connection;

        public GigStatusClient(ISettlerAPI swaggerClient)
		{
            this.swaggerClient = swaggerClient;
        }

		public async Task ConnectAsync(string authToken)
		{
            Connection = new HubConnectionBuilder()
                .WithUrl(swaggerClient.BaseUrl + "gigstatus?authtoken=" + Uri.EscapeDataString(authToken))
                .Build();
            await Connection.StartAsync();
        }

        public async Task MonitorAsync(string authToken, Guid gigId, Guid replierCertificateId)
        {
            await Connection.SendAsync("Monitor", authToken, gigId, replierCertificateId);
        }

        public IAsyncEnumerable<string> StreamAsync(string authToken, CancellationToken cancellationToken)
        {
            return Connection.StreamAsync<string>("StreamAsync", authToken, cancellationToken);
        }
    }
}

