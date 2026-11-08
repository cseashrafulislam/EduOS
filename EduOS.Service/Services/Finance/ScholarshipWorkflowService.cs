using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Documents;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Finance;

/// <summary>Human-reviewed educational assistance; only canonical Finance owns the actual award.</summary>
public sealed class ScholarshipWorkflowService(EduOSDbContext db, ICurrentUserService user,
    IFinanceAdjustmentService finance, IFileUploadService storage)
{
    private bool Scoped => user.IsAuthenticated && user.UserId > 0 && user.TenantId > 0 &&
        db.CurrentTenantId == user.TenantId;
    private bool Manager => Scoped && user.IsTenantAdmin;
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
    private static bool Version(byte[] actual, string? version)
    {
        try { return !string.IsNullOrEmpty(version) && actual.AsSpan().SequenceEqual(Convert.FromBase64String(version)); }
        catch (FormatException) { return false; }
    }
    private IQueryable<ScholarshipApplication> Applications =>
        db.ScholarshipApplications.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);

    private async Task<bool> OwnsEnrollment(long studentId, CancellationToken ct)
    {
        if (await db.Students.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && x.Id == studentId &&
            !x.IsDeleted && x.UserId == user.UserId, ct)) return true;
        return await (from link in db.StudentGuardians.AsNoTracking()
            join guardian in db.Guardians.AsNoTracking()
                on new { link.TenantId, Id = link.GuardianId } equals
                new { guardian.TenantId, guardian.Id }
            where link.TenantId == user.TenantId && guardian.TenantId == user.TenantId &&
                link.StudentId == studentId && guardian.UserId == user.UserId &&
                guardian.IsActive && !link.IsDeleted && !guardian.IsDeleted &&
                link.IsFinancialContact
            select link.Id).AnyAsync(ct);
    }
    private static ScholarshipRow Map(ScholarshipApplication x, Guid enrollmentReference, long campusId) =>
        new()
        {
            Reference = x.PublicId, EnrollmentReference = enrollmentReference, CampusId = campusId,
            DiscountRuleId = x.DiscountRuleId, State = x.State,
            EligibilityStatement = x.EligibilityStatement,
            EffectiveFrom = x.EffectiveFrom, EffectiveTo = x.EffectiveTo,
            SubmittedByUserId = x.SubmittedByUserId, SubmittedAtUtc = x.SubmittedAtUtc,
            ReviewedByUserId = x.ReviewedByUserId, DecisionReason = x.DecisionReason,
            StudentDiscountId = x.StudentDiscountId, HasEvidence = x.EvidenceFileAssetId.HasValue,
            RowVersion = Convert.ToBase64String(x.RowVersion)
        };
    private async Task<ScholarshipRow?> FindAsync(ScholarshipApplication application, CancellationToken ct)
    {
        var enrollment = await db.StudentEnrollments.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == application.StudentEnrollmentId && !x.IsDeleted, ct);
        if (enrollment == null || (!Manager &&
            !await OwnsEnrollment(enrollment.StudentId, ct))) return null;
        var result = Map(application, enrollment.PublicId, enrollment.CampusId);
        if (!Manager)
        {
            // A learner or financial guardian may see the decision, but no internal reviewer narrative.
            result.DecisionReason = null;
        }
        return result;
    }
    public async Task<ApiResponse<ScholarshipRow>> SubmitAsync(
        SubmitScholarshipRequest request, CancellationToken ct = default)
    {
        if (!Scoped) return Fail<ScholarshipRow>("Authentication required.", 403);
        if (request == null || request.RequestId == Guid.Empty ||
            request.EnrollmentReference == Guid.Empty || request.DiscountRuleId <= 0 ||
            request.EffectiveFrom == default || request.EffectiveTo < request.EffectiveFrom ||
            string.IsNullOrWhiteSpace(request.EligibilityStatement) ||
            request.EligibilityStatement.Trim().Length > 700 ||
            request.EvidenceFileReference == Guid.Empty)
            return Fail<ScholarshipRow>("Invalid scholarship application.");
        var enrollment = await db.StudentEnrollments.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == request.EnrollmentReference &&
            x.State == EnrollmentState.Active && x.IsCurrent && !x.IsDeleted, ct);
        if (enrollment == null || (!Manager &&
            !await OwnsEnrollment(enrollment.StudentId, ct)))
            return Fail<ScholarshipRow>("Enrollment not found.", 404);
        var previous = await Applications.AsNoTracking().FirstOrDefaultAsync(x =>
            x.ClientRequestId == request.RequestId, ct);
        if (previous != null)
        {
            var sameEvidence = !request.EvidenceFileReference.HasValue
                ? !previous.EvidenceFileAssetId.HasValue
                : previous.EvidenceFileAssetId.HasValue && await db.FileAssets.AsNoTracking()
                    .AnyAsync(x => x.TenantId == user.TenantId &&
                        x.Id == previous.EvidenceFileAssetId.Value &&
                        x.PublicId == request.EvidenceFileReference.Value, ct);
            var sameRenewal = !request.RenewalOfReference.HasValue
                ? !previous.RenewalOfApplicationId.HasValue
                : previous.RenewalOfApplicationId.HasValue && await Applications.AsNoTracking()
                    .AnyAsync(x => x.Id == previous.RenewalOfApplicationId.Value &&
                        x.PublicId == request.RenewalOfReference.Value, ct);
            return sameEvidence && sameRenewal && previous.SubmittedByUserId == user.UserId &&
                previous.StudentEnrollmentId == enrollment.Id &&
                previous.DiscountRuleId == request.DiscountRuleId &&
                previous.EffectiveFrom == request.EffectiveFrom &&
                previous.EffectiveTo == request.EffectiveTo &&
                previous.EligibilityStatement == request.EligibilityStatement.Trim()
                ? ApiResponse<ScholarshipRow>.SuccessResponse(Map(previous, enrollment.PublicId, enrollment.CampusId))
                : Fail<ScholarshipRow>("Request ID already used.", 409);
        }
        var rule = await db.DiscountRules.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == request.DiscountRuleId &&
            x.IsActive && !x.IsDeleted, ct);
        if (rule == null || (rule.EffectiveFrom.HasValue && request.EffectiveFrom < rule.EffectiveFrom.Value) ||
            (rule.EffectiveTo.HasValue && (!request.EffectiveTo.HasValue ||
                request.EffectiveTo.Value > rule.EffectiveTo.Value)))
            return Fail<ScholarshipRow>("Aid rule is unavailable for the requested dates.", 409);
        long? fileId = null;
        if (request.EvidenceFileReference.HasValue)
        {
            fileId = await db.FileAssets.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                x.PublicId == request.EvidenceFileReference.Value && !x.IsDeleted &&
                x.UploadedByUserId == user.UserId && x.IsVerifiedSafe &&
                x.MalwareScanStateCode == "Clean" && x.StorageProvider == "LocalPrivate" &&
                x.Visibility == FileVisibility.Private).Select(x => (long?)x.Id).FirstOrDefaultAsync(ct);
            if (!fileId.HasValue) return Fail<ScholarshipRow>("Private evidence not verified.", 404);
        }
        long? renewal = null;
        if (request.RenewalOfReference.HasValue)
        {
            var old = await Applications.AsNoTracking().FirstOrDefaultAsync(x =>
                x.PublicId == request.RenewalOfReference.Value, ct);
            if (old == null || old.StudentEnrollmentId != enrollment.Id ||
                old.State is not ("Awarded" or "Cancelled") ||
                (old.EffectiveTo.HasValue && request.EffectiveFrom <= old.EffectiveTo))
                return Fail<ScholarshipRow>("Renewal must follow an existing closed aid period.", 409);
            renewal = old.Id;
        }
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (await Applications.AnyAsync(x => x.StudentEnrollmentId == enrollment.Id &&
                    x.DiscountRuleId == request.DiscountRuleId &&
                    (x.State == "Submitted" || x.State == "Approved" || x.State == "Awarded") &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.EffectiveFrom) &&
                    (!request.EffectiveTo.HasValue || x.EffectiveFrom <= request.EffectiveTo), token))
                    return Fail<ScholarshipRow>("An active or pending aid period already overlaps.", 409);
                var now = DateTime.UtcNow;
                var row = new ScholarshipApplication
                {
                    TenantId = user.TenantId, ClientRequestId = request.RequestId,
                    StudentEnrollmentId = enrollment.Id, DiscountRuleId = request.DiscountRuleId,
                    EvidenceFileAssetId = fileId, RenewalOfApplicationId = renewal,
                    EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo,
                    EligibilityStatement = request.EligibilityStatement.Trim(),
                    SubmittedByUserId = user.UserId, SubmittedAtUtc = now, CreatedBy = user.UserId
                };
                db.ScholarshipApplications.Add(row);
                await db.SaveChangesAsync(token);
                return ApiResponse<ScholarshipRow>.SuccessResponse(Map(row, enrollment.PublicId, enrollment.CampusId));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException) { return Fail<ScholarshipRow>("Concurrent aid application; reload.", 409); }
    }

    public async Task<ApiResponse<ScholarshipRow>> DecideAsync(Guid reference,
        ScholarshipDecisionRequest request, CancellationToken ct = default)
    {
        if (!Manager) return Fail<ScholarshipRow>("Independent administrator approval required.", 403);
        if (request == null || request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return Fail<ScholarshipRow>("A decision reason is required.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await Applications.FirstOrDefaultAsync(x => x.PublicId == reference, token);
                if (row == null) return Fail<ScholarshipRow>("Aid application not found.", 404);
                if (row.SubmittedByUserId == user.UserId)
                    return Fail<ScholarshipRow>("The applicant cannot approve their own request.", 403);
                var prior = await db.ScholarshipApplicationEvents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId, token);
                var readback = await FindAsync(row, token);
                if (prior != null)
                    return prior.ScholarshipApplicationId == row.Id &&
                        prior.ActorUserId == user.UserId && prior.Action ==
                        (request.Approve ? "Approved" : "Rejected") &&
                        prior.Reason == request.Reason.Trim()
                        ? ApiResponse<ScholarshipRow>.SuccessResponse(readback!)
                        : Fail<ScholarshipRow>("Decision key already used.", 409);
                if (row.State != "Submitted" || !Version(row.RowVersion, request.RowVersion))
                    return Fail<ScholarshipRow>("Application changed; reload.", 409);
                row.State = request.Approve ? "Approved" : "Rejected";
                row.ReviewedByUserId = user.UserId; row.ReviewedAtUtc = DateTime.UtcNow;
                row.DecisionReason = request.Reason.Trim(); row.UpdatedBy = user.UserId;
                row.UpdatedAt = DateTime.UtcNow;
                db.ScholarshipApplicationEvents.Add(new ScholarshipApplicationEvent
                {
                    TenantId = user.TenantId, ScholarshipApplicationId = row.Id,
                    ClientRequestId = request.RequestId, Action = row.State,
                    Reason = row.DecisionReason, ActorUserId = user.UserId, CreatedBy = user.UserId
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<ScholarshipRow>.SuccessResponse((await FindAsync(row, token))!);
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<ScholarshipRow>("Concurrent decision; reload.", 409); }
        catch (DbUpdateException) { return Fail<ScholarshipRow>("Conflicting decision; reload.", 409); }
    }

    /// <summary>Uses the existing idempotent future-invoice discount writer; interrupted
    /// linkage is safely reconcilable by replaying with the application public ID.</summary>
    public async Task<ApiResponse<ScholarshipRow>> AwardAsync(Guid reference, CancellationToken ct = default)
    {
        if (!Manager) return Fail<ScholarshipRow>("Finance administrator required.", 403);
        var row = await Applications.AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
        if (row == null) return Fail<ScholarshipRow>("Application not found.", 404);
        if (row.State == "Awarded")
        {
            var replay = await FindAsync(row, ct);
            return replay == null ? Fail<ScholarshipRow>("Application unavailable.", 404) :
                ApiResponse<ScholarshipRow>.SuccessResponse(replay);
        }
        if (row.State != "Approved" || row.ReviewedByUserId == null ||
            row.ReviewedByUserId == row.SubmittedByUserId)
            return Fail<ScholarshipRow>("Independent approved aid is required.", 409);
        var enrollment = await db.StudentEnrollments.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.Id == row.StudentEnrollmentId && !x.IsDeleted)
            .Select(x => new { x.PublicId }).FirstOrDefaultAsync(ct);
        if (enrollment == null) return Fail<ScholarshipRow>("Enrollment missing.", 409);
        var assignment = await finance.AssignDiscountAsync(new AssignStudentDiscountRequestDto
        {
            ClientRequestId = row.PublicId, StudentEnrollmentReference = enrollment.PublicId,
            DiscountRuleId = row.DiscountRuleId, EffectiveFrom = row.EffectiveFrom,
            EffectiveTo = row.EffectiveTo, Reason = "Approved scholarship " + row.PublicId
        }, ct);
        if (!assignment.Success)
            return Fail<ScholarshipRow>(assignment.Message, assignment.StatusCode);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var current = await Applications.FirstOrDefaultAsync(x => x.Id == row.Id, token);
                if (current == null || current.State is not ("Approved" or "Awarded"))
                    return Fail<ScholarshipRow>("Application changed; finance reconciliation required.", 409);
                if (current.StudentDiscountId.HasValue &&
                    current.StudentDiscountId != assignment.Data!.Id)
                    return Fail<ScholarshipRow>("Award lineage mismatch.", 409);
                current.StudentDiscountId = assignment.Data!.Id;
                if (current.State == "Approved")
                {
                    current.State = "Awarded"; current.AwardedAtUtc = DateTime.UtcNow;
                    current.UpdatedAt = DateTime.UtcNow; current.UpdatedBy = user.UserId;
                    db.ScholarshipApplicationEvents.Add(new ScholarshipApplicationEvent
                    {
                        TenantId = user.TenantId, ScholarshipApplicationId = current.Id,
                        ClientRequestId = current.PublicId, Action = "Awarded",
                        ActorUserId = user.UserId, CreatedBy = user.UserId
                    });
                    await db.SaveChangesAsync(token);
                }
                return ApiResponse<ScholarshipRow>.SuccessResponse((await FindAsync(current, token))!);
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException) { return Fail<ScholarshipRow>("Award linked concurrently; reload.", 409); }
    }

    public async Task<ApiResponse<ScholarshipRow>> CancelAsync(Guid reference,
        ScholarshipCommandRequest request, CancellationToken ct = default)
    {
        if (!Manager) return Fail<ScholarshipRow>("Finance administrator required.", 403);
        if (request == null || request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return Fail<ScholarshipRow>("A documented cancellation reason is required.");
        var row = await Applications.AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
        if (row == null) return Fail<ScholarshipRow>("Application not found.", 404);
        if (row.State == "Cancelled")
        {
            var replay = await FindAsync(row, ct);
            return ApiResponse<ScholarshipRow>.SuccessResponse(replay!);
        }
        if (row.State is not ("Submitted" or "Awarded") ||
            !Version(row.RowVersion, request.RowVersion))
            return Fail<ScholarshipRow>("Only a pending or linked award may be cancelled.", 409);
        if (row.State == "Awarded")
        {
            if (!row.StudentDiscountId.HasValue)
                return Fail<ScholarshipRow>("Financial lineage missing; reconcile before cancellation.", 409);
            var assigned = await db.StudentDiscounts.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == row.StudentDiscountId &&
                x.ClientRequestId == row.PublicId, ct);
            if (assigned == null) return Fail<ScholarshipRow>("Financial assignment mismatch.", 409);
            // A timeout after the canonical deactivation committed must be retryable.
            if (assigned.IsActive)
            {
                var stop = await finance.DeactivateStudentDiscountAsync(assigned.Id,
                    new DeactivateStudentDiscountRequestDto
                    {
                        RowVersion = Convert.ToBase64String(assigned.RowVersion)
                    }, ct);
                if (!stop.Success) return Fail<ScholarshipRow>(stop.Message, stop.StatusCode);
            }
        }
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var current = await Applications.FirstOrDefaultAsync(x => x.Id == row.Id, token);
                if (current == null || current.State != row.State ||
                    !Version(current.RowVersion, request.RowVersion))
                    return Fail<ScholarshipRow>("Application changed; finance reconciliation required.", 409);
                var key = await db.ScholarshipApplicationEvents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId, token);
                if (key != null) return Fail<ScholarshipRow>("Cancellation key already used.", 409);
                current.State = "Cancelled"; current.UpdatedAt = DateTime.UtcNow;
                current.UpdatedBy = user.UserId;
                db.ScholarshipApplicationEvents.Add(new ScholarshipApplicationEvent
                {
                    TenantId = user.TenantId, ScholarshipApplicationId = current.Id,
                    ClientRequestId = request.RequestId, Action = "Cancelled",
                    Reason = request.Reason.Trim(), ActorUserId = user.UserId, CreatedBy = user.UserId
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<ScholarshipRow>.SuccessResponse((await FindAsync(current, token))!);
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException) { return Fail<ScholarshipRow>("Concurrent cancellation; reload.", 409); }
    }

    public async Task<ApiResponse<ScholarshipPage>> SearchAsync(int page = 1,
        int pageSize = 30, string? state = null, CancellationToken ct = default)
    {
        if (!Scoped) return Fail<ScholarshipPage>("Authentication required.", 403);
        if (page <= 0 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize ||
            (!string.IsNullOrEmpty(state) && state is not
                ("Submitted" or "Approved" or "Rejected" or "Awarded" or "Cancelled")))
            return Fail<ScholarshipPage>("Invalid pagination or state.");
        var isManager = Manager;
        var query =
            from row in Applications.AsNoTracking()
            join enrollment in db.StudentEnrollments.AsNoTracking()
                on new { row.TenantId, Id = row.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            where !enrollment.IsDeleted && (isManager ||
                db.Students.Any(x => x.TenantId == user.TenantId &&
                    x.Id == enrollment.StudentId && x.UserId == user.UserId && !x.IsDeleted) ||
                (from link in db.StudentGuardians
                 join guardian in db.Guardians on new { link.TenantId, Id = link.GuardianId }
                     equals new { guardian.TenantId, guardian.Id }
                 where link.TenantId == user.TenantId && link.StudentId == enrollment.StudentId &&
                     !link.IsDeleted && !guardian.IsDeleted && guardian.IsActive &&
                     guardian.UserId == user.UserId && link.IsFinancialContact
                 select link.Id).Any())
            select new { row, enrollment.PublicId, enrollment.CampusId };
        if (!string.IsNullOrEmpty(state)) query = query.Where(x => x.row.State == state);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.row.SubmittedAtUtc)
            .ThenByDescending(x => x.row.Id).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        var result = rows.Select(x => Map(x.row, x.PublicId, x.CampusId)).ToArray();
        if (!Manager)
            foreach (var entry in result) entry.DecisionReason = null;
        return ApiResponse<ScholarshipPage>.SuccessResponse(new ScholarshipPage
        {
            Page = page, PageSize = pageSize, TotalCount = count, Items = result
        });
    }

    public async Task<ApiResponse<ScholarshipRow>> GetAsync(Guid reference, CancellationToken ct = default)
    {
        if (!Scoped) return Fail<ScholarshipRow>("Authentication required.", 403);
        var row = await Applications.AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
        if (row == null) return Fail<ScholarshipRow>("Application not found.", 404);
        var mapped = await FindAsync(row, ct);
        return mapped == null ? Fail<ScholarshipRow>("Application not found.", 404) :
            ApiResponse<ScholarshipRow>.SuccessResponse(mapped);
    }

    /// <summary>Independent reviewers only. Evidence is private, scanned, tenant-keyed and hash-verified;
    /// every successful read is purpose-limited and recorded without saving the document contents.</summary>
    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadEvidenceAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!Manager) return Fail<DocumentFileDownloadDto>("Independent administrator review required.", 403);
        var application = await Applications.AsNoTracking().FirstOrDefaultAsync(x =>
            x.PublicId == reference && x.EvidenceFileAssetId.HasValue, ct);
        if (application == null || application.SubmittedByUserId == user.UserId)
            return Fail<DocumentFileDownloadDto>("Evidence unavailable for independent review.", 404);
        var enrollment = await db.StudentEnrollments.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.Id == application.StudentEnrollmentId && !x.IsDeleted)
            .Select(x => new { x.StudentId }).FirstOrDefaultAsync(ct);
        if (enrollment == null) return Fail<DocumentFileDownloadDto>("Enrollment unavailable.", 404);
        var file = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == application.EvidenceFileAssetId &&
            x.UploadedByUserId == application.SubmittedByUserId && !x.IsDeleted &&
            x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
            x.StorageProvider == "LocalPrivate" && x.Visibility == FileVisibility.Private, ct);
        if (file == null || file.SizeBytes is < 1 or > 5242880 ||
            !DocumentFileService.IsTenantDocumentKey(file.StorageKey, user.TenantId) ||
            string.IsNullOrWhiteSpace(file.Sha256))
            return Fail<DocumentFileDownloadDto>("Verified evidence unavailable.", 404);
        var otherApplication = await Applications.AsNoTracking().AnyAsync(x =>
            x.Id != application.Id && x.EvidenceFileAssetId == file.Id &&
            (x.StudentEnrollmentId != application.StudentEnrollmentId ||
                x.SubmittedByUserId != application.SubmittedByUserId), ct);
        var foreignAttachment = await db.Documents.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && x.FileAssetId == file.Id && !x.IsDeleted &&
            (x.EntityType != "Student" || x.EntityId != enrollment.StudentId), ct);
        if (otherApplication || foreignAttachment)
            return Fail<DocumentFileDownloadDto>("Evidence ownership conflict.", 409);
        FileDownloadResult? stored;
        try { stored = await storage.GetPrivateFileAsync(file.StorageKey); }
        catch (IOException) { return Fail<DocumentFileDownloadDto>("Private evidence temporarily unavailable.", 503); }
        catch (UnauthorizedAccessException) { return Fail<DocumentFileDownloadDto>("Private evidence temporarily unavailable.", 503); }
        if (stored == null || stored.Content.LongLength != file.SizeBytes ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(stored.Content)),
                file.Sha256, StringComparison.OrdinalIgnoreCase))
            return Fail<DocumentFileDownloadDto>("Evidence integrity check failed.", 409);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId, UserId = user.UserId,
            Action = "Scholarship.EvidenceAccessed", EntityName = "ScholarshipApplication",
            EntityId = application.Id, NewValue = application.PublicId.ToString("D"),
            OccurredAt = DateTime.UtcNow
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Fail<DocumentFileDownloadDto>("Evidence access could not be audited.", 503); }
        return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
        {
            Content = stored.Content, ContentType = file.ContentType,
            FileName = Path.GetFileName(file.OriginalFileName.Replace('\\', '/'))
        });
    }

    /// <summary>Read-only lineage check; invoice-level discount attribution is deliberately
    /// not inferred from aggregate invoice discounts when multiple award rules apply.</summary>
    public async Task<ApiResponse<ScholarshipReconciliationPage>> ReconciliationAsync(
        int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        if (!Manager) return Fail<ScholarshipReconciliationPage>("Finance administrator required.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return Fail<ScholarshipReconciliationPage>("Invalid page.");
        var query = from application in Applications.AsNoTracking()
            join enrollment in db.StudentEnrollments.AsNoTracking()
                on new { application.TenantId, Id = application.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            where !enrollment.IsDeleted
            select new { application, enrollment.PublicId };
        var total = await query.CountAsync(ct);
        var pageRows = await query.OrderByDescending(x => x.application.SubmittedAtUtc)
            .ThenByDescending(x => x.application.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.application.PublicId, x.application.State, x.application.StudentDiscountId,
                x.application.StudentEnrollmentId, x.application.DiscountRuleId,
                x.application.EffectiveFrom, x.application.EffectiveTo,
                EnrollmentReference = x.PublicId
            }).ToListAsync(ct);
        var keys = pageRows.Select(x => (Guid?)x.PublicId).ToArray();
        var canonical = await db.StudentDiscounts.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && x.ClientRequestId.HasValue &&
            keys.Contains(x.ClientRequestId)).Select(x => new
            {
                x.Id, x.ClientRequestId, x.StudentEnrollmentId, x.DiscountRuleId,
                x.EffectiveFrom, x.EffectiveTo, x.IsActive
            }).ToListAsync(ct);
        var rows = pageRows.Select(row =>
        {
            var matches = canonical.Where(x => x.ClientRequestId == row.PublicId).ToList();
            var award = matches.Count == 1 ? matches[0] : null;
            var valid = award != null && award.StudentEnrollmentId == row.StudentEnrollmentId &&
                award.DiscountRuleId == row.DiscountRuleId && award.EffectiveFrom == row.EffectiveFrom &&
                award.EffectiveTo == row.EffectiveTo;
            var mismatchedLink = row.StudentDiscountId.HasValue &&
                (!valid || row.StudentDiscountId.Value != award!.Id);
            var status = mismatchedLink ? "BrokenAwardLineage" :
                !valid && matches.Count > 0 ? "ConflictingCanonicalAward" :
                row.State == "Awarded" ? (!valid || row.StudentDiscountId != award!.Id ?
                    "BrokenAwardLineage" : award.IsActive ? "MatchedActive" : "AwardDeactivated") :
                row.State == "Approved" ? valid ? "LinkPending" : "AwaitingFinancialAward" :
                row.State == "Cancelled" ? valid && award!.IsActive ? "DeactivationPending" :
                    valid ? "StoppedForFutureBilling" : "CancelledWithoutAward" :
                valid ? "UnexpectedFinancialAward" : "NoFinancialAward";
            return new ScholarshipReconciliationRow
            {
                ApplicationReference = row.PublicId, EnrollmentReference = row.EnrollmentReference,
                ApplicationState = row.State, LinkedStudentDiscountId = row.StudentDiscountId,
                CanonicalStudentDiscountId = award?.Id,
                IsCanonicalDiscountActive = award?.IsActive, LineageStatus = status
            };
        }).ToArray();
        return ApiResponse<ScholarshipReconciliationPage>.SuccessResponse(new()
        {
            Page = page, PageSize = pageSize, TotalCount = total, Items = rows
        });
    }
}
