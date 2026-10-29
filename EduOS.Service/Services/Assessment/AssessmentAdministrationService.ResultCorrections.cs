using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    // A rejected calculated snapshot must roll back earlier mark changes. Returning
    // an unsuccessful ApiResponse from a transaction callback would still commit.
    private sealed class ResultCorrectionRollbackException : Exception
    {
        public ResultCorrectionRollbackException(string message) : base(message) { }
    }

    private static ResultCorrectionDto MapCorrection(ResultCorrectionEvent row) => new()
    {
        CaseReference = row.CaseReference,
        PreviousPublicationId = row.PreviousPublicationId,
        NewPublicationId = row.NewPublicationId,
        PreviousVersionNo = row.PreviousVersionNo,
        NewVersionNo = row.NewVersionNo,
        CorrectedByUserId = row.ActorUserId,
        CorrectedAt = row.OccurredAt,
        CorrectedSubjectCount = row.CorrectedSubjectCount,
        Reason = row.Reason
    };

    public async Task<ApiResponse<ResultCorrectionDto>> GetResultCorrectionAsync(
        Guid caseReference, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<ResultCorrectionDto>("Correction permission required.", 403);
        if (caseReference == Guid.Empty) return Error<ResultCorrectionDto>("Invalid correction case.");
        if (_corrections == null) return Error<ResultCorrectionDto>("Correction ledger unavailable.", 503);
        var row = await _corrections.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.CaseReference == caseReference && !x.IsDeleted, ct);
        if (row == null) return Error<ResultCorrectionDto>("Correction not found.", 404);
        var scope = await FindRescrutinyScopeAsync(row.PreviousPublicationId, ct);
        if (scope == null || !await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<ResultCorrectionDto>("Correction not found in accessible campus.", 404);
        if (row.PreviousVersionNo != scope.Publication.VersionNo ||
            row.NewVersionNo != row.PreviousVersionNo + 1 ||
            !await _publications.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == _user.TenantId && x.Id == row.NewPublicationId &&
                x.AssessmentId == scope.Publication.AssessmentId &&
                x.AcademicBatchId == scope.Publication.AcademicBatchId &&
                x.VersionNo == row.NewVersionNo && !x.IsDeleted, ct))
            return Error<ResultCorrectionDto>("Correction publication history requires reconciliation.", 409);
        var proofs = await GetPublicationEvidenceAsync(
            [row.PreviousPublicationId, row.NewPublicationId], scope.CampusId, scope.AcademicYearId, ct);
        if (!proofs.TryGetValue(row.PreviousPublicationId, out var oldProof) || !oldProof.IsReconciled ||
            !proofs.TryGetValue(row.NewPublicationId, out var newProof) || !newProof.IsReconciled)
            return Error<ResultCorrectionDto>("Correction snapshots require reconciliation.", 409);
        return ApiResponse<ResultCorrectionDto>.SuccessResponse(MapCorrection(row));
    }

    /// <summary>
    /// Applies a reviewer-accepted case by a DIFFERENT administrator. Every subject
    /// and component edit, immutable version withdrawal, next version, cohort-wide
    /// snapshot and permanent correction evidence commit or roll back together.
    /// </summary>
    public async Task<ApiResponse<ResultCorrectionDto>> ApplyResultCorrectionAsync(
        Guid caseReference, ApplyResultCorrectionRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<ResultCorrectionDto>("Correction permission required.", 403);
        if (_corrections == null) return Error<ResultCorrectionDto>("Correction ledger unavailable.", 503);
        if (caseReference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            !TryVersion(request.PublicationRowVersion, out _) ||
            !ValidReason(request.Reason) || request.Reason.Trim().Length < 10 ||
            request.Subjects == null || request.Subjects.Count is < 1 or > 25 ||
            request.Subjects.Any(x => x == null || x.AssessmentSubjectId <= 0 ||
                !TryVersion(x.RowVersion, out _) || x.ObtainedMarks < 0 || !Exact2(x.ObtainedMarks) ||
                x.IsAbsent && x.IsWithheld || (x.IsAbsent || x.IsWithheld) && x.ObtainedMarks != 0 ||
                x.Components == null || x.Components.Count > 50 || x.Components.Any(c => c == null ||
                    c.AssessmentComponentId <= 0 || !TryVersion(c.RowVersion, out _) ||
                    c.ObtainedMarks < 0 || !Exact2(c.ObtainedMarks) ||
                    c.IsAbsent && c.IsWithheld || (c.IsAbsent || c.IsWithheld) && c.ObtainedMarks != 0) ||
                x.Components.Select(c => c.AssessmentComponentId).Distinct().Count() != x.Components.Count) ||
            request.Subjects.Select(x => x.AssessmentSubjectId).Distinct().Count() != request.Subjects.Count)
            return Error<ResultCorrectionDto>("Valid case, unchanged row versions, reason and distinct corrected marks required.");

        // Replay token binds the exact requested edits and original concurrency
        // tokens; a reused idempotency key cannot silently correct different marks.
        var digest = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        try
        {
            return await WriteAsync("apply audited result correction", async token =>
            {
                var tenant = _user.TenantId;
                var replay = await _corrections.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, token);
                if (replay != null)
                    return replay.CaseReference == caseReference && replay.ActorUserId == _user.UserId &&
                        replay.RequestDigest == digest
                        ? ApiResponse<ResultCorrectionDto>.SuccessResponse(MapCorrection(replay), "Correction already applied.")
                        : Error<ResultCorrectionDto>("Correction request key belongs to another change.", 409);

                var original = await _rescrutinyEvents.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.CaseReference == caseReference && !x.IsTerminal &&
                    x.Action == ResultRescrutinyAction.Requested && !x.IsDeleted, token);
                if (original == null) return Error<ResultCorrectionDto>("Accepted re-scrutiny case not found.", 404);
                var scope = await FindRescrutinyScopeAsync(original.ResultPublicationId, token);
                if (scope == null || !await CanAccessCampusAsync(scope.CampusId, token))
                    return Error<ResultCorrectionDto>("Campus or publication not available.", 403);
                if (scope.Publication.State != ResultPublicationState.Published ||
                    scope.Publication.VersionNo != original.PublicationVersionNo ||
                    scope.Publication.VersionNo == int.MaxValue ||
                    !Matches(scope.Publication.RowVersion, request.PublicationRowVersion))
                    return Error<ResultCorrectionDto>("Published result version changed; reload case.", 409);
                if (!await HasValidRescrutinyEvidenceAsync(scope, token) ||
                    !await HasReconciledRescrutinyLedgerAsync(scope, token))
                    return Error<ResultCorrectionDto>("Published or case evidence requires reconciliation.", 409);
                var decisions = await _rescrutinyEvents.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.CaseReference == caseReference && x.IsTerminal &&
                    !x.IsDeleted).Take(2).ToListAsync(token);
                if (decisions.Count != 1 || decisions[0].Action != ResultRescrutinyAction.AcceptedForCorrection ||
                    decisions[0].ActorUserId == _user.UserId ||
                    decisions[0].ActorUserId == original.ActorUserId)
                    return Error<ResultCorrectionDto>("Independent acceptance and a separate correction officer required.", 403);
                if (await _corrections.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.CaseReference == caseReference, token))
                    return Error<ResultCorrectionDto>("This re-scrutiny case is already corrected.", 409);
                var publications = await _publications.GetQueryable().Where(x =>
                    x.TenantId == tenant && x.AssessmentId == scope.Publication.AssessmentId &&
                    x.AcademicBatchId == scope.Publication.AcademicBatchId && !x.IsDeleted)
                    .ToListAsync(token);
                if (publications.Count(x => x.State == ResultPublicationState.Published) != 1 ||
                    publications.Any(x => x.VersionNo >= scope.Publication.VersionNo &&
                        x.Id != scope.Publication.Id))
                    return Error<ResultCorrectionDto>("Latest publication version requires reconciliation.", 409);
                var oldPublication = publications.Single(x => x.Id == scope.Publication.Id);
                if (!Matches(oldPublication.RowVersion, request.PublicationRowVersion))
                    return Error<ResultCorrectionDto>("Publication was updated concurrently.", 409);

                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == original.StudentEnrollmentId &&
                    x.AcademicBatchId == scope.Publication.AcademicBatchId &&
                    x.AcademicYearId == scope.AcademicYearId && x.CampusId == scope.CampusId &&
                    !x.IsDeleted, token);
                if (enrollment == null) return Error<ResultCorrectionDto>("Student enrollment not found.", 409);
                // Reversal of completed subjects could invalidate downstream
                // course registration; it requires a separate academic reversal.
                if (await (from subject in _subjects.GetQueryable().AsNoTracking()
                    join registration in _registrations.GetQueryable().AsNoTracking()
                        on new { subject.TenantId, Id = subject.SubjectOfferingId }
                        equals new { registration.TenantId, Id = registration.SubjectOfferingId }
                    join registeredEnrollment in _enrollments.GetQueryable().AsNoTracking()
                        on new { registration.TenantId, Id = registration.StudentEnrollmentId }
                        equals new { registeredEnrollment.TenantId, registeredEnrollment.Id }
                    where subject.TenantId == tenant && subject.AssessmentId == scope.Publication.AssessmentId &&
                        registeredEnrollment.AcademicBatchId == scope.Publication.AcademicBatchId &&
                        registration.State == SubjectRegistrationState.Completed &&
                        !subject.IsDeleted && !registration.IsDeleted && !registeredEnrollment.IsDeleted
                    select registration.Id).AnyAsync(token))
                    return Error<ResultCorrectionDto>("Completed subject registrations require academic reversal before correction.", 409);
                // Existing issued documents have unversioned verification identities;
                // require revocation before modifying the result they could attest.
                if (await _certificates.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.StudentId == enrollment.StudentId &&
                        !x.IsRevoked && !x.IsDeleted, token) ||
                    await _transcripts.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.StudentId == enrollment.StudentId &&
                        !x.IsRevoked && !x.IsDeleted, token))
                    return Error<ResultCorrectionDto>("Revoke active student certificates/transcripts before amendment.", 409);

                var subjectIds = request.Subjects.Select(x => x.AssessmentSubjectId).ToArray();
                var subjectRows = await (from subject in _subjects.GetQueryable().AsNoTracking()
                    join offering in _offerings.GetQueryable().AsNoTracking()
                        on new { subject.TenantId, Id = subject.SubjectOfferingId }
                        equals new { offering.TenantId, offering.Id }
                    join registration in _registrations.GetQueryable().AsNoTracking()
                        on new { offering.TenantId, Id = offering.Id }
                        equals new { registration.TenantId, Id = registration.SubjectOfferingId }
                    where subject.TenantId == tenant && subject.AssessmentId == scope.Publication.AssessmentId &&
                        subjectIds.Contains(subject.Id) && offering.AcademicBatchId == scope.Publication.AcademicBatchId &&
                        offering.AcademicYearId == scope.AcademicYearId &&
                        registration.StudentEnrollmentId == enrollment.Id &&
                        registration.State == SubjectRegistrationState.Approved &&
                        !subject.IsDeleted && !offering.IsDeleted && !registration.IsDeleted
                    select new { Subject = subject, RegistrationId = registration.Id }).ToListAsync(token);
                if (subjectRows.Count != request.Subjects.Count ||
                    subjectRows.Select(x => x.Subject.Id).Distinct().Count() != request.Subjects.Count)
                    return Error<ResultCorrectionDto>("Correction subjects are not unique approved registrations.", 409);
                var registrationIds = subjectRows.Select(x => x.RegistrationId).ToArray();
                var masters = await _marks.GetQueryable().Where(x => x.TenantId == tenant &&
                    subjectIds.Contains(x.AssessmentSubjectId) &&
                    registrationIds.Contains(x.StudentSubjectRegistrationId) && !x.IsDeleted)
                    .ToListAsync(token);
                if (masters.Count != request.Subjects.Count ||
                    masters.GroupBy(x => (x.AssessmentSubjectId, x.StudentSubjectRegistrationId))
                        .Any(g => g.Count() != 1))
                    return Error<ResultCorrectionDto>("Existing subject marks require reconciliation.", 409);
                var masterByPair = masters.ToDictionary(x => (x.AssessmentSubjectId, x.StudentSubjectRegistrationId));
                var definitions = await _components.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && subjectIds.Contains(x.AssessmentSubjectId) && !x.IsDeleted)
                    .ToListAsync(token);
                var masterIds = masters.Select(x => x.Id).ToArray();
                var details = await _componentMarks.GetQueryable().Where(x =>
                    x.TenantId == tenant && masterIds.Contains(x.StudentAssessmentMarkId) && !x.IsDeleted)
                    .ToListAsync(token);
                var schemeIds = subjectRows.Select(x => x.Subject.GradeSchemeId).Where(x => x.HasValue)
                    .Select(x => x!.Value).Distinct().ToArray();
                var assessment = await _assessments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == scope.Publication.AssessmentId && !x.IsDeleted, token);
                if (assessment == null || assessment.State != AssessmentState.Locked)
                    return Error<ResultCorrectionDto>("Assessment is no longer locked.", 409);
                if (assessment.CountsTowardCgpa && await (
                    from otherSummary in _summaries.GetQueryable().AsNoTracking()
                    join laterPublication in _publications.GetQueryable().AsNoTracking()
                        on new { otherSummary.TenantId, Id = otherSummary.ResultPublicationId }
                        equals new { laterPublication.TenantId, laterPublication.Id }
                    join laterAssessment in _assessments.GetQueryable().AsNoTracking()
                        on new { laterPublication.TenantId, Id = laterPublication.AssessmentId }
                        equals new { laterAssessment.TenantId, laterAssessment.Id }
                    join laterEnrollment in _enrollments.GetQueryable().AsNoTracking()
                        on new { otherSummary.TenantId, Id = otherSummary.StudentEnrollmentId }
                        equals new { laterEnrollment.TenantId, laterEnrollment.Id }
                    where otherSummary.TenantId == tenant && !otherSummary.IsDeleted &&
                        !laterPublication.IsDeleted && !laterAssessment.IsDeleted &&
                        !laterEnrollment.IsDeleted && laterEnrollment.StudentId == enrollment.StudentId &&
                        laterAssessment.Id != assessment.Id && laterAssessment.CountsTowardCgpa &&
                        laterAssessment.StartDate > assessment.EndDate &&
                        laterAssessment.CampusId == assessment.CampusId &&
                        laterPublication.State == ResultPublicationState.Published
                    select otherSummary.Id).AnyAsync(token))
                    return Error<ResultCorrectionDto>(
                        "Later cumulative publications depend on this result; use a controlled downstream amendment.", 409);
                if (assessment.GradeSchemeId.HasValue)
                    schemeIds = schemeIds.Append(assessment.GradeSchemeId.Value).Distinct().ToArray();
                var gradeRules = await _grades.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && schemeIds.Contains(x.GradeSchemeId) && !x.IsDeleted)
                    .ToListAsync(token);

                var now = _clock.GetUtcNow().UtcDateTime;
                var before = new List<object>();
                var after = new List<object>();
                foreach (var input in request.Subjects)
                {
                    var subject = subjectRows.Single(x => x.Subject.Id == input.AssessmentSubjectId);
                    if (input.ObtainedMarks > subject.Subject.FullMarks || subject.Subject.FullMarks <= 0 ||
                        !masterByPair.TryGetValue((subject.Subject.Id, subject.RegistrationId), out var master) ||
                        !Matches(master.RowVersion, input.RowVersion))
                        return Error<ResultCorrectionDto>("Subject marks changed or exceed configured maximum.", 409);
                    var configured = definitions.Where(x => x.AssessmentSubjectId == subject.Subject.Id).ToList();
                    var current = details.Where(x => x.StudentAssessmentMarkId == master.Id).ToList();
                    if (configured.Count != input.Components.Count || current.Count != input.Components.Count ||
                        current.Select(x => x.AssessmentComponentId).Distinct().Count() != current.Count ||
                        input.Components.Any(c => !configured.Any(d => d.Id == c.AssessmentComponentId &&
                            c.ObtainedMarks <= d.FullMarks) ||
                            !current.Any(x => x.AssessmentComponentId == c.AssessmentComponentId &&
                                Matches(x.RowVersion, c.RowVersion))))
                        return Error<ResultCorrectionDto>("Component marks changed or require reconciliation.", 409);
                    var projected = current.Select(x =>
                    {
                        var update = input.Components.Single(y => y.AssessmentComponentId == x.AssessmentComponentId);
                        return new StudentAssessmentComponentMark
                        {
                            AssessmentComponentId = x.AssessmentComponentId,
                            StudentAssessmentMarkId = x.StudentAssessmentMarkId,
                            ObtainedMarks = update.ObtainedMarks,
                            IsAbsent = update.IsAbsent, IsWithheld = update.IsWithheld
                        };
                    }).ToList();
                    if (!AssessmentComponentMarkPolicy.Evaluate(subject.Subject.FullMarks,
                            master.ObtainedMarks, master.IsAbsent, master.IsWithheld,
                            configured, current).IsReconciled ||
                        !AssessmentComponentMarkPolicy.Evaluate(subject.Subject.FullMarks,
                            input.ObtainedMarks, input.IsAbsent, input.IsWithheld,
                            configured, projected).IsReconciled)
                        return Error<ResultCorrectionDto>("Subject and component totals do not reconcile.", 409);
                    var currentEvidence = current.OrderBy(x => x.AssessmentComponentId)
                        .Select(x => new { x.AssessmentComponentId, x.ObtainedMarks, x.IsAbsent, x.IsWithheld }).ToArray();
                    var amendedEvidence = projected.OrderBy(x => x.AssessmentComponentId)
                        .Select(x => new { x.AssessmentComponentId, x.ObtainedMarks, x.IsAbsent, x.IsWithheld }).ToArray();
                    before.Add(new { subjectId = subject.Subject.Id, master.StudentSubjectRegistrationId,
                        master.ObtainedMarks, master.IsAbsent, master.IsWithheld,
                        components = currentEvidence });
                    after.Add(new { subjectId = subject.Subject.Id, master.StudentSubjectRegistrationId,
                        input.ObtainedMarks, input.IsAbsent, input.IsWithheld,
                        components = amendedEvidence });
                    if (master.ObtainedMarks == input.ObtainedMarks && master.IsAbsent == input.IsAbsent &&
                        master.IsWithheld == input.IsWithheld &&
                        currentEvidence.Select(x => (x.AssessmentComponentId, x.ObtainedMarks, x.IsAbsent, x.IsWithheld))
                            .SequenceEqual(amendedEvidence.Select(x =>
                                (x.AssessmentComponentId, x.ObtainedMarks, x.IsAbsent, x.IsWithheld))))
                        return Error<ResultCorrectionDto>("Include changed marks only; unchanged subject submitted.", 409);
                    var schemeId = subject.Subject.GradeSchemeId ?? assessment.GradeSchemeId;
                    var percentage = input.ObtainedMarks * 100m / subject.Subject.FullMarks;
                    var grade = gradeRules.Where(x => x.GradeSchemeId == schemeId &&
                            percentage >= x.MinMarks && percentage <= x.MaxMarks)
                        .OrderByDescending(x => x.MinMarks).FirstOrDefault();
                    if (schemeId == null || grade == null)
                        return Error<ResultCorrectionDto>("Subject has no matching grading rule.", 409);
                    master.ObtainedMarks = input.ObtainedMarks;
                    master.IsAbsent = input.IsAbsent; master.IsWithheld = input.IsWithheld;
                    master.GradeLetter = grade.GradeLetter; master.GradePoint = grade.GradePoint;
                    master.EnteredByUserId = _user.UserId; master.EnteredAt = now;
                    master.UpdatedBy = _user.UserId; master.UpdatedAt = now;
                    _marks.Update(master);
                    foreach (var existing in current)
                    {
                        var changed = input.Components.Single(x => x.AssessmentComponentId == existing.AssessmentComponentId);
                        existing.ObtainedMarks = changed.ObtainedMarks;
                        existing.IsAbsent = changed.IsAbsent; existing.IsWithheld = changed.IsWithheld;
                        existing.EnteredByUserId = _user.UserId; existing.EnteredAt = now;
                        existing.UpdatedBy = _user.UserId; existing.UpdatedAt = now;
                        _componentMarks.Update(existing);
                    }
                }
                var beforeJson = JsonSerializer.Serialize(before);
                var afterJson = JsonSerializer.Serialize(after);
                if (beforeJson.Length > 100_000 || afterJson.Length > 100_000)
                    return Error<ResultCorrectionDto>("Correction evidence is too large.", 409);
                await _uow.SaveChangesAsync(token);
                var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == scope.Publication.AcademicBatchId && !x.IsDeleted, token);
                if (batch == null) throw new ResultCorrectionRollbackException("Assessment batch changed.");
                var recomputed = await CalculateAsync(assessment, batch, token);
                if (recomputed.Error != null || recomputed.Sheet == null)
                    throw new ResultCorrectionRollbackException(recomputed.Error ?? "Recalculation failed.");
                if (!CanCaptureCourseSnapshots(recomputed.Sheet) ||
                    (_courseSnapshots == null && recomputed.Sheet.Results.Any(x => x.CreditHours.HasValue)))
                    throw new ResultCorrectionRollbackException("Course grade snapshot storage requires reconciliation.");
                var priorSummaries = await _summaries.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.ResultPublicationId == oldPublication.Id && !x.IsDeleted)
                    .ToListAsync(token);
                var priorIds = priorSummaries.Select(x => x.StudentEnrollmentId).ToList();
                var enrollmentRows = await _enrollments.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.AcademicBatchId == batch.Id && x.CampusId == batch.CampusId &&
                    x.AcademicYearId == batch.AcademicYearId && x.IsCurrent &&
                    x.State == EnrollmentState.Active && !x.IsDeleted)
                    .Select(x => new { x.Id, x.RollNo }).Take(5001).ToListAsync(token);
                if (priorIds.Count != recomputed.Sheet.Results.Count ||
                    enrollmentRows.Count != recomputed.Sheet.Results.Count ||
                    priorIds.Distinct().Count() != priorIds.Count ||
                    enrollmentRows.Select(x => x.RollNo).Distinct().Count() != enrollmentRows.Count ||
                    !priorIds.Order().SequenceEqual(enrollmentRows.Select(x => x.Id).Order()))
                    throw new ResultCorrectionRollbackException("Published cohort membership changed; manual reconciliation required.");
                var enrollmentByRoll = enrollmentRows.ToDictionary(x => x.RollNo, x => x.Id);
                if (recomputed.Sheet.Results.Any(x => !enrollmentByRoll.ContainsKey(x.RollNo)))
                    throw new ResultCorrectionRollbackException("Recalculated roster does not match publication.");
                var beforeByEnrollment = priorSummaries.ToDictionary(x => x.StudentEnrollmentId);
                if (recomputed.Sheet.Results.Any(result =>
                {
                    var studentEnrollmentId = enrollmentByRoll[result.RollNo];
                    if (studentEnrollmentId == enrollment.Id) return false;
                    var prior = beforeByEnrollment[studentEnrollmentId];
                    return prior.TotalMarks != result.MaximumMarks ||
                        prior.ObtainedMarks != result.TotalMarks ||
                        prior.Percentage != result.Percentage ||
                        prior.GPA != result.GradePointAverage ||
                        prior.CGPA != result.CumulativeGradePointAverage ||
                        prior.CreditHours != result.CreditHours ||
                        (prior.CourseCount.HasValue && prior.CourseCount != result.Courses.Count) ||
                        prior.GradeLetter != result.GradeLetter ||
                        prior.IsPassed != result.IsPassed || prior.IsWithheld != result.IsWithheld;
                }))
                    throw new ResultCorrectionRollbackException(
                        "Unrelated student outcomes changed; reconcile academic data before correction.");

                oldPublication.State = ResultPublicationState.Withdrawn;
                oldPublication.VisibleToStudent = false; oldPublication.VisibleToGuardian = false;
                oldPublication.UpdatedAt = now; oldPublication.UpdatedBy = _user.UserId;
                // Already tracked: the immutable-history guard permits only these
                // five withdrawal fields, not a blanket EntityState.Modified.
                var version = oldPublication.VersionNo + 1;
                var replacement = new ResultPublication
                {
                    TenantId = tenant, AssessmentId = oldPublication.AssessmentId,
                    AcademicBatchId = oldPublication.AcademicBatchId, VersionNo = version,
                    State = ResultPublicationState.Published,
                    PublishedAt = now, PublishedByUserId = _user.UserId,
                    HasCourseGradeSnapshots = _courseSnapshots != null,
                    VisibleToStudent = scope.Publication.VisibleToStudent,
                    VisibleToGuardian = scope.Publication.VisibleToGuardian,
                    PublishNote = "Audited re-scrutiny correction " + caseReference.ToString("D"),
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _publications.AddAsync(replacement);
                await _uow.SaveChangesAsync(token);
                foreach (var result in recomputed.Sheet.Results)
                    await _summaries.AddAsync(new StudentResultSummary
                    {
                        TenantId = tenant, ResultPublicationId = replacement.Id,
                        AssessmentId = assessment.Id,
                        StudentEnrollmentId = enrollmentByRoll[result.RollNo],
                        PublicationVersionNo = version,
                        TotalMarks = result.MaximumMarks, ObtainedMarks = result.TotalMarks,
                        Percentage = result.Percentage, GPA = result.GradePointAverage,
                        CGPA = result.CumulativeGradePointAverage, CreditHours = result.CreditHours,
                        CourseCount = _courseSnapshots != null ? result.Courses.Count : null,
                        GradeLetter = result.GradeLetter,
                        MeritPosition = result.MeritPosition, IsPassed = result.IsPassed,
                        IsWithheld = result.IsWithheld, CalculatedAt = now,
                        CreatedAt = now, CreatedBy = _user.UserId
                    });
                await CaptureCourseSnapshotsAsync(replacement, recomputed.Sheet,
                    enrollmentByRoll, now, token);
                var correction = new ResultCorrectionEvent
                {
                    TenantId = tenant, CaseReference = caseReference,
                    ClientRequestId = request.ClientRequestId, RequestDigest = digest,
                    PreviousPublicationId = oldPublication.Id, NewPublicationId = replacement.Id,
                    PreviousVersionNo = oldPublication.VersionNo, NewVersionNo = version,
                    StudentEnrollmentId = enrollment.Id, ActorUserId = _user.UserId,
                    OccurredAt = now, Reason = request.Reason.Trim(),
                    CorrectedSubjectCount = request.Subjects.Count,
                    BeforeJson = beforeJson, AfterJson = afterJson,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _corrections.AddAsync(correction);
                await RecordAuditAsync("Correct", "ResultPublication", oldPublication.Id,
                    $"Re-scrutiny {caseReference:D}; replacement version {version}; {request.Reason.Trim()}", now);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<ResultCorrectionDto>.SuccessResponse(MapCorrection(correction),
                    "Result corrected; previous immutable version retained.");
            }, ct);
        }
        catch (ResultCorrectionRollbackException ex)
        {
            return Error<ResultCorrectionDto>(ex.Message, 409);
        }
    }
}
