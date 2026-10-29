using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    /// <summary>
    /// Version-pinned failed/withheld screening for exam controllers. Eligibility
    /// for retake or improvement requires a separate approved academic policy.
    /// </summary>
    public async Task<ApiResponse<ResultFollowUpPageDto>> SearchResultFollowUpAsync(
        ResultFollowUpQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<ResultFollowUpPageDto>("Examination follow-up permission required.", 403);
        if (request == null || request.PublicationId <= 0 ||
            request.Page is < 1 or > 100000 || request.PageSize is < 1 or > 50 ||
            request.Search?.Trim().Length > 100 ||
            request.Status is not (ResultFollowUpStatus.Failed or
                ResultFollowUpStatus.Withheld or ResultFollowUpStatus.Both))
            return Error<ResultFollowUpPageDto>("Invalid follow-up report scope or page.");

        var tenant = _user.TenantId;
        var publication = await _publications.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == request.PublicationId && !x.IsDeleted, ct);
        if (publication == null) return Error<ResultFollowUpPageDto>("Publication not found.", 404);
        if (publication.State is not (ResultPublicationState.Published or ResultPublicationState.Withdrawn))
            return Error<ResultFollowUpPageDto>("Only finalized publication versions may be reviewed.", 409);

        var scope = await (from exam in _assessments.GetQueryable().AsNoTracking()
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { exam.TenantId, CampusId = exam.CampusId, YearId = exam.AcademicYearId }
                equals new { batch.TenantId, CampusId = batch.CampusId, YearId = batch.AcademicYearId }
            where exam.TenantId == tenant && exam.Id == publication.AssessmentId &&
                batch.Id == publication.AcademicBatchId && !exam.IsDeleted && !batch.IsDeleted
            select new { exam.CampusId, exam.AcademicYearId,
                AssessmentName = exam.Name, BatchName = batch.Name }).FirstOrDefaultAsync(ct);
        if (scope == null) return Error<ResultFollowUpPageDto>("Publication scope requires reconciliation.", 409);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<ResultFollowUpPageDto>("Campus access denied.", 403);

        // Full publication integrity is checked BEFORE filtering; corrupt rows
        // cannot disappear from a seemingly legitimate failed/withheld register.
        var proof = await GetPublicationEvidenceAsync([publication.Id],
            scope.CampusId, scope.AcademicYearId, ct);
        if (!proof.TryGetValue(publication.Id, out var evidence) || !evidence.IsReconciled)
            return Error<ResultFollowUpPageDto>("Historical snapshot requires reconciliation.", 409);

        var query = from summary in _summaries.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { summary.TenantId, Id = summary.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where summary.TenantId == tenant && summary.ResultPublicationId == publication.Id &&
                summary.AssessmentId == publication.AssessmentId &&
                summary.PublicationVersionNo == publication.VersionNo &&
                enrollment.AcademicBatchId == publication.AcademicBatchId &&
                enrollment.AcademicYearId == scope.AcademicYearId &&
                enrollment.CampusId == scope.CampusId &&
                !summary.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted
            select new { summary, enrollment, student };

        // This is outcome screening, not an eligibility verdict. A withheld result
        // must never be misrepresented as a confirmed failed result.
        query = request.Status switch
        {
            ResultFollowUpStatus.Failed => query.Where(x => !x.summary.IsPassed && !x.summary.IsWithheld),
            ResultFollowUpStatus.Withheld => query.Where(x => x.summary.IsWithheld),
            _ => query.Where(x => !x.summary.IsPassed || x.summary.IsWithheld)
        };
        var search = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.enrollment.RollNo.StartsWith(search) ||
                x.student.StudentCode.StartsWith(search) || x.student.FullName.StartsWith(search));

        var total = await query.CountAsync(ct);
        var skip = ((long)request.Page - 1) * request.PageSize;
        if (skip > int.MaxValue)
            return Error<ResultFollowUpPageDto>("Report page exceeds supported range.");
        var items = await query.OrderBy(x => x.enrollment.RollNo).ThenBy(x => x.summary.Id)
            .Skip((int)skip).Take(request.PageSize)
            .Select(x => new ResultFollowUpRowDto
            {
                StudentReference = x.student.PublicId, StudentCode = x.student.StudentCode,
                StudentName = x.student.FullName, RollNo = x.enrollment.RollNo,
                ObtainedMarks = x.summary.ObtainedMarks, TotalMarks = x.summary.TotalMarks,
                GPA = x.summary.GPA, GradeLetter = x.summary.GradeLetter,
                IsWithheld = x.summary.IsWithheld
            }).ToListAsync(ct);

        return ApiResponse<ResultFollowUpPageDto>.SuccessResponse(new()
        {
            PublicationId = publication.Id, VersionNo = publication.VersionNo,
            AssessmentName = scope.AssessmentName, AcademicBatchName = scope.BatchName,
            Status = request.Status, TotalCount = total, Page = request.Page,
            PageSize = request.PageSize, Items = items
        });
    }
}
