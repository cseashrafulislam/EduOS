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
    public void Verified_gateway_callback_updates_payment_and_invoice_atomically_before_activation()
    {
        var callback = Slice(ReadService(),
            "public async Task<ApiResponse<bool>> HandleAamarPayCallbackAsync",
            "public Task<ApiResponse<SubscriptionPaymentDto>> SubmitManualPaymentAsync");

        var verify = callback.IndexOf("await _gateway.VerifyTransactionAsync(callback.MerTxnid)", StringComparison.Ordinal);
        var transaction = callback.IndexOf("using var tx = SerializableScope();", StringComparison.Ordinal);
        var state = callback.IndexOf("payment.State = PaymentState.Successful;", StringComparison.Ordinal);
        var invoice = callback.IndexOf("invoice.PaidAmount += payment.Amount;", StringComparison.Ordinal);
        var paymentUpdate = callback.IndexOf("_payments.Update(payment);", StringComparison.Ordinal);
        var invoiceUpdate = callback.IndexOf("_invoices.Update(invoice);", StringComparison.Ordinal);
        var save = callback.IndexOf("await _uow.SaveChangesAsync();", StringComparison.Ordinal);
        var activation = callback.IndexOf("await ActivatePaidSubscriptionAsync(invoice);", StringComparison.Ordinal);
        var commit = callback.IndexOf("tx.Complete();", activation, StringComparison.Ordinal);

        verify.Should().BeGreaterThanOrEqualTo(0);
        transaction.Should().BeGreaterThan(verify);
        state.Should().BeGreaterThan(transaction);
        invoice.Should().BeGreaterThan(state);
        paymentUpdate.Should().BeGreaterThan(invoice);
        invoiceUpdate.Should().BeGreaterThan(paymentUpdate);
        save.Should().BeGreaterThan(invoiceUpdate);
        activation.Should().BeGreaterThan(save);
        commit.Should().BeGreaterThan(activation);
        callback.Should().Contain("payment.State == PaymentState.Successful");
        callback.Should().Contain("payment.Amount <= 0");
        callback.Should().Contain("catch (DbUpdateConcurrencyException)");
    }

    [Fact]
    public void Manual_payment_review_updates_payment_and_invoice_atomically_before_activation()
    {
        var verify = Slice(ReadService(),
            "public async Task<ApiResponse<bool>> VerifyManualPaymentAsync",
            "public async Task<ApiResponse<List<SubscriptionPaymentDto>>> GetByInvoiceAsync");
        var transaction = verify.IndexOf("using var tx = SerializableScope();", StringComparison.Ordinal);
        var stateGuard = verify.IndexOf("payment.State != PaymentState.AwaitingVerification", StringComparison.Ordinal);
        var fileCheck = verify.IndexOf("file?.IsVerifiedSafe != true", StringComparison.Ordinal);
        var paymentMutation = verify.IndexOf("payment.State = PaymentState.Successful;", StringComparison.Ordinal);
        var invoiceMutation = verify.IndexOf("invoice.PaidAmount += payment.Amount;", StringComparison.Ordinal);
        var paymentUpdate = verify.IndexOf("_payments.Update(payment);", StringComparison.Ordinal);
        var invoiceUpdate = verify.IndexOf("_invoices.Update(invoice);", StringComparison.Ordinal);
        var save = verify.IndexOf("await _uow.SaveChangesAsync();", StringComparison.Ordinal);
        var activation = verify.IndexOf("await ActivatePaidSubscriptionAsync(invoice);", StringComparison.Ordinal);
        var commit = verify.IndexOf("tx.Complete();", StringComparison.Ordinal);

        transaction.Should().BeGreaterThanOrEqualTo(0);
        stateGuard.Should().BeGreaterThan(transaction);
        fileCheck.Should().BeGreaterThan(stateGuard);
        paymentMutation.Should().BeGreaterThan(fileCheck);
        invoiceMutation.Should().BeGreaterThan(paymentMutation);
        paymentUpdate.Should().BeGreaterThan(invoiceMutation);
        invoiceUpdate.Should().BeGreaterThan(paymentUpdate);
        save.Should().BeGreaterThan(invoiceUpdate);
        activation.Should().BeGreaterThan(save);
        commit.Should().BeGreaterThan(activation);
        verify.Should().Contain("if (!_user.IsSuperAdmin)");
        verify.Should().Contain("catch (DbUpdateConcurrencyException)");
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
