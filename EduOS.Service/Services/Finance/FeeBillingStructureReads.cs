using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeBillingService
{
    // Tenant administrators are the only role with institution-wide cross-campus register access.
    // No mutation or accounting posting occurs in these read operations.
    public async Task<ApiResponse<PagedResult<FeeStructureRegisterDto>>> GetStructureRegisterAsync(
        long? campusId = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || !_user.IsTenantAdmin || _user.TenantId <= 0)
            return Error<PagedResult<FeeStructureRegisterDto>>("Tenant administrator access required.", 403);
        if (campusId is <= 0 || !ValidPage(page, pageSize))
            return Error<PagedResult<FeeStructureRegisterDto>>("Invalid campus or pagination.");
        var skip = Skip(page, pageSize);
        if (skip < 0) return Error<PagedResult<FeeStructureRegisterDto>>("Page exceeds limit.");
        var query = _structures.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && !x.IsDeleted &&
            (!campusId.HasValue || x.CampusId == campusId.Value));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
            .Skip(skip).Take(pageSize).Select(x => new FeeStructureRegisterDto
            {
                Id = x.Id, CampusId = x.CampusId, AcademicYearId = x.AcademicYearId,
                AcademicLevelId = x.AcademicLevelId, AcademicBatchId = x.AcademicBatchId,
                Name = x.Name, CurrencyCode = x.CurrencyCode, EffectiveFrom = x.EffectiveFrom,
                EffectiveTo = x.EffectiveTo, IsActive = x.IsActive
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<FeeStructureRegisterDto>>.SuccessResponse(
            new PagedResult<FeeStructureRegisterDto>
            {
                Page = page, PageSize = pageSize, TotalCount = count, Items = rows
            });
    }

    public async Task<ApiResponse<FeeStructureDto>> GetStructureAsync(
        long id, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || !_user.IsTenantAdmin || _user.TenantId <= 0)
            return Error<FeeStructureDto>("Tenant administrator access required.", 403);
        if (id <= 0) return Error<FeeStructureDto>("Invalid fee structure.");
        var structure = await _structures.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == id && !x.IsDeleted, ct);
        if (structure == null) return Error<FeeStructureDto>("Fee structure not found.", 404);
        return ApiResponse<FeeStructureDto>.SuccessResponse(await MapStructureAsync(structure, ct));
    }
}
