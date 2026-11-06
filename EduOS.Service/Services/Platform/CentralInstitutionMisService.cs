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
        // Inclusive period: a difference of 365 days spans 366 calendar dates.
        if (fromDate == default || toDate < fromDate ||
            toDate.DayNumber - fromDate.DayNumber >= 366)
            return ApiResponse<CentralInstitutionMisDto>.ErrorResponse(
                "Select a valid MIS period of at most 366 days.", 400);

        var tenant = user.TenantId;
        if (!await CentralPrivilegedAccess.HasRoleAsync(db, user, new[] { "TENANTADMIN" }, ct))
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
        await Add("ATTENDANCE", "finalized_attendance_sessions", "AttendanceSessions",
            "Finalized student attendance sessions dated inside the selected period.", "Period",
            async () => await db.AttendanceSessions.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.IsFinalized &&
                x.AttendanceDate >= fromDate && x.AttendanceDate <= toDate, ct));
        await Add("ATTENDANCE", "finalized_student_absences", "StudentAttendances/AttendanceSessions",
            "Absent marks in finalized tenant attendance sessions dated inside the selected period; corrected current marks, not an enrollment denominator.",
            "Period", async () => await (
                from a in db.StudentAttendances.AsNoTracking()
                join s in db.AttendanceSessions.AsNoTracking() on a.AttendanceSessionId equals s.Id
                where a.TenantId == tenant && !a.IsDeleted && s.TenantId == tenant &&
                      !s.IsDeleted && s.IsFinalized &&
                      s.AttendanceDate >= fromDate && s.AttendanceDate <= toDate &&
                      a.State == AttendanceState.Absent
                select a.Id).CountAsync(ct));
        await Add("HR", "active_employees", "Employees",
            "Current tenant employees with State=Active.", "Current",
            async () => await db.Employees.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.State == EmployeeState.Active, ct));
        await Add("PAYROLL", "approved_unposted_payroll_runs", "PayrollRuns",
            "Payroll runs approved but not posted, with period end inside the selected period.", "Period",
            async () => await db.PayrollRuns.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.State == PayrollRunState.Approved &&
                x.JournalId == null && x.PeriodEnd >= fromDate && x.PeriodEnd <= toDate, ct));
        await Add("INVENTORY", "submitted_requisitions", "ProcurementRequisitions",
            "Current submitted procurement requisitions, not purchase orders.", "Current",
            async () => await db.ProcurementRequisitions.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.StateCode == "Submitted", ct));
        await Add("INVENTORY", "purchase_orders_awaiting_review", "ProcurementPurchaseOrders",
            "Current supplier purchase orders submitted for approval or cancellation, not goods receipts or GL liabilities.", "Current",
            async () => await db.ProcurementPurchaseOrders.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted &&
                (x.StateCode == "Submitted" || x.StateCode == "CancelRequested"), ct));
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
        await Add("FACILITIES", "visitor_policy_changes_awaiting_review", "FacilityVisitorPolicyChanges",
            "Current submitted visitor privacy policy amendments awaiting source-owned approval.", "Current",
            async () => await db.FacilityVisitorPolicyChanges.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.StateCode == "Submitted", ct));
        await Add("TRANSPORT", "planned_trips", "TransportTrips",
            "Transport trips with State=Planned and operating date inside the selected period.", "Period",
            async () => await db.TransportTrips.AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.State == "Planned" &&
                x.OperatingDate >= fromDate && x.OperatingDate <= toDate, ct));

        var financeAllowed = await modules.IsCurrentTenantModuleAvailableAsync("FINANCE");
        if (financeAllowed)
        {
            // Operational receivables belong to the canonical StudentInvoice, not the
            // posted GL income metric. A paid/cancelled/refunded/draft invoice cannot
            // be counted as an open receivable even if old data has a stale DueAmount.
            var openInvoices = db.StudentInvoices.AsNoTracking().Where(x =>
                x.TenantId == tenant && !x.IsDeleted &&
                (x.State == InvoiceState.Issued || x.State == InvoiceState.PartiallyPaid));
            if (await openInvoices.AnyAsync(x => x.DueAmount < 0m || x.CurrencyCode != "BDT", ct))
                return ApiResponse<CentralInstitutionMisDto>.ErrorResponse(
                    "Invalid student receivable amount or currency; reconcile invoices before MIS reporting.", 409);
            var outstanding = openInvoices.Where(x => x.DueAmount > 0m);
            metrics.Add(new CentralInstitutionMisMetricDto
            {
                Key = "outstanding_fee_invoices", ModuleCode = "FINANCE", Source = "StudentInvoices",
                Definition = "Issued or partially paid invoices with positive current due; excludes draft, cancelled, paid and refunded invoices.",
                Scope = "Current", Unit = "Count", Available = true,
                Value = await outstanding.CountAsync(ct)
            });
            metrics.Add(new CentralInstitutionMisMetricDto
            {
                Key = "outstanding_fee_amount", ModuleCode = "FINANCE", Source = "StudentInvoices",
                Definition = "Current operational invoice receivables, not collected cash or posted GL income.",
                Scope = "Current", Unit = "BDT", Available = true,
                Value = await outstanding.Select(x => (decimal?)x.DueAmount).SumAsync(ct) ?? 0m
            });
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
            metrics.Add(new CentralInstitutionMisMetricDto
            {
                Key = "outstanding_fee_invoices", ModuleCode = "FINANCE", Source = "StudentInvoices",
                Definition = "Issued or partially paid invoices with positive current due; excludes draft, cancelled, paid and refunded invoices.",
                Scope = "Current", Unit = "Count", Available = false
            });
            metrics.Add(new CentralInstitutionMisMetricDto
            {
                Key = "outstanding_fee_amount", ModuleCode = "FINANCE", Source = "StudentInvoices",
                Definition = "Current operational invoice receivables, not collected cash or posted GL income.",
                Scope = "Current", Unit = "BDT", Available = false
            });
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
