using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.LMS;

public sealed partial class LmsWorkflowService
{
    // SQL Server may report a race as a direct SqlException, an EF update error,
    // or an execution-strategy InvalidOperationException. Only known write
    // conflicts become HTTP 409; unrelated database faults must remain visible.
    private static bool IsCertificateSqlConflict(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
            if (current is Microsoft.Data.SqlClient.SqlException sql &&
                sql.Number is 1205 or 1222 or 3960 or 2601 or 2627)
                return true;
        return false;
    }

    /// <summary>
    /// Issuance is idempotent by tenant/request GUID and unique active enrollment.
    /// A revoked certificate can only be reissued as a new revision retaining the
    /// original evidence snapshot; no V1 exam/result record is modified.
    /// </summary>
    public async Task<ApiResponse<LmsCertificateDto>> IssueCertificateAsync(
        IssueLmsCertificateRequestDto request, CancellationToken ct = default)
    {
        if (!CanTeach()) return Error<LmsCertificateDto>("LMS instructor permission required.", 403);
        if (request == null || request.CourseEnrollmentId <= 0 || request.ClientRequestId == Guid.Empty)
            return Error<LmsCertificateDto>("Enrollment and request reference are required.");
        if (_certificates == null || _certificateAudits == null || _completionQuizzes == null || _completionAttempts == null)
            return Error<LmsCertificateDto>("LMS certificate repositories are unavailable.", 503);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var tenant = _user.TenantId;
                var existingRequest = await _certificates.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, token);
                if (existingRequest != null)
                {
                    if (existingRequest.CourseEnrollmentId != request.CourseEnrollmentId)
                        return Error<LmsCertificateDto>("Request reference belongs to another enrollment.", 409);
                    var previousCourse = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.Id == existingRequest.CourseId && !x.IsDeleted, token);
                    if (previousCourse == null || !await CanEditAsync(previousCourse, token))
                        return Error<LmsCertificateDto>("Course not found or access denied.", 404);
                    return ApiResponse<LmsCertificateDto>.SuccessResponse(
                        MapCertificate(existingRequest, previousCourse.PublicId), "Certificate request already processed.");
                }
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.CourseEnrollmentId && !x.IsDeleted, token);
                if (enrollment == null) return Error<LmsCertificateDto>("Course enrollment not found.", 404);
                var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == enrollment.CourseId && !x.IsDeleted, token);
                if (course == null || !await CanEditAsync(course, token))
                    return Error<LmsCertificateDto>("Course not found or instructor access denied.", 404);
                if (enrollment.State != CourseEnrollmentState.Completed || !enrollment.CompletedAt.HasValue ||
                    enrollment.CompletedAt.Value < enrollment.EnrolledAt ||
                    enrollment.CompletedAt.Value > _clock.GetUtcNow().UtcDateTime)
                    return Error<LmsCertificateDto>("A verified completed course enrollment is required.", 409);
                var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == enrollment.StudentId && !x.IsDeleted, token);
                if (student == null) return Error<LmsCertificateDto>("Student not found.", 404);
                if (enrollment.StudentEnrollmentId.HasValue && !await _academicEnrollments.GetQueryable()
                    .AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                        x.Id == enrollment.StudentEnrollmentId.Value && x.StudentId == student.Id &&
                        (!course.AcademicProgramId.HasValue || x.AcademicProgramId == course.AcademicProgramId.Value) &&
                        !x.IsDeleted, token))
                    return Error<LmsCertificateDto>("Original academic enrollment is unavailable.", 409);

                var latest = await _certificates.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.CourseEnrollmentId == enrollment.Id && !x.IsDeleted)
                    .OrderByDescending(x => x.Revision).ThenByDescending(x => x.Id).FirstOrDefaultAsync(token);
                if (latest != null && !latest.IsRevoked)
                    return ApiResponse<LmsCertificateDto>.SuccessResponse(
                        MapCertificate(latest, course.PublicId), "Certificate already issued.");
                if (latest != null && !IsManager())
                    return Error<LmsCertificateDto>("Reissue requires principal or tenant administrator approval.", 403);

                CourseCompletionEligibilityDto evidence;
                if (latest == null)
                {
                    evidence = await EvaluateCourseCompletionAsync(enrollment, course, token);
                    if (!evidence.Ready)
                        return Error<LmsCertificateDto>(
                            "Completion evidence is not valid for certificate issuance.", 409);
                }
                else
                {
                    // Reissue does not rewrite historical names or requirements.
                    evidence = new CourseCompletionEligibilityDto
                    {
                        RequiredLessons = latest.RequiredLessonsSnapshot,
                        CompletedLessons = latest.CompletedLessonsSnapshot,
                        RequiredAssignments = latest.RequiredAssignmentsSnapshot,
                        GradedAssignments = latest.GradedAssignmentsSnapshot,
                        RequiredQuizzes = latest.RequiredQuizzesSnapshot,
                        PassedQuizzes = latest.PassedQuizzesSnapshot,
                        Ready = true
                    };
                }

                var now = _clock.GetUtcNow().UtcDateTime;
                var certificate = new LmsCompletionCertificate
                {
                    TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId,
                    CourseEnrollmentId = enrollment.Id, CourseId = course.Id, StudentId = student.Id,
                    Revision = (latest?.Revision ?? 0) + 1,
                    CourseTitleSnapshot = latest?.CourseTitleSnapshot ?? course.Title,
                    CourseCodeSnapshot = latest?.CourseCodeSnapshot ?? course.Code,
                    StudentNameSnapshot = latest?.StudentNameSnapshot ?? student.FullName,
                    StudentCodeSnapshot = latest?.StudentCodeSnapshot ?? student.StudentCode,
                    CompletedAtSnapshot = latest?.CompletedAtSnapshot ?? enrollment.CompletedAt.Value,
                    RequiredLessonsSnapshot = evidence.RequiredLessons,
                    CompletedLessonsSnapshot = evidence.CompletedLessons,
                    RequiredAssignmentsSnapshot = evidence.RequiredAssignments,
                    GradedAssignmentsSnapshot = evidence.GradedAssignments,
                    RequiredQuizzesSnapshot = evidence.RequiredQuizzes,
                    PassedQuizzesSnapshot = evidence.PassedQuizzes,
                    IssuedAt = now, IssuedByUserId = _user.UserId, CreatedAt = now, CreatedBy = _user.UserId
                };
                await _certificates.AddAsync(certificate);
                await _uow.SaveChangesAsync(token);
                await WriteCertificateAuditAsync(certificate, latest == null
                    ? "LMS.CertificateIssued" : "LMS.CertificateReissued", now);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<LmsCertificateDto>.SuccessResponse(
                    MapCertificate(certificate, course.PublicId), latest == null
                        ? "LMS certificate issued." : "LMS certificate reissued.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        { return Error<LmsCertificateDto>("Certificate changed concurrently.", 409); }
        catch (Exception ex) when (IsCertificateSqlConflict(ex))
        {
            _logger.LogWarning(ex, "LMS certificate issuance SQL conflict tenant {TenantId}", _user.TenantId);
            return Error<LmsCertificateDto>("Certificate issuance conflicts with another request.", 409);
        }
    }

    public async Task<ApiResponse<LmsCertificateDto>> RevokeCertificateAsync(
        Guid reference, RevokeLmsCertificateRequestDto request, CancellationToken ct = default)
    {
        if (!Authenticated() || !IsManager()) return Error<LmsCertificateDto>("Principal or tenant administrator approval required.", 403);
        if (reference == Guid.Empty || request == null || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length > 500 || !TryVersion(request.RowVersion, out _))
            return Error<LmsCertificateDto>("Certificate, reason and valid row version are required.");
        if (_certificates == null || _certificateAudits == null)
            return Error<LmsCertificateDto>("Certificate audit repositories unavailable.", 503);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var tenant = _user.TenantId;
                var row = await _certificates.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted, token);
                if (row == null) return Error<LmsCertificateDto>("Certificate not found.", 404);
                var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == row.CourseId && !x.IsDeleted, token);
                if (course == null || !await CanEditAsync(course, token))
                    return Error<LmsCertificateDto>("Certificate not found or access denied.", 404);
                if (row.IsRevoked)
                    return ApiResponse<LmsCertificateDto>.SuccessResponse(
                        MapCertificate(row, course.PublicId), "Certificate already revoked.");
                if (!Matches(row.RowVersion, request.RowVersion))
                    return Error<LmsCertificateDto>("Certificate changed. Reload and retry.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                row.IsRevoked = true; row.RevokedAt = now;
                row.RevokedByUserId = _user.UserId; row.RevocationReason = request.Reason.Trim();
                row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
                _certificates.Update(row);
                await WriteCertificateAuditAsync(row, "LMS.CertificateRevoked", now);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<LmsCertificateDto>.SuccessResponse(
                    MapCertificate(row, course.PublicId), "Certificate revoked. Original issue remains in history.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        { return Error<LmsCertificateDto>("Certificate changed concurrently.", 409); }
        catch (Exception ex) when (IsCertificateSqlConflict(ex))
        {
            _logger.LogWarning(ex, "LMS certificate revocation SQL conflict tenant {TenantId}", _user.TenantId);
            return Error<LmsCertificateDto>("Certificate revocation conflicts with another request.", 409);
        }
    }

    public async Task<ApiResponse<LmsCertificateDto>> GetCertificateAsync(Guid reference, CancellationToken ct = default)
    {
        if (!Authenticated()) return Error<LmsCertificateDto>("Authentication required.", 403);
        if (reference == Guid.Empty) return Error<LmsCertificateDto>("Invalid certificate reference.");
        if (_certificates == null) return Error<LmsCertificateDto>("Certificate repository unavailable.", 503);
        var tenant = _user.TenantId;
        var row = await (from certificate in _certificates.GetQueryable().AsNoTracking()
            join course in _courses.GetQueryable().AsNoTracking() on certificate.CourseId equals course.Id
            where certificate.TenantId == tenant && course.TenantId == tenant &&
                  certificate.PublicId == reference && !certificate.IsDeleted && !course.IsDeleted
            select new { Certificate = certificate, Course = course }).FirstOrDefaultAsync(ct);
        if (row == null || !await CanReadCertificateAsync(row.Certificate, row.Course, ct))
            return Error<LmsCertificateDto>("Certificate not found.", 404);
        return ApiResponse<LmsCertificateDto>.SuccessResponse(
            MapCertificate(row.Certificate, row.Course.PublicId));
    }

    public async Task<ApiResponse<PagedResult<LmsCertificateDto>>> GetMyCertificatesAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        if (!Authenticated() || !_user.IsInRole("Student"))
            return Error<PagedResult<LmsCertificateDto>>("Student permission required.", 403);
        if (page < 1 || pageSize is < 1 or > 25 || ((long)page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<LmsCertificateDto>>("Invalid pagination.");
        if (_certificates == null) return Error<PagedResult<LmsCertificateDto>>("Certificate repository unavailable.", 503);
        var tenant = _user.TenantId;
        var studentId = await _students.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.UserId == _user.UserId && !x.IsDeleted)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (studentId == 0)
            return ApiResponse<PagedResult<LmsCertificateDto>>.SuccessResponse(
                EmptyPage<LmsCertificateDto>(page, pageSize));
        var query = from certificate in _certificates.GetQueryable().AsNoTracking()
                    join course in _courses.GetQueryable().AsNoTracking()
                        on certificate.CourseId equals course.Id
                    where certificate.TenantId == tenant && course.TenantId == tenant &&
                          certificate.StudentId == studentId && !certificate.IsDeleted && !course.IsDeleted
                    select new { Certificate = certificate, CourseReference = course.PublicId };
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Certificate.IssuedAt)
            .ThenByDescending(x => x.Certificate.Id)
            .Skip((int)(((long)page - 1) * pageSize)).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<LmsCertificateDto>>.SuccessResponse(
            new PagedResult<LmsCertificateDto>
            {
                Page = page, PageSize = pageSize, TotalCount = total,
                Items = rows.Select(x => MapCertificate(x.Certificate, x.CourseReference)).ToList()
            });
    }

    public async Task<ApiResponse<PagedResult<LmsCertificateDto>>> GetCourseCertificatesAsync(
        Guid courseReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanTeach()) return Error<PagedResult<LmsCertificateDto>>("Instructor permission required.", 403);
        if (courseReference == Guid.Empty || page < 1 || pageSize is < 1 or > 25 ||
            ((long)page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<LmsCertificateDto>>("Invalid course or pagination.");
        if (_certificates == null) return Error<PagedResult<LmsCertificateDto>>("Certificate repository unavailable.", 503);
        var course = await EditableCourseAsync(courseReference, ct);
        if (course == null) return Error<PagedResult<LmsCertificateDto>>("Course not found.", 404);
        var tenant = _user.TenantId;
        var query = _certificates.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.CourseId == course.Id && !x.IsDeleted);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.IssuedAt).ThenByDescending(x => x.Id)
            .Skip((int)(((long)page - 1) * pageSize)).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<LmsCertificateDto>>.SuccessResponse(
            new PagedResult<LmsCertificateDto>
            {
                Page = page, PageSize = pageSize, TotalCount = total,
                Items = rows.Select(x => MapCertificate(x, course.PublicId)).ToList()
            });
    }

    /// <summary>Bounded tenant-scoped certificate event history; never returns raw PII or audit payloads.</summary>
    public async Task<ApiResponse<IReadOnlyList<LmsCertificateAuditDto>>> GetCertificateAuditAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!CanTeach()) return Error<IReadOnlyList<LmsCertificateAuditDto>>("Instructor permission required.", 403);
        if (reference == Guid.Empty) return Error<IReadOnlyList<LmsCertificateAuditDto>>("Invalid certificate reference.");
        if (_certificates == null || _certificateAudits == null)
            return Error<IReadOnlyList<LmsCertificateAuditDto>>("Certificate audit unavailable.", 503);
        var tenant = _user.TenantId;
        var record = await (from certificate in _certificates.GetQueryable().AsNoTracking()
            join course in _courses.GetQueryable().AsNoTracking() on certificate.CourseId equals course.Id
            where certificate.TenantId == tenant && course.TenantId == tenant &&
                  certificate.PublicId == reference && !certificate.IsDeleted && !course.IsDeleted
            select new { CertificateId = certificate.Id, Course = course }).FirstOrDefaultAsync(ct);
        if (record == null || !await CanEditAsync(record.Course, ct))
            return Error<IReadOnlyList<LmsCertificateAuditDto>>("Certificate not found.", 404);
        IReadOnlyList<LmsCertificateAuditDto> events = await _certificateAudits.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.EntityName == "LmsCompletionCertificate" &&
                x.EntityId == record.CertificateId &&
                (x.Action == "LMS.CertificateIssued" || x.Action == "LMS.CertificateReissued" ||
                 x.Action == "LMS.CertificateRevoked"))
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(50)
            .Select(x => new LmsCertificateAuditDto
            {
                Action = x.Action, OccurredAt = x.OccurredAt, ActorUserId = x.UserId
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<LmsCertificateAuditDto>>.SuccessResponse(events);
    }

    private async Task WriteCertificateAuditAsync(LmsCompletionCertificate certificate, string action, DateTime now)
    {
        // Must share the issuance/revocation SQL transaction. Never record learner identity,
        // raw evidence, revocation reason, or sensitive request payload in the audit log.
        await _certificateAudits!.AddAsync(new EduOS.Core.Entities.System.AuditLog
        {
            TenantId = _user.TenantId, UserId = _user.UserId,
            EntityName = "LmsCompletionCertificate", EntityId = certificate.Id,
            Action = action, OccurredAt = now, IsSuccess = true
        });
    }

    // This is intentionally authenticated, tenant-scoped verification. Public third-party
    // verification requires a separate privacy/consent and rate-limiting acceptance gate.
    public async Task<ApiResponse<LmsCertificateVerificationDto>> VerifyCertificateAsync(
        Guid reference, CancellationToken ct = default)
    {
        var result = await GetCertificateAsync(reference, ct);
        if (!result.Success || result.Data == null)
            return Error<LmsCertificateVerificationDto>(result.Message, result.StatusCode);
        var row = result.Data;
        return ApiResponse<LmsCertificateVerificationDto>.SuccessResponse(
            new LmsCertificateVerificationDto
            {
                Reference = row.Reference, IsValid = !row.IsRevoked, IsRevoked = row.IsRevoked,
                CourseTitle = row.CourseTitle, CompletedAt = row.CompletedAt,
                IssuedAt = row.IssuedAt, Revision = row.Revision
            });
    }

    private async Task<bool> CanReadCertificateAsync(
        LmsCompletionCertificate certificate, Course course, CancellationToken ct)
    {
        if (await CanEditAsync(course, ct)) return true;
        if (!_user.IsInRole("Student")) return false;
        return await _students.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == _user.TenantId && x.Id == certificate.StudentId &&
            x.UserId == _user.UserId && !x.IsDeleted, ct);
    }

    internal static LmsCertificateDto MapCertificate(LmsCompletionCertificate row, Guid courseReference) => new()
    {
        Reference = row.PublicId, CourseEnrollmentId = row.CourseEnrollmentId,
        CourseReference = courseReference, CourseTitle = row.CourseTitleSnapshot,
        CourseCode = row.CourseCodeSnapshot, StudentName = row.StudentNameSnapshot,
        StudentCode = row.StudentCodeSnapshot, CompletedAt = row.CompletedAtSnapshot,
        RequiredLessons = row.RequiredLessonsSnapshot, CompletedLessons = row.CompletedLessonsSnapshot,
        RequiredAssignments = row.RequiredAssignmentsSnapshot, GradedAssignments = row.GradedAssignmentsSnapshot,
        RequiredQuizzes = row.RequiredQuizzesSnapshot, PassedQuizzes = row.PassedQuizzesSnapshot,
        Revision = row.Revision, IssuedAt = row.IssuedAt, IsRevoked = row.IsRevoked,
        RevokedAt = row.RevokedAt, RevocationReason = row.RevocationReason,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };
}
