using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EduOS.App.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    private const string TenantGroupPrefix = "tenant:";

    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.GetHttpContext()?.Items["TenantId"] as long?;
        if (tenantId is null or <= 0)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroupPrefix + tenantId.Value);
        await base.OnConnectedAsync();
    }

    [Authorize(Roles = "TenantAdmin,Admin")]
    public async Task SendNotification(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var tenantId = Context.GetHttpContext()?.Items["TenantId"] as long?;
        if (tenantId is null or <= 0)
            throw new HubException("Tenant context is required.");

        var sender = Context.User?.Identity?.Name ?? "System";
        await Clients.Group(TenantGroupPrefix + tenantId.Value)
            .SendAsync("ReceiveNotification", sender, message.Trim());
    }
}
