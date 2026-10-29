using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<PagedResult<AssessmentAttemptCandidateDto>>> SearchAssessmentAttemptCandidatesAsync(
        AssessmentAttemptCandidateQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<PagedResult<AssessmentAttemptCandidateDto>>("Exam administration permission required.", 403);
        if (_attemptRegistrations == null || _courseSnapshots == null)
            return Error<PagedResult<AssessmentAttemptCandidateDto>>("Attempt evidence is unavailable.", 503);
        if (request == null || request.AssessmentSubjectId <= 0 || request.Page is < 1 or > 100000 ||
            request.PageSize is < 1 or > 50 || request.Search?.Length > 100)
            return Error<PagedResult<AssessmentAttemptCandidateDto>>("Invalid attempt candidate filters.");
        var tenant = _user.TenantId;
        var subject = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == request.AssessmentSubjectId && !x.IsDeleted, ct);
        var exam = subject == null ? null : await _assessments.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == subject.AssessmentId && !x.IsDeleted, ct);
        if (exam == null || !await CanAccessCampusAsync(exam.CampusId, ct))
            return Error<PagedResult<AssessmentAttemptCandidateDto>>("Assessment not found.", 404);
        if (exam.AttemptMode == AssessmentAttemptMode.Regular)
            return Error<PagedResult<AssessmentAttemptCandidateDto>>("Select a targeted Retake or Improvement assessment.", 409);
        var offering = await _offerings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == subject!.SubjectOfferingId && x.IsActive && !x.IsDeleted, ct);
        if (offering == null) return Error<PagedResult<AssessmentAttemptCandidateDto>>("Subject offering unavailable.", 409);
        var q = from registration in _registrations.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on registration.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
            where registration.TenantId == tenant && enrollment.TenantId == tenant && student.TenantId == tenant &&
                registration.SubjectOfferingId == offering.Id && registration.State == SubjectRegistrationState.Approved &&
                enrollment.AcademicBatchId == offering.AcademicBatchId && enrollment.AcademicYearId == exam.AcademicYearId &&
                enrollment.CampusId == exam.CampusId && enrollment.IsCurrent && enrollment.State == EnrollmentState.Active &&
                student.StatusCode == "Active" && !registration.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted
            select new { RegistrationId = registration.Id, enrollment.StudentId,
                student.PublicId, student.StudentCode, StudentName = student.FullName, enrollment.RollNo };
        var term = request.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
            q = q.Where(x => x.StudentCode.StartsWith(term) || x.RollNo.StartsWith(term) || x.StudentName.StartsWith(term));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.RollNo).ThenBy(x => x.RegistrationId)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var studentIds = rows.Select(x => x.StudentId).Distinct().ToArray();
        var start = exam.StartDate.ToDateTime(TimeOnly.MinValue);
        var publications = _publications.GetQueryable().AsNoTracking();
        var history = await (from snapshot in _courseSnapshots.GetQueryable().AsNoTracking()
            join former in _enrollments.GetQueryable().AsNoTracking() on snapshot.StudentEnrollmentId equals former.Id
            join publication in publications on snapshot.ResultPublicationId equals publication.Id
            join formerExam in _assessments.GetQueryable().AsNoTracking() on publication.AssessmentId equals formerExam.Id
            where snapshot.TenantId == tenant && former.TenantId == tenant && publication.TenantId == tenant &&
                formerExam.TenantId == tenant && studentIds.Contains(former.StudentId) &&
                snapshot.CurriculumSubjectId == offering.CurriculumSubjectId &&
                publication.State == ResultPublicationState.Published && publication.VisibleToStudent &&
                publication.HasCourseGradeSnapshots && publication.PublishedAt != null &&
                publication.PublishedAt < start && formerExam.CountsTowardCgpa &&
                formerExam.CampusId == exam.CampusId && formerExam.GradeSchemeId == exam.GradeSchemeId &&
                !snapshot.IsDeleted && !former.IsDeleted && !publication.IsDeleted && !formerExam.IsDeleted &&
                !publications.Any(newer => newer.TenantId == tenant && !newer.IsDeleted &&
                    newer.AssessmentId == publication.AssessmentId && newer.AcademicBatchId == publication.AcademicBatchId &&
                    newer.VersionNo > publication.VersionNo &&
                    (newer.State == ResultPublicationState.Published || newer.State == ResultPublicationState.Withdrawn))
            select new { former.StudentId, publication.Id, publication.PublishedAt, snapshot.IsPassed, snapshot.IsWithheld })
            .OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id)
            .Take(2001).ToListAsync(ct);
        if (history.Count > 2000)
            return Error<PagedResult<AssessmentAttemptCandidateDto>>("Candidate history requires a narrower search.", 409);
        var registrationIds = rows.Select(x => x.RegistrationId).ToArray();
        var already = await _attemptRegistrations.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AssessmentSubjectId == subject.Id &&
            registrationIds.Contains(x.StudentSubjectRegistrationId) && !x.IsDeleted)
            .Select(x => x.StudentSubjectRegistrationId).ToListAsync(ct);
        var registered = already.ToHashSet();
        var byStudent = history.GroupBy(x => x.StudentId)
            .ToDictionary(x => x.Key, x => x.First());
        return ApiResponse<PagedResult<AssessmentAttemptCandidateDto>>.SuccessResponse(
            new PagedResult<AssessmentAttemptCandidateDto>
            {
                Page = request.Page, PageSize = request.PageSize, TotalCount = total,
                Items = rows.Select(row =>
                {
                    var found = byStudent.TryGetValue(row.StudentId, out var latest);
                    var correct = found && !latest!.IsWithheld &&
                        (exam.AttemptMode == AssessmentAttemptMode.Retake && !latest.IsPassed ||
                         exam.AttemptMode == AssessmentAttemptMode.Improvement && latest.IsPassed);
                    return new AssessmentAttemptCandidateDto
                    {
                        StudentSubjectRegistrationId = row.RegistrationId,
                        StudentReference = row.PublicId, StudentCode = row.StudentCode,
                        StudentName = row.StudentName, RollNo = row.RollNo,
                        PriorResultPublicationId = correct ? latest!.Id : null,
                        PriorOutcome = !found ? "No published prior course" : latest!.IsWithheld ? "Withheld prior result"
                            : latest.IsPassed ? "Passed prior course" : "Failed prior course",
                        CanRequest = correct && !registered.Contains(row.RegistrationId)
                    };
                }).ToList()
            });
    }

    public async Task<ApiResponse<PagedResult<AssessmentAttemptRegistrationDto>>> SearchAssessmentAttemptsAsync(
        AssessmentAttemptQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<PagedResult<AssessmentAttemptRegistrationDto>>("Exam administration permission required.", 403);
        if (_attemptRegistrations == null) return Error<PagedResult<AssessmentAttemptRegistrationDto>>("Attempt register is unavailable.", 503);
        if (request == null || request.AssessmentId <= 0 || request.Page is < 1 or > 100000 ||
            request.PageSize is < 1 or > 50)
            return Error<PagedResult<AssessmentAttemptRegistrationDto>>("Invalid attempt register filters.");
        var tenant = _user.TenantId;
        var exam = await _assessments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == request.AssessmentId && !x.IsDeleted, ct);
        if (exam == null || !await CanAccessCampusAsync(exam.CampusId, ct))
            return Error<PagedResult<AssessmentAttemptRegistrationDto>>("Assessment not found.", 404);
        var query = from attempt in _attemptRegistrations.GetQueryable().AsNoTracking()
            join subject in _subjects.GetQueryable().AsNoTracking() on attempt.AssessmentSubjectId equals subject.Id
            join registration in _registrations.GetQueryable().AsNoTracking() on attempt.StudentSubjectRegistrationId equals registration.Id
            join enrollment in _enrollments.GetQueryable().AsNoTracking() on registration.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
            join offering in _offerings.GetQueryable().AsNoTracking() on subject.SubjectOfferingId equals offering.Id
            join item in _curriculumSubjects.GetQueryable().AsNoTracking() on offering.CurriculumSubjectId equals item.Id
            join course in _subjectNames.GetQueryable().AsNoTracking() on item.SubjectId equals course.Id
            where attempt.TenantId == tenant && subject.TenantId == tenant && registration.TenantId == tenant &&
                enrollment.TenantId == tenant && student.TenantId == tenant && offering.TenantId == tenant &&
                item.TenantId == tenant && course.TenantId == tenant && subject.AssessmentId == exam.Id &&
                !attempt.IsDeleted && !subject.IsDeleted && !registration.IsDeleted &&
                !enrollment.IsDeleted && !student.IsDeleted && !offering.IsDeleted && !item.IsDeleted && !course.IsDeleted
            select new { attempt, student, CourseName = course.Name };
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.attempt.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<AssessmentAttemptRegistrationDto>>.SuccessResponse(
            new PagedResult<AssessmentAttemptRegistrationDto>
            {
                Page = request.Page, PageSize = request.PageSize, TotalCount = total,
                Items = rows.Select(x => new AssessmentAttemptRegistrationDto
                {
                    Id = x.attempt.Id, AssessmentSubjectId = x.attempt.AssessmentSubjectId,
                    StudentSubjectRegistrationId = x.attempt.StudentSubjectRegistrationId,
                    PriorResultPublicationId = x.attempt.PriorResultPublicationId,
                    StudentReference = x.student.PublicId, StudentCode = x.student.StudentCode,
                    StudentName = x.student.FullName, SubjectName = x.CourseName,
                    AttemptMode = exam.AttemptMode, Decision = x.attempt.Decision,
                    Reason = x.attempt.Reason, DecisionNote = x.attempt.DecisionNote,
                    RowVersion = Version(x.attempt.RowVersion)
                }).ToList()
            });
    }

    public Task<ApiResponse<AssessmentAttemptRegistrationDto>> RequestAssessmentAttemptAsync(
        RequestAssessmentAttemptDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<AssessmentAttemptRegistrationDto>("Attempt management permission required.", 403));
        if (_attemptRegistrations == null) return Task.FromResult(Error<AssessmentAttemptRegistrationDto>("Attempt register is unavailable.", 503));
        if (request == null || request.AssessmentSubjectId <= 0 ||
            request.StudentSubjectRegistrationId <= 0 || request.PriorResultPublicationId <= 0 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return Task.FromResult(Error<AssessmentAttemptRegistrationDto>("A valid subject, registration, prior result and reason are required."));
        return WriteAsync("request retake/improvement", async token =>
        {
            var tenant = _user.TenantId;
            var subject = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AssessmentSubjectId && !x.IsDeleted, token);
            var exam = subject == null ? null : await _assessments.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == subject.AssessmentId && !x.IsDeleted, token);
            if (exam == null || !await CanAccessCampusAsync(exam.CampusId, token))
                return Error<AssessmentAttemptRegistrationDto>("Assessment not found.", 404);
            if (exam.AttemptMode == AssessmentAttemptMode.Regular ||
                exam.State is not (AssessmentState.Draft or AssessmentState.Scheduled))
                return Error<AssessmentAttemptRegistrationDto>("Retake/improvement requests require a draft or scheduled targeted assessment.", 409);
            var course = await (from offering in _offerings.GetQueryable().AsNoTracking()
                join reg in _registrations.GetQueryable().AsNoTracking() on offering.Id equals reg.SubjectOfferingId
                join enrollment in _enrollments.GetQueryable().AsNoTracking() on reg.StudentEnrollmentId equals enrollment.Id
                join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
                where offering.TenantId == tenant && reg.TenantId == tenant && enrollment.TenantId == tenant &&
                    student.TenantId == tenant && offering.Id == subject!.SubjectOfferingId &&
                    reg.Id == request.StudentSubjectRegistrationId &&
                    reg.State == SubjectRegistrationState.Approved &&
                    enrollment.AcademicBatchId == offering.AcademicBatchId &&
                    enrollment.CampusId == exam.CampusId && enrollment.AcademicYearId == exam.AcademicYearId &&
                    enrollment.IsCurrent && enrollment.State == EnrollmentState.Active &&
                    student.StatusCode == "Active" && offering.IsActive && !offering.IsDeleted &&
                    !reg.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted
                select new { offering.CurriculumSubjectId, enrollment.StudentId, reg.Id })
                .FirstOrDefaultAsync(token);
            if (course == null)
                return Error<AssessmentAttemptRegistrationDto>("Approved student registration in this campus/batch is required.", 409);
            var assessmentStart = exam.StartDate.ToDateTime(TimeOnly.MinValue);
            var prior = await _publications.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.PriorResultPublicationId && !x.IsDeleted &&
                x.State == ResultPublicationState.Published && x.VisibleToStudent &&
                x.PublishedAt != null && x.PublishedAt < assessmentStart, token);
            if (prior == null || !prior.HasCourseGradeSnapshots || _courseSnapshots == null)
                return Error<AssessmentAttemptRegistrationDto>("A published, versioned prior course result is required.", 409);
            var earlierExam = await _assessments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == prior.AssessmentId &&
                x.CountsTowardCgpa && x.GradeSchemeId == exam.GradeSchemeId &&
                x.CampusId == exam.CampusId && !x.IsDeleted, token);
            if (earlierExam == null || earlierExam.EndDate >= exam.StartDate)
                return Error<AssessmentAttemptRegistrationDto>("Prior course must be from an earlier compatible terminal assessment.", 409);
            var superseded = await _publications.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AssessmentId == prior.AssessmentId &&
                x.AcademicBatchId == prior.AcademicBatchId && x.VersionNo > prior.VersionNo &&
                (x.State == ResultPublicationState.Published || x.State == ResultPublicationState.Withdrawn) &&
                !x.IsDeleted, token);
            if (superseded)
                return Error<AssessmentAttemptRegistrationDto>("The prior result was superseded or withdrawn.", 409);
            var previous = await (from snap in _courseSnapshots.GetQueryable().AsNoTracking()
                join enrolled in _enrollments.GetQueryable().AsNoTracking() on snap.StudentEnrollmentId equals enrolled.Id
                where snap.TenantId == tenant && enrolled.TenantId == tenant &&
                    snap.ResultPublicationId == prior.Id &&
                    snap.CurriculumSubjectId == course.CurriculumSubjectId &&
                    enrolled.StudentId == course.StudentId && !snap.IsDeleted && !enrolled.IsDeleted
                select new { snap.IsPassed, snap.IsWithheld }).Take(2).ToListAsync(token);
            if (previous.Count != 1 || previous[0].IsWithheld ||
                exam.AttemptMode == AssessmentAttemptMode.Retake && previous[0].IsPassed ||
                exam.AttemptMode == AssessmentAttemptMode.Improvement && !previous[0].IsPassed)
                return Error<AssessmentAttemptRegistrationDto>("Prior course outcome does not authorize this attempt type.", 409);
            var latest = await (from snapshot in _courseSnapshots.GetQueryable().AsNoTracking()
                join enrolled in _enrollments.GetQueryable().AsNoTracking() on snapshot.StudentEnrollmentId equals enrolled.Id
                join published in _publications.GetQueryable().AsNoTracking() on snapshot.ResultPublicationId equals published.Id
                join priorExam in _assessments.GetQueryable().AsNoTracking() on published.AssessmentId equals priorExam.Id
                where snapshot.TenantId == tenant && enrolled.TenantId == tenant &&
                    published.TenantId == tenant && priorExam.TenantId == tenant &&
                    enrolled.StudentId == course.StudentId && snapshot.CurriculumSubjectId == course.CurriculumSubjectId &&
                    published.State == ResultPublicationState.Published && published.VisibleToStudent &&
                    published.PublishedAt != null && published.PublishedAt < assessmentStart &&
                    priorExam.CountsTowardCgpa && priorExam.CampusId == exam.CampusId &&
                    priorExam.GradeSchemeId == exam.GradeSchemeId && priorExam.EndDate < exam.StartDate &&
                    !snapshot.IsDeleted && !enrolled.IsDeleted && !published.IsDeleted && !priorExam.IsDeleted
                orderby published.PublishedAt descending, published.Id descending
                select published.Id).FirstOrDefaultAsync(token);
            if (latest != prior.Id)
                return Error<AssessmentAttemptRegistrationDto>("Only the latest published course attempt can authorize a repeat.", 409);
            var existing = await _attemptRegistrations!.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.AssessmentSubjectId == subject.Id &&
                x.StudentSubjectRegistrationId == course.Id && !x.IsDeleted, token);
            if (existing != null)
                return existing.PriorResultPublicationId == prior.Id && existing.Reason == request.Reason.Trim()
                    ? ApiResponse<AssessmentAttemptRegistrationDto>.SuccessResponse(new AssessmentAttemptRegistrationDto
                    {
                        Id = existing.Id, AssessmentSubjectId = existing.AssessmentSubjectId,
                        StudentSubjectRegistrationId = existing.StudentSubjectRegistrationId,
                        PriorResultPublicationId = existing.PriorResultPublicationId,
                        Decision = existing.Decision, AttemptMode = exam.AttemptMode,
                        Reason = existing.Reason, RowVersion = Version(existing.RowVersion)
                    }, "Attempt request already recorded.")
                    : Error<AssessmentAttemptRegistrationDto>("Conflicting attempt request already exists.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var row = new AssessmentAttemptRegistration
            {
                TenantId = tenant, AssessmentSubjectId = subject.Id,
                StudentSubjectRegistrationId = course.Id, PriorResultPublicationId = prior.Id,
                Decision = AssessmentAttemptDecision.Requested,
                Reason = request.Reason.Trim(), CreatedAt = now, CreatedBy = _user.UserId
            };
            await _attemptRegistrations.AddAsync(row);
            await _auditLogs.AddAsync(new AuditLog
            {
                TenantId = tenant, UserId = _user.UserId, EntityName = "AssessmentAttemptRegistration",
                Action = "Request", EntityId = subject.Id,
                NewValue = JsonSerializer.Serialize(new
                {
                    AssessmentSubjectId = subject.Id, StudentSubjectRegistrationId = course.Id,
                    PriorResultPublicationId = prior.Id, exam.AttemptMode
                }),
                IsSuccess = true, OccurredAt = now
            });
            await _uow.SaveChangesAsync(token);
            return ApiResponse<AssessmentAttemptRegistrationDto>.SuccessResponse(new AssessmentAttemptRegistrationDto
            {
                Id = row.Id, AssessmentSubjectId = row.AssessmentSubjectId,
                StudentSubjectRegistrationId = row.StudentSubjectRegistrationId,
                PriorResultPublicationId = row.PriorResultPublicationId,
                Decision = row.Decision, AttemptMode = exam.AttemptMode,
                Reason = row.Reason, RowVersion = Version(row.RowVersion)
            }, "Attempt requested; controller approval required before marks entry.");
        }, ct);
    }

    public Task<ApiResponse<bool>> DecideAssessmentAttemptAsync(long attemptId,
        DecideAssessmentAttemptDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<bool>("Attempt approval permission required.", 403));
        if (_attemptRegistrations == null) return Task.FromResult(Error<bool>("Attempt register is unavailable.", 503));
        if (attemptId <= 0 || request == null ||
            request.Decision is not (AssessmentAttemptDecision.Approved or AssessmentAttemptDecision.Rejected) ||
            string.IsNullOrWhiteSpace(request.RowVersion) || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length > 500)
            return Task.FromResult(Error<bool>("Valid decision, reason and row version are required."));
        return WriteAsync("decide retake/improvement", async token =>
        {
            var tenant = _user.TenantId;
            var row = await _attemptRegistrations!.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == attemptId && !x.IsDeleted, token);
            var subject = row == null ? null : await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == row.AssessmentSubjectId && !x.IsDeleted, token);
            var exam = subject == null ? null : await _assessments.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == subject.AssessmentId && !x.IsDeleted, token);
            if (exam == null || !await CanAccessCampusAsync(exam.CampusId, token))
                return Error<bool>("Attempt not found.", 404);
            if (!Matches(row!.RowVersion, request.RowVersion))
                return Error<bool>("Attempt was changed; reload.", 409);
            if (row.Decision != AssessmentAttemptDecision.Requested ||
                exam.State is not (AssessmentState.Draft or AssessmentState.Scheduled))
                return Error<bool>("Only pending attempts before marks entry may be decided.", 409);
            var pinnedSource = await _publications.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == row.PriorResultPublicationId && !x.IsDeleted &&
                x.State == ResultPublicationState.Published && x.VisibleToStudent, token);
            if (pinnedSource == null || await _publications.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AssessmentId == pinnedSource.AssessmentId &&
                x.AcademicBatchId == pinnedSource.AcademicBatchId && x.VersionNo > pinnedSource.VersionNo &&
                (x.State == ResultPublicationState.Published || x.State == ResultPublicationState.Withdrawn) &&
                !x.IsDeleted, token))
                return Error<bool>("Prior result is no longer the active published version.", 409);
            if (await _marks.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AssessmentSubjectId == subject!.Id &&
                x.StudentSubjectRegistrationId == row.StudentSubjectRegistrationId && !x.IsDeleted, token))
                return Error<bool>("Existing mark evidence prevents attempt decision.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            row.Decision = request.Decision; row.DecisionNote = request.Reason.Trim();
            row.DecidedAt = now; row.DecidedByUserId = _user.UserId;
            row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            _attemptRegistrations.Update(row);
            // Shares the assessment rowversion boundary with lock and publication.
            exam.UpdatedAt = now; exam.UpdatedBy = _user.UserId;
            _assessments.Update(exam);
            await _auditLogs.AddAsync(new AuditLog
            {
                TenantId = tenant, UserId = _user.UserId, EntityName = "AssessmentAttemptRegistration",
                Action = request.Decision.ToString(), EntityId = row.Id,
                NewValue = JsonSerializer.Serialize(new { row.Decision, row.DecisionNote, row.PriorResultPublicationId }),
                IsSuccess = true, OccurredAt = now
            });
            await _uow.SaveChangesAsync(token);
            return ApiResponse<bool>.SuccessResponse(true, "Attempt decision recorded.");
        }, ct);
    }

    private IQueryable<AssessmentAttemptRegistration> ApprovedAttemptQuery(long subjectId)
    {
        if (_attemptRegistrations == null)
            return Array.Empty<AssessmentAttemptRegistration>().AsQueryable();
        return _attemptRegistrations.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.AssessmentSubjectId == subjectId &&
            x.Decision == AssessmentAttemptDecision.Approved && !x.IsDeleted);
    }
}
