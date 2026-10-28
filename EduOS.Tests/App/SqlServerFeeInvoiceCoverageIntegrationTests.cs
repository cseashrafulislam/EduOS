using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
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

/// <summary>Real SQL Server checks the correlated fee-line integrity query and tenant boundaries.</summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerFeeInvoiceCoverageIntegrationTests
{
    [Fact]
    public async Task Monthly_coverage_reconciles_invoice_lines_in_sql_and_never_posts_finance()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, batchId, invoiceId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-FEE-" + suffix, Name = "Fee Coverage CI",
                Email = "fee-" + suffix + "@example.test", State = TenantState.Active };
            var person = new Person { FullName = "Coverage CI Learner" };
            setup.Tenants.Add(tenant);
            setup.Persons.Add(person);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
            var personId = person.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
                var year = new AcademicYear { TenantId = tenantId, Name = "2026", Code = "2026",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
                var program = new AcademicProgram { TenantId = tenantId, Name = "Secondary", Code = "SEC" };
                var student = new Student { TenantId = tenantId, PersonId = personId,
                    FullName = "Coverage CI Learner", StudentCode = "COV-" + suffix,
                    StatusCode = "Active", AdmissionDate = new DateOnly(2026, 1, 1) };
                var head = new FeeHead { TenantId = tenantId, Code = "TUITION", Name = "Tuition" };
                setup.AddRange(campus, year, program, student, head);
                await setup.SaveChangesAsync();
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
                batchId = batch.Id;
                var enrollment = new StudentEnrollment { TenantId = tenantId, StudentId = student.Id,
                    CampusId = campus.Id, AcademicYearId = year.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, AcademicCurriculumId = curriculum.Id,
                    AcademicBatchId = batch.Id, RollNo = "01",
                    EnrollmentDate = new DateOnly(2026, 1, 1), IsCurrent = true,
                    State = EnrollmentState.Active, ClientRequestId = Guid.NewGuid() };
                setup.StudentEnrollments.Add(enrollment);
                await setup.SaveChangesAsync();
                var invoice = new StudentInvoice { TenantId = tenantId,
                    ClientRequestId = Guid.NewGuid(), StudentEnrollmentId = enrollment.Id,
                    InvoiceNumber = "CI-INV-" + suffix, InvoiceDate = new DateOnly(2026, 2, 1),
                    DueDate = new DateOnly(2026, 2, 28), Subtotal = 100m, TotalAmount = 100m,
                    DueAmount = 100m, CurrencyCode = "BDT", State = InvoiceState.Issued };
                setup.StudentInvoices.Add(invoice);
                await setup.SaveChangesAsync();
                invoiceId = invoice.Id;
                setup.StudentInvoiceLines.Add(new StudentInvoiceLine { TenantId = tenantId,
                    StudentInvoiceId = invoice.Id, FeeHeadId = head.Id, Description = "Tuition",
                    Amount = 100m, NetAmount = 100m });
                await setup.SaveChangesAsync();
            }
        }

        await using var db = new EduOSDbContext(options, Accessor(tenantId));
        var service = Service(db, tenantId);
        var request = new FeeInvoiceCoverageQueryDto {
            AcademicBatchId = batchId, BillingMonth = new DateOnly(2026, 2, 1) };
        var baseline = await service.GetInvoiceCoverageAsync(request);
        Assert.True(baseline.Success, baseline.Message);
        Assert.Equal(1, baseline.Data!.Billed);
        Assert.Equal(0, baseline.Data.Review);
        Assert.Equal(0, Assert.Single(baseline.Data.Students.Items).LineIssueCount);

        var line = await db.StudentInvoiceLines.SingleAsync(x => x.StudentInvoiceId == invoiceId);
        line.NetAmount = 99m;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var invalid = await service.GetInvoiceCoverageAsync(request);
        Assert.True(invalid.Success, invalid.Message);
        Assert.Equal(0, invalid.Data!.Billed);
        Assert.Equal(1, invalid.Data.Review);
        Assert.Equal(1, Assert.Single(invalid.Data.Students.Items).LineIssueCount);
        Assert.Equal(403, (await Service(db, tenantId + 1).GetInvoiceCoverageAsync(request)).StatusCode);
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
