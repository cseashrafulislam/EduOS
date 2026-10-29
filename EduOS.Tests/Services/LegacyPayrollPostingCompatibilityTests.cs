using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LegacyPayrollPostingCompatibilityTests
{
    [Fact]
    public async Task Legacy_route_service_uses_same_mapped_journal_posting_operation()
    {
        await using var db = Database();
        var run = await Seed(db);
        PostPayrollJournalRequestDto? forwarded = null;
        var ledger = new Mock<IPayrollJournalPostingService>();
        ledger.Setup(x => x.PostAsync(run.PublicId, It.IsAny<PostPayrollJournalRequestDto>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, PostPayrollJournalRequestDto, CancellationToken>((_, request, _) => forwarded = request)
            .ReturnsAsync(ApiResponse<PostPayrollJournalResultDto>.SuccessResponse(new PostPayrollJournalResultDto
            { PayrollReference = run.PublicId, JournalReference = Guid.NewGuid() }));
        var legacy = Service(db, "TenantAdmin", ledger.Object);
        var request = ValidRequest();
        var result = await legacy.PostPayrollRunAsync(run.PublicId, request);
        result.StatusCode.Should().Be(200);
        forwarded.Should().NotBeNull();
        forwarded!.AccountingPeriodId.Should().Be(request.AccountingPeriodId);
        forwarded.SalaryPayableAccountId.Should().Be(request.SalaryPayableAccountId);
        forwarded.RowVersion.Should().Be(request.RowVersion);
        forwarded.Mappings.Should().BeSameAs(request.Mappings);
        result.Data!.Reference.Should().Be(run.PublicId);
        ledger.Verify(x => x.PostAsync(run.PublicId, It.IsAny<PostPayrollJournalRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Legacy_route_propagates_canonical_financial_conflicts_and_preserves_state()
    {
        await using var db = Database();
        var run = await Seed(db);
        var ledger = new Mock<IPayrollJournalPostingService>();
        ledger.Setup(x => x.PostAsync(run.PublicId, It.IsAny<PostPayrollJournalRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PostPayrollJournalResultDto>.ErrorResponse("Accounting period is closed.", 409));
        var result = await Service(db, "TenantAdmin", ledger.Object).PostPayrollRunAsync(run.PublicId, ValidRequest());
        result.StatusCode.Should().Be(409);
        result.Message.Should().Be("Accounting period is closed.");
        (await db.PayrollRuns.SingleAsync()).State.Should().Be(PayrollRunState.Approved);
    }

    [Fact]
    public async Task Legacy_route_cannot_post_without_mappings_or_tenant_admin_permission()
    {
        await using var db = Database();
        var run = await Seed(db);
        var ledger = new Mock<IPayrollJournalPostingService>();
        var request = ValidRequest();
        request.AccountingPeriodId = 0;
        (await Service(db, "TenantAdmin", ledger.Object).PostPayrollRunAsync(run.PublicId, request))
            .StatusCode.Should().Be(400);
        (await Service(db, "Accountant", ledger.Object).PostPayrollRunAsync(run.PublicId, ValidRequest()))
            .StatusCode.Should().Be(403);
        ledger.Verify(x => x.PostAsync(It.IsAny<Guid>(), It.IsAny<PostPayrollJournalRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PostPayrollRunRequestDto ValidRequest() => new()
    {
        RowVersion = "AQ==", AccountingPeriodId = 3, SalaryPayableAccountId = 4,
        Mappings = [new PayrollComponentAccountMappingDto { SalaryComponentId = 1, AccountId = 2 }]
    };

    private static async Task<PayrollRun> Seed(EduOSDbContext db)
    {
        var run = new PayrollRun { Id = 1, TenantId = 41, PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(), RunNumber = "PAY-OCT", Year = 2026, Month = 10,
            PeriodStart = new DateOnly(2026, 10, 1), PeriodEnd = new DateOnly(2026, 10, 31),
            State = PayrollRunState.Approved, RowVersion = [1] };
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    private static HrPayrollService Service(EduOSDbContext db, string role, IPayrollJournalPostingService ledger)
    {
        var runs = new Mock<IGenericRepository<PayrollRun>>();
        runs.Setup(x => x.GetQueryable()).Returns(db.PayrollRuns);
        var people = new Mock<IGenericRepository<PayrollEmployee>>();
        people.Setup(x => x.GetQueryable()).Returns(db.PayrollEmployees);
        return new HrPayrollService(
            Mock.Of<IGenericRepository<Employee>>(),
            Mock.Of<IGenericRepository<ApplicationUser>>(),
            Mock.Of<IGenericRepository<Campus>>(),
            Mock.Of<IGenericRepository<EmployeeCampusAssignment>>(),
            Mock.Of<IGenericRepository<SalaryComponent>>(),
            Mock.Of<IGenericRepository<SalaryStructure>>(),
            Mock.Of<IGenericRepository<SalaryStructureLine>>(),
            runs.Object, people.Object,
            Mock.Of<IGenericRepository<PayrollLine>>(),
            Mock.Of<IGenericRepository<PayrollPayment>>(),
            Mock.Of<IGenericRepository<Bonus>>(),
            Mock.Of<IGenericRepository<LoanAdvance>>(),
            Mock.Of<IGenericRepository<LoanAdvanceRecovery>>(),
            Mock.Of<IGenericRepository<BankAccount>>(),
            Mock.Of<IGenericRepository<Journal>>(),
            Mock.Of<IGenericRepository<EmployeeBankAccount>>(),
            Mock.Of<IUnitOfWork>(), new TestUser(role), TimeProvider.System,
            NullLogger<HrPayrollService>.Instance, ledger);
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("payroll-legacy-post-" + Guid.NewGuid().ToString("N"))
            .Options, accessor);
    }

    private sealed class TestUser(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
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
