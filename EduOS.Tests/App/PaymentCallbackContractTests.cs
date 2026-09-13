using FluentAssertions;
using Xunit;

namespace EduOS.Tests.App;

public class PaymentCallbackContractTests
{
    [Fact]
    public void Anonymous_failure_and_cancel_callbacks_do_not_mutate_payment_state()
    {
        var controller = ReadController();
        var fail = Slice(controller, "public IActionResult FailCallback", "public IActionResult CancelCallback");
        var cancel = Slice(controller, "public IActionResult CancelCallback", "public async Task<IActionResult> IpnCallback");

        fail.Should().NotContain("HandleAamarPayCallbackAsync");
        cancel.Should().NotContain("HandleAamarPayCallbackAsync");
        fail.Should().Contain("PaymentFailed");
        cancel.Should().Contain("PaymentCancelled");
    }

    [Fact]
    public void Anonymous_ipn_only_processes_success_that_service_verifies_with_gateway()
    {
        var controller = ReadController();
        var ipn = Slice(controller, "public async Task<IActionResult> IpnCallback", "// MANUAL PAYMENT");

        ipn.Should().Contain("string.Equals(dto.PayStatus, \"Successful\", StringComparison.OrdinalIgnoreCase)");
        ipn.Should().Contain("HandleAamarPayCallbackAsync(dto)");
        ipn.Should().Contain("Non-success notification acknowledged");
    }

    private static string ReadController()
    {
        return File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "SubscriptionPaymentController.cs"));
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }
}
