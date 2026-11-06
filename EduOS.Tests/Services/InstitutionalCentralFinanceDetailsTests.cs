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
    public async Task Aggregate_only_finance_totals_remain_available_above_5000_posting_accounts()
    {
        await using var db = Database();
        SeedAccounts(db);
        db.Journals.Add(new Journal { Id = 1, TenantId = 41, JournalNumber = "JV-LARGE",
            JournalDate = From, AccountingPeriodId = 1, State = JournalState.Posted });
        db.JournalLines.Add(new JournalLine { Id = 1, TenantId = 41, JournalId = 1,
            LineNo = 1, AccountId = 1, DebitAmount = 5001m });
        for (var i = 0; i < 5001; i++)
        {
            var accountId = 10L + i;
            db.Accounts.Add(new Account { Id = accountId, TenantId = 41,
                Code = "INCOME-" + i, Name = "Historic revenue " + i,
                AccountType = AccountType.Income });
            db.JournalLines.Add(new JournalLine { Id = 2L + i, TenantId = 41,
                JournalId = 1, LineNo = 2 + i, AccountId = accountId,
                CreditAmount = 1m });
        }
        await db.SaveChangesAsync();

        var service = Service(db);
        var totals = await service.GetInstitutionalIncomeExpenseTotalsAsync(From, To);
        totals.StatusCode.Should().Be(200);
        totals.Data!.TotalIncome.Should().Be(5001m);
        totals.Data.TotalExpenses.Should().Be(0m);
        totals.Data.NetSurplus.Should().Be(5001m);
        var details = await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To, pageSize: 20);
        details.StatusCode.Should().Be(200);
        details.Data!.Entries.TotalCount.Should().Be(5001);
        details.Data.Entries.Items.Should().HaveCount(20);
        details.Data.TotalIncome.Should().Be(totals.Data.TotalIncome);
        var trialBalance = await service.GetTrialBalancePageAsync(To, 1, 25);
        trialBalance.StatusCode.Should().Be(200);
        trialBalance.Data!.Accounts.TotalCount.Should().Be(5002);
        trialBalance.Data.Accounts.Items.Should().HaveCount(25);
        trialBalance.Data.TotalDebitBalance.Should().Be(5001m);
        trialBalance.Data.TotalCreditBalance.Should().Be(5001m);
        var finalPage = await service.GetTrialBalancePageAsync(To, 201, 25);
        finalPage.StatusCode.Should().Be(200);
        finalPage.Data!.Accounts.Items.Should().HaveCount(2);
        finalPage.Data.TotalDebitBalance.Should().Be(5001m);
        var balanceSheet = await service.GetBalanceSheetPageAsync(To, pageSize: 10);
        balanceSheet.StatusCode.Should().Be(200);
        balanceSheet.Data!.TotalAssets.Should().Be(5001m);
        balanceSheet.Data.UnclosedNetSurplus.Should().Be(5001m);
        balanceSheet.Data.Accounts.TotalCount.Should().Be(1);
        balanceSheet.Data.Accounts.Items.Should().ContainSingle(x => x.AccountCode == "CASH" && x.Amount == 5001m);
        // The existing full-detail statement keeps its legacy bounded contract.
        (await service.GetIncomeStatementAsync(From, To)).StatusCode.Should().Be(422);
        var incomeFirst = await service.GetIncomeStatementPageAsync(From, To, page: 1, pageSize: 25);
        var incomeLast = await service.GetIncomeStatementPageAsync(From, To, page: 201, pageSize: 25);
        incomeFirst.StatusCode.Should().Be(200);
        incomeFirst.Data!.Accounts.TotalCount.Should().Be(5001);
        incomeFirst.Data.Accounts.Items.Should().HaveCount(25);
        incomeFirst.Data.TotalIncome.Should().Be(5001m);
        incomeFirst.Data.TotalExpenses.Should().Be(0m);
        incomeLast.StatusCode.Should().Be(200);
        incomeLast.Data!.Accounts.Items.Should().ContainSingle();
        incomeLast.Data.TotalIncome.Should().Be(incomeFirst.Data.TotalIncome);
    }

    [Fact]
    public async Task Income_statement_page_preserves_signed_balances_and_category_filters()
    {
        await using var db = Database();
        SeedAccounts(db);
        Add(db, 1, From, JournalState.Posted, 1, 2, 100m, "StudentInvoice", 10);
        Add(db, 2, From.AddDays(1), JournalState.Posted, 3, 1, 40m, "PayrollRun", 20);
        Add(db, 3, From.AddDays(2), JournalState.Posted, 2, 1, 20m, "StudentInvoiceReversal", 10);
        Add(db, 4, From.AddDays(3), JournalState.Draft, 1, 2, 999m, "StudentInvoice", 99);
        await db.SaveChangesAsync();

        var service = Service(db);
        var first = await service.GetIncomeStatementPageAsync(From, To, pageSize: 1);
        var second = await service.GetIncomeStatementPageAsync(From, To, page: 2, pageSize: 1);
        var onlyIncome = await service.GetIncomeStatementPageAsync(From, To, "income");
        var onlyExpense = await service.GetIncomeStatementPageAsync(From, To, "expense");
        first.StatusCode.Should().Be(200);
        second.StatusCode.Should().Be(200);
        first.Data!.Accounts.TotalCount.Should().Be(2);
        second.Data!.Accounts.TotalCount.Should().Be(2);
        first.Data.TotalIncome.Should().Be(80m);
        first.Data.TotalExpenses.Should().Be(40m);
        first.Data.NetSurplus.Should().Be(40m);
        first.Data.Accounts.Items.Concat(second.Data.Accounts.Items)
            .Should().ContainSingle(x => x.Category == "Income" && x.Amount == 80m);
        first.Data.Accounts.Items.Concat(second.Data.Accounts.Items)
            .Should().ContainSingle(x => x.Category == "Expense" && x.Amount == 40m);
        onlyIncome.Data!.Accounts.TotalCount.Should().Be(1);
        onlyIncome.Data.Accounts.Items.Should().ContainSingle(x => x.Amount == 80m);
        onlyIncome.Data.TotalExpenses.Should().Be(40m);
        onlyExpense.Data!.Accounts.TotalCount.Should().Be(1);
        onlyExpense.Data.Accounts.Items.Should().ContainSingle(x => x.Amount == 40m);
        onlyExpense.Data.TotalIncome.Should().Be(80m);

        (await Service(db, false).GetIncomeStatementPageAsync(From, To)).StatusCode.Should().Be(403);
        (await service.GetIncomeStatementPageAsync(To, From)).StatusCode.Should().Be(400);
        (await service.GetIncomeStatementPageAsync(From, To, "cash")).StatusCode.Should().Be(400);
        (await service.GetIncomeStatementPageAsync(From, To, page: 0)).StatusCode.Should().Be(400);
        (await service.GetIncomeStatementPageAsync(From, To, pageSize: 101)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Income_statement_page_blocks_corrupted_history_before_aggregate_and_page()
    {
        await using var db = Database();
        SeedAccounts(db);
        Add(db, 1, From, JournalState.Posted, 1, 2, 100m, "StudentInvoice", 7);
        db.JournalLines.Local.Single(x => x.AccountId == 2).CreditAmount = 99m;
        await db.SaveChangesAsync();
        (await Service(db).GetIncomeStatementPageAsync(From, To)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Aggregate_only_finance_totals_fail_closed_for_unauthorized_and_corrupt_history()
    {
        await using var db = Database();
        SeedAccounts(db);
        Add(db, 1, From, JournalState.Posted, 1, 2, 50m, "StudentInvoice", 1);
        db.JournalLines.Local.Single(x => x.AccountId == 2).CreditAmount = 49m;
        await db.SaveChangesAsync();
        (await Service(db, false).GetInstitutionalIncomeExpenseTotalsAsync(From, To))
            .StatusCode.Should().Be(403);
        (await Service(db).GetInstitutionalIncomeExpenseTotalsAsync(To, From))
            .StatusCode.Should().Be(400);
        (await Service(db).GetInstitutionalIncomeExpenseTotalsAsync(From, To))
            .StatusCode.Should().Be(409);
    }

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
    public async Task Fee_payroll_procurement_and_facilities_share_one_reconciled_institutional_GL()
    {
        await using var db = Database();
        SeedAccounts(db);
        db.Accounts.AddRange(
            new Account { Id = 4, TenantId = 41, Code = "FAC", Name = "Facilities expense", AccountType = AccountType.Expense },
            new Account { Id = 5, TenantId = 41, Code = "INV", Name = "Inventory", AccountType = AccountType.Asset },
            new Account { Id = 6, TenantId = 41, Code = "AR", Name = "Student receivables", AccountType = AccountType.Asset },
            new Account { Id = 7, TenantId = 41, Code = "AP", Name = "Supplier payables", AccountType = AccountType.Liability },
            new Account { Id = 8, TenantId = 41, Code = "SALPAY", Name = "Salary payables", AccountType = AccountType.Liability });
        Add(db, 1, From, JournalState.Posted, 6, 2, 100m, "StudentInvoice", 10);
        Add(db, 2, From.AddDays(1), JournalState.Posted, 3, 8, 30m, "PayrollRun", 20);
        Add(db, 3, From.AddDays(2), JournalState.Posted, 5, 7, 40m, "ProcurementSupplierInvoice", 30);
        Add(db, 4, From.AddDays(3), JournalState.Posted, 4, 7, 20m, "FacilityVendorServiceBill", 40);
        await db.SaveChangesAsync();

        var service = Service(db);
        var statement = await service.GetIncomeStatementAsync(From, To);
        var first = await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To, page: 1, pageSize: 2);
        var second = await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To, page: 2, pageSize: 2);

        statement.StatusCode.Should().Be(200);
        first.StatusCode.Should().Be(200);
        second.StatusCode.Should().Be(200);
        first.Data!.Entries.TotalCount.Should().Be(3);
        first.Data.Entries.Items.Should().HaveCount(2);
        second.Data!.Entries.Items.Should().ContainSingle();
        first.Data.TotalIncome.Should().Be(100m);
        first.Data.TotalExpenses.Should().Be(50m);
        first.Data.NetSurplus.Should().Be(50m);
        first.Data.TotalIncome.Should().Be(statement.Data!.TotalIncome);
        first.Data.TotalExpenses.Should().Be(statement.Data.TotalExpenses);

        foreach (var source in new[] { "StudentInvoice", "PayrollRun", "FacilityVendorServiceBill" })
        {
            var filtered = await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To, sourceType: source);
            filtered.StatusCode.Should().Be(200);
            filtered.Data!.Entries.TotalCount.Should().Be(1);
            filtered.Data.Entries.Items.Single().SourceType.Should().Be(source);
        }
        // Acquiring inventory creates an asset and a payable, not an immediate P&L expense.
        var procurement = await service.GetInstitutionalIncomeExpenseDetailsAsync(
            From, To, sourceType: "ProcurementSupplierInvoice");
        procurement.StatusCode.Should().Be(200);
        procurement.Data!.Entries.TotalCount.Should().Be(0);
        procurement.Data.TotalIncome.Should().Be(0m);
        procurement.Data.TotalExpenses.Should().Be(0m);
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
        var rejectedEdit = await FluentActions.Invoking(() => db.SaveChangesAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        rejectedEdit.Which.Message.Should().Contain("immutable");
        // A test must not mutate an already-posted journal to simulate bad
        // historical imports. Seed the malformed legacy entry before first save.
        await using var malformed = Database();
        SeedAccounts(malformed);
        Add(malformed, 1, From, JournalState.Posted, 1, 2, 100m, "StudentInvoice", 7);
        malformed.JournalLines.Local.Single(x => x.JournalId == 1 && x.AccountId == 2).CreditAmount = 99m;
        await malformed.SaveChangesAsync();
        (await Service(malformed).GetInstitutionalIncomeExpenseDetailsAsync(From, To)).StatusCode.Should().Be(409);
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
