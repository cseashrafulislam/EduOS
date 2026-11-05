using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Facilities;

public sealed partial class FacilityVendorServiceBillService
{
    private sealed class AgingSource
    {
        public long Id { get; set; }
        public long? JournalId { get; set; }
        public string InvoiceSource { get; set; } = "";
        public Guid Reference { get; set; }
        public long SupplierId { get; set; }
        public long CampusId { get; set; }
        public string Number { get; set; } = "";
        public DateOnly InvoiceDate { get; set; }
        public DateOnly DueDate { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// One read-only, tenant/campus-scoped stock + service payable aging.
    /// All heavy filters and global paging run in SQL. Only at most 50 rows
    /// are materialized; payment sums and contra lookups are batched by source.
    /// A historical as-of date uses posting/contra dates, never current balance.
    /// Totals explicitly describe the current page, not the entire tenant.
    /// </summary>
    public async Task<ApiResponse<FacilitySupplierAgingTotalsDto>> SupplierAgingAsync(
        DateOnly asOf, long? campusId, int page, int pageSize,
        CancellationToken ct = default)
    {
        if (!CanRead || asOf == default || asOf.Year is < 2000 or > 2100 ||
            campusId is <= 0 || page is < 1 or > 100000 ||
            pageSize is < 1 or > 50 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<FacilitySupplierAgingTotalsDto>("Invalid supplier aging scope.", 400);
        var sourceCampus = campusId ?? await DefaultCampusId(ct);
        if (sourceCampus <= 0 || !await LiveReader(sourceCampus, ct))
            return Error<FacilitySupplierAgingTotalsDto>("Inactive Finance role or campus denied.", 403);
        var global = await LiveGlobalGrant().AnyAsync(ct);
        var service = db.FacilityVendorServiceBills.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && !x.IsDeleted &&
                x.InvoiceDate <= asOf &&
                (x.StateCode == "Posted" || x.StateCode == "Reversed") &&
                x.JournalId.HasValue &&
                db.Journals.Any(j => j.TenantId == user.TenantId &&
                    j.Id == x.JournalId.Value && !j.IsDeleted &&
                    (j.State == JournalState.Posted || j.State == JournalState.Reversed) && j.JournalDate <= asOf) &&
                (global || db.UserCampusAccesses.Any(g =>
                    g.TenantId == user.TenantId && g.UserId == user.UserId &&
                    g.CampusId == x.CampusId && g.IsActive && !g.IsDeleted)))
            .Select(x => new AgingSource {
                Id = x.Id, JournalId = x.JournalId, InvoiceSource = "Service",
                Reference = x.PublicId, SupplierId = x.SupplierId, CampusId = x.CampusId,
                Number = x.SupplierInvoiceNumber, InvoiceDate = x.InvoiceDate,
                DueDate = x.DueDate, Amount = x.AmountBdt
            });
        var stock = db.ProcurementSupplierInvoices.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && !x.IsDeleted &&
                x.InvoiceDate <= asOf &&
                (x.StateCode == "Posted" || x.StateCode == "Reversed") &&
                x.JournalId.HasValue &&
                db.Journals.Any(j => j.TenantId == user.TenantId &&
                    j.Id == x.JournalId.Value && !j.IsDeleted &&
                    (j.State == JournalState.Posted || j.State == JournalState.Reversed) && j.JournalDate <= asOf) &&
                (global || db.UserCampusAccesses.Any(g =>
                    g.TenantId == user.TenantId && g.UserId == user.UserId &&
                    g.CampusId == x.CampusId && g.IsActive && !g.IsDeleted)))
            .Select(x => new AgingSource {
                Id = x.Id, JournalId = x.JournalId, InvoiceSource = "Stock",
                Reference = x.PublicId, SupplierId = x.SupplierId, CampusId = x.CampusId,
                Number = x.SupplierInvoiceNumber, InvoiceDate = x.InvoiceDate,
                DueDate = x.DueDate, Amount = x.TotalAmount
            });
        IQueryable<AgingSource> query = stock.Concat(service);
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        var count = await query.CountAsync(ct);
        var pageRows = await query.OrderBy(x => x.DueDate)
            .ThenBy(x => x.InvoiceSource).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        if (pageRows.Count == 0)
            return ApiResponse<FacilitySupplierAgingTotalsDto>.SuccessResponse(new() {
                AsOf = asOf,
                Page = new PagedResult<FacilitySupplierAgingRowDto> {
                    Page = page, PageSize = pageSize, TotalCount = count,
                    Items = new List<FacilitySupplierAgingRowDto>()
                }
            });
        var serviceIds = pageRows.Where(x => x.InvoiceSource == "Service")
            .Select(x => x.Id).ToArray();
        var stockIds = pageRows.Where(x => x.InvoiceSource == "Stock")
            .Select(x => x.Id).ToArray();
        var origJournalIds = pageRows.Where(x => x.JournalId.HasValue)
            .Select(x => x.JournalId!.Value).ToArray();
        // A valid JournalId FK does not prove which payable that journal
        // belongs to. Verify the source/campus/currency for every displayed
        // invoice before using its GL movements in management reports.
        var originals = await db.Journals.AsNoTracking().Where(j =>
            j.TenantId == user.TenantId && !j.IsDeleted &&
            origJournalIds.Contains(j.Id))
            .Select(j => new { j.Id, j.SourceType, j.SourceId,
                j.CampusId, j.CurrencyCode }).ToDictionaryAsync(j => j.Id, ct);
        foreach (var src in pageRows)
        {
            if (!src.JournalId.HasValue ||
                !originals.TryGetValue(src.JournalId.Value, out var original) ||
                original.SourceType != (src.InvoiceSource == "Service"
                    ? "FacilityVendorServiceBill" : "ProcurementSupplierInvoice") ||
                original.SourceId != src.Id || original.CampusId != src.CampusId ||
                original.CurrencyCode != "BDT")
                return Error<FacilitySupplierAgingTotalsDto>(
                    "Supplier payable journal source, campus or currency is inconsistent.", 409);
        }
        var invoiceReversals = await db.Journals.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && x.State == JournalState.Posted &&
            x.JournalDate <= asOf && x.ReversalOfJournalId.HasValue &&
            origJournalIds.Contains(x.ReversalOfJournalId.Value) &&
            (x.SourceType == "FacilityVendorServiceBillReversal" ||
             x.SourceType == "ProcurementSupplierInvoiceReversal"))
            .Select(x => new { x.ReversalOfJournalId, x.SourceId, x.SourceType,
                x.CampusId, x.CurrencyCode }).ToListAsync(ct);
        var reversedInvoices = invoiceReversals.Where(x => x.SourceId.HasValue &&
            x.ReversalOfJournalId.HasValue && x.CurrencyCode == "BDT")
            .Select(x => (JournalId: x.ReversalOfJournalId!.Value,
                InvoiceId: x.SourceId!.Value, x.SourceType, x.CampusId)).ToHashSet();
        // SQL first restricts to the selected page's invoice IDs. Posted contra
        // journals neutralize a payment only on/after their actual journal date.
        var payments = await (from j in db.Journals.AsNoTracking()
            join l in db.JournalLines.AsNoTracking()
                on new { j.TenantId, JournalId = j.Id }
                equals new { l.TenantId, l.JournalId }
            where j.TenantId == user.TenantId && !j.IsDeleted &&
                j.State == JournalState.Posted && j.JournalDate <= asOf &&
                ((j.SourceType == VendorPaymentSource && j.SourceId.HasValue &&
                  serviceIds.Contains(j.SourceId.Value)) ||
                 (j.SourceType == "ProcurementSupplierPayment" && j.SourceId.HasValue &&
                  stockIds.Contains(j.SourceId.Value))) &&
                l.TenantId == user.TenantId && !l.IsDeleted &&
                l.LineNo == 1 && l.DebitAmount > 0m && l.CreditAmount == 0m &&
                !db.Journals.Any(r => r.TenantId == user.TenantId && !r.IsDeleted &&
                    r.State == JournalState.Posted && r.JournalDate <= asOf &&
                    r.ReversalOfJournalId == j.Id &&
                    ((j.SourceType == VendorPaymentSource &&
                      r.SourceType == VendorPaymentReversalSource) ||
                     (j.SourceType == "ProcurementSupplierPayment" &&
                      r.SourceType == "ProcurementSupplierPaymentReversal")))
            group l by new { j.SourceType, j.SourceId } into g
            select new {
                g.Key.SourceType, g.Key.SourceId,
                Paid = g.Sum(x => x.DebitAmount)
            }).ToListAsync(ct);
        // A posted stock return may reduce the unpaid trade payable while
        // the already-paid part becomes a refund receivable. Only its
        // UnpaidCreditAmount reduces AP here; subtracting full CreditAmount
        // would double-count already executed bank payments.
        var stockCredits = await db.ProcurementSupplierReturnCredits.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                stockIds.Contains(x.SupplierInvoiceId) &&
                x.StateCode == "Posted" && x.ReturnDate <= asOf &&
                x.JournalId.HasValue &&
                db.Journals.Any(j => j.TenantId == user.TenantId &&
                    j.Id == x.JournalId.Value && j.SourceType == "ProcurementSupplierReturnCredit" &&
                    j.SourceId == x.Id && !j.IsDeleted &&
                    j.State == JournalState.Posted && j.JournalDate <= asOf))
            .GroupBy(x => x.SupplierInvoiceId)
            .Select(g => new { InvoiceId = g.Key,
                Amount = g.Sum(x => x.UnpaidCreditAmount) })
            .ToListAsync(ct);
        var credited = stockCredits.ToDictionary(x => x.InvoiceId, x => x.Amount);
        var paid = payments.ToDictionary(x =>
            (Source: x.SourceType == VendorPaymentSource ? "Service" : "Stock",
             Id: x.SourceId!.Value), x => x.Paid);
        var rows = new List<FacilitySupplierAgingRowDto>(pageRows.Count);
        foreach (var src in pageRows)
        {
            // Existing Finance contracts differ: the service-bill contra
            // references the original Journal.Id, while the stock-invoice
            // contra references SupplierInvoice.Id. Preserve both historic
            // source identities; never silently reinterpret posted records.
            var isReversed = src.JournalId.HasValue &&
                reversedInvoices.Contains((src.JournalId.Value,
                    src.InvoiceSource == "Service" ? src.JournalId.Value : src.Id,
                    src.InvoiceSource == "Service" ? "FacilityVendorServiceBillReversal" :
                        "ProcurementSupplierInvoiceReversal", (long?)src.CampusId));
            var paymentAmount = paid.TryGetValue((src.InvoiceSource, src.Id), out var amount)
                ? amount : 0m;
            var supplierCredit = src.InvoiceSource == "Stock" &&
                credited.TryGetValue(src.Id, out var credit) ? credit : 0m;
            var balance = isReversed ? 0m : src.Amount - paymentAmount - supplierCredit;
            if (balance < 0m || balance > src.Amount)
                return Error<FacilitySupplierAgingTotalsDto>(
                    "Supplier payment does not reconcile to its original payable.", 409);
            rows.Add(new FacilitySupplierAgingRowDto {
                InvoiceSource = src.InvoiceSource, InvoiceReference = src.Reference,
                SupplierId = src.SupplierId, CampusId = src.CampusId,
                SupplierInvoiceNumber = src.Number, InvoiceDate = src.InvoiceDate,
                DueDate = src.DueDate, InvoiceAmount = src.Amount,
                NetPostedPayments = isReversed ? 0m : paymentAmount,
                PostedSupplierCredits = isReversed ? 0m : supplierCredit,
                OpenBalance = balance, InvoiceReversedAsOf = isReversed,
                DaysOverdue = Math.Max(0, asOf.DayNumber - src.DueDate.DayNumber)
            });
        }
        var result = new FacilitySupplierAgingTotalsDto {
            AsOf = asOf,
            OutstandingOnPage = rows.Sum(x => x.OpenBalance),
            CurrentOnPage = rows.Where(x => x.DaysOverdue == 0).Sum(x => x.OpenBalance),
            Days1To30OnPage = rows.Where(x => x.DaysOverdue is >= 1 and <= 30).Sum(x => x.OpenBalance),
            Days31To60OnPage = rows.Where(x => x.DaysOverdue is >= 31 and <= 60).Sum(x => x.OpenBalance),
            Days61To90OnPage = rows.Where(x => x.DaysOverdue is >= 61 and <= 90).Sum(x => x.OpenBalance),
            Over90DaysOnPage = rows.Where(x => x.DaysOverdue > 90).Sum(x => x.OpenBalance),
            Page = new PagedResult<FacilitySupplierAgingRowDto> {
                Page = page, PageSize = pageSize, TotalCount = count, Items = rows
            }
        };
        return ApiResponse<FacilitySupplierAgingTotalsDto>.SuccessResponse(result);
    }
}
