using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    /// <summary>
    /// Authorized, bounded review queue. The latest decision is resolved in SQL using the
    /// tenant/subject/id index; no entire institute roster or decision history is loaded.
    /// </summary>
    public async Task<ApiResponse<PagedResult<AssessmentMarkReviewQueueItemDto>>> SearchMarkReviewQueueAsync(
        AssessmentMarkReviewQueueQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<PagedResult<AssessmentMarkReviewQueueItemDto>>("Review queue permission required.", 403);
        if (request == null || request.Page is < 1 or > 100000 ||
            request.PageSize is < 1 or > 50 || request.Search?.Length > 100 ||
            request.Status?.Length > 20 ||
            ((long)request.Page - 1) * request.PageSize > int.MaxValue)
            return Error<PagedResult<AssessmentMarkReviewQueueItemDto>>("Invalid review queue filter or page.");
        var status = (request.Status ?? "Pending").Trim().ToLowerInvariant();
        if (status is not ("pending" or "rejected" or "approved" or "unsubmitted" or "all"))
            return Error<PagedResult<AssessmentMarkReviewQueueItemDto>>("Invalid review status.");
        var tenant = _user.TenantId;
        var events = _markReviews.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted);
        // The correlated MAX uses IX_AssessmentMarkReviewEvents_TenantId_AssessmentSubjectId_Id.
        // A subject without a decision remains visible under Unsubmitted/All.
        var latest = events.Where(x => x.Id == events.Where(y =>
            y.AssessmentSubjectId == x.AssessmentSubjectId).Max(y => y.Id));
        var query =
            from exam in _assessments.GetQueryable().AsNoTracking()
            join sub in _subjects.GetQueryable().AsNoTracking() on exam.Id equals sub.AssessmentId
            join offering in _offerings.GetQueryable().AsNoTracking() on sub.SubjectOfferingId equals offering.Id
            join batch in _batches.GetQueryable().AsNoTracking() on offering.AcademicBatchId equals batch.Id
            join curriculum in _curriculumSubjects.GetQueryable().AsNoTracking()
                on offering.CurriculumSubjectId equals curriculum.Id
            join subject in _subjectNames.GetQueryable().AsNoTracking() on curriculum.SubjectId equals subject.Id
            from review in latest.Where(x => x.AssessmentSubjectId == sub.Id).DefaultIfEmpty()
            where exam.TenantId == tenant && sub.TenantId == tenant && offering.TenantId == tenant &&
                batch.TenantId == tenant && curriculum.TenantId == tenant && subject.TenantId == tenant &&
                exam.CampusId == batch.CampusId && exam.AcademicYearId == batch.AcademicYearId &&
                offering.AcademicYearId == exam.AcademicYearId &&
                exam.State != AssessmentState.Cancelled && batch.IsActive && offering.IsActive &&
                !exam.IsDeleted && !sub.IsDeleted && !offering.IsDeleted && !batch.IsDeleted &&
                !curriculum.IsDeleted && !subject.IsDeleted
            select new { exam, sub, batch, subject, review };
        if (IsRestrictedCampusManager())
            query = query.Where(x => AllowedCampusIds(tenant).Contains(x.exam.CampusId));
        var search = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.exam.Name.StartsWith(search) ||
                x.batch.Name.StartsWith(search) || x.subject.Name.StartsWith(search));
        query = status switch
        {
            "pending" => query.Where(x => x.review != null &&
                x.review.Decision == AssessmentMarkReviewDecision.Submitted),
            "rejected" => query.Where(x => x.review != null &&
                x.review.Decision == AssessmentMarkReviewDecision.Rejected),
            "approved" => query.Where(x => x.review != null &&
                x.review.Decision == AssessmentMarkReviewDecision.Approved),
            "unsubmitted" => query.Where(x => x.review == null),
            _ => query
        };
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.exam.StartDate)
            .ThenBy(x => x.exam.Id).ThenBy(x => x.sub.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new
            {
                x.sub.Id, AssessmentName = x.exam.Name, BatchName = x.batch.Name,
                SubjectName = x.subject.Name, x.exam.State, x.sub.RowVersion,
                Decision = x.review == null ? (AssessmentMarkReviewDecision?)null : x.review.Decision,
                Actor = x.review == null ? (long?)null : x.review.ActorUserId,
                MarkVersion = x.review == null ? null : x.review.MarkRowVersion,
                At = x.review == null ? (DateTime?)null : x.review.OccurredAt
            }).ToListAsync(ct);
        var items = rows.Select(x =>
        {
            var current = x.Decision.HasValue && x.MarkVersion == Version(x.RowVersion);
            return new AssessmentMarkReviewQueueItemDto
            {
                AssessmentSubjectId = x.Id, AssessmentName = x.AssessmentName,
                AcademicBatchName = x.BatchName, SubjectName = x.SubjectName,
                Decision = x.Decision, LastDecisionAt = x.At, IsCurrent = current,
                CanReview = x.State == AssessmentState.MarksEntry &&
                    x.Decision == AssessmentMarkReviewDecision.Submitted &&
                    x.Actor != _user.UserId && current
            };
        }).ToList();
        return ApiResponse<PagedResult<AssessmentMarkReviewQueueItemDto>>.SuccessResponse(
            new PagedResult<AssessmentMarkReviewQueueItemDto>
            { Items = items, TotalCount = total, Page = request.Page, PageSize = request.PageSize });
    }
}
