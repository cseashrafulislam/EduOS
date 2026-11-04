using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Facilities;

/// <summary>
/// Allocation-only management report sourced from an already POSTED canonical
/// PayrollRun and independently closed work order. Does not create any GL lines.
/// </summary>
public sealed class FacilityLabourCostService(
    EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
    ILogger<FacilityLabourCostService> logger)
{
    private bool Tenant => user.IsAuthenticated && user.UserId > 0 &&
        user.TenantId > 0 && db.CurrentTenantId == user.TenantId;
    private bool Reader => Tenant && (user.IsTenantAdmin ||
        user.IsInRole("Principal") || user.IsInRole("Accountant"));
    private bool Maker => Reader && (user.IsTenantAdmin || user.IsInRole("Accountant"));
    private bool Reviewer => Reader && (user.IsTenantAdmin || user.IsInRole("Principal"));
    private bool AllCampuses => user.IsTenantAdmin || user.IsInRole("Principal");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ApiResponse<T> Error<T>(string message, int status = 409) =>
        ApiResponse<T>.ErrorResponse(message, status);
    private static bool Version(byte[] rowVersion, string supplied)
    {
        try { return !string.IsNullOrWhiteSpace(supplied) &&
            rowVersion.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static bool Plain(string s) => s.All(x => !char.IsControl(x));
    private static string? Clean(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private Task<bool> LiveGlobalRole(CancellationToken ct) =>
        db.UserRoles.AnyAsync(ur => ur.UserId == user.UserId &&
            db.Roles.IgnoreQueryFilters().Any(r => r.Id == ur.RoleId &&
                r.IsActive && !r.IsDeleted &&
                (r.TenantId == user.TenantId || (r.TenantId == null && r.IsSystemRole)) &&
                (r.NormalizedName == "TENANTADMIN" || r.NormalizedName == "PRINCIPAL")), ct);
    private Task<bool> LiveAccountantRole(CancellationToken ct) =>
        db.UserRoles.AnyAsync(ur => ur.UserId == user.UserId &&
            db.Roles.IgnoreQueryFilters().Any(r => r.Id == ur.RoleId &&
                r.IsActive && !r.IsDeleted &&
                (r.TenantId == user.TenantId || (r.TenantId == null && r.IsSystemRole)) &&
                r.NormalizedName == "ACCOUNTANT"), ct);
    private IQueryable<FacilityLabourCostAllocation> Visible() =>
        db.FacilityLabourCostAllocations.AsNoTracking().Where(a =>
            a.TenantId == user.TenantId && !a.IsDeleted &&
            (db.UserRoles.Any(ur => ur.UserId == user.UserId &&
                db.Roles.IgnoreQueryFilters().Any(r => r.Id == ur.RoleId &&
                    r.IsActive && !r.IsDeleted &&
                    (r.TenantId == user.TenantId || (r.TenantId == null && r.IsSystemRole)) &&
                    (r.NormalizedName == "TENANTADMIN" || r.NormalizedName == "PRINCIPAL"))) ||
             db.UserCampusAccesses.Any(g =>
                g.TenantId == user.TenantId && g.UserId == user.UserId &&
                g.CampusId == a.CampusId && g.IsActive && !g.IsDeleted)));
    private async Task<bool> ActiveActor(long campusId, bool reviewer, CancellationToken ct)
    {
        if (!(reviewer ? Reviewer : Maker)) return false;
        var global = await LiveGlobalRole(ct);
        if (!(reviewer ? global : global || await LiveAccountantRole(ct))) return false;
        return await db.Campuses.AsNoTracking().AnyAsync(c => c.Id == campusId &&
                c.TenantId == user.TenantId && !c.IsDeleted && c.IsActive &&
                (global || db.UserCampusAccesses.Any(a => a.UserId == user.UserId &&
                    a.TenantId == user.TenantId && a.CampusId == campusId &&
                    a.IsActive && !a.IsDeleted)), ct) &&
            await db.Users.AsNoTracking().AnyAsync(x => x.Id == user.UserId &&
                x.IsActive && !x.IsDeleted, ct) &&
            await db.TenantMemberships.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.UserId == user.UserId &&
                x.Status == MembershipStatus.Active && !x.IsDeleted, ct) &&
            await db.UserRoles.AnyAsync(ur => ur.UserId == user.UserId &&
                db.Roles.IgnoreQueryFilters().Any(r => r.Id == ur.RoleId &&
                    r.IsActive && !r.IsDeleted &&
                    (r.TenantId == user.TenantId || (r.TenantId == null && r.IsSystemRole)) &&
                    (reviewer
                        ? (r.NormalizedName == "TENANTADMIN" || r.NormalizedName == "PRINCIPAL")
                        : (r.NormalizedName == "TENANTADMIN" || r.NormalizedName == "ACCOUNTANT"))), ct);
    }
    private async Task<bool> ActiveReader(long campusId, CancellationToken ct)
    {
        if (!Reader) return false;
        var global = await LiveGlobalRole(ct);
        if (!global && !await LiveAccountantRole(ct)) return false;
        return await db.Campuses.AsNoTracking().AnyAsync(c => c.Id == campusId &&
                c.TenantId == user.TenantId && c.IsActive && !c.IsDeleted &&
                (global || db.UserCampusAccesses.Any(a =>
                    a.TenantId == user.TenantId && a.UserId == user.UserId &&
                    a.CampusId == campusId && a.IsActive && !a.IsDeleted)), ct) &&
            await db.Users.AsNoTracking().AnyAsync(x => x.Id == user.UserId &&
                x.IsActive && !x.IsDeleted, ct) &&
            await db.TenantMemberships.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.UserId == user.UserId &&
                x.Status == MembershipStatus.Active && !x.IsDeleted, ct) &&
            await db.UserRoles.AnyAsync(ur => ur.UserId == user.UserId &&
                db.Roles.IgnoreQueryFilters().Any(r => r.Id == ur.RoleId &&
                    r.IsActive && !r.IsDeleted &&
                    (r.TenantId == user.TenantId || (r.TenantId == null && r.IsSystemRole)) &&
                    (r.NormalizedName == "TENANTADMIN" || r.NormalizedName == "PRINCIPAL" ||
                     r.NormalizedName == "ACCOUNTANT")), ct);
    }
    private static bool Conflict(Exception ex)
    {
        for (Exception? x = ex; x != null; x = x.InnerException)
            if (x is SqlException sql && sql.Number is 1205 or 1222 or 2601 or 2627 or 3960)
                return true;
        return ex is DbUpdateException or DbUpdateConcurrencyException;
    }
    private async Task<bool> PayrollPosted(long runId, long? journalId, CancellationToken ct)
    {
        if (journalId is not > 0) return false;
        return await db.Journals.AsNoTracking().AnyAsync(j =>
                j.TenantId == user.TenantId && j.Id == journalId &&
                j.SourceType == "PayrollRun" && j.SourceId == runId &&
                j.State == JournalState.Posted && !j.IsDeleted, ct) &&
            !await db.Journals.AsNoTracking().AnyAsync(j =>
                j.TenantId == user.TenantId &&
                j.SourceType == "PayrollRunReversal" &&
                j.ReversalOfJournalId == journalId &&
                j.State == JournalState.Posted && !j.IsDeleted, ct);
    }
    private async Task<(long Campus, long PayrollRun, long Employee, decimal Gross, int StandardMinutes)?>
        Source(long orderId, long payrollEmployeeId, CancellationToken ct)
    {
        var record = await (from order in db.FacilityWorkOrders.AsNoTracking()
            join req in db.FacilityServiceRequests.AsNoTracking()
                on new { order.TenantId, Id = order.FacilityServiceRequestId }
                equals new { req.TenantId, req.Id }
            join pe in db.PayrollEmployees.AsNoTracking()
                on order.TenantId equals pe.TenantId
            join run in db.PayrollRuns.AsNoTracking()
                on new { pe.TenantId, Id = pe.PayrollRunId }
                equals new { run.TenantId, run.Id }
            join employee in db.Employees.AsNoTracking()
                on new { pe.TenantId, Id = pe.EmployeeId }
                equals new { employee.TenantId, employee.Id }
            where order.Id == orderId && order.TenantId == user.TenantId &&
                pe.Id == payrollEmployeeId && !order.IsDeleted && !req.IsDeleted &&
                !pe.IsDeleted && !run.IsDeleted && !employee.IsDeleted &&
                order.StateCode == "Closed" && order.VerifiedAtUtc != null &&
                order.StartedAtUtc != null && order.CompletedAtUtc != null &&
                employee.UserId == order.AssignedTechnicianUserId &&
                run.State == PayrollRunState.Posted &&
                (run.CampusId == null || run.CampusId == req.CampusId)
            select new { order, req.CampusId, pe, run, employee })
            .FirstOrDefaultAsync(ct);
        if (record == null || record.pe.GrossAmount <= 0m ||
            record.order.StartedAtUtc is not DateTime began ||
            record.order.CompletedAtUtc is not DateTime ended ||
            ended <= began || record.run.JournalId is not > 0 ||
            DateOnly.FromDateTime(began) < record.run.PeriodStart ||
            DateOnly.FromDateTime(ended) > record.run.PeriodEnd ||
            !await PayrollPosted(record.run.Id, record.run.JournalId, ct))
            return null;
        var key = "Facilities.PayrollStandardMinutes." +
            record.CampusId.ToString(CultureInfo.InvariantCulture);
        var setting = await db.TenantSettings.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.Key == key && !x.IsDeleted)
            .Select(x => new { x.Value, x.IsSensitive }).SingleOrDefaultAsync(ct);
        if (setting == null || setting.IsSensitive ||
            !int.TryParse(setting.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var standard) ||
            standard is < 60 or > 60000)
            return null;
        return (record.CampusId, record.run.Id, record.employee.Id,
            record.pe.GrossAmount, standard);
    }
    public async Task<ApiResponse<PagedResult<FacilityLabourSourceChoiceDto>>> EligibleSourcesAsync(
        long? campusId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Maker) return Error<PagedResult<FacilityLabourSourceChoiceDto>>("Finance maker denied.", 403);
        if (campusId is <= 0 || page is < 1 or > 10000 || pageSize is < 1 or > 20 ||
            search?.Length > 100)
            return Error<PagedResult<FacilityLabourSourceChoiceDto>>("Invalid labour source search.", 400);
        var query = from w in db.FacilityWorkOrders.AsNoTracking()
            join req in db.FacilityServiceRequests.AsNoTracking()
                on new { w.TenantId, Id = w.FacilityServiceRequestId }
                equals new { req.TenantId, req.Id }
            join emp in db.Employees.AsNoTracking()
                on new { w.TenantId, UserId = w.AssignedTechnicianUserId }
                equals new { emp.TenantId, emp.UserId }
            join pe in db.PayrollEmployees.AsNoTracking()
                on new { emp.TenantId, EmployeeId = emp.Id }
                equals new { pe.TenantId, pe.EmployeeId }
            join run in db.PayrollRuns.AsNoTracking()
                on new { pe.TenantId, Id = pe.PayrollRunId }
                equals new { run.TenantId, run.Id }
            where w.TenantId == user.TenantId && !w.IsDeleted && !req.IsDeleted &&
                !emp.IsDeleted && !pe.IsDeleted && !run.IsDeleted &&
                w.StateCode == "Closed" && w.VerifiedAtUtc != null &&
                w.StartedAtUtc != null && w.CompletedAtUtc != null &&
                run.State == PayrollRunState.Posted &&
                (run.CampusId == null || run.CampusId == req.CampusId) &&
                !db.FacilityLabourCostAllocations.Any(a => a.TenantId == user.TenantId &&
                    a.FacilityWorkOrderId == w.Id && a.PayrollEmployeeId == pe.Id &&
                    a.StateCode != "Rejected" && !a.IsDeleted)
            select new {
                WorkOrderId = w.Id, req.CampusId, req.Summary,
                PayrollEmployeeId = pe.Id, EmployeeName = emp.FullName,
                run.RunNumber, pe.GrossAmount
            };
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId);
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.Summary.Contains(term) ||
                x.EmployeeName.Contains(term) || x.RunNumber.Contains(term));
        var raw = await query.OrderByDescending(x => x.WorkOrderId)
            .ThenByDescending(x => x.PayrollEmployeeId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var choices = new List<FacilityLabourSourceChoiceDto>(raw.Count);
        foreach (var row in raw)
        {
            if (!await ActiveActor(row.CampusId, false, ct)) continue;
            var valid = await Source(row.WorkOrderId, row.PayrollEmployeeId, ct);
            if (!valid.HasValue || valid.Value.Campus != row.CampusId) continue;
            choices.Add(new FacilityLabourSourceChoiceDto {
                WorkOrderId = row.WorkOrderId,
                PayrollEmployeeId = row.PayrollEmployeeId, CampusId = row.CampusId,
                WorkOrderSummary = row.Summary, EmployeeName = row.EmployeeName,
                PayrollRunNumber = row.RunNumber, PayrollGrossBdt = row.GrossAmount
            });
        }
        return ApiResponse<PagedResult<FacilityLabourSourceChoiceDto>>.SuccessResponse(new() {
            Page = page, PageSize = pageSize, TotalCount = choices.Count, Items = choices
        });
    }

    private async Task EnrichAsync(List<FacilityLabourAllocationDto> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        var orderIds = rows.Select(x => x.FacilityWorkOrderId).Distinct().ToArray();
        var payrollIds = rows.Select(x => x.PayrollEmployeeId).Distinct().ToArray();
        var orderLabels = await (from w in db.FacilityWorkOrders.AsNoTracking()
            join r in db.FacilityServiceRequests.AsNoTracking()
                on new { w.TenantId, Id = w.FacilityServiceRequestId }
                equals new { r.TenantId, r.Id }
            where w.TenantId == user.TenantId && orderIds.Contains(w.Id)
            select new { w.Id, r.Summary }).ToDictionaryAsync(x => x.Id, ct);
        var payrollLabels = await (from pe in db.PayrollEmployees.AsNoTracking()
            join employee in db.Employees.AsNoTracking()
                on new { pe.TenantId, Id = pe.EmployeeId }
                equals new { employee.TenantId, employee.Id }
            join run in db.PayrollRuns.AsNoTracking()
                on new { pe.TenantId, Id = pe.PayrollRunId }
                equals new { run.TenantId, run.Id }
            where pe.TenantId == user.TenantId && payrollIds.Contains(pe.Id)
            select new { pe.Id, Name = employee.FullName, run.RunNumber })
            .ToDictionaryAsync(x => x.Id, ct);
        foreach (var item in rows)
        {
            if (orderLabels.TryGetValue(item.FacilityWorkOrderId, out var o))
                item.WorkOrderSummary = o.Summary;
            if (payrollLabels.TryGetValue(item.PayrollEmployeeId, out var p))
            {
                item.EmployeeName = p.Name;
                item.PayrollRunNumber = p.RunNumber;
            }
        }
    }

    private static FacilityLabourAllocationDto Map(FacilityLabourCostAllocation item,
        bool sourceReversed = false) => new() {
        Reference = item.PublicId, FacilityWorkOrderId = item.FacilityWorkOrderId,
        CampusId = item.CampusId, PayrollEmployeeId = item.PayrollEmployeeId,
        EmployeeId = item.EmployeeId, PayrollRunId = item.PayrollRunId,
        ActualWorkMinutes = item.ActualWorkMinutes,
        StandardPayrollMinutesSnapshot = item.StandardPayrollMinutesSnapshot,
        PayrollGrossSnapshotBdt = item.PayrollGrossSnapshotBdt,
        AllocatedAmountBdt = item.AllocatedAmountBdt,
        StateCode = item.StateCode, RequestedByUserId = item.RequestedByUserId,
        ReviewedByUserId = item.ReviewedByUserId, ReviewReason = item.ReviewReason,
        RowVersion = Convert.ToBase64String(item.RowVersion),
        PayrollSourceReversed = sourceReversed
    };

    private async Task<bool> Reversed(long runId, CancellationToken ct)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == runId && !x.IsDeleted, ct);
        return run == null || !await PayrollPosted(runId, run.JournalId, ct);
    }
    public async Task<ApiResponse<FacilityLabourAllocationDto>> GetAsync(Guid reference,
        CancellationToken ct = default)
    {
        if (!Reader) return Error<FacilityLabourAllocationDto>("Finance report denied.", 403);
        var item = await Visible().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
        if (item == null || !await ActiveReader(item.CampusId, ct))
            return Error<FacilityLabourAllocationDto>("Labour allocation not found.", 404);
        var mapped = Map(item, await Reversed(item.PayrollRunId, ct));
        await EnrichAsync(new List<FacilityLabourAllocationDto> { mapped }, ct);
        return ApiResponse<FacilityLabourAllocationDto>.SuccessResponse(mapped);
    }
    public async Task<ApiResponse<PagedResult<FacilityLabourAllocationDto>>> ListAsync(
        long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Reader || page is < 1 or > 100000 || pageSize is < 1 or > 50 ||
            campusId is <= 0)
            return Error<PagedResult<FacilityLabourAllocationDto>>("Invalid report scope.", 400);
        var query = Visible();
        if (campusId.HasValue)
        {
            if (!await ActiveReader(campusId.Value, ct))
                return Error<PagedResult<FacilityLabourAllocationDto>>("Campus denied.", 403);
            query = query.Where(x => x.CampusId == campusId);
        }
        else if ((!await LiveGlobalRole(ct) && !await LiveAccountantRole(ct)) ||
            !await db.TenantMemberships.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && x.UserId == user.UserId &&
            x.Status == MembershipStatus.Active && !x.IsDeleted, ct) ||
            !await db.Users.AsNoTracking().AnyAsync(x => x.Id == user.UserId &&
                x.IsActive && !x.IsDeleted, ct))
            return Error<PagedResult<FacilityLabourAllocationDto>>("Inactive reader.", 403);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var runIds = items.Select(x => x.PayrollRunId).Distinct().ToArray();
        var runs = await db.PayrollRuns.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && runIds.Contains(x.Id) && !x.IsDeleted)
            .Select(x => new { x.Id, x.JournalId }).ToListAsync(ct);
        var journalIds = runs.Where(x => x.JournalId.HasValue)
            .Select(x => x.JournalId!.Value).ToArray();
        var reversals = await db.Journals.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.State == JournalState.Posted &&
            x.SourceType == "PayrollRunReversal" && x.ReversalOfJournalId != null &&
            journalIds.Contains(x.ReversalOfJournalId.Value) && !x.IsDeleted)
            .Select(x => x.ReversalOfJournalId!.Value).ToListAsync(ct);
        var reversalSet = reversals.ToHashSet();
        var available = runs.ToDictionary(x => x.Id, x => x.JournalId);
        var mapped = items.Select(x => Map(x,
            !available.TryGetValue(x.PayrollRunId, out var j) ||
            !j.HasValue || reversalSet.Contains(j.Value))).ToList();
        await EnrichAsync(mapped, ct);
        return ApiResponse<PagedResult<FacilityLabourAllocationDto>>.SuccessResponse(new() {
            Page = page, PageSize = pageSize, TotalCount = count, Items = mapped
        });
    }

    public async Task<ApiResponse<FacilityLabourAllocationDto>> CreateAsync(
        CreateFacilityLabourAllocationDto request, CancellationToken ct = default)
    {
        if (!Maker) return Error<FacilityLabourAllocationDto>("Finance maker denied.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.FacilityWorkOrderId <= 0 || request.PayrollEmployeeId <= 0 ||
            request.ActualWorkMinutes is < 1 or > 60000 ||
            Clean(request.Evidence) is not string evidence ||
            evidence.Length is < 10 or > 800 || !Plain(evidence))
            return Error<FacilityLabourAllocationDto>("Work order, payroll source, minutes and evidence required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var prior = await db.FacilityLabourCostAllocations.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (prior != null)
            {
                if (prior.RequestedByUserId != user.UserId ||
                    prior.FacilityWorkOrderId != request.FacilityWorkOrderId ||
                    prior.PayrollEmployeeId != request.PayrollEmployeeId ||
                    prior.ActualWorkMinutes != request.ActualWorkMinutes ||
                    prior.Evidence != evidence)
                    return Error<FacilityLabourAllocationDto>("Changed cost allocation command.", 409);
                if (!await ActiveActor(prior.CampusId, false, ct))
                    return Error<FacilityLabourAllocationDto>("Maker grant revoked.", 403);
                return ApiResponse<FacilityLabourAllocationDto>.SuccessResponse(Map(prior));
            }
            var source = await Source(request.FacilityWorkOrderId, request.PayrollEmployeeId, ct);
            if (!source.HasValue || !await ActiveActor(source.Value.Campus, false, ct))
                return Error<FacilityLabourAllocationDto>("Posted payroll and verified work required.", 403);
            if (request.ActualWorkMinutes > source.Value.StandardMinutes)
                return Error<FacilityLabourAllocationDto>("Actual minutes exceed standard payroll minutes.", 409);
            var work = await db.FacilityWorkOrders.AsNoTracking().FirstAsync(x =>
                x.TenantId == user.TenantId && x.Id == request.FacilityWorkOrderId, ct);
            if (work.StartedAtUtc is not DateTime start || work.CompletedAtUtc is not DateTime end ||
                request.ActualWorkMinutes > (end - start).TotalMinutes)
                return Error<FacilityLabourAllocationDto>("Claimed minutes exceed measured work window.", 409);
            // Lock the one canonical employee payroll snapshot until competing
            // allocation reservations have committed. No MAX()+1 / blind sum.
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    IF NOT EXISTS (SELECT 1 FROM dbo.PayrollEmployees WITH (UPDLOCK,HOLDLOCK)
                        WHERE TenantId={user.TenantId} AND Id={request.PayrollEmployeeId} AND IsDeleted=0)
                        THROW 53511, 'Payroll source disappeared', 1;
                    """, ct);
            if (await db.FacilityLabourCostAllocations.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && !x.IsDeleted &&
                x.StateCode != "Rejected" &&
                x.FacilityWorkOrderId == request.FacilityWorkOrderId &&
                x.PayrollEmployeeId == request.PayrollEmployeeId, ct))
                return Error<FacilityLabourAllocationDto>("This work order already has that payroll allocation.", 409);
            var amount = decimal.Round(source.Value.Gross * request.ActualWorkMinutes /
                source.Value.StandardMinutes, 2, MidpointRounding.AwayFromZero);
            if (amount <= 0m || amount > source.Value.Gross)
                return Error<FacilityLabourAllocationDto>("Invalid derived payroll cost.", 409);
            var reserved = await db.FacilityLabourCostAllocations.AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                    x.PayrollEmployeeId == request.PayrollEmployeeId &&
                    x.StateCode != "Rejected")
                .SumAsync(x => (decimal?)x.AllocatedAmountBdt, ct) ?? 0m;
            if (reserved + amount > source.Value.Gross)
                return Error<FacilityLabourAllocationDto>("Allocated minutes exceed posted payroll gross.", 409);
            var now = Now;
            var record = new FacilityLabourCostAllocation {
                TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                FacilityWorkOrderId = request.FacilityWorkOrderId,
                CampusId = source.Value.Campus,
                PayrollEmployeeId = request.PayrollEmployeeId,
                EmployeeId = source.Value.Employee, PayrollRunId = source.Value.PayrollRun,
                ActualWorkMinutes = request.ActualWorkMinutes,
                StandardPayrollMinutesSnapshot = source.Value.StandardMinutes,
                PayrollGrossSnapshotBdt = source.Value.Gross, AllocatedAmountBdt = amount,
                StateCode = "Submitted", Evidence = evidence,
                RequestedByUserId = user.UserId, CreatedAt = now, CreatedBy = user.UserId
            };
            db.FacilityLabourCostAllocations.Add(record);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<FacilityLabourAllocationDto>.SuccessResponse(Map(record));
        }
        catch (Exception ex) when (Conflict(ex))
        {
            logger.LogWarning(ex, "Facility labour allocation contention for tenant {TenantId}", user.TenantId);
            return Error<FacilityLabourAllocationDto>("Concurrent payroll reservation or authorization conflict.", 409);
        }
    }

    public async Task<ApiResponse<FacilityLabourAllocationDto>> ReviewAsync(
        Guid reference, ReviewFacilityLabourAllocationDto request, CancellationToken ct = default)
    {
        if (!Reviewer) return Error<FacilityLabourAllocationDto>("Finance reviewer denied.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion) ||
            Clean(request.Reason) is not string reason || reason.Length is < 10 or > 800 ||
            !Plain(reason))
            return Error<FacilityLabourAllocationDto>("Independent review reason required.", 400);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.Approve + "|" + reason)));
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var item = await db.FacilityLabourCostAllocations.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
            if (item == null) return Error<FacilityLabourAllocationDto>("Allocation not found.", 404);
            if (!await ActiveActor(item.CampusId, true, ct) ||
                item.RequestedByUserId == user.UserId)
                return Error<FacilityLabourAllocationDto>("Independent checker and live campus access required.", 403);
            if (item.ReviewRequestId == request.ClientRequestId &&
                item.ReviewFingerprint == fingerprint &&
                item.ReviewedByUserId == user.UserId &&
                item.StateCode == (request.Approve ? "Approved" : "Rejected"))
                return ApiResponse<FacilityLabourAllocationDto>.SuccessResponse(Map(item));
            if (item.StateCode != "Submitted" || !Version(item.RowVersion, request.RowVersion))
                return Error<FacilityLabourAllocationDto>("Allocation stale or already reviewed.", 409);
            if (request.Approve)
            {
                var source = await Source(item.FacilityWorkOrderId, item.PayrollEmployeeId, ct);
                if (!source.HasValue || source.Value.Campus != item.CampusId ||
                    source.Value.PayrollRun != item.PayrollRunId ||
                    source.Value.Employee != item.EmployeeId ||
                    source.Value.Gross != item.PayrollGrossSnapshotBdt)
                    return Error<FacilityLabourAllocationDto>("Payroll or work source changed; reject, do not approve.", 409);
                var sum = await db.FacilityLabourCostAllocations.AsNoTracking()
                    .Where(x => x.TenantId == user.TenantId && x.PayrollEmployeeId == item.PayrollEmployeeId &&
                        x.StateCode != "Rejected" && !x.IsDeleted)
                    .SumAsync(x => (decimal?)x.AllocatedAmountBdt, ct) ?? 0m;
                if (sum > source.Value.Gross)
                    return Error<FacilityLabourAllocationDto>("Total reservations exceed original payroll gross.", 409);
            }
            var now = Now;
            item.StateCode = request.Approve ? "Approved" : "Rejected";
            item.ReviewRequestId = request.ClientRequestId;
            item.ReviewFingerprint = fingerprint;
            item.ReviewedByUserId = user.UserId; item.ReviewedAtUtc = now;
            item.ReviewReason = reason; item.UpdatedAt = now; item.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<FacilityLabourAllocationDto>.SuccessResponse(Map(item));
        }
        catch (Exception ex) when (Conflict(ex))
        {
            logger.LogWarning(ex, "Facility labour allocation approval contention tenant {TenantId}", user.TenantId);
            return Error<FacilityLabourAllocationDto>("Concurrent payroll review conflict.", 409);
        }
    }
}
