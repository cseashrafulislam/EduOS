using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CentralFinancePostingAuditTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    [Fact]
    public async Task Fee_source_audit_separates_verified_pending_and_draft_without_double_counting()
    {
        var store = "finance-source-audit-"+Guid.NewGuid().ToString("N");
        // HttpContextAccessor uses a shared AsyncLocal. Seed the other tenant
        // first so the active tenant-41 context remains the last scope.
        await using (var outside = Database(store, 42))
        {
            outside.StudentInvoices.Add(Invoice(4, InvoiceState.Issued, 99, tenant:42));
            await outside.SaveChangesAsync();
        }
        await using var db = Database(store);
        SeedAccounts(db);
        db.StudentInvoices.AddRange(
            Invoice(1, InvoiceState.Issued, 11),
            Invoice(2, InvoiceState.Issued, null),
            Invoice(3, InvoiceState.Draft, null));
        Journal(db, 11, "StudentInvoice", 1, 90m);
        await db.SaveChangesAsync();

        var service = Service(db);
        var page = await service.GetFinanceSourcePostingAuditAsync(From, To, "fee-invoices", 1, 2);
        var second = await service.GetFinanceSourcePostingAuditAsync(From, To, "fee-invoices", 2, 2);
        page.StatusCode.Should().Be(200);
        page.Data!.Sources.TotalCount.Should().Be(3);
        page.Data.LinkedCount.Should().Be(1);
        page.Data.PendingPostingCount.Should().Be(1);
        page.Data.UnpostedCount.Should().Be(1);
        page.Data.Sources.Items.Should().Contain(x => x.SourceId == 2 && x.PostingStatus == "PendingPosting");
        second.Data!.Sources.Items.Should().ContainSingle(x => x.SourceId == 1 &&
            x.PostingStatus == "Posted" && x.JournalNumber == "JV-11" && x.JournalReference.HasValue);
    }

    [Fact]
    public async Task Broken_source_links_and_balanced_but_wrong_source_fail_visible_audit()
    {
        await using var db = Database();
        SeedAccounts(db);
        db.StudentInvoices.AddRange(Invoice(1, InvoiceState.Issued, 11),
            Invoice(2, InvoiceState.Issued, null), Invoice(3, InvoiceState.Issued, 33));
        Journal(db, 11, "PayrollRun", 1, 90m);
        Journal(db, 22, "StudentInvoice", 2, 90m);
        Journal(db, 33, "StudentInvoice", 3, 90m, balanced:false);
        await db.SaveChangesAsync();

        var page = await Service(db).GetFinanceSourcePostingAuditAsync(From, To, "fee-invoices");
        page.StatusCode.Should().Be(200);
        page.Data!.Sources.Items.Single(x => x.SourceId == 1).PostingStatus.Should().Be("SourceMismatch");
        page.Data.Sources.Items.Single(x => x.SourceId == 2).PostingStatus.Should().Be("BrokenLink");
        page.Data.Sources.Items.Single(x => x.SourceId == 3).PostingStatus.Should().Be("InvalidJournal");
    }

    [Fact]
    public async Task Payroll_payment_and_supplier_return_audit_keep_the_original_posted_lineage()
    {
        await using var db = Database();
        SeedAccounts(db);
        db.PayrollPayments.Add(new PayrollPayment { Id=1, TenantId=41,
            PayrollEmployeeId=2, PaymentDate=From, Amount=90m,
            State=PaymentState.Successful, JournalId=51 });
        db.ProcurementSupplierReturnCredits.Add(new ProcurementSupplierReturnCredit {
            Id=2, TenantId=41, SupplierInvoiceId=5, SupplierInvoiceLineId=6,
            CampusId=10, AccountingPeriodId=1, ReturnDate=From,
            SupplierReturnReference="RET-2", StateCode="Reversed",
            CreditAmount=90m, JournalId=52, StockAccountId=1,
            PayableAccountId=2, Reason="Accounting audit fixture" });
        Journal(db, 51, "PayrollPayment", 1, 90m);
        Journal(db, 52, "ProcurementSupplierReturnCredit", 2, 90m);
        db.Journals.Local.Single(x => x.Id == 52).CampusId = 10;
        await db.SaveChangesAsync();

        var service = Service(db);
        var payroll = await service.GetFinanceSourcePostingAuditAsync(From, To, "payroll-payments");
        payroll.StatusCode.Should().Be(200);
        payroll.Data!.Sources.Items.Should().ContainSingle(x => x.SourceId == 1 &&
            x.PostingStatus == "Posted" && x.JournalNumber == "JV-51");
        var returns = await service.GetFinanceSourcePostingAuditAsync(From, To, "procurement-returns");
        returns.StatusCode.Should().Be(200);
        returns.Data!.Sources.Items.Should().ContainSingle(x => x.SourceId == 2 &&
            x.PostingStatus == "Posted" && x.SourceState == "Reversed");
    }

    [Fact]
    public async Task Cross_tenant_permission_date_scope_and_invalid_requests_are_denied()
    {
        var store = "finance-source-audit-"+Guid.NewGuid().ToString("N");
        await using (var outside = Database(store, 42))
        {
            Journal(outside, 11, "StudentInvoice", 1, 90m, tenant:42);
            await outside.SaveChangesAsync();
        }
        await using var db = Database(store);
        SeedAccounts(db);
        db.StudentInvoices.Add(Invoice(1, InvoiceState.Issued, 11));
        await db.SaveChangesAsync();
        var service = Service(db);
        (await Service(db, false).GetFinanceSourcePostingAuditAsync(From, To, "fee-invoices"))
            .StatusCode.Should().Be(403);
        (await service.GetFinanceSourcePostingAuditAsync(To, From, "fee-invoices"))
            .StatusCode.Should().Be(400);
        (await service.GetFinanceSourcePostingAuditAsync(From, To, "bad-module"))
            .StatusCode.Should().Be(400);
        (await service.GetFinanceSourcePostingAuditAsync(From, To, "fee-invoices", pageSize:101))
            .StatusCode.Should().Be(400);
        var page = await service.GetFinanceSourcePostingAuditAsync(From, To, "fee-invoices");
        page.Data!.Sources.Items.Single().PostingStatus.Should().Be("MissingJournal");
        (await service.GetFinanceSourcePostingAuditAsync(From.AddYears(1), To.AddYears(1), "fee-invoices"))
            .Data!.Sources.TotalCount.Should().Be(0);
    }

    private static StudentInvoice Invoice(long id, InvoiceState state, long? journalId, long tenant=41) =>
        new() { Id=id, TenantId=tenant, State=state, JournalId=journalId,
            InvoiceNumber="INV-"+id, InvoiceDate=From, DueDate=To,
            TotalAmount=90m, StudentEnrollmentId=1, ClientRequestId=Guid.NewGuid() };

    private static void SeedAccounts(EduOSDbContext db) => db.Accounts.AddRange(
        new Account { Id=1, TenantId=41, Code="CASH", Name="Cash", AccountType=AccountType.Asset },
        new Account { Id=2, TenantId=41, Code="FEES", Name="Fees", AccountType=AccountType.Income });

    private static void Journal(EduOSDbContext db, long id, string source, long sourceId,
        decimal amount, bool balanced=true, long tenant=41)
    {
        db.Journals.Add(new Journal { Id=id, TenantId=tenant, PublicId=Guid.NewGuid(),
            JournalDate=From, JournalNumber="JV-"+id, AccountingPeriodId=1,
            SourceType=source, SourceId=sourceId, State=JournalState.Posted });
        db.JournalLines.AddRange(
            new JournalLine { Id=id*10+1, TenantId=tenant, JournalId=id,
                AccountId=1, LineNo=1, DebitAmount=amount },
            new JournalLine { Id=id*10+2, TenantId=tenant, JournalId=id,
                AccountId=2, LineNo=2, CreditAmount=balanced?amount:amount-1m });
    }

    private static AccountingReportingService Service(EduOSDbContext db, bool admin=true) =>
        new(db, new TestUser(admin));

    private static EduOSDbContext Database(string? storeName = null, long tenant = 41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(storeName ?? "finance-source-audit-"+Guid.NewGuid().ToString("N")).Options, accessor);
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
