using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class DailyCashCollectionReportTests
{
    private static readonly DateOnly Day = new(2026, 10, 20);

    [Fact]
    public async Task Authorized_register_has_full_period_totals_and_paged_receipts()
    {
        await using var db = Database();
        await SeedReceipt(db, 100m, "RCP-A");
        await SeedReceipt(db, 25.50m, "RCP-B");
        var service = new FeeReceivablesAgingService(db, new User("TenantAdmin"));
        var report = await service.GetDailyCashCollectionsAsync(Query(pageSize: 1));
        report.StatusCode.Should().Be(200);
        report.Data!.Summary.Sum(x => x.GrossCollected).Should().Be(125.50m);
        report.Data.Summary.Sum(x => x.ReceiptCount).Should().Be(2);
        report.Data.Receipts.TotalCount.Should().Be(2);
        report.Data.Receipts.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Unallocated_or_duplicate_allocations_are_excluded_not_silently_counted()
    {
        await using var db = Database();
        var payment = await SeedReceipt(db, 100m, "RCP-1");
        var old = await db.PaymentAllocations.SingleAsync(x => x.StudentPaymentId == payment.Id);
        db.PaymentAllocations.Add(new PaymentAllocation
        {
            TenantId = 41, StudentPaymentId = payment.Id,
            StudentInvoiceId = old.StudentInvoiceId, Amount = 10m
        });
        await db.SaveChangesAsync();
        var report = await new FeeReceivablesAgingService(db, new User("TenantAdmin"))
            .GetDailyCashCollectionsAsync(Query());
        report.StatusCode.Should().Be(200);
        report.Data!.Receipts.TotalCount.Should().Be(0);
        report.Data.Summary.Should().BeEmpty();
    }

    [Fact]
    public async Task Campus_guesses_and_non_finance_roles_fail_closed()
    {
        await using var db = Database();
        var payment = await SeedReceipt(db, 100m, "RCP-A");
        var campus = await db.Campuses.SingleAsync();
        (await new FeeReceivablesAgingService(db, new User("Staff"))
            .GetDailyCashCollectionsAsync(Query())).StatusCode.Should().Be(403);
        (await new FeeReceivablesAgingService(db, new User("Accountant"))
            .GetDailyCashCollectionsAsync(Query(campusId: campus.Id))).StatusCode.Should().Be(403);
        (await new FeeReceivablesAgingService(db, new User("TenantAdmin"))
            .GetDailyCashCollectionsAsync(Query(campusId: campus.Id + 1000))).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Foreign_tenant_receipts_never_appear_even_when_dates_and_codes_match()
    {
        var name = "cash-register-" + Guid.NewGuid().ToString("N");
        await using (var foreign = Database(name, 42))
            await SeedReceipt(foreign, 200m, "RCP-FOREIGN", 42);
        await using var db = Database(name);
        var report = await new FeeReceivablesAgingService(db, new User("TenantAdmin"))
            .GetDailyCashCollectionsAsync(Query());
        report.StatusCode.Should().Be(200);
        report.Data!.Receipts.TotalCount.Should().Be(0);
        report.Data.Summary.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_period_and_unbounded_pages_are_rejected()
    {
        await using var db = Database();
        var service = new FeeReceivablesAgingService(db, new User("TenantAdmin"));
        (await service.GetDailyCashCollectionsAsync(new DailyCashCollectionQueryDto
        {
            FromDate = Day.AddDays(-40), ToDate = Day
        })).StatusCode.Should().Be(400);
        (await service.GetDailyCashCollectionsAsync(new DailyCashCollectionQueryDto
        {
            FromDate = Day, ToDate = Day, PageSize = 101
        })).StatusCode.Should().Be(400);
    }

    private static DailyCashCollectionQueryDto Query(int pageSize = 25, long? campusId = null) =>
        new() { FromDate = Day, ToDate = Day, CampusId = campusId, PageSize = pageSize };

    private static async Task<StudentPayment> SeedReceipt(
        EduOSDbContext db, decimal amount, string number, long tenant = 41)
    {
        var campus = new Campus { TenantId = tenant, Name = "Main", Code = "MAIN-" + number };
        var student = new Student { TenantId = tenant, StudentCode = "ST-" + number, FullName = "Learner" };
        db.Campuses.Add(campus); db.Students.Add(student);
        await db.SaveChangesAsync();
        var enrollment = new StudentEnrollment
        {
            TenantId = tenant, StudentId = student.Id, CampusId = campus.Id, RollNo = "1"
        };
        db.StudentEnrollments.Add(enrollment);
        await db.SaveChangesAsync();
        var invoice = new StudentInvoice
        {
            TenantId = tenant, StudentEnrollmentId = enrollment.Id,
            ClientRequestId = Guid.NewGuid(), InvoiceNumber = "INV-" + number,
            InvoiceDate = Day, DueDate = Day, TotalAmount = amount, PaidAmount = amount,
            CurrencyCode = "BDT", State = InvoiceState.Paid
        };
        var payment = new StudentPayment
        {
            TenantId = tenant, StudentId = student.Id, ClientRequestId = Guid.NewGuid(),
            ReceiptNumber = number, PaymentDate = Day, PaymentMethod = PaymentMethodType.Cash,
            Amount = amount, CurrencyCode = "BDT", State = PaymentState.Successful,
            ReceivedByUserId = 9
        };
        db.StudentInvoices.Add(invoice); db.StudentPayments.Add(payment);
        await db.SaveChangesAsync();
        db.PaymentAllocations.Add(new PaymentAllocation
        {
            TenantId = tenant, StudentPaymentId = payment.Id,
            StudentInvoiceId = invoice.Id, Amount = amount
        });
        await db.SaveChangesAsync();
        return payment;
    }

    private static EduOSDbContext Database(string? name = null, long tenant = 41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(name ?? "cash-register-" + Guid.NewGuid().ToString("N"))
            .Options, accessor);
    }

    private sealed class User(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string name) => name == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
