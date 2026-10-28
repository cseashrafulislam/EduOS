using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<AssessmentMarkReviewDto>> GetMarkReviewAsync(long subjectId,
        CancellationToken ct = default)
    {
        if (!CanRead()) return Error<AssessmentMarkReviewDto>("Assessment permission required.", 403);
        if (subjectId <= 0) return Error<AssessmentMarkReviewDto>("Invalid assessment subject.");
        var tenant = _user.TenantId;
        var subject = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == subjectId && !x.IsDeleted, ct);
        var exam = subject == null ? null : await _assessments.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == subject.AssessmentId && !x.IsDeleted, ct);
        if (exam == null || subject == null) return Error<AssessmentMarkReviewDto>("Assessment subject not found.", 404);
        if (!await CanAccessCampusAsync(exam.CampusId, ct))
            return Error<AssessmentMarkReviewDto>("Campus access denied.", 403);
        var offering = await _offerings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == subject.SubjectOfferingId && x.IsActive && !x.IsDeleted, ct);
        var batch = offering == null ? null : await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == offering.AcademicBatchId && x.IsActive && !x.IsDeleted, ct);
        if (batch == null || batch.CampusId != exam.CampusId || batch.AcademicYearId != exam.AcademicYearId)
            return Error<AssessmentMarkReviewDto>("Assessment subject scope is inconsistent.", 409);
        var canSubmit = await CanSubmitReviewAsync(subject.SubjectOfferingId, ct);
        if (!canSubmit && !CanPublish()) return Error<AssessmentMarkReviewDto>("Instructor assignment required.", 403);
        return ApiResponse<AssessmentMarkReviewDto>.SuccessResponse(
            await MapMarkReviewAsync(subject, exam, canSubmit, ct));
    }

    public Task<ApiResponse<AssessmentMarkReviewDto>> DecideMarkReviewAsync(long subjectId,
        AssessmentMarkReviewRequestDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return Task.FromResult(Error<AssessmentMarkReviewDto>("Assessment permission required.", 403));
        if (subjectId <= 0 || request == null || request.ClientRequestId == Guid.Empty ||
            !TryVersion(request.AssessmentRowVersion, out _) ||
            !Enum.IsDefined(request.Decision) ||
            request.Reason?.Length > 500 ||
            request.Decision == AssessmentMarkReviewDecision.Rejected &&
                string.IsNullOrWhiteSpace(request.Reason))
            return Task.FromResult(Error<AssessmentMarkReviewDto>("Decision, request key, rowversion and rejection reason are required."));
        return WriteAsync("assessment mark review", async token =>
        {
            var tenant = _user.TenantId;
            var subject = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == subjectId && !x.IsDeleted, token);
            var exam = subject == null ? null : await _assessments.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == subject.AssessmentId && !x.IsDeleted, token);
            if (exam == null || subject == null) return Error<AssessmentMarkReviewDto>("Assessment subject not found.", 404);
            if (!await CanAccessCampusAsync(exam.CampusId, token))
                return Error<AssessmentMarkReviewDto>("Campus access denied.", 403);
            var offering = await _offerings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == subject.SubjectOfferingId && x.IsActive && !x.IsDeleted, token);
            var batch = offering == null ? null : await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == offering.AcademicBatchId && x.IsActive && !x.IsDeleted, token);
            if (batch == null || batch.CampusId != exam.CampusId || batch.AcademicYearId != exam.AcademicYearId)
                return Error<AssessmentMarkReviewDto>("Assessment subject scope is inconsistent.", 409);
            var canSubmit = await CanSubmitReviewAsync(subject.SubjectOfferingId, token);
            if (request.Decision == AssessmentMarkReviewDecision.Submitted ? !canSubmit : !CanPublish())
                return Error<AssessmentMarkReviewDto>("Mark review permission denied.", 403);
            var previousRequest = await _markReviews.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, token);
            if (previousRequest != null)
            {
                if (previousRequest.AssessmentSubjectId != subjectId || previousRequest.Decision != request.Decision)
                    return Error<AssessmentMarkReviewDto>("Request key already used for another decision.", 409);
                return ApiResponse<AssessmentMarkReviewDto>.SuccessResponse(
                    await MapMarkReviewAsync(subject, exam, canSubmit, token), "Decision already recorded.");
            }
            if (exam.State != AssessmentState.MarksEntry)
                return Error<AssessmentMarkReviewDto>("Assessment is not open for mark review.", 409);
            if (!Matches(exam.RowVersion, request.AssessmentRowVersion))
                return Error<AssessmentMarkReviewDto>("Assessment changed; refresh before deciding.", 409);
            var latest = await _markReviews.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AssessmentSubjectId == subjectId && !x.IsDeleted)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(token);
            if (request.Decision == AssessmentMarkReviewDecision.Submitted)
            {
                if (latest != null && latest.Decision != AssessmentMarkReviewDecision.Rejected)
                    return Error<AssessmentMarkReviewDto>("Only unsubmitted or rejected marks may be submitted.", 409);
                // SQL aggregates, never materialize an entire class roster for submission.
                var eligible = from registration in _registrations.GetQueryable().AsNoTracking()
                    join enrollment in _enrollments.GetQueryable().AsNoTracking()
                        on registration.StudentEnrollmentId equals enrollment.Id
                    join student in _students.GetQueryable().AsNoTracking()
                        on enrollment.StudentId equals student.Id
                    where registration.TenantId == tenant && enrollment.TenantId == tenant &&
                        student.TenantId == tenant && registration.SubjectOfferingId == offering!.Id &&
                        registration.State == SubjectRegistrationState.Approved &&
                        enrollment.AcademicBatchId == batch.Id && enrollment.IsCurrent &&
                        enrollment.State == EnrollmentState.Active && student.StatusCode == "Active" &&
                        !registration.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted
                    select registration.Id;
                var expected = await eligible.CountAsync(token);
                var recorded = await _marks.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && x.AssessmentSubjectId == subjectId && !x.IsDeleted, token);
                var matched = await (from mark in _marks.GetQueryable().AsNoTracking()
                    join registrationId in eligible on mark.StudentSubjectRegistrationId equals registrationId
                    where mark.TenantId == tenant && mark.AssessmentSubjectId == subjectId &&
                        !mark.IsDeleted && !mark.IsWithheld &&
                        mark.ObtainedMarks >= 0 && mark.ObtainedMarks <= subject.FullMarks
                    select mark.Id).CountAsync(token);
                if (expected == 0 || recorded != expected || matched != expected)
                    return Error<AssessmentMarkReviewDto>("Marks are incomplete, withheld or inconsistent with eligible registrations.", 409);
                var components = await _components.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.AssessmentSubjectId == subjectId && !x.IsDeleted)
                    .Select(x => new { x.Id, x.Weightage }).ToListAsync(token);
                if (components.Count > 0)
                {
                    if (components.Count > 50 || components.Sum(x => x.Weightage) != 100m)
                        return Error<AssessmentMarkReviewDto>("Component weighting requires reconciliation.", 409);
                    var ids = components.Select(x => x.Id).ToArray();
                    var componentCount = await (from part in _componentMarks.GetQueryable().AsNoTracking()
                        join mark in _marks.GetQueryable().AsNoTracking()
                            on part.StudentAssessmentMarkId equals mark.Id
                        where part.TenantId == tenant && mark.TenantId == tenant &&
                            ids.Contains(part.AssessmentComponentId) && mark.AssessmentSubjectId == subjectId &&
                            !part.IsDeleted && !mark.IsDeleted && !part.IsWithheld
                        select part.Id).CountAsync(token);
                    if ((long)componentCount != (long)expected * components.Count)
                        return Error<AssessmentMarkReviewDto>("Component marks are incomplete or withheld.", 409);
                }
            }
            else
            {
                if (latest?.Decision != AssessmentMarkReviewDecision.Submitted ||
                    latest.MarkRowVersion != Version(subject.RowVersion))
                    return Error<AssessmentMarkReviewDto>("Only unchanged submitted marks can be reviewed.", 409);
                if (latest.ActorUserId == _user.UserId)
                    return Error<AssessmentMarkReviewDto>("A submitter cannot approve or reject their own marks.", 403);
            }
            var now = _clock.GetUtcNow().UtcDateTime;
            await _markReviews.AddAsync(new AssessmentMarkReviewEvent
            {
                TenantId = tenant, AssessmentSubjectId = subjectId,
                ClientRequestId = request.ClientRequestId, Decision = request.Decision,
                ActorUserId = _user.UserId, OccurredAt = now,
                MarkRowVersion = Version(subject.RowVersion), Reason = Trim(request.Reason),
                CreatedAt = now, CreatedBy = _user.UserId
            });
            // The assessment rowversion serializes review decisions against marks writes and lock.
            exam.UpdatedAt = now; exam.UpdatedBy = _user.UserId;
            _assessments.Update(exam);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<AssessmentMarkReviewDto>.SuccessResponse(
                await MapMarkReviewAsync(subject, exam, canSubmit, token), "Review decision recorded.");
        }, ct);
    }

    // A submission is always owned by an active teacher assigned to the exact offering.
    // Administrative marks access does not automatically grant submission authorship.
    private async Task<bool> CanSubmitReviewAsync(long offeringId, CancellationToken ct)
    {
        var employeeId = await OwnTeacherIdAsync(ct);
        return employeeId > 0 && await _instructors.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == _user.TenantId && x.SubjectOfferingId == offeringId &&
            x.EmployeeId == employeeId && x.IsActive && !x.IsDeleted, ct);
    }

    private async Task<AssessmentMarkReviewDto> MapMarkReviewAsync(AssessmentSubject subject,
        Assessment exam, bool canSubmit, CancellationToken ct)
    {
        var query = _markReviews.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.AssessmentSubjectId == subject.Id && !x.IsDeleted);
        var total = await query.CountAsync(ct);
        var events = await query.OrderByDescending(x => x.Id).Take(50)
            .Select(x => new AssessmentMarkReviewEventDto
            {
                Id = x.Id, Decision = x.Decision, OccurredAt = x.OccurredAt,
                ActorUserId = x.ActorUserId, Reason = x.Reason
            }).ToListAsync(ct);
        var latest = await query.OrderByDescending(x => x.Id).Select(x => new
        {
            x.Decision, x.MarkRowVersion, x.ActorUserId
        }).FirstOrDefaultAsync(ct);
        return new AssessmentMarkReviewDto
        {
            AssessmentSubjectId = subject.Id, AssessmentRowVersion = Version(exam.RowVersion),
            CurrentDecision = latest?.Decision, HistoryTotal = total, RecentHistory = events,
            CanSubmit = canSubmit && exam.State == AssessmentState.MarksEntry &&
                (latest == null || latest.Decision == AssessmentMarkReviewDecision.Rejected),
            CanReview = CanPublish() && exam.State == AssessmentState.MarksEntry &&
                latest?.Decision == AssessmentMarkReviewDecision.Submitted &&
                latest.ActorUserId != _user.UserId &&
                latest.MarkRowVersion == Version(subject.RowVersion)
        };
    }
}
