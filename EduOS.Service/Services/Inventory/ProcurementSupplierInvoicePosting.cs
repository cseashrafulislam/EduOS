using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

public sealed partial class ProcurementSupplierInvoiceService
{
    public async Task<ApiResponse<ProcurementSupplierInvoiceDto>> ReviewAsync(Guid reference,
        ReviewProcurementSupplierInvoiceRequest request, CancellationToken ct = default)
    {
        if (!CanReview) return Err<ProcurementSupplierInvoiceDto>("Independent Finance review denied.",403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.Decision is not ("Approve" or "Reject") ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 800 ||
            (request.Decision == "Approve" && (request.AccountingPeriodId <= 0 ||
                request.InventoryAccountId <= 0 || request.PayableAccountId <= 0 ||
                request.InventoryAccountId == request.PayableAccountId)))
            return Err<ProcurementSupplierInvoiceDto>("Decision, Finance account mapping, period and reason (10–800) required.");
        var reason = request.Reason.Trim();
        var hash = Hash(new { request.Decision, request.AccountingPeriodId, request.InventoryAccountId,
            request.PayableAccountId, request.PriceVarianceAccountId, Reason = reason });
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var invoice = await db.ProcurementSupplierInvoices.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == reference && !x.IsDeleted, ct);
            if (invoice == null || !await CampusAllowedAsync(invoice.CampusId, ct))
                return Err<ProcurementSupplierInvoiceDto>("Supplier invoice not found.",404);
            if (!invoice.CreatedBy.HasValue || invoice.CreatedBy == user.UserId || invoice.SubmittedByUserId == user.UserId)
                return Err<ProcurementSupplierInvoiceDto>("Maker and Finance reviewer must be independent.",403);
            if (invoice.ReviewRequestId == request.ClientRequestId && invoice.ReviewFingerprint == hash)
                return ApiResponse<ProcurementSupplierInvoiceDto>.SuccessResponse(await MapAsync(invoice,ct),"Existing Finance decision returned.");
            if (invoice.StateCode != "Submitted" || !Version(invoice.RowVersion, request.RowVersion))
                return Err<ProcurementSupplierInvoiceDto>("Invoice changed or already reviewed.",409);
            var now = clock.GetUtcNow().UtcDateTime;
            if (request.Decision == "Approve")
            {
                var lines = await db.ProcurementSupplierInvoiceLines.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.SupplierInvoiceId == invoice.Id && !x.IsDeleted).ToListAsync(ct);
                var error = await RevalidateAsync(invoice, lines, ct);
                if (error != null) return Err<ProcurementSupplierInvoiceDto>(error,409);
                if (!Money(invoice.TotalAmount) || !Money(invoice.StockValue) || invoice.TotalAmount <= 0m ||
                    invoice.StockValue <= 0m || invoice.PriceVariance != invoice.TotalAmount - invoice.StockValue)
                    return Err<ProcurementSupplierInvoiceDto>("Invoice valuation cannot be posted.",409);
                var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Id == request.AccountingPeriodId && !x.IsDeleted &&
                    !x.IsClosed && x.StartDate <= invoice.InvoiceDate && x.EndDate >= invoice.InvoiceDate,ct);
                if (period == null) return Err<ProcurementSupplierInvoiceDto>("Invoice date requires an open accounting period.",409);
                var accountIds = new[] { request.InventoryAccountId, request.PayableAccountId,
                    invoice.PriceVariance == 0m ? 0L : request.PriceVarianceAccountId }.Where(x => x > 0).Distinct().ToArray();
                if (accountIds.Length != (invoice.PriceVariance == 0m ? 2 : 3))
                    return Err<ProcurementSupplierInvoiceDto>("Distinct inventory, payable and price-variance accounts required.",409);
                var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                    accountIds.Contains(x.Id) && !x.IsDeleted && x.IsActive && !x.IsGroup &&
                    x.AllowsPosting && x.CurrencyCode == "BDT").ToDictionaryAsync(x => x.Id,ct);
                if (accounts.Count != accountIds.Length ||
                    accounts[request.InventoryAccountId].AccountType != AccountType.Asset ||
                    accounts[request.PayableAccountId].AccountType != AccountType.Liability ||
                    (invoice.PriceVariance != 0m && accounts[request.PriceVarianceAccountId].AccountType != AccountType.Expense))
                    return Err<ProcurementSupplierInvoiceDto>("Active BDT asset, liability and variance expense ledgers required.",409);
                if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && ((x.SourceType == "ProcurementSupplierInvoice" && x.SourceId == invoice.Id) ||
                    x.IdempotencyKey == PostingKey(user.TenantId, invoice.PublicId, "Payable")),ct))
                    return Err<ProcurementSupplierInvoiceDto>("A supplier invoice payable journal already exists; reconcile.",409);
                var debits = new List<JournalLine>
                {
                    new() { AccountId = request.InventoryAccountId, DebitAmount = invoice.StockValue,
                        Description = "Supplier delivery stock valuation" },
                    new() { AccountId = request.PayableAccountId, CreditAmount = invoice.TotalAmount,
                        Description = "Supplier invoice payable" }
                };
                if (invoice.PriceVariance > 0m)
                    debits.Add(new JournalLine { AccountId = request.PriceVarianceAccountId,
                        DebitAmount = invoice.PriceVariance, Description = "Supplier price variance" });
                else if (invoice.PriceVariance < 0m)
                    debits.Add(new JournalLine { AccountId = request.PriceVarianceAccountId,
                        CreditAmount = -invoice.PriceVariance, Description = "Supplier price variance" });
                if (debits.Sum(x => x.DebitAmount) != debits.Sum(x => x.CreditAmount) ||
                    debits.Sum(x => x.DebitAmount) <= 0m) return Err<ProcurementSupplierInvoiceDto>("Journal is not balanced.",409);
                var jRef = Guid.NewGuid();
                var journal = new Journal
                {
                    TenantId = user.TenantId, PublicId = jRef,
                    IdempotencyKey = PostingKey(user.TenantId, invoice.PublicId, "Payable"),
                    JournalNumber = "PAP-" + jRef.ToString("N").ToUpperInvariant(),
                    JournalDate = invoice.InvoiceDate, AccountingPeriodId = period.Id, CampusId = invoice.CampusId,
                    CurrencyCode = "BDT", SourceType = "ProcurementSupplierInvoice", SourceId = invoice.Id,
                    ReferenceNumber = invoice.SupplierInvoiceNumber, Description = "Approved supplier invoice " + invoice.SupplierInvoiceNumber,
                    State = JournalState.Posted, ApprovedByUserId = user.UserId, ApprovedAt = now,
                    PostedByUserId = user.UserId, PostedAt = now, CreatedBy = user.UserId, CreatedAt = now
                };
                db.Journals.Add(journal); await db.SaveChangesAsync(ct);
                for (var i = 0; i < debits.Count; i++)
                {
                    var line = debits[i]; line.TenantId = user.TenantId; line.JournalId = journal.Id;
                    line.CampusId = invoice.CampusId; line.LineNo = i + 1;
                    line.CreatedAt = now; line.CreatedBy = user.UserId;
                }
                db.JournalLines.AddRange(debits);
                invoice.JournalId = journal.Id;
            }
            invoice.StateCode = request.Decision == "Approve" ? "Posted" : "Rejected";
            invoice.ReviewRequestId = request.ClientRequestId;
            invoice.ReviewFingerprint = hash; invoice.ReviewReason = reason;
            invoice.ReviewedByUserId = user.UserId; invoice.ReviewedAtUtc = now;
            invoice.UpdatedAt = now; invoice.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<ProcurementSupplierInvoiceDto>.SuccessResponse(await MapAsync(invoice,ct),
                request.Decision == "Approve" ? "Supplier payable posted to canonical GL." : "Invoice rejected without GL mutation.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex,"Supplier invoice posting race"); return Err<ProcurementSupplierInvoiceDto>("Invoice Finance posting conflict.",409); }
        catch (SqlException ex) when (ConflictSql(ex)) { logger.LogWarning(ex,"Supplier invoice posting SQL race"); return Err<ProcurementSupplierInvoiceDto>("Concurrent invoice Finance posting conflict.",409); }
        catch (InvalidOperationException ex) when (ConflictSql(ex)) { logger.LogWarning(ex,"Supplier invoice posting wrapped race"); return Err<ProcurementSupplierInvoiceDto>("Concurrent invoice posting conflict.",409); }
    }
}
