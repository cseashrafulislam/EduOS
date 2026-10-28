using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.HR;

/// <summary>
/// Reports posted salary net, actually posted/verified settlement and pending payments separately.
/// Never counts a payment as paid based only on PaymentState.Successful.
/// </summary>
public sealed class PayrollReconciliationService : IPayrollReconciliationService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    public PayrollReconciliationService(EduOSDbContext db, ICurrentUserService user)
    { this.db = db; this.user = user; }
    private static ApiResponse<T> Error<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);

    public async Task<ApiResponse<PagedResult<PayrollReconciliationRowDto>>> GetAsync(
        int year, int month, long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || !user.IsTenantAdmin || user.TenantId <= 0 ||
            db.CurrentTenantId != user.TenantId)
            return Error<PagedResult<PayrollReconciliationRowDto>>("Payroll report access denied.", 403);
        if (year is < 2000 or > 2200 || month is < 1 or > 12 || campusId is <= 0 ||
            page < 1 || pageSize is < 1 or > 250 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<PayrollReconciliationRowDto>>("Invalid period or page.");
        var tenant = user.TenantId;
        var q = from employee in db.PayrollEmployees.AsNoTracking()
                join run in db.PayrollRuns.AsNoTracking()
                    on new { employee.TenantId, Id = employee.PayrollRunId }
                    equals new { run.TenantId, run.Id }
                where employee.TenantId == tenant && !employee.IsDeleted &&
                    !run.IsDeleted && run.Year == year && run.Month == month &&
                    run.State != PayrollRunState.Cancelled
                select new { employee, run };
        if (campusId.HasValue) q = q.Where(x => x.run.CampusId == campusId.Value);
        var count = await q.CountAsync(ct);
        if (pageSize > 50 && count > 250)
            return Error<PagedResult<PayrollReconciliationRowDto>>(
                "Export exceeds 250 employees. Narrow by campus or use the paged report.", 409);
        var pageRows = await q.OrderBy(x => x.run.Id).ThenBy(x => x.employee.Id)
            .Skip((int)((long)(page - 1) * pageSize)).Take(pageSize)
            .Select(x => new
            {
                EmployeeId = x.employee.Id,
                x.employee.EmployeeCodeSnapshot, x.employee.EmployeeNameSnapshot,
                x.employee.NetAmount, RunId = x.run.Id, x.run.PublicId,
                x.run.RunNumber, x.run.CampusId, x.run.State,
                x.run.JournalId, x.run.Year, x.run.Month
            }).ToListAsync(ct);
        var employeeIds = pageRows.Select(x => x.EmployeeId).ToArray();
        var runIds = pageRows.Select(x => x.RunId).Distinct().ToArray();
        var accrualIds = pageRows.Where(x => x.JournalId.HasValue)
            .Select(x => x.JournalId!.Value).Distinct().ToArray();

        // Include soft-deleted financial history: deleting a payment must not erase an anomaly.
        var payments = await db.PayrollPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && employeeIds.Contains(x.PayrollEmployeeId) &&
                (x.State == PaymentState.Successful || x.State == PaymentState.AwaitingVerification))
            .OrderBy(x => x.Id).Take(4001)
            .Select(x => new
            {
                x.Id, x.PayrollEmployeeId, x.JournalId, x.State,
                x.Amount, x.IsDeleted, x.CurrencyCode, x.BankAccountId,
                x.PaymentMethod
            }).ToListAsync(ct);
        if (payments.Count > 4000)
            return Error<PagedResult<PayrollReconciliationRowDto>>("Payment history exceeds report safety limit.", 409);
        var payJournalIds = payments.Where(x => x.JournalId.HasValue)
            .Select(x => x.JournalId!.Value).Distinct().ToArray();
        var journalIds = accrualIds.Concat(payJournalIds).Distinct().ToArray();
        var journals = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && journalIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id, x.SourceType, x.SourceId, x.State, x.IsDeleted, x.CurrencyCode,
                x.ReversalOfJournalId
            }).ToDictionaryAsync(x => x.Id, ct);
        var reversalIds = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.ReversalOfJournalId.HasValue &&
                journalIds.Contains(x.ReversalOfJournalId.Value) &&
                (x.State == JournalState.Posted || x.State == JournalState.Reversed))
            .Select(x => x.ReversalOfJournalId!.Value).Distinct().ToListAsync(ct);
        var reversed = reversalIds.ToHashSet();

        var lines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && journalIds.Contains(x.JournalId))
            .OrderBy(x => x.Id).Take(12001)
            .Select(x => new
            {
                x.JournalId, x.DebitAmount, x.CreditAmount, x.Description, x.IsDeleted
            }).ToListAsync(ct);
        if (lines.Count > 12000)
            return Error<PagedResult<PayrollReconciliationRowDto>>(
                "GL line history exceeds report safety limit.", 409);
        var byJournal = lines.ToLookup(x => x.JournalId);
        var byEmployee = payments.ToLookup(x => x.PayrollEmployeeId);
        var runTotals = await db.PayrollEmployees.AsNoTracking()
            .Where(x => x.TenantId == tenant && runIds.Contains(x.PayrollRunId) && !x.IsDeleted)
            .GroupBy(x => x.PayrollRunId)
            .Select(x => new { RunId = x.Key, Net = x.Sum(y => y.NetAmount) })
            .ToDictionaryAsync(x => x.RunId, x => x.Net, ct);

        bool BaseJournal(long? id, string type, long sourceId)
        {
            return id.HasValue && journals.TryGetValue(id.Value, out var j) &&
                !j.IsDeleted && !j.ReversalOfJournalId.HasValue &&
                j.State == JournalState.Posted && j.SourceType == type &&
                j.SourceId == sourceId && j.CurrencyCode == "BDT" && !reversed.Contains(j.Id);
        }
        bool AccrualValid(long? id, long sourceId)
        {
            if (!BaseJournal(id, "PayrollRun", sourceId)) return false;
            var l = byJournal[id!.Value].ToArray();
            if (l.Length < 2 || l.Any(x => x.IsDeleted || x.DebitAmount < 0m ||
                x.CreditAmount < 0m || x.DebitAmount > 0m && x.CreditAmount > 0m))
                return false;
            if (l.Sum(x => x.DebitAmount) <= 0m ||
                l.Sum(x => x.DebitAmount) != l.Sum(x => x.CreditAmount))
                return false;
            return l.Count(x => x.Description == "Payroll net payable" &&
                x.DebitAmount == 0m && x.CreditAmount == runTotals.GetValueOrDefault(sourceId)) == 1;
        }
        bool PaymentValid(long? id, long sourceId, decimal amount)
        {
            if (!BaseJournal(id, "PayrollPayment", sourceId)) return false;
            var l = byJournal[id!.Value].ToArray();
            return l.Length == 2 && l.All(x => !x.IsDeleted &&
                x.DebitAmount >= 0m && x.CreditAmount >= 0m &&
                (x.DebitAmount == 0m) != (x.CreditAmount == 0m)) &&
                l.Sum(x => x.DebitAmount) == amount && l.Sum(x => x.CreditAmount) == amount;
        }
        var rows = pageRows.Select(r =>
        {
            var runPosted = r.State == PayrollRunState.Posted && r.JournalId.HasValue;
            var accrualValid = runPosted && AccrualValid(r.JournalId, r.RunId);
            var paid = 0m;
            var pending = 0m;
            var discrepancy = false;
            foreach (var p in byEmployee[r.EmployeeId])
            {
                if (p.State == PaymentState.AwaitingVerification)
                {
                    if (p.IsDeleted || p.JournalId.HasValue || p.CurrencyCode != "BDT" || p.Amount <= 0m)
                        discrepancy = true;
                    else pending += p.Amount;
                }
                else if (p.State == PaymentState.Successful)
                {
                    if (p.IsDeleted || p.CurrencyCode != "BDT" || p.Amount <= 0m ||
                        !PaymentValid(p.JournalId, p.Id, p.Amount))
                        discrepancy = true;
                    else paid += p.Amount;
                }
            }
            return new PayrollReconciliationRowDto
            {
                RunReference = r.PublicId, RunNumber = r.RunNumber,
                Year = r.Year, Month = r.Month, CampusId = r.CampusId,
                RunState = r.State,
                EmployeeCode = r.EmployeeCodeSnapshot ?? string.Empty,
                EmployeeName = r.EmployeeNameSnapshot ?? string.Empty,
                NetPayable = r.NetAmount, VerifiedPaid = paid,
                PendingVerification = pending, Outstanding = r.NetAmount - paid,
                AccrualStatus = PayrollReconciliationRules.Accrual(runPosted, accrualValid),
                SettlementStatus = PayrollReconciliationRules.Settlement(runPosted, accrualValid,
                    discrepancy, r.NetAmount, paid, pending)
            };
        }).ToList();
        return ApiResponse<PagedResult<PayrollReconciliationRowDto>>.SuccessResponse(
            new PagedResult<PayrollReconciliationRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = count, Items = rows
            });
    }
}
