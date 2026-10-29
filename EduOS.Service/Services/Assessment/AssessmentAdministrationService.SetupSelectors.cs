using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<AssessmentSetupReferenceDataDto>> GetAssessmentSetupReferenceDataAsync(
        CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<AssessmentSetupReferenceDataDto>("Exam setup permission required.", 403);
        var tenant = _user.TenantId;
        var campuses = _campuses.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted);
        if (IsRestrictedCampusManager())
            campuses = campuses.Where(x => AllowedCampusIds(tenant).Contains(x.Id));
        var campusRows = await campuses.OrderBy(x => x.Name)
            .Select(x => new AssessmentSetupCampusDto { Id = x.Id, Name = x.Name })
            .Take(1000).ToListAsync(ct);
        var campusIds = campusRows.Select(x => x.Id).ToArray();
        var years = await _years.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted &&
            (x.CampusId == null || campusIds.Contains(x.CampusId.Value)))
            .OrderByDescending(x => x.StartDate)
            .Select(x => new AssessmentSetupYearDto { Id = x.Id, CampusId = x.CampusId, Name = x.Name })
            .Take(2000).ToListAsync(ct);
        var yearIds = years.Select(x => x.Id).ToArray();
        var terms = await _terms.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted && yearIds.Contains(x.AcademicYearId))
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id)
            .Select(x => new AssessmentSetupTermDto { Id = x.Id, AcademicYearId = x.AcademicYearId, Name = x.Name })
            .Take(4000).ToListAsync(ct);
        return ApiResponse<AssessmentSetupReferenceDataDto>.SuccessResponse(new()
        { Campuses = campusRows, AcademicYears = years, AcademicTerms = terms });
    }

    private static bool ValidSetupPage(AssessmentSetupListQueryDto? q) =>
        q is { Page: >= 1 and <= 100000, PageSize: >= 1 and <= 50 } &&
        (q.Search == null || q.Search.Length <= 100) && (q.CampusId is null or > 0) &&
        (q.AcademicYearId is null or > 0) && ((long)q.Page - 1) * q.PageSize <= int.MaxValue;

    public async Task<ApiResponse<PagedResult<AssessmentSetupListItemDto>>> SearchAssessmentDefinitionsAsync(
        AssessmentSetupListQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<PagedResult<AssessmentSetupListItemDto>>("Exam setup permission required.", 403);
        if (!ValidSetupPage(request)) return Error<PagedResult<AssessmentSetupListItemDto>>("Invalid search or page.");
        var tenant = _user.TenantId;
        var q = _assessments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted);
        if (IsRestrictedCampusManager()) q = q.Where(x => AllowedCampusIds(tenant).Contains(x.CampusId));
        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(x => x.Code.StartsWith(search) || x.Name.StartsWith(search));
        var count = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new AssessmentSetupListItemDto
            {
                Reference = x.PublicId, Name = x.Name, Code = x.Code,
                CampusId = x.CampusId, AcademicYearId = x.AcademicYearId,
                State = x.State, AttemptMode = x.AttemptMode
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<AssessmentSetupListItemDto>>.SuccessResponse(new()
        { Page = request.Page, PageSize = request.PageSize, TotalCount = count, Items = rows });
    }

    public async Task<ApiResponse<PagedResult<AssessmentSetupBatchDto>>> SearchAssessmentSetupBatchesAsync(
        AssessmentSetupListQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<PagedResult<AssessmentSetupBatchDto>>("Exam setup permission required.", 403);
        if (!ValidSetupPage(request)) return Error<PagedResult<AssessmentSetupBatchDto>>("Invalid search or page.");
        var tenant = _user.TenantId;
        var q = _batches.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted);
        if (request.CampusId.HasValue) q = q.Where(x => x.CampusId == request.CampusId.Value);
        if (request.AcademicYearId.HasValue) q = q.Where(x => x.AcademicYearId == request.AcademicYearId.Value);
        if (IsRestrictedCampusManager()) q = q.Where(x => AllowedCampusIds(tenant).Contains(x.CampusId));
        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(x => x.Code.StartsWith(search) || x.Name.StartsWith(search));
        var count = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new AssessmentSetupBatchDto
            {
                Id = x.Id, Name = x.Name, Code = x.Code,
                CampusId = x.CampusId, AcademicYearId = x.AcademicYearId
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<AssessmentSetupBatchDto>>.SuccessResponse(new()
        { Page = request.Page, PageSize = request.PageSize, TotalCount = count, Items = rows });
    }

    public async Task<ApiResponse<PagedResult<AssessmentSetupOfferingDto>>> SearchAssessmentSetupOfferingsAsync(
        long academicBatchId, AssessmentSetupListQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<PagedResult<AssessmentSetupOfferingDto>>("Exam setup permission required.", 403);
        if (academicBatchId <= 0 || !ValidSetupPage(request))
            return Error<PagedResult<AssessmentSetupOfferingDto>>("Invalid batch, search or page.");
        var tenant = _user.TenantId;
        var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == academicBatchId && x.IsActive && !x.IsDeleted, ct);
        if (batch == null || !await CanAccessCampusAsync(batch.CampusId, ct))
            return Error<PagedResult<AssessmentSetupOfferingDto>>("Batch not found.", 404);
        var q = from offering in _offerings.GetQueryable().AsNoTracking()
            join curriculum in _curriculumSubjects.GetQueryable().AsNoTracking()
                on offering.CurriculumSubjectId equals curriculum.Id
            join subject in _subjectNames.GetQueryable().AsNoTracking() on curriculum.SubjectId equals subject.Id
            where offering.TenantId == tenant && curriculum.TenantId == tenant && subject.TenantId == tenant &&
                offering.AcademicBatchId == batch.Id && offering.AcademicYearId == batch.AcademicYearId &&
                offering.IsActive && !offering.IsDeleted && !curriculum.IsDeleted && !subject.IsDeleted
            select new { offering, SubjectName = subject.Name, SubjectCode = subject.Code };
        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(x => x.offering.Code.StartsWith(search) || x.SubjectCode.StartsWith(search) ||
                x.SubjectName.StartsWith(search));
        var count = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.offering.Code).ThenBy(x => x.offering.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new AssessmentSetupOfferingDto
            {
                Reference = x.offering.PublicId, Code = x.offering.Code, SubjectName = x.SubjectName
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<AssessmentSetupOfferingDto>>.SuccessResponse(new()
        { Page = request.Page, PageSize = request.PageSize, TotalCount = count, Items = rows });
    }
}
