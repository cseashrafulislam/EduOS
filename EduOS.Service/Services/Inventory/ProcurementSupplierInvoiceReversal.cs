using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Enums.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class ProcurementSupplierInvoiceService
{
    private async Task<(Journal? Journal, List<JournalLine> Lines, string? Error)> OriginalPayableAsync(
        ProcurementSupplierInvoice invoice, CancellationToken ct)
    {
        if (invoice.StateCode != "Posted" || !invoice.JournalId.HasValue)
            return (null, [], "Only posted supplier payables can be reversed.");
        var original = await db.Journals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == invoice.JournalId.Value,ct);
        if (original == null || original.IsDeleted || original.State != JournalState.Posted ||
            original.SourceType != "ProcurementSupplierInvoice" || original.SourceId != invoice.Id ||
            original.ReversalOfJournalId.HasValue || original.CampusId != invoice.CampusId ||
            original.CurrencyCode != "BDT" || original.JournalDate != invoice.InvoiceDate)
            return (original, [], "Original posted payable journal has inconsistent provenance.");
        var lines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.JournalId == original.Id)
            .OrderBy(x => x.LineNo).Take(101).ToListAsync(ct);
        if (lines.Count is < 2 or > 100 || lines.Any(x => x.IsDeleted || x.CampusId != invoice.CampusId ||
            !Money(x.DebitAmount) || !Money(x.CreditAmount) ||
            (x.DebitAmount > 0m) == (x.CreditAmount > 0m)) ||
            lines.Select((x,i) => x.LineNo == i+1).Any(x=>!x) ||
            lines.Sum(x => x.DebitAmount) <= 0m || lines.Sum(x => x.DebitAmount) != lines.Sum(x => x.CreditAmount) ||
            lines.Sum(x => x.CreditAmount) < invoice.TotalAmount)
            return (original, lines, "Posted payable journal is missing or unbalanced.");
        if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.SourceType == "ProcurementSupplierPayment" && x.SourceId == invoice.Id &&
            (x.State == JournalState.Posted || (!x.IsDeleted && x.State != JournalState.Rejected &&
                x.State != JournalState.Cancelled)),ct))
            return (original, lines, "Linked supplier settlement exists; reverse payments first.");
        return (original, lines, null);
    }
    public async Task<ApiResponse<ProcurementSupplierInvoiceDto>> RequestReversalAsync(Guid reference,
        RequestProcurementSupplierInvoiceReversal request, CancellationToken ct = default)
    {
        if (!CanMake) return Err<ProcurementSupplierInvoiceDto>("Finance reversal maker access denied.",403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.AccountingPeriodId <= 0 || request.ReversalDate == default ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 800)
            return Err<ProcurementSupplierInvoiceDto>("Reversal key, date, period and documented reason required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var invoice = await db.ProcurementSupplierInvoices.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == reference && !x.IsDeleted,ct);
            if (invoice == null || !await CampusAllowedAsync(invoice.CampusId,ct))
                return Err<ProcurementSupplierInvoiceDto>("Supplier invoice not found.",404);
            var existing = await db.Journals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.IdempotencyKey == request.ClientRequestId,ct);
            if (existing != null)
            {
                if (existing.IsDeleted || existing.SourceType != "ProcurementSupplierInvoiceReversal" ||
                    existing.SourceId != invoice.Id || existing.ReversalOfJournalId != invoice.JournalId ||
                    existing.JournalDate != request.ReversalDate || existing.AccountingPeriodId != request.AccountingPeriodId ||
                    existing.Description != request.Reason.Trim())
                    return Err<ProcurementSupplierInvoiceDto>("Reversal key belongs to another request.",409);
                return ApiResponse<ProcurementSupplierInvoiceDto>.SuccessResponse(await MapAsync(invoice,ct),"Existing contra request returned.");
            }
            if (!Version(invoice.RowVersion,request.RowVersion)) return Err<ProcurementSupplierInvoiceDto>("Invoice changed; reload.",409);
            var (original, lines, error) = await OriginalPayableAsync(invoice,ct);
            if (error != null || original == null) return Err<ProcurementSupplierInvoiceDto>(error ?? "Missing payable.",409);
            if (request.ReversalDate < original.JournalDate ||
                request.ReversalDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                return Err<ProcurementSupplierInvoiceDto>("Reversal must not predate payable or be in the future.",409);
            if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.ReversalOfJournalId == original.Id && (x.State == JournalState.Posted ||
                    (!x.IsDeleted && x.State != JournalState.Rejected && x.State != JournalState.Cancelled)),ct))
                return Err<ProcurementSupplierInvoiceDto>("Another active payable reversal already exists.",409);
            if (!await db.AccountingPeriods.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.Id == request.AccountingPeriodId && !x.IsDeleted && !x.IsClosed &&
                x.StartDate <= request.ReversalDate && x.EndDate >= request.ReversalDate,ct))
                return Err<ProcurementSupplierInvoiceDto>("Open reversal period not found.",409);
            var now = clock.GetUtcNow().UtcDateTime;
            var jRef = Guid.NewGuid();
            var reversal = new Journal
            {
                TenantId = user.TenantId, PublicId = jRef, IdempotencyKey = request.ClientRequestId,
                JournalNumber = "PAR-" + jRef.ToString("N").ToUpperInvariant(),
                JournalDate = request.ReversalDate, AccountingPeriodId = request.AccountingPeriodId,
                CampusId = invoice.CampusId, SourceType = "ProcurementSupplierInvoiceReversal",
                SourceId = invoice.Id, ReversalOfJournalId = original.Id, CurrencyCode = "BDT",
                ReferenceNumber = invoice.SupplierInvoiceNumber, Description = request.Reason.Trim(),
                State = JournalState.Submitted, CreatedAt = now, CreatedBy = user.UserId
            };
            db.Journals.Add(reversal); await db.SaveChangesAsync(ct);
            if (reversal.CreatedBy != user.UserId) reversal.CreatedBy = user.UserId;
            db.JournalLines.AddRange(lines.Select(x => new JournalLine
            {
                TenantId = user.TenantId, JournalId = reversal.Id, LineNo = x.LineNo,
                AccountId = x.AccountId, CampusId = x.CampusId, DebitAmount = x.CreditAmount,
                CreditAmount = x.DebitAmount, Description = x.Description,
                CreatedAt = now, CreatedBy = user.UserId
            }));
            invoice.ReversalJournalId = reversal.Id; invoice.UpdatedAt = now; invoice.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<ProcurementSupplierInvoiceDto>.SuccessResponse(await MapAsync(invoice,ct),
                "Unposted payable contra submitted for an independent Finance reviewer.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex,"Payable reversal request race"); return Err<ProcurementSupplierInvoiceDto>("Reversal request conflict.",409); }
        catch (SqlException ex) when (ConflictSql(ex)) { logger.LogWarning(ex,"Payable reversal request SQL race"); return Err<ProcurementSupplierInvoiceDto>("Reversal request contention.",409); }
        catch (InvalidOperationException ex) when (ConflictSql(ex)) { logger.LogWarning(ex,"Payable reversal request wrapped SQL race"); return Err<ProcurementSupplierInvoiceDto>("Reversal request contention.",409); }
    }
    public async Task<ApiResponse<ProcurementSupplierInvoiceDto>> ReviewReversalAsync(Guid reference,
        ReviewProcurementSupplierInvoiceReversal request, CancellationToken ct = default)
    {
        if (!CanReview) return Err<ProcurementSupplierInvoiceDto>("Finance reversal approval denied.",403);
        if (request == null || string.IsNullOrWhiteSpace(request.RowVersion) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 800)
            return Err<ProcurementSupplierInvoiceDto>("Reversal journal version and independent decision reason required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var invoice = await db.ProcurementSupplierInvoices.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == reference && !x.IsDeleted,ct);
            if (invoice == null || !await CampusAllowedAsync(invoice.CampusId,ct))
                return Err<ProcurementSupplierInvoiceDto>("Supplier invoice not found.",404);
            if (!invoice.ReversalJournalId.HasValue) return Err<ProcurementSupplierInvoiceDto>("No reversal pending.",409);
            var contra = await db.Journals.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.Id == invoice.ReversalJournalId.Value && !x.IsDeleted,ct);
            if (contra == null || contra.SourceType != "ProcurementSupplierInvoiceReversal" || contra.SourceId != invoice.Id)
                return Err<ProcurementSupplierInvoiceDto>("Reversal provenance missing.",409);
            if (contra.CreatedBy is null or <= 0 || contra.CreatedBy == user.UserId ||
                invoice.CreatedBy == user.UserId || invoice.SubmittedByUserId == user.UserId)
                return Err<ProcurementSupplierInvoiceDto>("Independent reversal reviewer required.",403);
            if (contra.State != JournalState.Submitted || !Version(contra.RowVersion,request.RowVersion))
                return Err<ProcurementSupplierInvoiceDto>("Contra changed or was already reviewed.",409);
            var (original, sourceLines, error) = await OriginalPayableAsync(invoice,ct);
            if (error != null || original == null || contra.ReversalOfJournalId != original.Id)
                return Err<ProcurementSupplierInvoiceDto>(error ?? "Original payable conflict.",409);
            if (request.Approve)
            {
                if (!await db.AccountingPeriods.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                    x.Id == contra.AccountingPeriodId && !x.IsDeleted && !x.IsClosed &&
                    x.StartDate <= contra.JournalDate && x.EndDate >= contra.JournalDate,ct))
                    return Err<ProcurementSupplierInvoiceDto>("Contra accounting period closed.",409);
                var reversals = await db.JournalLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.JournalId == contra.Id)
                    .OrderBy(x => x.LineNo).Take(101).ToListAsync(ct);
                if (reversals.Count != sourceLines.Count || reversals.Any(x => x.IsDeleted) ||
                    sourceLines.Where((line,i) => reversals[i].LineNo != line.LineNo ||
                        reversals[i].AccountId != line.AccountId || reversals[i].CampusId != line.CampusId ||
                        reversals[i].DebitAmount != line.CreditAmount ||
                        reversals[i].CreditAmount != line.DebitAmount).Any())
                    return Err<ProcurementSupplierInvoiceDto>("Contra lines fail the exact original journal mirror.",409);
            }
            var now = clock.GetUtcNow().UtcDateTime;
            contra.State = request.Approve ? JournalState.Posted : JournalState.Rejected;
            contra.ApprovedByUserId = request.Approve ? user.UserId : null;
            contra.ApprovedAt = request.Approve ? now : null;
            contra.PostedByUserId = request.Approve ? user.UserId : null;
            contra.PostedAt = request.Approve ? now : null;
            contra.UpdatedAt = now; contra.UpdatedBy = user.UserId;
            if (request.Approve)
            {
                invoice.StateCode = "Reversed";
            }
            else
            {
                // Retain the rejected contra as immutable Journal audit history while
                // unlocking a newly documented reversal request on this payable.
                invoice.ReversalJournalId = null;
            }
            invoice.UpdatedAt = now; invoice.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<ProcurementSupplierInvoiceDto>.SuccessResponse(await MapAsync(invoice,ct),
                request.Approve ? "Original payable retained; compensating GL contra posted." : "Contra rejected; original payable unchanged.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex,"Payable reversal review race"); return Err<ProcurementSupplierInvoiceDto>("Reversal review conflict.",409); }
        catch (SqlException ex) when (ConflictSql(ex)) { logger.LogWarning(ex,"Payable reversal review SQL race"); return Err<ProcurementSupplierInvoiceDto>("Concurrent reversal review conflict.",409); }
        catch (InvalidOperationException ex) when (ConflictSql(ex)) { logger.LogWarning(ex,"Payable reversal review wrapped SQL race"); return Err<ProcurementSupplierInvoiceDto>("Concurrent reversal review conflict.",409); }
    }
}
