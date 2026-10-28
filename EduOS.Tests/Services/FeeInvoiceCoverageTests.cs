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

public sealed class FeeInvoiceCoverageTests
{
    private static FeeInvoiceCoverageQueryDto Query() => new()
    {
        AcademicBatchId = 1, BillingMonth = new DateOnly(2026, 2, 1)
    };

    [Fact]
    public async Task Reports_unbilled_billed_and_duplicate_without_writing()
    {
        await using var db = Database(); Seed(db);
        var result = await Service(db).GetInvoiceCoverageAsync(Query());
        result.StatusCode.Should().Be(200);
        result.Data!.Eligible.Should().Be(3);
        result.Data.Billed.Should().Be(1);
        result.Data.Unbilled.Should().Be(1);
        result.Data.Review.Should().Be(1);
        result.Data.Students.Items.Should().HaveCount(3);
        (await db.StudentInvoices.CountAsync()).Should().Be(3);
        (await db.StudentPayments.CountAsync()).Should().Be(0);
        (await db.Journals.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Status_and_search_filter_only_rows_not_authorized_cohort_totals()
    {
        await using var db = Database(); Seed(db);
        var request = Query(); request.Status = "unbilled"; request.Search = "ST-2";
        var result = await Service(db).GetInvoiceCoverageAsync(request);
        result.StatusCode.Should().Be(200);
        result.Data!.Eligible.Should().Be(3);
        result.Data.Students.TotalCount.Should().Be(1);
        result.Data.Students.Items.Single().StudentCode.Should().Be("ST-2");
    }

    [Fact]
    public async Task Invalid_period_page_and_role_fail_closed()
    {
        await using var db = Database(); Seed(db);
        (await Service(db, "Student").GetInvoiceCoverageAsync(Query())).StatusCode.Should().Be(403);
        var request = Query(); request.BillingMonth = new DateOnly(2025, 12, 1);
        (await Service(db).GetInvoiceCoverageAsync(request)).StatusCode.Should().Be(409);
        request = Query(); request.PageSize = 101;
        (await Service(db).GetInvoiceCoverageAsync(request)).StatusCode.Should().Be(400);
        request = Query(); request.Status = "hidden";
        (await Service(db).GetInvoiceCoverageAsync(request)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Campus_restricted_accountant_requires_grant()
    {
        await using var db = Database(); Seed(db);
        (await Service(db, "Accountant").GetInvoiceCoverageAsync(Query())).StatusCode.Should().Be(404);
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = 41, UserId = 9, CampusId = 1, IsActive = true
        });
        await db.SaveChangesAsync();
        (await Service(db, "Accountant").GetInvoiceCoverageAsync(Query())).StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Corrupt_single_invoice_is_review_not_billed()
    {
        await using var db = Database(); Seed(db);
        var invoice = await db.StudentInvoices.SingleAsync(x => x.StudentEnrollmentId == 1);
        invoice.DueAmount = 99m;
        await db.SaveChangesAsync();
        var result = await Service(db).GetInvoiceCoverageAsync(Query());
        result.Data!.Billed.Should().Be(0);
        result.Data.Review.Should().Be(2);
    }

    [Fact]
    public async Task Missing_or_inconsistent_invoice_lines_are_reviewed_without_mutation()
    {
        await using var db = Database(); Seed(db, omitFirstLine: true, corruptSecondLine: true);
        var result = await Service(db).GetInvoiceCoverageAsync(Query());
        result.StatusCode.Should().Be(200);
        result.Data!.Billed.Should().Be(0);
        result.Data.Review.Should().Be(2);
        result.Data.Unbilled.Should().Be(1);
        var learner = result.Data.Students.Items.Single(x => x.StudentCode == "ST-1");
        learner.IntegrityIssueCount.Should().Be(1);
        learner.LineIssueCount.Should().Be(1);
        (await db.StudentInvoices.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Missing_invoice_line_remains_a_review_exception()
    {
        await using var db = Database(); Seed(db, omitFirstLine: true);
        var result = await Service(db).GetInvoiceCoverageAsync(Query());
        result.Data!.Students.Items.Single(x => x.StudentCode == "ST-1")
            .LineIssueCount.Should().Be(1);
    }

    private static void Seed(EduOSDbContext db, bool omitFirstLine = false,
        bool corruptSecondLine = false)
    {
        db.Tenants.Add(new Tenant { Id = 41, Name = "Institute", Code = "INST-41" });
        db.Campuses.Add(new Campus { Id = 1, TenantId = 41, Code = "C1", Name = "Main" });
        db.AcademicYears.Add(new AcademicYear { Id = 2, TenantId = 41, Name = "2026",
            Code = "2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) });
        db.AcademicBatches.Add(new AcademicBatch { Id = 1, TenantId = 41,
            CampusId = 1, AcademicYearId = 2, AcademicProgramId = 3, AcademicLevelId = 4,
            Code = "B1", Name = "Batch 1" });
        for (var i = 1; i <= 3; i++)
        {
            db.Students.Add(new Student { Id = i, TenantId = 41, PersonId = i,
                StudentCode = "ST-" + i, FullName = "Learner " + i, StatusCode = "Active" });
            db.StudentEnrollments.Add(new StudentEnrollment { Id = i, TenantId = 41,
                StudentId = i, CampusId = 1, AcademicYearId = 2, AcademicBatchId = 1,
                AcademicProgramId = 3, AcademicLevelId = 4, AcademicCurriculumId = 5,
                RollNo = "0" + i, IsCurrent = true, State = EnrollmentState.Active });
        }
        foreach (var (id, enrollmentId) in new[] { (1, 1), (2, 3), (3, 3) })
            db.StudentInvoices.Add(new StudentInvoice { Id = id, TenantId = 41,
                StudentEnrollmentId = enrollmentId, InvoiceNumber = "INV-" + id,
                InvoiceDate = new DateOnly(2026, 2, 1), DueDate = new DateOnly(2026, 2, 28),
                CurrencyCode = "BDT", Subtotal = 100m, TotalAmount = 100m, PaidAmount = 20m,
                DueAmount = 80m, State = InvoiceState.PartiallyPaid });
        foreach (var id in new[] { 1, 2, 3 })
        {
            if (id == 1 && omitFirstLine) continue;
            db.StudentInvoiceLines.Add(new StudentInvoiceLine { TenantId = 41,
                StudentInvoiceId = id, FeeHeadId = 1, Description = "Tuition",
                Amount = 100m, NetAmount = id == 2 && corruptSecondLine ? 99m : 100m });
        }
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
            .UseInMemoryDatabase("invoice-coverage-" + Guid.NewGuid().ToString("N"))
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
