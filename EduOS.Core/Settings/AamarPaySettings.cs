namespace EduOS.Core.Settings
{
    /// <summary>
    /// AamarPay gateway configuration - bound from appsettings.json
    /// </summary>
    public class AamarPaySettings
    {
        public const string SectionName = "Payments:AamarPay";

        /// <summary>
        /// Sandbox or Live mode
        /// </summary>
        public bool IsSandbox { get; set; } = true;

        /// <summary>
        /// Your AamarPay store ID (different for sandbox vs live)
        /// </summary>
        public string StoreId { get; set; } = string.Empty;

        /// <summary>
        /// Signature key from AamarPay merchant panel
        /// </summary>
        public string SignatureKey { get; set; } = string.Empty;

        /// <summary>
        /// Sandbox URL: https://sandbox.aamarpay.com/jsonpost.php
        /// Live URL: https://secure.aamarpay.com/jsonpost.php
        /// </summary>
        public string PaymentUrl { get; set; } = "https://sandbox.aamarpay.com/jsonpost.php";

        /// <summary>
        /// URL to verify a transaction status
        /// Sandbox: https://sandbox.aamarpay.com/api/v1/trxcheck/request.php
        /// Live: https://secure.aamarpay.com/api/v1/trxcheck/request.php
        /// </summary>
        public string VerifyUrl { get; set; } = "https://sandbox.aamarpay.com/api/v1/trxcheck/request.php";

        /// <summary>
        /// Currency code (BDT, USD)
        /// </summary>
        public string Currency { get; set; } = "BDT";
    }

    /// <summary>
    /// Manual bank transfer info shown to tenant
    /// </summary>
    public class ManualPaymentSettings
    {
        public const string SectionName = "ManualPayment";

        public string BankName { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string? RoutingNumber { get; set; }
        public string? BranchName { get; set; }
        public string Instructions { get; set; } =
            "Please deposit the amount to the bank account above and submit the deposit slip with your payment reference.";
    }

    /// <summary>
    /// File upload settings
    /// </summary>
    public class FileUploadSettings
    {
        public const string SectionName = "FileUpload";

        /// <summary>
        /// Base path under wwwroot (e.g. "uploads")
        /// </summary>
        public string BasePath { get; set; } = "uploads";

        /// <summary>
        /// Max file size in MB
        /// </summary>
        public int MaxFileSizeMb { get; set; } = 5;

        public string[] AllowedImageExtensions { get; set; } = { ".jpg", ".jpeg", ".png", ".webp" };
        public string[] AllowedDocumentExtensions { get; set; } = { ".pdf", ".jpg", ".jpeg", ".png" };
    }
}
