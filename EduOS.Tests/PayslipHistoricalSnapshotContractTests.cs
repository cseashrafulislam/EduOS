using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Enums.Domain;
using Xunit;

namespace EduOS.Tests;

public sealed class PayslipHistoricalSnapshotContractTests
{
    [Fact]
    public void Employee_payslip_contains_payroll_period_and_posting_state_not_only_current_person_data()
    {
        var dto = new PayrollEmployeeDto();
        Assert.Equal(Guid.Empty, dto.RunReference);
        Assert.Equal(default, dto.PeriodStart);
        Assert.Equal(default, dto.PeriodEnd);
        Assert.Equal(PayrollRunState.Draft, dto.RunState);
        Assert.Empty(dto.RunNumber);
    }
}
