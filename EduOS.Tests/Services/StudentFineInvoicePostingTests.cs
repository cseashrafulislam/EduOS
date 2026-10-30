using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentFineInvoicePostingTests
{
    [Fact]
    public async Task Separate_administrator_posts_fine_once_with_balanced_ledger_and_audited_invoice_link()
    {
        await using var db = Database();
        var fine = await SeedAsync(db);
        var request = new PostStudentFineInvoiceRequestDto
        {
            AccountingPeriodId = db.AccountingPeriods.Single().Id,
            RowVersion = Convert.ToBase64String(fine.RowVersion)
        };
        var assessor = new FinanceAdjustmentService(db, new TestUser(10), TimeProvider.System);
        (await assessor.PostFineInvoiceAsync(fine.Id, request)).StatusCode.Should().Be(403);
        db.StudentInvoices.Should().BeEmpty();

        var poster = new FinanceAdjustmentService(db, new TestUser(20), TimeProvider.System);
        var posted = await poster.PostFineInvoiceAsync(fine.Id, request);
        posted.Success.Should().BeTrue();
        posted.Data!.AlreadyPosted.Should().BeFalse();
        var replay = await poster.PostFineInvoiceAsync(fine.Id, request);
        replay.Success.Should().BeTrue();
        replay.Data!.AlreadyPosted.Should().BeTrue();
        replay.Data.InvoiceReference.Should().Be(posted.Data.InvoiceReference);
        db.StudentInvoices.Should().ContainSingle();
        db.StudentInvoiceLines.Should().ContainSingle();
        db.Journals.Should().ContainSingle();
        db.JournalLines.Should().HaveCount(2);
        var invoice = db.StudentInvoices.Single();
        invoice.PaidAmount.Should().Be(0m);
        invoice.DueAmount.Should().Be(75m);
        invoice.FineAmount.Should().Be(75m);
        db.StudentInvoiceLines.Single().StudentFineId.Should().Be(fine.Id);
        db.JournalLines.Sum(x => x.DebitAmount).Should().Be(75m);
        db.JournalLines.Sum(x => x.CreditAmount).Should().Be(75m);
        db.Journals.Single().SourceType.Should().Be("StudentInvoice");
        db.Journals.Single().SourceId.Should().Be(invoice.Id);
        var register = await poster.GetFineRegisterAsync(new StudentFineRegisterQueryDto());
        register.Success.Should().BeTrue();
        register.Data!.PostedFineTotal.Should().Be(75m);
        register.Data.OutstandingAdministrativeTotal.Should().Be(0m);
        register.Data.Fines.Items.Single().BilledInvoiceReference.Should().Be(invoice.PublicId);
        var studentRegister = await poster.GetStudentRegisterAsync(db.StudentEnrollments.Single().PublicId);
        studentRegister.Success.Should().BeTrue();
        studentRegister.Data!.Fines.Single().BilledInvoiceReference.Should().Be(invoice.PublicId);
        (await poster.WaiveFineAsync(fine.Id, new WaiveStudentFineRequestDto
        {
            RowVersion = request.RowVersion, Reason = "Must reverse posted history"
        })).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Fine_posting_refuses_wrong_period_and_unconfigured_income_mapping_without_changes()
    {
        await using var db = Database();
        var fine = await SeedAsync(db);
        var service = new FinanceAdjustmentService(db, new TestUser(20), TimeProvider.System);
        var request = new PostStudentFineInvoiceRequestDto
        {
            AccountingPeriodId = db.AccountingPeriods.Single().Id,
            RowVersion = Convert.ToBase64String(fine.RowVersion)
        };
        db.AccountingPeriods.Single().IsClosed = true;
        await TestTenantSeeder.SaveAsync(db);
        (await service.PostFineInvoiceAsync(fine.Id, request)).StatusCode.Should().Be(409);
        db.AccountingPeriods.Single().IsClosed = false;
        db.FeeHeads.Single().IncomeAccountId = null;
        await TestTenantSeeder.SaveAsync(db);
        (await service.PostFineInvoiceAsync(fine.Id, request)).StatusCode.Should().Be(409);
        db.StudentInvoices.Should().BeEmpty();
        db.JournalLines.Should().BeEmpty();
    }

    [Fact]
    public async Task Foreign_tenant_and_staff_cannot_post_a_fine()
    {
        await using var db = Database();
        var service = new FinanceAdjustmentService(db, new TestUser(20, 12), TimeProvider.System);
        (await service.PostFineInvoiceAsync(1, new PostStudentFineInvoiceRequestDto
        { AccountingPeriodId = 1, RowVersion = "AQ==" })).StatusCode.Should().Be(403);
        var staff = new FinanceAdjustmentService(db, new TestUser(20, 11, false), TimeProvider.System);
        (await staff.PostFineInvoiceAsync(1, new PostStudentFineInvoiceRequestDto
        { AccountingPeriodId = 1, RowVersion = "AQ==" })).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Posted_unpaid_fine_reversal_requires_second_administrator_and_preserves_source_history()
    {
        await using var db = Database();
        var fine = await SeedAsync(db);
        var periodId = db.AccountingPeriods.Single().Id;
        var post = new FinanceAdjustmentService(db, new TestUser(20), TimeProvider.System);
        (await post.PostFineInvoiceAsync(fine.Id, new PostStudentFineInvoiceRequestDto
        { AccountingPeriodId = periodId, RowVersion = Convert.ToBase64String(fine.RowVersion) })).Success.Should().BeTrue();
        var invoice = db.StudentInvoices.Single();
        var original = db.Journals.Single();
        var request = new RequestStudentFineReversalDto
        {
            ClientRequestId = Guid.NewGuid(), AccountingPeriodId = periodId,
            ReversalDate = new DateOnly(2026, 10, 29),
            Reason = "Formal administrative fine waiver", RowVersion = Convert.ToBase64String(fine.RowVersion)
        };
        var submitted = await post.RequestFineReversalAsync(fine.Id, request);
        submitted.Success.Should().BeTrue();
        submitted.Data!.State.Should().Be(JournalState.Submitted);
        var retried = await post.RequestFineReversalAsync(fine.Id, request);
        retried.Success.Should().BeTrue();
        retried.Data!.AlreadyProcessed.Should().BeTrue();
        db.Journals.Should().HaveCount(2);
        db.JournalLines.Should().HaveCount(4);
        invoice.State.Should().Be(InvoiceState.Issued);
        fine.IsWaived.Should().BeFalse();
        var reversal = db.Journals.Single(x => x.State == JournalState.Submitted);
        reversal.RowVersion = new byte[] { 4, 5, 6 };
        await TestTenantSeeder.SaveAsync(db);
        var review = new ReviewStudentFineReversalDto
        { Approve = true, RowVersion = Convert.ToBase64String(reversal.RowVersion) };
        (await post.ReviewFineReversalAsync(fine.Id, reversal.PublicId, review)).StatusCode.Should().Be(403);
        var approver = new FinanceAdjustmentService(db, new TestUser(30), TimeProvider.System);
        var approved = await approver.ReviewFineReversalAsync(fine.Id, reversal.PublicId, review);
        approved.Success.Should().BeTrue();
        approved.Data!.State.Should().Be(JournalState.Posted);
        var replay = await approver.ReviewFineReversalAsync(fine.Id, reversal.PublicId, review);
        replay.Success.Should().BeTrue();
        replay.Data!.AlreadyProcessed.Should().BeTrue();

        invoice.State.Should().Be(InvoiceState.Cancelled);
        invoice.TotalAmount.Should().Be(75m);
        invoice.DueAmount.Should().Be(75m); // Historical invoice figures stay untouched.
        original.State.Should().Be(JournalState.Posted);
        fine.IsWaived.Should().BeTrue();
        fine.WaivedByUserId.Should().Be(30);
        reversal.ReversalOfJournalId.Should().Be(original.Id);
        reversal.ApprovedByUserId.Should().Be(30);
        db.JournalLines.Where(x => x.JournalId == reversal.Id).Sum(x => x.CreditAmount).Should().Be(75m);
        db.JournalLines.Where(x => x.JournalId == reversal.Id).Sum(x => x.DebitAmount).Should().Be(75m);
        db.JournalLines.GroupBy(x => x.AccountId).Select(x =>
            x.Sum(y => y.DebitAmount - y.CreditAmount)).Should().OnlyContain(x => x == 0m);
        var register = await approver.GetFineRegisterAsync(new StudentFineRegisterQueryDto());
        register.Data!.PostedFineTotal.Should().Be(0m);
        register.Data.WaivedTotal.Should().Be(75m);
        register.Data.OutstandingAdministrativeTotal.Should().Be(0m);
        register.Data.Fines.Items.Single().ReversalState.Should().Be(JournalState.Posted);
        var studentRegister = await approver.GetStudentRegisterAsync(db.StudentEnrollments.Single().PublicId);
        studentRegister.Data!.Fines.Single().ReversalState.Should().Be(JournalState.Posted);
        var reconciliation = await new FeeReceivablesAgingService(db, new TestUser(30))
            .GetReconciliationAsync(null, null, 1, 25);
        reconciliation.Success.Should().BeTrue();
        reconciliation.Data!.Rows.Single().Status.Should().Be("Reversed");
        reconciliation.Data.Rows.Single().DueAmount.Should().Be(0m);
        (await post.PostFineInvoiceAsync(fine.Id, new PostStudentFineInvoiceRequestDto
        { AccountingPeriodId = periodId, RowVersion = Convert.ToBase64String(fine.RowVersion) })).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Fine_reversal_rejects_historical_allocation_and_closed_period_without_mutation()
    {
        await using var db = Database();
        var fine = await SeedAsync(db);
        var period = db.AccountingPeriods.Single();
        var service = new FinanceAdjustmentService(db, new TestUser(20), TimeProvider.System);
        (await service.PostFineInvoiceAsync(fine.Id, new PostStudentFineInvoiceRequestDto
        { AccountingPeriodId = period.Id, RowVersion = Convert.ToBase64String(fine.RowVersion) })).Success.Should().BeTrue();
        var request = new RequestStudentFineReversalDto
        {
            ClientRequestId = Guid.NewGuid(), AccountingPeriodId = period.Id,
            ReversalDate = new DateOnly(2026, 10, 29), Reason = "Administrative correction",
            RowVersion = Convert.ToBase64String(fine.RowVersion)
        };
        period.IsClosed = true;
        await TestTenantSeeder.SaveAsync(db);
        (await service.RequestFineReversalAsync(fine.Id, request)).StatusCode.Should().Be(409);
        db.Journals.Should().ContainSingle();
        period.IsClosed = false;
        await TestTenantSeeder.SaveAsync(db);
        var payment = new StudentPayment { TenantId = 11,
            StudentId = db.Students.Single().Id, Amount = 75m, ReceivedByUserId = 20,
            PaymentDate = new DateOnly(2026, 10, 29), ReceiptNumber = "RECEIPT-1" };
        db.StudentPayments.Add(payment);
        await TestTenantSeeder.SaveAsync(db);
        db.PaymentAllocations.Add(new PaymentAllocation
        { TenantId = 11, StudentPaymentId = payment.Id, StudentInvoiceId = db.StudentInvoices.Single().Id, Amount = 75m });
        await TestTenantSeeder.SaveAsync(db);
        (await service.RequestFineReversalAsync(fine.Id, request)).StatusCode.Should().Be(409);
        db.Journals.Should().ContainSingle();
        db.StudentInvoices.Single().State.Should().Be(InvoiceState.Issued);
    }

    [Fact]
    public async Task Rejected_fine_reversal_is_audited_and_can_be_resubmitted_with_a_new_key()
    {
        await using var db = Database();
        var fine = await SeedAsync(db);
        var periodId = db.AccountingPeriods.Single().Id;
        var maker = new FinanceAdjustmentService(db, new TestUser(20), TimeProvider.System);
        (await maker.PostFineInvoiceAsync(fine.Id, new PostStudentFineInvoiceRequestDto
        { AccountingPeriodId = periodId, RowVersion = Convert.ToBase64String(fine.RowVersion) })).Success.Should().BeTrue();
        var request = new RequestStudentFineReversalDto
        { ClientRequestId = Guid.NewGuid(), AccountingPeriodId = periodId,
            ReversalDate = new DateOnly(2026, 10, 29), Reason = "Disputed fine",
            RowVersion = Convert.ToBase64String(fine.RowVersion) };
        var submitted = await maker.RequestFineReversalAsync(fine.Id, request);
        submitted.Success.Should().BeTrue();
        var journal = db.Journals.Single(x => x.State == JournalState.Submitted);
        journal.RowVersion = new byte[] { 4, 5, 6 };
        await TestTenantSeeder.SaveAsync(db);
        var reviewer = new FinanceAdjustmentService(db, new TestUser(30), TimeProvider.System);
        (await reviewer.ReviewFineReversalAsync(fine.Id, journal.PublicId,
            new ReviewStudentFineReversalDto { Approve = false,
                RejectionReason = "Insufficient justification", RowVersion = Convert.ToBase64String(journal.RowVersion) })).Success.Should().BeTrue();
        journal.State.Should().Be(JournalState.Rejected);
        fine.IsWaived.Should().BeFalse();
        db.StudentInvoices.Single().State.Should().Be(InvoiceState.Issued);
        request.ClientRequestId = Guid.NewGuid();
        (await maker.RequestFineReversalAsync(fine.Id, request)).Success.Should().BeTrue();
        db.Journals.Count(x => x.SourceType == "StudentFineReversal").Should().Be(2);
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 11L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("fine-gl-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
    }

    private static async Task<StudentFine> SeedAsync(EduOSDbContext db)
    {
        var campus = new Campus { TenantId = 11, Code = "MAIN", Name = "Main" };
        var student = new Student { TenantId = 11, StudentCode = "S-FINE", FullName = "Student" };
        var receivable = new Account { TenantId = 11, Code = "AR-FINE", Name = "Fine receivable", AccountType = AccountType.Asset };
        var income = new Account { TenantId = 11, Code = "INCOME-FINE", Name = "Fine income", AccountType = AccountType.Income };
        var period = new AccountingPeriod { TenantId = 11, Name = "Oct 2026",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31) };
        db.AddRange(campus, student, receivable, income, period);
        await TestTenantSeeder.SaveAsync(db);
        var enrollment = new StudentEnrollment { TenantId = 11, CampusId = campus.Id,
            StudentId = student.Id, RollNo = "1", AcademicProgramId = 1 };
        var head = new FeeHead { TenantId = 11, Code = "FINE", Name = "Late fine",
            IncomeAccountId = income.Id, ReceivableAccountId = receivable.Id,
            DefaultFrequency = FeeFrequencyType.OneTime };
        db.AddRange(enrollment, head);
        await TestTenantSeeder.SaveAsync(db);
        var rule = new FineRule { TenantId = 11, Name = "Fixed late fine", FeeHeadId = head.Id, Amount = 75m };
        db.Add(rule);
        await TestTenantSeeder.SaveAsync(db);
        var fine = new StudentFine { TenantId = 11, StudentEnrollmentId = enrollment.Id,
            FineRuleId = rule.Id, FineDate = new DateOnly(2026, 10, 25),
            Amount = 75m, CreatedBy = 10, RowVersion = new byte[] { 1, 2, 3 } };
        db.StudentFines.Add(fine);
        await TestTenantSeeder.SaveAsync(db);
        // Test bootstrap is deliberately anonymous and stamps CreatedBy=null;
        // reproduce an actual assessed fine's persisted maker identity.
        fine.CreatedBy = 10;
        await TestTenantSeeder.SaveAsync(db);
        return fine;
    }

    private sealed class TestUser(long userId, long tenantId = 11, bool admin = true) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Staff"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
