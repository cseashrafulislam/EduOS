using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Facilities;
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
    public async Task Balanced_wrong_fee_and_payroll_payment_amounts_are_invalid()
    {
        await using var db = Database();
        SeedAccounts(db);
        db.StudentInvoices.Add(Invoice(1, InvoiceState.Issued, 11));
        db.PayrollPayments.Add(new PayrollPayment { Id = 2, TenantId = 41,
            PayrollEmployeeId = 3, PaymentDate = From, Amount = 85m,
            State = PaymentState.Successful, JournalId = 12 });
        Journal(db, 11, "StudentInvoice", 1, 89m);
        Journal(db, 12, "PayrollPayment", 2, 90m);
        await db.SaveChangesAsync();

        var service = Service(db);
        var fees = await service.GetFinanceSourcePostingAuditAsync(From, To, "fee-invoices");
        var salaries = await service.GetFinanceSourcePostingAuditAsync(From, To, "payroll-payments");
        fees.Data!.Sources.Items.Should().ContainSingle(x => x.PostingStatus == "InvalidJournal");
        salaries.Data!.Sources.Items.Should().ContainSingle(x => x.PostingStatus == "InvalidJournal");
    }

    [Fact]
    public async Task Supplier_price_variance_and_asset_capitalization_validate_their_own_GL_legs()
    {
        await using var db = Database();
        SeedAccounts(db);
        db.Accounts.AddRange(
            new Account { Id = 3, TenantId = 41, Code = "AP", Name = "Payable", AccountType = AccountType.Liability },
            new Account { Id = 4, TenantId = 41, Code = "VAR", Name = "Variance", AccountType = AccountType.Expense },
            new Account { Id = 5, TenantId = 41, Code = "ASSET", Name = "Fixed Asset", AccountType = AccountType.Asset });
        db.ProcurementSupplierInvoices.Add(new ProcurementSupplierInvoice {
            Id = 1, TenantId = 41, PurchaseOrderId = 7, SupplierId = 8, CampusId = 10,
            SupplierInvoiceNumber = "P-1", NormalizedSupplierInvoiceNumber = "P-1",
            InvoiceDate = From, DueDate = To, TotalAmount = 90m, StockValue = 100m,
            PriceVariance = -10m, StateCode = "Posted", JournalId = 61 });
        db.ProcurementSupplierInvoices.Add(new ProcurementSupplierInvoice {
            Id = 3, TenantId = 41, PurchaseOrderId = 7, SupplierId = 8, CampusId = 10,
            SupplierInvoiceNumber = "P-3", NormalizedSupplierInvoiceNumber = "P-3",
            InvoiceDate = From, DueDate = To, TotalAmount = 90m, StockValue = 99m,
            PriceVariance = -9m, StateCode = "Posted", JournalId = 63 });
        db.AssetCapitalizationCommands.Add(new AssetCapitalizationCommand {
            Id = 2, TenantId = 41, AssetId = 9, CampusId = 10,
            CapitalizationDate = From, ProposedCostBasis = 100m, FixedAssetAccountId = 5,
            StateCode = "Posted", JournalId = 62 });
        db.AssetCapitalizationCommands.Add(new AssetCapitalizationCommand {
            Id = 4, TenantId = 41, AssetId = 10, CampusId = 10,
            CapitalizationDate = From, ProposedCostBasis = 99m, FixedAssetAccountId = 5,
            StateCode = "Posted", JournalId = 64 });
        Journal(db, 61, "ProcurementSupplierInvoice", 1, 100m);
        Journal(db, 62, "AssetCapitalization", 2, 130m);
        Journal(db, 63, "ProcurementSupplierInvoice", 3, 100m);
        Journal(db, 64, "AssetCapitalization", 4, 130m);
        foreach (var journal in db.Journals.Local)
            journal.CampusId = 10;
        foreach (var id in new long[] { 61, 63 })
        {
            db.JournalLines.Local.Single(x => x.JournalId == id && x.DebitAmount > 0).AccountId = 1;
            var credit = db.JournalLines.Local.Single(x => x.JournalId == id && x.CreditAmount > 0);
            credit.AccountId = 3;
            credit.CreditAmount = 90m;
            db.JournalLines.Add(new JournalLine { Id = id * 10 + 3, TenantId = 41,
                JournalId = id, AccountId = 4, LineNo = 3, CreditAmount = 10m });
        }
        foreach (var id in new long[] { 62, 64 })
        {
            var debit = db.JournalLines.Local.Single(x => x.JournalId == id && x.DebitAmount > 0);
            debit.AccountId = 5;
            debit.DebitAmount = 100m;
            db.JournalLines.Add(new JournalLine { Id = id * 10 + 3, TenantId = 41,
                JournalId = id, AccountId = 4, LineNo = 3, DebitAmount = 30m });
        }
        await db.SaveChangesAsync();

        var service = Service(db);
        var procurement = await service.GetFinanceSourcePostingAuditAsync(From, To, "procurement");
        var assets = await service.GetFinanceSourcePostingAuditAsync(From, To, "assets");
        procurement.Data!.Sources.Items.Should().ContainSingle(x => x.SourceId == 1 && x.PostingStatus == "Posted");
        assets.Data!.Sources.Items.Should().ContainSingle(x => x.SourceId == 2 && x.PostingStatus == "Posted");
        procurement.Data.Sources.Items.Should().ContainSingle(x => x.SourceId == 3 && x.PostingStatus == "InvalidJournal");
        assets.Data.Sources.Items.Should().ContainSingle(x => x.SourceId == 4 && x.PostingStatus == "InvalidJournal");
    }

    [Fact]
    public async Task Posted_source_audit_rejects_foreign_tenant_lines_even_when_local_lines_balance()
    {
        var store = "cross-tenant-line-audit-" + Guid.NewGuid().ToString("N");
        await using (var local = Database(store))
        {
            SeedAccounts(local);
            local.StudentInvoices.Add(Invoice(1, InvoiceState.Issued, 11));
            Journal(local, 11, "StudentInvoice", 1, 90m);
            await local.SaveChangesAsync();
        }
        // Simulate legacy/import corruption through a different authorized tenant
        // context; the normal application writer does not allow cross-tenant inserts.
        await using (var foreign = Database(store, 42))
        {
            foreign.JournalLines.Add(new JournalLine { Id = 113, TenantId = 42,
                JournalId = 11, AccountId = 1, LineNo = 3, DebitAmount = 10m });
            await foreign.SaveChangesAsync();
        }
        await using var db = Database(store);
        var result = await Service(db).GetFinanceSourcePostingAuditAsync(From, To, "fee-invoices");
        result.StatusCode.Should().Be(200);
        result.Data!.Sources.Items.Should().ContainSingle(x => x.SourceId == 1 &&
            x.PostingStatus == "InvalidJournal");
    }

    [Fact]
    public async Task Source_audit_validates_more_than_five_thousand_lines_and_fifteen_hundred_accounts()
    {
        await using var db = Database();
        SeedAccounts(db);
        var invoice = Invoice(1, InvoiceState.Issued, 11);
        invoice.TotalAmount = 25.01m;
        db.StudentInvoices.Add(invoice);
        db.Journals.Add(new Journal { Id = 11, TenantId = 41, PublicId = Guid.NewGuid(),
            JournalDate = From, JournalNumber = "JV-LARGE", AccountingPeriodId = 1,
            SourceType = "StudentInvoice", SourceId = invoice.Id, State = JournalState.Posted });
        var accounts = Enumerable.Range(0, 1601).Select(i => new Account { Id = 100 + i,
            TenantId = 41, Code = $"A{i:0000}", Name = $"Posting account {i}",
            AccountType = AccountType.Asset }).ToArray();
        db.Accounts.AddRange(accounts);
        db.JournalLines.AddRange(Enumerable.Range(0, 2501).SelectMany(i => new[] {
            new JournalLine { Id = 10000 + i * 2, TenantId = 41, JournalId = 11,
                AccountId = accounts[i % accounts.Length].Id, LineNo = i * 2 + 1,
                DebitAmount = 0.01m },
            new JournalLine { Id = 10001 + i * 2, TenantId = 41, JournalId = 11,
                AccountId = 2, LineNo = i * 2 + 2, CreditAmount = 0.01m }
        }));
        await db.SaveChangesAsync();

        var audit = await Service(db).GetFinanceSourcePostingAuditAsync(From, To, "fee-invoices");
        audit.StatusCode.Should().Be(200);
        audit.Data!.Sources.Items.Should().ContainSingle(x => x.SourceId == 1 &&
            x.PostingStatus == "Posted" && x.JournalNumber == "JV-LARGE");

        accounts[^1].IsDeleted = true;
        await db.SaveChangesAsync();
        var invalid = await Service(db).GetFinanceSourcePostingAuditAsync(From, To, "fee-invoices");
        invalid.StatusCode.Should().Be(200);
        invalid.Data!.Sources.Items.Should().ContainSingle(x => x.SourceId == 1 &&
            x.PostingStatus == "InvalidJournal");
    }

    [Fact]
    public async Task Payroll_payment_and_supplier_return_audit_keep_the_original_posted_lineage()
    {
        await using var db = Database();
        SeedAccounts(db);
        db.Accounts.Add(new Account { Id = 3, TenantId = 41, Code = "PAYABLE",
            Name = "Supplier payable", AccountType = AccountType.Liability });
        db.PayrollPayments.Add(new PayrollPayment { Id=1, TenantId=41,
            PayrollEmployeeId=2, PaymentDate=From, Amount=90m,
            State=PaymentState.Successful, JournalId=51 });
        db.ProcurementSupplierReturnCredits.Add(new ProcurementSupplierReturnCredit {
            Id=2, TenantId=41, SupplierInvoiceId=5, SupplierInvoiceLineId=6,
            CampusId=10, AccountingPeriodId=1, ReturnDate=From,
            SupplierReturnReference="RET-2", StateCode="Reversed",
            CreditAmount=90m, StockValue=90m, UnpaidCreditAmount=90m,
            JournalId=52, StockAccountId=1,
            PayableAccountId=3, Reason="Accounting audit fixture" });
        Journal(db, 51, "PayrollPayment", 1, 90m);
        Journal(db, 52, "ProcurementSupplierReturnCredit", 2, 90m);
        db.Journals.Local.Single(x => x.Id == 52).CampusId = 10;
        db.JournalLines.Local.Single(x => x.JournalId == 52 && x.DebitAmount > 0m).AccountId = 3;
        db.JournalLines.Local.Single(x => x.JournalId == 52 && x.CreditAmount > 0m).AccountId = 1;
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
    public async Task Procurement_inventory_and_facility_service_share_GL_without_double_counting_purchase_as_expense()
    {
        await using var db = Database();
        SeedAccounts(db);
        db.Accounts.AddRange(
            new Account { Id=3, TenantId=41, Code="EXP", Name="Facility expense", AccountType=AccountType.Expense },
            new Account { Id=4, TenantId=41, Code="AP", Name="Supplier payable", AccountType=AccountType.Liability },
            new Account { Id=5, TenantId=41, Code="INV", Name="Inventory asset", AccountType=AccountType.Asset });
        db.ProcurementSupplierInvoices.Add(new ProcurementSupplierInvoice {
            Id=1, TenantId=41, PurchaseOrderId=7, SupplierId=8, CampusId=10,
            SupplierInvoiceNumber="SUP-1", NormalizedSupplierInvoiceNumber="SUP-1",
            InvoiceDate=From, DueDate=To, TotalAmount=90m, StockValue=90m,
            StateCode="Posted", JournalId=61 });
        db.FacilityVendorServiceBills.Add(new FacilityVendorServiceBill {
            Id=2, TenantId=41, FacilityWorkOrderId=9, SupplierId=8, CampusId=10,
            SupplierInvoiceNumber="FAC-2", NormalizedSupplierInvoiceNumber="FAC-2",
            InvoiceDate=From, DueDate=To, AmountBdt=90m,
            StateCode="Posted", JournalId=62 });
        Journal(db, 61, "ProcurementSupplierInvoice", 1, 90m);
        Journal(db, 62, "FacilityVendorServiceBill", 2, 90m);
        db.Journals.Local.Single(x => x.Id == 61).CampusId = 10;
        db.Journals.Local.Single(x => x.Id == 62).CampusId = 10;
        db.JournalLines.Local.Single(x => x.JournalId == 61 && x.DebitAmount > 0m).AccountId = 5;
        db.JournalLines.Local.Single(x => x.JournalId == 61 && x.CreditAmount > 0m).AccountId = 4;
        db.JournalLines.Local.Single(x => x.JournalId == 62 && x.DebitAmount > 0m).AccountId = 3;
        db.JournalLines.Local.Single(x => x.JournalId == 62 && x.CreditAmount > 0m).AccountId = 4;
        await db.SaveChangesAsync();

        var service = Service(db);
        var procurement = await service.GetFinanceSourcePostingAuditAsync(From, To, "procurement");
        var facility = await service.GetFinanceSourcePostingAuditAsync(From, To, "facilities");
        procurement.StatusCode.Should().Be(200);
        facility.StatusCode.Should().Be(200);
        procurement.Data!.Sources.Items.Should().ContainSingle(x => x.SourceId == 1 && x.PostingStatus == "Posted");
        facility.Data!.Sources.Items.Should().ContainSingle(x => x.SourceId == 2 && x.PostingStatus == "Posted");

        var incomeCost = await service.GetInstitutionalIncomeExpenseDetailsAsync(From, To);
        incomeCost.StatusCode.Should().Be(200);
        incomeCost.Data!.TotalIncome.Should().Be(0m);
        incomeCost.Data.TotalExpenses.Should().Be(90m);
        incomeCost.Data.Entries.Items.Should().ContainSingle(x =>
            x.SourceType == "FacilityVendorServiceBill" && x.Amount == 90m);
    }

    [Fact]
    public async Task Paid_refunds_reuse_the_existing_cash_and_credit_note_GL_evidence()
    {
        await using var db = Database();
        SeedAccounts(db);
        db.Accounts.Add(new Account { Id=3, TenantId=41, Code="AR", Name="Student receivables",
            AccountType=AccountType.Asset });
        db.StudentPayments.Add(new StudentPayment { Id=1, TenantId=41, StudentId=3,
            ReceiptNumber="RCPT-1", Amount=90m, PaymentDate=From,
            State=PaymentState.Refunded });
        db.StudentEnrollments.Add(new StudentEnrollment { Id=14, TenantId=41, StudentId=3,
            CampusId=10, RollNo="3", EnrollmentDate=From });
        db.StudentInvoices.Add(Invoice(22, InvoiceState.Paid, null));
        db.StudentInvoices.Local.Single().StudentEnrollmentId = 14;
        db.PaymentAllocations.Add(new PaymentAllocation { Id=31, TenantId=41,
            StudentPaymentId=1, StudentInvoiceId=22, Amount=90m });
        db.Refunds.AddRange(
            new Refund { Id=7, TenantId=41, StudentPaymentId=1, Amount=30m,
                State=RefundState.Paid, ReversalJournalId=77,
                CreatedAt=From.ToDateTime(TimeOnly.MinValue),
                PaidAt=From.AddDays(1).ToDateTime(TimeOnly.MinValue) },
            new Refund { Id=8, TenantId=41, StudentPaymentId=1, Amount=10m,
                State=RefundState.Paid, ReversalJournalId=null,
                CreatedAt=From.ToDateTime(TimeOnly.MinValue),
                PaidAt=From.AddDays(2).ToDateTime(TimeOnly.MinValue) },
            new Refund { Id=9, TenantId=41, StudentPaymentId=1, Amount=5m,
                State=RefundState.Approved, CreatedAt=From.ToDateTime(TimeOnly.MinValue) });
        db.RefundAllocations.Add(new RefundAllocation { Id=44, TenantId=41,
            RefundId=7, PaymentAllocationId=31, Amount=10m, IsCreditNote=true });
        Journal(db, 77, "StudentRefund", 7, 30m);
        Journal(db, 78, "StudentRefundCreditNote", 7, 10m);
        foreach (var journal in db.Journals.Local)
            journal.CampusId = 10;
        foreach (var line in db.JournalLines.Local)
        {
            line.CampusId = 10;
            line.StudentId = 3;
        }
        db.JournalLines.Local.Single(x => x.JournalId == 77 && x.DebitAmount > 0).AccountId = 3;
        db.JournalLines.Local.Single(x => x.JournalId == 77 && x.CreditAmount > 0).AccountId = 1;
        db.JournalLines.Local.Single(x => x.JournalId == 78 && x.DebitAmount > 0).AccountId = 2;
        db.JournalLines.Local.Single(x => x.JournalId == 78 && x.CreditAmount > 0).AccountId = 3;
        await db.SaveChangesAsync();

        // SaveChanges assigns the actual UTC CreatedAt to pending requests;
        // include that date rather than pretending the seeded creation date survived.
        var reportEnd = DateOnly.FromDateTime(db.Refunds.Local.Single(x => x.Id == 9).CreatedAt);
        var audit = await Service(db).GetFinanceSourcePostingAuditAsync(From, reportEnd, "fee-refunds");
        audit.StatusCode.Should().Be(200);
        audit.Data!.LinkedCount.Should().Be(1);
        audit.Data.PendingPostingCount.Should().Be(1);
        audit.Data.UnpostedCount.Should().Be(1);
        audit.Data.Sources.Items.Single(x => x.SourceId == 7).PostingStatus.Should().Be("Posted");
        audit.Data.Sources.Items.Single(x => x.SourceId == 7).DocumentNumber.Should().Be("RCPT-1");
        audit.Data.Sources.Items.Single(x => x.SourceId == 8).PostingStatus.Should().Be("PendingPosting");
        audit.Data.Sources.Items.Single(x => x.SourceId == 9).PostingStatus.Should().Be("NotPosted");

        // Duplicate cash-side source postings must not pass as a single paid refund.
        Journal(db, 79, "StudentRefund", 7, 30m);
        db.Journals.Local.Single(x => x.Id == 79).CampusId = 10;
        foreach (var line in db.JournalLines.Local.Where(x => x.JournalId == 79))
        {
            line.StudentId = 3;
            line.CampusId = 10;
        }
        await db.SaveChangesAsync();
        var duplicate = await Service(db).GetFinanceSourcePostingAuditAsync(From, reportEnd, "fee-refunds");
        duplicate.Data!.Sources.Items.Single(x => x.SourceId == 7).PostingStatus.Should().Be("InvalidJournal");
        (await Service(db, false).GetFinanceSourcePostingAuditAsync(From, reportEnd, "fee-refunds"))
            .StatusCode.Should().Be(403);
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
