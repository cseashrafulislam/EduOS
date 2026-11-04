using System.Data;
using System.Security.Cryptography;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Facilities;

public sealed partial class FacilityVendorServiceBillService
{
    /// <summary>
    /// Reversal starts as an independently approvable canonical contra Journal.
    /// No original posted transaction is edited. A rejected request can be
    /// replaced; one active contra exists per original posted service bill.
    /// </summary>
    public async Task<ApiResponse<FacilityVendorBillDto>> RequestReversalAsync(
        Guid reference, FacilityVendorBillReversalRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanMake) return Error<FacilityVendorBillDto>("Finance maker access denied.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.ReversalDate == default || request.AccountingPeriodId <= 0 ||
            Clean(request.Reason) is not string reason || reason.Length is < 10 or > 800 ||
            !Plain(reason))
            return Error<FacilityVendorBillDto>("Reversal date, period, command and reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var bill = await db.FacilityVendorServiceBills.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
            if (bill == null) return Error<FacilityVendorBillDto>("Bill not found.", 404);
            if (!await LiveActor(bill.CampusId, false, ct))
                return Error<FacilityVendorBillDto>("Finance maker authorization revoked.", 403);
            if (bill.StateCode != "Posted" || !Version(bill.RowVersion, request.RowVersion) ||
                bill.JournalId is not long journalId)
                return Error<FacilityVendorBillDto>("Only a posted, unchanged service bill can be reversed.", 409);
            if (await HasActiveVendorPaymentsAsync(bill.Id, ct))
                return Error<FacilityVendorBillDto>(
                    "Service bill has pending or posted supplier payments; reverse those first.", 409);
            var original = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == journalId &&
                x.SourceType == "FacilityVendorServiceBill" && x.SourceId == bill.Id &&
                x.State == JournalState.Posted && !x.IsDeleted, ct);
            if (original == null || original.CampusId != bill.CampusId ||
                request.ReversalDate < original.JournalDate)
                return Error<FacilityVendorBillDto>("Original source journal or reversal date invalid.", 409);
            var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == request.AccountingPeriodId &&
                !x.IsDeleted && !x.IsClosed &&
                x.StartDate <= request.ReversalDate && x.EndDate >= request.ReversalDate, ct);
            if (period == null)
                return Error<FacilityVendorBillDto>("Open reversal accounting period required.", 409);
            var existing = await db.Journals.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && x.SourceType == "FacilityVendorServiceBillReversal" &&
                x.SourceId == original.Id && !x.IsDeleted &&
                x.State != JournalState.Rejected && x.State != JournalState.Cancelled)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (existing != null)
            {
                if (existing.State == JournalState.Submitted &&
                    existing.IdempotencyKey == request.ClientRequestId &&
                    existing.CreatedBy == user.UserId &&
                    existing.JournalDate == request.ReversalDate &&
                    existing.AccountingPeriodId == request.AccountingPeriodId &&
                    existing.Description == reason)
                    return ApiResponse<FacilityVendorBillDto>.SuccessResponse(await Map(bill, ct));
                return Error<FacilityVendorBillDto>("An active reversal already exists.", 409);
            }
            if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.IdempotencyKey == request.ClientRequestId, ct))
                return Error<FacilityVendorBillDto>("Reversal command ID already used.", 409);
            var originalLines = await db.JournalLines.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && x.JournalId == original.Id && !x.IsDeleted)
                .OrderBy(x => x.LineNo).ToListAsync(ct);
            if (originalLines.Count != 2 ||
                originalLines[0].LineNo != 1 || originalLines[1].LineNo != 2 ||
                originalLines[0].DebitAmount != bill.AmountBdt ||
                originalLines[0].CreditAmount != 0m ||
                originalLines[1].DebitAmount != 0m ||
                originalLines[1].CreditAmount != bill.AmountBdt ||
                originalLines[0].CampusId != bill.CampusId ||
                originalLines[1].CampusId != bill.CampusId)
                return Error<FacilityVendorBillDto>("Original GL lines do not match immutable bill.", 409);
            var now = Now;
            var newReference = Guid.NewGuid();
            var contra = new Journal {
                TenantId = user.TenantId, PublicId = newReference,
                IdempotencyKey = request.ClientRequestId,
                JournalNumber = "FVR-" + newReference.ToString("N").ToUpperInvariant(),
                JournalDate = request.ReversalDate, AccountingPeriodId = period.Id,
                CampusId = bill.CampusId, CurrencyCode = "BDT",
                SourceType = "FacilityVendorServiceBillReversal", SourceId = original.Id,
                ReversalOfJournalId = original.Id, ReferenceNumber = bill.SupplierInvoiceNumber,
                Description = reason, State = JournalState.Submitted,
                CreatedBy = user.UserId, CreatedAt = now
            };
            db.Journals.Add(contra);
            await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(originalLines.Select(line => new JournalLine {
                TenantId = user.TenantId, JournalId = contra.Id, LineNo = line.LineNo,
                CampusId = bill.CampusId, AccountId = line.AccountId,
                DebitAmount = line.CreditAmount, CreditAmount = line.DebitAmount,
                Description = line.Description, CreatedBy = user.UserId, CreatedAt = now
            }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<FacilityVendorBillDto>.SuccessResponse(await Map(bill, ct));
        }
        catch (Exception ex) when (Conflict(ex))
        {
            logger.LogWarning(ex, "Facility supplier reversal request conflict tenant {TenantId}", user.TenantId);
            return Error<FacilityVendorBillDto>("Concurrent service reversal conflict.", 409);
        }
    }

    public async Task<ApiResponse<FacilityVendorBillDto>> ReviewReversalAsync(
        Guid reference, FacilityVendorBillReversalReviewDto request,
        CancellationToken ct = default)
    {
        if (!CanReview) return Error<FacilityVendorBillDto>("Finance reviewer access denied.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion) ||
            Clean(request.Reason) is not string reason || reason.Length is < 10 or > 800 ||
            !Plain(reason))
            return Error<FacilityVendorBillDto>("Independent reversal decision required.", 400);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.Approve + "|" + reason)));
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var bill = await db.FacilityVendorServiceBills.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
            if (bill == null) return Error<FacilityVendorBillDto>("Bill not found.", 404);
            if (!await LiveActor(bill.CampusId, true, ct) ||
                bill.PreparedByUserId == user.UserId)
                return Error<FacilityVendorBillDto>("Independent active reviewer required.", 403);
            if (bill.JournalId is not long journalId)
                return Error<FacilityVendorBillDto>("Source journal not posted.", 409);
            var contra = await db.Journals.Where(x => x.TenantId == user.TenantId &&
                x.SourceType == "FacilityVendorServiceBillReversal" &&
                x.SourceId == journalId && !x.IsDeleted &&
                x.State != JournalState.Cancelled)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (contra == null) return Error<FacilityVendorBillDto>("Reversal request not found.", 404);
            if (contra.CreatedBy == user.UserId)
                return Error<FacilityVendorBillDto>("Maker cannot review own reversal.", 403);
            if (bill.ReversalReviewRequestId == request.ClientRequestId &&
                bill.ReversalReviewFingerprint == fingerprint &&
                contra.State == (request.Approve ? JournalState.Posted : JournalState.Rejected) &&
                (request.Approve ? bill.StateCode == "Reversed" : bill.StateCode == "Posted"))
                return ApiResponse<FacilityVendorBillDto>.SuccessResponse(await Map(bill, ct));
            if (contra.State != JournalState.Submitted ||
                bill.StateCode != "Posted" ||
                !Version(contra.RowVersion, request.RowVersion))
                return Error<FacilityVendorBillDto>("Reversal changed or already reviewed.", 409);
            var origin = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == journalId &&
                x.SourceType == "FacilityVendorServiceBill" && x.SourceId == bill.Id &&
                x.State == JournalState.Posted && !x.IsDeleted, ct);
            var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(p =>
                p.TenantId == user.TenantId && p.Id == contra.AccountingPeriodId &&
                p.StartDate <= contra.JournalDate && p.EndDate >= contra.JournalDate &&
                !p.IsDeleted && !p.IsClosed, ct);
            if (origin == null || period == null || contra.ReversalOfJournalId != origin.Id ||
                contra.CampusId != bill.CampusId)
                return Error<FacilityVendorBillDto>("Reversal provenance or period invalid.", 409);
            if (request.Approve)
            {
                if (await HasActiveVendorPaymentsAsync(bill.Id, ct))
                    return Error<FacilityVendorBillDto>(
                        "Supplier payment or reserved instruction exists; reverse it before invoice contra.", 409);
                var prior = await db.JournalLines.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.JournalId == origin.Id && !x.IsDeleted)
                    .OrderBy(x => x.LineNo).ToListAsync(ct);
                var lines = await db.JournalLines.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.JournalId == contra.Id && !x.IsDeleted)
                    .OrderBy(x => x.LineNo).ToListAsync(ct);
                if (prior.Count != 2 || lines.Count != 2 ||
                    prior.Zip(lines).Any(pair => pair.First.LineNo != pair.Second.LineNo ||
                        pair.First.AccountId != pair.Second.AccountId ||
                        pair.First.CampusId != pair.Second.CampusId ||
                        pair.First.DebitAmount != pair.Second.CreditAmount ||
                        pair.First.CreditAmount != pair.Second.DebitAmount))
                    return Error<FacilityVendorBillDto>("Reversal journal does not exactly negate source.", 409);
            }
            var now = Now;
            contra.State = request.Approve ? JournalState.Posted : JournalState.Rejected;
            contra.ApprovedByUserId = user.UserId; contra.ApprovedAt = now;
            contra.PostedByUserId = request.Approve ? user.UserId : null;
            contra.PostedAt = request.Approve ? now : null;
            contra.UpdatedAt = now; contra.UpdatedBy = user.UserId;
            bill.ReversalReviewRequestId = request.ClientRequestId;
            bill.ReversalReviewFingerprint = fingerprint;
            bill.UpdatedAt = now; bill.UpdatedBy = user.UserId;
            if (request.Approve) bill.StateCode = "Reversed";
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<FacilityVendorBillDto>.SuccessResponse(await Map(bill, ct));
        }
        catch (Exception ex) when (Conflict(ex))
        {
            logger.LogWarning(ex, "Facility supplier reversal review conflict tenant {TenantId}", user.TenantId);
            return Error<FacilityVendorBillDto>("Concurrent service reversal review.", 409);
        }
    }
}
