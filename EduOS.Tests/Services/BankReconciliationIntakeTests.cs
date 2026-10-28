using EduOS.Core.DTOs.Accounting;
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

public sealed class BankReconciliationIntakeTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);

    [Fact]
    public async Task Valid_draft_persists_snapshot_without_clearing_journal_and_replays_idempotently()
    {
        await using var db = Database();
        Seed(db);
        var service = new AccountingReportingService(db, new User());
        var request = Request();
        var first = await service.SaveBankReconciliationDraftAsync(request);
        first.StatusCode.Should().Be(200);
        first.Data!.StateCode.Should().Be(1);
        (await service.SaveBankReconciliationDraftAsync(request)).Data!.Id.Should().Be(first.Data.Id);
        (await db.BankReconciliationBatches.CountAsync()).Should().Be(1);
        (await db.BankReconciliationStatementLines.CountAsync()).Should().Be(1);
        (await db.Journals.SingleAsync()).State.Should().Be(JournalState.Posted);
        var detail = await service.GetBankReconciliationDraftAsync(first.Data.Id);
        detail.Data!.Movements.Should().ContainSingle();
        detail.Data.Movements[0].Reference.Should().Be("DEP-1");
        detail.Data.SourceFingerprint.Should().HaveLength(64);
    }

    [Fact]
    public async Task Same_statement_with_new_key_or_reused_key_with_changed_identity_is_conflict()
    {
        await using var db = Database();
        Seed(db);
        var service = new AccountingReportingService(db, new User());
        var original = Request();
        (await service.SaveBankReconciliationDraftAsync(original)).StatusCode.Should().Be(200);
        var duplicate = Request(); duplicate.ClientRequestId = Guid.NewGuid();
        (await service.SaveBankReconciliationDraftAsync(duplicate)).StatusCode.Should().Be(409);
        var altered = Request(); altered.StatementIdentifier = "DIFFERENT";
        (await service.SaveBankReconciliationDraftAsync(altered)).StatusCode.Should().Be(409);
        (await db.BankReconciliationBatches.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Campus_tenant_and_role_probes_fail_closed()
    {
        await using var db = Database();
        Seed(db);
        var request = Request();
        (await new AccountingReportingService(db, new User(false))
            .SaveBankReconciliationDraftAsync(request)).StatusCode.Should().Be(403);
        request.Statement.BankAccountId = 999;
        (await new AccountingReportingService(db, new User())
            .SaveBankReconciliationDraftAsync(request)).StatusCode.Should().Be(404);
        (await new AccountingReportingService(db, new User(false))
            .GetBankReconciliationDraftsAsync(1)).StatusCode.Should().Be(403);
        (await new AccountingReportingService(db, new User())
            .GetBankReconciliationDraftAsync(999)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Closed_period_and_invalid_statement_cannot_be_imported()
    {
        await using var db = Database();
        Seed(db);
        db.AccountingPeriods.Add(new AccountingPeriod
        { Id = 1, TenantId = 41, Name = "October", StartDate = Day, EndDate = Day, IsClosed = true });
        await db.SaveChangesAsync();
        var service = new AccountingReportingService(db, new User());
        (await service.SaveBankReconciliationDraftAsync(Request())).StatusCode.Should().Be(409);
        db.AccountingPeriods.Single().IsClosed = false;
        await db.SaveChangesAsync();
        var invalid = Request();
        invalid.Statement.Movements = new[] { new BankStatementMovementDto
            { Date = Day, Reference = "DEP-1", Deposit = -100m } };
        (await service.SaveBankReconciliationDraftAsync(invalid)).StatusCode.Should().Be(400);
        (await db.BankReconciliationBatches.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Register_is_bounded_and_snapshot_readback_detects_missing_rows()
    {
        await using var db = Database();
        Seed(db);
        var service = new AccountingReportingService(db, new User());
        var result = await service.SaveBankReconciliationDraftAsync(Request());
        (await service.GetBankReconciliationDraftsAsync(1, 1, 101)).StatusCode.Should().Be(400);
        (await service.GetBankReconciliationDraftsAsync(1)).Data!.Items.Should().ContainSingle();
        db.BankReconciliationStatementLines.RemoveRange(db.BankReconciliationStatementLines);
        await db.SaveChangesAsync();
        (await service.GetBankReconciliationDraftAsync(result.Data!.Id)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Altered_persisted_statement_amount_is_rejected_by_fingerprint()
    {
        await using var db = Database();
        Seed(db);
        var service = new AccountingReportingService(db, new User());
        var saved = await service.SaveBankReconciliationDraftAsync(Request());
        var line = await db.BankReconciliationStatementLines.SingleAsync();
        line.Reference = "FORGED";
        await db.SaveChangesAsync();
        (await service.GetBankReconciliationDraftAsync(saved.Data!.Id)).StatusCode.Should().Be(409);
    }

    private static BankReconciliationSaveRequestDto Request() => new()
    {
        ClientRequestId = Guid.NewGuid(), StatementIdentifier = "OCT-2026-OPERATING",
        Statement = new BankStatementPreviewRequestDto
        {
            BankAccountId = 1, FromDate = Day, ToDate = Day,
            OpeningBalance = 0m, ClosingBalance = 100m,
            Movements = new[] { new BankStatementMovementDto
                { Date = Day, Reference = "DEP-1", Deposit = 100m } }
        }
    };
    private static void Seed(EduOSDbContext db)
    {
        db.Accounts.AddRange(
            new Account { Id = 1, TenantId = 41, Code = "BANK", Name = "Bank", AccountType = AccountType.Asset },
            new Account { Id = 2, TenantId = 41, Code = "INC", Name = "Income", AccountType = AccountType.Income });
        db.BankAccounts.Add(new BankAccount
        { Id = 1, TenantId = 41, AccountId = 1, BankName = "Bank", AccountName = "Current", AccountNumber = "000" });
        db.Journals.Add(new Journal { Id = 1, TenantId = 41, JournalNumber = "J-1",
            JournalDate = Day, AccountingPeriodId = 1, State = JournalState.Posted, ReferenceNumber = "DEP-1" });
        db.JournalLines.AddRange(
            new JournalLine { Id = 1, TenantId = 41, JournalId = 1, AccountId = 1, LineNo = 1, DebitAmount = 100m },
            new JournalLine { Id = 2, TenantId = 41, JournalId = 1, AccountId = 2, LineNo = 2, CreditAmount = 100m });
        db.SaveChanges();
    }
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("bank-intake-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }
    private sealed class User(bool admin = true) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Accountant"];
        public bool IsInRole(string value) => admin ? value == "TenantAdmin" : value == "Accountant";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
