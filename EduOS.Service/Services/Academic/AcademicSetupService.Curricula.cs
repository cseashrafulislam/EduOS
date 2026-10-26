using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Academic;

public sealed partial class AcademicSetupService
{
    public async Task<ApiResponse<PagedResult<AcademicCurriculumDto>>> GetCurriculaAsync(
        string? search, long? academicProgramId, DateOnly? effectiveOn,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<PagedResult<AcademicCurriculumDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 80 ||
            academicProgramId is <= 0 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<AcademicCurriculumDto>>("Invalid curriculum search or page.");
        var tenant = _currentUser.TenantId;
        var query = _curricula.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted);
        if (academicProgramId.HasValue)
            query = query.Where(x => x.AcademicProgramId == academicProgramId.Value);
        if (effectiveOn.HasValue)
            query = query.Where(x => x.EffectiveFrom <= effectiveOn.Value &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= effectiveOn.Value));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var prefix = search.Trim();
            query = query.Where(x => x.Name.StartsWith(prefix) || x.Code.StartsWith(prefix));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.AcademicProgramId).ThenByDescending(x => x.VersionNo)
            .ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var counts = await _curriculumSubjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && ids.Contains(x.AcademicCurriculumId))
            .GroupBy(x => x.AcademicCurriculumId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var items = rows.Select(row =>
        {
            var dto = MapCurriculum(row);
            dto.SubjectCount = counts.GetValueOrDefault(row.Id);
            return dto;
        }).ToList();
        return ApiResponse<PagedResult<AcademicCurriculumDto>>.SuccessResponse(
            new PagedResult<AcademicCurriculumDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<AcademicCurriculumDto>> GetCurriculumAsync(
        long id, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<AcademicCurriculumDto>();
        if (id <= 0) return Error<AcademicCurriculumDto>("Valid curriculum ID is required.");
        var tenant = _currentUser.TenantId;
        var row = await _curricula.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
        if (row == null) return Error<AcademicCurriculumDto>("Curriculum not found.", 404);
        var mappings = await _curriculumSubjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.AcademicCurriculumId == id && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToListAsync(ct);
        var levelIds = mappings.Select(x => x.AcademicLevelId).Distinct().ToArray();
        var subjectIds = mappings.Select(x => x.SubjectId).Distinct().ToArray();
        var levels = await _levels.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && levelIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var subjects = await _subjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && subjectIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Code }).ToListAsync(ct);
        var subjectNames = subjects.ToDictionary(x => x.Id);
        var dto = MapCurriculum(row);
        dto.SubjectCount = mappings.Count;
        dto.Subjects = mappings.Select(mapping =>
        {
            var item = MapCurriculumSubject(mapping);
            item.AcademicLevelName = levels.GetValueOrDefault(mapping.AcademicLevelId) ?? string.Empty;
            if (subjectNames.TryGetValue(mapping.SubjectId, out var subject))
            {
                item.SubjectName = subject.Name;
                item.SubjectCode = subject.Code;
            }
            return item;
        }).ToList();
        return ApiResponse<AcademicCurriculumDto>.SuccessResponse(dto);
    }
}
