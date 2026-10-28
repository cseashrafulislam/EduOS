using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.AssessmentAdministration;

/// <summary>Read-only, tenant-scoped comparison of immutable result publication versions.</summary>
public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<PublishedVersionComparisonPageDto>> SearchPublishedVersionComparisonAsync(
        PublishedVersionComparisonQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<PublishedVersionComparisonPageDto>("Publication comparison permission required.", 403);
        if (request == null || request.PublicationId <= 0 ||
            request.BaselinePublicationId is <= 0 ||
            request.BaselinePublicationId == request.PublicationId ||
            request.Page is < 1 or > 100000 || request.PageSize is < 1 or > 50 ||
            request.Search?.Trim().Length > 100)
            return Error<PublishedVersionComparisonPageDto>("Invalid comparison scope or page.");

        var tenant = _user.TenantId;
        var target = await _publications.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == request.PublicationId && !x.IsDeleted, ct);
        if (target == null) return Error<PublishedVersionComparisonPageDto>("Publication not found.", 404);
        if (target.State is not (ResultPublicationState.Published or ResultPublicationState.Withdrawn))
            return Error<PublishedVersionComparisonPageDto>("Only finalized versions may be compared.", 409);
        var scope = await (from exam in _assessments.GetQueryable().AsNoTracking()
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { exam.TenantId, CampusId = exam.CampusId, YearId = exam.AcademicYearId }
                equals new { batch.TenantId, CampusId = batch.CampusId, YearId = batch.AcademicYearId }
            where exam.TenantId == tenant && exam.Id == target.AssessmentId &&
                batch.Id == target.AcademicBatchId && !exam.IsDeleted && !batch.IsDeleted
            select new { exam.CampusId, exam.AcademicYearId }).FirstOrDefaultAsync(ct);
        if (scope == null) return Error<PublishedVersionComparisonPageDto>("Publication scope is inconsistent.", 409);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<PublishedVersionComparisonPageDto>("Campus access denied.", 403);

        var older = _publications.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AssessmentId == target.AssessmentId &&
            x.AcademicBatchId == target.AcademicBatchId && x.VersionNo < target.VersionNo &&
            !x.IsDeleted && (x.State == ResultPublicationState.Published ||
                x.State == ResultPublicationState.Withdrawn));
        if (request.BaselinePublicationId.HasValue)
            older = older.Where(x => x.Id == request.BaselinePublicationId.Value);
        var baseline = await older.OrderByDescending(x => x.VersionNo).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        if (baseline == null)
            return Error<PublishedVersionComparisonPageDto>(
                request.BaselinePublicationId.HasValue ? "Baseline publication not found." :
                "No earlier finalized version exists.", request.BaselinePublicationId.HasValue ? 404 : 409);

        // Both complete snapshots must reconcile BEFORE search, filtering or page export.
        var proof = await GetPublicationEvidenceAsync([baseline.Id, target.Id],
            scope.CampusId, scope.AcademicYearId, ct);
        if (!proof.TryGetValue(baseline.Id, out var oldProof) || !oldProof.IsReconciled ||
            !proof.TryGetValue(target.Id, out var newProof) || !newProof.IsReconciled)
            return Error<PublishedVersionComparisonPageDto>(
                "One or both historical snapshots require reconciliation.", 409);
        var snapshots = _summaries.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted &&
            (x.ResultPublicationId == baseline.Id || x.ResultPublicationId == target.Id));
        var expectedCount = await snapshots.CountAsync(ct);
        var validCount = await (from row in snapshots
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { row.TenantId, Id = row.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where !enrollment.IsDeleted && !student.IsDeleted &&
                row.AssessmentId == target.AssessmentId &&
                ((row.ResultPublicationId == baseline.Id && row.PublicationVersionNo == baseline.VersionNo) ||
                 (row.ResultPublicationId == target.Id && row.PublicationVersionNo == target.VersionNo)) &&
                enrollment.AcademicBatchId == target.AcademicBatchId &&
                enrollment.CampusId == scope.CampusId &&
                enrollment.AcademicYearId == scope.AcademicYearId
            select row.Id).CountAsync(ct);
        if (validCount != expectedCount)
            return Error<PublishedVersionComparisonPageDto>("Historical enrollment provenance requires reconciliation.", 409);

        var beforeRows = snapshots.Where(x => x.ResultPublicationId == baseline.Id);
        var afterRows = snapshots.Where(x => x.ResultPublicationId == target.Id);
        // UNION, left joins, filtering, ordering and paging all remain in SQL.
        var keys = beforeRows.Select(x => x.StudentEnrollmentId)
            .Union(afterRows.Select(x => x.StudentEnrollmentId));
        var query = from enrollmentId in keys
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on enrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            join prior in beforeRows on enrollmentId equals prior.StudentEnrollmentId into priorGroup
            from prior in priorGroup.DefaultIfEmpty()
            join current in afterRows on enrollmentId equals current.StudentEnrollmentId into currentGroup
            from current in currentGroup.DefaultIfEmpty()
            where enrollment.TenantId == tenant && student.TenantId == tenant &&
                !enrollment.IsDeleted && !student.IsDeleted &&
                enrollment.AcademicBatchId == target.AcademicBatchId &&
                enrollment.CampusId == scope.CampusId &&
                enrollment.AcademicYearId == scope.AcademicYearId
            select new { enrollment, student, prior, current };
        var term = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.enrollment.RollNo.Contains(term) ||
                x.student.StudentCode.Contains(term) || x.student.FullName.Contains(term));
        if (request.ChangesOnly)
            query = query.Where(x => x.prior == null || x.current == null ||
                x.prior.TotalMarks != x.current.TotalMarks ||
                x.prior.ObtainedMarks != x.current.ObtainedMarks ||
                x.prior.Percentage != x.current.Percentage ||
                x.prior.GPA != x.current.GPA || x.prior.CGPA != x.current.CGPA ||
                x.prior.GradeLetter != x.current.GradeLetter ||
                x.prior.MeritPosition != x.current.MeritPosition ||
                x.prior.IsPassed != x.current.IsPassed ||
                x.prior.IsWithheld != x.current.IsWithheld);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.enrollment.RollNo).ThenBy(x => x.enrollment.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new PublishedVersionComparisonRowDto
            {
                StudentEnrollmentId = x.enrollment.Id,
                RollNo = x.enrollment.RollNo, StudentCode = x.student.StudentCode,
                StudentName = x.student.FullName,
                Change = x.prior == null ? "Added" : x.current == null ? "Removed" :
                    x.prior.TotalMarks != x.current.TotalMarks ||
                    x.prior.ObtainedMarks != x.current.ObtainedMarks ||
                    x.prior.Percentage != x.current.Percentage ||
                    x.prior.GPA != x.current.GPA || x.prior.CGPA != x.current.CGPA ||
                    x.prior.GradeLetter != x.current.GradeLetter ||
                    x.prior.MeritPosition != x.current.MeritPosition ||
                    x.prior.IsPassed != x.current.IsPassed ||
                    x.prior.IsWithheld != x.current.IsWithheld ? "Changed" : "Unchanged",
                BeforeObtainedMarks = x.prior == null ? null : x.prior.ObtainedMarks,
                AfterObtainedMarks = x.current == null ? null : x.current.ObtainedMarks,
                BeforeTotalMarks = x.prior == null ? null : x.prior.TotalMarks,
                AfterTotalMarks = x.current == null ? null : x.current.TotalMarks,
                BeforeGpa = x.prior == null ? null : x.prior.GPA,
                AfterGpa = x.current == null ? null : x.current.GPA,
                BeforeCgpa = x.prior == null ? null : x.prior.CGPA,
                AfterCgpa = x.current == null ? null : x.current.CGPA,
                BeforePercentage = x.prior == null ? null : x.prior.Percentage,
                AfterPercentage = x.current == null ? null : x.current.Percentage,
                BeforeGrade = x.prior == null ? null : x.prior.GradeLetter,
                AfterGrade = x.current == null ? null : x.current.GradeLetter,
                BeforeMerit = x.prior == null ? null : x.prior.MeritPosition,
                AfterMerit = x.current == null ? null : x.current.MeritPosition,
                BeforePassed = x.prior == null ? null : x.prior.IsPassed,
                AfterPassed = x.current == null ? null : x.current.IsPassed,
                BeforeWithheld = x.prior == null ? null : x.prior.IsWithheld,
                AfterWithheld = x.current == null ? null : x.current.IsWithheld
            }).ToListAsync(ct);
        _logger.LogInformation("Result version comparison tenant {TenantId} actor {ActorId} baseline {BaselineId} target {TargetId} page {Page}",
            tenant, _user.UserId, baseline.Id, target.Id, request.Page);
        return ApiResponse<PublishedVersionComparisonPageDto>.SuccessResponse(new()
        {
            BaselinePublicationId = baseline.Id, BaselineVersionNo = baseline.VersionNo,
            PublicationId = target.Id, VersionNo = target.VersionNo,
            TotalCount = total, Page = request.Page, PageSize = request.PageSize, Items = items
        });
    }
}
