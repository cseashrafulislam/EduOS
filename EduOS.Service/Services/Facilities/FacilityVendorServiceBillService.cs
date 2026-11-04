using System.Data;
using System.Security.Cryptography;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Facilities;

/// <summary>
/// Non-stock supplier service billing sourced only from independently verified
/// Facilities work evidence. All amounts are posted in the canonical Finance GL.
/// Does not create stock receipts, shadow payable balances or bank payments.
/// </summary>
public sealed partial class FacilityVendorServiceBillService(
    EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
    ILogger<FacilityVendorServiceBillService> logger)
{
    private bool ValidTenant => user.IsAuthenticated && user.UserId > 0 &&
        user.TenantId > 0 && db.CurrentTenantId == user.TenantId;
    private bool CanRead => ValidTenant && (user.IsTenantAdmin ||
        user.IsInRole("Principal") || user.IsInRole("Accountant"));
    private bool CanMake => CanRead && (user.IsTenantAdmin || user.IsInRole("Accountant"));
    private bool CanReview => CanRead && (user.IsTenantAdmin || user.IsInRole("Principal"));
    private bool AllCampuses => user.IsTenantAdmin || user.IsInRole("Principal");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ApiResponse<T> Error<T>(string message, int status = 409) =>
        ApiResponse<T>.ErrorResponse(message, status);
    private static bool Money(decimal amount) => amount is > 0m and <= 9999999999999999.99m &&
        decimal.Round(amount, 2) == amount;
    private static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static bool Plain(string text) => text.All(c => !char.IsControl(c));
    private static bool Version(byte[] original, string submitted)
    {
        try { return !string.IsNullOrWhiteSpace(submitted) &&
            original.AsSpan().SequenceEqual(Convert.FromBase64String(submitted)); }
        catch (FormatException) { return false; }
    }
    private static string Fingerprint(FacilityVendorBillReviewDto request)
    {
        var source = $"{request.Approve}|{request.AccountingPeriodId}|" +
            $"{request.RepairsExpenseAccountId}|{request.SupplierPayableAccountId}|" +
            request.Reason.Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }
    // Current DB role, not a stale Principal/TenantAdmin cookie, grants
    // institution-wide visibility. An Accountant still needs a campus grant.
    private IQueryable<bool> LiveGlobalGrant() =>
        db.UserRoles.Where(ur => ur.UserId == user.UserId &&
            db.Roles.IgnoreQueryFilters().Any(role =>
                role.Id == ur.RoleId && role.IsActive && !role.IsDeleted &&
                (role.TenantId == user.TenantId ||
                 (role.TenantId == null && role.IsSystemRole)) &&
                (role.NormalizedName == "TENANTADMIN" ||
                 role.NormalizedName == "PRINCIPAL"))).Select(ur => true);
    private IQueryable<FacilityVendorServiceBill> Visible() =>
        db.FacilityVendorServiceBills.AsNoTracking().Where(b =>
            b.TenantId == user.TenantId && !b.IsDeleted &&
            (LiveGlobalGrant().Any() || db.UserCampusAccesses.Any(a =>
                a.TenantId == user.TenantId && a.UserId == user.UserId &&
                a.CampusId == b.CampusId && a.IsActive && !a.IsDeleted)));
    private async Task<bool> CampusAllowed(long campusId, CancellationToken ct) =>
        await db.Campuses.AsNoTracking().AnyAsync(c => c.TenantId == user.TenantId &&
            c.Id == campusId && !c.IsDeleted && c.IsActive &&
            (LiveGlobalGrant().Any() || db.UserCampusAccesses.Any(a => a.TenantId == user.TenantId &&
                a.UserId == user.UserId && a.CampusId == campusId &&
                a.IsActive && !a.IsDeleted)), ct);

    /// <summary>
    /// Re-check and SQL-lock the live user, membership, scoped role and campus
    /// throughout the SERIALIZABLE bill or journal commit, not a stale cookie.
    /// </summary>
    private async Task<bool> LiveActor(long campusId, bool reviewer, CancellationToken ct)
    {
        if (!(reviewer ? CanReview : CanMake) || !await CampusAllowed(campusId, ct))
            return false;
        if (db.Database.IsSqlServer())
        {
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    IF NOT EXISTS (
                        SELECT 1 FROM dbo.AspNetUsers u WITH (UPDLOCK,HOLDLOCK)
                        JOIN dbo.TenantMemberships m WITH (UPDLOCK,HOLDLOCK)
                          ON m.UserId=u.Id AND m.TenantId={user.TenantId}
                        JOIN dbo.AspNetUserRoles ur WITH (UPDLOCK,HOLDLOCK) ON ur.UserId=u.Id
                        JOIN dbo.AspNetRoles r WITH (UPDLOCK,HOLDLOCK) ON r.Id=ur.RoleId
                        JOIN dbo.Campuses c WITH (UPDLOCK,HOLDLOCK)
                          ON c.Id={campusId} AND c.TenantId={user.TenantId}
                        WHERE u.Id={user.UserId} AND u.IsDeleted=0 AND u.IsActive=1
                          AND m.IsDeleted=0 AND m.Status={(int)MembershipStatus.Active}
                          AND r.IsDeleted=0 AND r.IsActive=1
                          AND (r.TenantId={user.TenantId}
                               OR (r.TenantId IS NULL AND r.IsSystemRole=1))
                          AND c.IsDeleted=0 AND c.IsActive=1
                          AND (({reviewer}=1 AND r.NormalizedName IN
                                 (N'TENANTADMIN',N'PRINCIPAL'))
                               OR ({reviewer}=0 AND r.NormalizedName IN
                                 (N'TENANTADMIN',N'ACCOUNTANT')))
                          AND (r.NormalizedName IN (N'TENANTADMIN',N'PRINCIPAL')
                               OR EXISTS (SELECT 1 FROM dbo.UserCampusAccesses g WITH (UPDLOCK,HOLDLOCK)
                                  WHERE g.TenantId={user.TenantId} AND g.CampusId=c.Id
                                  AND g.UserId=u.Id AND g.IsActive=1 AND g.IsDeleted=0))
                    ) THROW 53501, 'Service bill actor authorization changed', 1;
                    """, ct);
            }
            catch (SqlException ex) when (ex.Number is 53501 or 1205 or 1222)
            {
                logger.LogWarning(ex, "Facility service bill actor rejected for tenant {TenantId}", user.TenantId);
                return false;
            }
        }
        return await db.Users.AsNoTracking().AnyAsync(x => x.Id == user.UserId &&
                   x.IsActive && !x.IsDeleted, ct) &&
            await db.TenantMemberships.AsNoTracking().AnyAsync(x => x.UserId == user.UserId &&
                x.TenantId == user.TenantId && !x.IsDeleted &&
                x.Status == MembershipStatus.Active, ct) &&
            await db.UserRoles.AnyAsync(ur => ur.UserId == user.UserId &&
                db.Roles.IgnoreQueryFilters().Any(r => r.Id == ur.RoleId &&
                    r.IsActive && !r.IsDeleted &&
                    (r.TenantId == user.TenantId || (r.TenantId == null && r.IsSystemRole)) &&
                    (reviewer
                        ? r.NormalizedName == "TENANTADMIN" || r.NormalizedName == "PRINCIPAL"
                        : r.NormalizedName == "TENANTADMIN" || r.NormalizedName == "ACCOUNTANT")), ct) &&
            await CampusAllowed(campusId, ct);
    }

    /// <summary>
    /// A closed, approved vendor booking is not sufficient: it must have a
    /// verified completion AFTER the last technician completion/return.
    /// </summary>
    private async Task<(long CampusId, long SupplierId)?> VerifiedSource(
        long workOrderId, CancellationToken ct)
    {
        var source = await (from w in db.FacilityWorkOrders.AsNoTracking()
            join req in db.FacilityServiceRequests.AsNoTracking()
                on new { w.TenantId, Id = w.FacilityServiceRequestId }
                equals new { req.TenantId, req.Id }
            join supplier in db.ProcurementSuppliers.AsNoTracking()
                on new { w.TenantId, Id = w.VendorSupplierId!.Value }
                equals new { supplier.TenantId, supplier.Id }
            where w.Id == workOrderId && w.TenantId == user.TenantId && !w.IsDeleted &&
                !req.IsDeleted && !supplier.IsDeleted && supplier.StateCode == "Approved" &&
                supplier.CampusId == req.CampusId && req.StateCode == "Approved" &&
                w.StateCode == "Closed" && w.VendorStateCode == "Approved" &&
                w.VendorSupplierId != null && w.CompletedAtUtc != null && w.VerifiedAtUtc != null
            select new { req.CampusId, SupplierId = supplier.Id, RequestId = req.Id })
            .FirstOrDefaultAsync(ct);
        if (source == null || !await CampusAllowed(source.CampusId, ct)) return null;
        var events = db.FacilityWorkflowEvents.AsNoTracking().Where(e =>
            e.TenantId == user.TenantId && e.FacilityServiceRequestId == source.RequestId &&
            !e.IsDeleted);
        var lastAction = await events.Where(e => e.ActionCode == "vendorattest" ||
                e.ActionCode == "vendorverify" || e.ActionCode == "vendorreturn")
            .OrderByDescending(e => e.Id).Select(e => new { e.Id, e.ActionCode })
            .FirstOrDefaultAsync(ct);
        var lastCompletion = await events.Where(e => e.ActionCode == "complete")
            .OrderByDescending(e => e.Id).Select(e => e.Id).FirstOrDefaultAsync(ct);
        var lastReturn = await events.Where(e => e.ActionCode == "return")
            .OrderByDescending(e => e.Id).Select(e => e.Id).FirstOrDefaultAsync(ct);
        if (lastAction?.ActionCode != "vendorverify" ||
            lastCompletion == 0 || lastAction.Id <= lastCompletion ||
            lastCompletion <= lastReturn) return null;
        return (source.CampusId, source.SupplierId);
    }

    private async Task<FacilityVendorBillDto> Map(FacilityVendorServiceBill bill, CancellationToken ct)
    {
        var journal = bill.JournalId is long id
            ? await db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                x.Id == id && !x.IsDeleted).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync(ct)
            : null;
        var reversal = bill.JournalId is long originalId
            ? await db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                x.SourceType == "FacilityVendorServiceBillReversal" &&
                x.SourceId == originalId && !x.IsDeleted && x.State != JournalState.Rejected &&
                x.State != JournalState.Cancelled)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct) : null;
        return new FacilityVendorBillDto {
            Reference = bill.PublicId, FacilityWorkOrderId = bill.FacilityWorkOrderId,
            CampusId = bill.CampusId, SupplierId = bill.SupplierId,
            SupplierInvoiceNumber = bill.SupplierInvoiceNumber, InvoiceDate = bill.InvoiceDate,
            DueDate = bill.DueDate, AmountBdt = bill.AmountBdt, StateCode = bill.StateCode,
            JournalReference = journal, RowVersion = Convert.ToBase64String(bill.RowVersion),
            ReversalReference = reversal?.PublicId, ReversalState = reversal?.State.ToString() ?? "None",
            ReversalRowVersion = reversal == null ? null : Convert.ToBase64String(reversal.RowVersion),
            ReviewReason = bill.ReviewReason,
            PreparedByUserId = bill.PreparedByUserId, ReviewedByUserId = bill.ReviewedByUserId
        };
    }

    public async Task<ApiResponse<FacilityVendorBillDto>> GetAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!CanRead) return Error<FacilityVendorBillDto>("Finance access denied.", 403);
        var bill = await Visible().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
        if (bill == null || !await LiveReader(bill.CampusId, ct))
            return Error<FacilityVendorBillDto>("Service bill not found.", 404);
        return ApiResponse<FacilityVendorBillDto>.SuccessResponse(await Map(bill, ct));
    }

    private async Task<bool> LiveReader(long campusId, CancellationToken ct) =>
        await CampusAllowed(campusId, ct) &&
        await db.Users.AsNoTracking().AnyAsync(x => x.Id == user.UserId && x.IsActive &&
            !x.IsDeleted, ct) &&
        await db.TenantMemberships.AsNoTracking().AnyAsync(x => x.UserId == user.UserId &&
            x.TenantId == user.TenantId && x.Status == MembershipStatus.Active && !x.IsDeleted, ct) &&
        await db.UserRoles.AnyAsync(ur => ur.UserId == user.UserId &&
            db.Roles.IgnoreQueryFilters().Any(r => r.Id == ur.RoleId &&
                r.IsActive && !r.IsDeleted &&
                (r.TenantId == user.TenantId || (r.TenantId == null && r.IsSystemRole)) &&
                (r.NormalizedName == "ACCOUNTANT" || r.NormalizedName == "TENANTADMIN" ||
                 r.NormalizedName == "PRINCIPAL")), ct);

    public async Task<ApiResponse<PagedResult<FacilityVendorBillDto>>> ListAsync(
        long? campusId, string? state, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead || !await LiveReader(campusId ?? await DefaultCampusId(ct), ct))
            return Error<PagedResult<FacilityVendorBillDto>>("Finance access denied.", 403);
        if (campusId is <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50 ||
            state?.Length > 20)
            return Error<PagedResult<FacilityVendorBillDto>>("Invalid list filter.", 400);
        var query = Visible();
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId);
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(x => x.StateCode == state.Trim());
        var count = await query.CountAsync(ct);
        var bills = await query.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = new List<FacilityVendorBillDto>(bills.Count);
        foreach (var bill in bills) items.Add(await Map(bill, ct));
        return ApiResponse<PagedResult<FacilityVendorBillDto>>.SuccessResponse(new() {
            Page = page, PageSize = pageSize, TotalCount = count, Items = items
        });
    }

    // A campus-less Accountant may still read only currently granted campuses.
    private async Task<long> DefaultCampusId(CancellationToken ct) =>
        await LiveGlobalGrant().AnyAsync(ct)
            ? await db.Campuses.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                x.IsActive && !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync(ct)
            : await db.UserCampusAccesses.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                x.UserId == user.UserId && x.IsActive && !x.IsDeleted)
                .Select(x => x.CampusId).FirstOrDefaultAsync(ct);

    public async Task<ApiResponse<FacilityVendorBillDto>> CreateAsync(
        CreateFacilityVendorServiceBillDto request, CancellationToken ct = default)
    {
        if (!CanMake) return Error<FacilityVendorBillDto>("Finance maker access denied.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.FacilityWorkOrderId <= 0 || !Money(request.AmountBdt) ||
            request.InvoiceDate == default || request.DueDate < request.InvoiceDate ||
            request.DueDate == default || string.IsNullOrWhiteSpace(request.SupplierInvoiceNumber) ||
            request.SupplierInvoiceNumber.Trim().Length > 100 ||
            !Plain(request.SupplierInvoiceNumber.Trim()) ||
            Clean(request.Reason) is not string reason || reason.Length is < 10 or > 800 ||
            !Plain(reason))
            return Error<FacilityVendorBillDto>("Invalid invoice, amount, dates or reason.", 400);
        var number = request.SupplierInvoiceNumber.Trim();
        var normalized = number.ToUpperInvariant();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var prior = await db.FacilityVendorServiceBills.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (prior != null)
            {
                if (prior.PreparedByUserId != user.UserId ||
                    prior.FacilityWorkOrderId != request.FacilityWorkOrderId ||
                    prior.SupplierInvoiceNumber != number ||
                    prior.AmountBdt != request.AmountBdt ||
                    prior.InvoiceDate != request.InvoiceDate || prior.DueDate != request.DueDate ||
                    prior.Reason != reason)
                    return Error<FacilityVendorBillDto>("Request ID reused with changed bill.", 409);
                if (!await LiveActor(prior.CampusId, false, ct))
                    return Error<FacilityVendorBillDto>("Maker authorization revoked.", 403);
                return ApiResponse<FacilityVendorBillDto>.SuccessResponse(await Map(prior, ct));
            }
            var source = await VerifiedSource(request.FacilityWorkOrderId, ct);
            if (!source.HasValue || !await LiveActor(source.Value.CampusId, false, ct))
                return Error<FacilityVendorBillDto>("Verified vendor work not accessible.", 403);
            if (await db.FacilityVendorServiceBills.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && !x.IsDeleted &&
                (x.FacilityWorkOrderId == request.FacilityWorkOrderId ||
                 (x.SupplierId == source.Value.SupplierId &&
                  x.NormalizedSupplierInvoiceNumber == normalized)), ct))
                return Error<FacilityVendorBillDto>("Invoice or work order already billed.", 409);
            var now = Now;
            var bill = new FacilityVendorServiceBill {
                TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                FacilityWorkOrderId = request.FacilityWorkOrderId,
                CampusId = source.Value.CampusId, SupplierId = source.Value.SupplierId,
                SupplierInvoiceNumber = number, NormalizedSupplierInvoiceNumber = normalized,
                InvoiceDate = request.InvoiceDate, DueDate = request.DueDate,
                AmountBdt = request.AmountBdt, StateCode = "Draft",
                Reason = reason, PreparedByUserId = user.UserId,
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.FacilityVendorServiceBills.Add(bill);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<FacilityVendorBillDto>.SuccessResponse(await Map(bill, ct));
        }
        catch (Exception ex) when (Conflict(ex))
        {
            logger.LogWarning(ex, "Facility supplier invoice conflict tenant {TenantId}", user.TenantId);
            return Error<FacilityVendorBillDto>("Concurrent service invoice or authorization change.", 409);
        }
    }

    private static bool Conflict(Exception ex)
    {
        for (Exception? source = ex; source != null; source = source.InnerException)
            if (source is SqlException sql && sql.Number is 1205 or 1222 or 2601 or 2627 or 3960 or 53501)
                return true;
        return ex is DbUpdateConcurrencyException or DbUpdateException;
    }

    public async Task<ApiResponse<FacilityVendorBillDto>> SubmitAsync(
        Guid reference, FacilityVendorBillSubmitDto request, CancellationToken ct = default)
    {
        if (!CanMake) return Error<FacilityVendorBillDto>("Finance maker access denied.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion))
            return Error<FacilityVendorBillDto>("Command ID and rowversion required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var bill = await db.FacilityVendorServiceBills.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
            if (bill == null) return Error<FacilityVendorBillDto>("Service bill not found.", 404);
            if (bill.PreparedByUserId != user.UserId || !await LiveActor(bill.CampusId, false, ct))
                return Error<FacilityVendorBillDto>("Only the active bill maker may submit.", 403);
            if (bill.SubmitRequestId == request.ClientRequestId && bill.StateCode == "Submitted")
                return ApiResponse<FacilityVendorBillDto>.SuccessResponse(await Map(bill, ct));
            var source = await VerifiedSource(bill.FacilityWorkOrderId, ct);
            if (bill.StateCode != "Draft" ||
                !Version(bill.RowVersion, request.RowVersion) || !source.HasValue ||
                source.Value.SupplierId != bill.SupplierId ||
                source.Value.CampusId != bill.CampusId)
                return Error<FacilityVendorBillDto>("Bill changed or service proof expired.", 409);
            bill.SubmitRequestId = request.ClientRequestId;
            bill.StateCode = "Submitted"; bill.SubmittedAtUtc = Now;
            bill.UpdatedAt = Now; bill.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<FacilityVendorBillDto>.SuccessResponse(await Map(bill, ct));
        }
        catch (Exception ex) when (Conflict(ex))
        {
            logger.LogWarning(ex, "Facility service bill submit conflict tenant {TenantId}", user.TenantId);
            return Error<FacilityVendorBillDto>("Concurrent submit or authorization change.", 409);
        }
    }

    public async Task<ApiResponse<FacilityVendorBillDto>> ReviewAsync(
        Guid reference, FacilityVendorBillReviewDto request, CancellationToken ct = default)
    {
        if (!CanReview) return Error<FacilityVendorBillDto>("Independent reviewer access denied.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion) ||
            Clean(request.Reason) is not string reason || reason.Length is < 10 or > 800 ||
            !Plain(reason) || (request.Approve && (request.AccountingPeriodId <= 0 ||
            request.RepairsExpenseAccountId <= 0 || request.SupplierPayableAccountId <= 0 ||
            request.RepairsExpenseAccountId == request.SupplierPayableAccountId)))
            return Error<FacilityVendorBillDto>("Valid independent decision, mapping and reason required.", 400);
        var fingerprint = Fingerprint(request);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var bill = await db.FacilityVendorServiceBills.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
            if (bill == null) return Error<FacilityVendorBillDto>("Bill not found.", 404);
            if (bill.PreparedByUserId == user.UserId ||
                !await LiveActor(bill.CampusId, true, ct))
                return Error<FacilityVendorBillDto>("Independent, active reviewer required.", 403);
            if (bill.ReviewRequestId == request.ClientRequestId &&
                bill.ReviewFingerprint == fingerprint && bill.ReviewedByUserId == user.UserId &&
                bill.StateCode == (request.Approve ? "Posted" : "Rejected"))
                return ApiResponse<FacilityVendorBillDto>.SuccessResponse(await Map(bill, ct));
            if (bill.StateCode != "Submitted" || !Version(bill.RowVersion, request.RowVersion))
                return Error<FacilityVendorBillDto>("Bill changed, finalized, or request reused.", 409);
            if (request.Approve)
            {
                var source = await VerifiedSource(bill.FacilityWorkOrderId, ct);
                if (!source.HasValue || source.Value.CampusId != bill.CampusId ||
                    source.Value.SupplierId != bill.SupplierId)
                    return Error<FacilityVendorBillDto>("Service acceptance superseded; do not post.", 409);
                var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(p =>
                    p.Id == request.AccountingPeriodId && p.TenantId == user.TenantId &&
                    !p.IsDeleted && !p.IsClosed && p.StartDate <= bill.InvoiceDate &&
                    p.EndDate >= bill.InvoiceDate, ct);
                if (period == null)
                    return Error<FacilityVendorBillDto>("Open accounting period for invoice date required.", 409);
                var mapping = await db.Accounts.AsNoTracking().Where(a => a.TenantId == user.TenantId &&
                    (a.Id == request.RepairsExpenseAccountId ||
                     a.Id == request.SupplierPayableAccountId) &&
                    !a.IsDeleted && a.IsActive && !a.IsGroup && a.AllowsPosting &&
                    a.CurrencyCode == "BDT").Select(a => new { a.Id, a.AccountType })
                    .ToListAsync(ct);
                if (mapping.Count != 2 || !mapping.Any(a =>
                        a.Id == request.RepairsExpenseAccountId && a.AccountType == AccountType.Expense) ||
                    !mapping.Any(a =>
                        a.Id == request.SupplierPayableAccountId && a.AccountType == AccountType.Liability))
                    return Error<FacilityVendorBillDto>("Active BDT expense and payable accounts required.", 409);
                if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(j =>
                    j.TenantId == user.TenantId &&
                    ((j.SourceType == "FacilityVendorServiceBill" && j.SourceId == bill.Id) ||
                     j.IdempotencyKey == request.ClientRequestId), ct))
                    return Error<FacilityVendorBillDto>("Payable journal already exists. Reconcile.", 409);
                var now = Now;
                var journalReference = Guid.NewGuid();
                var journal = new Journal {
                    TenantId = user.TenantId, PublicId = journalReference,
                    IdempotencyKey = request.ClientRequestId,
                    JournalNumber = "FVB-" + journalReference.ToString("N").ToUpperInvariant(),
                    JournalDate = bill.InvoiceDate, AccountingPeriodId = period.Id,
                    CampusId = bill.CampusId, CurrencyCode = "BDT",
                    SourceType = "FacilityVendorServiceBill", SourceId = bill.Id,
                    ReferenceNumber = bill.SupplierInvoiceNumber,
                    Description = "Independently approved facility service " + bill.SupplierInvoiceNumber,
                    State = JournalState.Posted, ApprovedByUserId = user.UserId, ApprovedAt = now,
                    PostedByUserId = user.UserId, PostedAt = now,
                    CreatedBy = user.UserId, CreatedAt = now
                };
                db.Journals.Add(journal);
                await db.SaveChangesAsync(ct);
                db.JournalLines.AddRange(
                    new JournalLine {
                        TenantId = user.TenantId, JournalId = journal.Id, LineNo = 1,
                        CampusId = bill.CampusId, AccountId = request.RepairsExpenseAccountId,
                        DebitAmount = bill.AmountBdt, CreditAmount = 0m,
                        Description = "Facility service expense",
                        CreatedAt = now, CreatedBy = user.UserId
                    },
                    new JournalLine {
                        TenantId = user.TenantId, JournalId = journal.Id, LineNo = 2,
                        CampusId = bill.CampusId, AccountId = request.SupplierPayableAccountId,
                        DebitAmount = 0m, CreditAmount = bill.AmountBdt,
                        Description = "Facility vendor payable",
                        CreatedAt = now, CreatedBy = user.UserId
                    });
                bill.JournalId = journal.Id;
            }
            bill.StateCode = request.Approve ? "Posted" : "Rejected";
            bill.ReviewRequestId = request.ClientRequestId;
            bill.ReviewFingerprint = fingerprint;
            bill.ReviewedByUserId = user.UserId; bill.ReviewedAtUtc = Now;
            bill.ReviewReason = reason;
            bill.UpdatedAt = Now; bill.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<FacilityVendorBillDto>.SuccessResponse(await Map(bill, ct));
        }
        catch (Exception ex) when (Conflict(ex))
        {
            logger.LogWarning(ex, "Facility vendor GL posting conflict tenant {TenantId}", user.TenantId);
            return Error<FacilityVendorBillDto>("Concurrent Finance approval or authorization conflict.", 409);
        }
    }
}
