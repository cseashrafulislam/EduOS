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

public sealed class FeeInvoiceBatchPreflightTests
{
    private static readonly DateOnly InvoiceDay = new(2026, 10, 1);

    [Fact]
    public async Task Preview_shows_discounted_eligible_student_without_writing_any_invoice()
    {
        await using var db = Database();
        Seed(db);
        var response = await Service(db).PreviewInvoiceBatchAsync(Request());
        response.StatusCode.Should().Be(200);
        response.Data!.CanGenerate.Should().BeTrue();
        response.Data.EligibleCount.Should().Be(1);
        response.Data.NewCount.Should().Be(1);
        response.Data.NewSubtotal.Should().Be(100m);
        response.Data.NewDiscount.Should().Be(10m);
        response.Data.NewTotal.Should().Be(90m);
        response.Data.Rows.Single().StudentCode.Should().Be("ST-1");
        (await db.StudentInvoices.CountAsync()).Should().Be(0);
        (await db.Journals.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Existing_invoice_with_another_request_key_blocks_generation()
    {
        await using var db = Database();
        Seed(db);
        db.StudentInvoices.Add(new StudentInvoice
        {
            Id = 5, TenantId = 41, StudentEnrollmentId = 1,
            ClientRequestId = Guid.NewGuid(), InvoiceNumber = "INV-OLD",
            InvoiceDate = InvoiceDay, DueDate = InvoiceDay.AddDays(10),
            Subtotal = 100m, TotalAmount = 100m, DueAmount = 100m,
            State = InvoiceState.Issued
        });
        await db.SaveChangesAsync();
        var response = await Service(db).PreviewInvoiceBatchAsync(Request());
        response.StatusCode.Should().Be(200);
        response.Data!.CanGenerate.Should().BeFalse();
        response.Data.ConflictCount.Should().Be(1);
        response.Data.NewCount.Should().Be(0);
        (await db.StudentInvoices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Foreign_tenant_batch_and_non_finance_role_are_denied()
    {
        await using var db = Database();
        Seed(db);
        (await Service(db, "Student").PreviewInvoiceBatchAsync(Request())).StatusCode.Should().Be(403);
        var foreign = Request();
        foreign.AcademicBatchId = 999;
        (await Service(db).PreviewInvoiceBatchAsync(foreign)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Accountant_requires_active_campus_grant_for_preflight_and_generation()
    {
        await using var db = Database();
        Seed(db);
        var denied = await Service(db, "Accountant").PreviewInvoiceBatchAsync(Request());
        denied.StatusCode.Should().Be(404);
        (await Service(db, "Accountant").GenerateInvoicesAsync(Request())).StatusCode.Should().Be(404);
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = 41, UserId = 9, CampusId = 1, IsActive = true
        });
        await db.SaveChangesAsync();
        (await Service(db, "Accountant").PreviewInvoiceBatchAsync(Request())).StatusCode.Should().Be(200);
        (await db.StudentInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Enrollment_with_inconsistent_campus_is_not_eligible()
    {
        await using var db = Database();
        Seed(db);
        var enrollment = await db.StudentEnrollments.SingleAsync();
        enrollment.CampusId = 999;
        await db.SaveChangesAsync();
        (await Service(db).PreviewInvoiceBatchAsync(Request())).StatusCode.Should().Be(422);
        (await Service(db).GenerateInvoicesAsync(Request())).StatusCode.Should().Be(409);
        (await db.StudentInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Reusing_request_key_for_a_different_month_is_reported_as_conflict()
    {
        await using var db = Database();
        Seed(db);
        var request = Request();
        var first = await Service(db).GenerateInvoicesAsync(request);
        first.StatusCode.Should().Be(200);
        var changed = Request();
        changed.ClientRequestId = request.ClientRequestId;
        changed.Month = 11;
        changed.InvoiceDate = new DateOnly(2026, 11, 1);
        changed.DueDate = changed.InvoiceDate.AddDays(10);
        var preview = await Service(db).PreviewInvoiceBatchAsync(changed);
        preview.StatusCode.Should().Be(200);
        preview.Data!.CanGenerate.Should().BeFalse();
        preview.Data.ConflictCount.Should().Be(1);
    }

    private static GenerateStudentInvoiceBatchRequestDto Request() => new()
    {
        ClientRequestId = Guid.NewGuid(), AcademicBatchId = 1, FeeStructureId = 1,
        Year = 2026, Month = 10, InvoiceDate = InvoiceDay, DueDate = InvoiceDay.AddDays(10)
    };

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
            StudentCode = "ST-1", FullName = "Learner" });
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
            Amount = 100m });
        db.DiscountRules.Add(new DiscountRule { Id = 1, TenantId = 41,
            Name = "Merit", IsPercentage = true, Value = 10m });
        db.StudentDiscounts.Add(new StudentDiscount { Id = 1, TenantId = 41,
            StudentEnrollmentId = 1, DiscountRuleId = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1), ApprovedByUserId = 9 });
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
            .UseInMemoryDatabase("invoice-preflight-" + Guid.NewGuid().ToString("N"))
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
