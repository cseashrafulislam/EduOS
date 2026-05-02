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
        public string Name { get; set; } = string.Empty;
        public string? InstitutionType { get; set; }
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
    }

    /// <summary>
    /// Update tenant branding (logo, colors, favicon)
    /// </summary>
    public class UpdateBrandingDto
    {
        public string? PrimaryColor { get; set; }
        public string? SecondaryColor { get; set; }
        public string? AccentColor { get; set; }
        // Logo and favicon uploaded as multipart/form-data
    }

    /// <summary>
    /// Update tenant subdomain
    /// </summary>
    public class UpdateSubdomainDto
    {
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
        public string Currency { get; set; } = "BDT";
        public string CurrencySymbol { get; set; } = "৳";
        public string TimeZone { get; set; } = "Asia/Dhaka";
        public string Language { get; set; } = "en";
        public string DateFormat { get; set; } = "dd-MM-yyyy";
    }

    /// <summary>
    /// SMS gateway settings (stored in TenantSetting table)
    /// </summary>
    public class SmsGatewaySettingsDto
    {
        public string? Provider { get; set; }      // BulkSMSBD, SslWireless, etc.
        public string? ApiUrl { get; set; }
        public string? ApiKey { get; set; }
        public string? SenderId { get; set; }
        public bool IsEnabled { get; set; }
    }

    /// <summary>
    /// Email gateway/SMTP settings
    /// </summary>
    public class EmailGatewaySettingsDto
    {
        public string? SmtpHost { get; set; }
        public int? SmtpPort { get; set; }
        public string? SmtpUsername { get; set; }
        public string? SmtpPassword { get; set; }
        public string? FromEmail { get; set; }
        public string? FromName { get; set; }
        public bool UseSsl { get; set; } = true;
        public bool IsEnabled { get; set; }
    }
}
