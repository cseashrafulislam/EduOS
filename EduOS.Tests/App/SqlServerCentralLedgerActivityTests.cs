using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Opt-in disposable SQL Server gate for the actual ordered page-prefix aggregate.
/// EF InMemory does not prove that SQL Server translates Take + GroupBy or applies
/// the same order as Skip + Take when journal lines share a date.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerCentralLedgerActivityTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    [Fact]
    public async Task SQL_posted_ledger_page_prefix_running_balance_and_tenant_scope()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, foreignTenantId, cashId, foreignCashId;

        await using (var root = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "LED-P-" + suffix,
                Name = "Ledger activity SQL", Email = "ledp-" + suffix + "@example.test",
                State = TenantState.Active };
            var foreign = new Tenant { Code = "LED-X-" + suffix,
                Name = "Foreign ledger SQL", Email = "ledx-" + suffix + "@example.test",
                State = TenantState.Active };
            root.Tenants.AddRange(tenant, foreign);
            await root.SaveChangesAsync();
            tenantId = tenant.Id;
            foreignTenantId = foreign.Id;
        }

        await using (var db = Open(options, tenantId))
        {
            var period = new AccountingPeriod { TenantId = tenantId,
                Name = "Ledger test " + suffix, StartDate = new DateOnly(2026, 9, 1),
                EndDate = To };
            var cash = new Account { TenantId = tenantId, Code = "CA-" + suffix,
                Name = "Cash", AccountType = AccountType.Asset };
            var revenue = new Account { TenantId = tenantId, Code = "RE-" + suffix,
                Name = "Revenue", AccountType = AccountType.Income };
            db.AddRange(period, cash, revenue);
            await db.SaveChangesAsync();
            cashId = cash.Id;

            async Task JournalAsync(int ordinal, DateOnly date, decimal amount,
                JournalState state = JournalState.Posted, long? reverses = null)
            {
                var journal = new Journal { TenantId = tenantId,
                    JournalNumber = "SQL-L-" + suffix + "-" + ordinal,
                    AccountingPeriodId = period.Id, JournalDate = date,
                    State = state, SourceType = "Manual", ReversalOfJournalId = reverses };
                db.Journals.Add(journal);
                await db.SaveChangesAsync();
                db.JournalLines.AddRange(
                    new JournalLine { TenantId = tenantId, JournalId = journal.Id,
                        AccountId = cash.Id, LineNo = 1,
                        DebitAmount = Math.Max(amount, 0m), CreditAmount = Math.Max(-amount, 0m) },
                    new JournalLine { TenantId = tenantId, JournalId = journal.Id,
                        AccountId = revenue.Id, LineNo = 2,
                        DebitAmount = Math.Max(-amount, 0m), CreditAmount = Math.Max(amount, 0m) });
                await db.SaveChangesAsync();
            }

            await JournalAsync(1, new DateOnly(2026, 9, 30), 100m);
            await JournalAsync(2, new DateOnly(2026, 10, 3), 50m);
            await JournalAsync(3, new DateOnly(2026, 10, 4), -20m);
            await JournalAsync(4, new DateOnly(2026, 10, 5), -20m);
            var service = new AccountingReportingService(db, new User(tenantId));
            var first = await service.GetLedgerActivityAsync(cash.Id, From, To, 1, 2);
            var incomeExpense = await service.GetInstitutionalIncomeExpenseTotalsAsync(From, To);
            var incomeStatement = await service.GetIncomeStatementPageAsync(From, To, "income", 1, 1);
            var trialPageOne = await service.GetTrialBalancePageAsync(To, 1, 1);
            var trialPageTwo = await service.GetTrialBalancePageAsync(To, 2, 1);
            var balanceSheet = await service.GetBalanceSheetPageAsync(To, "asset", 1, 1);
            var second = await service.GetLedgerActivityAsync(cash.Id, From, To, 2, 2);
            var beyond = await service.GetLedgerActivityAsync(cash.Id, From, To, 4, 2);
            Assert.Equal(200, first.StatusCode);
            Assert.Equal(200, incomeExpense.StatusCode);
            Assert.Equal(200, incomeStatement.StatusCode);
            Assert.Equal(1, incomeStatement.Data!.Accounts.TotalCount);
            Assert.Equal(10m, Assert.Single(incomeStatement.Data.Accounts.Items).Amount);
            Assert.Equal(10m, incomeStatement.Data.TotalIncome);
            Assert.Equal(0m, incomeStatement.Data.TotalExpenses);
            Assert.Equal(10m, incomeExpense.Data!.TotalIncome);
            Assert.Equal(10m, incomeExpense.Data.NetSurplus);
            Assert.Equal(200, trialPageOne.StatusCode);
            Assert.Equal(200, trialPageTwo.StatusCode);
            Assert.Equal(2, trialPageOne.Data!.Accounts.TotalCount);
            Assert.Equal(110m, trialPageOne.Data.TotalDebitBalance);
            Assert.Equal(110m, trialPageOne.Data.TotalCreditBalance);
            Assert.Single(trialPageOne.Data.Accounts.Items);
            Assert.Single(trialPageTwo.Data!.Accounts.Items);
            Assert.Equal(200, balanceSheet.StatusCode);
            Assert.Equal(110m, balanceSheet.Data!.TotalAssets);
            Assert.Equal(110m, balanceSheet.Data.UnclosedNetSurplus);
            Assert.Single(balanceSheet.Data.Accounts.Items);
            Assert.Equal(200, second.StatusCode);
            Assert.Equal(200, beyond.StatusCode);
            Assert.Equal(3, first.Data!.Entries.TotalCount);
            Assert.Equal(100m, first.Data.OpeningBalance);
            Assert.Equal(110m, first.Data.ClosingBalance);
            Assert.Equal(130m, first.Data.PageClosingBalance);
            Assert.Equal(new[] { 150m, 130m },
                first.Data.Entries.Items.Select(x => x.RunningBalance).ToArray());
            Assert.Equal(130m, second.Data!.PageOpeningBalance);
            Assert.Equal(110m, second.Data.PageClosingBalance);
            Assert.Equal(110m, Assert.Single(second.Data.Entries.Items).RunningBalance);
            Assert.Empty(beyond.Data!.Entries.Items);
            Assert.Equal(110m, beyond.Data.PageOpeningBalance);
            Assert.Equal(110m, beyond.Data.PageClosingBalance);
            Assert.Equal(403, (await new AccountingReportingService(db,
                new User(tenantId, false)).GetLedgerActivityAsync(cash.Id, From, To)).StatusCode);
            Assert.Equal(403, (await new AccountingReportingService(db,
                new User(tenantId, false)).GetTrialBalancePageAsync(To)).StatusCode);
        }

        await using (var foreign = Open(options, foreignTenantId))
        {
            var account = new Account { TenantId = foreignTenantId,
                Code = "FX-" + suffix, Name = "Foreign cash",
                AccountType = AccountType.Asset };
            foreign.Accounts.Add(account);
            await foreign.SaveChangesAsync();
            foreignCashId = account.Id;
            var reporter = new AccountingReportingService(foreign, new User(foreignTenantId));
            Assert.Equal(404, (await reporter.GetLedgerActivityAsync(cashId, From, To)).StatusCode);
        }

        await using (var original = Open(options, tenantId))
        {
            var reporter = new AccountingReportingService(original, new User(tenantId));
            Assert.Equal(404, (await reporter.GetLedgerActivityAsync(foreignCashId, From, To)).StatusCode);
        }
    }

    private static EduOSDbContext Open(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options,
            new HttpContextAccessor { HttpContext = http });
    }

    private sealed class User(long tenant, bool admin = true) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 1;
        public long TenantId => tenant;
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
