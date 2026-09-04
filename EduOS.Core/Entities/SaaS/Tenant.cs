using EduOS.Core.Entities.Base;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.SaaS
{

    public class Tenant : BaseEntity
    {
        // ==================== Basic Identification ====================
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;

        public string? Subdomain { get; set; }
        public string? CustomDomain { get; set; }
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
        public string OwnerName { get; set; } = string.Empty;
        public string? OwnerPhone { get; set; }
        public string? OwnerEmail { get; set; }
        public string? OwnerDesignation { get; set; }
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
        public TenantStatus Status { get; set; } = TenantStatus.PendingVerification;
        public long? CurrentSubscriptionId { get; set; }
        public DateTime? TrialEndsAt { get; set; }

        public DateTime? SubscriptionEndsAt { get; set; }
        public bool IsTrialActive { get; set; }

        // ==================== Onboarding Progress ====================
        public OnboardingStep OnboardingStep { get; set; } = OnboardingStep.EmailVerification;
        public bool IsOnboardingComplete { get; set; }

        public DateTime? OnboardingCompletedAt { get; set; }

        // ==================== Email Verification ====================

        public bool IsEmailVerified { get; set; }
        public DateTime? EmailVerifiedAt { get; set; }

        // ==================== Activity Tracking ====================
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
