namespace EduOS.Core.Enums
{
    public enum PaymentMethod
    {
        /// <summary>
        /// Manual bank transfer - admin verifies
        /// </summary>
        ManualBankTransfer = 1,

        /// <summary>
        /// AamarPay gateway (cards, mobile banking)
        /// </summary>
        AamarPay = 2,

        /// <summary>
        /// bKash personal/merchant
        /// </summary>
        Bkash = 3,

        /// <summary>
        /// Nagad personal/merchant
        /// </summary>
        Nagad = 4,

        /// <summary>
        /// SSL Commerz gateway
        /// </summary>
        SslCommerz = 5,

        /// <summary>
        /// Stripe (international)
        /// </summary>
        Stripe = 6,

        /// <summary>
        /// Free trial - no payment
        /// </summary>
        FreeTrial = 99
    }
}
