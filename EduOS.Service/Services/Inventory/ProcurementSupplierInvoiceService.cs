using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

/// <summary>Supplier invoice intake and Finance posting against immutable, posted procurement stock evidence.</summary>
public sealed partial class ProcurementSupplierInvoiceService : IProcurementSupplierInvoiceService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly IInventoryMovementService inventory;
    private readonly TimeProvider clock;
    private readonly ILogger<ProcurementSupplierInvoiceService> logger;
    public ProcurementSupplierInvoiceService(EduOSDbContext db, ICurrentUserService user,
        IInventoryMovementService inventory, TimeProvider clock, ILogger<ProcurementSupplierInvoiceService> logger)
    { this.db = db; this.user = user; this.inventory = inventory; this.clock = clock; this.logger = logger; }
    private bool CanRead => user.IsAuthenticated && user.TenantId > 0 && db.CurrentTenantId == user.TenantId &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("Accountant"));
    private bool CanMake => CanRead && (user.IsTenantAdmin || user.IsInRole("Accountant"));
    private bool CanReview => CanRead;
    private IQueryable<ProcurementSupplierInvoice> Visible() => db.ProcurementSupplierInvoices.AsNoTracking().Where(x =>
        x.TenantId == user.TenantId && !x.IsDeleted && (user.IsTenantAdmin || user.IsInRole("Principal") ||
        db.UserCampusAccesses.Any(a => a.TenantId == user.TenantId && a.UserId == user.UserId &&
            a.CampusId == x.CampusId && a.IsActive && !a.IsDeleted)));
    private async Task<bool> CampusAllowedAsync(long campus, CancellationToken ct) =>
        await db.Campuses.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId && x.Id == campus &&
            x.IsActive && !x.IsDeleted, ct) && (user.IsTenantAdmin || user.IsInRole("Principal") ||
        await db.UserCampusAccesses.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.UserId == user.UserId && x.CampusId == campus && x.IsActive && !x.IsDeleted, ct));
    private static ApiResponse<T> Err<T>(string message, int code = 400) => ApiResponse<T>.ErrorResponse(message, code);
    private static bool Quantity(decimal x) => x > 0m && x <= 999999999999m && decimal.Round(x, 6) == x;
    private static bool Money(decimal x) => x >= 0m && x <= 999999999999.99m && decimal.Round(x, 2) == x;
    private static bool Version(byte[] value, string? version)
    { try { return !string.IsNullOrWhiteSpace(version) && value.AsSpan().SequenceEqual(Convert.FromBase64String(version)); }
      catch (FormatException) { return false; } }
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static string InvoiceCreateHash(CreateProcurementSupplierInvoiceRequest request, string invoiceNo, string reason) => Hash(new
    {
        request.PurchaseOrderId, Number = invoiceNo, request.InvoiceDate, request.DueDate,
        request.PriceMode, Reason = reason, Lines = request.Lines.OrderBy(x => x.GoodsReceiptReference)
            .ThenBy(x => x.ReceiptLineNo).Select(x => new { x.GoodsReceiptReference, x.ReceiptLineNo,
                x.Quantity, x.Amount, Reason = x.VarianceReason?.Trim() ?? "" }).ToArray()
    });
    private static Guid PostingKey(long tenantId, Guid reference, string kind)
    { var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("EduOS:Procurement:" + kind + ":" + tenantId + ":" + reference.ToString("N")));
      return new Guid(bytes.AsSpan(0, 16)); }
    private static bool ConflictSql(Exception ex)
    {
        for (Exception? x = ex; x != null; x = x.InnerException)
            if (x is SqlException sql && sql.Number is 1205 or 1222 or 2601 or 2627 or 3960 or 51000 or 51130 or 51131)
                return true;
        return false;
    }

    private async Task<ProcurementSupplierInvoiceDto> MapAsync(ProcurementSupplierInvoice invoice, CancellationToken ct)
    {
        var lines = await db.ProcurementSupplierInvoiceLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.SupplierInvoiceId == invoice.Id && !x.IsDeleted).OrderBy(x => x.LineNo).ToListAsync(ct);
        var orderNumber = await db.ProcurementPurchaseOrders.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.Id == invoice.PurchaseOrderId && !x.IsDeleted).Select(x => x.OrderNumber).FirstOrDefaultAsync(ct) ?? "";
        var refs = await db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
            (x.Id == invoice.JournalId || x.Id == invoice.ReversalJournalId))
            .Select(x => new { x.Id, x.PublicId, x.RowVersion }).ToListAsync(ct);
        return new ProcurementSupplierInvoiceDto
        {
            Reference = invoice.PublicId, PurchaseOrderId = invoice.PurchaseOrderId, OrderNumber = orderNumber,
            CampusId = invoice.CampusId, SupplierId = invoice.SupplierId,
            SupplierInvoiceNumber = invoice.SupplierInvoiceNumber, InvoiceDate = invoice.InvoiceDate,
            DueDate = invoice.DueDate, StateCode = invoice.StateCode, PriceMode = invoice.PriceMode,
            Reason = invoice.Reason, TotalAmount = invoice.TotalAmount, StockValue = invoice.StockValue,
            PriceVariance = invoice.PriceVariance, CreatedByUserId = invoice.CreatedBy,
            SubmittedAtUtc = invoice.SubmittedAtUtc, ReviewedAtUtc = invoice.ReviewedAtUtc,
            ReviewedByUserId = invoice.ReviewedByUserId, ReviewReason = invoice.ReviewReason,
            JournalReference = refs.FirstOrDefault(x => x.Id == invoice.JournalId)?.PublicId,
            ReversalJournalReference = refs.FirstOrDefault(x => x.Id == invoice.ReversalJournalId)?.PublicId,
            ReversalJournalRowVersion = refs.FirstOrDefault(x => x.Id == invoice.ReversalJournalId) is { } reversed
                ? Convert.ToBase64String(reversed.RowVersion) : null,
            RowVersion = Convert.ToBase64String(invoice.RowVersion),
            Lines = lines.Select(x => new ProcurementSupplierInvoiceLineDto
            {
                LineNo = x.LineNo, InventoryItemId = x.InventoryItemId, PurchaseOrderLineId = x.PurchaseOrderLineId,
                GoodsReceiptLineId = x.GoodsReceiptLineId, LotOrBatchNo = x.LotOrBatchNo,
                Quantity = x.Quantity, ReceivedQuantity = x.ReceivedQuantitySnapshot,
                PoUnitPrice = x.PoUnitPriceSnapshot, StockUnitCost = x.StockUnitCostSnapshot,
                InvoiceUnitPrice = x.InvoiceUnitPrice, InvoiceAmount = x.InvoiceAmount,
                StockValue = x.StockValue, VarianceReason = x.VarianceReason
            }).ToList()
        };
    }
    public async Task<ApiResponse<PagedResult<ProcurementSupplierInvoiceDto>>> ListAsync(
        long? purchaseOrderId, string? state, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead) return Err<PagedResult<ProcurementSupplierInvoiceDto>>("Invoice access denied.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            purchaseOrderId is <= 0 || (!string.IsNullOrWhiteSpace(state) &&
            state is not ("Draft" or "Submitted" or "Posted" or "Rejected" or "Reversed")))
            return Err<PagedResult<ProcurementSupplierInvoiceDto>>("Invalid invoice paging or filters.");
        var query = Visible();
        if (purchaseOrderId.HasValue) query = query.Where(x => x.PurchaseOrderId == purchaseOrderId.Value);
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(x => x.StateCode == state);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = new List<ProcurementSupplierInvoiceDto>();
        foreach (var row in rows) items.Add(await MapAsync(row, ct));
        return ApiResponse<PagedResult<ProcurementSupplierInvoiceDto>>.SuccessResponse(new PagedResult<ProcurementSupplierInvoiceDto>
        { Items = items, TotalCount = count, Page = page, PageSize = pageSize });
    }
    public async Task<ApiResponse<ProcurementSupplierInvoiceDto>> GetAsync(Guid reference, CancellationToken ct = default)
    {
        if (!CanRead) return Err<ProcurementSupplierInvoiceDto>("Invoice access denied.", 403);
        var row = await Visible().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
        return row == null ? Err<ProcurementSupplierInvoiceDto>("Supplier invoice not found.", 404) :
            ApiResponse<ProcurementSupplierInvoiceDto>.SuccessResponse(await MapAsync(row, ct));
    }
    public async Task<ApiResponse<ProcurementInvoicePostingOptionsDto>> PostingOptionsAsync(CancellationToken ct = default)
    {
        if (!CanReview) return Err<ProcurementInvoicePostingOptionsDto>("Finance access denied.", 403);
        var periods = await db.AccountingPeriods.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && !x.IsClosed).OrderByDescending(x => x.EndDate).Take(501).ToListAsync(ct);
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT")
            .OrderBy(x => x.Code).Take(1001).ToListAsync(ct);
        if (periods.Count > 500 || accounts.Count > 1000)
            return Err<ProcurementInvoicePostingOptionsDto>("Narrow the accounting selector; too many active choices.", 409);
        return ApiResponse<ProcurementInvoicePostingOptionsDto>.SuccessResponse(new ProcurementInvoicePostingOptionsDto
        {
            Periods = periods.Select(x => new ProcurementInvoicePeriodOptionDto
                { Id = x.Id, Name = x.Name, StartDate = x.StartDate, EndDate = x.EndDate }).ToList(),
            Accounts = accounts.Select(x => new ProcurementInvoiceAccountOptionDto
                { Id = x.Id, Code = x.Code, Name = x.Name, Type = x.AccountType.ToString() }).ToList()
        });
    }
}
