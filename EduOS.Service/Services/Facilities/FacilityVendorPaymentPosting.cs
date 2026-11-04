using System.Data;
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
    /// Records an externally completed disbursement; never initiates a transfer.
    /// Independent Finance approval and the same dated, open accounting period
    /// are mandatory. Bank clearance is separately derived from statement matches.
    /// </summary>
    public async Task<ApiResponse<FacilityVendorPaymentDto>> PostPaymentAsync(
        Guid billReference, Guid paymentReference,
        FacilityVendorPaymentPostRequest request, CancellationToken ct = default)
    {
        if (!CanMake) return Error<FacilityVendorPaymentDto>("Finance posting denied.", 403);
        if (billReference == Guid.Empty || paymentReference == Guid.Empty ||
            request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.ExecutedDate == default ||
            Clean(request.BankTransactionReference) is not string bankReference ||
            bankReference.Length is < 6 or > 100 || !Plain(bankReference))
            return Error<FacilityVendorPaymentDto>("Execution date, bank reference and command required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (bill, _, payableId, failure) = await PayableSourceAsync(billReference, ct);
            if (failure != null || bill == null || !await LiveActor(bill.CampusId, false, ct))
                return Error<FacilityVendorPaymentDto>(failure ?? "Finance maker revoked.", 403);
            var source = await PaymentSourceAsync(bill, paymentReference, payableId, ct);
            if (!source.HasValue)
                return Error<FacilityVendorPaymentDto>("Payment source or bank ledger invalid.", 409);
            var j = source.Value.Journal;
            var marker = " | Bank execution " + request.ClientRequestId.ToString("N");
            if (j.State == JournalState.Posted && j.PostedByUserId == user.UserId &&
                j.JournalDate == request.ExecutedDate &&
                j.ReferenceNumber == bankReference &&
                (j.Description ?? "").EndsWith(marker, StringComparison.Ordinal))
                return ApiResponse<FacilityVendorPaymentDto>.SuccessResponse(
                    await PaymentMapAsync(j, source.Value.Lines, source.Value.BankId, ct));
            if (j.State != JournalState.Approved || j.ApprovedByUserId is null or <= 0 ||
                j.ApprovedByUserId == user.UserId || j.CreatedBy != user.UserId ||
                !Version(j.RowVersion, request.RowVersion))
                return Error<FacilityVendorPaymentDto>("Independent approved, unchanged instruction required.", 409);
            if (request.ExecutedDate != j.JournalDate ||
                !await db.AccountingPeriods.AsNoTracking().AnyAsync(p =>
                    p.TenantId == user.TenantId && p.Id == j.AccountingPeriodId &&
                    !p.IsDeleted && !p.IsClosed && p.StartDate <= request.ExecutedDate &&
                    p.EndDate >= request.ExecutedDate, ct))
                return Error<FacilityVendorPaymentDto>(
                    "Bank execution date must equal the approved open-period journal date.", 409);
            var (_, posted) = await PaymentTotalsAsync(bill.Id, payableId, null, ct);
            if (posted + source.Value.Lines[0].DebitAmount > bill.AmountBdt)
                return Error<FacilityVendorPaymentDto>("Supplier payable would be overpaid.", 409);
            var bankGlAccountId = source.Value.Lines[1].AccountId;
            // Reject duplicate transaction IDs both in the existing Procurement
            // and new Facilities streams for the same actual bank GL account.
            if (await (from other in db.Journals.AsNoTracking()
                join line in db.JournalLines.AsNoTracking()
                    on new { other.TenantId, JournalId = other.Id }
                    equals new { line.TenantId, line.JournalId }
                where other.TenantId == user.TenantId && !other.IsDeleted &&
                    other.Id != j.Id && other.State == JournalState.Posted &&
                    (other.SourceType == VendorPaymentSource ||
                     other.SourceType == "ProcurementSupplierPayment") &&
                    other.ReferenceNumber == bankReference &&
                    !line.IsDeleted && line.LineNo == 2 &&
                    line.AccountId == bankGlAccountId
                select other.Id).AnyAsync(ct))
                return Error<FacilityVendorPaymentDto>(
                    "Bank transaction reference already posted for this bank ledger.", 409);
            if ((j.Description ?? "").Length + marker.Length > 1000)
                return Error<FacilityVendorPaymentDto>("Bank execution audit capacity exceeded.", 409);
            j.State = JournalState.Posted;
            j.ReferenceNumber = bankReference;
            j.PostedByUserId = user.UserId;
            j.PostedAt = Now;
            j.Description += marker;
            j.UpdatedBy = user.UserId;
            j.UpdatedAt = Now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<FacilityVendorPaymentDto>.SuccessResponse(
                await PaymentMapAsync(j, source.Value.Lines, source.Value.BankId, ct),
                "Externally executed bank payment recorded; bank statement clearance is separate.");
        }
        catch (Exception ex) when (Conflict(ex))
        {
            logger.LogWarning(ex, "Facilities service payment posting conflict tenant {Tenant}", user.TenantId);
            return Error<FacilityVendorPaymentDto>("Concurrent bank payment or permission conflict.", 409);
        }
    }

    private async Task<bool> ActiveBankMatchAsync(long lineId, CancellationToken ct) =>
        await db.BankReconciliationMatchEvents.AsNoTracking().AnyAsync(m =>
            m.TenantId == user.TenantId && !m.IsDeleted &&
            m.JournalLineId == lineId && m.ActionCode == 1 &&
            !db.BankReconciliationMatchEvents.Any(unmatch =>
                unmatch.TenantId == user.TenantId && !unmatch.IsDeleted &&
                unmatch.ActionCode == 2 && unmatch.OriginalMatchEventId == m.Id), ct);

    /// <summary>
    /// A confirmed bank refund/reversal is source-linked to the posted payment.
    /// Already-matched bank statements must be explicitly unmatched under
    /// the Bank Reconciliation workflow before reversal.
    /// </summary>
    public async Task<ApiResponse<FacilityVendorPaymentDto>> RequestPaymentReversalAsync(
        Guid billReference, Guid paymentReference,
        FacilityVendorPaymentReversalRequest request, CancellationToken ct = default)
    {
        if (!CanMake) return Error<FacilityVendorPaymentDto>("Finance maker denied.", 403);
        if (billReference == Guid.Empty || paymentReference == Guid.Empty ||
            request == null || request.ClientRequestId == Guid.Empty ||
            request.AccountingPeriodId <= 0 || request.ReversalDate == default ||
            string.IsNullOrWhiteSpace(request.RowVersion) ||
            Clean(request.Reason) is not string reason || reason.Length is < 10 or > 500 ||
            !Plain(reason) ||
            Clean(request.BankReversalReference) is not string bankReference ||
            bankReference.Length is < 6 or > 100 || !Plain(bankReference))
            return Error<FacilityVendorPaymentDto>("Independent refund reference and reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (bill, _, payableId, failure) = await PayableSourceAsync(billReference, ct);
            if (failure != null || bill == null || !await LiveActor(bill.CampusId, false, ct))
                return Error<FacilityVendorPaymentDto>(failure ?? "Finance maker revoked.", 403);
            var source = await PaymentSourceAsync(bill, paymentReference, payableId, ct);
            if (!source.HasValue) return Error<FacilityVendorPaymentDto>("Payment not found.", 404);
            var payment = source.Value.Journal;
            if (payment.State != JournalState.Posted || !Version(payment.RowVersion, request.RowVersion) ||
                request.ReversalDate < payment.JournalDate)
                return Error<FacilityVendorPaymentDto>("Only an unchanged posted payment can be reversed.", 409);
            if (await ActiveBankMatchAsync(source.Value.Lines[1].Id, ct))
                return Error<FacilityVendorPaymentDto>(
                    "Unmatch the bank statement through independent reconciliation before reversal.", 409);
            if (!await db.AccountingPeriods.AsNoTracking().AnyAsync(p =>
                p.TenantId == user.TenantId && p.Id == request.AccountingPeriodId &&
                !p.IsDeleted && !p.IsClosed && p.StartDate <= request.ReversalDate &&
                p.EndDate >= request.ReversalDate, ct))
                return Error<FacilityVendorPaymentDto>("Open contra accounting period required.", 409);
            var old = await db.Journals.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && !x.IsDeleted &&
                x.SourceType == VendorPaymentReversalSource && x.SourceId == payment.Id &&
                x.ReversalOfJournalId == payment.Id &&
                x.State != JournalState.Rejected && x.State != JournalState.Cancelled)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (old != null)
            {
                if (old.State == JournalState.Submitted &&
                    old.IdempotencyKey == request.ClientRequestId &&
                    old.CreatedBy == user.UserId &&
                    old.JournalDate == request.ReversalDate &&
                    old.AccountingPeriodId == request.AccountingPeriodId &&
                    old.ReferenceNumber == bankReference && old.Description == reason)
                    return ApiResponse<FacilityVendorPaymentDto>.SuccessResponse(
                        await PaymentMapAsync(payment, source.Value.Lines, source.Value.BankId, ct));
                return Error<FacilityVendorPaymentDto>("An active contra already exists.", 409);
            }
            if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.IdempotencyKey == request.ClientRequestId, ct))
                return Error<FacilityVendorPaymentDto>("Reversal request ID already used.", 409);
            var now = Now;
            var publicId = Guid.NewGuid();
            var contra = new Journal {
                TenantId = user.TenantId, PublicId = publicId,
                IdempotencyKey = request.ClientRequestId,
                JournalNumber = "FVPR-" + publicId.ToString("N").ToUpperInvariant(),
                JournalDate = request.ReversalDate,
                AccountingPeriodId = request.AccountingPeriodId,
                CampusId = bill.CampusId, CurrencyCode = "BDT",
                SourceType = VendorPaymentReversalSource, SourceId = payment.Id,
                ReversalOfJournalId = payment.Id,
                ReferenceNumber = bankReference, Description = reason,
                State = JournalState.Submitted, CreatedBy = user.UserId, CreatedAt = now
            };
            db.Journals.Add(contra);
            await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(source.Value.Lines.Select(x => new JournalLine {
                TenantId = user.TenantId, JournalId = contra.Id,
                LineNo = x.LineNo, CampusId = bill.CampusId, AccountId = x.AccountId,
                DebitAmount = x.CreditAmount, CreditAmount = x.DebitAmount,
                Description = "Vendor bank reversal",
                CreatedBy = user.UserId, CreatedAt = now
            }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<FacilityVendorPaymentDto>.SuccessResponse(
                await PaymentMapAsync(payment, source.Value.Lines, source.Value.BankId, ct),
                "Bank reversal submitted for independent checker; contra is UNPOSTED.");
        }
        catch (Exception ex) when (Conflict(ex))
        {
            logger.LogWarning(ex, "Vendor settlement reversal contention tenant {Tenant}", user.TenantId);
            return Error<FacilityVendorPaymentDto>("Concurrent bank reversal conflict.", 409);
        }
    }

    public async Task<ApiResponse<FacilityVendorPaymentDto>> ReviewPaymentReversalAsync(
        Guid billReference, Guid paymentReference,
        FacilityVendorPaymentReversalReviewRequest request, CancellationToken ct = default)
    {
        if (!CanReview) return Error<FacilityVendorPaymentDto>("Independent reviewer denied.", 403);
        if (billReference == Guid.Empty || paymentReference == Guid.Empty ||
            request == null || string.IsNullOrWhiteSpace(request.RowVersion) ||
            Clean(request.Reason) is not string reason || reason.Length is < 10 or > 400 ||
            !Plain(reason))
            return Error<FacilityVendorPaymentDto>("Reversal review reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (bill, _, payableId, failure) = await PayableSourceAsync(billReference, ct);
            if (bill == null || failure != null || !await LiveActor(bill.CampusId, true, ct))
                return Error<FacilityVendorPaymentDto>(failure ?? "Reviewer denied.", 403);
            var source = await PaymentSourceAsync(bill, paymentReference, payableId, ct);
            if (!source.HasValue || source.Value.Journal.State != JournalState.Posted)
                return Error<FacilityVendorPaymentDto>("Posted payment unavailable.", 404);
            var payment = source.Value.Journal;
            var contra = await db.Journals.Where(x =>
                x.TenantId == user.TenantId && !x.IsDeleted &&
                x.SourceType == VendorPaymentReversalSource &&
                x.SourceId == payment.Id && x.ReversalOfJournalId == payment.Id &&
                x.State != JournalState.Cancelled)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (contra == null) return Error<FacilityVendorPaymentDto>("Contra request unavailable.", 404);
            if (contra.CreatedBy == user.UserId || payment.CreatedBy == user.UserId ||
                bill.PreparedByUserId == user.UserId)
                return Error<FacilityVendorPaymentDto>("Independent reversal checker required.", 403);
            if (contra.State != JournalState.Submitted ||
                !Version(contra.RowVersion, request.RowVersion))
                return Error<FacilityVendorPaymentDto>("Contra stale or already reviewed.", 409);
            if (request.Approve)
            {
                if (await ActiveBankMatchAsync(source.Value.Lines[1].Id, ct))
                    return Error<FacilityVendorPaymentDto>(
                        "Bank statement still matched; explicit unmatched evidence required.", 409);
                if (!await db.AccountingPeriods.AsNoTracking().AnyAsync(p =>
                    p.TenantId == user.TenantId && p.Id == contra.AccountingPeriodId &&
                    !p.IsClosed && !p.IsDeleted && p.StartDate <= contra.JournalDate &&
                    p.EndDate >= contra.JournalDate, ct))
                    return Error<FacilityVendorPaymentDto>("Contra accounting period closed.", 409);
                var lines = await db.JournalLines.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.JournalId == contra.Id && !x.IsDeleted)
                    .OrderBy(x => x.LineNo).Take(3).ToListAsync(ct);
                if (lines.Count != 2 || source.Value.Lines.Zip(lines).Any(pair =>
                    pair.First.LineNo != pair.Second.LineNo ||
                    pair.First.AccountId != pair.Second.AccountId ||
                    pair.First.CampusId != pair.Second.CampusId ||
                    pair.First.DebitAmount != pair.Second.CreditAmount ||
                    pair.First.CreditAmount != pair.Second.DebitAmount))
                    return Error<FacilityVendorPaymentDto>("Contra does not exactly reverse bank payment.", 409);
                if (string.IsNullOrWhiteSpace(contra.ReferenceNumber) ||
                    contra.ReferenceNumber.Length < 6 ||
                    contra.JournalDate < payment.JournalDate)
                    return Error<FacilityVendorPaymentDto>("Refund reference and date invalid.", 409);
            }
            var now = Now;
            contra.State = request.Approve ? JournalState.Posted : JournalState.Rejected;
            contra.ApprovedByUserId = user.UserId;
            contra.ApprovedAt = now;
            contra.PostedByUserId = request.Approve ? user.UserId : null;
            contra.PostedAt = request.Approve ? now : null;
            if ((contra.Description ?? "").Length + reason.Length + 20 > 1000)
                return Error<FacilityVendorPaymentDto>("Contra decision audit note too long.", 409);
            contra.Description += " | Decision: " + reason;
            contra.UpdatedBy = user.UserId; contra.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<FacilityVendorPaymentDto>.SuccessResponse(
                await PaymentMapAsync(payment, source.Value.Lines, source.Value.BankId, ct),
                request.Approve ? "Bank refund recorded by independently approved contra." :
                    "Contra rejected; original bank payment remains posted.");
        }
        catch (Exception ex) when (Conflict(ex))
        {
            logger.LogWarning(ex, "Vendor bank reversal review contention tenant {Tenant}", user.TenantId);
            return Error<FacilityVendorPaymentDto>("Concurrent contra review conflict.", 409);
        }
    }
}
