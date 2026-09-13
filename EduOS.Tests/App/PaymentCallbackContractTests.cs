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

    [Fact]
    public void Successful_gateway_callback_claims_payment_before_invoice_side_effects()
    {
        var service = ReadService();
        var callback = Slice(service,
            "public async Task<ApiResponse<bool>> HandleAamarPayCallbackAsync",
            "// SUBMIT MANUAL PAYMENT");
        var successful = Slice(callback,
            "payment.Status = PaymentStatus.Successful;",
            "else\n                {");

        var save = successful.IndexOf("await _unitOfWork.SaveChangesAsync();", StringComparison.Ordinal);
        var invoice = successful.IndexOf("// Update invoice", StringComparison.Ordinal);

        save.Should().BeGreaterThanOrEqualTo(0);
        invoice.Should().BeGreaterThan(save);
        callback.Should().Contain("catch (DbUpdateConcurrencyException ex)");
        callback.Should().Contain("Already processed");
    }

    [Fact]
    public void Manual_approval_claims_review_before_subscription_activation_side_effects()
    {
        var service = ReadService();
        var verify = Slice(service,
            "public async Task<ApiResponse<bool>> VerifyManualPaymentAsync",
            "// GET PAYMENTS BY INVOICE");
        var approve = Slice(verify,
            "if (dto.Approve)",
            "else\n                {");

        var save = approve.IndexOf("await _unitOfWork.SaveChangesAsync();", StringComparison.Ordinal);
        var invoiceMutation = approve.IndexOf("if (invoice != null)", StringComparison.Ordinal);

        save.Should().BeGreaterThanOrEqualTo(0);
        invoiceMutation.Should().BeGreaterThan(save);
        verify.Should().Contain("catch (DbUpdateConcurrencyException ex)");
        verify.Should().Contain("Payment was already reviewed");
    }

    private static string ReadController()
    {
        return File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "SubscriptionPaymentController.cs"));
    }

    private static string ReadService()
    {
        return File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "SubscriptionPaymentService.cs"));
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
