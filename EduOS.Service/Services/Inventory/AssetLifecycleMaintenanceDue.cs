using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class AssetLifecycleService
{
    public async Task<ApiResponse<PagedResult<AssetMaintenanceDueDto>>> GetMaintenanceDueAsync(
        DateOnly? dueBy, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<PagedResult<AssetMaintenanceDueDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            (dueBy.HasValue && dueBy.Value.Year is < 2000 or > 2200))
            return Invalid<PagedResult<AssetMaintenanceDueDto>>("Invalid maintenance due date or paging parameters.");
        var cutoff = dueBy ?? Today;
        var tenant = Tenant;
        // A newer service without a next date supersedes an older overdue schedule.
        // The anti-join chooses exactly one latest non-deleted maintenance per asset.
        var query =
            from asset in VisibleAssets().AsNoTracking()
            join entry in _db.AssetMaintenances.AsNoTracking() on asset.Id equals entry.AssetId
            where asset.IsActive && entry.TenantId == tenant && !entry.IsDeleted &&
                entry.NextMaintenanceDate.HasValue && entry.NextMaintenanceDate.Value <= cutoff &&
                !_db.AssetMaintenances.AsNoTracking().Any(later =>
                    later.TenantId == tenant && !later.IsDeleted && later.AssetId == asset.Id &&
                    (later.MaintenanceDate > entry.MaintenanceDate ||
                     (later.MaintenanceDate == entry.MaintenanceDate && later.Id > entry.Id)))
            select new
            {
                asset.Id, asset.PublicId, asset.CampusId, asset.AssetTag, asset.Name,
                asset.StateCode, entry.MaintenanceDate, entry.NextMaintenanceDate
            };
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.NextMaintenanceDate).ThenBy(x => x.AssetTag)
            .ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var today = Today;
        return ApiResponse<PagedResult<AssetMaintenanceDueDto>>.SuccessResponse(
            new PagedResult<AssetMaintenanceDueDto>
            {
                Items = rows.Select(x => new AssetMaintenanceDueDto
                {
                    AssetReference = x.PublicId, CampusId = x.CampusId, AssetTag = x.AssetTag,
                    AssetName = x.Name, StateCode = x.StateCode,
                    LastMaintenanceDate = x.MaintenanceDate,
                    NextMaintenanceDate = x.NextMaintenanceDate!.Value,
                    DaysOverdue = Math.Max(0, today.DayNumber - x.NextMaintenanceDate.Value.DayNumber)
                }).ToList(),
                TotalCount = count, Page = page, PageSize = pageSize
            });
    }
}
