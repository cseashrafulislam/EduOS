using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class InstitutionBudgetReportingTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    [Fact]
    public async Task Approved_budget_uses_posted_GL_actuals_including_unbudgeted_expense_accounts()
    {
        await using var db = Db();
        db.InstitutionBudgets.Add(new InstitutionBudget { Id=9, TenantId=41, PublicId=Guid.NewGuid(),
            ClientRequestId=Guid.NewGuid(), Name="Annual plan", StartDate=From, EndDate=To,
            StateCode="Approved", SubmittedByUserId=5, ReviewedByUserId=8,
            SubmittedAtUtc=DateTime.UtcNow, ReviewedAtUtc=DateTime.UtcNow, ReviewRequestId=Guid.NewGuid() });
        db.InstitutionBudgetLines.AddRange(
            Line(1, 9, 101, "400", "Fee income", AccountType.Income, 200m),
            Line(2, 9, 102, "500", "Payroll", AccountType.Expense, 100m));
        await db.SaveChangesAsync();
        var financials = new IncomeStatementDto { FromDate=From, ToDate=To,
            IncomeRows = [new IncomeStatementRowDto { AccountId=101, AccountCode="400", AccountName="Fee income", Amount=180m }],
            ExpenseRows = [new IncomeStatementRowDto { AccountId=102, AccountCode="500", AccountName="Payroll", Amount=120m },
                new IncomeStatementRowDto { AccountId=103, AccountCode="510", AccountName="Maintenance", Amount=35m }],
            TotalIncome=180m, TotalExpenses=155m, NetSurplus=25m };
        var reporting = new Mock<IAccountingReportingService>();
        reporting.Setup(x => x.GetIncomeStatementAsync(From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<IncomeStatementDto>.SuccessResponse(financials));
        var result = await Service(db, reporting.Object).ActualAsync(9);
        result.StatusCode.Should().Be(200);
        result.Data!.PlannedIncomeBdt.Should().Be(200m);
        result.Data.ActualIncomeBdt.Should().Be(180m);
        result.Data.PlannedExpenseBdt.Should().Be(100m);
        result.Data.ActualExpenseBdt.Should().Be(155m);
        result.Data.Lines.Single(x => x.AccountId == 101).VarianceBdt.Should().Be(-20m);
        result.Data.Lines.Single(x => x.AccountId == 102).VarianceBdt.Should().Be(-20m);
        result.Data.Lines.Single(x => x.AccountId == 103).Should().Match<InstitutionBudgetLineReportDto>(x =>
            x.IsUnbudgeted && x.PlannedAmountBdt == 0m && x.ActualAmountBdt == 35m && x.VarianceBdt == -35m);
    }

    [Fact]
    public async Task Pending_budget_and_non_admin_cannot_read_actuals()
    {
        await using var db = Db();
        db.InstitutionBudgets.Add(new InstitutionBudget { Id=1,TenantId=41,
            ClientRequestId=Guid.NewGuid(), Name="Draft proposal", StartDate=From, EndDate=To,
            StateCode="Submitted", SubmittedByUserId=5, SubmittedAtUtc=DateTime.UtcNow });
        await db.SaveChangesAsync();
        var report = new Mock<IAccountingReportingService>();
        (await Service(db, report.Object).ActualAsync(1)).StatusCode.Should().Be(409);
        (await Service(db, report.Object, false).ActualAsync(1)).StatusCode.Should().Be(403);
        (await Service(db, report.Object).GetAsync(777)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Another_tenants_budget_is_not_exposed_by_list_or_direct_id()
    {
        var store = "budget-isolation-" + Guid.NewGuid().ToString("N");
        await using (var other = Db(store, 42))
        {
            other.InstitutionBudgets.Add(new InstitutionBudget { Id=7,TenantId=42,
                ClientRequestId=Guid.NewGuid(),Name="Private",StartDate=From,EndDate=To,
                StateCode="Submitted",SubmittedByUserId=5,SubmittedAtUtc=DateTime.UtcNow });
            await other.SaveChangesAsync();
        }
        await using var db = Db(store, 41);
        var service = Service(db, new Mock<IAccountingReportingService>().Object);
        (await service.GetAsync(7)).StatusCode.Should().Be(404);
        (await service.ListAsync(1, 25)).Data!.TotalCount.Should().Be(0);
    }

    private static InstitutionBudgetLine Line(long id, long budget, long account, string code,
        string name, AccountType type, decimal amount) => new() { Id=id,TenantId=41,
            InstitutionBudgetId=budget,AccountId=account,AccountCodeSnapshot=code,
            AccountNameSnapshot=name,AccountTypeSnapshot=type,PlannedAmountBdt=amount };

    private static InstitutionBudgetService Service(EduOSDbContext db, IAccountingReportingService reporting,
        bool admin=true) => new(db, new TestUser(admin), reporting, TimeProvider.System);

    private static EduOSDbContext Db(string? store = null, long tenant = 41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(store ?? "budget-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class TestUser(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 8;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Principal"];
        public bool IsInRole(string role) => admin ? role == "TenantAdmin" : role == "Principal";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
