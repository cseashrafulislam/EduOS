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

public sealed class PayrollJournalReversalWorkflowTests
{
    private static readonly DateOnly Payday = new(2026, 10, 15);

    [Fact]
    public async Task Request_is_idempotent_and_immutably_negates_original_balanced_journal()
    {
        await using var db = Database();
        var reference = await SeedPostedRun(db);
        var key = Guid.NewGuid();
        var service = Service(db, 11);
        var first = await service.RequestReversalAsync(reference, Request(key));
        first.StatusCode.Should().Be(200, first.Message);
        first.Data!.State.Should().Be(JournalState.Submitted);
        var replay = await service.RequestReversalAsync(reference, Request(key));
        replay.StatusCode.Should().Be(200);
        replay.Data!.AlreadyProcessed.Should().BeTrue();
        replay.Data.ReversalReference.Should().Be(first.Data.ReversalReference);
        (await service.RequestReversalAsync(reference, Request(Guid.NewGuid()))).StatusCode.Should().Be(409);
        var changed = Request(key);changed.Reason = "different reason";
        (await service.RequestReversalAsync(reference,changed)).StatusCode.Should().Be(409);

        var original = await db.Journals.SingleAsync(x => x.Id == 1);
        original.State.Should().Be(JournalState.Posted);
        original.ReversalOfJournalId.Should().BeNull();
        (await db.PayrollRuns.SingleAsync()).State.Should().Be(PayrollRunState.Posted);
        var reversal = await db.Journals.SingleAsync(x => x.ReversalOfJournalId == 1);
        reversal.SourceType.Should().Be("PayrollRunReversal");
        reversal.CreatedBy.Should().Be(11);
        var lines = await db.JournalLines.Where(x => x.JournalId == reversal.Id).OrderBy(x => x.LineNo).ToListAsync();
        lines.Should().HaveCount(2);
        lines[0].CreditAmount.Should().Be(100m);
        lines[1].DebitAmount.Should().Be(100m);
        lines.Sum(x => x.CreditAmount).Should().Be(lines.Sum(x => x.DebitAmount));
    }

    [Fact]
    public async Task Review_and_post_require_independent_administrators_and_preserve_original()
    {
        await using var db = Database();
        var reference = await SeedPostedRun(db);
        (await Service(db,11).RequestReversalAsync(reference, Request(Guid.NewGuid()))).StatusCode.Should().Be(200);
        var journal = await db.Journals.SingleAsync(x => x.ReversalOfJournalId == 1);
        journal.RowVersion = [2];
        (await Service(db,11).ReviewReversalAsync(reference,Review(true))).StatusCode.Should().Be(403);
        (await Service(db,22).ReviewReversalAsync(reference,Review(true))).StatusCode.Should().Be(200);
        journal.State.Should().Be(JournalState.Approved);
        journal.ApprovedByUserId.Should().Be(22);
        (await Service(db,11).PostReversalAsync(reference,Post())).StatusCode.Should().Be(403);
        var posted = await Service(db,33).PostReversalAsync(reference,Post());
        posted.StatusCode.Should().Be(200, posted.Message);
        posted.Data!.State.Should().Be(JournalState.Posted);
        var replay = await Service(db,33).PostReversalAsync(reference,Post());
        replay.StatusCode.Should().Be(200);
        replay.Data!.AlreadyProcessed.Should().BeTrue();
        (await Service(db,22).RequestReversalAsync(reference,Request(Guid.NewGuid()))).StatusCode.Should().Be(409);
        (await db.Journals.SingleAsync(x => x.Id == 1)).State.Should().Be(JournalState.Posted);
        (await db.PayrollRuns.SingleAsync()).State.Should().Be(PayrollRunState.Posted);
        var all = await db.JournalLines.ToListAsync();
        all.Sum(x => x.DebitAmount).Should().Be(all.Sum(x => x.CreditAmount));
        all.Where(x => x.JournalId == journal.Id).Sum(x => x.DebitAmount).Should().Be(100m);
    }

    [Fact]
    public async Task Rejection_can_cancel_approved_reversal_after_later_payment_history()
    {
        await using var db = Database();
        var reference = await SeedPostedRun(db);
        var key = Guid.NewGuid();
        (await Service(db,11).RequestReversalAsync(reference,Request(key))).StatusCode.Should().Be(200);
        var journal = await db.Journals.SingleAsync(x => x.ReversalOfJournalId == 1);
        journal.RowVersion = [2];
        (await Service(db,22).ReviewReversalAsync(reference,Review(true))).StatusCode.Should().Be(200);
        db.PayrollEmployees.Add(new PayrollEmployee { Id = 8,TenantId = 41,PayrollRunId = 1,EmployeeId = 10,NetAmount = 100m });
        db.PayrollPayments.Add(new PayrollPayment { Id = 9,TenantId = 41,PayrollEmployeeId = 8,
            ClientRequestId = Guid.NewGuid(),State = PaymentState.Successful,Amount = 10m });
        await db.SaveChangesAsync();
        (await Service(db,33).PostReversalAsync(reference,Post())).StatusCode.Should().Be(409);
        var retry = await Service(db,11).RequestReversalAsync(reference,Request(key));
        retry.StatusCode.Should().Be(200);
        retry.Data!.AlreadyProcessed.Should().BeTrue();
        (await Service(db,22).ReviewReversalAsync(reference,Review(false,"payment happened"))).StatusCode.Should().Be(200);
        journal.State.Should().Be(JournalState.Rejected);
        (await db.Journals.SingleAsync(x => x.Id == 1)).State.Should().Be(JournalState.Posted);
        (await Service(db,33).PostReversalAsync(reference,Post())).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Payment_loan_recovery_closed_period_and_invalid_date_are_rejected()
    {
        await using var db = Database();
        var reference = await SeedPostedRun(db);
        var request = Request(Guid.NewGuid());request.ReversalDate = Payday.AddDays(-1);
        (await Service(db,11).RequestReversalAsync(reference,request)).StatusCode.Should().Be(409);
        request = Request(Guid.NewGuid());request.AccountingPeriodId = 99;
        (await Service(db,11).RequestReversalAsync(reference,request)).StatusCode.Should().Be(409);
        (await db.AccountingPeriods.SingleAsync()).IsClosed = true;
        await db.SaveChangesAsync();
        (await Service(db,11).RequestReversalAsync(reference,Request(Guid.NewGuid()))).StatusCode.Should().Be(409);
        (await db.AccountingPeriods.SingleAsync()).IsClosed = false;
        db.LoanAdvanceRecoveries.Add(new LoanAdvanceRecovery {Id = 4,TenantId = 41,JournalId = 1,
            LoanAdvanceId = 3,ClientRequestId = Guid.NewGuid(),Amount = 1m});
        await db.SaveChangesAsync();
        (await Service(db,11).RequestReversalAsync(reference,Request(Guid.NewGuid()))).StatusCode.Should().Be(409);
        (await db.Journals.CountAsync()).Should().Be(1);

        await using var paymentDb = Database();
        var paymentRun = await SeedPostedRun(paymentDb);
        paymentDb.PayrollEmployees.Add(new PayrollEmployee {Id = 8,TenantId = 41,PayrollRunId = 1,EmployeeId = 10});
        paymentDb.PayrollPayments.Add(new PayrollPayment {Id = 9,TenantId = 41,PayrollEmployeeId = 8,
            ClientRequestId = Guid.NewGuid(),State = PaymentState.Successful,Amount = 10m});
        await paymentDb.SaveChangesAsync();
        (await Service(paymentDb,11).RequestReversalAsync(paymentRun,Request(Guid.NewGuid()))).StatusCode.Should().Be(409);
        (await paymentDb.Journals.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Other_tenant_and_non_admin_are_forbidden()
    {
        await using var db = Database(42);
        var reference = Guid.NewGuid();
        (await Service(db,11).RequestReversalAsync(reference,Request(Guid.NewGuid()))).StatusCode.Should().Be(403);
        (await Service(db,11).ReviewReversalAsync(reference,Review(true))).StatusCode.Should().Be(403);
        (await Service(db,11).PostReversalAsync(reference,Post())).StatusCode.Should().Be(403);
        await using var own = Database();
        (await Service(own,11,false).RequestReversalAsync(reference,Request(Guid.NewGuid()))).StatusCode.Should().Be(403);
    }

    private static RequestPayrollReversalDto Request(Guid key) => new()
    {
        ClientRequestId = key,AccountingPeriodId = 1,ReversalDate = Payday.AddDays(1),
        Reason = "Correct original payroll accrual in next open period",RowVersion = "AQ=="
    };
    private static ReviewPayrollReversalDto Review(bool approve,string? note=null) => new()
    {Approve = approve,Note = note,RowVersion = "Ag=="};
    private static PostPayrollReversalDto Post() => new() {RowVersion = "Ag=="};
    private static PayrollJournalPostingService Service(EduOSDbContext db,long userId,bool isAdmin=true)
        => new(db,new User(userId,isAdmin),TimeProvider.System);

    private static async Task<Guid> SeedPostedRun(EduOSDbContext db)
    {
        var reference = Guid.NewGuid();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = 1,TenantId = 41,PublicId = reference,RunNumber = "PAY-2026-10",State = PayrollRunState.Posted,
            PeriodStart = new DateOnly(2026,10,1),PeriodEnd = Payday,CampusId = 5,JournalId = 1,
            RowVersion = [1]
        });
        db.AccountingPeriods.Add(new AccountingPeriod {Id = 1,TenantId = 41,Name = "Oct 2026",
            StartDate = new DateOnly(2026,10,1),EndDate = new DateOnly(2026,10,31)});
        db.Journals.Add(new Journal {Id = 1,TenantId = 41,PublicId = Guid.NewGuid(),
            JournalNumber = "GL-2026-10",JournalDate = Payday,AccountingPeriodId = 1,
            CampusId = 5,CurrencyCode = "BDT",State = JournalState.Posted,
            SourceType = "PayrollRun",SourceId = 1,CreatedBy = 6});
        db.JournalLines.AddRange(
            new JournalLine {Id = 1,TenantId = 41,JournalId = 1,LineNo = 1,AccountId = 8,
                CampusId = 5,DebitAmount = 100m,Description = "Salary expense"},
            new JournalLine {Id = 2,TenantId = 41,JournalId = 1,LineNo = 2,AccountId = 9,
                CampusId = 5,CreditAmount = 100m,Description = "Salary payable"});
        await db.SaveChangesAsync();
        return reference;
    }
    private static EduOSDbContext Database(long contextTenant = 41)
    {
        var accessor = new HttpContextAccessor {HttpContext = new DefaultHttpContext()};
        accessor.HttpContext.Items["TenantId"] = contextTenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("payroll-reversal-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options,accessor);
    }
    private sealed class User(long id,bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Accountant"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
