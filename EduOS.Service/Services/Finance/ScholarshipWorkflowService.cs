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
    IFinanceAdjustmentService finance, IFileUploadService storage, IDocumentFileService files)
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
            DiscountRuleId = x.DiscountRuleId, AwardMode = x.AwardMode,
            CashAmount = x.CashAmount, State = x.State,
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

    /// <summary>Stages already scanned, private evidence through the canonical file service;
    /// the application still binds this asset in SubmitAsync under the same actor/tenant.</summary>
    public async Task<ApiResponse<FileAssetDto>> UploadEvidenceAsync(Guid enrollmentReference,
        Guid requestId, PrivateFileUploadDto upload, CancellationToken ct = default)
    {
        if (!Scoped) return Fail<FileAssetDto>("Authentication required.", 403);
        if (enrollmentReference == Guid.Empty || requestId == Guid.Empty || upload == null ||
            upload.Length is < 1 or > 5242880)
            return Fail<FileAssetDto>("Select an enrollment and a private evidence file up to 5 MB.");
        var enrollment = await db.StudentEnrollments.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == enrollmentReference &&
            x.State == EnrollmentState.Active && x.IsCurrent && !x.IsDeleted, ct);
        if (enrollment == null || (!Manager && !await OwnsEnrollment(enrollment.StudentId, ct)))
            return Fail<FileAssetDto>("Enrollment not accessible.", 404);
        if (Manager) return await files.UploadAsync(requestId, upload, ct);
        var studentRef = await db.Students.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.Id == enrollment.StudentId && !x.IsDeleted)
            .Select(x => x.PublicId).FirstOrDefaultAsync(ct);
        return studentRef == Guid.Empty ? Fail<FileAssetDto>("Student unavailable.", 404) :
            await files.UploadPortalAsync(studentRef, requestId, upload, ct);
    }
    public async Task<ApiResponse<ScholarshipRow>> SubmitAsync(
        SubmitScholarshipRequest request, CancellationToken ct = default)
    {
        if (!Scoped) return Fail<ScholarshipRow>("Authentication required.", 403);
        if (request == null || request.RequestId == Guid.Empty ||
            request.EnrollmentReference == Guid.Empty ||
            request.AwardMode is not ("FeeCredit" or "CashGrant") ||
            (request.AwardMode == "FeeCredit" && (request.DiscountRuleId <= 0 || request.CashAmount.HasValue)) ||
            (request.AwardMode == "CashGrant" && (request.DiscountRuleId != 0 ||
                !request.CashAmount.HasValue || request.CashAmount <= 0m ||
                request.CashAmount > 99999999m || decimal.Round(request.CashAmount.Value, 2) != request.CashAmount)) ||
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
                previous.DiscountRuleId == (request.AwardMode == "FeeCredit" ? (long?)request.DiscountRuleId : null) &&
                previous.AwardMode == request.AwardMode && previous.CashAmount == request.CashAmount &&
                previous.EffectiveFrom == request.EffectiveFrom &&
                previous.EffectiveTo == request.EffectiveTo &&
                previous.EligibilityStatement == request.EligibilityStatement.Trim()
                ? ApiResponse<ScholarshipRow>.SuccessResponse(Map(previous, enrollment.PublicId, enrollment.CampusId))
                : Fail<ScholarshipRow>("Request ID already used.", 409);
        }
        if (request.AwardMode == "FeeCredit")
        {
            var rule = await db.DiscountRules.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == request.DiscountRuleId &&
                x.IsActive && !x.IsDeleted, ct);
            if (rule == null || (rule.EffectiveFrom.HasValue && request.EffectiveFrom < rule.EffectiveFrom.Value) ||
                (rule.EffectiveTo.HasValue && (!request.EffectiveTo.HasValue ||
                    request.EffectiveTo.Value > rule.EffectiveTo.Value)))
                return Fail<ScholarshipRow>("Aid rule is unavailable for the requested dates.", 409);
        }
        long? fileId = null;
        if (request.EvidenceFileReference.HasValue)
        {
            fileId = await db.FileAssets.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                x.PublicId == request.EvidenceFileReference.Value && !x.IsDeleted &&
                x.UploadedByUserId == user.UserId && x.IsVerifiedSafe &&
                x.MalwareScanStateCode == "Clean" && x.StorageProvider == "LocalPrivate" &&
                x.Visibility == FileVisibility.Private).Select(x => (long?)x.Id).FirstOrDefaultAsync(ct);
            if (!fileId.HasValue) return Fail<ScholarshipRow>("Private evidence not verified.", 404);
            if (await db.Documents.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.FileAssetId == fileId.Value && !x.IsDeleted &&
                (x.EntityType != "Student" || x.EntityId != enrollment.StudentId), ct) ||
                await Applications.AsNoTracking().AnyAsync(x =>
                    x.EvidenceFileAssetId == fileId.Value &&
                    x.StudentEnrollmentId != enrollment.Id, ct))
                return Fail<ScholarshipRow>("Evidence belongs to another student or purpose.", 409);
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
                    x.AwardMode == request.AwardMode &&
                    x.DiscountRuleId == (request.AwardMode == "FeeCredit" ? (long?)request.DiscountRuleId : null) &&
                    (x.State == "Submitted" || x.State == "Approved" || x.State == "Awarded") &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.EffectiveFrom) &&
                    (!request.EffectiveTo.HasValue || x.EffectiveFrom <= request.EffectiveTo), token))
                    return Fail<ScholarshipRow>("An active or pending aid period already overlaps.", 409);
                var now = DateTime.UtcNow;
                var row = new ScholarshipApplication
                {
                    TenantId = user.TenantId, ClientRequestId = request.RequestId,
                    StudentEnrollmentId = enrollment.Id,
                    DiscountRuleId = request.AwardMode == "FeeCredit" ? request.DiscountRuleId : null,
                    AwardMode = request.AwardMode, CashAmount = request.CashAmount,
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
        if (row.AwardMode != "FeeCredit" || !row.DiscountRuleId.HasValue || row.CashAmount.HasValue)
            return Fail<ScholarshipRow>("Cash grants require an independently reviewed disbursement, not a fee credit.", 409);
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
            DiscountRuleId = row.DiscountRuleId.Value, EffectiveFrom = row.EffectiveFrom,
            EffectiveTo = row.EffectiveTo, Reason = "Approved scholarship " + row.PublicId
        }, ct);
        if (!assignment.Success)
            return Fail<ScholarshipRow>(assignment.Message, assignment.StatusCode);
        if (assignment.Data == null || !assignment.Data.IsActive)
            return Fail<ScholarshipRow>("Canonical award is inactive or unavailable; reconcile Finance.", 409);
        var canonicalAward = await db.StudentDiscounts.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == assignment.Data.Id && !x.IsDeleted &&
            x.ClientRequestId == row.PublicId && x.StudentEnrollmentId == row.StudentEnrollmentId &&
            x.DiscountRuleId == row.DiscountRuleId && x.EffectiveFrom == row.EffectiveFrom &&
            x.EffectiveTo == row.EffectiveTo, ct);
        if (canonicalAward == null || !canonicalAward.IsActive)
            return Fail<ScholarshipRow>("Award must have its own active, verified financial lineage.", 409);
        ApiResponse<ScholarshipRow> linked;
        try
        {
            linked = await db.ExecuteInTransactionAsync(async token =>
            {
                var current = await Applications.FirstOrDefaultAsync(x => x.Id == row.Id, token);
                if (current == null || current.State is not ("Approved" or "Awarded"))
                    return Fail<ScholarshipRow>("Application changed; finance reconciliation required.", 409);
                if (current.StudentDiscountId.HasValue &&
                    current.StudentDiscountId != canonicalAward.Id)
                    return Fail<ScholarshipRow>("Award lineage mismatch.", 409);
                if (!await db.StudentDiscounts.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.Id == canonicalAward.Id && !x.IsDeleted &&
                    x.IsActive && x.ClientRequestId == current.PublicId, token))
                    return Fail<ScholarshipRow>("Financial award changed before linkage; reload.", 409);
                current.StudentDiscountId = canonicalAward.Id;
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
        catch (DbUpdateConcurrencyException)
        {
            linked = Fail<ScholarshipRow>("Award linked concurrently; reload.", 409);
        }
        catch (DbUpdateException)
        {
            linked = Fail<ScholarshipRow>("Award linked concurrently; reload.", 409);
        }
        if (!linked.Success)
        {
            // A cancellation can commit between the Finance assignment and its
            // scholarship link. Do not strand an active discount after cancellation.
            var currentState = await Applications.AsNoTracking().Where(x => x.Id == row.Id)
                .Select(x => x.State).FirstOrDefaultAsync(ct);
            if (currentState == "Cancelled")
            {
                var posted = await db.StudentDiscounts.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Id == canonicalAward.Id && !x.IsDeleted &&
                    x.ClientRequestId == row.PublicId, ct);
                if (posted?.IsActive == true)
                {
                    var stop = await finance.DeactivateStudentDiscountAsync(posted.Id,
                        new DeactivateStudentDiscountRequestDto
                        {
                            RowVersion = Convert.ToBase64String(posted.RowVersion)
                        }, ct);
                    if (!stop.Success)
                        return Fail<ScholarshipRow>("Cancelled award needs Finance reconciliation: " +
                            stop.Message, 409);
                }
            }
        }
        return linked;
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
        if (row.State is not ("Submitted" or "Approved" or "Awarded" or "Cancelled") ||
            (row.State != "Cancelled" && !Version(row.RowVersion, request.RowVersion)))
            return Fail<ScholarshipRow>("Only a submitted, approved or linked award may be cancelled.", 409);
        if (row.AwardMode == "CashGrant")
        {
            if (await db.ScholarshipDisbursements.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.ScholarshipApplicationId == row.Id &&
                (x.State == "Submitted" || x.State == "Posted" || x.State == "ReversalRequested"), ct))
                return Fail<ScholarshipRow>("Cash payment must be rejected or reversed before cancellation.", 409);
        }
        // AwardAsync commits the canonical financial assignment before it links the
        // scholarship row. Reconcile by the stable award key, not just the nullable FK.
        var candidates = await db.StudentDiscounts.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && x.ClientRequestId == row.PublicId)
            .Take(2).ToListAsync(ct);
        if (candidates.Count > 1)
            return Fail<ScholarshipRow>("Multiple financial awards found; reconcile before cancellation.", 409);
        var assigned = candidates.SingleOrDefault();
        if (assigned != null && (assigned.StudentEnrollmentId != row.StudentEnrollmentId ||
            assigned.DiscountRuleId != row.DiscountRuleId ||
            assigned.EffectiveFrom != row.EffectiveFrom || assigned.EffectiveTo != row.EffectiveTo ||
            (row.StudentDiscountId.HasValue && row.StudentDiscountId != assigned.Id)))
            return Fail<ScholarshipRow>("Financial assignment mismatch; reconcile before cancellation.", 409);
        if ((row.StudentDiscountId.HasValue && (assigned == null || row.StudentDiscountId != assigned.Id)) ||
            (row.State == "Awarded" && assigned == null) ||
            (row.State == "Submitted" && assigned != null))
            return Fail<ScholarshipRow>("Financial lineage needs reconciliation before cancellation.", 409);
        // Stop future invoice reductions first. A retry after a successful finance
        // commit can safely finish the status change without reactivating an award.
        if (assigned?.IsActive == true)
        {
            var stop = await finance.DeactivateStudentDiscountAsync(assigned.Id,
                new DeactivateStudentDiscountRequestDto
                {
                    RowVersion = Convert.ToBase64String(assigned.RowVersion)
                }, ct);
            if (!stop.Success) return Fail<ScholarshipRow>(stop.Message, stop.StatusCode);
        }
        if (row.State == "Cancelled")
        {
            var replay = await FindAsync(row, ct);
            return replay == null ? Fail<ScholarshipRow>("Application unavailable.", 404) :
                ApiResponse<ScholarshipRow>.SuccessResponse(replay);
        }
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var current = await Applications.FirstOrDefaultAsync(x => x.Id == row.Id, token);
                if (current == null || current.State != row.State ||
                    !Version(current.RowVersion, request.RowVersion))
                    return Fail<ScholarshipRow>("Application changed; finance reconciliation required.", 409);
                if (assigned != null)
                {
                    var canonical = await db.StudentDiscounts.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == user.TenantId && x.Id == assigned.Id && !x.IsDeleted, token);
                    if (canonical == null || canonical.IsActive || canonical.ClientRequestId != current.PublicId)
                        return Fail<ScholarshipRow>("Financial deactivation not confirmed; retry cancellation.", 409);
                    current.StudentDiscountId = canonical.Id;
                }
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
        catch (DbUpdateConcurrencyException) { return Fail<ScholarshipRow>("Concurrent cancellation; reload.", 409); }
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

    /// <summary>Admin-only immutable invoice-time credit report. Historical invoices issued
    /// before source attribution was introduced are intentionally not backfilled.</summary>
    public async Task<ApiResponse<ScholarshipInvoiceCreditPage>> InvoiceCreditsAsync(
        Guid reference, int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        if (!Manager) return Fail<ScholarshipInvoiceCreditPage>("Finance administrator required.", 403);
        if (reference == Guid.Empty || page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return Fail<ScholarshipInvoiceCreditPage>("Invalid application or page.");
        var aid = await Applications.AsNoTracking().Where(x => x.PublicId == reference)
            .Select(x => new { x.StudentDiscountId, x.StudentEnrollmentId }).FirstOrDefaultAsync(ct);
        if (aid == null) return Fail<ScholarshipInvoiceCreditPage>("Application not found.", 404);
        var result = new ScholarshipInvoiceCreditPage
        {
            ApplicationReference = reference, Page = page, PageSize = pageSize,
            HasCanonicalAward = aid.StudentDiscountId.HasValue
        };
        if (!aid.StudentDiscountId.HasValue)
            return ApiResponse<ScholarshipInvoiceCreditPage>.SuccessResponse(result);

        var source = from allocation in db.StudentInvoiceDiscountAttributions.AsNoTracking()
            join line in db.StudentInvoiceLines.AsNoTracking()
                on new { allocation.TenantId, Id = allocation.StudentInvoiceLineId }
                equals new { line.TenantId, line.Id }
            join invoice in db.StudentInvoices.AsNoTracking()
                on new { line.TenantId, Id = line.StudentInvoiceId }
                equals new { invoice.TenantId, invoice.Id }
            where allocation.TenantId == user.TenantId &&
                allocation.StudentDiscountId == aid.StudentDiscountId.Value &&
                invoice.StudentEnrollmentId == aid.StudentEnrollmentId
            select new { allocation, line, invoice };
        result.TotalCount = await source.CountAsync(ct);
        var totals = await source.GroupBy(x => x.invoice.State == InvoiceState.Cancelled)
            .Select(g => new { Cancelled = g.Key, Amount = g.Sum(x => x.allocation.Amount) })
            .ToArrayAsync(ct);
        result.ActiveInvoiceCreditTotal = totals.Where(x => !x.Cancelled).Sum(x => x.Amount);
        result.CancelledInvoiceCreditTotal = totals.Where(x => x.Cancelled).Sum(x => x.Amount);
        var rows = await source.OrderByDescending(x => x.invoice.InvoiceDate)
            .ThenByDescending(x => x.invoice.Id).ThenBy(x => x.line.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.invoice.PublicId, x.invoice.InvoiceNumber, x.invoice.InvoiceDate, x.invoice.State,
                x.line.FeeHeadNameSnapshot, x.allocation.Amount,
                x.allocation.RuleValueSnapshot, x.allocation.IsPercentageSnapshot
            }).ToListAsync(ct);
        result.Items = rows.Select(x => new ScholarshipInvoiceCreditRow
        {
            InvoiceReference = x.PublicId, InvoiceNumber = x.InvoiceNumber, InvoiceDate = x.InvoiceDate,
            InvoiceState = x.State.ToString(), FeeHeadName = x.FeeHeadNameSnapshot ?? "",
            CreditedAmount = x.Amount, RuleValueSnapshot = x.RuleValueSnapshot,
            IsPercentageSnapshot = x.IsPercentageSnapshot
        }).ToArray();
        return ApiResponse<ScholarshipInvoiceCreditPage>.SuccessResponse(result);
    }

    /// <summary>Read-only canonical award lineage; historical aggregate discounts are not
    /// inferred as an award amount unless an issuance-time attribution snapshot exists.</summary>
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
                x.application.Id, x.application.PublicId, x.application.State,
                x.application.AwardMode, x.application.CashAmount, x.application.StudentDiscountId,
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
        var cashIds = pageRows.Where(x => x.AwardMode == "CashGrant").Select(x => x.Id).ToArray();
        var cash = await db.ScholarshipDisbursements.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && cashIds.Contains(x.ScholarshipApplicationId) && !x.IsDeleted)
            .OrderByDescending(x => x.Id).Select(x => new { x.Id, x.ScholarshipApplicationId,
                x.State, x.Amount, x.JournalId, x.ReversalJournalId }).ToArrayAsync(ct);
        var journalIds = cash.SelectMany(x => new[] { x.JournalId, x.ReversalJournalId })
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var posted = await db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            journalIds.Contains(x.Id) && x.State == EduOS.Core.Enums.Domain.JournalState.Posted &&
            !x.IsDeleted).Select(x => new { x.Id, x.SourceType, x.SourceId, x.ReversalOfJournalId })
            .ToArrayAsync(ct);
        var rows = pageRows.Select(row =>
        {
            if (row.AwardMode == "CashGrant")
            {
                var payment = cash.FirstOrDefault(x => x.ScholarshipApplicationId == row.Id && x.State != "Rejected") ??
                    cash.FirstOrDefault(x => x.ScholarshipApplicationId == row.Id);
                var original = payment == null ? null : posted.FirstOrDefault(x => x.Id == payment.JournalId);
                var reversal = payment == null ? null : posted.FirstOrDefault(x => x.Id == payment.ReversalJournalId);
                var validOriginal = payment != null && original != null &&
                    original.SourceType == "ScholarshipDisbursement" && original.SourceId == payment.Id;
                var validReversal = payment != null && reversal != null && validOriginal &&
                    reversal.SourceType == "ScholarshipDisbursementReversal" &&
                    reversal.SourceId == payment.Id && reversal.ReversalOfJournalId == original!.Id;
                var cashStatus = payment == null ? row.State == "Cancelled" ? "CancelledWithoutAward" : "AwaitingCashInstruction" :
                    payment.Amount != row.CashAmount || row.StudentDiscountId.HasValue ? "BrokenCashLineage" :
                    payment.State == "Submitted" ? "AwaitingExternalConfirmation" :
                    payment.State == "Rejected" ? "CashInstructionRejected" :
                    payment.State == "Posted" && validOriginal && !validReversal ? "PostedCashGrant" :
                    payment.State == "ReversalRequested" && validOriginal ? "CashReversalPending" :
                    payment.State == "Reversed" && validReversal ? "ReversedCashGrant" : "BrokenCashLineage";
                return new ScholarshipReconciliationRow
                {
                    ApplicationReference = row.PublicId, EnrollmentReference = row.EnrollmentReference,
                    ApplicationState = row.State, LineageStatus = cashStatus
                };
            }
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
