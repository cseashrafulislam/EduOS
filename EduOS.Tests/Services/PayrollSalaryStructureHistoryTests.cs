using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Service.Services.HR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollSalaryStructureHistoryTests
{
    [Fact]
    public async Task Tenant_admin_reads_effective_dated_history_in_descending_order()
    {
        await using var db=Database();
        var reference=Guid.NewGuid();
        Seed(db,reference);
        var response=await Service(db,"TenantAdmin").GetSalaryStructuresAsync(reference,1,10);
        Assert.Equal(200,response.StatusCode);
        Assert.NotNull(response.Data);
        Assert.Equal(2,response.Data!.TotalCount);
        Assert.Equal(2,response.Data.Items.Count);
        Assert.Equal(new DateOnly(2026,7,1),response.Data.Items[0].EffectiveFrom);
        Assert.True(response.Data.Items[0].IsCurrent);
        Assert.Equal(1500m,response.Data.Items[0].GrossMonthlyAmount);
        Assert.Equal(2,response.Data.Items[0].Lines.Count);
        Assert.Equal(new DateOnly(2026,1,1),response.Data.Items[1].EffectiveFrom);
    }

    [Fact]
    public async Task Campus_limited_hr_role_cannot_read_institution_wide_salary_history()
    {
        await using var db=Database();
        var reference=Guid.NewGuid();
        Seed(db,reference);
        var response=await Service(db,"HR").GetSalaryStructuresAsync(reference,1,10);
        Assert.Equal(403,response.StatusCode);
    }

    [Theory]
    [InlineData(0,10)]
    [InlineData(1,0)]
    [InlineData(1,51)]
    public async Task History_rejects_invalid_pagination(int page,int size)
    {
        await using var db=Database();
        var response=await Service(db,"TenantAdmin").GetSalaryStructuresAsync(Guid.NewGuid(),page,size);
        Assert.Equal(400,response.StatusCode);
    }

    [Fact]
    public async Task Salary_history_rejects_missing_component_instead_of_partial_reporting()
    {
        await using var db=Database();
        var reference=Guid.NewGuid();
        Seed(db,reference);
        foreach (var component in db.SalaryComponents) component.IsDeleted = true;
        await db.SaveChangesAsync();
        var response=await Service(db,"TenantAdmin").GetSalaryStructuresAsync(reference,1,10);
        Assert.Equal(409,response.StatusCode);
    }

    private static void Seed(EduOSDbContext db,Guid reference)
    {
        db.Employees.Add(new Employee
        {
            Id=7,TenantId=41,PublicId=reference,EmployeeCode="E-7",
            FullName="Payroll Employee",JoiningDate=new DateOnly(2025,1,1)
        });
        db.SalaryComponents.AddRange(
            new SalaryComponent { Id=1,TenantId=41,Code="BASIC",Name="Basic",Type=SalaryComponentType.Earning },
            new SalaryComponent { Id=2,TenantId=41,Code="TAX",Name="Tax",Type=SalaryComponentType.Deduction });
        db.SalaryStructures.AddRange(
            new SalaryStructure { Id=10,TenantId=41,EmployeeId=7,EffectiveFrom=new DateOnly(2026,1,1),EffectiveTo=new DateOnly(2026,6,30),IsCurrent=false },
            new SalaryStructure { Id=11,TenantId=41,EmployeeId=7,EffectiveFrom=new DateOnly(2026,7,1),IsCurrent=true });
        db.SalaryStructureLines.AddRange(
            new SalaryStructureLine { Id=1,TenantId=41,SalaryStructureId=10,SalaryComponentId=1,Amount=1200m },
            new SalaryStructureLine { Id=2,TenantId=41,SalaryStructureId=11,SalaryComponentId=1,Amount=1500m },
            new SalaryStructureLine { Id=3,TenantId=41,SalaryStructureId=11,SalaryComponentId=2,Amount=100m });
        db.SaveChanges();
    }

    private static HrPayrollService Service(EduOSDbContext db,string role)
    {
        var employees=new Mock<IGenericRepository<Employee>>();
        var components=new Mock<IGenericRepository<SalaryComponent>>();
        var structures=new Mock<IGenericRepository<SalaryStructure>>();
        var lines=new Mock<IGenericRepository<SalaryStructureLine>>();
        employees.Setup(x=>x.GetQueryable()).Returns(db.Employees);
        components.Setup(x=>x.GetQueryable()).Returns(db.SalaryComponents);
        structures.Setup(x=>x.GetQueryable()).Returns(db.SalaryStructures);
        lines.Setup(x=>x.GetQueryable()).Returns(db.SalaryStructureLines);
        return new HrPayrollService(
            employees.Object,components.Object,structures.Object,lines.Object,
            Mock.Of<IGenericRepository<PayrollRun>>(),
            Mock.Of<IGenericRepository<PayrollEmployee>>(),
            Mock.Of<IGenericRepository<PayrollLine>>(),
            Mock.Of<IGenericRepository<PayrollPayment>>(),
            Mock.Of<IGenericRepository<Bonus>>(),
            Mock.Of<IGenericRepository<LoanAdvance>>(),
            Mock.Of<IGenericRepository<LoanAdvanceRecovery>>(),
            Mock.Of<IGenericRepository<EduOS.Core.Entities.Accounting.BankAccount>>(),
            Mock.Of<IGenericRepository<EduOS.Core.Entities.Accounting.Journal>>(),
            Mock.Of<IGenericRepository<EmployeeBankAccount>>(),
            Mock.Of<IUnitOfWork>(),new TestUser(role),TimeProvider.System,
            NullLogger<HrPayrollService>.Instance);
    }

    private static EduOSDbContext Database()
    {
        var accessor=new HttpContextAccessor { HttpContext=new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"]=41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("salary-history-"+Guid.NewGuid().ToString("N"))
            .Options,accessor);
    }

    private sealed class TestUser(string role) : ICurrentUserService
    {
        public bool IsAuthenticated=>true;
        public long UserId=>9;
        public long TenantId=>41;
        public string? FullName=>null;
        public string? Email=>null;
        public bool IsSuperAdmin=>false;
        public bool IsTenantAdmin=>role=="TenantAdmin";
        public IReadOnlyList<string> Roles=>[role];
        public bool IsInRole(string value)=>value==role;
        public string? IpAddress=>null;
        public string? UserAgent=>null;
    }
}
