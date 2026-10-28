using EduOS.Core.Entities.Accounting;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Service.Services.HR;
using Xunit;

namespace EduOS.Tests;

public sealed class PayrollLoanDisbursementDependencyTests
{
    [Fact]
    public void Payroll_calculation_explicitly_depends_on_journal_repository_for_loan_reconciliation()
    {
        var constructor = Assert.Single(typeof(HrPayrollService).GetConstructors());
        Assert.Contains(constructor.GetParameters(),
            p => p.ParameterType == typeof(IGenericRepository<Journal>));
    }
}
