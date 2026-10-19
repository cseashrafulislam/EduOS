using System.Globalization;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Communication;

public sealed partial class CommunicationAdministrationService
{
    public async Task<ApiResponse<string>> ExportDeliveriesCsvAsync(int? channel, int? state, CancellationToken ct = default)
    {
        if (!CanManage()) return ApiResponse<string>.ErrorResponse("Tenant administrator required.", 403);
        if ((channel.HasValue && !Enum.IsDefined(typeof(NotificationChannelType), channel.Value)) ||
            (state.HasValue && !Enum.IsDefined(typeof(DeliveryState), state.Value)))
            return ApiResponse<string>.ErrorResponse("Invalid delivery filters.", 400);
        var query = _deliveries.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted);
        if (channel.HasValue) query = query.Where(x => x.Channel == (NotificationChannelType)channel.Value);
        if (state.HasValue) query = query.Where(x => x.State == (DeliveryState)state.Value);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Take(1001).Select(x => new
            {
                x.Id, x.IdempotencyKey, x.Channel, x.Destination, x.Subject,
                x.State, x.AttemptCount, x.SentAt, x.DeliveredAt, x.FailureReason
            }).ToListAsync(ct);
        if (rows.Count > 1000)
            return ApiResponse<string>.ErrorResponse("More than 1,000 matching deliveries. Narrow filters before exporting.", 413);
        var csv = new StringBuilder("Id,IdempotencyKey,Channel,Destination,Subject,State,Attempts,SentAtUtc,DeliveredAtUtc,FailureReason\r\n");
        foreach (var row in rows)
        {
            string?[] cells =
            {
                row.Id.ToString(CultureInfo.InvariantCulture), row.IdempotencyKey.ToString(),
                row.Channel.ToString(), row.Destination, row.Subject, row.State.ToString(),
                row.AttemptCount.ToString(CultureInfo.InvariantCulture),
                row.SentAt?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                row.DeliveredAt?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                row.FailureReason
            };
            csv.Append(string.Join(",", cells.Select(DeliveryCsvCell))).Append("\r\n");
        }
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
