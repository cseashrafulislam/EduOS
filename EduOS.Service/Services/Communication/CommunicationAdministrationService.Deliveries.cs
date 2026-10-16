using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Communication;

public sealed partial class CommunicationAdministrationService
{
    /// <summary>TenantAdmin delivery history. Never returns message bodies or provider credentials.</summary>
    public async Task<ApiResponse<PagedResult<CommunicationDeliveryDto>>> GetDeliveriesAsync(
        int page, int pageSize, int? channel, int? state, CancellationToken ct = default)
    {
        if (!CanManage())
            return ApiResponse<PagedResult<CommunicationDeliveryDto>>.ErrorResponse("Tenant administrator required.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize ||
            (channel.HasValue && !Enum.IsDefined(typeof(NotificationChannelType), channel.Value)) ||
            (state.HasValue && !Enum.IsDefined(typeof(DeliveryState), state.Value)))
            return ApiResponse<PagedResult<CommunicationDeliveryDto>>.ErrorResponse("Invalid delivery filters.");
        var query = _deliveries.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted);
        if (channel.HasValue)
        {
            var selected = (NotificationChannelType)channel.Value;
            query = query.Where(x => x.Channel == selected);
        }
        if (state.HasValue)
        {
            var selected = (DeliveryState)state.Value;
            query = query.Where(x => x.State == selected);
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<CommunicationDeliveryDto>>.SuccessResponse(new()
        {
            Items = rows.Select(x => new CommunicationDeliveryDto
            {
                Id = x.Id, IdempotencyKey = x.IdempotencyKey, Channel = x.Channel,
                Destination = x.Destination, Subject = x.Subject, State = x.State,
                AttemptCount = x.AttemptCount, SentAt = x.SentAt,
                DeliveredAt = x.DeliveredAt, FailureReason = x.FailureReason
            }).ToList(),
            TotalCount = count, Page = page, PageSize = pageSize
        });
    }
}
