namespace EduOS.Core.Enums
{
    /// <summary>
    /// Represents the lifecycle status of a tenant (institution)
    /// </summary>
    public enum TenantStatus
    {
        /// <summary>
        /// Just signed up, email not verified yet
        /// </summary>
        PendingVerification = 1,

        /// <summary>
        /// Email verified but onboarding not complete (profile, payment, etc.)
        /// </summary>
        Onboarding = 2,

        /// <summary>
        /// On free trial period
        /// </summary>
        Trial = 3,

        /// <summary>
        /// Active paid subscription
        /// </summary>
        Active = 4,

        /// <summary>
        /// Subscription expired, awaiting payment
        /// </summary>
        Expired = 5,

        /// <summary>
        /// Manually suspended by SuperAdmin
        /// </summary>
        Suspended = 6,

        /// <summary>
        /// Tenant has cancelled subscription
        /// </summary>
        Cancelled = 7,

        /// <summary>
        /// Soft-deleted by tenant or admin
        /// </summary>
        Deleted = 8
    }
}
