using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.Tenants
{
    /// <summary>
    /// Represents an institution (School/College/University/Coaching) using the EduOS platform.
    /// Each Tenant is an independent customer with isolated data.
    /// </summary>
    public class Tenant : BaseEntity
    {
        // ==================== Basic Identification ====================

        /// <summary>
        /// Display name of the institution (e.g. "ABC International School")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Short unique code (e.g. "ABC001"). Used internally.
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// Subdomain for tenant access (e.g. "abc-school" → abc-school.eduos.com).
        /// Nullable until tenant configures it during onboarding.
        /// </summary>
        public string? Subdomain { get; set; }

        /// <summary>
        /// Optional custom domain (e.g. "portal.abcschool.com")
        /// </summary>
        public string? CustomDomain { get; set; }

        /// <summary>
        /// Type of institution: School, College, University, Coaching, Training
        /// </summary>
        public string? InstitutionType { get; set; }

        // ==================== Contact Information ====================

        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Website { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; } = "Bangladesh";
        public string? PostalCode { get; set; }

        // ==================== Owner Information ====================

        /// <summary>
        /// Name of the person who signed up (institution owner/admin)
        /// </summary>
        public string OwnerName { get; set; } = string.Empty;
        public string? OwnerPhone { get; set; }
        public string? OwnerEmail { get; set; }
        public string? OwnerDesignation { get; set; }

        /// <summary>
        /// Initial admin user ID created during signup
        /// </summary>
        public long? OwnerUserId { get; set; }

        // ==================== Branding ====================

        public string? LogoUrl { get; set; }
        public string? FaviconUrl { get; set; }
        public string? PrimaryColor { get; set; } = "#1E40AF";
        public string? SecondaryColor { get; set; } = "#64748B";
        public string? AccentColor { get; set; } = "#F59E0B";

        // ==================== Localization ====================

        public string? Currency { get; set; } = "BDT";
        public string? CurrencySymbol { get; set; } = "৳";
        public string? TimeZone { get; set; } = "Asia/Dhaka";
        public string? Language { get; set; } = "en";
        public string? DateFormat { get; set; } = "dd-MM-yyyy";

        // ==================== Status & Subscription ====================

        /// <summary>
        /// Current lifecycle status of the tenant
        /// </summary>
        public TenantStatus Status { get; set; } = TenantStatus.PendingVerification;

        /// <summary>
        /// Currently active subscription ID
        /// </summary>
        public long? CurrentSubscriptionId { get; set; }

        /// <summary>
        /// Trial ends at this date (NULL if not on trial)
        /// </summary>
        public DateTime? TrialEndsAt { get; set; }

        /// <summary>
        /// Subscription expires on this date
        /// </summary>
        public DateTime? SubscriptionEndsAt { get; set; }

        /// <summary>
        /// Convenience flag - is tenant on trial right now?
        /// </summary>
        public bool IsTrialActive { get; set; }

        // ==================== Onboarding Progress ====================

        /// <summary>
        /// Current step in the onboarding wizard
        /// </summary>
        public OnboardingStep OnboardingStep { get; set; } = OnboardingStep.EmailVerification;

        /// <summary>
        /// True when tenant has completed all onboarding steps
        /// </summary>
        public bool IsOnboardingComplete { get; set; }

        /// <summary>
        /// When onboarding was finally completed
        /// </summary>
        public DateTime? OnboardingCompletedAt { get; set; }

        // ==================== Email Verification ====================

        public bool IsEmailVerified { get; set; }
        public DateTime? EmailVerifiedAt { get; set; }

        // ==================== Activity Tracking ====================

        /// <summary>
        /// Used for soft-blocking access without deleting
        /// </summary>
        public bool IsActive { get; set; } = true;

        public DateTime? LastActivityAt { get; set; }
        public DateTime? ActivatedAt { get; set; }
        public DateTime? SuspendedAt { get; set; }
        public string? SuspensionReason { get; set; }

        // ==================== Limits (cached from current plan) ====================

        public int MaxStudents { get; set; }
        public int MaxTeachers { get; set; }
        public int MaxCampuses { get; set; } = 1;
        public int MaxStorageMb { get; set; } = 1024;
        public int CurrentStudents { get; set; }
        public int CurrentTeachers { get; set; }
        public int CurrentStorageMb { get; set; }

        // ==================== Navigation Properties ====================

        public virtual ICollection<TenantSetting> Settings { get; set; } = new List<TenantSetting>();
        public virtual ICollection<TenantSubscription> Subscriptions { get; set; } = new List<TenantSubscription>();
    }
}
