using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Service.Services.HR;
using EduOS.Service.Services.Portals;
using Xunit;

namespace EduOS.Tests;

public sealed class EmployeeVerifiedIdentityBoundaryTests
{
    [Fact]
    public void HR_creation_self_service_and_payslips_require_authoritative_user_person_mapping()
    {
        foreach (var service in new[] { typeof(HrAdminService), typeof(EmployeeSelfServiceService),
                     typeof(HrPayrollService) })
        {
            var constructor=Assert.Single(service.GetConstructors());
            Assert.Contains(constructor.GetParameters(),
                p=>p.ParameterType==typeof(IGenericRepository<ApplicationUser>));
        }
    }
}
