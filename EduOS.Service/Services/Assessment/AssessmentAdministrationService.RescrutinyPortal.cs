using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    private sealed record LinkedRescrutinyStudent(Student Student, bool IsOwner, bool IsGuardian);

    private bool CanRescrutinySelfServe() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsInRole("Student") || _user.IsInRole("Guardian") || _user.IsInRole("Parent"));

    private async Task<LinkedRescrutinyStudent?> FindLinkedRescrutinyStudentAsync(
        Guid studentReference, CancellationToken ct)
    {
        if (!CanRescrutinySelfServe() || studentReference == Guid.Empty) return null;
        var tenant = _user.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == studentReference &&
            x.StatusCode == "Active" && !x.IsDeleted, ct);
        if (student == null) return null;
        var owner = _user.IsInRole("Student") && student.UserId == _user.UserId;
        var guardian = false;
        if ((_user.IsInRole("Guardian") || _user.IsInRole("Parent")) &&
            _rescrutinyGuardians != null && _rescrutinyStudentGuardians != null)
        {
            guardian = await (from link in _rescrutinyStudentGuardians.GetQueryable().AsNoTracking()
                join adult in _rescrutinyGuardians.GetQueryable().AsNoTracking()
                    on new { link.TenantId, Id = link.GuardianId }
                    equals new { adult.TenantId, adult.Id }
                where link.TenantId == tenant && link.StudentId == student.Id &&
                    !link.IsDeleted && !adult.IsDeleted && adult.IsActive &&
                    adult.UserId == _user.UserId
                select link.Id).AnyAsync(ct);
        }
        return owner || guardian ? new LinkedRescrutinyStudent(student, owner, guardian) : null;
    }

    private static bool CanViewLinkedRescrutinyPublication(
        LinkedRescrutinyStudent linked, ResultPublication publication) =>
        publication.State == ResultPublicationState.Published &&
        ((linked.IsOwner && publication.VisibleToStudent) ||
         (linked.IsGuardian && publication.VisibleToGuardian));

    /// <summary>
    /// Only the linked learner's own applications are returned. This is not an
    /// administrator's worklist and never exposes other learners in a publication.
    /// </summary>
    public async Task<ApiResponse<ResultRescrutinyPageDto>> SearchLinkedResultRescrutinyAsync(
        Guid studentReference, long publicationId, long? beforeId,
        CancellationToken ct = default)
    {
        if (!CanRescrutinySelfServe())
            return Error<ResultRescrutinyPageDto>("Linked learner access required.", 403);
        if (studentReference == Guid.Empty || publicationId <= 0 || beforeId is <= 0)
            return Error<ResultRescrutinyPageDto>("Invalid learner or publication scope.");

        var linked = await FindLinkedRescrutinyStudentAsync(studentReference, ct);
        if (linked == null)
            return Error<ResultRescrutinyPageDto>("Student or published result not available.", 404);
        var scope = await FindRescrutinyScopeAsync(publicationId, ct);
        if (scope == null || !CanViewLinkedRescrutinyPublication(linked, scope.Publication))
            return Error<ResultRescrutinyPageDto>("Student or published result not available.", 404);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<ResultRescrutinyPageDto>("Campus access denied.", 403);
        if (!await HasValidRescrutinyEvidenceAsync(scope, ct))
            return Error<ResultRescrutinyPageDto>("Published snapshot requires reconciliation.", 409);

        var tenant = _user.TenantId;
        var enrolled = await (from summary in _summaries.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { summary.TenantId, Id = summary.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            where summary.TenantId == tenant &&
                summary.ResultPublicationId == scope.Publication.Id &&
                summary.AssessmentId == scope.Publication.AssessmentId &&
                summary.PublicationVersionNo == scope.Publication.VersionNo &&
                enrollment.StudentId == linked.Student.Id &&
                enrollment.AcademicBatchId == scope.Publication.AcademicBatchId &&
                enrollment.CampusId == scope.CampusId &&
                enrollment.AcademicYearId == scope.AcademicYearId &&
                !summary.IsDeleted && !summary.IsWithheld && !enrollment.IsDeleted
            select new { enrollment.Id, enrollment.RollNo }).Take(2).ToListAsync(ct);
        if (enrolled.Count != 1)
            return Error<ResultRescrutinyPageDto>("Published student evidence requires reconciliation.", 409);
        var enrollmentId = enrolled[0].Id;
        var requests = _rescrutinyEvents.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.ResultPublicationId == publicationId &&
            x.StudentEnrollmentId == enrollmentId &&
            x.PublicationVersionNo == scope.Publication.VersionNo &&
            x.Action == ResultRescrutinyAction.Requested && !x.IsTerminal && !x.IsDeleted);
        if (beforeId.HasValue) requests = requests.Where(x => x.Id < beforeId.Value);
        const int pageSize = 25;
        var rows = await requests.OrderByDescending(x => x.Id).Take(pageSize + 1)
            .Select(x => new { x.Id, x.CaseReference, x.PublicationVersionNo,
                x.ActorUserId, x.OccurredAt, x.Reason, x.RowVersion }).ToListAsync(ct);
        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var refs = rows.Select(x => x.CaseReference).ToArray();
        var decisions = await _rescrutinyEvents.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.ResultPublicationId == publicationId &&
                x.StudentEnrollmentId == enrollmentId && x.IsTerminal &&
                !x.IsDeleted && refs.Contains(x.CaseReference))
            .Select(x => new { x.CaseReference, x.Action, x.ActorUserId, x.OccurredAt, x.Reason })
            .ToListAsync(ct);
        if (decisions.GroupBy(x => x.CaseReference).Any(x => x.Count() != 1))
            return Error<ResultRescrutinyPageDto>("Case decision evidence requires reconciliation.", 409);
        var byCase = decisions.ToDictionary(x => x.CaseReference);
        return ApiResponse<ResultRescrutinyPageDto>.SuccessResponse(new()
        {
            HasMore = hasMore,
            NextBeforeId = hasMore && rows.Count > 0 ? rows[^1].Id : null,
            Items = rows.Select(x =>
            {
                byCase.TryGetValue(x.CaseReference, out var decision);
                return new ResultRescrutinyItemDto
                {
                    RequestEventId = x.Id, CaseReference = x.CaseReference,
                    PublicationId = publicationId, PublicationVersionNo = x.PublicationVersionNo,
                    StudentReference = linked.Student.PublicId,
                    StudentCode = linked.Student.StudentCode, RollNo = enrolled[0].RollNo,
                    Status = decision?.Action ?? ResultRescrutinyAction.Requested,
                    // Portal readers need the decision, never internal staff/user IDs.
                    RequestedByUserId = 0, RequestedAt = x.OccurredAt,
                    RequestReason = x.Reason, RequestRowVersion = string.Empty,
                    DecidedByUserId = null, DecidedAt = decision?.OccurredAt,
                    DecisionReason = decision?.Reason
                };
            }).ToArray()
        });
    }
}
