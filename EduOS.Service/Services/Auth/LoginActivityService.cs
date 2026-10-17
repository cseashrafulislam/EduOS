using EduOS.Core.Common;
using EduOS.Core.DTOs.Auth;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Auth;

public sealed class LoginActivityService(ILoginHistoryRepository history, ICurrentUserService user) : ILoginActivityService
{
    public async Task<ApiResponse<PagedResult<LoginHistoryDto>>> GetMyHistoryAsync(
        int page, int pageSize, bool? success, CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.UserId <= 0 || user.TenantId <= 0)
            return ApiResponse<PagedResult<LoginHistoryDto>>.ErrorResponse("Institution session required.", 403);
        if (page < 1 || pageSize is < 1 or > 50 || (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<LoginHistoryDto>>.ErrorResponse("Invalid pagination.", 400);
        // The DbContext applies a tenant filter to login history. Keep the explicit
        // tenant and user predicates here so only this institution's own events appear.
        var query = history.GetQueryable().AsNoTracking().Where(x =>
            x.UserId == user.UserId && x.TenantId == user.TenantId);
        if (success.HasValue) query = query.Where(x => x.IsSuccess == success.Value);
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new LoginHistoryDto { Id = x.Id, OccurredAt = x.OccurredAt, IsSuccess = x.IsSuccess })
            .ToListAsync(cancellationToken);
        foreach (var item in items) item.OccurredAt = DateTime.SpecifyKind(item.OccurredAt, DateTimeKind.Utc);
        return ApiResponse<PagedResult<LoginHistoryDto>>.SuccessResponse(new PagedResult<LoginHistoryDto>
        { Items = items, TotalCount = count, Page = page, PageSize = pageSize });
    }
}
