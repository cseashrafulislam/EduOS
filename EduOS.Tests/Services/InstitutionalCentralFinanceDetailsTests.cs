using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class InstitutionalCentralFinanceDetailsTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    [Fact]
    public async Task One_central_GL_shows_income_cost_source_and_reversals_on_bounded_pages()
    {
        await using var db = Database();
        SeedAccounts(db);
        Add(db, 1, From, JournalState.Reversed, 1, 2, 100m, "StudentInvoice", 7);
        Add(db, 2, From.AddDays(1), JournalState.Posted, 3, 1, 40m, "PayrollRun", 15);
        Add(db, 3, From.AddDays(2), JournalState.Posted, 2, 1, 20m, "StudentInvoiceReversal", 7, reversalOf:1);
        Add(db, 4, From.AddDays(3), JournalState.Draft, 1, 2, 990m, "StudentInvoice", 9);
        await db.SaveChangesAsync();

        var service = Service(db);
        var first = await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To, page:1, pageSize:2);
        var second = await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To, page:2, pageSize:2);
        first.StatusCode.Should().Be(200);
        second.StatusCode.Should().Be(200);
        first.Data!.Entries.TotalCount.Should().Be(3);
        first.Data.Entries.Items.Should().HaveCount(2);
        second.Data!.Entries.Items.Should().ContainSingle();
        first.Data.TotalIncome.Should().Be(80m);
        first.Data.TotalExpenses.Should().Be(40m);
        first.Data.NetSurplus.Should().Be(40m);
        first.Data.Entries.Items.Should().Contain(x => x.Category == "Income" &&
            x.Amount == -20m && x.IsReversal && x.SourceType == "StudentInvoiceReversal");
        second.Data.Entries.Items.Single().IsReversedOriginal.Should().BeTrue();
        second.Data.Entries.Items.Single().SourceId.Should().Be(7);
    }

    [Fact]
    public async Task Source_account_category_and_pagination_filters_have_consistent_totals()
    {
        await using var db = Database();
        SeedAccounts(db);
        Add(db, 1, From, JournalState.Posted, 1, 2, 250m, "StudentInvoice", 7);
        Add(db, 2, From.AddDays(1), JournalState.Posted, 3, 1, 90m, "PayrollRun", 15);
        await db.SaveChangesAsync();
        var service = Service(db);
        var expense = await service.GetInstitutionalIncomeExpenseDetailsAsync(
            From, To, "expense", 3, "PayrollRun", 1, 25);
        expense.StatusCode.Should().Be(200);
        expense.Data!.TotalIncome.Should().Be(0m);
        expense.Data.TotalExpenses.Should().Be(90m);
        expense.Data.NetSurplus.Should().Be(-90m);
        expense.Data.Entries.Items.Should().ContainSingle(x => x.Category == "Expense" &&
            x.AccountCode == "SALARY" && x.SourceId == 15);
        (await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To, "income", 2, "PayrollRun"))
            .Data!.Entries.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Unauthorized_invalid_cross_tenant_and_unbalanced_history_fail_closed()
    {
        await using var db = Database();
        SeedAccounts(db);
        Add(db, 1, From, JournalState.Posted, 1, 2, 100m, "StudentInvoice", 7);
        await db.SaveChangesAsync();
        var service = Service(db);
        (await Service(db, false).GetInstitutionalIncomeExpenseDetailsAsync(From, To)).StatusCode.Should().Be(403);
        (await service.GetInstitutionalIncomeExpenseDetailsAsync(To, From)).StatusCode.Should().Be(400);
        (await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To, "cash")).StatusCode.Should().Be(400);
        (await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To, page:0)).StatusCode.Should().Be(400);
        (await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To, pageSize:101)).StatusCode.Should().Be(400);
        (await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To, accountId:999)).StatusCode.Should().Be(404);
        (await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To, accountId:1)).StatusCode.Should().Be(404);
        db.JournalLines.Local.Single(x => x.JournalId == 1 && x.AccountId == 2).CreditAmount = 99m;
        await db.SaveChangesAsync();
        (await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To)).StatusCode.Should().Be(409);
    }

    private static void SeedAccounts(EduOSDbContext db) => db.Accounts.AddRange(
        new Account { Id = 1, TenantId = 41, Code = "CASH", Name = "Cash", AccountType = AccountType.Asset },
        new Account { Id = 2, TenantId = 41, Code = "FEES", Name = "Fee revenue", AccountType = AccountType.Income },
        new Account { Id = 3, TenantId = 41, Code = "SALARY", Name = "Salary expense", AccountType = AccountType.Expense });

    private static void Add(EduOSDbContext db, long id, DateOnly date, JournalState state,
        long debitAccountId, long creditAccountId, decimal amount, string sourceType,
        long sourceId, long? reversalOf = null)
    {
        db.Journals.Add(new Journal { Id = id, TenantId = 41, PublicId = Guid.NewGuid(),
            JournalDate = date, JournalNumber = "JV-" + id, AccountingPeriodId = 1,
            State = state, SourceType = sourceType, SourceId = sourceId,
            ReferenceNumber = sourceType + "-" + sourceId, ReversalOfJournalId = reversalOf });
        db.JournalLines.AddRange(
            new JournalLine { Id = id * 10 + 1, TenantId = 41, JournalId = id,
                LineNo = 1, AccountId = debitAccountId, DebitAmount = amount },
            new JournalLine { Id = id * 10 + 2, TenantId = 41, JournalId = id,
                LineNo = 2, AccountId = creditAccountId, CreditAmount = amount });
    }

    private static AccountingReportingService Service(EduOSDbContext db, bool admin = true) =>
        new(db, new TestUser(admin));

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("central-finance-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class TestUser(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Accountant"];
        public bool IsInRole(string role) => admin ? role == "TenantAdmin" : role == "Accountant";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
