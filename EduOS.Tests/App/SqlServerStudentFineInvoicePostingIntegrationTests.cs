using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Runs against a disposable, migrated SQL Server when EDUOS_SQL_AUTH_TEST_CONNECTION is available.</summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerStudentFineInvoicePostingIntegrationTests
{
    [Fact]
    public async Task Dedicated_fine_is_tenant_isolated_uniquely_billed_and_balanced_in_real_sql()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, fineId, periodId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "FINE-" + suffix, Name = "Fine Posting CI",
                Email = "fine-" + suffix + "@example.test", State = TenantState.Active };
            var person = new Person { FullName = "Fine CI Student" };
            setup.AddRange(tenant, person);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
                var year = new AcademicYear { TenantId = tenantId, Name = "2026", Code = "2026",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
                var program = new AcademicProgram { TenantId = tenantId, Name = "General", Code = "GEN" };
                var student = new Student { TenantId = tenantId, PersonId = person.Id,
                    FullName = "Fine CI Student", StudentCode = "FINE-" + suffix,
                    AdmissionDate = new DateOnly(2026, 1, 1) };
                var asset = new Account { TenantId = tenantId, Code = "FAR", Name = "Fine receivable",
                    AccountType = AccountType.Asset };
                var income = new Account { TenantId = tenantId, Code = "FINCOME", Name = "Fine income",
                    AccountType = AccountType.Income };
                var period = new AccountingPeriod { TenantId = tenantId, Name = "October 2026",
                    StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31) };
                setup.AddRange(campus, year, program, student, asset, income, period);
                await setup.SaveChangesAsync();
                periodId = period.Id;
                var level = new AcademicLevel { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Level 1", Code = "L1", LevelNo = 1 };
                var curriculum = new AcademicCurriculum { TenantId = tenantId,
                    AcademicProgramId = program.Id, Name = "2026", Code = "C2026",
                    EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true };
                var head = new FeeHead { TenantId = tenantId, Code = "FINE", Name = "Late fine",
                    DefaultFrequency = FeeFrequencyType.OneTime,
                    IncomeAccountId = income.Id, ReceivableAccountId = asset.Id };
                setup.AddRange(level, curriculum, head);
                await setup.SaveChangesAsync();
                var batch = new AcademicBatch { TenantId = tenantId, CampusId = campus.Id,
                    AcademicYearId = year.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, Name = "Batch", Code = "BATCH", Capacity = 30 };
                setup.AcademicBatches.Add(batch);
                await setup.SaveChangesAsync();
                var enrollment = new StudentEnrollment { TenantId = tenantId, StudentId = student.Id,
                    CampusId = campus.Id, AcademicYearId = year.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, AcademicCurriculumId = curriculum.Id,
                    AcademicBatchId = batch.Id, RollNo = "01", EnrollmentDate = new DateOnly(2026, 1, 1),
                    ClientRequestId = Guid.NewGuid() };
                var rule = new FineRule { TenantId = tenantId, Name = "Late return",
                    FeeHeadId = head.Id, Amount = 75m };
                setup.AddRange(enrollment, rule);
                await setup.SaveChangesAsync();
                var fine = new StudentFine { TenantId = tenantId, StudentEnrollmentId = enrollment.Id,
                    FineRuleId = rule.Id, FineDate = new DateOnly(2026, 10, 25), Amount = 75m,
                    ClientRequestId = Guid.NewGuid() };
                setup.StudentFines.Add(fine);
                await setup.SaveChangesAsync();
                fineId = fine.Id;
                // System seeding has no user claims; simulate a fine assessed by administrator 100.
                await setup.StudentFines.Where(x => x.Id == fineId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedBy, 100L));
            }
        }
        await using var db = new EduOSDbContext(options, Accessor(tenantId));
        var service = new FinanceAdjustmentService(db, new Actor(tenantId, 200), TimeProvider.System);
        var fineRow = await db.StudentFines.AsNoTracking().SingleAsync(x => x.Id == fineId);
        var request = new PostStudentFineInvoiceRequestDto { AccountingPeriodId = periodId,
            RowVersion = Convert.ToBase64String(fineRow.RowVersion) };
        var first = await service.PostFineInvoiceAsync(fineId, request);
        Assert.True(first.Success, first.Message);
        var second = await service.PostFineInvoiceAsync(fineId, request);
        Assert.True(second.Success, second.Message);
        Assert.True(second.Data!.AlreadyPosted);
        Assert.Equal(first.Data!.InvoiceReference, second.Data.InvoiceReference);
        Assert.Equal(1, await db.StudentInvoiceLines.CountAsync(x => x.StudentFineId == fineId));
        var journalId = (await db.StudentInvoices.SingleAsync(x => x.PublicId == first.Data.InvoiceReference)).JournalId;
        var lines = await db.JournalLines.AsNoTracking().Where(x => x.JournalId == journalId).ToListAsync();
        Assert.Equal(75m, lines.Sum(x => x.DebitAmount));
        Assert.Equal(75m, lines.Sum(x => x.CreditAmount));
        Assert.Equal(403, (await new FinanceAdjustmentService(db, new Actor(tenantId + 1, 200), TimeProvider.System)
            .PostFineInvoiceAsync(fineId, request)).StatusCode);
    }

    private static IHttpContextAccessor Accessor(long tenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return accessor;
    }

    private sealed class Actor(long tenantId, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
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
