using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Facilities;

/// <summary>
/// Source-linked vendor disbursement instruction -> independent review ->
/// confirmed bank payment. Approval alone does NOT transfer funds.
/// The original non-stock bill is never edited to mirror a derived balance.
/// </summary>
public sealed partial class FacilityVendorServiceBillService
{
    private const string VendorPaymentSource = "FacilityVendorServicePayment";
    private const string VendorPaymentReversalSource = "FacilityVendorServicePaymentReversal";

    private async Task<(FacilityVendorServiceBill? Bill, Journal? Original,
        long PayableAccountId, string? Failure)> PayableSourceAsync(
        Guid reference, CancellationToken ct)
    {
        var bill = await db.FacilityVendorServiceBills.FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
        if (bill == null || !await CampusAllowed(bill.CampusId, ct))
            return (null, null, 0, "Service invoice not found or campus access denied.");
        if (bill.StateCode != "Posted" || bill.JournalId is not > 0)
            return (bill, null, 0, "A posted, non-reversing service payable is required.");
        var original = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == bill.JournalId.Value && !x.IsDeleted &&
            x.State == JournalState.Posted && x.SourceType == "FacilityVendorServiceBill" &&
            x.SourceId == bill.Id && x.CampusId == bill.CampusId && x.CurrencyCode == "BDT", ct);
        if (original == null) return (bill, null, 0, "Canonical payable journal missing.");
        if (await db.Journals.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && !x.IsDeleted &&
            x.SourceType == "FacilityVendorServiceBillReversal" &&
            x.SourceId == original.Id &&
            x.State != JournalState.Rejected && x.State != JournalState.Cancelled, ct))
            return (bill, original, 0, "Cannot settle a bill with pending or posted reversal.");
        var payableLines = await (from line in db.JournalLines.AsNoTracking()
            join account in db.Accounts.AsNoTracking()
                on new { line.TenantId, Id = line.AccountId }
                equals new { account.TenantId, account.Id }
            where line.TenantId == user.TenantId && line.JournalId == original.Id &&
                !line.IsDeleted && line.CreditAmount == bill.AmountBdt &&
                line.DebitAmount == 0m && line.CampusId == bill.CampusId &&
                account.AccountType == AccountType.Liability && !account.IsDeleted &&
                account.IsActive && account.AllowsPosting && !account.IsGroup &&
                account.CurrencyCode == "BDT"
            select line.AccountId).Take(2).ToListAsync(ct);
        return payableLines.Count == 1
            ? (bill, original, payableLines[0], null)
            : (bill, original, 0, "Unique posted BDT liability account is required.");
    }

    private async Task<(decimal Reserved, decimal Posted)> PaymentTotalsAsync(
        long billId, long payableAccountId, DateOnly? asOf, CancellationToken ct)
    {
        var paymentQuery = db.Journals.AsNoTracking().Where(j =>
            j.TenantId == user.TenantId && !j.IsDeleted &&
            j.SourceType == VendorPaymentSource && j.SourceId == billId &&
            (j.State == JournalState.Submitted || j.State == JournalState.Approved ||
             j.State == JournalState.Posted));
        if (asOf.HasValue) paymentQuery = paymentQuery.Where(j => j.JournalDate <= asOf.Value);
        var pending = await (from j in paymentQuery
            join l in db.JournalLines.AsNoTracking()
                on new { j.TenantId, JournalId = j.Id }
                equals new { l.TenantId, l.JournalId }
            where !l.IsDeleted && l.LineNo == 1 && l.AccountId == payableAccountId &&
                l.CreditAmount == 0m && l.DebitAmount > 0m &&
                (j.State == JournalState.Submitted || j.State == JournalState.Approved)
            select (decimal?)l.DebitAmount).SumAsync(ct) ?? 0m;
        var posted = await (from j in paymentQuery
            join l in db.JournalLines.AsNoTracking()
                on new { j.TenantId, JournalId = j.Id }
                equals new { l.TenantId, l.JournalId }
            where !l.IsDeleted && l.LineNo == 1 && l.AccountId == payableAccountId &&
                l.CreditAmount == 0m && l.DebitAmount > 0m &&
                j.State == JournalState.Posted &&
                !db.Journals.Any(r => r.TenantId == user.TenantId && !r.IsDeleted &&
                    r.SourceType == VendorPaymentReversalSource &&
                    r.SourceId == j.Id && r.ReversalOfJournalId == j.Id &&
                    r.State == JournalState.Posted &&
                    (!asOf.HasValue || r.JournalDate <= asOf.Value))
            select (decimal?)l.DebitAmount).SumAsync(ct) ?? 0m;
        return (pending, posted);
    }

    /// <summary>Blocks invoice contra while any active payment or reservation exists.</summary>
    private Task<bool> HasActiveVendorPaymentsAsync(long billId, CancellationToken ct) =>
        db.Journals.AsNoTracking().AnyAsync(j =>
            j.TenantId == user.TenantId && !j.IsDeleted &&
            j.SourceType == VendorPaymentSource && j.SourceId == billId &&
            (j.State == JournalState.Submitted || j.State == JournalState.Approved ||
             (j.State == JournalState.Posted &&
              !db.Journals.Any(r => r.TenantId == user.TenantId && !r.IsDeleted &&
                  r.SourceType == VendorPaymentReversalSource &&
                  r.SourceId == j.Id && r.ReversalOfJournalId == j.Id &&
                  r.State == JournalState.Posted))), ct);

    private async Task<(long BankAccountId, long GlAccountId, string Name)?>
        ValidBankAsync(long bankId, long campusId, CancellationToken ct)
    {
        var rows = await (from bank in db.BankAccounts.AsNoTracking()
            join account in db.Accounts.AsNoTracking()
                on new { bank.TenantId, Id = bank.AccountId }
                equals new { account.TenantId, account.Id }
            where bank.TenantId == user.TenantId && bank.Id == bankId && !bank.IsDeleted &&
                bank.IsActive && bank.CurrencyCode == "BDT" &&
                (bank.CampusId == null || bank.CampusId == campusId) &&
                account.CurrencyCode == "BDT" && account.AccountType == AccountType.Asset &&
                !account.IsDeleted && account.IsActive && account.AllowsPosting && !account.IsGroup
            select new { bank.Id, bank.AccountId, bank.BankName, bank.AccountName })
            .Take(2).ToListAsync(ct);
        if (rows.Count != 1) return null;
        var bankAccount = rows[0];
        if (await db.BankAccounts.AsNoTracking().CountAsync(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && x.IsActive &&
            x.AccountId == bankAccount.AccountId &&
            (x.CampusId == null || x.CampusId == campusId), ct) != 1)
            return null;
        return (bankAccount.Id, bankAccount.AccountId,
            bankAccount.BankName + " — " + bankAccount.AccountName);
    }

    private async Task<(Journal Journal, List<JournalLine> Lines, long BankId)?>
        PaymentSourceAsync(FacilityVendorServiceBill bill, Guid paymentReference,
            long payableId, CancellationToken ct)
    {
        var j = await db.Journals.FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && !x.IsDeleted &&
            x.PublicId == paymentReference && x.SourceType == VendorPaymentSource &&
            x.SourceId == bill.Id && x.CampusId == bill.CampusId, ct);
        if (j == null || j.CurrencyCode != "BDT" || j.ReversalOfJournalId != null)
            return null;
        var lines = await db.JournalLines.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && x.JournalId == j.Id)
            .OrderBy(x => x.LineNo).Take(3).ToListAsync(ct);
        if (lines.Count != 2 || lines[0].LineNo != 1 || lines[1].LineNo != 2 ||
            lines[0].CampusId != bill.CampusId || lines[1].CampusId != bill.CampusId ||
            lines[0].AccountId != payableId || lines[0].CreditAmount != 0m ||
            lines[0].DebitAmount <= 0m || lines[1].DebitAmount != 0m ||
            lines[1].CreditAmount != lines[0].DebitAmount)
            return null;
        var bankId = await db.BankAccounts.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.AccountId == lines[1].AccountId &&
            !x.IsDeleted && (x.CampusId == null || x.CampusId == bill.CampusId))
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (bankId <= 0) return null;
        var validated = await ValidBankAsync(bankId, bill.CampusId, ct);
        if (!validated.HasValue || validated.Value.GlAccountId != lines[1].AccountId)
            return null;
        return (j, lines, bankId);
    }

    private async Task<FacilityVendorPaymentDto> PaymentMapAsync(
        Journal j, List<JournalLine> lines, long bankId, CancellationToken ct)
    {
        var bankName = await db.BankAccounts.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.Id == bankId && !x.IsDeleted)
            .Select(x => x.BankName + " — " + x.AccountName).FirstOrDefaultAsync(ct);
        var contra = await db.Journals.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted &&
            x.SourceType == VendorPaymentReversalSource &&
            x.SourceId == j.Id && x.ReversalOfJournalId == j.Id)
            .OrderByDescending(x => x.Id).Select(x => new {
                x.PublicId, x.State, x.RowVersion
            }).FirstOrDefaultAsync(ct);
        var cleared = j.State == JournalState.Posted && lines.Count == 2
            ? await (from match in db.BankReconciliationMatchEvents.AsNoTracking()
                join batch in db.BankReconciliationBatches.AsNoTracking()
                    on new { match.TenantId, Id = match.BankReconciliationBatchId }
                    equals new { batch.TenantId, batch.Id }
                where match.TenantId == user.TenantId && !match.IsDeleted &&
                    match.ActionCode == 1 && match.JournalLineId == lines[1].Id &&
                    !db.BankReconciliationMatchEvents.Any(w =>
                        w.TenantId == user.TenantId && !w.IsDeleted &&
                        w.ActionCode == 2 && w.OriginalMatchEventId == match.Id) &&
                    batch.BankAccountId == bankId && !batch.IsDeleted &&
                    db.BankReconciliationApprovalEvents.Any(a =>
                        a.TenantId == user.TenantId && !a.IsDeleted &&
                        a.BankReconciliationBatchId == batch.Id && a.ActionCode == 3 &&
                        !db.BankReconciliationApprovalEvents.Any(r =>
                            r.TenantId == user.TenantId && !r.IsDeleted &&
                            r.BankReconciliationBatchId == batch.Id && r.ActionCode == 5 &&
                            (r.EventAt > a.EventAt ||
                             (r.EventAt == a.EventAt && r.Id > a.Id))))
                orderby batch.Id descending
                select batch.StatementIdentifier).FirstOrDefaultAsync(ct)
            : null;
        return new FacilityVendorPaymentDto {
            Reference = j.PublicId, State = j.State.ToString(),
            Amount = lines.Count == 2 ? lines[0].DebitAmount : 0m,
            PaymentDate = j.JournalDate, BankAccountId = bankId,
            BankName = bankName ?? string.Empty,
            MakerId = j.CreatedBy, ReviewerId = j.ApprovedByUserId,
            PostedByUserId = j.PostedByUserId,
            BankTransactionReference = j.State == JournalState.Posted ? j.ReferenceNumber : null,
            BankStatementCleared = cleared != null, ClearedStatementIdentifier = cleared,
            ReversalReference = contra?.PublicId,
            ReversalState = contra?.State.ToString(),
            ReversalRowVersion = contra == null ? null :
                Convert.ToBase64String(contra.RowVersion),
            RowVersion = Convert.ToBase64String(j.RowVersion)
        };
    }

    public async Task<ApiResponse<FacilityVendorPaymentOptionsDto>> PaymentOptionsAsync(
        Guid billReference, CancellationToken ct = default)
    {
        if (!CanRead) return Error<FacilityVendorPaymentOptionsDto>("Finance access denied.", 403);
        var (bill, _, payableId, failure) = await PayableSourceAsync(billReference, ct);
        if (failure != null || bill == null || !await LiveReader(bill.CampusId, ct))
            return Error<FacilityVendorPaymentOptionsDto>(failure ?? "Bill unavailable.", 409);
        var (reserved, posted) = await PaymentTotalsAsync(bill.Id, payableId, null, ct);
        var banks = await (from b in db.BankAccounts.AsNoTracking()
            join a in db.Accounts.AsNoTracking()
                on new { b.TenantId, Id = b.AccountId }
                equals new { a.TenantId, a.Id }
            where b.TenantId == user.TenantId && !b.IsDeleted && b.IsActive &&
                b.CurrencyCode == "BDT" &&
                (b.CampusId == null || b.CampusId == bill.CampusId) &&
                a.CurrencyCode == "BDT" && !a.IsDeleted && a.IsActive &&
                !a.IsGroup && a.AllowsPosting && a.AccountType == AccountType.Asset
            orderby b.BankName, b.Id
            select new FacilityVendorPaymentBankChoiceDto {
                BankAccountId = b.Id, Name = b.BankName + " — " + b.AccountName
            }).Take(201).ToListAsync(ct);
        var periods = await db.AccountingPeriods.AsNoTracking().Where(p =>
            p.TenantId == user.TenantId && !p.IsDeleted && !p.IsClosed)
            .OrderByDescending(p => p.EndDate).Take(201)
            .Select(p => new FacilityVendorBillingPeriodOptionDto {
                AccountingPeriodId = p.Id, Name = p.Name,
                StartDate = p.StartDate, EndDate = p.EndDate
            }).ToListAsync(ct);
        if (banks.Count > 200 || periods.Count > 200 || reserved + posted > bill.AmountBdt)
            return Error<FacilityVendorPaymentOptionsDto>("Ambiguous Finance mapping or overpayment; reconcile.", 409);
        return ApiResponse<FacilityVendorPaymentOptionsDto>.SuccessResponse(new() {
            InvoiceAmount = bill.AmountBdt, ReservedAmount = reserved,
            PostedNetPayments = posted,
            AvailableAmount = bill.AmountBdt - reserved - posted,
            Banks = banks, Periods = periods
        });
    }

    public async Task<ApiResponse<PagedResult<FacilityVendorPaymentDto>>> PaymentsAsync(
        Guid billReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead || page is < 1 or > 100000 || pageSize is < 1 or > 50)
            return Error<PagedResult<FacilityVendorPaymentDto>>("Invalid payment scope.", 400);
        var (bill, _, payableId, failure) = await PayableSourceAsync(billReference, ct);
        if (bill == null || failure != null || !await LiveReader(bill.CampusId, ct))
            return Error<PagedResult<FacilityVendorPaymentDto>>(failure ?? "Bill not found.", 409);
        var query = db.Journals.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted &&
            x.SourceType == VendorPaymentSource && x.SourceId == bill.Id);
        var count = await query.CountAsync(ct);
        var journals = await query.OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var results = new List<FacilityVendorPaymentDto>(journals.Count);
        foreach (var j in journals)
        {
            var payment = await PaymentSourceAsync(bill, j.PublicId, payableId, ct);
            if (payment.HasValue)
                results.Add(await PaymentMapAsync(payment.Value.Journal,
                    payment.Value.Lines, payment.Value.BankId, ct));
        }
        return ApiResponse<PagedResult<FacilityVendorPaymentDto>>.SuccessResponse(new() {
            Page = page, PageSize = pageSize, TotalCount = count, Items = results
        });
    }

    public async Task<ApiResponse<FacilityVendorPaymentDto>> CreatePaymentAsync(
        Guid billReference, FacilityVendorPaymentInstructionRequest request,
        CancellationToken ct = default)
    {
        if (!CanMake) return Error<FacilityVendorPaymentDto>("Finance maker access denied.", 403);
        if (billReference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.BillRowVersion) || request.BankAccountId <= 0 ||
            request.AccountingPeriodId <= 0 || request.PlannedDate == default ||
            request.Amount <= 0m || !Money(request.Amount) ||
            Clean(request.Reason) is not string reason || reason.Length is < 10 or > 500 ||
            !Plain(reason))
            return Error<FacilityVendorPaymentDto>("Valid payment instruction, amount and reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (bill, original, payableId, failure) = await PayableSourceAsync(billReference, ct);
            if (failure != null || bill == null || original == null)
                return Error<FacilityVendorPaymentDto>(failure ?? "Payable missing.", 409);
            if (!await LiveActor(bill.CampusId, false, ct))
                return Error<FacilityVendorPaymentDto>("Finance maker grant revoked.", 403);
            var replay = await db.Journals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.IdempotencyKey == request.ClientRequestId, ct);
            if (replay != null)
            {
                var match = await PaymentSourceAsync(bill, replay.PublicId, payableId, ct);
                if (replay.IsDeleted || replay.SourceType != VendorPaymentSource ||
                    replay.SourceId != bill.Id || replay.CreatedBy != user.UserId ||
                    replay.JournalDate != request.PlannedDate ||
                    replay.AccountingPeriodId != request.AccountingPeriodId ||
                    replay.Description != reason || !match.HasValue ||
                    match.Value.BankId != request.BankAccountId ||
                    match.Value.Lines[0].DebitAmount != request.Amount)
                    return Error<FacilityVendorPaymentDto>("Payment command ID reused with different intent.", 409);
                return ApiResponse<FacilityVendorPaymentDto>.SuccessResponse(
                    await PaymentMapAsync(match.Value.Journal, match.Value.Lines, match.Value.BankId, ct));
            }
            if (!Version(bill.RowVersion, request.BillRowVersion))
                return Error<FacilityVendorPaymentDto>("Bill changed; reload.", 409);
            if (request.PlannedDate < bill.InvoiceDate ||
                !await db.AccountingPeriods.AsNoTracking().AnyAsync(p =>
                    p.TenantId == user.TenantId && p.Id == request.AccountingPeriodId &&
                    !p.IsDeleted && !p.IsClosed &&
                    p.StartDate <= request.PlannedDate && p.EndDate >= request.PlannedDate, ct))
                return Error<FacilityVendorPaymentDto>("Open payment period after invoice date required.", 409);
            var bank = await ValidBankAsync(request.BankAccountId, bill.CampusId, ct);
            if (!bank.HasValue || bank.Value.GlAccountId == payableId)
                return Error<FacilityVendorPaymentDto>("Unique active BDT bank ledger required.", 409);
            var (reserved, posted) = await PaymentTotalsAsync(bill.Id, payableId, null, ct);
            if (reserved < 0m || posted < 0m || reserved + posted > bill.AmountBdt ||
                request.Amount > bill.AmountBdt - reserved - posted)
                return Error<FacilityVendorPaymentDto>("Insufficient unreserved payable balance.", 409);
            var now = Now;
            var refId = Guid.NewGuid();
            var j = new Journal {
                TenantId = user.TenantId, PublicId = refId,
                IdempotencyKey = request.ClientRequestId,
                JournalNumber = "FVP-" + refId.ToString("N").ToUpperInvariant(),
                JournalDate = request.PlannedDate, AccountingPeriodId = request.AccountingPeriodId,
                CampusId = bill.CampusId, CurrencyCode = "BDT",
                SourceType = VendorPaymentSource, SourceId = bill.Id,
                Description = reason, State = JournalState.Submitted,
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.Journals.Add(j);
            await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(
                new JournalLine { TenantId = user.TenantId, JournalId = j.Id,
                    LineNo = 1, CampusId = bill.CampusId, AccountId = payableId,
                    DebitAmount = request.Amount, CreditAmount = 0m,
                    Description = "Supplier payable instruction (not yet posted)",
                    CreatedAt = now, CreatedBy = user.UserId },
                new JournalLine { TenantId = user.TenantId, JournalId = j.Id,
                    LineNo = 2, CampusId = bill.CampusId, AccountId = bank.Value.GlAccountId,
                    DebitAmount = 0m, CreditAmount = request.Amount,
                    Description = "Bank disbursement instruction (not yet executed)",
                    CreatedAt = now, CreatedBy = user.UserId });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<FacilityVendorPaymentDto>.SuccessResponse(
                await PaymentMapAsync(j, await db.JournalLines.AsNoTracking()
                    .Where(x => x.TenantId == user.TenantId && x.JournalId == j.Id)
                    .OrderBy(x => x.LineNo).ToListAsync(ct), request.BankAccountId, ct));
        }
        catch (Exception ex) when (Conflict(ex))
        {
            logger.LogWarning(ex, "Facilities payment instruction contention tenant {TenantId}", user.TenantId);
            return Error<FacilityVendorPaymentDto>("Concurrent payment reservation or authorization change.", 409);
        }
    }

    public async Task<ApiResponse<FacilityVendorPaymentDto>> ReviewPaymentAsync(
        Guid billReference, Guid paymentReference,
        FacilityVendorPaymentReviewRequest request, CancellationToken ct = default)
    {
        if (!CanReview) return Error<FacilityVendorPaymentDto>("Independent Finance reviewer denied.", 403);
        if (billReference == Guid.Empty || paymentReference == Guid.Empty ||
            request == null || string.IsNullOrWhiteSpace(request.RowVersion) ||
            Clean(request.Reason) is not string reason || reason.Length is < 10 or > 400 ||
            !Plain(reason))
            return Error<FacilityVendorPaymentDto>("Independent payment review reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (bill, _, payableId, failure) = await PayableSourceAsync(billReference, ct);
            if (bill == null || failure != null || !await LiveActor(bill.CampusId, true, ct))
                return Error<FacilityVendorPaymentDto>(failure ?? "Reviewer not authorized.", 403);
            var payment = await PaymentSourceAsync(bill, paymentReference, payableId, ct);
            if (!payment.HasValue)
                return Error<FacilityVendorPaymentDto>("Payment instruction missing or inconsistent.", 404);
            var j = payment.Value.Journal;
            if (j.CreatedBy == user.UserId || bill.PreparedByUserId == user.UserId)
                return Error<FacilityVendorPaymentDto>("Maker cannot approve own supplier payment.", 403);
            if (j.State != JournalState.Submitted ||
                !Version(j.RowVersion, request.RowVersion))
                return Error<FacilityVendorPaymentDto>("Payment changed or already decided.", 409);
            if (request.Approve)
            {
                if (!await db.AccountingPeriods.AsNoTracking().AnyAsync(p =>
                    p.TenantId == user.TenantId && p.Id == j.AccountingPeriodId &&
                    !p.IsDeleted && !p.IsClosed &&
                    p.StartDate <= j.JournalDate && p.EndDate >= j.JournalDate, ct))
                    return Error<FacilityVendorPaymentDto>("Payment period closed.", 409);
                var (reserved, posted) = await PaymentTotalsAsync(bill.Id, payableId, null, ct);
                if (reserved < 0m || posted < 0m || reserved + posted > bill.AmountBdt)
                    return Error<FacilityVendorPaymentDto>("Payment reservations exceed service invoice.", 409);
            }
            var now = Now;
            j.State = request.Approve ? JournalState.Approved : JournalState.Rejected;
            if (request.Approve)
            {
                j.ApprovedByUserId = user.UserId;
                j.ApprovedAt = now;
            }
            j.Description = (j.Description ?? "") + " | Finance decision: " + reason;
            if (j.Description.Length > 1000)
                return Error<FacilityVendorPaymentDto>("Review reason exceeds audit capacity.", 409);
            j.UpdatedAt = now; j.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<FacilityVendorPaymentDto>.SuccessResponse(
                await PaymentMapAsync(j, payment.Value.Lines, payment.Value.BankId, ct));
        }
        catch (Exception ex) when (Conflict(ex))
        {
            logger.LogWarning(ex, "Facilities payment review conflict tenant {TenantId}", user.TenantId);
            return Error<FacilityVendorPaymentDto>("Concurrent payment review conflict.", 409);
        }
    }
}
