using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<AssessmentMarkChangePageDto>> SearchMarkChangesAsync(
        AssessmentMarkChangeQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<AssessmentMarkChangePageDto>("Marks review permission required.", 403);
        if (request == null || request.AssessmentSubjectId <= 0 ||
            request.Page is < 1 or > 100000 || request.PageSize is < 1 or > 50)
            return Error<AssessmentMarkChangePageDto>("Invalid marks history scope or page.");
        var scope = await ScopeAsync(request, ct);
        if (scope == null) return Error<AssessmentMarkChangePageDto>("Assessment or batch not found.", 404);
        var tenant = _user.TenantId;
        var validSubject = await (from subject in _subjects.GetQueryable().AsNoTracking()
            join offering in _offerings.GetQueryable().AsNoTracking()
                on subject.SubjectOfferingId equals offering.Id
            where subject.TenantId == tenant && offering.TenantId == tenant &&
                subject.Id == request.AssessmentSubjectId && subject.AssessmentId == scope.Value.Exam.Id &&
                offering.AcademicBatchId == scope.Value.Batch.Id &&
                offering.AcademicYearId == scope.Value.Exam.AcademicYearId &&
                !subject.IsDeleted && !offering.IsDeleted
            select subject.Id).AnyAsync(ct);
        if (!validSubject) return Error<AssessmentMarkChangePageDto>("Subject not found in batch.", 404);
        // AuditLog is operational history with the configured retention period, not
        // a replacement for immutable published result snapshots or approval evidence.
        var query = _auditLogs.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.EntityName == "AssessmentMarkChange" &&
            x.EntityId == request.AssessmentSubjectId && x.IsSuccess);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new AssessmentMarkChangeDto
            {
                Id = x.Id, OccurredAt = x.OccurredAt, ActorUserId = x.UserId,
                Action = x.Action, Before = x.OldValue, After = x.NewValue
            }).ToListAsync(ct);
        return ApiResponse<AssessmentMarkChangePageDto>.SuccessResponse(new()
        { Page = request.Page, PageSize = request.PageSize, TotalCount = count, Items = items });
    }
}
