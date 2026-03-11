using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.SaaS
{
    public class Tenant : BaseAuditableEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;

        public string InstitutionType { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? AlternatePhone { get; set; }
        public string? Address { get; set; }
        public string? ContactPersonName { get; set; }
        public string? ContactPersonDesignation { get; set; }
        public string? ContactPersonEmail { get; set; }

        public string? ShortName { get; set; }
        public string? TimeZone { get; set; }
        public string? Currency { get; set; }

        public string? Country { get; set; }
        public string? Division { get; set; }
        public string? District { get; set; }
        public string? Thana { get; set; }
        public string? PostCode { get; set; }

        public string? Subdomain { get; set; }
        public string? CustomDomain { get; set; }
        public bool IsCustomDomainVerified { get; set; } = false;
        public string? DomainVerificationToken { get; set; }

        public string? LogoUrl { get; set; }
        public string? FaviconUrl { get; set; }
        public string? PrimaryColor { get; set; }
        public string? SecondaryColor { get; set; }
        public string? WebsiteUrl { get; set; }

        public string? EIIN { get; set; }
        public string? RegistrationNumber { get; set; }
        public string? EducationBoard { get; set; }
        public DateTime? EstablishedDate { get; set; }

        public string? InstitutionCode { get; set; }
        public string? DatabaseName { get; set; }
        public string? SchemaName { get; set; }
        public string? StorageKey { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsEmailVerified { get; set; } = false;
        public bool IsSetupCompleted { get; set; } = false;

        public DateTime? TrialStartDate { get; set; }
        public DateTime? TrialEndDate { get; set; }
        public DateTime? SubscriptionExpireAt { get; set; }
        public bool IsSuspended { get; set; } = false;
        public string? SuspensionReason { get; set; }
        
        public DateTime? LastLoginAt { get; set; }
        public DateTime? LastActivityAt { get; set; }
        public int FailedLoginCount { get; set; } = 0;
        public bool IsLocked { get; set; } = false;

        public string Status { get; set; } = "Pending";
        public int CurrentOnboardingStep { get; set; } = 1;
        public DateTime? SetupCompletedAt { get; set; }

        public string? Language { get; set; } = "en";
        public string? DateFormat { get; set; } = "dd-MMM-yyyy";

        public DateTime? LockedUntil { get; set; }
        public DateTime? LastPasswordChangedAt { get; set; }

        public byte[]? RowVersion { get; set; }
    }
}