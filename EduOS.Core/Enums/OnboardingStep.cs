namespace EduOS.Core.Enums
{
    /// <summary>
    /// Tracks tenant onboarding wizard progress.
    /// User must complete each step in order before accessing the dashboard.
    /// </summary>
    public enum OnboardingStep
    {
        /// <summary>
        /// Just signed up - waiting for email verification
        /// </summary>
        EmailVerification = 0,

        /// <summary>
        /// Email verified, ready to set up profile
        /// </summary>
        InstitutionProfile = 1,

        /// <summary>
        /// Profile saved, select subscription plan
        /// </summary>
        PlanSelection = 2,

        /// <summary>
        /// Plan selected, complete payment (or start trial)
        /// </summary>
        Payment = 3,

        /// <summary>
        /// Payment done, set up campus(es)
        /// </summary>
        CampusSetup = 4,

        /// <summary>
        /// Campus done, set up academic year + terms
        /// </summary>
        AcademicSetup = 5,

        /// <summary>
        /// Choose the plan-entitled modules used by this institution. The value
        /// is intentionally appended so existing persisted step values remain
        /// backward compatible.
        /// </summary>
        ModuleSetup = 9,

        /// <summary>
        /// Module selection done, configure branding (logo, colors, subdomain)
        /// </summary>
        BrandingSetup = 6,

        /// <summary>
        /// Branding done, set general preferences (currency, timezone, language)
        /// </summary>
        GeneralSettings = 7,

        /// <summary>
        /// Settings done, configure SMS/Email gateway (skippable)
        /// </summary>
        GatewaySetup = 8,

        /// <summary>
        /// Onboarding fully complete, dashboard unlocked
        /// </summary>
        Completed = 99
    }
}
