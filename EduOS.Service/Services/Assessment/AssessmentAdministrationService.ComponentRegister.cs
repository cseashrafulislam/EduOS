using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<AssessmentComponentRegisterPageDto>> SearchAssessmentComponentsAsync(
        AssessmentComponentRegisterQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<AssessmentComponentRegisterPageDto>("Assessment management permission required.", 403);
        if (request == null || request.Page is < 1 or > 100000 ||
            request.PageSize is < 1 or > 100 || request.AssessmentSubjectId is <= 0 ||
            request.Search?.Length > 100)
            return Error<AssessmentComponentRegisterPageDto>("Invalid component register filters.");
        var tenant = _user.TenantId;
        var query =
            from component in _components.GetQueryable().AsNoTracking()
            join subject in _subjects.GetQueryable().AsNoTracking()
                on component.AssessmentSubjectId equals subject.Id
            join exam in _assessments.GetQueryable().AsNoTracking()
                on subject.AssessmentId equals exam.Id
            join offering in _offerings.GetQueryable().AsNoTracking()
                on subject.SubjectOfferingId equals offering.Id
            join batch in _batches.GetQueryable().AsNoTracking()
                on offering.AcademicBatchId equals batch.Id
            join curriculum in _curriculumSubjects.GetQueryable().AsNoTracking()
                on offering.CurriculumSubjectId equals curriculum.Id
            join name in _subjectNames.GetQueryable().AsNoTracking()
                on curriculum.SubjectId equals name.Id
            where component.TenantId == tenant && subject.TenantId == tenant &&
                exam.TenantId == tenant && offering.TenantId == tenant &&
                batch.TenantId == tenant && curriculum.TenantId == tenant &&
                name.TenantId == tenant && !component.IsDeleted && !subject.IsDeleted &&
                !exam.IsDeleted && !offering.IsDeleted && !batch.IsDeleted &&
                !curriculum.IsDeleted && !name.IsDeleted &&
                exam.CampusId == batch.CampusId && exam.AcademicYearId == batch.AcademicYearId
            select new { component, subject, exam, batch, name };
        if (IsRestrictedCampusManager())
            query = query.Where(x => AllowedCampusIds(tenant).Contains(x.exam.CampusId));
        if (request.AssessmentSubjectId.HasValue)
            query = query.Where(x => x.subject.Id == request.AssessmentSubjectId.Value);
        var search = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.component.Name.Contains(search) ||
                x.component.Code.Contains(search) || x.exam.Name.Contains(search) ||
                x.name.Name.Contains(search));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.exam.StartDate)
            .ThenBy(x => x.exam.Id).ThenBy(x => x.batch.Name)
            .ThenBy(x => x.subject.Id).ThenBy(x => x.component.DisplayOrder)
            .ThenBy(x => x.component.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new
            {
                x.component.Id, x.component.AssessmentSubjectId, x.component.Name,
                x.component.Code, x.component.FullMarks, x.component.PassMarks,
                x.component.Weightage, x.component.IsMandatoryPass,
                x.component.DisplayOrder, x.component.RowVersion,
                AssessmentId = x.exam.Id, AssessmentName = x.exam.Name,
                x.exam.State, BatchName = x.batch.Name, SubjectName = x.name.Name
            }).ToListAsync(ct);
        IReadOnlyList<AssessmentComponentRegisterRowDto> items = rows.Select(x =>
            new AssessmentComponentRegisterRowDto
            {
                Id = x.Id, AssessmentId = x.AssessmentId,
                AssessmentSubjectId = x.AssessmentSubjectId,
                AssessmentName = x.AssessmentName, BatchName = x.BatchName,
                SubjectName = x.SubjectName, State = x.State,
                Name = x.Name, Code = x.Code, FullMarks = x.FullMarks,
                PassMarks = x.PassMarks, Weightage = x.Weightage,
                IsMandatoryPass = x.IsMandatoryPass, DisplayOrder = x.DisplayOrder,
                RowVersion = Version(x.RowVersion)
            }).ToList();
        return ApiResponse<AssessmentComponentRegisterPageDto>.SuccessResponse(new()
        {
            Page = request.Page, PageSize = request.PageSize, TotalCount = total, Items = items
        });
    }
}
