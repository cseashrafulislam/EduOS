namespace EduOS.Core.Enums
{
    public enum SubscriptionStatus
    {
        /// <summary>
        /// Subscription created, awaiting first payment
        /// </summary>
        PendingPayment = 1,

        /// <summary>
        /// Active free trial
        /// </summary>
        Trialing = 2,

        /// <summary>
        /// Active paid subscription
        /// </summary>
        Active = 3,

        /// <summary>
        /// Payment failed, in grace period
        /// </summary>
        PastDue = 4,

        /// <summary>
        /// Subscription expired
        /// </summary>
        Expired = 5,

        /// <summary>
        /// Cancelled by user but still active until period ends
        /// </summary>
        CancelAtPeriodEnd = 6,

        /// <summary>
        /// Fully cancelled
        /// </summary>
        Cancelled = 7
    }
}
