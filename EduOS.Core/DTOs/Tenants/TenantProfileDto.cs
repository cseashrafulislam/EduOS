using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Tenants
{
    /// <summary>
    /// Full tenant profile shown in onboarding & settings page
    /// </summary>
    public class TenantProfileDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? Subdomain { get; set; }
        public string? CustomDomain { get; set; }
        public string? InstitutionType { get; set; }

        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Website { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }
        public string? PostalCode { get; set; }

        public string OwnerName { get; set; } = string.Empty;
        public string? OwnerPhone { get; set; }
        public string? OwnerEmail { get; set; }
        public string? OwnerDesignation { get; set; }

        public string? LogoUrl { get; set; }
        public string? FaviconUrl { get; set; }
        public string? PrimaryColor { get; set; }
        public string? SecondaryColor { get; set; }
        public string? AccentColor { get; set; }

        public string? Currency { get; set; }
        public string? CurrencySymbol { get; set; }
        public string? TimeZone { get; set; }
        public string? Language { get; set; }
        public string? DateFormat { get; set; }

        public bool IsEmailVerified { get; set; }
        public bool IsOnboardingComplete { get; set; }
        public int OnboardingStep { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Update tenant basic profile (institution info, address, owner details)
    /// </summary>
    public class UpdateTenantProfileDto
    {
        [Required, StringLength(200)]
        public string Name { get; set; } = string.Empty;
        [StringLength(50)]
        public string? InstitutionType { get; set; }
        [StringLength(20)]
        public string? Phone { get; set; }
        [StringLength(200)]
        public string? Website { get; set; }
        [StringLength(500)]
        public string? Address { get; set; }
        [StringLength(100)]
        public string? City { get; set; }
        [StringLength(100)]
        public string? State { get; set; }
        [StringLength(100)]
        public string? Country { get; set; }
        [StringLength(20)]
        public string? PostalCode { get; set; }

        [Required, StringLength(150)]
        public string OwnerName { get; set; } = string.Empty;
        [StringLength(20)]
        public string? OwnerPhone { get; set; }
        [EmailAddress, StringLength(150)]
        public string? OwnerEmail { get; set; }
        [StringLength(100)]
        public string? OwnerDesignation { get; set; }
    }

    /// <summary>
    /// Update tenant branding (logo, colors, favicon)
    /// </summary>
    public class UpdateBrandingDto
    {
        [StringLength(7)]
        public string? PrimaryColor { get; set; }
        [StringLength(7)]
        public string? SecondaryColor { get; set; }
        [StringLength(7)]
        public string? AccentColor { get; set; }
        // Logo and favicon uploaded as multipart/form-data
    }

    /// <summary>
    /// Update tenant subdomain
    /// </summary>
    public class UpdateSubdomainDto
    {
        [Required, StringLength(50, MinimumLength = 3)]
        public string Subdomain { get; set; } = string.Empty;
    }

    /// <summary>
    /// Subdomain availability check result
    /// </summary>
    public class SubdomainCheckResult
    {
        public bool IsAvailable { get; set; }
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Subdomain { get; set; }
        public string? FullUrl { get; set; }
    }

    /// <summary>
    /// Update general/localization settings
    /// </summary>
    public class UpdateGeneralSettingsDto
    {
        [Required, StringLength(10)]
        public string Currency { get; set; } = "BDT";
        [Required, StringLength(10)]
        public string CurrencySymbol { get; set; } = "৳";
        [Required, StringLength(50)]
        public string TimeZone { get; set; } = "Asia/Dhaka";
        [Required, StringLength(10)]
        public string Language { get; set; } = "en";
        [Required, StringLength(20)]
        public string DateFormat { get; set; } = "dd-MM-yyyy";
    }

    /// <summary>
    /// SMS gateway settings (stored in TenantSetting table)
    /// </summary>
    public class SmsGatewaySettingsDto
    {
        [StringLength(40)]
        public string? Provider { get; set; }      // BulkSMSBD, SslWireless, etc.
        [StringLength(2048)]
        public string? ApiUrl { get; set; }
        [StringLength(1000)]
        public string? ApiKey { get; set; }
        [StringLength(20)]
        public string? SenderId { get; set; }
        public bool IsEnabled { get; set; }
    }

    /// <summary>
    /// Email gateway/SMTP settings
    /// </summary>
    public class EmailGatewaySettingsDto
    {
        [StringLength(253)]
        public string? SmtpHost { get; set; }
        [Range(1, 65535)]
        public int? SmtpPort { get; set; }
        [StringLength(320)]
        public string? SmtpUsername { get; set; }
        [StringLength(1000)]
        public string? SmtpPassword { get; set; }
        [EmailAddress, StringLength(320)]
        public string? FromEmail { get; set; }
        [StringLength(150)]
        public string? FromName { get; set; }
        public bool UseSsl { get; set; } = true;
        public bool IsEnabled { get; set; }
    }
}
