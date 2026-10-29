using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollRunCancellationServiceTests
{
    [Fact]
    public async Task Cancellation_of_approved_run_preserves_snapshots_and_appends_reasoned_audit()
    {
        await using var db = Database();
        var run = await SeedRun(db);
        db.PayrollEmployees.Add(new PayrollEmployee
        { Id = 17, TenantId = 41, PayrollRunId = run.Id, EmployeeId = 3, NetAmount = 100m });
        await db.SaveChangesAsync();

        var service = Service(db);
        var first = await service.CancelAsync(run.PublicId, Request());
        first.StatusCode.Should().Be(200);
        first.Data!.State.Should().Be(PayrollRunState.Cancelled);
        first.Data.AlreadyCancelled.Should().BeFalse();
        (await db.PayrollRuns.SingleAsync()).State.Should().Be(PayrollRunState.Cancelled);
        (await db.PayrollEmployees.SingleAsync()).NetAmount.Should().Be(100m);
        var audit = await db.AuditLogs.SingleAsync(x => x.Action == "CancelUnpostedPayroll");
        audit.TenantId.Should().Be(41);
        audit.UserId.Should().Be(9);
        audit.EntityId.Should().Be(run.Id);
        audit.OldValue.Should().Be("Approved");
        audit.NewValue.Should().Be("Duplicate monthly payroll created in error.");

        var replay = await service.CancelAsync(run.PublicId, Request());
        replay.StatusCode.Should().Be(200);
        replay.Data!.AlreadyCancelled.Should().BeTrue();
        (await db.AuditLogs.CountAsync(x => x.Action == "CancelUnpostedPayroll")).Should().Be(1);
    }

    [Theory]
    [InlineData(PayrollRunState.Draft)]
    [InlineData(PayrollRunState.Calculated)]
    public async Task Draft_or_calculated_run_may_be_cancelled_without_posting(PayrollRunState state)
    {
        await using var db = Database();
        var run = await SeedRun(db, state);
        (await Service(db).CancelAsync(run.PublicId, Request())).StatusCode.Should().Be(200);
        (await db.Journals.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Stale_version_and_different_retry_reason_are_rejected()
    {
        await using var db = Database();
        var run = await SeedRun(db);
        var stale = await Service(db).CancelAsync(run.PublicId,
            new CancelPayrollRunRequestDto { RowVersion = "Ag==", Reason = Request().Reason });
        stale.StatusCode.Should().Be(409);
        (await db.PayrollRuns.SingleAsync()).State.Should().Be(PayrollRunState.Approved);
        (await Service(db).CancelAsync(run.PublicId, Request())).StatusCode.Should().Be(200);
        var conflict = await Service(db).CancelAsync(run.PublicId,
            new CancelPayrollRunRequestDto { RowVersion = "AQ==", Reason = "A completely unrelated cancellation reason." });
        conflict.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Linked_or_orphaned_posted_journal_blocks_cancellation()
    {
        await using var db = Database();
        var run = await SeedRun(db);
        db.Journals.Add(new Journal { Id = 100, TenantId = 41, JournalNumber = "GL-100",
            SourceType = "PayrollRun", SourceId = run.Id, State = JournalState.Posted });
        await db.SaveChangesAsync();
        (await Service(db).CancelAsync(run.PublicId, Request())).StatusCode.Should().Be(409);
        (await db.PayrollRuns.SingleAsync()).State.Should().Be(PayrollRunState.Approved);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Legacy_soft_deleted_financial_history_cannot_be_ignored(bool journal)
    {
        await using var db = Database();
        var run = await SeedRun(db);
        if (journal)
            db.Journals.Add(new Journal { Id = 100, TenantId = 41, JournalNumber = "GL-HIST",
                SourceType = "PayrollRun", SourceId = run.Id, State = JournalState.Posted,
                IsDeleted = true });
        else
        {
            db.PayrollEmployees.Add(new PayrollEmployee
            { Id = 17, TenantId = 41, PayrollRunId = run.Id, EmployeeId = 3, NetAmount = 100m,
                IsDeleted = true });
            db.PayrollPayments.Add(new PayrollPayment
            { Id = 21, TenantId = 41, PayrollEmployeeId = 17, ClientRequestId = Guid.NewGuid(),
                PaymentDate = new DateOnly(2026, 10, 31), Amount = 100m,
                State = PaymentState.Successful, IsDeleted = true });
        }
        await db.SaveChangesAsync();
        (await Service(db).CancelAsync(run.PublicId, Request())).StatusCode.Should().Be(409);
        (await db.PayrollRuns.SingleAsync()).State.Should().Be(PayrollRunState.Approved);
    }

    [Fact]
    public async Task Posted_run_is_immutable_even_without_linked_journal()
    {
        await using var db = Database();
        var run = await SeedRun(db, PayrollRunState.Posted);
        (await Service(db).CancelAsync(run.PublicId, Request())).StatusCode.Should().Be(409);
        (await db.PayrollRuns.SingleAsync()).State.Should().Be(PayrollRunState.Posted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Payment_or_loan_recovery_history_blocks_cancellation(bool payment)
    {
        await using var db = Database();
        var run = await SeedRun(db);
        db.PayrollEmployees.Add(new PayrollEmployee
        { Id = 17, TenantId = 41, PayrollRunId = run.Id, EmployeeId = 3, NetAmount = 100m });
        if (payment)
            db.PayrollPayments.Add(new PayrollPayment
            { Id = 21, TenantId = 41, PayrollEmployeeId = 17, ClientRequestId = Guid.NewGuid(),
                PaymentDate = new DateOnly(2026, 10, 31), Amount = 100m, State = PaymentState.Initiated });
        else
            db.LoanAdvanceRecoveries.Add(new LoanAdvanceRecovery
            { Id = 22, TenantId = 41, LoanAdvanceId = 30, PayrollEmployeeId = 17,
                ClientRequestId = Guid.NewGuid(), RecoveryDate = new DateOnly(2026, 10, 31), Amount = 10m });
        await db.SaveChangesAsync();
        (await Service(db).CancelAsync(run.PublicId, Request())).StatusCode.Should().Be(409);
        (await db.PayrollRuns.SingleAsync()).State.Should().Be(PayrollRunState.Approved);
    }

    [Fact]
    public async Task Tenant_isolation_and_role_checks_block_unauthorized_cancellation()
    {
        var databaseName = "cross-tenant-payroll-" + Guid.NewGuid().ToString("N");
        await using var db = Database(databaseName);
        var run = await SeedRun(db);
        (await new PayrollRunCancellationService(db, new TestUser("Accountant", 41), TimeProvider.System)
            .CancelAsync(run.PublicId, Request())).StatusCode.Should().Be(403);
        (await new PayrollRunCancellationService(db, new TestUser("TenantAdmin", 42), TimeProvider.System)
            .CancelAsync(run.PublicId, Request())).StatusCode.Should().Be(403);
        var foreign = new PayrollRun { Id = 55, TenantId = 42, PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(), RunNumber = "FOREIGN", Year = 2026, Month = 10,
            PeriodStart = new DateOnly(2026, 10, 1), PeriodEnd = new DateOnly(2026, 10, 31),
            State = PayrollRunState.Approved, RowVersion = [1] };
        await using (var tenant42Db = Database(databaseName, 42))
        {
            tenant42Db.PayrollRuns.Add(foreign);
            await tenant42Db.SaveChangesAsync();
        }
        (await Service(db).CancelAsync(foreign.PublicId, Request())).StatusCode.Should().Be(404);
        (await db.PayrollRuns.SingleAsync()).State.Should().Be(PayrollRunState.Approved);
    }

    [Fact]
    public async Task Invalid_reason_never_changes_payroll()
    {
        await using var db = Database();
        var run = await SeedRun(db);
        (await Service(db).CancelAsync(run.PublicId,
            new CancelPayrollRunRequestDto { RowVersion = "AQ==", Reason = "short" })).StatusCode.Should().Be(400);
        (await db.PayrollRuns.SingleAsync()).State.Should().Be(PayrollRunState.Approved);
    }

    private static CancelPayrollRunRequestDto Request() => new()
    { RowVersion = "AQ==", Reason = "Duplicate monthly payroll created in error." };

    private static PayrollRunCancellationService Service(EduOSDbContext db) =>
        new(db, new TestUser("TenantAdmin", 41), TimeProvider.System);

    private static async Task<PayrollRun> SeedRun(EduOSDbContext db,
        PayrollRunState state = PayrollRunState.Approved)
    {
        var run = new PayrollRun { Id = 1, TenantId = 41, PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(), RunNumber = "PAY-2026-10", Year = 2026, Month = 10,
            PeriodStart = new DateOnly(2026, 10, 1), PeriodEnd = new DateOnly(2026, 10, 31),
            State = state, RowVersion = [1] };
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    private static readonly InMemoryDatabaseRoot SharedRoot = new();

    private static EduOSDbContext Database(string? name = null, long tenant = 41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(name ?? "payroll-cancel-" + Guid.NewGuid().ToString("N"), SharedRoot)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
    }

    private sealed class TestUser(string role, long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => tenant;
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
