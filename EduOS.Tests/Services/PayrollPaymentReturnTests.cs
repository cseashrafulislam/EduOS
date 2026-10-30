using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollPaymentReturnTests
{
    [Fact]
    public async Task Returned_salary_is_independently_reviewed_reversed_and_reissuable_without_deleting_history()
    {
        await using var db = Database();
        var payment = await SeedPostedSalaryAsync(db);
        var request = ReturnRequest(payment);
        var maker = Service(db, 9);
        var proposed = await maker.RequestReturnAsync(payment.PublicId, request);
        proposed.Success.Should().BeTrue(proposed.Message);
        await StampInMemoryVersionAsync(db, proposed.Data!);
        proposed.Data!.State.Should().Be(JournalState.Submitted);
        db.PayrollPayments.Single().State.Should().Be(PaymentState.Successful);
        (await maker.ReviewReturnAsync(payment.PublicId, proposed.Data.ReturnReference,
            new ReviewPayrollPaymentReturnDto { Approve = true, RowVersion = proposed.Data.RowVersion }))
            .StatusCode.Should().Be(403);

        var checker = Service(db, 10);
        var approved = await checker.ReviewReturnAsync(payment.PublicId, proposed.Data.ReturnReference,
            new ReviewPayrollPaymentReturnDto { Approve = true, RowVersion = proposed.Data.RowVersion });
        approved.Success.Should().BeTrue(approved.Message);
        approved.Data!.State.Should().Be(JournalState.Posted);
        (await db.PayrollPayments.SingleAsync()).State.Should().Be(PaymentState.Successful);
        var original = await db.Journals.SingleAsync(x => x.SourceType == "PayrollPayment");
        original.State.Should().Be(JournalState.Posted);
        var contra = await db.Journals.SingleAsync(x => x.SourceType == "PayrollPaymentReturn");
        contra.ReversalOfJournalId.Should().Be(original.Id);
        contra.CreatedBy.Should().Be(9);
        contra.ApprovedByUserId.Should().Be(10);
        var sourceLines = await db.JournalLines.Where(x => x.JournalId == original.Id)
            .OrderBy(x => x.LineNo).ToListAsync();
        var reversedLines = await db.JournalLines.Where(x => x.JournalId == contra.Id)
            .OrderBy(x => x.LineNo).ToListAsync();
        reversedLines.Should().HaveCount(2);
        for (var i = 0; i < 2; i++)
        {
            reversedLines[i].DebitAmount.Should().Be(sourceLines[i].CreditAmount);
            reversedLines[i].CreditAmount.Should().Be(sourceLines[i].DebitAmount);
        }
        (await maker.RequestReturnAsync(payment.PublicId, request)).Data!.AlreadyProcessed.Should().BeTrue();
        (await checker.ReviewReturnAsync(payment.PublicId, proposed.Data.ReturnReference,
            new ReviewPayrollPaymentReturnDto { Approve = true, RowVersion = proposed.Data.RowVersion }))
            .Data!.AlreadyProcessed.Should().BeTrue();

        var register = await checker.GetRegisterAsync(false, 1, 10);
        register.Success.Should().BeTrue(register.Message);
        var row = register.Data!.Payments.Single();
        row.ReconciliationStatus.Should().Be("Returned");
        row.SettledAmount.Should().Be(0m);
        row.RemainingAmount.Should().Be(100m);
        row.ReturnReference.Should().Be(contra.PublicId);

        // The corrected amount may be paid only through a fresh independently verified request.
        db.PayrollPayments.Add(new PayrollPayment { TenantId = 41, PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(), PayrollEmployeeId = 1,
            PaymentDate = new DateOnly(2026, 10, 20), PaymentMethod = PaymentMethodType.Cash,
            Amount = 30m, State = PaymentState.AwaitingVerification,
            RowVersion = [2], CreatedBy = 5 });
        await db.SaveChangesAsync();
        var next = await db.PayrollPayments.SingleAsync(x => x.State == PaymentState.AwaitingVerification);
        var posted = await checker.PostAsync(next.PublicId, new PostPayrollPaymentJournalRequestDto
        { AccountingPeriodId = 1, SettlementAccountId = 2,
            RowVersion = Convert.ToBase64String(next.RowVersion) });
        posted.Success.Should().BeTrue(posted.Message);
        (await db.Journals.CountAsync(x => x.SourceType == "PayrollPaymentReturn")).Should().Be(1);
        (await db.Journals.CountAsync(x => x.SourceType == "PayrollPayment")).Should().Be(2);
    }

    [Fact]
    public async Task Rejected_return_preserves_payment_and_replay_requires_identical_evidence()
    {
        await using var db = Database();
        var payment = await SeedPostedSalaryAsync(db);
        var request = ReturnRequest(payment);
        var maker = Service(db, 9);
        var proposed = await maker.RequestReturnAsync(payment.PublicId, request);
        proposed.Success.Should().BeTrue(proposed.Message);
        await StampInMemoryVersionAsync(db, proposed.Data!);
        var changed = ReturnRequest(payment);
        changed.ClientRequestId = request.ClientRequestId;
        changed.EvidenceReference = "OTHER-VOUCHER";
        (await maker.RequestReturnAsync(payment.PublicId, changed)).StatusCode.Should().Be(409);
        var checker = Service(db, 10);
        var rejected = await checker.ReviewReturnAsync(payment.PublicId, proposed.Data!.ReturnReference,
            new ReviewPayrollPaymentReturnDto { Approve = false, RowVersion = proposed.Data.RowVersion,
                RejectionReason = "No evidence of returned cash" });
        rejected.Success.Should().BeTrue(rejected.Message);
        (await db.PayrollPayments.SingleAsync()).State.Should().Be(PaymentState.Successful);
        db.Journals.Single(x => x.SourceType == "PayrollPaymentReturn").State
            .Should().Be(JournalState.Rejected);
        // The same referenced return cannot be turned into a posted journal after rejection.
        (await checker.ReviewReturnAsync(payment.PublicId, proposed.Data.ReturnReference,
            new ReviewPayrollPaymentReturnDto { Approve = true, RowVersion = proposed.Data.RowVersion }))
            .StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Posted_payroll_accrual_cannot_be_reversed_until_every_disbursement_is_returned()
    {
        await using var db = Database();
        var payment = await SeedPostedSalaryAsync(db);
        var run = await db.PayrollRuns.SingleAsync();
        var reversal = new PayrollJournalPostingService(db, new Actor(11, 41, "TenantAdmin"), TimeProvider.System);
        var request = new RequestPayrollReversalDto
        {
            ClientRequestId = Guid.NewGuid(), AccountingPeriodId = 1,
            ReversalDate = new DateOnly(2026, 10, 27),
            Reason = "Correct entirely returned salary after independent verification",
            RowVersion = Convert.ToBase64String(run.RowVersion)
        };
        (await reversal.RequestReversalAsync(run.PublicId, request)).StatusCode.Should().Be(409);
        var pending = await Service(db, 9).RequestReturnAsync(payment.PublicId, ReturnRequest(payment));
        pending.Success.Should().BeTrue();
        (await reversal.RequestReversalAsync(run.PublicId, request)).StatusCode.Should().Be(409);
        await StampInMemoryVersionAsync(db, pending.Data!);
        (await Service(db, 10).ReviewReturnAsync(payment.PublicId, pending.Data!.ReturnReference,
            new ReviewPayrollPaymentReturnDto { Approve = true, RowVersion = pending.Data.RowVersion }))
            .Success.Should().BeTrue();
        var allowed = await reversal.RequestReversalAsync(run.PublicId, request);
        allowed.Success.Should().BeTrue(allowed.Message);
        (await db.PayrollPayments.SingleAsync()).State.Should().Be(PaymentState.Successful);
        (await db.Journals.SingleAsync(x => x.SourceType == "PayrollRun")).State.Should().Be(JournalState.Posted);
    }

    [Fact]
    public async Task New_payroll_payment_request_reserves_only_the_net_outstanding_after_verified_return()
    {
        await using var db = Database();
        var original = await SeedPostedSalaryAsync(db);
        var proposed = await Service(db, 9).RequestReturnAsync(original.PublicId, ReturnRequest(original));
        proposed.Success.Should().BeTrue();
        await StampInMemoryVersionAsync(db, proposed.Data!);
        (await Service(db, 10).ReviewReturnAsync(original.PublicId, proposed.Data!.ReturnReference,
            new ReviewPayrollPaymentReturnDto { Approve = true, RowVersion = proposed.Data.RowVersion }))
            .Success.Should().BeTrue();

        GenericRepository<T> Repo<T>() where T : class => new(db);
        var administration = new HrPayrollService(
            Repo<Employee>(), Repo<ApplicationUser>(), Repo<Campus>(),
            Repo<EmployeeCampusAssignment>(), Repo<SalaryComponent>(),
            Repo<SalaryStructure>(), Repo<SalaryStructureLine>(), Repo<PayrollRun>(),
            Repo<PayrollEmployee>(), Repo<PayrollLine>(), Repo<PayrollPayment>(),
            Repo<Bonus>(), Repo<LoanAdvance>(), Repo<LoanAdvanceRecovery>(),
            Repo<BankAccount>(), Repo<Journal>(), Repo<EmployeeBankAccount>(),
            db, new Actor(10, 41, "TenantAdmin"), TimeProvider.System,
            NullLogger<HrPayrollService>.Instance);
        var employee = await db.PayrollEmployees.SingleAsync();
        var newRequest = await administration.RecordPayrollPaymentAsync(
            new RecordPayrollPaymentRequestDto
            {
                ClientRequestId = Guid.NewGuid(), PayrollEmployeeId = employee.Id,
                PaymentDate = new DateOnly(2026, 10, 27),
                PaymentMethod = PaymentMethodType.Cash, Amount = 100m,
                PayrollEmployeeRowVersion = Convert.ToBase64String(employee.RowVersion)
            });
        newRequest.Success.Should().BeTrue(newRequest.Message);
        (await db.PayrollPayments.CountAsync(x => x.State == PaymentState.Successful)).Should().Be(1);
        (await db.PayrollPayments.CountAsync(x => x.State == PaymentState.AwaitingVerification)).Should().Be(1);
    }

    [Fact]
    public async Task Unverified_or_corrupt_return_cannot_free_salary_payable_for_another_payment()
    {
        await using var db = Database();
        var payment = await SeedPostedSalaryAsync(db);
        var proposed = await Service(db, 9).RequestReturnAsync(payment.PublicId, ReturnRequest(payment));
        await StampInMemoryVersionAsync(db, proposed.Data!);
        (await Service(db, 10).ReviewReturnAsync(payment.PublicId, proposed.Data!.ReturnReference,
            new ReviewPayrollPaymentReturnDto { Approve = true, RowVersion = proposed.Data.RowVersion }))
            .Success.Should().BeTrue();
        // Simulate an inconsistent imported history with an additional posted contra.
        // A posted journal's lines are immutable and cannot be tampered with through EF.
        db.Journals.Add(new Journal { TenantId = 41, JournalNumber = "DUPLICATE-RETURN",
            PublicId = Guid.NewGuid(), SourceType = "PayrollPaymentReturn", SourceId = payment.Id,
            ReversalOfJournalId = payment.JournalId, State = JournalState.Posted,
            AccountingPeriodId = 1, JournalDate = new DateOnly(2026, 10, 25) });
        await db.SaveChangesAsync();
        var register = await Service(db, 10).GetRegisterAsync(false, 1, 10);
        register.Data!.Payments.Single().ReconciliationStatus.Should().Be("RequiresReview");
        db.PayrollPayments.Add(new PayrollPayment { TenantId = 41, PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(), PayrollEmployeeId = 1,
            PaymentDate = new DateOnly(2026, 10, 20), PaymentMethod = PaymentMethodType.Cash,
            Amount = 20m, State = PaymentState.AwaitingVerification,
            RowVersion = [3], CreatedBy = 5 });
        await db.SaveChangesAsync();
        var next = await db.PayrollPayments.SingleAsync(x => x.State == PaymentState.AwaitingVerification);
        (await Service(db, 10).PostAsync(next.PublicId, new PostPayrollPaymentJournalRequestDto
        { AccountingPeriodId = 1, SettlementAccountId = 2,
            RowVersion = Convert.ToBase64String(next.RowVersion) })).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Non_admin_and_other_tenant_cannot_request_or_review_salary_return()
    {
        await using var db = Database();
        var payment = await SeedPostedSalaryAsync(db);
        var request = ReturnRequest(payment);
        (await Service(db, 10, "Accountant").RequestReturnAsync(payment.PublicId, request))
            .StatusCode.Should().Be(403);
        (await Service(db, 10, "TenantAdmin", 42).RequestReturnAsync(payment.PublicId, request))
            .StatusCode.Should().Be(403);
        (await Service(db, 9).RequestReturnAsync(payment.PublicId, request)).Success.Should().BeTrue();
    }

    private static RequestPayrollPaymentReturnDto ReturnRequest(PayrollPayment payment) => new()
    {
        ClientRequestId = Guid.NewGuid(), AccountingPeriodId = 1,
        ReturnDate = new DateOnly(2026, 10, 25),
        EvidenceReference = "CASH-RETURN-2026-001", Reason = "Confirmed overpayment returned in full",
        RowVersion = Convert.ToBase64String(payment.RowVersion)
    };

    // EF InMemory does not generate SQL Server rowversion values. Simulate the database token
    // after insertion; the real SQL test exercises SQL Server-generated versions separately.
    private static async Task StampInMemoryVersionAsync(EduOSDbContext db, PayrollPaymentReturnResultDto result)
    {
        var journal = await db.Journals.SingleAsync(x => x.PublicId == result.ReturnReference);
        journal.RowVersion = [4, 3, 2, 1];
        await db.SaveChangesAsync();
        result.RowVersion = Convert.ToBase64String(journal.RowVersion);
    }

    private static async Task<PayrollPayment> SeedPostedSalaryAsync(EduOSDbContext db)
    {
        db.Accounts.AddRange(
            new Account { Id = 1, TenantId = 41, Code = "SALARY-PAYABLE", Name = "Salary payable",
                AccountType = AccountType.Liability },
            new Account { Id = 2, TenantId = 41, Code = "CASH-HAND", Name = "Cash in hand",
                AccountType = AccountType.Asset },
            new Account { Id = 3, TenantId = 41, Code = "SALARY-EXP", Name = "Salary expense",
                AccountType = AccountType.Expense });
        db.AccountingPeriods.Add(new AccountingPeriod { Id = 1, TenantId = 41, Name = "October 2026",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31) });
        db.PayrollRuns.Add(new PayrollRun { Id = 1, TenantId = 41, PublicId = Guid.NewGuid(),
            RunNumber = "PAY-2026-10", PeriodEnd = new DateOnly(2026, 10, 19),
            State = PayrollRunState.Posted, JournalId = 1, RowVersion = [1] });
        db.PayrollEmployees.Add(new PayrollEmployee { Id = 1, TenantId = 41,
            PayrollRunId = 1, EmployeeId = 7, NetAmount = 100m, GrossAmount = 100m,
            RowVersion = [1] });
        db.Journals.AddRange(
            new Journal { Id = 1, TenantId = 41, JournalNumber = "JRN-ACCRUAL", PublicId = Guid.NewGuid(),
                JournalDate = new DateOnly(2026, 10, 19), AccountingPeriodId = 1,
                SourceType = "PayrollRun", SourceId = 1, State = JournalState.Posted },
            new Journal { Id = 2, TenantId = 41, JournalNumber = "JRN-SETTLE", PublicId = Guid.NewGuid(),
                JournalDate = new DateOnly(2026, 10, 20), AccountingPeriodId = 1,
                SourceType = "PayrollPayment", SourceId = 1, State = JournalState.Posted });
        db.JournalLines.AddRange(
            new JournalLine { TenantId = 41, JournalId = 1, LineNo = 1,
                AccountId = 3, DebitAmount = 100m, Description = "Salary expense" },
            new JournalLine { TenantId = 41, JournalId = 1, LineNo = 2,
                AccountId = 1, CreditAmount = 100m, Description = "Payroll net payable" },
            new JournalLine { TenantId = 41, JournalId = 2, LineNo = 1, AccountId = 1,
                EmployeeId = 7, DebitAmount = 40m, Description = "Salary payable settlement" },
            new JournalLine { TenantId = 41, JournalId = 2, LineNo = 2, AccountId = 2,
                EmployeeId = 7, CreditAmount = 40m, Description = "Cash/bank payroll disbursement" });
        var payment = new PayrollPayment { Id = 1, TenantId = 41, PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(), PayrollEmployeeId = 1,
            PaymentDate = new DateOnly(2026, 10, 20), PaymentMethod = PaymentMethodType.Cash,
            Amount = 40m, State = PaymentState.Successful, JournalId = 2,
            RowVersion = [1], CreatedBy = 5 };
        db.PayrollPayments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    private static PayrollPaymentPostingService Service(EduOSDbContext db, long id,
        string role = "TenantAdmin", long tenant = 41) => new(db, new Actor(id, tenant, role), TimeProvider.System);
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("payroll-return-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
    }
    private sealed class Actor(long id, long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
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
