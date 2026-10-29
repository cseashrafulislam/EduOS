using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<GradeSchemeRegisterPageDto>> SearchGradeSchemesAsync(
        GradeSchemeRegisterQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<GradeSchemeRegisterPageDto>("Assessment management permission required.", 403);
        if (request == null || request.Page is < 1 or > 100000 ||
            request.PageSize is < 1 or > 100 || request.Search?.Trim().Length > 100 ||
            request.AcademicProgramId is <= 0)
            return Error<GradeSchemeRegisterPageDto>("Invalid grade scheme filters.");
        var tenant = _user.TenantId;
        var query = _gradeSchemes.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted);
        if (request.AcademicProgramId.HasValue)
            query = query.Where(x => x.AcademicProgramId == request.AcademicProgramId.Value);
        if (request.ActiveOnly) query = query.Where(x => x.IsActive);
        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(x => x.Name.Contains(search) || x.Code.Contains(search));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var counts = await _grades.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && ids.Contains(x.GradeSchemeId))
            .GroupBy(x => x.GradeSchemeId)
            .Select(x => new { Id = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return ApiResponse<GradeSchemeRegisterPageDto>.SuccessResponse(
            new GradeSchemeRegisterPageDto
            {
                Page = request.Page, PageSize = request.PageSize, TotalCount = total,
                Items = rows.Select(x => new GradeSchemeRegisterRowDto
                {
                    Id = x.Id, Code = x.Code, Name = x.Name,
                    AcademicProgramId = x.AcademicProgramId,
                    IsDefault = x.IsDefault, IsActive = x.IsActive,
                    UseCreditWeightedGpa = x.UseCreditWeightedGpa,
                    RuleCount = counts.GetValueOrDefault(x.Id),
                    RowVersion = Version(x.RowVersion)
                }).ToList()
            });
    }

    public async Task<ApiResponse<GradeSchemeDto>> GetGradeSchemeAsync(
        long gradeSchemeId, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<GradeSchemeDto>("Assessment management permission required.", 403);
        if (gradeSchemeId <= 0) return Error<GradeSchemeDto>("Invalid grade scheme ID.");
        var tenant = _user.TenantId;
        var row = await _gradeSchemes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == gradeSchemeId && !x.IsDeleted, ct);
        if (row == null) return Error<GradeSchemeDto>("Grade scheme not found.", 404);
        var rules = await _grades.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.GradeSchemeId == row.Id && !x.IsDeleted)
            .OrderBy(x => x.MinMarks).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
        if (rules.Count > 100)
            return Error<GradeSchemeDto>("Grade scheme exceeds the supported rule limit.", 409);
        return ApiResponse<GradeSchemeDto>.SuccessResponse(MapGradeScheme(row, rules));
    }
}
