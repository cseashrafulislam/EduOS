using EduOS.Core.Common;
using EduOS.Core.DTOs.Platform;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Platform;

/// <summary>
/// Institution-wide, read-only MIS over canonical domain sources. No KPI table,
/// extra GL, stock ledger, generalized status or inferred financial posting.
/// No campus filter until campus-scoped Finance GL is formally supported.
/// </summary>
public sealed class CentralInstitutionMisService(
    EduOSDbContext db, ICurrentUserService user, ITenantModuleService modules,
    IAccountingReportingService finance, TimeProvider clock) : ICentralInstitutionMisService
{
    public async Task<ApiResponse<CentralInstitutionMisDto>> GetAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || !user.IsTenantAdmin || user.TenantId <= 0 ||
            user.UserId <= 0 || db.CurrentTenantId != user.TenantId)
            return ApiResponse<CentralInstitutionMisDto>.ErrorResponse(
                "Institution administrator access required.", 403);
        if (fromDate == default || toDate < fromDate ||
            toDate.DayNumber - fromDate.DayNumber > 366)
            return ApiResponse<CentralInstitutionMisDto>.ErrorResponse(
                "Select a valid MIS period of at most 366 days.", 400);

        var tenant = user.TenantId;
        var active = await db.Users.AsNoTracking().AnyAsync(x =>
            x.Id == user.UserId && x.IsActive && !x.IsDeleted, ct) &&
            await db.TenantMemberships.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.UserId == user.UserId && !x.IsDeleted &&
                x.Status == MembershipStatus.Active, ct) &&
            await db.UserRoles.AsNoTracking().AnyAsync(ur => ur.UserId == user.UserId &&
                db.Roles.IgnoreQueryFilters().AsNoTracking().Any(r => r.Id == ur.RoleId &&
                    r.IsActive && !r.IsDeleted && r.NormalizedName == "TENANTADMIN" &&
                    (r.TenantId == tenant || (r.TenantId == null && r.IsSystemRole))), ct);
        if (!active)
            return ApiResponse<CentralInstitutionMisDto>.ErrorResponse(
                "Institution membership or administrator role is no longer active.", 403);

        var metrics = new List<CentralInstitutionMisMetricDto>();
        async Task Add(string module, string key, string source, string definition,
            string scope, Func<Task<decimal>> value, string unit = "Count")
        {
            var allowed = await modules.IsCurrentTenantModuleAvailableAsync(module);
            var amount = allowed ? await value() : (decimal?)null;
            metrics.Add(new CentralInstitutionMisMetricDto
            {
                Key = key, ModuleCode = module, Source = source,
                Definition = definition, Scope = scope, Unit = unit,
                Available = allowed, Value = amount
            });
        }

        await Add("STUDENT", "active_students", "Students",
            "Current tenant students with StatusCode=Active.", "Current",
            async () => await db.Students.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.StatusCode == "Active", ct));
        await Add("HR", "active_employees", "Employees",
            "Current tenant employees with State=Active.", "Current",
            async () => await db.Employees.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.State == EmployeeState.Active, ct));
        await Add("INVENTORY", "submitted_requisitions", "ProcurementRequisitions",
            "Current submitted procurement requisitions, not purchase orders.", "Current",
            async () => await db.ProcurementRequisitions.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.StateCode == "Submitted", ct));
        await Add("INVENTORY", "active_assets", "Assets",
            "Current non-deleted, active asset master records, not asset book value.", "Current",
            async () => await db.Assets.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.IsActive, ct));
        await Add("LIBRARY", "issued_loans", "BookIssues",
            "Current canonical library issues with State=Issued.", "Current",
            async () => await db.BookIssues.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.State == BookIssueState.Issued, ct));
        await Add("HOSTEL", "active_hostel_allocations", "StudentHostelAllocations",
            "Current tenant bed allocations with State=Active, not vacant-bed inventory.", "Current",
            async () => await db.StudentHostelAllocations.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.State == HostelAllocationState.Active, ct));
        await Add("FACILITIES", "open_work_orders", "FacilityWorkOrders",
            "Current facility work orders in Approved, Scheduled, InProgress or Paused state.", "Current",
            async () => await db.FacilityWorkOrders.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted &&
                (x.StateCode == "Approved" || x.StateCode == "Scheduled" ||
                 x.StateCode == "InProgress" || x.StateCode == "Paused"), ct));
        await Add("TRANSPORT", "planned_trips", "TransportTrips",
            "Transport trips with State=Planned and operating date inside the selected period.", "Period",
            async () => await db.TransportTrips.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.State == "Planned" &&
                x.OperatingDate >= fromDate && x.OperatingDate <= toDate, ct));

        var financeAllowed = await modules.IsCurrentTenantModuleAvailableAsync("FINANCE");
        if (financeAllowed)
        {
            var totals = await finance.GetInstitutionalIncomeExpenseTotalsAsync(fromDate, toDate, ct);
            if (!totals.Success || totals.Data == null)
                return ApiResponse<CentralInstitutionMisDto>.ErrorResponse(
                    totals.Message, totals.StatusCode);
            AddFinance("posted_income", "Posted GL income account credits minus debits.",
                totals.Data.TotalIncome);
            AddFinance("posted_expenses", "Posted GL expense account debits minus credits.",
                totals.Data.TotalExpenses);
            AddFinance("net_surplus", "Posted GL income minus expenses, including reversals.",
                totals.Data.NetSurplus);
        }
        else
        {
            AddFinance("posted_income", "Posted GL income account credits minus debits.", null);
            AddFinance("posted_expenses", "Posted GL expense account debits minus credits.", null);
            AddFinance("net_surplus", "Posted GL income minus expenses, including reversals.", null);
        }
        void AddFinance(string key, string definition, decimal? value) => metrics.Add(
            new CentralInstitutionMisMetricDto
            {
                Key = key, ModuleCode = "FINANCE", Source = "Journals/JournalLines/Accounts",
                Definition = definition, Scope = "Period", Unit = "BDT",
                Available = financeAllowed, Value = value
            });

        return ApiResponse<CentralInstitutionMisDto>.SuccessResponse(
            new CentralInstitutionMisDto
            {
                GeneratedAtUtc = clock.GetUtcNow().UtcDateTime,
                PeriodFrom = fromDate, PeriodTo = toDate, Metrics = metrics
            });
    }
}
