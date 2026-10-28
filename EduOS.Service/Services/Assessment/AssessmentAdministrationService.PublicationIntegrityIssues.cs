using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.AssessmentAdministration;

/// <summary>Read-only reconciliation diagnostics; never repairs or rewrites historical results.</summary>
public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<PublishedSnapshotIntegrityPageDto>> SearchPublishedSnapshotIssuesAsync(
        long publicationId, PublishedSnapshotIntegrityQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<PublishedSnapshotIntegrityPageDto>("Publication integrity permission required.", 403);
        if (publicationId <= 0 || request == null ||
            request.Page is < 1 or > 100000 || request.PageSize is < 1 or > 50)
            return Error<PublishedSnapshotIntegrityPageDto>("Invalid publication or integrity page.");

        var tenant = _user.TenantId;
        var publication = await _publications.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == publicationId && !x.IsDeleted, ct);
        if (publication == null)
            return Error<PublishedSnapshotIntegrityPageDto>("Publication not found.", 404);
        if (publication.State is not (ResultPublicationState.Published or ResultPublicationState.Withdrawn))
            return Error<PublishedSnapshotIntegrityPageDto>("Only finalized versions have historical snapshots.", 409);

        var scope = await (from exam in _assessments.GetQueryable().AsNoTracking()
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { exam.TenantId, CampusId = exam.CampusId, YearId = exam.AcademicYearId }
                equals new { batch.TenantId, CampusId = batch.CampusId, YearId = batch.AcademicYearId }
            where exam.TenantId == tenant && exam.Id == publication.AssessmentId &&
                batch.Id == publication.AcademicBatchId && !exam.IsDeleted && !batch.IsDeleted
            select new { exam.CampusId, exam.AcademicYearId }).FirstOrDefaultAsync(ct);
        if (scope == null)
            return Error<PublishedSnapshotIntegrityPageDto>("Publication scope is inconsistent.", 409);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<PublishedSnapshotIntegrityPageDto>("Campus access denied.", 403);

        // All diagnostics remain inside the publication's tenant; left-out or deleted
        // enrollment/student evidence becomes an issue, never a cross-tenant join.
        // Privileged read includes soft-deleted evidence, but explicitly re-applies tenant
        // isolation after bypassing the global filter. No other tenant's rows are joined.
        _logger.LogInformation("Historical result integrity inspection tenant {TenantId} actor {ActorId} publication {PublicationId}",
            tenant, _user.UserId, publicationId);
        var snapshots = _summaries.GetQueryable().AsNoTracking().IgnoreQueryFilters().Where(x =>
            x.TenantId == tenant && x.ResultPublicationId == publication.Id);
        var duplicates = snapshots.Where(x => !x.IsDeleted)
            .GroupBy(x => x.StudentEnrollmentId).Where(g => g.Count() > 1)
            .Select(g => g.Key);
        var validIds = from row in snapshots
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { row.TenantId, Id = row.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where !row.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted &&
                row.AssessmentId == publication.AssessmentId &&
                row.PublicationVersionNo == publication.VersionNo &&
                enrollment.AcademicBatchId == publication.AcademicBatchId &&
                enrollment.CampusId == scope.CampusId &&
                enrollment.AcademicYearId == scope.AcademicYearId &&
                row.TotalMarks >= 0m && row.ObtainedMarks >= 0m &&
                row.ObtainedMarks <= row.TotalMarks &&
                (!row.Percentage.HasValue || row.Percentage >= 0m && row.Percentage <= 100m) &&
                (!row.GPA.HasValue || row.GPA >= 0m) &&
                (!row.CGPA.HasValue || row.CGPA >= 0m)
            select row.Id;

        var invalid = snapshots.Where(row =>
            !validIds.Contains(row.Id) || duplicates.Contains(row.StudentEnrollmentId));
        var snapshotCount = await snapshots.CountAsync(ct);
        var invalidCount = await invalid.CountAsync(ct);
        var skip = (request.Page - 1) * request.PageSize;
        var page = await invalid.OrderBy(x => x.Id).Skip(skip).Take(request.PageSize)
            .Select(x => new
            {
                SummaryId = x.Id, x.StudentEnrollmentId, x.IsDeleted,
                VersionMismatch = x.AssessmentId != publication.AssessmentId ||
                    x.PublicationVersionNo != publication.VersionNo,
                NumericInvalid = x.TotalMarks < 0m || x.ObtainedMarks < 0m ||
                    x.ObtainedMarks > x.TotalMarks ||
                    x.Percentage.HasValue && (x.Percentage < 0m || x.Percentage > 100m) ||
                    x.GPA.HasValue && x.GPA < 0m ||
                    x.CGPA.HasValue && x.CGPA < 0m,
                Duplicate = duplicates.Contains(x.StudentEnrollmentId),
                ValidProvenance = validIds.Contains(x.Id)
            }).ToListAsync(ct);
        var rows = page.Select(x => new PublishedSnapshotIssueDto
        {
            SummaryId = x.SummaryId, StudentEnrollmentId = x.StudentEnrollmentId,
            IssueCode = x.IsDeleted ? "DeletedSnapshot" :
                x.VersionMismatch ? "VersionMismatch" :
                x.NumericInvalid ? "InvalidNumeric" :
                x.Duplicate ? "DuplicateEnrollment" :
                !x.ValidProvenance ? "EnrollmentScopeMismatch" : "RequiresReview"
        }).ToList();
        return ApiResponse<PublishedSnapshotIntegrityPageDto>.SuccessResponse(new()
        {
            PublicationId = publication.Id, VersionNo = publication.VersionNo,
            SnapshotCount = snapshotCount, InvalidCount = invalidCount,
            IsEmptySnapshot = snapshotCount == 0,
            IsReconciled = snapshotCount > 0 && invalidCount == 0,
            Page = request.Page, PageSize = request.PageSize, Items = rows
        });
    }
}
