using Microsoft.Extensions.Logging;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.Entities.Communication;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Communication;

public sealed partial class CommunicationAdministrationService
{
    public async Task<ApiResponse<PagedResult<MessageTemplateDto>>> GetTemplatesAsync(int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<PagedResult<MessageTemplateDto>>.ErrorResponse("Tenant administrator access is required.", 403);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _templates.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId);
        var count = await query.CountAsync(cancellationToken);
        var offset = ((long)page - 1) * pageSize;
        var rows = offset > int.MaxValue ? new List<MessageTemplate>() : await query
            .OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((int)offset).Take(pageSize).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<MessageTemplateDto>>.SuccessResponse(new PagedResult<MessageTemplateDto>
        { Items = rows.Select(Map).ToList(), TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<MessageTemplateDto>> SaveTemplateAsync(long? templateId,
        SaveMessageTemplateRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<MessageTemplateDto>.ErrorResponse("Tenant administrator access is required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 100
            || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200
            || string.IsNullOrWhiteSpace(request.BodyTemplate) || request.BodyTemplate.Length > 10000
            || request.SubjectTemplate?.Length > 250 || !ValidChannel(request.Channel))
            return ApiResponse<MessageTemplateDto>.ErrorResponse("Template details are invalid.");
        var code = request.Code.Trim().ToUpperInvariant();
        try
        {
            var query = _templates.GetQueryable().Where(x => x.TenantId == _user.TenantId);
            var row = templateId.HasValue
                ? await query.FirstOrDefaultAsync(x => x.Id == templateId.Value, cancellationToken)
                : await query.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);
            if (templateId.HasValue && row == null) return ApiResponse<MessageTemplateDto>.ErrorResponse("Template not found.", 404);
            if (row != null && templateId.HasValue && !MatchesVersion(row.RowVersion, request.RowVersion))
                return ApiResponse<MessageTemplateDto>.ErrorResponse("Template changed. Reload and retry.", 409);
            if (row != null && row.Code != code) return ApiResponse<MessageTemplateDto>.ErrorResponse("Template code cannot be changed.", 409);
            if (await query.AnyAsync(x => x.Code == code && x.Id != (row == null ? 0 : row.Id), cancellationToken))
                return ApiResponse<MessageTemplateDto>.ErrorResponse("Template code already exists.", 409);
            row ??= new MessageTemplate { TenantId = _user.TenantId, Code = code,
                CreatedAt = DateTime.UtcNow, CreatedBy = _user.UserId };
            row.Name = request.Name.Trim();
            row.Channel = request.Channel;
            row.SubjectTemplate = string.IsNullOrWhiteSpace(request.SubjectTemplate) ? null : request.SubjectTemplate.Trim();
            row.BodyTemplate = request.BodyTemplate;
            row.IsActive = request.IsActive;
            if (row.Id == 0) await _templates.AddAsync(row);
            else { row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId; }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<MessageTemplateDto>.SuccessResponse(Map(row));
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<MessageTemplateDto>.ErrorResponse("Template changed. Reload and retry.", 409); }
        catch (DbUpdateException) { return ApiResponse<MessageTemplateDto>.ErrorResponse("Template conflicts with another update.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Template save failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<MessageTemplateDto>.ErrorResponse("Template could not be saved.", 500);
        }
    }
}
