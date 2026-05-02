namespace EduOS.Core.Enums
{
    public enum PaymentStatus
    {
        /// <summary>
        /// Payment initiated but not completed
        /// </summary>
        Pending = 1,

        /// <summary>
        /// Payment in progress (redirected to gateway)
        /// </summary>
        Processing = 2,

        /// <summary>
        /// Payment successful
        /// </summary>
        Successful = 3,

        /// <summary>
        /// Payment failed
        /// </summary>
        Failed = 4,

        /// <summary>
        /// User cancelled payment
        /// </summary>
        Cancelled = 5,

        /// <summary>
        /// Payment refunded
        /// </summary>
        Refunded = 6,

        /// <summary>
        /// Manual payment awaiting admin verification
        /// </summary>
        AwaitingVerification = 7
    }
}
