using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Platform;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CentralInstitutionMisServiceTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    [Fact]
    public async Task Mis_uses_tenant_canonical_sources_and_does_not_sum_domain_payments_twice()
    {
        await using var db = Database();
        SeedAdmin(db);
        db.Students.Add(new Student { Id = 1, TenantId = 41, StudentCode = "S-1",
            FullName = "Active learner", StatusCode = "Active" });
        db.Students.Add(new Student { Id = 2, TenantId = 41, StudentCode = "S-2",
            FullName = "Inactive learner", StatusCode = "Inactive" });
        db.Employees.Add(new Employee { Id = 3, TenantId = 41, FullName = "Teacher",
            EmployeeCode = "E-3", State = EmployeeState.Active });
        db.ProcurementRequisitions.Add(new ProcurementRequisition { Id = 5, TenantId = 41,
            CampusId = 10, RequestNumber = "R-5", Purpose = "Office", StateCode = "Submitted" });
        db.Assets.Add(new Asset { Id = 6, TenantId = 41, AssetTag = "AST-6",
            Name = "Projector", IsActive = true });
        db.BookIssues.Add(new BookIssue { Id = 7, TenantId = 41, BookCopyId = 1,
            State = BookIssueState.Issued });
        db.AttendanceSessions.Add(new AttendanceSession { Id = 8, TenantId = 41,
            AcademicBatchId = 1, AttendanceDate = new DateOnly(2026, 10, 15), IsFinalized = true });
        db.StudentAttendances.Add(new StudentAttendance { Id = 9, TenantId = 41,
            AttendanceSessionId = 8, StudentEnrollmentId = 1, State = AttendanceState.Absent });
        db.StudentAttendances.Add(new StudentAttendance { Id = 10, TenantId = 41,
            AttendanceSessionId = 8, StudentEnrollmentId = 2, State = AttendanceState.Present });
        db.PayrollRuns.Add(new PayrollRun { Id = 11, TenantId = 41, RunNumber = "PAY-11",
            Year = 2026, Month = 10, PeriodStart = From, PeriodEnd = To,
            State = PayrollRunState.Approved });
        db.StudentInvoices.Add(new StudentInvoice { Id = 12, TenantId = 41,
            StudentEnrollmentId = 1, InvoiceNumber = "INV-12", InvoiceDate = From, DueDate = To,
            State = InvoiceState.PartiallyPaid, TotalAmount = 100m,
            PaidAmount = 80m, DueAmount = 20m });
        db.StudentInvoices.Add(new StudentInvoice { Id = 13, TenantId = 41,
            StudentEnrollmentId = 1, InvoiceNumber = "INV-13", InvoiceDate = From, DueDate = To,
            State = InvoiceState.Cancelled, TotalAmount = 99m, DueAmount = 99m });
        await db.SaveChangesAsync();

        var finance = Finance();
        var result = await Service(db, Modules(), finance.Object).GetAsync(From, To);
        result.StatusCode.Should().Be(200);
        var metrics = result.Data!.Metrics.ToDictionary(x => x.Key);
        metrics["active_students"].Value.Should().Be(1m);
        metrics["active_employees"].Value.Should().Be(1m);
        metrics["submitted_requisitions"].Value.Should().Be(1m);
        metrics["active_assets"].Value.Should().Be(1m);
        metrics["issued_loans"].Value.Should().Be(1m);
        metrics["finalized_attendance_sessions"].Value.Should().Be(1m);
        metrics["finalized_student_absences"].Value.Should().Be(1m);
        metrics["approved_unposted_payroll_runs"].Value.Should().Be(1m);
        metrics["outstanding_fee_invoices"].Value.Should().Be(1m);
        metrics["outstanding_fee_amount"].Value.Should().Be(20m);
        metrics["outstanding_fee_amount"].Scope.Should().Be("Current");
        metrics["posted_income"].Value.Should().Be(150m);
        metrics["posted_expenses"].Value.Should().Be(70m);
        metrics["net_surplus"].Value.Should().Be(80m);
        metrics["posted_income"].Unit.Should().Be("BDT");
        metrics["planned_trips"].Scope.Should().Be("Period");
        metrics.Values.Should().OnlyContain(x => x.Available && x.Value.HasValue);
        finance.Verify(x => x.GetInstitutionalIncomeExpenseTotalsAsync(From, To,
            It.IsAny<CancellationToken>()), Times.Once);
        db.ChangeTracker.HasChanges().Should().BeFalse();
    }

    [Fact]
    public async Task Mis_blocks_stale_admin_roles_and_does_not_label_disabled_modules_as_zero()
    {
        await using var db = Database();
        SeedAdmin(db);
        await db.SaveChangesAsync();
        var modules = Modules();
        modules.Setup(x => x.IsCurrentTenantModuleAvailableAsync("FINANCE")).ReturnsAsync(false);
        modules.Setup(x => x.IsCurrentTenantModuleAvailableAsync("HOSTEL")).ReturnsAsync(false);
        var finance = Finance();
        var service = Service(db, modules, finance.Object);
        var page = await service.GetAsync(From, To);
        page.StatusCode.Should().Be(200);
        page.Data!.Metrics.Single(x => x.Key == "posted_income").Available.Should().BeFalse();
        page.Data.Metrics.Single(x => x.Key == "posted_income").Value.Should().BeNull();
        page.Data.Metrics.Single(x => x.Key == "outstanding_fee_amount").Value.Should().BeNull();
        page.Data.Metrics.Single(x => x.Key == "outstanding_fee_invoices").Available.Should().BeFalse();
        page.Data.Metrics.Single(x => x.Key == "active_hostel_allocations").Value.Should().BeNull();
        page.Data.Metrics.Single(x => x.Key == "active_students").Value.Should().Be(0m);
        (await service.GetAsync(To, From)).StatusCode.Should().Be(400);
        (await service.GetAsync(From, From.AddDays(366))).StatusCode.Should().Be(400);
        (await service.GetAsync(From, From.AddDays(365))).StatusCode.Should().Be(200);
        finance.Verify(x => x.GetInstitutionalIncomeExpenseTotalsAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        db.UserRoles.Remove(db.UserRoles.Single());
        await db.SaveChangesAsync();
        (await service.GetAsync(From, To)).StatusCode.Should().Be(403);
        (await Service(db, Modules(), finance.Object, admin: false).GetAsync(From, To))
            .StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Mis_fail_closes_financial_integrity_errors_instead_of_showing_partial_totals()
    {
        await using var db = Database();
        SeedAdmin(db);
        await db.SaveChangesAsync();
        var finance = Finance(status: 409);
        (await Service(db, Modules(), finance.Object).GetAsync(From, To))
            .StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Mis_rejects_negative_or_foreign_currency_operational_receivables()
    {
        await using var db = Database();
        SeedAdmin(db);
        db.StudentInvoices.Add(new StudentInvoice { Id = 15, TenantId = 41,
            StudentEnrollmentId = 1, InvoiceNumber = "INV-15", InvoiceDate = From,
            DueDate = To, State = InvoiceState.Issued, DueAmount = -1m });
        await db.SaveChangesAsync();
        var finance = Finance();
        (await Service(db, Modules(), finance.Object).GetAsync(From, To))
            .StatusCode.Should().Be(409);
        finance.Verify(x => x.GetInstitutionalIncomeExpenseTotalsAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IAccountingReportingService> Finance(int status = 200)
    {
        var mock = new Mock<IAccountingReportingService>();
        var response = status == 200
            ? ApiResponse<InstitutionalIncomeExpenseTotalsDto>.SuccessResponse(
                new InstitutionalIncomeExpenseTotalsDto { FromDate = From, ToDate = To,
                    TotalIncome = 150m, TotalExpenses = 70m, NetSurplus = 80m })
            : ApiResponse<InstitutionalIncomeExpenseTotalsDto>.ErrorResponse("Unbalanced GL", status);
        mock.Setup(x => x.GetInstitutionalIncomeExpenseTotalsAsync(
            From, To, It.IsAny<CancellationToken>())).ReturnsAsync(response);
        return mock;
    }

    private static Mock<ITenantModuleService> Modules()
    {
        var mock = new Mock<ITenantModuleService>();
        mock.Setup(x => x.IsCurrentTenantModuleAvailableAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        return mock;
    }

    private static CentralInstitutionMisService Service(EduOSDbContext db,
        Mock<ITenantModuleService> modules, IAccountingReportingService finance, bool admin = true) =>
        new(db, new Actor(admin), modules.Object, finance, TimeProvider.System);

    private static void SeedAdmin(EduOSDbContext db)
    {
        db.Users.Add(new ApplicationUser { Id = 9, UserName = "mis-admin" });
        db.TenantMemberships.Add(new TenantMembership { Id = 1, TenantId = 41,
            UserId = 9, Status = MembershipStatus.Active });
        db.Roles.Add(new ApplicationRole { Id = 2, TenantId = 41,
            Name = "TenantAdmin", NormalizedName = "TENANTADMIN" });
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = 9, RoleId = 2 });
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("central-mis-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class Actor(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Accountant"];
        public bool IsInRole(string role) => admin ? role == "TenantAdmin" : role == "Accountant";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
