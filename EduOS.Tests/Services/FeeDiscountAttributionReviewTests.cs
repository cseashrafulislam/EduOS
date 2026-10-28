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

public sealed class FeeDiscountAttributionReviewTests
{
    private static FeeDiscountAttributionReviewQueryDto Query() => new()
    {
        CampusId = 1, FromDate = new DateOnly(2026, 2, 1),
        ToDate = new DateOnly(2026, 2, 28)
    };

    [Fact]
    public async Task Flags_candidate_cross_head_discount_without_mutating_invoice_or_journal()
    {
        await using var db = Database(); Seed(db);
        var result = await Service(db).GetDiscountAttributionReviewAsync(Query());
        result.StatusCode.Should().Be(200);
        result.Data!.Invoices.TotalCount.Should().Be(2);
        var flagged = result.Data.Invoices.Items.Single(x => x.InvoiceNumber == "INV-1");
        flagged.EvidenceStatus.Should().Be("Possible cross-head attribution");
        flagged.PotentiallyMisattributedDiscount.Should().Be(20m);
        flagged.NeedsReview.Should().BeTrue();
        (await db.StudentInvoices.SingleAsync(x => x.Id == 1)).TotalAmount.Should().Be(90m);
        (await db.Journals.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Global_award_is_not_falsely_assigned_to_a_specific_head()
    {
        await using var db = Database(); Seed(db);
        var rule = await db.DiscountRules.SingleAsync();
        rule.FeeHeadId = null;
        await db.SaveChangesAsync();
        var result = await Service(db).GetDiscountAttributionReviewAsync(Query());
        var row = result.Data!.Invoices.Items.Single(x => x.InvoiceNumber == "INV-1");
        row.PotentiallyMisattributedDiscount.Should().Be(0m);
        row.EvidenceStatus.Should().Be("Global/mixed awards: manual review");
    }

    [Fact]
    public async Task Missing_historical_award_and_invalid_line_are_separate_review_signals()
    {
        await using var db = Database(); Seed(db);
        var award = await db.StudentDiscounts.SingleAsync();
        award.IsDeleted = true;
        var line = await db.StudentInvoiceLines.SingleAsync(x => x.StudentInvoiceId == 2);
        line.NetAmount = 88m;
        await db.SaveChangesAsync();
        var report = (await Service(db).GetDiscountAttributionReviewAsync(Query())).Data!;
        report.Invoices.Items.Single(x => x.InvoiceNumber == "INV-1")
            .EvidenceStatus.Should().Be("Historical award evidence unavailable");
        report.Invoices.Items.Single(x => x.InvoiceNumber == "INV-2")
            .EvidenceStatus.Should().Be("Header/line integrity exception");
    }

    [Fact]
    public async Task Role_campus_and_range_controls_fail_closed()
    {
        await using var db = Database(); Seed(db);
        (await Service(db, "Student").GetDiscountAttributionReviewAsync(Query()))
            .StatusCode.Should().Be(403);
        (await Service(db, "Accountant").GetDiscountAttributionReviewAsync(Query()))
            .StatusCode.Should().Be(404);
        var request = Query(); request.CampusId = 999;
        (await Service(db).GetDiscountAttributionReviewAsync(request)).StatusCode.Should().Be(404);
        request = Query(); request.ToDate = new DateOnly(2026, 7, 1);
        (await Service(db).GetDiscountAttributionReviewAsync(request)).StatusCode.Should().Be(400);
        request = Query(); request.PageSize = 101;
        (await Service(db).GetDiscountAttributionReviewAsync(request)).StatusCode.Should().Be(400);
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = 41, UserId = 9, CampusId = 1, IsActive = true
        });
        await db.SaveChangesAsync();
        (await Service(db, "Accountant").GetDiscountAttributionReviewAsync(Query()))
            .StatusCode.Should().Be(200);
    }

    private static void Seed(EduOSDbContext db)
    {
        db.Tenants.Add(new Tenant { Id = 41, Name = "Institute", Code = "INST-41" });
        db.Campuses.Add(new Campus { Id = 1, TenantId = 41, Code = "C1", Name = "Main" });
        db.Students.Add(new Student { Id = 1, TenantId = 41, PersonId = 1,
            StudentCode = "ST-1", FullName = "Learner", StatusCode = "Active" });
        db.StudentEnrollments.Add(new StudentEnrollment { Id = 1, TenantId = 41,
            StudentId = 1, CampusId = 1, AcademicYearId = 2, AcademicBatchId = 3,
            AcademicProgramId = 4, AcademicLevelId = 5, AcademicCurriculumId = 6,
            RollNo = "01", IsCurrent = true, State = EnrollmentState.Active });
        db.FeeHeads.AddRange(
            new FeeHead { Id = 1, TenantId = 41, Code = "TUITION", Name = "Tuition" },
            new FeeHead { Id = 2, TenantId = 41, Code = "LAB", Name = "Lab" });
        db.DiscountRules.Add(new DiscountRule { Id = 1, TenantId = 41, Name = "Tuition award",
            FeeHeadId = 1, Value = 10m });
        db.StudentDiscounts.Add(new StudentDiscount { Id = 1, TenantId = 41,
            StudentEnrollmentId = 1, DiscountRuleId = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1), ApprovedByUserId = 9 });
        db.StudentInvoices.AddRange(
            new StudentInvoice { Id = 1, TenantId = 41, StudentEnrollmentId = 1,
                InvoiceNumber = "INV-1", InvoiceDate = new DateOnly(2026, 2, 1),
                DueDate = new DateOnly(2026, 2, 28), CurrencyCode = "BDT",
                Subtotal = 120m, DiscountAmount = 30m, TotalAmount = 90m, DueAmount = 90m },
            new StudentInvoice { Id = 2, TenantId = 41, StudentEnrollmentId = 1,
                InvoiceNumber = "INV-2", InvoiceDate = new DateOnly(2026, 2, 2),
                DueDate = new DateOnly(2026, 2, 28), CurrencyCode = "BDT",
                Subtotal = 100m, TotalAmount = 100m, DueAmount = 100m });
        db.StudentInvoiceLines.AddRange(
            new StudentInvoiceLine { TenantId = 41, StudentInvoiceId = 1, FeeHeadId = 1,
                Description = "Tuition", Amount = 100m, DiscountAmount = 10m, NetAmount = 90m },
            new StudentInvoiceLine { TenantId = 41, StudentInvoiceId = 1, FeeHeadId = 2,
                Description = "Lab", Amount = 20m, DiscountAmount = 20m, NetAmount = 0m },
            new StudentInvoiceLine { TenantId = 41, StudentInvoiceId = 2, FeeHeadId = 1,
                Description = "Tuition", Amount = 100m, NetAmount = 100m });
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
            .UseInMemoryDatabase("discount-attribution-" + Guid.NewGuid().ToString("N"))
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
