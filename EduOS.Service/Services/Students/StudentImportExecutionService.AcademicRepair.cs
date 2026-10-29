using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentImportExecutionService
{
    private const string AcademicRepairScope = "StudentImportAcademicRepair";
    private sealed record MissingAcademicRegistration(
        int SourceRow, long EnrollmentId, long BatchId, long OfferingId, decimal CreditHours);
    private sealed record AcademicRepairPlan(
        StudentImportAcademicRepairPlanDto Report,
        List<MissingAcademicRegistration> Missing);

    /// <summary>
    /// Tenant-admin-only preview. Historical/completed enrollments and published
    /// results are deliberately excluded from automated correction.
    /// </summary>
    public async Task<ApiResponse<StudentImportAcademicRepairPlanDto>> PreviewAcademicRepairAsync(
        Guid importReference, CancellationToken cancellationToken = default)
    {
        if (!CanManage())
            return ApiResponse<StudentImportAcademicRepairPlanDto>.ErrorResponse(
                "Student import access denied.", 403);
        if (importReference == Guid.Empty)
            return ApiResponse<StudentImportAcademicRepairPlanDto>.ErrorResponse(
                "Invalid import reference.", 400);
        var plan = await BuildAcademicRepairPlanAsync(importReference, cancellationToken);
        return plan.Success && plan.Data != null
            ? ApiResponse<StudentImportAcademicRepairPlanDto>.SuccessResponse(plan.Data.Report)
            : ApiResponse<StudentImportAcademicRepairPlanDto>.ErrorResponse(plan.Message, plan.StatusCode);
    }

    /// <summary>
    /// Reconciles only missing mandatory registrations for unchanged, single,
    /// active enrollments from one committed import window. The request is
    /// idempotent, consented to by an explicit administrator action, and must
    /// match a fresh, PII-free preview. Never adjusts marks or result history.
    /// </summary>
    public async Task<ApiResponse<StudentImportAcademicRepairResultDto>> ApplyAcademicRepairAsync(
        Guid importReference, Guid clientRequestId, string expectedPlanHash,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage())
            return ApiResponse<StudentImportAcademicRepairResultDto>.ErrorResponse(
                "Student import access denied.", 403);
        if (importReference == Guid.Empty || clientRequestId == Guid.Empty ||
            expectedPlanHash?.Length != 64 ||
            !expectedPlanHash.All(Uri.IsHexDigit))
            return ApiResponse<StudentImportAcademicRepairResultDto>.ErrorResponse(
                "A valid import, request ID and preview fingerprint are required.", 400);
        expectedPlanHash = expectedPlanHash.ToUpperInvariant();
        var tenant = _user.TenantId;
        var knownLogId = await _logs.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                x.ImportType == "Student" && x.PublicId == importReference)
            .Select(x => (long?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (!knownLogId.HasValue)
            return ApiResponse<StudentImportAcademicRepairResultDto>.ErrorResponse(
                "Import batch not found.", 404);
        // A committed response can be replayed even if the newly repaired
        // registration has made the original preflight fingerprint obsolete.
        var committed = await _keys.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Scope == AcademicRepairScope &&
            x.RequestId == clientRequestId, cancellationToken);
        if (committed != null)
        {
            if (!committed.CompletedAt.HasValue || committed.RequestHash != expectedPlanHash ||
                committed.TargetId != knownLogId ||
                !int.TryParse(committed.ResultJson, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var priorCount))
                return ApiResponse<StudentImportAcademicRepairResultDto>.ErrorResponse(
                    "Repair request ID was already used for another operation.", 409);
            return ApiResponse<StudentImportAcademicRepairResultDto>.SuccessResponse(
                new StudentImportAcademicRepairResultDto
                {
                    ImportReference = importReference, CreatedRegistrations = priorCount, Replayed = true
                }, "Previously committed academic repair returned.");
        }
        var initial = await BuildAcademicRepairPlanAsync(importReference, cancellationToken);
        if (!initial.Success || initial.Data == null)
            return ApiResponse<StudentImportAcademicRepairResultDto>.ErrorResponse(
                initial.Message, initial.StatusCode);
        var preview = initial.Data;
        if (preview.Report.PlanHash != expectedPlanHash || !preview.Report.CanApply)
            return ApiResponse<StudentImportAcademicRepairResultDto>.ErrorResponse(
                "Academic repair is no longer applicable. Re-run the preview.", 409);

        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var log = await _logs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.ImportType == "Student" &&
                    x.PublicId == importReference, token);
                if (log == null)
                    throw new ImportConflictException("Import batch is no longer available.");

                var previous = await _keys.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Scope == AcademicRepairScope &&
                    x.RequestId == clientRequestId, token);
                if (previous != null)
                {
                    if (!previous.CompletedAt.HasValue || previous.RequestHash != expectedPlanHash ||
                        previous.TargetId != log.Id ||
                        !int.TryParse(previous.ResultJson, NumberStyles.None,
                            CultureInfo.InvariantCulture, out var previousCount))
                        throw new ImportConflictException(
                            "Repair request ID was already used for another operation.");
                    return ApiResponse<StudentImportAcademicRepairResultDto>.SuccessResponse(
                        new StudentImportAcademicRepairResultDto
                        {
                            ImportReference = importReference,
                            CreatedRegistrations = previousCount, Replayed = true
                        }, "Previously committed academic repair returned.");
                }

                // Same deterministic batch rowversion write locks as ordinary
                // import/academic enrollment, before testing offering capacities.
                var batchIds = preview.Missing.Select(x => x.BatchId).Distinct().OrderBy(x => x).ToArray();
                var batches = await _batches.GetQueryable().Where(x => x.TenantId == tenant &&
                    !x.IsDeleted && x.IsActive && batchIds.Contains(x.Id))
                    .OrderBy(x => x.Id).ToListAsync(token);
                if (batches.Count != batchIds.Length)
                    throw new ImportConflictException("Academic batches changed. Re-run the preview.");
                var now = DateTime.UtcNow;
                foreach (var batch in batches)
                {
                    batch.UpdatedAt = now;
                    batch.UpdatedBy = _user.UserId;
                    _batches.Update(batch);
                }
                await _uow.SaveChangesAsync(token);

                // Re-read every source receipt, student/enrollment, offering,
                // capacity, result and registration AFTER acquiring write locks.
                var fresh = await BuildAcademicRepairPlanAsync(importReference, token);
                if (!fresh.Success || fresh.Data == null || !fresh.Data.Report.CanApply ||
                    fresh.Data.Report.PlanHash != expectedPlanHash)
                    throw new ImportConflictException(
                        "Import, academic records or offering capacity changed. Re-run the preview.");

                foreach (var row in fresh.Data.Missing)
                    await _registrations.AddAsync(new StudentSubjectRegistration
                    {
                        TenantId = tenant, ClientRequestId = Guid.NewGuid(),
                        StudentEnrollmentId = row.EnrollmentId,
                        SubjectOfferingId = row.OfferingId,
                        State = SubjectRegistrationState.Approved,
                        RegisteredAt = now, CreditHoursSnapshot = row.CreditHours,
                        ApprovedByUserId = _user.UserId, ApprovedAt = now,
                        Remarks = "Verified historical student-import academic reconciliation.",
                        CreatedAt = now, CreatedBy = _user.UserId
                    });
                await _keys.AddAsync(new IdempotencyRecord
                {
                    TenantId = tenant, Scope = AcademicRepairScope,
                    RequestId = clientRequestId, RequestHash = expectedPlanHash,
                    TargetId = log.Id, ResultJson = fresh.Data.Missing.Count.ToString(CultureInfo.InvariantCulture),
                    CompletedByUserId = _user.UserId, CompletedAt = now, ExpiresAt = now.AddYears(50),
                    CreatedAt = now, CreatedBy = _user.UserId
                });
                await _uow.SaveChangesAsync(token);
                return ApiResponse<StudentImportAcademicRepairResultDto>.SuccessResponse(
                    new StudentImportAcademicRepairResultDto
                    {
                        ImportReference = importReference,
                        CreatedRegistrations = fresh.Data.Missing.Count
                    }, "Missing required registrations were reconciled.");
            }, IsolationLevel.ReadCommitted, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ImportConflictException ex)
        {
            return ApiResponse<StudentImportAcademicRepairResultDto>.ErrorResponse(ex.Message, 409);
        }
        catch (Exception ex) when (IsRetryableSqlAllocationConflict(ex) || ex is DbUpdateException)
        {
            _logger.LogWarning("Student import academic repair concurrency conflict for tenant {TenantId}", tenant);
            return ApiResponse<StudentImportAcademicRepairResultDto>.ErrorResponse(
                "Academic records changed concurrently. Refresh the preview and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student import academic repair failed for tenant {TenantId}", tenant);
            return ApiResponse<StudentImportAcademicRepairResultDto>.ErrorResponse(
                "Academic repair failed without a confirmed commit.", 500);
        }
    }

    private async Task<ApiResponse<AcademicRepairPlan>> BuildAcademicRepairPlanAsync(
        Guid importReference, CancellationToken token)
    {
        var integrity = await GetIntegrityAsync(importReference, token);
        if (!integrity.Success || integrity.Data == null)
            return ApiResponse<AcademicRepairPlan>.ErrorResponse(integrity.Message, integrity.StatusCode);
        var tenant = _user.TenantId;
        var report = new StudentImportAcademicRepairPlanDto
        {
            ImportReference = importReference,
            ImportedRowsChecked = integrity.Data.CheckedRows,
            Blockers = integrity.Data.Issues.Where(x => x.Code != "MissingRequiredRegistration").ToList()
        };
        var missing = new List<MissingAcademicRegistration>();
        if (report.Blockers.Count > 0)
            return ApiResponse<AcademicRepairPlan>.SuccessResponse(new AcademicRepairPlan(report, missing));

        var logId = await _logs.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                x.ImportType == "Student" && x.PublicId == importReference)
            .Select(x => x.Id).SingleAsync(token);
        var items = await _items.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.ImportLogId == logId)
            .OrderBy(x => x.RowNumber)
            .Select(x => new { x.RowNumber, x.BusinessKey }).Take(101).ToListAsync(token);
        if (items.Count != integrity.Data.ExpectedRows || items.Any(x => string.IsNullOrWhiteSpace(x.BusinessKey)))
            return ApiResponse<AcademicRepairPlan>.ErrorResponse(
                "Import source receipts require review.", 409);
        var sourceIds = items.Select(x => SourceId(x.BusinessKey!)).ToArray();
        var receipts = await _keys.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.Scope == SourceScope &&
                sourceIds.Contains(x.RequestId) && x.TargetId.HasValue && x.CompletedAt.HasValue)
            .Select(x => new { x.RequestId, StudentId = x.TargetId!.Value }).ToListAsync(token);
        if (receipts.Count != items.Count)
            return ApiResponse<AcademicRepairPlan>.ErrorResponse("Import source identities require review.", 409);
        var bySource = receipts.ToDictionary(x => x.RequestId, x => x.StudentId);
        var studentIds = receipts.Select(x => x.StudentId).Distinct().ToArray();
        var rowsByStudent = items.ToDictionary(x => bySource[SourceId(x.BusinessKey!)], x => x.RowNumber);
        // A promoted, transferred, readmitted, archived or completed student
        // must NEVER receive a retroactive registration on a later enrollment.
        var enrollments = await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && studentIds.Contains(x.StudentId))
            .Select(x => new { x.Id, x.StudentId, x.AcademicCurriculumId, x.AcademicLevelId,
                x.AcademicBatchId, x.AcademicYearId, x.IsCurrent, x.State })
            .Take(201).ToListAsync(token);
        foreach (var studentId in studentIds)
        {
            var history = enrollments.Where(x => x.StudentId == studentId).ToArray();
            if (history.Length != 1 || !history[0].IsCurrent || history[0].State != EnrollmentState.Active)
                report.Blockers.Add(new StudentImportIntegrityIssueDto
                {
                    RowNumber = rowsByStudent[studentId], Code = "ChangedEnrollmentHistory",
                    Message = "Only an unchanged single active enrollment can be repaired automatically."
                });
        }
        if (report.Blockers.Count > 0)
            return ApiResponse<AcademicRepairPlan>.SuccessResponse(new AcademicRepairPlan(report, missing));

        var enrollmentIds = enrollments.Select(x => x.Id).ToArray();
        if (await _db.StudentResultSummaries.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && enrollmentIds.Contains(x.StudentEnrollmentId), token))
        {
            report.Blockers.Add(new StudentImportIntegrityIssueDto
            {
                Code = "PublishedOrCalculatedResult",
                Message = "Enrollment result history exists; manual academic review is required."
            });
            return ApiResponse<AcademicRepairPlan>.SuccessResponse(new AcademicRepairPlan(report, missing));
        }

        var curriculumIds = enrollments.Select(x => x.AcademicCurriculumId).Distinct().ToArray();
        var currentCurricula = await _curricula.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.IsCurrent &&
            curriculumIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(token);
        if (currentCurricula.Count != curriculumIds.Length)
        {
            report.Blockers.Add(new StudentImportIntegrityIssueDto
            {
                Code = "HistoricalCurriculum",
                Message = "The imported curriculum is no longer current; its historic subject rules need manual review."
            });
            return ApiResponse<AcademicRepairPlan>.SuccessResponse(new AcademicRepairPlan(report, missing));
        }
        var levelIds = enrollments.Select(x => x.AcademicLevelId).Distinct().ToArray();
        var subjects = await _curriculumSubjects.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive && !x.IsOptional &&
            curriculumIds.Contains(x.AcademicCurriculumId) && levelIds.Contains(x.AcademicLevelId))
            .Select(x => new { x.Id, x.AcademicCurriculumId, x.AcademicLevelId, x.CreditHours })
            .Take(2001).ToListAsync(token);
        if (subjects.Count > 2000)
            return ApiResponse<AcademicRepairPlan>.ErrorResponse("Required-subject limit exceeded.", 409);
        var subjectIds = subjects.Select(x => x.Id).ToArray();
        var batchIds = enrollments.Select(x => x.AcademicBatchId).Distinct().ToArray();
        var yearIds = enrollments.Select(x => x.AcademicYearId).Distinct().ToArray();
        var offerings = await _offerings.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
            batchIds.Contains(x.AcademicBatchId) && yearIds.Contains(x.AcademicYearId) &&
            subjectIds.Contains(x.CurriculumSubjectId))
            .Select(x => new { x.Id, x.AcademicBatchId, x.AcademicYearId,
                x.CurriculumSubjectId, x.Capacity }).Take(4001).ToListAsync(token);
        if (offerings.Count > 4000)
            return ApiResponse<AcademicRepairPlan>.ErrorResponse("Subject-offering limit exceeded.", 409);
        var offeringIds = offerings.Select(x => x.Id).ToArray();
        var registrations = await _registrations.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && enrollmentIds.Contains(x.StudentEnrollmentId))
            .Select(x => new { x.StudentEnrollmentId, x.SubjectOfferingId }).Take(4001).ToListAsync(token);
        if (registrations.Count > 4000)
            return ApiResponse<AcademicRepairPlan>.ErrorResponse("Subject-registration limit exceeded.", 409);

        foreach (var enrollment in enrollments)
        foreach (var subject in subjects.Where(x =>
            x.AcademicCurriculumId == enrollment.AcademicCurriculumId &&
            x.AcademicLevelId == enrollment.AcademicLevelId))
        {
            var matches = offerings.Where(x => x.AcademicBatchId == enrollment.AcademicBatchId &&
                x.AcademicYearId == enrollment.AcademicYearId &&
                x.CurriculumSubjectId == subject.Id).ToArray();
            if (matches.Length != 1)
            {
                report.Blockers.Add(new StudentImportIntegrityIssueDto
                {
                    RowNumber = rowsByStudent[enrollment.StudentId],
                    Code = "RequiredOfferingChanged",
                    Message = "A required subject offering is missing or ambiguous."
                });
                continue;
            }
            if (!registrations.Any(x => x.StudentEnrollmentId == enrollment.Id &&
                x.SubjectOfferingId == matches[0].Id))
                missing.Add(new MissingAcademicRegistration(rowsByStudent[enrollment.StudentId],
                    enrollment.Id, enrollment.AcademicBatchId, matches[0].Id, subject.CreditHours));
        }
        if (missing.Count > 2000)
            return ApiResponse<AcademicRepairPlan>.ErrorResponse("Repair exceeds the 2,000-registration limit.", 409);
        if (missing.Count != integrity.Data.Issues.Count(x => x.Code == "MissingRequiredRegistration"))
            report.Blockers.Add(new StudentImportIntegrityIssueDto
            {
                Code = "AcademicAuditChanged",
                Message = "Required-subject integrity changed during preview."
            });
        var newByOffering = missing.GroupBy(x => x.OfferingId).ToDictionary(x => x.Key, x => x.Count());
        var occupied = await _registrations.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && offeringIds.Contains(x.SubjectOfferingId) &&
            x.State != SubjectRegistrationState.Rejected && x.State != SubjectRegistrationState.Dropped)
            .GroupBy(x => x.SubjectOfferingId)
            .Select(x => new { OfferingId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.OfferingId, x => x.Count, token);
        foreach (var offering in offerings.Where(x => newByOffering.ContainsKey(x.Id)))
            if (offering.Capacity <= 0 ||
                occupied.GetValueOrDefault(offering.Id) + newByOffering[offering.Id] > offering.Capacity)
                report.Blockers.Add(new StudentImportIntegrityIssueDto
                {
                    Code = "RequiredOfferingFull",
                    Message = "A required subject offering lacks capacity for the repair."
                });

        report.MissingRegistrations = missing.Count;
        var fingerprint = new StringBuilder(tenant.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(logId);
        foreach (var row in missing.OrderBy(x => x.EnrollmentId).ThenBy(x => x.OfferingId))
            fingerprint.Append('|').Append(row.EnrollmentId).Append(':').Append(row.OfferingId)
                .Append(':').Append(row.CreditHours.ToString(CultureInfo.InvariantCulture));
        report.PlanHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));
        return ApiResponse<AcademicRepairPlan>.SuccessResponse(new AcademicRepairPlan(report, missing));
    }
}
