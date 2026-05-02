using EduOS.Core.Enums;

namespace EduOS.Core.DTOs.SaaS
{
    public class SubscriptionInvoiceDto
    {
        public long Id { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        public decimal Subtotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal DueAmount { get; set; }

        public string Currency { get; set; } = "BDT";
        public PaymentStatus PaymentStatus { get; set; }
        public DateTime? PaidAt { get; set; }

        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }
        public string? CustomerAddress { get; set; }

        public string? Description { get; set; }

        public string PlanName { get; set; } = string.Empty;
    }

    public class SubscriptionPaymentDto
    {
        public long Id { get; set; }
        public long InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;

        public string TransactionId { get; set; } = string.Empty;
        public string? GatewayTransactionId { get; set; }

        public PaymentMethod PaymentMethod { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "BDT";
        public PaymentStatus Status { get; set; }

        public DateTime InitiatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        // Manual payment fields
        public string? PayerBankName { get; set; }
        public string? PayerAccountNumber { get; set; }
        public string? DepositSlipNumber { get; set; }
        public DateTime? DepositDate { get; set; }
        public string? DepositSlipUrl { get; set; }
        public string? VerificationNote { get; set; }
        public DateTime? VerifiedAt { get; set; }

        public string? FailureReason { get; set; }
    }

    /// <summary>
    /// Initiate online payment (AamarPay)
    /// </summary>
    public class InitiatePaymentRequestDto
    {
        public int InvoiceId { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
    }

    public class InitiatePaymentResponseDto
    {
        public string TransactionId { get; set; } = string.Empty;
        public string? PaymentUrl { get; set; }
        public PaymentStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Submit manual bank transfer details
    /// </summary>
    public class ManualPaymentSubmitDto
    {
        public long InvoiceId { get; set; }
        public string PayerBankName { get; set; } = string.Empty;
        public string PayerAccountNumber { get; set; } = string.Empty;
        public string DepositSlipNumber { get; set; } = string.Empty;
        public DateTime DepositDate { get; set; }
        public decimal Amount { get; set; }
        public IFormFileLite? DepositSlip { get; set; } // file upload (handled in controller)
        public string? Note { get; set; }
    }

    /// <summary>
    /// Stub interface so DTOs can reference IFormFile without Microsoft.AspNetCore reference
    /// (actual upload handled in controller using IFormFile)
    /// </summary>
    public interface IFormFileLite
    {
        string FileName { get; }
        long Length { get; }
    }

    /// <summary>
    /// Admin verifies/rejects a manual payment
    /// </summary>
    public class VerifyManualPaymentDto
    {
        public long PaymentId { get; set; }
        public bool Approve { get; set; }
        public string? VerificationNote { get; set; }
    }

    /// <summary>
    /// AamarPay IPN/callback payload
    /// </summary>
    public class AamarPayCallbackDto
    {
        public string? PgTxnid { get; set; }       // Gateway transaction ID
        public string? MerTxnid { get; set; }      // Our transaction ID (we sent)
        public string? PayStatus { get; set; }     // Successful, Failed, Cancelled
        public string? Amount { get; set; }
        public string? CardType { get; set; }
        public string? PayTime { get; set; }
        public string? StoreId { get; set; }
        public string? StoreAmount { get; set; }
        public string? Currency { get; set; }
        public string? BankTxnid { get; set; }
        public string? RiskLevel { get; set; }
        public string? RiskTitle { get; set; }
    }
}
