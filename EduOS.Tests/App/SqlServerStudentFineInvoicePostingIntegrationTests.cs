using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
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
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
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
        var persistenceErrors = new ConcurrentQueue<string>();
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).LogTo(message => persistenceErrors.Enqueue(message), LogLevel.Error)
            .Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, fineId, periodId, assessorUserId, postingUserId, checkerUserId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "FINE-" + suffix, Name = "Fine Posting CI",
                Email = "fine-" + suffix + "@example.test", State = TenantState.Active };
            var person = new Person { FullName = "Fine CI Student" };
            setup.AddRange(tenant, person);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
            setup.ChangeTracker.Clear();
            // SQL Server enforces Journals.ApprovedByUserId/PostedByUserId -> AspNetUsers.
            // Each simulated actor must therefore correspond to a persisted identity row.
            var assessor = SeedUser("assessor", suffix);
            var postingOfficer = SeedUser("poster", suffix);
            var checkerUser = SeedUser("checker", suffix);
            setup.ApplicationUsers.AddRange(assessor, postingOfficer, checkerUser);
            await setup.SaveChangesAsync();
            assessorUserId = assessor.Id;
            postingUserId = postingOfficer.Id;
            checkerUserId = checkerUser.Id;
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
                // Simulate the original fine being assessed by a different real user.
                await setup.StudentFines.Where(x => x.Id == fineId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedBy, assessorUserId));
            }
        }
        await using var db = new EduOSDbContext(options, Accessor(tenantId));
        var service = new FinanceAdjustmentService(db, new Actor(tenantId, postingUserId), TimeProvider.System);
        var fineRow = await db.StudentFines.AsNoTracking().SingleAsync(x => x.Id == fineId);
        var request = new PostStudentFineInvoiceRequestDto { AccountingPeriodId = periodId,
            RowVersion = Convert.ToBase64String(fineRow.RowVersion) };
        var first = await service.PostFineInvoiceAsync(fineId, request);
        Assert.True(first.Success, first.Message + " | Persistence evidence: " +
            string.Join(" | ", persistenceErrors.TakeLast(5)));
        var second = await service.PostFineInvoiceAsync(fineId, request);
        Assert.True(second.Success, second.Message);
        Assert.True(second.Data!.AlreadyPosted);
        Assert.Equal(first.Data!.InvoiceReference, second.Data.InvoiceReference);
        Assert.Equal(1, await db.StudentInvoiceLines.CountAsync(x => x.StudentFineId == fineId));
        var journalId = (await db.StudentInvoices.SingleAsync(x => x.PublicId == first.Data.InvoiceReference)).JournalId;
        var lines = await db.JournalLines.AsNoTracking().Where(x => x.JournalId == journalId).ToListAsync();
        Assert.Equal(75m, lines.Sum(x => x.DebitAmount));
        Assert.Equal(75m, lines.Sum(x => x.CreditAmount));
        Assert.Equal(403, (await new FinanceAdjustmentService(db, new Actor(tenantId + 1, postingUserId), TimeProvider.System)
            .PostFineInvoiceAsync(fineId, request)).StatusCode);

        // Real SQL Server rowversion, maker/checker and immutable contra journal proof.
        var reversalRequest = new RequestStudentFineReversalDto
        {
            ClientRequestId = Guid.NewGuid(), AccountingPeriodId = periodId,
            ReversalDate = new DateOnly(2026, 10, 29), Reason = "Approved historical correction",
            RowVersion = Convert.ToBase64String(fineRow.RowVersion)
        };
        var proposed = await service.RequestFineReversalAsync(fineId, reversalRequest);
        Assert.True(proposed.Success, proposed.Message);
        Assert.NotEmpty(proposed.Data!.RowVersion);
        Assert.Equal(JournalState.Submitted, proposed.Data.State);
        var approval = new ReviewStudentFineReversalDto
        { Approve = true, RowVersion = proposed.Data.RowVersion };
        Assert.Equal(403, (await service.ReviewFineReversalAsync(fineId,
            proposed.Data.ReversalReference, approval)).StatusCode);
        var checker = new FinanceAdjustmentService(db, new Actor(tenantId, checkerUserId), TimeProvider.System);
        var postedReversal = await checker.ReviewFineReversalAsync(fineId,
            proposed.Data.ReversalReference, approval);
        Assert.True(postedReversal.Success, postedReversal.Message);
        Assert.Equal(JournalState.Posted, postedReversal.Data!.State);
        Assert.Equal(InvoiceState.Cancelled, (await db.StudentInvoices.AsNoTracking()
            .SingleAsync(x => x.PublicId == first.Data.InvoiceReference)).State);
        Assert.True((await db.StudentFines.AsNoTracking().SingleAsync(x => x.Id == fineId)).IsWaived);
        Assert.Equal(2, await db.Journals.CountAsync(x => x.TenantId == tenantId &&
            (x.SourceType == "StudentInvoice" || x.SourceType == "StudentFineReversal")));
        var reversalId = (await db.Journals.AsNoTracking()
            .SingleAsync(x => x.PublicId == proposed.Data.ReversalReference)).Id;
        var reversalLines = await db.JournalLines.AsNoTracking()
            .Where(x => x.JournalId == reversalId).ToListAsync();
        Assert.Equal(75m, reversalLines.Sum(x => x.DebitAmount));
        Assert.Equal(75m, reversalLines.Sum(x => x.CreditAmount));
        var reconciled = await new FeeReceivablesAgingService(db, new Actor(tenantId, checkerUserId))
            .GetReconciliationAsync(null, null, 1, 25);
        Assert.True(reconciled.Success, reconciled.Message);
        Assert.Equal("Reversed", Assert.Single(reconciled.Data!.Rows).Status);
        Assert.Equal(0m, Assert.Single(reconciled.Data.Rows).DueAmount);
    }

    private static ApplicationUser SeedUser(string role, string suffix)
    {
        var username = $"fine-{role}-{suffix}";
        return new ApplicationUser
        {
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
            Email = username + "@example.test",
            NormalizedEmail = (username + "@example.test").ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            FullName = "Fine CI " + role
        };
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
