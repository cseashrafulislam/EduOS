using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Exams;

public sealed partial class ExamWorkflowService
{
    public async Task<ApiResponse<CertificateTemplateRegisterPageDto>> SearchCertificateTemplatesAsync(
        CertificateTemplateRegisterQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<CertificateTemplateRegisterPageDto>("Certificate management permission required.", 403);
        if (request == null || request.Page is < 1 or > 100000 ||
            request.PageSize is < 1 or > 100 || request.Search?.Trim().Length > 100)
            return Error<CertificateTemplateRegisterPageDto>("Invalid certificate template filters.");
        var tenant = _user.TenantId;
        var term = request.Search?.Trim();
        var query = _templates.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var lockedIds = await _certificates.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && ids.Contains(x.CertificateTemplateId))
            .Select(x => x.CertificateTemplateId).Distinct().ToListAsync(ct);
        var locked = lockedIds.ToHashSet();
        return ApiResponse<CertificateTemplateRegisterPageDto>.SuccessResponse(
            new CertificateTemplateRegisterPageDto
            {
                TotalCount = total, Page = request.Page, PageSize = request.PageSize,
                Items = rows.Select(x => new CertificateTemplateRegisterRowDto
                {
                    Id = x.Id, Code = x.Code, Name = x.Name,
                    IsActive = x.IsActive, IsDefault = x.IsDefault,
                    IsLocked = locked.Contains(x.Id),
                    RowVersion = Version(x.RowVersion)
                }).ToList()
            });
    }

    public async Task<ApiResponse<CertificateTemplateDto>> GetCertificateTemplateAsync(
        long templateId, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<CertificateTemplateDto>("Certificate management permission required.", 403);
        if (templateId <= 0) return Error<CertificateTemplateDto>("Invalid template ID.");
        var row = await _templates.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == templateId && !x.IsDeleted, ct);
        return row == null
            ? Error<CertificateTemplateDto>("Certificate template not found.", 404)
            : ApiResponse<CertificateTemplateDto>.SuccessResponse(MapTemplate(row));
    }
}
