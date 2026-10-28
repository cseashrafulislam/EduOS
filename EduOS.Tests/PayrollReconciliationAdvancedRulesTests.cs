using EduOS.Service.Services.HR;
using Xunit;

namespace EduOS.Tests;

public sealed class PayrollReconciliationAdvancedRulesTests
{
    [Theory]
    [InlineData(true, true, false, 1000, 1000, 0, "Paid")]
    [InlineData(true, true, false, 1000, 1100, 0, "Overpaid")]
    [InlineData(true, true, false, 1000, 400, 0, "Partial")]
    [InlineData(true, true, false, 1000, 0, 300, "PendingVerification")]
    [InlineData(true, true, false, 1000, 0, 0, "Outstanding")]
    [InlineData(true, true, false, 1000, 800, 300, "NeedsReview")]
    [InlineData(true, false, false, 1000, 1000, 0, "NeedsReview")]
    [InlineData(true, true, true, 1000, 1000, 0, "NeedsReview")]
    [InlineData(false, false, false, 1000, 0, 0, "NotPosted")]
    public void Settlement_is_accounting_conservative(
        bool posted, bool validJournal, bool invalidPayment,
        int net, int paid, int pending, string expected)
    {
        Assert.Equal(expected, PayrollReconciliationRules.Settlement(
            posted, validJournal, invalidPayment, net, paid, pending));
    }

    [Fact]
    public void Accrual_requires_posted_and_verified_balanced_journal()
    {
        Assert.Equal("Verified", PayrollReconciliationRules.Accrual(true, true));
        Assert.Equal("NeedsReview", PayrollReconciliationRules.Accrual(true, false));
        Assert.Equal("Unposted", PayrollReconciliationRules.Accrual(false, false));
    }
}
