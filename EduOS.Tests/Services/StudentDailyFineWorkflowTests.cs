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

public sealed class StudentDailyFineWorkflowTests
{
    [Fact]
    public async Task Daily_fine_assesses_immutable_due_date_and_rate_once_per_source_rule()
    {
        await using var db = Database();
        var source = await SeedAsync(db);
        var service = new FinanceAdjustmentService(db, new User(10), TimeProvider.System);
        var eligible = await service.GetFineSourceInvoicesAsync(db.StudentEnrollments.Single().PublicId);
        eligible.Success.Should().BeTrue();
        eligible.Data.Should().ContainSingle().Which.Reference.Should().Be(source.PublicId);

        var request = Request(db, source);
        var first = await service.ApplyFineAsync(request);
        first.Success.Should().BeTrue(first.Message);
        first.Data!.Amount.Should().Be(62.50m);
        first.Data.ChargedOverdueDays.Should().Be(5);
        first.Data.DailyRateSnapshot.Should().Be(12.50m);
        first.Data.SourceInvoiceDueDate.Should().Be(source.DueDate);
        first.Data.SourceInvoiceReference.Should().Be(source.PublicId);
        (await service.ApplyFineAsync(request)).Data!.Id.Should().Be(first.Data.Id);
        var duplicate = Request(db, source);
        (await service.ApplyFineAsync(duplicate)).StatusCode.Should().Be(409);
        db.StudentFines.Should().ContainSingle();
        source.DueAmount.Should().Be(100m);
        source.TotalAmount.Should().Be(100m);

        var originalRule = db.FineRules.Single();
        originalRule.RowVersion = new byte[] { 7, 8, 9 };
        var mutable = new SaveFineRuleRequestDto { Name = originalRule.Name, Amount = originalRule.Amount,
            IsDaily = true, FeeHeadId = originalRule.FeeHeadId, GraceDays = 3,
            RowVersion = Convert.ToBase64String(originalRule.RowVersion) };
        (await service.SaveFineRuleAsync(originalRule.Id, mutable)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Fine_assessment_rejects_early_future_paid_foreign_and_reversed_source()
    {
        await using var db = Database();
        var source = await SeedAsync(db);
        var service = new FinanceAdjustmentService(db, new User(10), TimeProvider.System);
        var early = Request(db, source);early.FineDate = source.DueDate.AddDays(2);
        (await service.ApplyFineAsync(early)).StatusCode.Should().Be(409);
        var future = Request(db, source);future.FineDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        (await service.ApplyFineAsync(future)).StatusCode.Should().Be(400);
        var foreign = Request(db, source);foreign.SourceInvoiceReference = Guid.NewGuid();
        (await service.ApplyFineAsync(foreign)).StatusCode.Should().Be(404);
        source.State = InvoiceState.Paid;source.PaidAmount = 100m;source.DueAmount = 0m;
        (await service.ApplyFineAsync(Request(db, source))).StatusCode.Should().Be(409);
        source.State = InvoiceState.Issued;source.PaidAmount = 0m;source.DueAmount = 100m;
        db.Entry(source).State = EntityState.Unchanged;
        db.Journals.Add(new Journal { TenantId = 11, JournalNumber = "REV-SOURCE", JournalDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AccountingPeriodId = db.AccountingPeriods.Single().Id, State = JournalState.Posted,
            ReversalOfJournalId = source.JournalId });
        await TestTenantSeeder.SaveAsync(db);
        (await service.ApplyFineAsync(Request(db, source))).StatusCode.Should().Be(409);
        db.StudentFines.Should().BeEmpty();
    }

    [Fact]
    public async Task Separate_administrator_posts_daily_fine_as_its_own_balanced_document()
    {
        await using var db = Database();
        var source = await SeedAsync(db);
        var assessor = new FinanceAdjustmentService(db, new User(10), TimeProvider.System);
        var assessed = await assessor.ApplyFineAsync(Request(db, source));
        assessed.Success.Should().BeTrue();
        var fine = db.StudentFines.Single();
        fine.CreatedBy = 10;
        // EF InMemory does not generate SQL Server rowversion values.
        fine.RowVersion = new byte[] { 1, 2, 3 };
        var post = new PostStudentFineInvoiceRequestDto { AccountingPeriodId = db.AccountingPeriods.Single().Id,
            RowVersion = Convert.ToBase64String(fine.RowVersion) };
        (await assessor.PostFineInvoiceAsync(fine.Id, post)).StatusCode.Should().Be(403);
        var poster = new FinanceAdjustmentService(db, new User(20), TimeProvider.System);
        var result = await poster.PostFineInvoiceAsync(fine.Id, post);
        result.Success.Should().BeTrue(result.Message);
        result.Data!.Amount.Should().Be(62.50m);
        (await poster.PostFineInvoiceAsync(fine.Id, post)).Data!.AlreadyPosted.Should().BeTrue();
        db.StudentInvoices.Should().HaveCount(2);
        db.StudentInvoiceLines.Should().ContainSingle(x => x.StudentFineId == fine.Id);
        db.Journals.Should().HaveCount(2);
        var posted = db.Journals.Single(x => x.SourceId != source.Id && x.SourceType == "StudentInvoice");
        db.JournalLines.Where(x => x.JournalId == posted.Id).Sum(x => x.DebitAmount).Should().Be(62.50m);
        db.JournalLines.Where(x => x.JournalId == posted.Id).Sum(x => x.CreditAmount).Should().Be(62.50m);
        var register = await poster.GetStudentRegisterAsync(db.StudentEnrollments.Single().PublicId);
        register.Data!.Fines.Single().SourceInvoiceReference.Should().Be(source.PublicId);
        register.Data.Fines.Single().BilledInvoiceReference.Should().Be(result.Data.InvoiceReference);
        source.DueAmount.Should().Be(100m);
    }

    [Fact]
    public async Task Tenant_and_module_boundary_stays_server_enforced_for_daily_fine_sources()
    {
        await using var db = Database();
        var source = await SeedAsync(db);
        var reference = db.StudentEnrollments.Single().PublicId;
        (await new FinanceAdjustmentService(db, new User(10, admin:false), TimeProvider.System)
            .GetFineSourceInvoicesAsync(reference)).StatusCode.Should().Be(403);
        (await new FinanceAdjustmentService(db, new User(10, tenant:12), TimeProvider.System)
            .ApplyFineAsync(Request(db, source))).StatusCode.Should().Be(403);
    }

    private static ApplyStudentFineRequestDto Request(EduOSDbContext db, StudentInvoice source) => new()
    {
        ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = db.StudentEnrollments.Single().PublicId,
        FineRuleId = db.FineRules.Single().Id, FineDate = DateOnly.FromDateTime(DateTime.UtcNow),
        SourceInvoiceReference = source.PublicId, Reason = "Verified overdue tuition"
    };

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 11L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("daily-fine-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
    }

    private static async Task<StudentInvoice> SeedAsync(EduOSDbContext db)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var campus = new Campus { TenantId = 11, Code = "C1", Name = "Campus" };
        var student = new Student { TenantId = 11, StudentCode = "LATE-01", FullName = "Learner" };
        var ar = new Account { TenantId = 11, Code = "AR-DAILY", Name = "Receivables", AccountType = AccountType.Asset };
        var revenue = new Account { TenantId = 11, Code = "FIN-DAILY", Name = "Fine revenue", AccountType = AccountType.Income };
        var period = new AccountingPeriod { TenantId = 11, Name = "Fine period", StartDate = today.AddDays(-30), EndDate = today.AddDays(5) };
        db.AddRange(campus, student, ar, revenue, period);
        await TestTenantSeeder.SaveAsync(db);
        var enrollment = new StudentEnrollment { TenantId = 11, CampusId = campus.Id, StudentId = student.Id, RollNo = "1" };
        var head = new FeeHead { TenantId = 11, Code = "DAILY-FINE", Name = "Daily fine",
            IncomeAccountId = revenue.Id, ReceivableAccountId = ar.Id };
        db.AddRange(enrollment, head);
        await TestTenantSeeder.SaveAsync(db);
        db.FineRules.Add(new FineRule { TenantId = 11, Name = "Per-day late fee", FeeHeadId = head.Id,
            Amount = 12.50m, GraceDays = 2, IsDaily = true });
        var invoice = new StudentInvoice { TenantId = 11, StudentEnrollmentId = enrollment.Id,
            InvoiceNumber = "INV-OVERDUE", InvoiceDate = today.AddDays(-25), DueDate = today.AddDays(-7),
            TotalAmount = 100m, Subtotal = 100m, DueAmount = 100m, CurrencyCode = "BDT", State = InvoiceState.Issued,
            ClientRequestId = Guid.NewGuid() };
        db.StudentInvoices.Add(invoice);
        await TestTenantSeeder.SaveAsync(db);
        var journal = new Journal { TenantId = 11, JournalNumber = "JRN-OVERDUE", JournalDate = invoice.InvoiceDate,
            AccountingPeriodId = period.Id, CampusId = campus.Id, State = JournalState.Posted,
            SourceType = "StudentInvoice", SourceId = invoice.Id, PostedByUserId = 20, ApprovedByUserId = 20 };
        db.Journals.Add(journal);
        await TestTenantSeeder.SaveAsync(db);
        db.JournalLines.AddRange(
            new JournalLine { TenantId = 11, JournalId = journal.Id, LineNo = 1, AccountId = ar.Id,
                CampusId = campus.Id, StudentId = student.Id, DebitAmount = 100m },
            new JournalLine { TenantId = 11, JournalId = journal.Id, LineNo = 2, AccountId = revenue.Id,
                CampusId = campus.Id, StudentId = student.Id, CreditAmount = 100m });
        invoice.JournalId = journal.Id;
        await TestTenantSeeder.SaveAsync(db);
        return invoice;
    }

    private sealed class User(long userId, long tenant = 11, bool admin = true) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenant;
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
