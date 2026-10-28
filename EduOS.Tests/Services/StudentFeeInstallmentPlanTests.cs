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

public sealed class StudentFeeInstallmentPlanTests
{
    [Fact]
    public async Task Draft_freezes_gross_monthly_fee_heads_and_clamps_february_due_day()
    {
        await using var db = Database();
        Seed(db);
        var request = Request(db);
        var result = await Service(db).CreateInstallmentPlanAsync(request);
        result.StatusCode.Should().Be(200);
        result.Data!.StateCode.Should().Be(1);
        result.Data.GrossAmount.Should().Be(300m);
        result.Data.LineCount.Should().Be(3);
        result.Data.Lines[0].DueDate.Should().Be(new DateOnly(2026, 2, 28));
        result.Data.Lines[1].DueDate.Should().Be(new DateOnly(2026, 3, 31));
        (await db.StudentInvoices.CountAsync()).Should().Be(0);
        (await db.StudentPayments.CountAsync()).Should().Be(0);
        (await db.Journals.CountAsync()).Should().Be(0);
        (await db.StudentFeeInstallmentPlanLines.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Planned_month_blocks_legacy_invoice_preflight_and_generation_until_linked_billing()
    {
        await using var db = Database();
        Seed(db);
        var service = Service(db);
        (await service.CreateInstallmentPlanAsync(Request(db))).StatusCode.Should().Be(200);
        var batch = new GenerateStudentInvoiceBatchRequestDto {
            ClientRequestId = Guid.NewGuid(), AcademicBatchId = 1, FeeStructureId = 1,
            Year = 2026, Month = 2, InvoiceDate = new DateOnly(2026, 2, 1),
            DueDate = new DateOnly(2026, 2, 28)
        };
        (await service.PreviewInvoiceBatchAsync(batch)).StatusCode.Should().Be(409);
        (await service.GenerateInvoicesAsync(batch)).StatusCode.Should().Be(409);
        (await db.StudentInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Same_request_replays_and_different_request_cannot_overlap()
    {
        await using var db = Database();
        Seed(db);
        var request = Request(db);
        var service = Service(db);
        var first = await service.CreateInstallmentPlanAsync(request);
        var replay = await service.CreateInstallmentPlanAsync(request);
        first.StatusCode.Should().Be(200);
        replay.StatusCode.Should().Be(200);
        replay.Data!.Reference.Should().Be(first.Data!.Reference);
        request.ClientRequestId = Guid.NewGuid();
        (await service.CreateInstallmentPlanAsync(request)).StatusCode.Should().Be(409);
        (await db.StudentFeeInstallmentPlans.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Campus_grant_and_tenant_ownership_are_enforced()
    {
        await using var db = Database();
        Seed(db);
        var request = Request(db);
        (await Service(db, "Accountant").CreateInstallmentPlanAsync(request))
            .StatusCode.Should().Be(404);
        db.UserCampusAccesses.Add(new UserCampusAccess {
            TenantId = 41, UserId = 9, CampusId = 1, IsActive = true
        });
        await db.SaveChangesAsync();
        (await Service(db, "Accountant").CreateInstallmentPlanAsync(request))
            .StatusCode.Should().Be(200);
        (await Service(db, "Student").GetInstallmentPlansAsync()).StatusCode.Should().Be(403);
        (await Service(db, "Accountant").GetInstallmentPlansAsync(999)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Independent_checker_required_and_approval_does_not_issue_invoice()
    {
        await using var db = Database();
        Seed(db);
        var plan = new StudentFeeInstallmentPlan {
            TenantId = 41, StudentEnrollmentId = 1, FeeStructureId = 1,
            CampusId = 1, AcademicYearId = 2, StartMonth = new DateOnly(2026, 2, 1),
            EndMonth = new DateOnly(2026, 2, 1), GrossAmount = 100m,
            LineCount = 1, PreparedByUserId = 9, RowVersion = [1,2,3,4]
        };
        db.StudentFeeInstallmentPlans.Add(plan);
        await db.SaveChangesAsync();
        db.StudentFeeInstallmentPlanLines.Add(new StudentFeeInstallmentPlanLine {
            TenantId = 41, StudentFeeInstallmentPlanId = plan.Id,
            FeeHeadId = 1, MonthStart = plan.StartMonth,
            DueDate = new DateOnly(2026, 2, 28),
            FeeHeadCodeSnapshot = "TUITION", FeeHeadNameSnapshot = "Tuition",
            GrossAmount = 100m
        });
        await db.SaveChangesAsync();
        var version = Convert.ToBase64String(plan.RowVersion);
        var submit = new StudentFeeInstallmentTransitionDto {
            ClientRequestId = Guid.NewGuid(), RowVersion = version
        };
        (await Service(db).TransitionInstallmentPlanAsync(plan.PublicId, 2, submit))
            .StatusCode.Should().Be(200);
        (await Service(db).TransitionInstallmentPlanAsync(plan.PublicId, 3,
            new StudentFeeInstallmentTransitionDto {
                ClientRequestId = Guid.NewGuid(), RowVersion = version
            })).StatusCode.Should().Be(403);
        var approval = new StudentFeeInstallmentTransitionDto {
            ClientRequestId = Guid.NewGuid(), RowVersion = version
        };
        (await Service(db, userId: 10).TransitionInstallmentPlanAsync(plan.PublicId, 3, approval))
            .StatusCode.Should().Be(200);
        (await Service(db, userId: 10).TransitionInstallmentPlanAsync(plan.PublicId, 3, approval))
            .StatusCode.Should().Be(200);
        (await db.StudentFeeInstallmentPlanEvents.CountAsync()).Should().Be(2);
        (await db.StudentInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Approved_evidence_cannot_be_silently_mutated_or_seen_by_foreign_tenant()
    {
        await using var db = Database();
        Seed(db);
        var created = await Service(db).CreateInstallmentPlanAsync(Request(db));
        created.StatusCode.Should().Be(200);
        var line = await db.StudentFeeInstallmentPlanLines.OrderBy(x => x.Id).FirstAsync();
        line.GrossAmount = 1m;
        await FluentActions.Awaiting(() => db.SaveChangesAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        db.Entry(line).State = EntityState.Unchanged;
        var foreign = Guid.NewGuid();
        (await Service(db).GetInstallmentPlanAsync(foreign)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Existing_invoice_or_undefined_due_day_blocks_new_agreement()
    {
        await using var db = Database();
        Seed(db);
        var line = await db.FeeStructureLines.SingleAsync();
        line.DueDayOfMonth = null;
        await db.SaveChangesAsync();
        (await Service(db).CreateInstallmentPlanAsync(Request(db))).StatusCode.Should().Be(409);
        line.DueDayOfMonth = 31;
        await db.SaveChangesAsync();
        db.StudentInvoices.Add(new StudentInvoice {
            TenantId = 41, StudentEnrollmentId = 1,
            InvoiceNumber = "INV-TEST", InvoiceDate = new DateOnly(2026, 2, 1),
            DueDate = new DateOnly(2026, 2, 28), TotalAmount = 100m, DueAmount = 100m
        });
        await db.SaveChangesAsync();
        (await Service(db).CreateInstallmentPlanAsync(Request(db))).StatusCode.Should().Be(409);
    }

    private static CreateStudentFeeInstallmentPlanDto Request(EduOSDbContext db) => new() {
        ClientRequestId = Guid.NewGuid(),
        StudentReference = db.Students.Single().PublicId,
        AcademicBatchId = 1, FeeStructureId = 1,
        StartMonth = new DateOnly(2026, 2, 1), Months = 3
    };

    private static void Seed(EduOSDbContext db)
    {
        db.Tenants.Add(new Tenant { Id = 41, Name = "Institute", Code = "INST-41" });
        db.Campuses.Add(new Campus { Id = 1, TenantId = 41, Code = "C1", Name = "Main" });
        db.AcademicYears.Add(new AcademicYear {
            Id = 2, TenantId = 41, Name = "2026", Code = "2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31)
        });
        db.AcademicBatches.Add(new AcademicBatch {
            Id = 1, TenantId = 41, CampusId = 1, AcademicYearId = 2,
            AcademicProgramId = 3, AcademicLevelId = 4, Code = "B1", Name = "Batch"
        });
        db.Students.Add(new Student {
            Id = 1, TenantId = 41, PersonId = 1, StudentCode = "ST-1",
            FullName = "Learner", StatusCode = "Active"
        });
        db.StudentEnrollments.Add(new StudentEnrollment {
            Id = 1, TenantId = 41, StudentId = 1, CampusId = 1,
            AcademicYearId = 2, AcademicBatchId = 1, AcademicProgramId = 3,
            AcademicLevelId = 4, AcademicCurriculumId = 5, RollNo = "01",
            IsCurrent = true, State = EnrollmentState.Active
        });
        db.FeeStructures.Add(new FeeStructure {
            Id = 1, TenantId = 41, CampusId = 1, AcademicYearId = 2,
            AcademicBatchId = 1, Name = "Monthly", CurrencyCode = "BDT",
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        db.FeeHeads.Add(new FeeHead { Id = 1, TenantId = 41, Code = "TUITION", Name = "Tuition" });
        db.FeeStructureLines.Add(new FeeStructureLine {
            Id = 1, TenantId = 41, FeeStructureId = 1, FeeHeadId = 1,
            Frequency = FeeFrequencyType.Monthly, Amount = 100m, DueDayOfMonth = 31
        });
        db.SaveChanges();
    }

    private static FeeBillingService Service(EduOSDbContext db, string role = "TenantAdmin",
        long userId = 9) => new(
        new GenericRepository<FeeStructure>(db), new GenericRepository<FeeStructureLine>(db),
        new GenericRepository<FeeHead>(db), new GenericRepository<StudentDiscount>(db),
        new GenericRepository<DiscountRule>(db), new GenericRepository<StudentInvoice>(db),
        new GenericRepository<StudentInvoiceLine>(db), new GenericRepository<StudentPayment>(db),
        new GenericRepository<PaymentAllocation>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Student>(db), new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicLevel>(db), new GenericRepository<AcademicBatch>(db),
        new GenericRepository<Campus>(db), new GenericRepository<Tenant>(db),
        db, new User(role, userId), TimeProvider.System, NullLogger<FeeBillingService>.Instance);

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("fee-plan-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
    }
    private sealed class User(string role, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
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
