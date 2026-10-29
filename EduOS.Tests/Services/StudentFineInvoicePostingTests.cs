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
