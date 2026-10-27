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
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollPostingHistoricalIntegrityTests
{
    [Fact]
    public async Task Accrual_replay_rejects_soft_deleted_posted_lines()
    {
        await using var db = Database();
        var reference = Guid.NewGuid();
        db.PayrollRuns.Add(new PayrollRun { Id = 1, TenantId = 41, PublicId = reference,
            RunNumber = "PR-1", State = PayrollRunState.Posted, JournalId = 1 });
        db.Journals.Add(new Journal { Id = 1, TenantId = 41, JournalNumber = "J-1",
            AccountingPeriodId = 1, SourceType = "PayrollRun", SourceId = 1,
            State = JournalState.Posted });
        db.JournalLines.AddRange(
            new JournalLine { Id = 1, TenantId = 41, JournalId = 1, LineNo = 1,
                AccountId = 3, DebitAmount = 100m, Description = "Component 1 earning" },
            new JournalLine { Id = 2, TenantId = 41, JournalId = 1, LineNo = 2,
                AccountId = 2, CreditAmount = 100m, Description = "Payroll net payable" },
            new JournalLine { Id = 3, TenantId = 41, JournalId = 1, LineNo = 3,
                AccountId = 3, DebitAmount = 10m, IsDeleted = true });
        await db.SaveChangesAsync();

        var result = await new PayrollJournalPostingService(db, new User(), TimeProvider.System)
            .PostAsync(reference, new PostPayrollJournalRequestDto
            {
                AccountingPeriodId = 1, SalaryPayableAccountId = 2, RowVersion = "AQ==",
                Mappings = [new PayrollComponentAccountMappingDto { SalaryComponentId = 1, AccountId = 3 }]
            });
        result.StatusCode.Should().Be(409);
        (await db.Journals.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Settlement_replay_rejects_deleted_posted_lines()
    {
        await using var db = Database();
        var reference = Guid.NewGuid();
        db.PayrollPayments.Add(new PayrollPayment { Id = 1, TenantId = 41,
            PublicId = reference, ClientRequestId = Guid.NewGuid(), Amount = 40m,
            State = PaymentState.Successful, JournalId = 2 });
        db.Journals.Add(new Journal { Id = 2, TenantId = 41, JournalNumber = "J-2",
            AccountingPeriodId = 1, SourceType = "PayrollPayment", SourceId = 1,
            State = JournalState.Posted });
        db.JournalLines.AddRange(
            new JournalLine { Id = 4, TenantId = 41, JournalId = 2, LineNo = 1,
                AccountId = 1, DebitAmount = 40m },
            new JournalLine { Id = 5, TenantId = 41, JournalId = 2, LineNo = 2,
                AccountId = 2, CreditAmount = 40m },
            new JournalLine { Id = 6, TenantId = 41, JournalId = 2, LineNo = 3,
                AccountId = 1, DebitAmount = 10m, IsDeleted = true });
        await db.SaveChangesAsync();

        var result = await new PayrollPaymentPostingService(db, new User(), TimeProvider.System)
            .PostAsync(reference, new PostPayrollPaymentJournalRequestDto
            { AccountingPeriodId = 1, SettlementAccountId = 2, RowVersion = "AQ==" });
        result.StatusCode.Should().Be(409);
        (await db.Journals.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Settlement_replay_rejects_reversed_posting()
    {
        await using var db = Database();
        var reference = Guid.NewGuid();
        db.PayrollPayments.Add(new PayrollPayment { Id = 1, TenantId = 41,
            PublicId = reference, ClientRequestId = Guid.NewGuid(), Amount = 40m,
            State = PaymentState.Successful, JournalId = 2 });
        db.Journals.AddRange(
            new Journal { Id = 2, TenantId = 41, JournalNumber = "J-2",
                AccountingPeriodId = 1, SourceType = "PayrollPayment", SourceId = 1,
                State = JournalState.Posted },
            new Journal { Id = 3, TenantId = 41, JournalNumber = "J-3",
                AccountingPeriodId = 1, ReversalOfJournalId = 2,
                State = JournalState.Posted, IsDeleted = true });
        await db.SaveChangesAsync();

        var result = await new PayrollPaymentPostingService(db, new User(), TimeProvider.System)
            .PostAsync(reference, new PostPayrollPaymentJournalRequestDto
            { AccountingPeriodId = 1, SettlementAccountId = 2, RowVersion = "AQ==" });
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Tenant_context_mismatch_blocks_both_payroll_posting_workspaces()
    {
        await using var db = Database(42);
        var accrual = new PayrollJournalPostingService(db, new User(), TimeProvider.System);
        var settlement = new PayrollPaymentPostingService(db, new User(), TimeProvider.System);
        (await accrual.GetRegisterAsync(false, 1, 5)).StatusCode.Should().Be(403);
        (await accrual.PostAsync(Guid.NewGuid(), new PostPayrollJournalRequestDto
        {
            AccountingPeriodId = 1, SalaryPayableAccountId = 2, RowVersion = "AQ==",
            Mappings = [new PayrollComponentAccountMappingDto { SalaryComponentId = 1, AccountId = 3 }]
        })).StatusCode.Should().Be(403);
        (await settlement.GetRegisterAsync(false, 1, 5)).StatusCode.Should().Be(403);
        (await settlement.PostAsync(Guid.NewGuid(), new PostPayrollPaymentJournalRequestDto
        { AccountingPeriodId = 1, SettlementAccountId = 2, RowVersion = "AQ==" }))
            .StatusCode.Should().Be(403);
    }

    private static EduOSDbContext Database(long contextTenant = 41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = contextTenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("payroll-posting-history-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
    }

    private sealed class User : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
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
