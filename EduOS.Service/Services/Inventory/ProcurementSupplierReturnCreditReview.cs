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

public sealed partial class ProcurementSupplierInvoiceService
{
    public async Task<ApiResponse<SupplierReturnCreditDto>> ReviewReturnCreditAsync(
        Guid invoiceReference, Guid returnReference, ReviewSupplierReturnCreditRequest request,
        CancellationToken ct = default)
    {
        if (!CanReview || !(user.IsTenantAdmin || user.IsInRole("Principal")))
            return Err<SupplierReturnCreditDto>("Independent Inventory and Finance reviewer denied.", 403);
        if (invoiceReference == Guid.Empty || returnReference == Guid.Empty || request == null ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 800)
            return Err<SupplierReturnCreditDto>("Review version, decision and reason required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var invoice = await Visible().FirstOrDefaultAsync(x => x.PublicId == invoiceReference, ct);
            if (invoice == null) return Err<SupplierReturnCreditDto>("Invoice not found.", 404);
            var credit = await db.ProcurementSupplierReturnCredits.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.SupplierInvoiceId == invoice.Id &&
                x.PublicId == returnReference && !x.IsDeleted, ct);
            if (credit == null) return Err<SupplierReturnCreditDto>("Return request not found.", 404);
            if (credit.StateCode != "Submitted" || !Version(credit.RowVersion, request.RowVersion) ||
                credit.CreatedBy is null or <= 0 || credit.CreatedBy == user.UserId ||
                invoice.CreatedBy == user.UserId || invoice.SubmittedByUserId == user.UserId)
                return Err<SupplierReturnCreditDto>("Changed source or same maker/checker prohibited.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            if (!request.Approve)
            {
                credit.StateCode = "Rejected";
            }
            else
            {
                if (invoice.StateCode != "Posted" || invoice.JournalId is null or <= 0 ||
                    invoice.ReversalJournalId.HasValue || credit.ReturnDate < invoice.InvoiceDate ||
                    credit.ReturnDate > DateOnly.FromDateTime(now) ||
                    !await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
                        x.TenantId == user.TenantId && x.Id == credit.AccountingPeriodId &&
                        !x.IsDeleted && !x.IsClosed && x.StartDate <= credit.ReturnDate &&
                        x.EndDate >= credit.ReturnDate, ct) ||
                    await PendingSupplierSettlementAsync(invoice.Id, ct))
                    return Err<SupplierReturnCreditDto>(
                        "Posted invoice/open period required; withdraw all pending payment instructions before crediting.", 409);
                var line = await db.ProcurementSupplierInvoiceLines.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Id == credit.SupplierInvoiceLineId &&
                    x.SupplierInvoiceId == invoice.Id && !x.IsDeleted, ct);
                if (line == null || credit.Quantity > line.Quantity ||
                    credit.StockValue != decimal.Round(line.StockUnitCostSnapshot * credit.Quantity, 2,
                        MidpointRounding.AwayFromZero))
                    return Err<SupplierReturnCreditDto>("Source quantity/valuation no longer matches.", 409);
                var postedQty = await db.ProcurementSupplierReturnCredits.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.Id != credit.Id &&
                    x.SupplierInvoiceLineId == line.Id && x.StateCode == "Posted" && !x.IsDeleted)
                    .SumAsync(x => (decimal?)x.Quantity, ct) ?? 0m;
                var priorCredits = await CreditedAsync(invoice.Id, false, credit.Id, ct);
                if (credit.Quantity > line.Quantity - postedQty ||
                    credit.CreditAmount > invoice.TotalAmount - priorCredits)
                    return Err<SupplierReturnCreditDto>("Another posted return already consumes this invoice or quantity.", 409);
                var (stockId, payableId, sourceError) = await ReturnGlSourceAsync(invoice, ct);
                if (sourceError != null || stockId != credit.StockAccountId ||
                    payableId != credit.PayableAccountId)
                    return Err<SupplierReturnCreditDto>("Original posted Finance mapping changed.", 409);
                var settled = await PostedSupplierPaymentsAsync(invoice.Id, payableId, ct);
                if (settled < 0m || settled > invoice.TotalAmount)
                    return Err<SupplierReturnCreditDto>("Supplier payment and earlier credits do not reconcile.", 409);
                var unpaid = Math.Min(credit.CreditAmount,
                    Math.Max(0m, invoice.TotalAmount - priorCredits - settled));
                var receivable = credit.CreditAmount - unpaid;
                var variance = credit.CreditAmount - credit.StockValue;
                var accountIds = new[] { credit.StockAccountId, credit.PayableAccountId,
                    variance == 0m ? 0L : credit.VarianceAccountId ?? 0L,
                    receivable == 0m ? 0L : credit.RefundReceivableAccountId ?? 0L }
                    .Where(x => x > 0).ToArray();
                if (accountIds.Distinct().Count() != accountIds.Length ||
                    (variance != 0m && credit.VarianceAccountId is null or <= 0) ||
                    (receivable != 0m && credit.RefundReceivableAccountId is null or <= 0))
                    return Err<SupplierReturnCreditDto>("Distinct variance and refund-receivable GL accounts required.", 409);
                var accounts = await db.Accounts.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && accountIds.Contains(x.Id) &&
                    !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting &&
                    x.CurrencyCode == "BDT").ToDictionaryAsync(x => x.Id, ct);
                if (accounts.Count != accountIds.Length ||
                    accounts[credit.StockAccountId].AccountType != AccountType.Asset ||
                    accounts[credit.PayableAccountId].AccountType != AccountType.Liability ||
                    (variance != 0m && accounts[credit.VarianceAccountId!.Value].AccountType != AccountType.Expense) ||
                    (receivable != 0m && accounts[credit.RefundReceivableAccountId!.Value].AccountType != AccountType.Asset))
                    return Err<SupplierReturnCreditDto>("Typed active BDT Finance ledger mapping required.", 409);
                var receiptLine = await db.ProcurementGoodsReceiptLines.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Id == line.GoodsReceiptLineId && !x.IsDeleted, ct);
                var receipt = receiptLine == null ? null : await db.ProcurementGoodsReceipts.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                        x.Id == receiptLine.GoodsReceiptId && x.StateCode == "Posted" &&
                        x.CampusId == invoice.CampusId && !x.IsDeleted, ct);
                if (receipt == null || receipt.PostedMovementId is null or <= 0 ||
                    await db.InventoryMovements.AsNoTracking().AnyAsync(x =>
                        x.TenantId == user.TenantId && !x.IsDeleted &&
                        x.ReversalOfMovementId == receipt.PostedMovementId && x.StateCode == "Posted", ct))
                    return Err<SupplierReturnCreditDto>("Canonical goods receipt no longer posted.", 409);
                var (movement, issueError) = await inventory.PostSupplierReturnInTransactionAsync(
                    credit, line, receipt, ct);
                if (movement == null || issueError != null)
                    return Err<SupplierReturnCreditDto>(issueError ?? "Stock return issue failed.", 409);
                var lines = new List<(long Id, decimal Debit, decimal Credit)>();
                if (unpaid > 0m) lines.Add((payableId, unpaid, 0m));
                if (receivable > 0m) lines.Add((credit.RefundReceivableAccountId!.Value, receivable, 0m));
                lines.Add((stockId, 0m, credit.StockValue));
                if (variance > 0m) lines.Add((credit.VarianceAccountId!.Value, 0m, variance));
                else if (variance < 0m) lines.Add((credit.VarianceAccountId!.Value, -variance, 0m));
                if (lines.Sum(x => x.Debit) != lines.Sum(x => x.Credit) ||
                    lines.Sum(x => x.Debit) <= 0m)
                    return Err<SupplierReturnCreditDto>("Credit-note GL does not balance.", 409);
                var jRef = Guid.NewGuid();
                var journal = new Journal
                {
                    TenantId = user.TenantId, PublicId = jRef, IdempotencyKey = credit.ClientRequestId,
                    JournalNumber = "SRC-" + jRef.ToString("N").ToUpperInvariant(),
                    JournalDate = credit.ReturnDate, AccountingPeriodId = credit.AccountingPeriodId,
                    CampusId = invoice.CampusId, CurrencyCode = "BDT",
                    SourceType = ReturnCreditSource, SourceId = credit.Id,
                    ReferenceNumber = credit.SupplierReturnReference,
                    Description = "Approved supplier credit on " + invoice.SupplierInvoiceNumber + ": " +
                        credit.Reason[..Math.Min(400, credit.Reason.Length)],
                    State = JournalState.Posted, CreatedBy = credit.CreatedBy, CreatedAt = now,
                    ApprovedByUserId = user.UserId, ApprovedAt = now,
                    PostedByUserId = user.UserId, PostedAt = now
                };
                db.Journals.Add(journal); await db.SaveChangesAsync(ct);
                db.JournalLines.AddRange(lines.Select((entry, index) => new JournalLine
                {
                    TenantId = user.TenantId, JournalId = journal.Id, LineNo = index + 1,
                    AccountId = entry.Id, CampusId = invoice.CampusId,
                    DebitAmount = entry.Debit, CreditAmount = entry.Credit,
                    Description = "Source-linked supplier return credit/refund receivable",
                    CreatedBy = user.UserId, CreatedAt = now
                }));
                credit.UnpaidCreditAmount = unpaid;
                credit.RefundReceivableAmount = receivable;
                credit.StockMovementId = movement.Id;
                credit.JournalId = journal.Id;
                credit.StateCode = "Posted";
            }
            credit.ApprovedByUserId = user.UserId;
            credit.ApprovedAtUtc = now; credit.ReviewReason = request.Reason.Trim();
            credit.UpdatedAt = now; credit.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<SupplierReturnCreditDto>.SuccessResponse(await ReturnDtoAsync(credit, ct),
                request.Approve ? "Supplier stock return and credit-note GL posted atomically; no bank refund issued."
                    : "Supplier credit rejected; stock and GL unchanged.");
        }
        catch (DbUpdateConcurrencyException) { return Err<SupplierReturnCreditDto>("Concurrent credit review.", 409); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Supplier credit GL conflict"); return Err<SupplierReturnCreditDto>("Credit GL/stock transaction rolled back.", 409); }
        catch (SqlException ex) when (ConflictSql(ex)) { return Err<SupplierReturnCreditDto>("Supplier credit SQL contention.", 409); }
    }
}
