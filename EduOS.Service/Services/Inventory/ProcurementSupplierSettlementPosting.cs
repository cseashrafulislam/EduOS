using System.Data;
using System.Text.RegularExpressions;
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
/// Finance owns the sole GL journal. A settlement posting records an externally confirmed
/// bank disbursement, and a contra is an independent, full-value GL reversal. Neither
/// endpoint calls a bank, changes stock, or writes a second supplier-payable balance.
/// </summary>
public sealed partial class ProcurementSupplierInvoiceService
{
    private static bool BankReference(string value) => value.Length is >= 6 and <= 100 &&
        Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9./:_-]{5,99}\z", RegexOptions.CultureInvariant);

    private const string ReversalDecisionMarker = " | Independent reversal decision: ";

    // The original reversal reason is followed by an append-only independent
    // review note. Retrying the SAME maker command after review must return the
    // existing contra, not falsely report that its idempotency key was reused.
    // Check the immutable source/bank/campus/actor identity before accepting it.
    private static bool SameReversalCommand(Journal replay, Journal source,
        ProcurementSupplierInvoice invoice, RequestSupplierSettlementReversal request, long actorId)
    {
        var reason = request.Reason.Trim();
        var description = replay.Description ?? "";
        var reviewPrefix = reason + ReversalDecisionMarker;
        var sameReason = replay.State == JournalState.Submitted
            ? description == reason
            : (replay.State == JournalState.Posted || replay.State == JournalState.Rejected) &&
              description.StartsWith(reviewPrefix, StringComparison.Ordinal) &&
              description.Length > reviewPrefix.Length;
        return !replay.IsDeleted && replay.CreatedBy == actorId &&
            replay.SourceType == SettlementReversalSource &&
            replay.SourceId == source.Id && replay.ReversalOfJournalId == source.Id &&
            replay.CampusId == invoice.CampusId && replay.CurrencyCode == "BDT" &&
            replay.ReferenceNumber == source.ReferenceNumber &&
            replay.AccountingPeriodId == request.AccountingPeriodId &&
            replay.JournalDate == request.ReversalDate && sameReason;
    }

    // Business dates follow the source campus, inheriting the institution time zone.
    // An absent/invalid zone is a posting conflict, never an implicit UTC fallback.
    private async Task<DateOnly?> SettlementLocalDayAsync(long campusId, CancellationToken ct)
    {
        var scope = await (from campus in db.Campuses.AsNoTracking()
            join tenant in db.Tenants.AsNoTracking() on campus.TenantId equals tenant.Id
            where campus.TenantId == user.TenantId && campus.Id == campusId &&
                campus.IsActive && !campus.IsDeleted &&
                tenant.Id == user.TenantId && !tenant.IsDeleted
            select new { CampusZone = campus.TimeZoneId, InstitutionZone = tenant.TimeZoneId })
            .SingleOrDefaultAsync(ct);
        if (scope == null) return null;
        var zoneId = string.IsNullOrWhiteSpace(scope.CampusZone) ? scope.InstitutionZone : scope.CampusZone;
        if (string.IsNullOrWhiteSpace(zoneId)) return null;
        try
        {
            var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(),
                TimeZoneInfo.FindSystemTimeZoneById(zoneId));
            return DateOnly.FromDateTime(local.DateTime);
        }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private async Task<(List<JournalLine> Lines, string? Error)> CheckedSettlementLinesAsync(
        Journal journal, long payableId, long campusId, CancellationToken ct)
    {
        var lines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.JournalId == journal.Id)
            .OrderBy(x => x.LineNo).Take(3).ToListAsync(ct);
        if (lines.Count != 2 || lines.Any(x => x.IsDeleted || x.CampusId != campusId) ||
            lines[0].LineNo != 1 || lines[1].LineNo != 2 ||
            lines[0].AccountId != payableId || lines[0].DebitAmount <= 0m ||
            !Money(lines[0].DebitAmount) || lines[0].CreditAmount != 0m ||
            lines[1].DebitAmount != 0m || lines[1].CreditAmount != lines[0].DebitAmount ||
            lines[1].AccountId == payableId)
            return (lines, "Settlement GL lines are missing or inconsistent.");
        var banks = await (from b in db.BankAccounts.AsNoTracking()
            join a in db.Accounts.AsNoTracking() on b.AccountId equals a.Id
            where b.TenantId == user.TenantId && !b.IsDeleted && b.IsActive &&
                b.AccountId == lines[1].AccountId && b.CurrencyCode == "BDT" &&
                (b.CampusId == null || b.CampusId == campusId) &&
                a.TenantId == user.TenantId && !a.IsDeleted && a.IsActive &&
                a.AccountType == AccountType.Asset && !a.IsGroup && a.AllowsPosting &&
                a.CurrencyCode == "BDT"
            select b.Id).Take(2).ToListAsync(ct);
        return banks.Count == 1 ? (lines, null) :
            (lines, "Exactly one active campus-eligible bank and GL mapping is required.");
    }

    public async Task<ApiResponse<SupplierSettlementInstructionDto>> PostSettlementAsync(Guid reference,
        Guid instructionReference, PostSupplierSettlementRequest request, CancellationToken ct = default)
    {
        if (!CanReview) return Err<SupplierSettlementInstructionDto>("Finance posting denied.", 403);
        var bankRef = request?.BankTransactionReference?.Trim() ?? "";
        if (reference == Guid.Empty || instructionReference == Guid.Empty || request == null ||
            request.ClientRequestId == Guid.Empty || request.ExecutedDate == default || !BankReference(bankRef))
            return Err<SupplierSettlementInstructionDto>("Valid confirmation key, date and bank transaction reference (6–100 ASCII characters) required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (invoice, original, payableId, error) = await SettlementSourceAsync(reference, ct);
            if (error != null || invoice == null || original == null)
                return Err<SupplierSettlementInstructionDto>(error ?? "Payable unavailable.", 409);
            var journal = await db.Journals.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == instructionReference && x.SourceType == SettlementSource &&
                x.SourceId == invoice.Id && x.CampusId == invoice.CampusId && !x.IsDeleted, ct);
            if (journal == null) return Err<SupplierSettlementInstructionDto>("Settlement instruction not found.", 404);
            var marker = " | Bank-confirmed (" + request.ClientRequestId.ToString("N") + "): " + bankRef;
            if (journal.State == JournalState.Posted)
                return journal.JournalDate == request.ExecutedDate && journal.ReferenceNumber == bankRef &&
                    (journal.Description ?? "").EndsWith(marker, StringComparison.Ordinal)
                    ? ApiResponse<SupplierSettlementInstructionDto>.SuccessResponse(
                        await SettlementMapAsync(journal, ct), "Existing confirmed settlement returned.")
                    : Err<SupplierSettlementInstructionDto>("This settlement is already posted under different confirmation evidence.", 409);
            if (journal.State != JournalState.Approved || journal.ApprovedByUserId is null or <= 0 ||
                !journal.ApprovedAt.HasValue || journal.PostedByUserId.HasValue || journal.PostedAt.HasValue ||
                journal.ReversalOfJournalId.HasValue || journal.CurrencyCode != "BDT" ||
                journal.CreatedBy is null or <= 0 || journal.CreatedBy == journal.ApprovedByUserId ||
                journal.CreatedBy == user.UserId || journal.ApprovedByUserId == invoice.CreatedBy ||
                journal.ApprovedByUserId == invoice.SubmittedByUserId)
                return Err<SupplierSettlementInstructionDto>("Only independently approved, unposted Finance instructions may be confirmed.", 409);
            if (!Version(journal.RowVersion, request.RowVersion))
                return Err<SupplierSettlementInstructionDto>("Settlement changed; reload.", 409);
            var businessDay = await SettlementLocalDayAsync(invoice.CampusId, ct);
            if (!businessDay.HasValue)
                return Err<SupplierSettlementInstructionDto>("Institution/campus business time zone unavailable.", 409);
            if (request.ExecutedDate != journal.JournalDate || request.ExecutedDate < invoice.InvoiceDate ||
                request.ExecutedDate > businessDay.Value ||
                !await db.AccountingPeriods.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                    x.Id == journal.AccountingPeriodId && !x.IsDeleted && !x.IsClosed &&
                    x.StartDate <= request.ExecutedDate && x.EndDate >= request.ExecutedDate, ct))
                return Err<SupplierSettlementInstructionDto>("Confirmation date must match the approved open-period date; otherwise request a new instruction.", 409);
            var (_, linesError) = await CheckedSettlementLinesAsync(journal, payableId, invoice.CampusId, ct);
            if (linesError != null) return Err<SupplierSettlementInstructionDto>(linesError, 409);
            if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Id != journal.Id && !x.IsDeleted &&
                x.SourceType == SettlementSource && x.State == JournalState.Posted &&
                x.ReferenceNumber == bankRef, ct))
                return Err<SupplierSettlementInstructionDto>("Bank transaction reference already confirms another settlement.", 409);
            if (await ReservedSettlementAsync(invoice.Id, payableId, ct) +
                await CreditedAsync(invoice.Id, true, 0, ct) > invoice.TotalAmount)
                return Err<SupplierSettlementInstructionDto>("Competing settlements exceed the supplier payable.", 409);
            if ((journal.Description ?? "").Length + marker.Length > 1000)
                return Err<SupplierSettlementInstructionDto>("Confirmation audit exceeds journal capacity.", 409);
            // Reference is the bank's external proof, NOT evidence that EduOS itself transferred money.
            var now = clock.GetUtcNow().UtcDateTime;
            journal.ReferenceNumber = bankRef;
            journal.Description = (journal.Description ?? "") + marker;
            journal.State = JournalState.Posted;
            journal.PostedAt = now; journal.PostedByUserId = user.UserId;
            journal.UpdatedAt = now; journal.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<SupplierSettlementInstructionDto>.SuccessResponse(
                await SettlementMapAsync(journal, ct), "Confirmed bank disbursement posted to the canonical Finance GL.");
        }
        catch (DbUpdateConcurrencyException) { return Err<SupplierSettlementInstructionDto>("Concurrent posting conflict.", 409); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Supplier settlement posting conflict"); return Err<SupplierSettlementInstructionDto>("Settlement posting conflict.", 409); }
        catch (SqlException ex) when (ConflictSql(ex)) { return Err<SupplierSettlementInstructionDto>("Settlement posting SQL conflict.", 409); }
        catch (InvalidOperationException ex) when (ConflictSql(ex)) { return Err<SupplierSettlementInstructionDto>("Settlement posting SQL conflict.", 409); }
    }

    public async Task<ApiResponse<SupplierSettlementInstructionDto>> RequestSettlementReversalAsync(Guid reference,
        Guid instructionReference, RequestSupplierSettlementReversal request, CancellationToken ct = default)
    {
        if (!CanMake) return Err<SupplierSettlementInstructionDto>("Finance reversal maker denied.", 403);
        if (reference == Guid.Empty || instructionReference == Guid.Empty || request == null ||
            request.ClientRequestId == Guid.Empty || request.AccountingPeriodId <= 0 ||
            request.ReversalDate == default || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length is < 10 or > 800)
            return Err<SupplierSettlementInstructionDto>("Reversal key, date, open period and documented reason required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (invoice, _, payableId, error) = await SettlementSourceAsync(reference, ct);
            if (error != null || invoice == null)
                return Err<SupplierSettlementInstructionDto>(error ?? "Payable unavailable.", 409);
            var source = await db.Journals.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == instructionReference && x.SourceType == SettlementSource &&
                x.SourceId == invoice.Id && x.CampusId == invoice.CampusId && !x.IsDeleted, ct);
            if (source == null) return Err<SupplierSettlementInstructionDto>("Settlement not found.", 404);
            var replay = await db.Journals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.IdempotencyKey == request.ClientRequestId, ct);
            if (replay != null)
                return SameReversalCommand(replay, source, invoice, request, user.UserId)
                    ? ApiResponse<SupplierSettlementInstructionDto>.SuccessResponse(
                        await SettlementMapAsync(source, ct), "Existing reversal request returned.")
                    : Err<SupplierSettlementInstructionDto>("Reversal request key belongs to another command.", 409);
            if (source.State != JournalState.Posted || source.PostedAt == null ||
                source.PostedByUserId is null or <= 0 || source.ApprovedByUserId is null or <= 0 ||
                !Version(source.RowVersion, request.OriginalRowVersion))
                return Err<SupplierSettlementInstructionDto>("Only a posted, unchanged settlement may be reversed.", 409);
            var businessDay = await SettlementLocalDayAsync(invoice.CampusId, ct);
            if (!businessDay.HasValue)
                return Err<SupplierSettlementInstructionDto>("Institution/campus business time zone unavailable.", 409);
            if (request.ReversalDate < source.JournalDate ||
                request.ReversalDate > businessDay.Value ||
                !await db.AccountingPeriods.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                    x.Id == request.AccountingPeriodId && !x.IsDeleted && !x.IsClosed &&
                    x.StartDate <= request.ReversalDate && x.EndDate >= request.ReversalDate, ct))
                return Err<SupplierSettlementInstructionDto>("Reversal needs an open period no earlier than the payment.", 409);
            if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.ReversalOfJournalId == source.Id &&
                (x.State == JournalState.Posted || (!x.IsDeleted && x.State != JournalState.Rejected &&
                    x.State != JournalState.Cancelled)), ct))
                return Err<SupplierSettlementInstructionDto>("A posted or pending reversal already exists.", 409);
            var (lines, linesError) = await CheckedSettlementLinesAsync(source, payableId, invoice.CampusId, ct);
            if (linesError != null) return Err<SupplierSettlementInstructionDto>(linesError, 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var id = Guid.NewGuid();
            var reversal = new Journal
            {
                TenantId = user.TenantId, PublicId = id, IdempotencyKey = request.ClientRequestId,
                JournalNumber = "PSR-" + id.ToString("N").ToUpperInvariant(),
                JournalDate = request.ReversalDate, AccountingPeriodId = request.AccountingPeriodId,
                CampusId = invoice.CampusId, CurrencyCode = "BDT",
                SourceType = SettlementReversalSource, SourceId = source.Id, ReversalOfJournalId = source.Id,
                ReferenceNumber = source.ReferenceNumber, Description = request.Reason.Trim(),
                State = JournalState.Submitted, CreatedBy = user.UserId, CreatedAt = now
            };
            db.Journals.Add(reversal);
            await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(lines.Select(x => new JournalLine
            {
                TenantId = user.TenantId, JournalId = reversal.Id, LineNo = x.LineNo,
                AccountId = x.AccountId, CampusId = x.CampusId,
                DebitAmount = x.CreditAmount, CreditAmount = x.DebitAmount,
                CreatedBy = user.UserId, CreatedAt = now,
                Description = "Supplier settlement correction; original posted bank evidence preserved"
            }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<SupplierSettlementInstructionDto>.SuccessResponse(
                await SettlementMapAsync(source, ct), "Full-value GL contra submitted to an independent reviewer; bank refund not initiated.");
        }
        catch (DbUpdateConcurrencyException) { return Err<SupplierSettlementInstructionDto>("Concurrent reversal request conflict.", 409); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Settlement reversal request conflict"); return Err<SupplierSettlementInstructionDto>("Reversal request conflict.", 409); }
        catch (SqlException ex) when (ConflictSql(ex)) { return Err<SupplierSettlementInstructionDto>("Reversal request SQL conflict.", 409); }
        catch (InvalidOperationException ex) when (ConflictSql(ex)) { return Err<SupplierSettlementInstructionDto>("Reversal request SQL conflict.", 409); }
    }

    public async Task<ApiResponse<SupplierSettlementInstructionDto>> ReviewSettlementReversalAsync(Guid reference,
        Guid instructionReference, Guid reversalReference, ReviewSupplierSettlementReversal request, CancellationToken ct = default)
    {
        if (!CanReview) return Err<SupplierSettlementInstructionDto>("Independent Finance reversal approval denied.", 403);
        if (reference == Guid.Empty || instructionReference == Guid.Empty || reversalReference == Guid.Empty ||
            request == null || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 300)
            return Err<SupplierSettlementInstructionDto>("Reversal version, decision and reason (10–300) required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (invoice, _, payableId, error) = await SettlementSourceAsync(reference, ct);
            if (error != null || invoice == null)
                return Err<SupplierSettlementInstructionDto>(error ?? "Payable unavailable.", 409);
            var source = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == instructionReference && x.SourceType == SettlementSource &&
                x.SourceId == invoice.Id && x.CampusId == invoice.CampusId && !x.IsDeleted, ct);
            if (source == null || source.State != JournalState.Posted)
                return Err<SupplierSettlementInstructionDto>("Posted settlement missing.", 409);
            var reversal = await db.Journals.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == reversalReference && x.SourceType == SettlementReversalSource &&
                x.SourceId == source.Id && x.ReversalOfJournalId == source.Id &&
                x.CampusId == invoice.CampusId && !x.IsDeleted, ct);
            if (reversal == null) return Err<SupplierSettlementInstructionDto>("Reversal instruction missing.", 404);
            if (reversal.State != JournalState.Submitted || !Version(reversal.RowVersion, request.RowVersion))
                return Err<SupplierSettlementInstructionDto>("Reversal changed or already reviewed; reload.", 409);
            if (reversal.CreatedBy is null or <= 0 || reversal.CreatedBy == user.UserId ||
                source.CreatedBy == user.UserId || invoice.CreatedBy == user.UserId ||
                invoice.SubmittedByUserId == user.UserId)
                return Err<SupplierSettlementInstructionDto>("Independent reversal reviewer required.", 403);
            if (request.Approve)
            {
                if (!await db.AccountingPeriods.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                    x.Id == reversal.AccountingPeriodId && !x.IsDeleted && !x.IsClosed &&
                    x.StartDate <= reversal.JournalDate && x.EndDate >= reversal.JournalDate, ct))
                    return Err<SupplierSettlementInstructionDto>("Reversal accounting period closed.", 409);
                var (sourceLines, linesError) = await CheckedSettlementLinesAsync(source, payableId, invoice.CampusId, ct);
                if (linesError != null) return Err<SupplierSettlementInstructionDto>(linesError, 409);
                var contra = await db.JournalLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.JournalId == reversal.Id)
                    .OrderBy(x => x.LineNo).Take(3).ToListAsync(ct);
                if (contra.Count != 2 || contra.Any(x => x.IsDeleted) ||
                    sourceLines.Where((x, i) => contra[i].LineNo != x.LineNo ||
                        contra[i].AccountId != x.AccountId || contra[i].CampusId != x.CampusId ||
                        contra[i].DebitAmount != x.CreditAmount || contra[i].CreditAmount != x.DebitAmount).Any())
                    return Err<SupplierSettlementInstructionDto>("Reversal must exactly mirror the posted GL lines.", 409);
                if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                    x.Id != reversal.Id && x.ReversalOfJournalId == source.Id && x.State == JournalState.Posted, ct))
                    return Err<SupplierSettlementInstructionDto>("Settlement already reversed.", 409);
            }
            var note = ReversalDecisionMarker + request.Reason.Trim();
            if ((reversal.Description ?? "").Length + note.Length > 1000)
                return Err<SupplierSettlementInstructionDto>("Reversal decision exceeds journal audit capacity.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            reversal.Description = (reversal.Description ?? "") + note;
            reversal.State = request.Approve ? JournalState.Posted : JournalState.Rejected;
            reversal.ApprovedByUserId = request.Approve ? user.UserId : null;
            reversal.ApprovedAt = request.Approve ? now : null;
            reversal.PostedByUserId = request.Approve ? user.UserId : null;
            reversal.PostedAt = request.Approve ? now : null;
            reversal.UpdatedAt = now; reversal.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<SupplierSettlementInstructionDto>.SuccessResponse(
                await SettlementMapAsync(source, ct), request.Approve
                    ? "Compensating Finance GL journal posted; original bank confirmation retained."
                    : "Reversal rejected; the original GL settlement remains posted.");
        }
        catch (DbUpdateConcurrencyException) { return Err<SupplierSettlementInstructionDto>("Concurrent reversal review conflict.", 409); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Settlement reversal review conflict"); return Err<SupplierSettlementInstructionDto>("Reversal review conflict.", 409); }
        catch (SqlException ex) when (ConflictSql(ex)) { return Err<SupplierSettlementInstructionDto>("Reversal review SQL conflict.", 409); }
        catch (InvalidOperationException ex) when (ConflictSql(ex)) { return Err<SupplierSettlementInstructionDto>("Reversal review SQL conflict.", 409); }
    }
}
