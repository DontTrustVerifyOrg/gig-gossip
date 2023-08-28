using System;
using Microsoft.AspNetCore.SignalR;

namespace GigLNDWalletAPI;

public class InvoiceStatusUpdatesHub : Hub
{
    public InvoiceStatusUpdatesHub()
    {
    }

    public override async Task OnConnectedAsync()
    {
        var authToken = Context?.GetHttpContext()?.GetRouteValue("authtoken") as string;
        await Groups.AddToGroupAsync(Context?.ConnectionId, authToken);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var authToken = Context?.GetHttpContext()?.GetRouteValue("authtoken") as string;
        await Groups.RemoveFromGroupAsync(Context?.ConnectionId, authToken);
        await base.OnDisconnectedAsync(exception);
    }

    public async IAsyncEnumerable<DateTime> Streaming(CancellationToken cancellationToken)
    {
        this.Groups.AddToGroupAsync
        while (true)
        {
            yield return DateTime.UtcNow;
            await Task.Delay(1000, cancellationToken);
        }
    }

}

