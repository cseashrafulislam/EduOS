using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class FeeInstallmentProjectionTests
{
    private static FeeInstallmentProjectionQueryDto Query() => new()
    {
        AcademicBatchId = 1, FeeStructureId = 1,
        StartMonth = new DateOnly(2026, 2, 1), Months = 3
    };

    [Fact]
    public async Task Monthly_projection_clamps_due_day_and_never_creates_financial_records()
    {
        await using var db = Database();
        Seed(db);
        var result = await Service(db).GetInstallmentProjectionAsync(Query());
        result.StatusCode.Should().Be(200);
        var report = result.Data!;
        report.CurrentEligibleEnrollments.Should().Be(1);
        report.Months.Should().HaveCount(3);
        report.Months[0].EarliestConfiguredDueDate.Should().Be(new DateOnly(2026, 2, 28));
        report.Months[1].EarliestConfiguredDueDate.Should().Be(new DateOnly(2026, 3, 31));
        report.Months[0].GrossPerStudent.Should().Be(100m);
        report.Months[0].GrossForCurrentCohort.Should().Be(100m);
        report.Months[0].ObservedInvoiceCount.Should().Be(1);
        report.Months[0].ObservedInvoiceTotal.Should().Be(70m);
        (await db.StudentInvoices.CountAsync()).Should().Be(1);
        (await db.StudentPayments.CountAsync()).Should().Be(0);
        (await db.Journals.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Unauthorized_role_and_foreign_batch_are_denied()
    {
        await using var db = Database();
        Seed(db);
        (await Service(db, "Student").GetInstallmentProjectionAsync(Query()))
            .StatusCode.Should().Be(403);
        var foreign = Query(); foreign.AcademicBatchId = 999;
        (await Service(db).GetInstallmentProjectionAsync(foreign))
            .StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Accountant_requires_active_grant_for_same_campus()
    {
        await using var db = Database();
        Seed(db);
        (await Service(db, "Accountant").GetInstallmentProjectionAsync(Query()))
            .StatusCode.Should().Be(404);
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = 41, UserId = 9, CampusId = 1, IsActive = true
        });
        await db.SaveChangesAsync();
        (await Service(db, "Accountant").GetInstallmentProjectionAsync(Query()))
            .StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Invalid_month_count_and_out_of_effective_range_fail_closed()
    {
        await using var db = Database();
        Seed(db);
        var invalid = Query(); invalid.Months = 13;
        (await Service(db).GetInstallmentProjectionAsync(invalid))
            .StatusCode.Should().Be(400);
        invalid = Query(); invalid.StartMonth = new DateOnly(2025, 12, 1);
        (await Service(db).GetInstallmentProjectionAsync(invalid))
            .StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Corrupt_issued_invoice_totals_block_projection()
    {
        await using var db = Database();
        Seed(db);
        var invoice = await db.StudentInvoices.SingleAsync();
        invoice.DueAmount = 51m;
        await db.SaveChangesAsync();
        (await Service(db).GetInstallmentProjectionAsync(Query()))
            .StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Unspecified_due_day_is_not_invented_and_nonmonthly_fees_are_rejected()
    {
        await using var db = Database();
        Seed(db);
        var line = await db.FeeStructureLines.SingleAsync();
        line.DueDayOfMonth = null;
        await db.SaveChangesAsync();
        var preview = await Service(db).GetInstallmentProjectionAsync(Query());
        preview.StatusCode.Should().Be(200);
        preview.Data!.Months[0].HasUnspecifiedDueDays.Should().BeTrue();
        preview.Data.Months[0].EarliestConfiguredDueDate.Should().BeNull();
        line.Frequency = (FeeFrequencyType)999;
        await db.SaveChangesAsync();
        (await Service(db).GetInstallmentProjectionAsync(Query()))
            .StatusCode.Should().Be(409);
    }

    private static void Seed(EduOSDbContext db)
    {
        db.Tenants.Add(new Tenant { Id = 41, Name = "Institute", Code = "INST-41" });
        db.Campuses.Add(new Campus { Id = 1, TenantId = 41, Code = "C1", Name = "Main" });
        db.AcademicYears.Add(new AcademicYear { Id = 2, TenantId = 41, Name = "2026",
            Code = "2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) });
        db.AcademicBatches.Add(new AcademicBatch { Id = 1, TenantId = 41,
            CampusId = 1, AcademicYearId = 2, AcademicProgramId = 3, AcademicLevelId = 4,
            Code = "B1", Name = "Batch 1" });
        db.Students.Add(new Student { Id = 1, TenantId = 41, PersonId = 1,
            StudentCode = "ST-1", FullName = "Learner", StatusCode = "Active" });
        db.StudentEnrollments.Add(new StudentEnrollment { Id = 1, TenantId = 41,
            StudentId = 1, CampusId = 1, AcademicYearId = 2, AcademicBatchId = 1,
            AcademicProgramId = 3, AcademicLevelId = 4, AcademicCurriculumId = 5,
            RollNo = "01", IsCurrent = true, State = EnrollmentState.Active });
        db.FeeStructures.Add(new FeeStructure { Id = 1, TenantId = 41,
            CampusId = 1, AcademicYearId = 2, AcademicBatchId = 1,
            Name = "Monthly", CurrencyCode = "BDT",
            EffectiveFrom = new DateOnly(2026, 1, 1) });
        db.FeeHeads.Add(new FeeHead { Id = 1, TenantId = 41, Code = "TUITION", Name = "Tuition" });
        db.FeeStructureLines.Add(new FeeStructureLine { Id = 1, TenantId = 41,
            FeeStructureId = 1, FeeHeadId = 1, Frequency = FeeFrequencyType.Monthly,
            Amount = 100m, DueDayOfMonth = 31 });
        db.StudentInvoices.Add(new StudentInvoice { Id = 1, TenantId = 41,
            StudentEnrollmentId = 1, InvoiceNumber = "INV-1",
            InvoiceDate = new DateOnly(2026, 2, 1), DueDate = new DateOnly(2026, 2, 28),
            CurrencyCode = "BDT", TotalAmount = 70m, PaidAmount = 20m,
            DueAmount = 50m, State = InvoiceState.PartiallyPaid });
        db.SaveChanges();
    }

    private static FeeBillingService Service(EduOSDbContext db, string role = "TenantAdmin") => new(
        new GenericRepository<FeeStructure>(db), new GenericRepository<FeeStructureLine>(db),
        new GenericRepository<FeeHead>(db), new GenericRepository<StudentDiscount>(db),
        new GenericRepository<DiscountRule>(db), new GenericRepository<StudentInvoice>(db),
        new GenericRepository<StudentInvoiceLine>(db), new GenericRepository<StudentPayment>(db),
        new GenericRepository<PaymentAllocation>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Student>(db), new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicLevel>(db), new GenericRepository<AcademicBatch>(db),
        new GenericRepository<Campus>(db), new GenericRepository<Tenant>(db),
        db, new User(role), TimeProvider.System, NullLogger<FeeBillingService>.Instance);

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("installment-projection-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
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
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
