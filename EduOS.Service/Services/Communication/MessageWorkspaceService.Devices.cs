using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Communication;

/// <summary>Tenant-and-user-scoped read model for existing push device registrations.</summary>
public sealed partial class MessageWorkspaceService
{
    public async Task<ApiResponse<PagedResult<DeviceTokenDto>>> GetMyDevicesAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!SignedIn)
            return ApiResponse<PagedResult<DeviceTokenDto>>.ErrorResponse("Sign in required.", 403);
        if (!PageOk(page, pageSize))
            return ApiResponse<PagedResult<DeviceTokenDto>>.ErrorResponse("Invalid paging.", 400);
        // Never return the protected push token. The existing CommunicationService owns registration/revocation.
        var query = db.DeviceTokens.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.UserId == user.UserId && !x.IsDeleted);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.LastSeenAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new DeviceTokenDto
            {
                Id = x.Id, Platform = x.Platform, DeviceId = x.DeviceId,
                IsActive = x.IsActive, LastSeenAt = x.LastSeenAt
            }).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<DeviceTokenDto>>.SuccessResponse(new()
        {
            Items = rows, TotalCount = total, Page = page, PageSize = pageSize
        });
    }
}
