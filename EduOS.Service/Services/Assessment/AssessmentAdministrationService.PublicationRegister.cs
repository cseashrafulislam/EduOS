using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<AssessmentPublicationHistoryPageDto>> SearchPublicationHistoryAsync(
        AssessmentPublicationHistoryQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<AssessmentPublicationHistoryPageDto>("Publication history permission required.", 403);
        if (request == null || request.AssessmentId <= 0 || request.AcademicBatchId <= 0 ||
            request.Page is < 1 or > 100000 || request.PageSize is < 1 or > 100)
            return Error<AssessmentPublicationHistoryPageDto>("Invalid publication history scope or page.");

        var tenant = _user.TenantId;
        // Do not require IsActive: archived batches still need their immutable publication history.
        var scope = await (from exam in _assessments.GetQueryable().AsNoTracking()
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { exam.TenantId, CampusId = exam.CampusId, YearId = exam.AcademicYearId }
                equals new { batch.TenantId, CampusId = batch.CampusId, YearId = batch.AcademicYearId }
            where exam.TenantId == tenant && exam.Id == request.AssessmentId &&
                batch.Id == request.AcademicBatchId && !exam.IsDeleted && !batch.IsDeleted
            select new { exam.CampusId }).FirstOrDefaultAsync(ct);
        if (scope == null) return Error<AssessmentPublicationHistoryPageDto>("Assessment or batch not found.", 404);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<AssessmentPublicationHistoryPageDto>("Campus access denied.", 403);

        var query = _publications.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AssessmentId == request.AssessmentId &&
            x.AcademicBatchId == request.AcademicBatchId && !x.IsDeleted);
        var total = await query.CountAsync(ct);
        var raw = await query.OrderByDescending(x => x.VersionNo).ThenByDescending(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new
            {
                x.Id, x.VersionNo, x.State, x.PublishedAt, x.PublishedByUserId,
                x.VisibleToStudent, x.VisibleToGuardian, x.PublishNote, x.RowVersion,
                SnapshotCount = _summaries.GetQueryable().AsNoTracking().Count(summary =>
                    summary.TenantId == tenant && summary.ResultPublicationId == x.Id &&
                    summary.PublicationVersionNo == x.VersionNo && !summary.IsDeleted)
            }).ToListAsync(ct);
        return ApiResponse<AssessmentPublicationHistoryPageDto>.SuccessResponse(new()
        {
            AssessmentId = request.AssessmentId, AcademicBatchId = request.AcademicBatchId,
            TotalCount = total, Page = request.Page, PageSize = request.PageSize,
            Items = raw.Select(x => new AssessmentPublicationHistoryRowDto
            {
                Id = x.Id, VersionNo = x.VersionNo, State = x.State,
                PublishedAt = x.PublishedAt, PublishedByUserId = x.PublishedByUserId,
                VisibleToStudent = x.VisibleToStudent, VisibleToGuardian = x.VisibleToGuardian,
                PublishNote = x.PublishNote, SnapshotCount = x.SnapshotCount,
                RowVersion = Version(x.RowVersion)
            }).ToList()
        });
    }

    public async Task<ApiResponse<PublishedTabulationPageDto>> SearchPublishedTabulationAsync(
        PublishedTabulationQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<PublishedTabulationPageDto>("Published tabulation permission required.", 403);
        if (request == null || request.PublicationId <= 0 || request.Page is < 1 or > 100000 ||
            request.PageSize is < 1 or > 100 || request.Search?.Trim().Length > 100)
            return Error<PublishedTabulationPageDto>("Invalid tabulation filters.");

        var tenant = _user.TenantId;
        var publication = await _publications.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == request.PublicationId && !x.IsDeleted, ct);
        if (publication == null) return Error<PublishedTabulationPageDto>("Publication not found.", 404);
        if (publication.State is not (ResultPublicationState.Published or ResultPublicationState.Withdrawn))
            return Error<PublishedTabulationPageDto>("Only finalized publication versions have tabulation snapshots.", 409);
        var scope = await (from exam in _assessments.GetQueryable().AsNoTracking()
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { exam.TenantId, CampusId = exam.CampusId, YearId = exam.AcademicYearId }
                equals new { batch.TenantId, CampusId = batch.CampusId, YearId = batch.AcademicYearId }
            where exam.TenantId == tenant && exam.Id == publication.AssessmentId &&
                batch.Id == publication.AcademicBatchId && !exam.IsDeleted && !batch.IsDeleted
            select new { exam.CampusId, AssessmentName = exam.Name, BatchName = batch.Name,
                AcademicYearId = exam.AcademicYearId }).FirstOrDefaultAsync(ct);
        if (scope == null) return Error<PublishedTabulationPageDto>("Publication scope is inconsistent.", 409);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<PublishedTabulationPageDto>("Campus access denied.", 403);

        // Validate the entire visible snapshot's enrollment provenance before applying a search filter.
        // A malformed cross-batch or cross-campus summary must not disappear behind paging/search.
        var snapshotCount = await _summaries.GetQueryable().AsNoTracking().CountAsync(x =>
            x.TenantId == tenant && x.ResultPublicationId == publication.Id && !x.IsDeleted, ct);
        var valid = from summary in _summaries.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { summary.TenantId, Id = summary.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where summary.TenantId == tenant && !summary.IsDeleted &&
                !enrollment.IsDeleted && !student.IsDeleted &&
                summary.ResultPublicationId == publication.Id &&
                summary.AssessmentId == publication.AssessmentId &&
                summary.PublicationVersionNo == publication.VersionNo &&
                enrollment.AcademicBatchId == publication.AcademicBatchId &&
                enrollment.CampusId == scope.CampusId &&
                enrollment.AcademicYearId == scope.AcademicYearId
            select new { summary, enrollment, student };
        if (await valid.CountAsync(ct) != snapshotCount)
            return Error<PublishedTabulationPageDto>(
                "Published result snapshot has inconsistent enrollment or version provenance.", 409);

        var term = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            valid = valid.Where(x => x.enrollment.RollNo.Contains(term) ||
                x.student.StudentCode.Contains(term) || x.student.FullName.Contains(term));
        var total = await valid.CountAsync(ct);
        var items = await valid.OrderBy(x => x.summary.MeritPosition == null)
            .ThenBy(x => x.summary.MeritPosition).ThenBy(x => x.enrollment.RollNo)
            .ThenBy(x => x.summary.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new PublishedTabulationRowDto
            {
                StudentReference = x.student.PublicId, StudentCode = x.student.StudentCode,
                StudentName = x.student.FullName, RollNo = x.enrollment.RollNo,
                TotalMarks = x.summary.TotalMarks, ObtainedMarks = x.summary.ObtainedMarks,
                Percentage = x.summary.Percentage, GPA = x.summary.GPA, CGPA = x.summary.CGPA,
                GradeLetter = x.summary.GradeLetter, MeritPosition = x.summary.MeritPosition,
                IsPassed = x.summary.IsPassed, IsWithheld = x.summary.IsWithheld
            }).ToListAsync(ct);
        return ApiResponse<PublishedTabulationPageDto>.SuccessResponse(new()
        {
            PublicationId = publication.Id, VersionNo = publication.VersionNo,
            State = publication.State, PublishedAt = publication.PublishedAt,
            AssessmentName = scope.AssessmentName, AcademicBatchName = scope.BatchName,
            TotalCount = total, Page = request.Page, PageSize = request.PageSize, Items = items
        });
    }
}
