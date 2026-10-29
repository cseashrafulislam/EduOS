using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    private sealed record RescrutinyScope(ResultPublication Publication, long CampusId, long AcademicYearId);

    private async Task<RescrutinyScope?> FindRescrutinyScopeAsync(long publicationId, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        return await (from publication in _publications.GetQueryable().AsNoTracking()
            join exam in _assessments.GetQueryable().AsNoTracking()
                on new { publication.TenantId, Id = publication.AssessmentId }
                equals new { exam.TenantId, exam.Id }
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { publication.TenantId, Id = publication.AcademicBatchId }
                equals new { batch.TenantId, batch.Id }
            where publication.TenantId == tenant && publication.Id == publicationId &&
                !publication.IsDeleted && !exam.IsDeleted && !batch.IsDeleted &&
                exam.CampusId == batch.CampusId && exam.AcademicYearId == batch.AcademicYearId &&
                (publication.State == ResultPublicationState.Published ||
                 publication.State == ResultPublicationState.Withdrawn)
            select new RescrutinyScope(publication, exam.CampusId, exam.AcademicYearId))
            .FirstOrDefaultAsync(ct);
    }

    private async Task<bool> HasValidRescrutinyEvidenceAsync(RescrutinyScope scope, CancellationToken ct)
    {
        var proof = await GetPublicationEvidenceAsync([scope.Publication.Id],
            scope.CampusId, scope.AcademicYearId, ct);
        return proof.TryGetValue(scope.Publication.Id, out var evidence) && evidence.IsReconciled;
    }

    // A status-filtered worklist can otherwise hide orphan/duplicate terminal events.
    // Validate the complete tenant-owned publication ledger in SQL before displaying
    // counts, allowing decisions or exporting even one case.
    private async Task<bool> HasReconciledRescrutinyLedgerAsync(
        RescrutinyScope scope, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var events = _rescrutinyEvents.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.ResultPublicationId == scope.Publication.Id && !x.IsDeleted);
        var requests = events.Where(x => !x.IsTerminal && x.Action == ResultRescrutinyAction.Requested);
        var decisions = events.Where(x => x.IsTerminal);

        if (await events.AnyAsync(x =>
            x.PublicationVersionNo != scope.Publication.VersionNo ||
            x.CaseReference == Guid.Empty || x.ClientRequestId == Guid.Empty ||
            x.ActorUserId <= 0 ||
            (x.IsTerminal
                ? x.Action != ResultRescrutinyAction.Rejected &&
                  x.Action != ResultRescrutinyAction.AcceptedForCorrection
                : x.Action != ResultRescrutinyAction.Requested) ||
            !_enrollments.GetQueryable().AsNoTracking().Any(e =>
                e.TenantId == tenant && e.Id == x.StudentEnrollmentId && !e.IsDeleted &&
                e.AcademicBatchId == scope.Publication.AcademicBatchId &&
                e.AcademicYearId == scope.AcademicYearId && e.CampusId == scope.CampusId), ct))
            return false;

        // Aggregation is executed in SQL: no unbounded materialization of case events.
        if (await events.GroupBy(x => x.CaseReference)
            .Select(g => new
            {
                Requests = g.Count(x => !x.IsTerminal),
                Decisions = g.Count(x => x.IsTerminal)
            }).AnyAsync(x => x.Requests != 1 || x.Decisions > 1, ct))
            return false;

        // A terminal event must reference the exact original case, enrollment,
        // version and a different actor; no independent decision may predate intake.
        return !await decisions.AnyAsync(d => !requests.Any(r =>
            r.CaseReference == d.CaseReference &&
            r.StudentEnrollmentId == d.StudentEnrollmentId &&
            r.PublicationVersionNo == d.PublicationVersionNo &&
            r.ActorUserId != d.ActorUserId &&
            r.OccurredAt <= d.OccurredAt), ct);
    }

    public async Task<ApiResponse<ResultRescrutinyPageDto>> SearchResultRescrutinyAsync(
        ResultRescrutinyQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<ResultRescrutinyPageDto>("Rescrutiny permission required.", 403);
        if (request == null || request.PublicationId <= 0 || request.PageSize is < 1 or > 50 ||
            request.BeforeId is <= 0 ||
            (request.Status.HasValue && request.Status.Value is not
                (ResultRescrutinyAction.Requested or ResultRescrutinyAction.Rejected or ResultRescrutinyAction.AcceptedForCorrection)))
            return Error<ResultRescrutinyPageDto>("Invalid rescrutiny page.");
        var scope = await FindRescrutinyScopeAsync(request.PublicationId, ct);
        if (scope == null) return Error<ResultRescrutinyPageDto>("Publication not found.", 404);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<ResultRescrutinyPageDto>("Campus access denied.", 403);
        if (!await HasValidRescrutinyEvidenceAsync(scope, ct))
            return Error<ResultRescrutinyPageDto>("Published snapshot requires reconciliation.", 409);
        if (!await HasReconciledRescrutinyLedgerAsync(scope, ct))
            return Error<ResultRescrutinyPageDto>("Rescrutiny case evidence requires reconciliation.", 409);

        var tenant = _user.TenantId;
        var query = from evt in _rescrutinyEvents.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { evt.TenantId, Id = evt.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where evt.TenantId == tenant && evt.ResultPublicationId == scope.Publication.Id &&
                !evt.IsDeleted && !evt.IsTerminal && evt.Action == ResultRescrutinyAction.Requested &&
                !enrollment.IsDeleted && !student.IsDeleted &&
                enrollment.AcademicBatchId == scope.Publication.AcademicBatchId &&
                enrollment.CampusId == scope.CampusId
            select new { evt, enrollment, student };
        if (request.Status.HasValue)
        {
            var decisionsForStatus = _rescrutinyEvents.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.ResultPublicationId == scope.Publication.Id &&
                x.IsTerminal && !x.IsDeleted);
            if (request.Status.Value == ResultRescrutinyAction.Requested)
                query = query.Where(x => !decisionsForStatus.Any(d =>
                    d.CaseReference == x.evt.CaseReference));
            else
                query = query.Where(x => decisionsForStatus.Any(d =>
                    d.CaseReference == x.evt.CaseReference && d.Action == request.Status.Value));
        }
        if (request.BeforeId.HasValue)
            query = query.Where(x => x.evt.Id < request.BeforeId.Value);
        var rows = await query.OrderByDescending(x => x.evt.Id).Take(request.PageSize + 1)
            .Select(x => new
            {
                x.evt.Id, x.evt.CaseReference, x.evt.ResultPublicationId,
                x.evt.StudentEnrollmentId, x.evt.PublicationVersionNo, x.evt.ActorUserId, x.evt.OccurredAt,
                x.evt.Reason, x.evt.RowVersion, x.student.PublicId,
                x.student.StudentCode, x.enrollment.RollNo
            }).ToListAsync(ct);
        var hasMore = rows.Count > request.PageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var caseReferences = rows.Select(x => x.CaseReference).ToArray();
        var decisions = await _rescrutinyEvents.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.ResultPublicationId == scope.Publication.Id &&
                x.IsTerminal && !x.IsDeleted && caseReferences.Contains(x.CaseReference))
            .Select(x => new { x.CaseReference, x.ResultPublicationId, x.StudentEnrollmentId,
                x.PublicationVersionNo, x.Action, x.ActorUserId, x.OccurredAt, x.Reason })
            .ToListAsync(ct);
        // A corrupt or mismatched terminal event must not become a valid-looking
        // case status (nor throw a 500 through ToDictionary on duplicate evidence).
        if (rows.GroupBy(x => x.CaseReference).Any(g => g.Count() != 1) ||
            rows.Any(x => x.PublicationVersionNo != scope.Publication.VersionNo) ||
            decisions.GroupBy(x => x.CaseReference).Any(g => g.Count() != 1) ||
            decisions.Any(d => d.ResultPublicationId != scope.Publication.Id ||
                d.PublicationVersionNo != scope.Publication.VersionNo ||
                d.Action is not (ResultRescrutinyAction.Rejected or ResultRescrutinyAction.AcceptedForCorrection) ||
                !rows.Any(x => x.CaseReference == d.CaseReference &&
                    x.StudentEnrollmentId == d.StudentEnrollmentId)))
            return Error<ResultRescrutinyPageDto>("Rescrutiny case evidence requires reconciliation.", 409);
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
                    PublicationId = x.ResultPublicationId,
                    PublicationVersionNo = x.PublicationVersionNo,
                    StudentReference = x.PublicId, StudentCode = x.StudentCode,
                    RollNo = x.RollNo,
                    Status = decision?.Action ?? ResultRescrutinyAction.Requested,
                    RequestedByUserId = x.ActorUserId, RequestedAt = x.OccurredAt,
                    RequestReason = x.Reason, RequestRowVersion = Version(x.RowVersion),
                    DecidedByUserId = decision?.ActorUserId, DecidedAt = decision?.OccurredAt,
                    DecisionReason = decision?.Reason
                };
            }).ToArray()
        });
    }

    public Task<ApiResponse<ResultRescrutinyItemDto>> RequestResultRescrutinyAsync(
        RequestResultRescrutinyDto request, CancellationToken ct = default)
    {
        if (!CanPublish() && !CanRescrutinySelfServe())
            return Task.FromResult(Error<ResultRescrutinyItemDto>("Rescrutiny intake permission required.", 403));
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.PublicationId <= 0 || request.StudentReference == Guid.Empty ||
            !ValidReason(request.Reason) || request.Reason.Trim().Length < 10)
            return Task.FromResult(Error<ResultRescrutinyItemDto>("Publication, student and a reason of 10-1000 characters are required."));
        return WriteAsync("result rescrutiny request", async token =>
        {
            var tenant = _user.TenantId;
            var linked = CanPublish() ? null : await FindLinkedRescrutinyStudentAsync(request.StudentReference, token);
            if (!CanPublish() && linked == null)
                return Error<ResultRescrutinyItemDto>("Student or published result not available.", 404);
            var scope = await FindRescrutinyScopeAsync(request.PublicationId, token);
            if (scope == null) return Error<ResultRescrutinyItemDto>("Publication not found.", 404);
            if (!await CanAccessCampusAsync(scope.CampusId, token))
                return Error<ResultRescrutinyItemDto>("Campus access denied.", 403);
            if (scope.Publication.State != ResultPublicationState.Published)
                return Error<ResultRescrutinyItemDto>("Only currently published results accept new applications.", 409);
            if (linked != null && !CanViewLinkedRescrutinyPublication(linked, scope.Publication))
                return Error<ResultRescrutinyItemDto>("Student or published result not available.", 404);
            var student = await (from enrollment in _enrollments.GetQueryable().AsNoTracking()
                join learner in _students.GetQueryable().AsNoTracking()
                    on new { enrollment.TenantId, Id = enrollment.StudentId }
                    equals new { learner.TenantId, learner.Id }
                where enrollment.TenantId == tenant && learner.TenantId == tenant &&
                    learner.PublicId == request.StudentReference &&
                    enrollment.AcademicBatchId == scope.Publication.AcademicBatchId &&
                    enrollment.CampusId == scope.CampusId &&
                    enrollment.AcademicYearId == scope.AcademicYearId &&
                    !enrollment.IsDeleted && !learner.IsDeleted
                select new { enrollment.Id, StudentId = learner.Id, learner.PublicId, learner.StudentCode, enrollment.RollNo })
                .FirstOrDefaultAsync(token);
            if (student == null || (linked != null && student.StudentId != linked.Student.Id))
                return Error<ResultRescrutinyItemDto>("Student enrollment not found in publication scope.", 404);
            if (!await HasValidRescrutinyEvidenceAsync(scope, token))
                return Error<ResultRescrutinyItemDto>("Published snapshot requires reconciliation.", 409);
            if (!await HasReconciledRescrutinyLedgerAsync(scope, token))
                return Error<ResultRescrutinyItemDto>("Rescrutiny case evidence requires reconciliation.", 409);
            var privilegedIntake = linked == null;
            if (!await _summaries.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.ResultPublicationId == scope.Publication.Id &&
                x.StudentEnrollmentId == student.Id &&
                x.PublicationVersionNo == scope.Publication.VersionNo && !x.IsDeleted &&
                (privilegedIntake || !x.IsWithheld), token))
                return Error<ResultRescrutinyItemDto>("Student has no published snapshot in this version.", 409);
            var normalized = request.Reason.Trim();
            var replay = await _rescrutinyEvents.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, token);
            if (replay != null)
            {
                if (replay.IsTerminal || replay.ResultPublicationId != scope.Publication.Id ||
                    replay.StudentEnrollmentId != student.Id ||
                    replay.PublicationVersionNo != scope.Publication.VersionNo ||
                    replay.ActorUserId != _user.UserId || replay.Reason != normalized)
                    return Error<ResultRescrutinyItemDto>("Request key belongs to another application.", 409);
                // The same request key may be retried after an independent decision.
                // Return its current verified state, not a misleading Pending response.
                var terminal = await _rescrutinyEvents.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.CaseReference == replay.CaseReference &&
                        x.IsTerminal && !x.IsDeleted)
                    .Select(x => new { x.ResultPublicationId, x.StudentEnrollmentId,
                        x.PublicationVersionNo, x.Action, x.ActorUserId, x.OccurredAt, x.Reason })
                    .Take(2).ToListAsync(token);
                if (terminal.Count > 1 || terminal.Any(x =>
                    x.ResultPublicationId != replay.ResultPublicationId ||
                    x.StudentEnrollmentId != replay.StudentEnrollmentId ||
                    x.PublicationVersionNo != replay.PublicationVersionNo ||
                    x.Action is not (ResultRescrutinyAction.Rejected or ResultRescrutinyAction.AcceptedForCorrection)))
                    return Error<ResultRescrutinyItemDto>("Rescrutiny decision evidence requires reconciliation.", 409);
                var decision = terminal.SingleOrDefault();
                return ApiResponse<ResultRescrutinyItemDto>.SuccessResponse(new()
                {
                    RequestEventId = replay.Id, CaseReference = replay.CaseReference,
                    PublicationId = replay.ResultPublicationId,
                    PublicationVersionNo = replay.PublicationVersionNo,
                    StudentReference = student.PublicId, StudentCode = student.StudentCode,
                    RollNo = student.RollNo,
                    Status = decision?.Action ?? ResultRescrutinyAction.Requested,
                    RequestedByUserId = replay.ActorUserId, RequestedAt = replay.OccurredAt,
                    RequestReason = replay.Reason,
                    RequestRowVersion = CanPublish() ? Version(replay.RowVersion) : string.Empty,
                    DecidedByUserId = CanPublish() ? decision?.ActorUserId : null,
                    DecidedAt = decision?.OccurredAt, DecisionReason = decision?.Reason
                }, "Application already recorded.");
            }
            if (await _rescrutinyEvents.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.ResultPublicationId == scope.Publication.Id &&
                x.StudentEnrollmentId == student.Id && !x.IsTerminal && !x.IsDeleted, token))
                return Error<ResultRescrutinyItemDto>("A case already exists for this student and publication version.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var row = new ResultRescrutinyEvent
            {
                TenantId = tenant, CaseReference = request.ClientRequestId,
                ClientRequestId = request.ClientRequestId, ResultPublicationId = scope.Publication.Id,
                StudentEnrollmentId = student.Id, PublicationVersionNo = scope.Publication.VersionNo,
                Action = ResultRescrutinyAction.Requested, IsTerminal = false,
                ActorUserId = _user.UserId, OccurredAt = now, Reason = normalized,
                CreatedAt = now, CreatedBy = _user.UserId
            };
            await _rescrutinyEvents.AddAsync(row);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<ResultRescrutinyItemDto>.SuccessResponse(new()
            {
                RequestEventId = row.Id, CaseReference = row.CaseReference,
                PublicationId = row.ResultPublicationId, PublicationVersionNo = row.PublicationVersionNo,
                StudentReference = student.PublicId, StudentCode = student.StudentCode,
                RollNo = student.RollNo, Status = row.Action,
                RequestedByUserId = row.ActorUserId, RequestedAt = row.OccurredAt,
                RequestReason = row.Reason, RequestRowVersion = Version(row.RowVersion)
            }, "Application recorded.");
        }, ct);
    }

    public Task<ApiResponse<bool>> DecideResultRescrutinyAsync(
        Guid caseReference, DecideResultRescrutinyDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<bool>("Rescrutiny review permission required.", 403));
        if (caseReference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.Decision is not (ResultRescrutinyAction.Rejected or ResultRescrutinyAction.AcceptedForCorrection) ||
            !TryVersion(request.RequestRowVersion, out _) ||
            !ValidReason(request.Reason) || request.Reason.Trim().Length < 10)
            return Task.FromResult(Error<bool>("Decision, original version and reason are required."));
        return WriteAsync("result rescrutiny decision", async token =>
        {
            var tenant = _user.TenantId;
            var original = await _rescrutinyEvents.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.CaseReference == caseReference && !x.IsTerminal &&
                x.Action == ResultRescrutinyAction.Requested && !x.IsDeleted, token);
            if (original == null) return Error<bool>("Application not found.", 404);
            var scope = await FindRescrutinyScopeAsync(original.ResultPublicationId, token);
            if (scope == null || original.PublicationVersionNo != scope.Publication.VersionNo)
                return Error<bool>("Publication version is no longer valid.", 409);
            if (!await CanAccessCampusAsync(scope.CampusId, token))
                return Error<bool>("Campus access denied.", 403);
            if (original.ActorUserId == _user.UserId)
                return Error<bool>("The applicant cannot review their own case.", 403);
            if (request.Decision == ResultRescrutinyAction.AcceptedForCorrection &&
                (scope.Publication.State != ResultPublicationState.Published ||
                 await _publications.GetQueryable().AsNoTracking().AnyAsync(x =>
                     x.TenantId == tenant && x.AssessmentId == scope.Publication.AssessmentId &&
                     x.AcademicBatchId == scope.Publication.AcademicBatchId &&
                     x.VersionNo > scope.Publication.VersionNo && !x.IsDeleted &&
                     (x.State == ResultPublicationState.Published ||
                      x.State == ResultPublicationState.Withdrawn), token)))
                return Error<bool>("A newer or withdrawn publication requires a fresh review.", 409);
            if (!Matches(original.RowVersion, request.RequestRowVersion))
                return Error<bool>("Application version changed; refresh.", 409);
            if (!await HasValidRescrutinyEvidenceAsync(scope, token))
                return Error<bool>("Published snapshot requires reconciliation.", 409);
            if (!await HasReconciledRescrutinyLedgerAsync(scope, token))
                return Error<bool>("Rescrutiny case evidence requires reconciliation.", 409);
            var normalized = request.Reason.Trim();
            var replay = await _rescrutinyEvents.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, token);
            if (replay != null)
            {
                if (replay.CaseReference != caseReference || !replay.IsTerminal ||
                    replay.Action != request.Decision || replay.ActorUserId != _user.UserId ||
                    replay.Reason != normalized)
                    return Error<bool>("Request key belongs to another decision.", 409);
                return ApiResponse<bool>.SuccessResponse(true, "Decision already recorded.");
            }
            if (await _rescrutinyEvents.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.CaseReference == caseReference && x.IsTerminal && !x.IsDeleted, token))
                return Error<bool>("Case already decided.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            await _rescrutinyEvents.AddAsync(new ResultRescrutinyEvent
            {
                TenantId = tenant, CaseReference = caseReference,
                ClientRequestId = request.ClientRequestId, ResultPublicationId = original.ResultPublicationId,
                StudentEnrollmentId = original.StudentEnrollmentId,
                PublicationVersionNo = original.PublicationVersionNo,
                Action = request.Decision, IsTerminal = true,
                ActorUserId = _user.UserId, OccurredAt = now, Reason = normalized,
                CreatedAt = now, CreatedBy = _user.UserId
            });
            await _uow.SaveChangesAsync(token);
            return ApiResponse<bool>.SuccessResponse(true,
                "Decision recorded. Published results remain unchanged pending audited correction.");
        }, ct);
    }
}
