using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerBankReconciliationIntakeIntegrationTests
{
    [Fact]
    public async Task Draft_snapshot_enforces_sql_uniqueness_tenant_fk_rowversion_and_append_only_history()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, otherTenantId, userId, otherUserId, bankId;
        var date = new DateOnly(2026, 10, 15);

        await using (var seed = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-REC-" + suffix, Name = "Reconciliation CI",
                Email = "rec-" + suffix + "@example.test", State = TenantState.Active };
            var other = new Tenant { Code = "CI-RECO-" + suffix, Name = "Other Reconciliation CI",
                Email = "reco-" + suffix + "@example.test", State = TenantState.Active };
            var actor = new ApplicationUser { UserName = "rec-" + suffix,
                NormalizedUserName = ("REC-" + suffix).ToUpperInvariant(),
                Email = "recuser-" + suffix + "@example.test", FullName = "Finance CI" };
            var otherActor = new ApplicationUser { UserName = "reco-" + suffix,
                NormalizedUserName = ("RECO-" + suffix).ToUpperInvariant(),
                Email = "recother-" + suffix + "@example.test", FullName = "Other Finance CI" };
            seed.Tenants.AddRange(tenant, other);
            seed.Users.AddRange(actor, otherActor);
            await seed.SaveChangesAsync();
            tenantId = tenant.Id; otherTenantId = other.Id;
            userId = actor.Id; otherUserId = otherActor.Id;
            seed.ChangeTracker.Clear();

            using (seed.BeginSystemTenantScope(tenantId))
            {
                var bankLedger = new Account { TenantId = tenantId, Code = "CI-BANK-" + suffix,
                    Name = "Operating Bank", AccountType = AccountType.Asset };
                var income = new Account { TenantId = tenantId, Code = "CI-INC-" + suffix,
                    Name = "Income", AccountType = AccountType.Income };
                seed.Accounts.AddRange(bankLedger, income);
                var period = new AccountingPeriod { TenantId = tenantId, Name = "October CI",
                    StartDate = date, EndDate = date };
                seed.AccountingPeriods.Add(period);
                await seed.SaveChangesAsync();

                var bank = new BankAccount { TenantId = tenantId, AccountId = bankLedger.Id,
                    BankName = "CI Bank", AccountName = "Operating",
                    AccountNumber = "CI-" + suffix };
                seed.BankAccounts.Add(bank);
                var journal = new Journal { TenantId = tenantId, JournalNumber = "CI-J-" + suffix,
                    JournalDate = date, AccountingPeriodId = period.Id,
                    State = JournalState.Posted, ReferenceNumber = "DEP-" + suffix };
                seed.Journals.Add(journal);
                await seed.SaveChangesAsync();
                bankId = bank.Id;
                seed.JournalLines.AddRange(
                    new JournalLine { TenantId = tenantId, JournalId = journal.Id,
                        AccountId = bankLedger.Id, LineNo = 1, DebitAmount = 100m },
                    new JournalLine { TenantId = tenantId, JournalId = journal.Id,
                        AccountId = income.Id, LineNo = 2, CreditAmount = 100m });
                await seed.SaveChangesAsync();
            }
        }

        var request = new BankReconciliationSaveRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StatementIdentifier = "CI-STATEMENT-" + suffix,
            Statement = new BankStatementPreviewRequestDto
            {
                BankAccountId = bankId, FromDate = date, ToDate = date,
                OpeningBalance = 0m, ClosingBalance = 100m,
                Movements = new[] { new BankStatementMovementDto
                    { Date = date, Reference = "DEP-" + suffix, Deposit = 100m } }
            }
        };
        await using var db = new EduOSDbContext(options, Accessor(tenantId, userId));
        var service = new AccountingReportingService(db, new TestUser(tenantId, userId));
        var saved = await service.SaveBankReconciliationDraftAsync(request);
        Assert.True(saved.Success, saved.Message);
        Assert.NotNull(saved.Data);
        Assert.NotEmpty(saved.Data!.RowVersion);
        var replay = await service.SaveBankReconciliationDraftAsync(request);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(saved.Data.Id, replay.Data!.Id);
        Assert.Equal(1, await db.BankReconciliationBatches.CountAsync());
        Assert.Equal(1, await db.BankReconciliationStatementLines.CountAsync());

        var duplicate = new BankReconciliationSaveRequestDto
        { ClientRequestId = Guid.NewGuid(), StatementIdentifier = request.StatementIdentifier,
            Statement = request.Statement };
        Assert.Equal(409, (await service.SaveBankReconciliationDraftAsync(duplicate)).StatusCode);
        var detail = await service.GetBankReconciliationDraftAsync(saved.Data.Id);
        Assert.True(detail.Success, detail.Message);
        Assert.Single(detail.Data!.Movements);
        var reviewed = await service.ReviewSavedBankReconciliationAsync(saved.Data.Id);
        Assert.True(reviewed.Success, reviewed.Message);
        Assert.True(reviewed.Data!.Comparison.IsExactMatch);
        Assert.Equal(saved.Data.SourceFingerprint, reviewed.Data.Draft.SourceFingerprint);

        var exact = reviewed.Data.Comparison.Matches.Single();
        var confirm = new BankReconciliationMatchCommandDto
        {
            ClientRequestId = Guid.NewGuid(), BatchId = saved.Data.Id,
            BatchRowVersion = reviewed.Data.Draft.RowVersion, ActionCode = 1,
            StatementRowNumber = exact.StatementRowNumber, JournalLineId = exact.JournalLineId,
            JournalLineVersion = exact.JournalLineVersion
        };
        var matched = await service.RecordBankReconciliationMatchEventAsync(confirm);
        Assert.True(matched.Success, matched.Message);
        Assert.Equal(matched.Data!.Id,
            (await service.RecordBankReconciliationMatchEventAsync(confirm)).Data!.Id);
        Assert.Equal(409, (await service.RecordBankReconciliationMatchEventAsync(new BankReconciliationMatchCommandDto
        {
            ClientRequestId = Guid.NewGuid(), BatchId = confirm.BatchId,
            BatchRowVersion = confirm.BatchRowVersion, ActionCode = 1,
            StatementRowNumber = confirm.StatementRowNumber, JournalLineId = confirm.JournalLineId,
            JournalLineVersion = confirm.JournalLineVersion
        })).StatusCode);
        var withdrawn = await service.RecordBankReconciliationMatchEventAsync(new BankReconciliationMatchCommandDto
        {
            ClientRequestId = Guid.NewGuid(), BatchId = confirm.BatchId,
            BatchRowVersion = confirm.BatchRowVersion, ActionCode = 2,
            OriginalMatchEventId = matched.Data.Id, Reason = "SQL integration correction"
        });
        Assert.True(withdrawn.Success, withdrawn.Message);
        Assert.Equal(2, await db.BankReconciliationMatchEvents.CountAsync());
        var history = await service.GetBankReconciliationMatchEventsAsync(confirm.BatchId);
        Assert.True(history.Success, history.Message);
        Assert.True(history.Data!.Items.Single(x => x.ActionCode == 1).IsWithdrawn);
        var matchEvent = await db.BankReconciliationMatchEvents.SingleAsync(x => x.ActionCode == 1);
        matchEvent.Reference = "FORGED";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(matchEvent).State = EntityState.Unchanged;

        var line = await db.BankReconciliationStatementLines.SingleAsync();
        line.Reference = "ALTERED";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(line).State = EntityState.Unchanged;

        // Database unique constraint is authoritative even if a caller bypasses service preflight.
        db.BankReconciliationBatches.Add(new BankReconciliationBatch
        {
            TenantId = tenantId, BankAccountId = bankId, ClientRequestId = request.ClientRequestId,
            StatementIdentifier = "CI-DUPLICATE", SourceFingerprint = new string('A', 64),
            FromDate = date, ToDate = date, OpeningBalance = 0m, ClosingBalance = 100m,
            MovementCount = 1, StateCode = 1, PreparedByUserId = userId
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // HttpContextAccessor uses AsyncLocal. Switch the test request tenant only
        // after the original tenant's write assertions have finished.
        await using var otherDb = new EduOSDbContext(options, Accessor(otherTenantId, otherUserId));
        var otherService = new AccountingReportingService(otherDb, new TestUser(otherTenantId, otherUserId));
        Assert.Equal(404, (await otherService.GetBankReconciliationDraftAsync(saved.Data.Id)).StatusCode);
        Assert.Equal(404, (await otherService.ReviewSavedBankReconciliationAsync(saved.Data.Id)).StatusCode);
        Assert.Equal(404, (await otherService.GetBankReconciliationDraftsAsync(bankId)).StatusCode);
    }

    private static HttpContextAccessor Accessor(long tenantId, long userId)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenantId.ToString())
            }, "TestAuthentication"))
        };
        http.Items["TenantId"] = tenantId;
        return new HttpContextAccessor { HttpContext = http };
    }

    private sealed class TestUser(long tenantId, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Finance CI";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string value) => value == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
