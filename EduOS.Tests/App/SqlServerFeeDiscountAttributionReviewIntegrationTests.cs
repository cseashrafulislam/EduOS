using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Finance;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>SQL Server translation, tenant isolation and immutable historical evidence.</summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerFeeDiscountAttributionReviewIntegrationTests
{
    [Fact]
    public async Task Historical_discount_candidates_are_scoped_and_do_not_rewrite_posted_evidence()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, campusId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-ATTR-" + suffix, Name = "Fee Attribution CI",
                Email = "attr-" + suffix + "@example.test", State = TenantState.Active };
            var person = new Person { FullName = "Attribution CI Learner" };
            var reviewer = new ApplicationUser { UserName = "attr-review-" + suffix,
                NormalizedUserName = ("ATTR-REVIEW-" + suffix).ToUpperInvariant(),
                Email = "review-" + suffix + "@example.test" };
            setup.Tenants.Add(tenant);
            setup.Persons.Add(person);
            setup.Users.Add(reviewer);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
            var personId = person.Id;
            var reviewerId = reviewer.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
                var year = new AcademicYear { TenantId = tenantId, Name = "2026", Code = "2026",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
                var program = new AcademicProgram { TenantId = tenantId, Name = "Secondary", Code = "SEC" };
                var student = new Student { TenantId = tenantId, PersonId = personId,
                    FullName = "Attribution CI Learner", StudentCode = "ATTR-" + suffix,
                    StatusCode = "Active", AdmissionDate = new DateOnly(2026, 1, 1) };
                var tuition = new FeeHead { TenantId = tenantId, Code = "TUITION", Name = "Tuition" };
                var lab = new FeeHead { TenantId = tenantId, Code = "LAB", Name = "Lab" };
                setup.AddRange(campus, year, program, student, tuition, lab);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var level = new AcademicLevel { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Nine", Code = "G9", LevelNo = 9 };
                var curriculum = new AcademicCurriculum { TenantId = tenantId,
                    AcademicProgramId = program.Id, Name = "2026", Code = "SEC26",
                    EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true };
                setup.AddRange(level, curriculum);
                await setup.SaveChangesAsync();
                var batch = new AcademicBatch { TenantId = tenantId, CampusId = campus.Id,
                    AcademicYearId = year.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, Name = "Nine A", Code = "NINE-A", Capacity = 30 };
                setup.AcademicBatches.Add(batch);
                await setup.SaveChangesAsync();
                var enrollment = new StudentEnrollment { TenantId = tenantId, StudentId = student.Id,
                    CampusId = campus.Id, AcademicYearId = year.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, AcademicCurriculumId = curriculum.Id,
                    AcademicBatchId = batch.Id, RollNo = "01",
                    EnrollmentDate = new DateOnly(2026, 1, 1), IsCurrent = true,
                    State = EnrollmentState.Active, ClientRequestId = Guid.NewGuid() };
                setup.StudentEnrollments.Add(enrollment);
                await setup.SaveChangesAsync();
                var rule = new DiscountRule { TenantId = tenantId, Name = "Tuition award",
                    FeeHeadId = tuition.Id, Value = 10m, IsActive = true };
                setup.DiscountRules.Add(rule);
                await setup.SaveChangesAsync();
                setup.StudentDiscounts.Add(new StudentDiscount { TenantId = tenantId,
                    StudentEnrollmentId = enrollment.Id, DiscountRuleId = rule.Id,
                    EffectiveFrom = new DateOnly(2026, 1, 1), ApprovedByUserId = reviewerId,
                    IsActive = true });
                var invoice = new StudentInvoice { TenantId = tenantId,
                    ClientRequestId = Guid.NewGuid(), StudentEnrollmentId = enrollment.Id,
                    InvoiceNumber = "CI-ATTR-" + suffix, InvoiceDate = new DateOnly(2026, 2, 1),
                    DueDate = new DateOnly(2026, 2, 28), Subtotal = 120m,
                    DiscountAmount = 30m, TotalAmount = 90m, DueAmount = 90m,
                    CurrencyCode = "BDT", State = InvoiceState.Issued };
                setup.StudentInvoices.Add(invoice);
                await setup.SaveChangesAsync();
                setup.StudentInvoiceLines.AddRange(
                    new StudentInvoiceLine { TenantId = tenantId, StudentInvoiceId = invoice.Id,
                        FeeHeadId = tuition.Id, Description = "Tuition",
                        Amount = 100m, DiscountAmount = 10m, NetAmount = 90m },
                    new StudentInvoiceLine { TenantId = tenantId, StudentInvoiceId = invoice.Id,
                        FeeHeadId = lab.Id, Description = "Lab",
                        Amount = 20m, DiscountAmount = 20m, NetAmount = 0m });
                await setup.SaveChangesAsync();
            }
        }
        await using var db = new EduOSDbContext(options, Accessor(tenantId));
        var request = new FeeDiscountAttributionReviewQueryDto
        {
            CampusId = campusId, FromDate = new DateOnly(2026, 2, 1),
            ToDate = new DateOnly(2026, 2, 28)
        };
        var result = await Service(db, tenantId).GetDiscountAttributionReviewAsync(request);
        Assert.True(result.Success, result.Message);
        var row = Assert.Single(result.Data!.Invoices.Items);
        Assert.Equal("Possible cross-head attribution", row.EvidenceStatus);
        Assert.Equal(20m, row.PotentiallyMisattributedDiscount);
        Assert.Equal(90m, row.TotalAmount);
        Assert.Equal(403, (await Service(db, tenantId + 1)
            .GetDiscountAttributionReviewAsync(request)).StatusCode);
        Assert.Equal(1, await db.StudentInvoices.CountAsync());
        Assert.Equal(0, await db.StudentPayments.CountAsync());
        Assert.Equal(0, await db.Journals.CountAsync());
    }

    private static IHttpContextAccessor Accessor(long tenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return accessor;
    }
    private static FeeBillingService Service(EduOSDbContext db, long tenantId) => new(
        new GenericRepository<FeeStructure>(db), new GenericRepository<FeeStructureLine>(db),
        new GenericRepository<FeeHead>(db), new GenericRepository<StudentDiscount>(db),
        new GenericRepository<DiscountRule>(db), new GenericRepository<StudentInvoice>(db),
        new GenericRepository<StudentInvoiceLine>(db), new GenericRepository<StudentPayment>(db),
        new GenericRepository<PaymentAllocation>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Student>(db), new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicLevel>(db), new GenericRepository<AcademicBatch>(db),
        new GenericRepository<Campus>(db), new GenericRepository<Tenant>(db),
        db, new Actor(tenantId), TimeProvider.System, NullLogger<FeeBillingService>.Instance);
    private sealed class Actor(long tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9001;
        public long TenantId => tenantId;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
