using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Enums.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

/// <summary>
/// Procurement-only maker/checker settlement instructions in the EXISTING Finance Journal.
/// Approved instructions remain unposted: this workflow never transfers money or reduces GL cash.
/// </summary>
public sealed partial class ProcurementSupplierInvoiceService
{
    private const string SettlementSource = "ProcurementSupplierPayment";
    private async Task<(ProcurementSupplierInvoice? Invoice, Journal? Original, long PayableId, string? Error)>
        SettlementSourceAsync(Guid reference, CancellationToken ct)
    {
        var invoice = await db.ProcurementSupplierInvoices.FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
        if (invoice == null || !await CampusAllowedAsync(invoice.CampusId, ct))
            return (null, null, 0, "Supplier invoice not found or campus denied.");
        if (invoice.StateCode != "Posted" || !invoice.JournalId.HasValue || invoice.ReversalJournalId.HasValue)
            return (invoice, null, 0, "Only posted, non-reversing payables accept settlement instructions.");
        var original = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == invoice.JournalId.Value && !x.IsDeleted &&
            x.State == JournalState.Posted && x.SourceType == "ProcurementSupplierInvoice" &&
            x.SourceId == invoice.Id && x.CampusId == invoice.CampusId && x.CurrencyCode == "BDT", ct);
        if (original == null) return (invoice, null, 0, "Posted payable journal provenance invalid.");
        var payable = await (from line in db.JournalLines.AsNoTracking()
            join account in db.Accounts.AsNoTracking() on line.AccountId equals account.Id
            where line.TenantId == user.TenantId && !line.IsDeleted && line.JournalId == original.Id &&
                line.CampusId == invoice.CampusId && line.DebitAmount == 0m &&
                line.CreditAmount == invoice.TotalAmount && account.TenantId == user.TenantId &&
                !account.IsDeleted && account.AccountType == AccountType.Liability &&
                account.IsActive && !account.IsGroup && account.AllowsPosting && account.CurrencyCode == "BDT"
            select line.AccountId).Take(2).ToListAsync(ct);
        return payable.Count == 1 ? (invoice, original, payable[0], null) :
            (invoice, original, 0, "Exactly one canonical payable credit line is required.");
    }

    private async Task<decimal> ReservedSettlementAsync(long invoiceId, long payableAccountId, CancellationToken ct) =>
        await db.JournalLines.AsNoTracking().Where(l => l.TenantId == user.TenantId && !l.IsDeleted &&
            l.AccountId == payableAccountId && l.CreditAmount == 0m &&
            db.Journals.Any(j => j.TenantId == user.TenantId && !j.IsDeleted &&
                j.Id == l.JournalId && j.SourceType == SettlementSource && j.SourceId == invoiceId &&
                (j.State == JournalState.Submitted || j.State == JournalState.Approved ||
                 j.State == JournalState.Posted)))
            .SumAsync(l => (decimal?)l.DebitAmount, ct) ?? 0m;

    private async Task<SupplierSettlementInstructionDto> SettlementMapAsync(Journal journal, CancellationToken ct)
    {
        var lines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.JournalId == journal.Id && !x.IsDeleted).OrderBy(x => x.LineNo).Take(3).ToListAsync(ct);
        var bankAccountId = lines.Count == 2 ? await db.BankAccounts.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.AccountId == lines[1].AccountId &&
                !x.IsDeleted && (x.CampusId == null || x.CampusId == journal.CampusId))
            .Select(x => x.Id).FirstOrDefaultAsync(ct) : 0;
        return new SupplierSettlementInstructionDto
        {
            Reference = journal.PublicId, State = journal.State.ToString(), PlannedDate = journal.JournalDate,
            Amount = lines.Count == 2 ? lines[0].DebitAmount : 0m, BankAccountId = bankAccountId,
            MakerId = journal.CreatedBy, ReviewerId = journal.ApprovedByUserId,
            Reason = journal.Description ?? "", RowVersion = Convert.ToBase64String(journal.RowVersion)
        };
    }

    public async Task<ApiResponse<SupplierSettlementOptionsDto>> SettlementOptionsAsync(Guid reference,
        CancellationToken ct = default)
    {
        if (!CanRead) return Err<SupplierSettlementOptionsDto>("Finance settlement access denied.", 403);
        var (invoice, _, payableId, error) = await SettlementSourceAsync(reference, ct);
        if (error != null || invoice == null) return Err<SupplierSettlementOptionsDto>(error ?? "Not found.", 409);
        var reserved = await ReservedSettlementAsync(invoice.Id, payableId, ct);
        var banks = await (from b in db.BankAccounts.AsNoTracking()
            join a in db.Accounts.AsNoTracking() on b.AccountId equals a.Id
            where b.TenantId == user.TenantId && !b.IsDeleted && b.IsActive && b.CurrencyCode == "BDT" &&
                (b.CampusId == null || b.CampusId == invoice.CampusId) &&
                a.TenantId == user.TenantId && !a.IsDeleted && a.IsActive && !a.IsGroup &&
                a.AllowsPosting && a.AccountType == AccountType.Asset && a.CurrencyCode == "BDT"
            orderby b.BankName, b.Id
            select new SupplierSettlementBankOptionDto { Id = b.Id, Name = b.BankName + " · " + b.AccountName })
            .Take(201).ToListAsync(ct);
        var periods = await db.AccountingPeriods.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && !x.IsClosed).OrderByDescending(x => x.EndDate).Take(201)
            .Select(x => new ProcurementInvoicePeriodOptionDto
            { Id = x.Id, Name = x.Name, StartDate = x.StartDate, EndDate = x.EndDate }).ToListAsync(ct);
        if (banks.Count > 200 || periods.Count > 200)
            return Err<SupplierSettlementOptionsDto>("Too many active banks or periods; narrow configuration.", 409);
        return ApiResponse<SupplierSettlementOptionsDto>.SuccessResponse(new SupplierSettlementOptionsDto
        {
            InvoiceAmount = invoice.TotalAmount, ReservedAmount = reserved,
            AvailableAmount = Math.Max(0m, invoice.TotalAmount - reserved), Banks = banks, Periods = periods
        });
    }

    public async Task<ApiResponse<PagedResult<SupplierSettlementInstructionDto>>> SettlementInstructionsAsync(
        Guid reference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead) return Err<PagedResult<SupplierSettlementInstructionDto>>("Finance settlement access denied.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return Err<PagedResult<SupplierSettlementInstructionDto>>("Invalid paging.");
        var invoice = await Visible().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
        if (invoice == null) return Err<PagedResult<SupplierSettlementInstructionDto>>("Supplier invoice not found.", 404);
        var query = db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.SourceType == SettlementSource && x.SourceId == invoice.Id && !x.IsDeleted &&
            x.CampusId == invoice.CampusId);
        var count = await query.CountAsync(ct);
        var journals = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var result = new List<SupplierSettlementInstructionDto>();
        foreach (var journal in journals) result.Add(await SettlementMapAsync(journal, ct));
        return ApiResponse<PagedResult<SupplierSettlementInstructionDto>>.SuccessResponse(
            new PagedResult<SupplierSettlementInstructionDto>
            { Items = result, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<SupplierSettlementInstructionDto>> RequestSettlementAsync(Guid reference,
        CreateSupplierSettlementInstructionRequest request, CancellationToken ct = default)
    {
        if (!CanMake) return Err<SupplierSettlementInstructionDto>("Finance settlement maker denied.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.BankAccountId <= 0 || request.AccountingPeriodId <= 0 || request.PlannedDate == default ||
            request.Amount <= 0m || !Money(request.Amount) || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length is < 10 or > 500)
            return Err<SupplierSettlementInstructionDto>("Valid key, bank, period, amount and reason (10–500) required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (invoice, original, payableId, error) = await SettlementSourceAsync(reference, ct);
            if (error != null || invoice == null || original == null)
                return Err<SupplierSettlementInstructionDto>(error ?? "Invoice unavailable.", 409);
            var replay = await db.Journals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.IdempotencyKey == request.ClientRequestId, ct);
            if (replay != null)
            {
                var mapped = await SettlementMapAsync(replay, ct);
                if (replay.IsDeleted || replay.SourceType != SettlementSource || replay.SourceId != invoice.Id ||
                    replay.CampusId != invoice.CampusId || replay.JournalDate != request.PlannedDate ||
                    replay.AccountingPeriodId != request.AccountingPeriodId || mapped.Amount != request.Amount ||
                    mapped.BankAccountId != request.BankAccountId ||
                    !(replay.Description ?? "").StartsWith(request.Reason.Trim(), StringComparison.Ordinal))
                    return Err<SupplierSettlementInstructionDto>("Idempotency key belongs to a different instruction.", 409);
                return ApiResponse<SupplierSettlementInstructionDto>.SuccessResponse(mapped, "Existing instruction returned.");
            }
            if (!Version(invoice.RowVersion, request.InvoiceRowVersion))
                return Err<SupplierSettlementInstructionDto>("Supplier invoice changed; reload.", 409);
            if (request.PlannedDate < invoice.InvoiceDate ||
                !await db.AccountingPeriods.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                    x.Id == request.AccountingPeriodId && !x.IsDeleted && !x.IsClosed &&
                    x.StartDate <= request.PlannedDate && x.EndDate >= request.PlannedDate, ct))
                return Err<SupplierSettlementInstructionDto>("Planned date requires an open period after the invoice.", 409);
            var bank = await (from b in db.BankAccounts.AsNoTracking()
                join a in db.Accounts.AsNoTracking() on b.AccountId equals a.Id
                where b.TenantId == user.TenantId && b.Id == request.BankAccountId && !b.IsDeleted &&
                    b.IsActive && b.CurrencyCode == "BDT" &&
                    (b.CampusId == null || b.CampusId == invoice.CampusId) &&
                    a.TenantId == user.TenantId && !a.IsDeleted && a.IsActive && !a.IsGroup &&
                    a.AllowsPosting && a.AccountType == AccountType.Asset && a.CurrencyCode == "BDT"
                select new { b.AccountId }).FirstOrDefaultAsync(ct);
            if (bank == null || bank.AccountId == payableId)
                return Err<SupplierSettlementInstructionDto>("Active campus-eligible BDT bank ledger required.", 409);
            if (await db.BankAccounts.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
                !x.IsDeleted && x.IsActive && x.AccountId == bank.AccountId &&
                (x.CampusId == null || x.CampusId == invoice.CampusId), ct) != 1)
                return Err<SupplierSettlementInstructionDto>("Bank ledger must resolve to exactly one eligible bank account.", 409);
            var reserved = await ReservedSettlementAsync(invoice.Id, payableId, ct);
            if (reserved < 0m || reserved > invoice.TotalAmount ||
                request.Amount > invoice.TotalAmount - reserved)
                return Err<SupplierSettlementInstructionDto>("Settlement exceeds unreserved payable.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var jRef = Guid.NewGuid();
            var journal = new Journal
            {
                TenantId = user.TenantId, PublicId = jRef, IdempotencyKey = request.ClientRequestId,
                JournalNumber = "PSI-" + jRef.ToString("N").ToUpperInvariant(),
                JournalDate = request.PlannedDate, AccountingPeriodId = request.AccountingPeriodId,
                CampusId = invoice.CampusId, CurrencyCode = "BDT",
                SourceType = SettlementSource, SourceId = invoice.Id,
                ReferenceNumber = invoice.SupplierInvoiceNumber, Description = request.Reason.Trim(),
                State = JournalState.Submitted, CreatedAt = now, CreatedBy = user.UserId
            };
            db.Journals.Add(journal);
            await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(
                new JournalLine { TenantId = user.TenantId, JournalId = journal.Id, LineNo = 1,
                    AccountId = payableId, CampusId = invoice.CampusId, DebitAmount = request.Amount,
                    Description = "Proposed payable settlement (NOT POSTED)", CreatedAt = now, CreatedBy = user.UserId },
                new JournalLine { TenantId = user.TenantId, JournalId = journal.Id, LineNo = 2,
                    AccountId = bank.AccountId, CampusId = invoice.CampusId, CreditAmount = request.Amount,
                    Description = "Proposed bank disbursement (NOT EXECUTED)", CreatedAt = now, CreatedBy = user.UserId });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<SupplierSettlementInstructionDto>.SuccessResponse(await SettlementMapAsync(journal, ct),
                "Source-linked instruction submitted; no payment or GL posting executed.");
        }
        catch (DbUpdateConcurrencyException) { return Err<SupplierSettlementInstructionDto>("Concurrent settlement conflict.", 409); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Settlement instruction write conflict"); return Err<SupplierSettlementInstructionDto>("Settlement instruction conflict.", 409); }
        catch (SqlException ex) when (ConflictSql(ex)) { return Err<SupplierSettlementInstructionDto>("Concurrent settlement SQL conflict.", 409); }
        catch (InvalidOperationException ex) when (ConflictSql(ex)) { return Err<SupplierSettlementInstructionDto>("Concurrent settlement conflict.", 409); }
    }

    /// <summary>
    /// Original maker can withdraw a submitted, UNPOSTED instruction. Rejected journal
    /// remains as immutable source-linked audit; reserved AP is released automatically.
    /// Retried requests must present the exact original withdrawal key and reason.
    /// </summary>
    public async Task<ApiResponse<SupplierSettlementInstructionDto>> WithdrawSettlementAsync(Guid reference,
        Guid instructionReference, WithdrawSupplierSettlementInstructionRequest request, CancellationToken ct = default)
    {
        if (!CanMake) return Err<SupplierSettlementInstructionDto>("Finance settlement maker denied.", 403);
        if (reference == Guid.Empty || instructionReference == Guid.Empty || request == null ||
            request.ClientRequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length is < 10 or > 400)
            return Err<SupplierSettlementInstructionDto>("Withdrawal key and documented reason (10–400) required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (invoice, _, payableId, error) = await SettlementSourceAsync(reference, ct);
            if (error != null || invoice == null)
                return Err<SupplierSettlementInstructionDto>(error ?? "Invoice unavailable.", 409);
            var journal = await db.Journals.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == instructionReference && x.SourceType == SettlementSource &&
                x.SourceId == invoice.Id && x.CampusId == invoice.CampusId && !x.IsDeleted, ct);
            if (journal == null) return Err<SupplierSettlementInstructionDto>("Instruction not found.", 404);
            if (journal.CreatedBy != user.UserId)
                return Err<SupplierSettlementInstructionDto>("Only the original instruction maker may withdraw.", 403);
            var marker = " | Maker withdrawal (" + request.ClientRequestId.ToString("N") + "): " + request.Reason.Trim();
            if (journal.State == JournalState.Rejected &&
                (journal.Description ?? "").EndsWith(marker, StringComparison.Ordinal))
                return ApiResponse<SupplierSettlementInstructionDto>.SuccessResponse(
                    await SettlementMapAsync(journal, ct), "Existing withdrawal returned; no payment executed.");
            if (journal.State != JournalState.Submitted || journal.ApprovedByUserId.HasValue ||
                journal.PostedAt.HasValue || journal.PostedByUserId.HasValue)
                return Err<SupplierSettlementInstructionDto>("Only submitted, unapproved, unposted instructions may be withdrawn.", 409);
            if (!Version(journal.RowVersion, request.RowVersion))
                return Err<SupplierSettlementInstructionDto>("Instruction version changed; reload.", 409);
            var lines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                x.JournalId == journal.Id && !x.IsDeleted).OrderBy(x => x.LineNo).Take(3).ToListAsync(ct);
            if (lines.Count != 2 || lines[0].LineNo != 1 || lines[1].LineNo != 2 ||
                lines[0].AccountId != payableId || lines[0].CampusId != invoice.CampusId ||
                lines[1].CampusId != invoice.CampusId || lines[0].DebitAmount <= 0m ||
                !Money(lines[0].DebitAmount) || lines[0].CreditAmount != 0m ||
                lines[1].DebitAmount != 0m || lines[1].CreditAmount != lines[0].DebitAmount)
                return Err<SupplierSettlementInstructionDto>("Instruction journal lines are inconsistent.", 409);
            if ((journal.Description ?? "").Length + marker.Length > 1000)
                return Err<SupplierSettlementInstructionDto>("Withdrawal audit exceeds journal capacity; shorten reason.", 409);
            journal.State = JournalState.Rejected;
            journal.Description = (journal.Description ?? "") + marker;
            journal.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            journal.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<SupplierSettlementInstructionDto>.SuccessResponse(
                await SettlementMapAsync(journal, ct),
                "Submitted instruction withdrawn; payable reservation released, GL and bank unchanged.");
        }
        catch (DbUpdateConcurrencyException) { return Err<SupplierSettlementInstructionDto>("Concurrent withdrawal conflict.", 409); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Settlement withdrawal write conflict"); return Err<SupplierSettlementInstructionDto>("Settlement withdrawal conflict.", 409); }
        catch (SqlException ex) when (ConflictSql(ex)) { return Err<SupplierSettlementInstructionDto>("Concurrent settlement SQL conflict.", 409); }
        catch (InvalidOperationException ex) when (ConflictSql(ex)) { return Err<SupplierSettlementInstructionDto>("Concurrent settlement conflict.", 409); }
    }

    public async Task<ApiResponse<SupplierSettlementInstructionDto>> ReviewSettlementAsync(Guid reference,
        Guid instructionReference, ReviewSupplierSettlementInstructionRequest request, CancellationToken ct = default)
    {
        if (!CanReview) return Err<SupplierSettlementInstructionDto>("Independent Finance approval denied.", 403);
        if (reference == Guid.Empty || instructionReference == Guid.Empty || request == null ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 400)
            return Err<SupplierSettlementInstructionDto>("Independent decision reason (10–400) required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (invoice, original, payableId, error) = await SettlementSourceAsync(reference, ct);
            if (error != null || invoice == null || original == null)
                return Err<SupplierSettlementInstructionDto>(error ?? "Invoice unavailable.", 409);
            var journal = await db.Journals.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == instructionReference && x.SourceType == SettlementSource &&
                x.SourceId == invoice.Id && x.CampusId == invoice.CampusId && !x.IsDeleted, ct);
            if (journal == null) return Err<SupplierSettlementInstructionDto>("Instruction not found.", 404);
            if (journal.CreatedBy is null or <= 0 || journal.CreatedBy == user.UserId ||
                invoice.CreatedBy == user.UserId || invoice.SubmittedByUserId == user.UserId)
                return Err<SupplierSettlementInstructionDto>("Independent maker-checker separation required.", 403);
            // Independently rescind an approved but still UNPOSTED instruction;
            // this only releases the payable reservation, never moves money.
            var revokingApproval = journal.State == JournalState.Approved && !request.Approve;
            if (journal.State != JournalState.Submitted && !revokingApproval)
                return Err<SupplierSettlementInstructionDto>("Only submitted decisions or approved-instruction revocation are allowed.", 409);
            if (!Version(journal.RowVersion, request.RowVersion))
                return Err<SupplierSettlementInstructionDto>("Instruction version changed; reload.", 409);
            if (revokingApproval && (journal.ApprovedByUserId is null or <= 0 ||
                !journal.ApprovedAt.HasValue || journal.PostedAt.HasValue ||
                journal.PostedByUserId.HasValue))
                return Err<SupplierSettlementInstructionDto>("Approved instruction audit or unposted state invalid.", 409);
            if (journal.CurrencyCode != "BDT" || journal.JournalDate < invoice.InvoiceDate ||
                journal.ReversalOfJournalId.HasValue || journal.AccountingPeriodId <= 0)
                return Err<SupplierSettlementInstructionDto>("Instruction source or accounting period invalid.", 409);
            var lines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                x.JournalId == journal.Id && !x.IsDeleted).OrderBy(x => x.LineNo).Take(3).ToListAsync(ct);
            if (lines.Count != 2 || lines[0].LineNo != 1 || lines[1].LineNo != 2 ||
                lines[0].AccountId != payableId || lines[0].CampusId != invoice.CampusId ||
                lines[1].CampusId != invoice.CampusId || lines[0].DebitAmount <= 0m ||
                !Money(lines[0].DebitAmount) || lines[0].CreditAmount != 0m ||
                lines[1].DebitAmount != 0m || lines[1].CreditAmount != lines[0].DebitAmount)
                return Err<SupplierSettlementInstructionDto>("Instruction journal lines are inconsistent.", 409);
            if (request.Approve)
            {
                if (!await db.AccountingPeriods.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                    x.Id == journal.AccountingPeriodId && !x.IsDeleted && !x.IsClosed &&
                    x.StartDate <= journal.JournalDate && x.EndDate >= journal.JournalDate, ct))
                    return Err<SupplierSettlementInstructionDto>("Settlement accounting period closed.", 409);
                // Source lineage must agree with the independent payable trace: an
                // inactive but non-deleted bank mapping still makes the ledger ambiguous.
                // Approval additionally requires that the sole bank/GL remains active.
                var bankMappings = await (from b in db.BankAccounts.AsNoTracking()
                    join a in db.Accounts.AsNoTracking() on b.AccountId equals a.Id
                    where b.TenantId == user.TenantId && !b.IsDeleted && b.AccountId == lines[1].AccountId &&
                        b.CurrencyCode == "BDT" && (b.CampusId == null || b.CampusId == invoice.CampusId) &&
                        a.TenantId == user.TenantId && !a.IsDeleted &&
                        a.AccountType == AccountType.Asset && a.CurrencyCode == "BDT"
                    select new { BankActive = b.IsActive, LedgerActive = a.IsActive,
                        a.IsGroup, a.AllowsPosting }).Take(2).ToListAsync(ct);
                if (bankMappings.Count != 1 || !bankMappings[0].BankActive ||
                    !bankMappings[0].LedgerActive || bankMappings[0].IsGroup ||
                    !bankMappings[0].AllowsPosting)
                    return Err<SupplierSettlementInstructionDto>(
                        "Bank ledger must resolve to exactly one eligible bank account at approval.", 409);
                var reserved = await ReservedSettlementAsync(invoice.Id, payableId, ct);
                if (reserved > invoice.TotalAmount)
                    return Err<SupplierSettlementInstructionDto>("Competing settlement exceeds invoice.", 409);
            }
            var now = clock.GetUtcNow().UtcDateTime;
            journal.State = request.Approve ? JournalState.Approved : JournalState.Rejected;
            // Retain the first approval actor/time as immutable audit evidence on revocation.
            if (request.Approve)
            {
                journal.ApprovedByUserId = user.UserId;
                journal.ApprovedAt = now;
            }
            var decisionNote = " | Finance decision: " +
                (revokingApproval ? "Approved instruction revoked: " : "") + request.Reason.Trim();
            var history = journal.Description ?? "";
            if (history.Length + decisionNote.Length > 1000)
                return Err<SupplierSettlementInstructionDto>("Finance decision audit exceeds journal capacity; use a shorter reason.", 409);
            journal.Description = history + decisionNote;
            journal.UpdatedAt = now; journal.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<SupplierSettlementInstructionDto>.SuccessResponse(await SettlementMapAsync(journal, ct),
                request.Approve ? "Instruction approved but UNPOSTED; no money moved." :
                    revokingApproval ? "Approved instruction revoked; reservation released, GL and bank unchanged." :
                    "Instruction rejected; reserved amount released.");
        }
        catch (DbUpdateConcurrencyException) { return Err<SupplierSettlementInstructionDto>("Concurrent review conflict.", 409); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Settlement review conflict"); return Err<SupplierSettlementInstructionDto>("Settlement review conflict.", 409); }
        catch (SqlException ex) when (ConflictSql(ex)) { return Err<SupplierSettlementInstructionDto>("Settlement review SQL conflict.", 409); }
        catch (InvalidOperationException ex) when (ConflictSql(ex)) { return Err<SupplierSettlementInstructionDto>("Settlement review conflict.", 409); }
    }
}
