using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS;

public class Tenant : BaseEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long? InstitutionTypeDefinitionId { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(250)] public string? LegalName { get; set; }
    [MaxLength(100)] public string? RegistrationNumber { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(100)] public string? Subdomain { get; set; }
    [Required, MaxLength(200)] public string Email { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    [MaxLength(10)] public string CountryCode { get; set; } = "BD";
    [MaxLength(500)] public string? LogoUrl { get; set; }
    [MaxLength(500)] public string? FaviconUrl { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    [MaxLength(100)] public string TimeZoneId { get; set; } = "Asia/Dhaka";
    [MaxLength(20)] public string DefaultLanguage { get; set; } = "bn-BD";
    public TenantState State { get; set; } = TenantState.PendingVerification;
    public OnboardingStage OnboardingStage { get; set; } = OnboardingStage.EmailVerification;
    public bool IsOnboardingComplete { get; set; }
    public DateTime? OnboardingCompletedAt { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Campus : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(500)] public string? Address { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(100)] public string? TimeZoneId { get; set; }
    public bool IsOnlineCampus { get; set; }
    public bool IsHeadOffice { get; set; }
    public bool IsActive { get; set; } = true;
}

public class InstitutionTypeDefinition : BaseEntity
{
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(150)] public string? NameBangla { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public AcademicCycleType DefaultAcademicCycle { get; set; } = AcademicCycleType.Annual;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class TenantSetting : BaseTenantEntity
{
    [Required, MaxLength(150)] public string Key { get; set; } = string.Empty;
    [Required] public string Value { get; set; } = string.Empty;
    public bool IsSensitive { get; set; }
    [MaxLength(100)] public string? Category { get; set; }
}

public class TenantTerminology : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Key { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string SingularLabel { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string PluralLabel { get; set; } = string.Empty;
    [MaxLength(100)] public string? SingularLabelBangla { get; set; }
    [MaxLength(100)] public string? PluralLabelBangla { get; set; }
}

public class ProductModule : BaseEntity
{
    [Required, MaxLength(100)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsCore { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class Feature : BaseEntity
{
    [Required, MaxLength(100)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ProductModuleFeature : BaseEntity
{
    public long ProductModuleId { get; set; }
    public long FeatureId { get; set; }
}

public class InstitutionTypeModule : BaseEntity
{
    public long InstitutionTypeDefinitionId { get; set; }
    public long ProductModuleId { get; set; }
    public bool IsRequired { get; set; }
    public bool IsDefaultEnabled { get; set; } = true;
}

public class TenantModule : BaseTenantEntity
{
    public long ProductModuleId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime? EnabledAt { get; set; }
    public DateTime? DisabledAt { get; set; }
}

public class SubscriptionPlan : BaseEntity
{
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")] public decimal MonthlyPrice { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal YearlyPrice { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public int TrialDays { get; set; }
    public int MaxStudents { get; set; }
    public int MaxEmployees { get; set; }
    public int MaxUsers { get; set; }
    public int MaxCampuses { get; set; }
    public int MaxStorageMb { get; set; }
    public bool IsPublic { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public class PlanFeature : BaseEntity
{
    public long SubscriptionPlanId { get; set; }
    public long FeatureId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public long? LimitValue { get; set; }
}

public class TenantSubscription : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long SubscriptionPlanId { get; set; }
    public long? PreviousSubscriptionId { get; set; }
    [MaxLength(30)] public string BillingCycleCode { get; set; } = "Monthly";
    public SubscriptionState State { get; set; } = SubscriptionState.Active;
    public bool IsTrial { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PriceSnapshot { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public bool AutoRenew { get; set; }
}

public class SubscriptionInvoice : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long TenantSubscriptionId { get; set; }
    [Required, MaxLength(50)] public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Subtotal { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TaxAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PaidAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DueAmount { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public InvoiceState State { get; set; } = InvoiceState.Issued;
    public DateTime? PaidAt { get; set; }
}

public class SubscriptionInvoiceLine : BaseTenantEntity
{
    public long SubscriptionInvoiceId { get; set; }
    [Required, MaxLength(250)] public string Description { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,4)")] public decimal Quantity { get; set; } = 1;
    [Column(TypeName = "decimal(18,2)")] public decimal UnitPrice { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
}

public class SubscriptionPayment : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long SubscriptionInvoiceId { get; set; }
    [Required, MaxLength(100)] public string TransactionId { get; set; } = string.Empty;
    [MaxLength(100)] public string? GatewayCode { get; set; }
    [MaxLength(150)] public string? ProviderTransactionId { get; set; }
    [MaxLength(150)] public string? ExternalReference { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public PaymentState State { get; set; } = PaymentState.Initiated;
    public DateTime InitiatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    [MaxLength(150)] public string? PayerBankName { get; set; }
    [MaxLength(100)] public string? DepositSlipNumber { get; set; }
    public DateOnly? DepositDate { get; set; }
    public long? DepositSlipFileId { get; set; }
    public long? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAt { get; set; }
    [MaxLength(1000)] public string? VerificationNote { get; set; }
    [MaxLength(1000)] public string? FailureReason { get; set; }
}

public class UsageStatistics : BaseTenantEntity
{
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int ActiveStudents { get; set; }
    public int ActiveEmployees { get; set; }
    public int ActiveCampuses { get; set; }
    public long StorageBytes { get; set; }
    public long SmsCount { get; set; }
    public long EmailCount { get; set; }
}

public class TenantDomain : BaseTenantEntity
{
    [Required, MaxLength(255)] public string HostName { get; set; } = string.Empty;
    [MaxLength(100)] public string VerificationTokenHash { get; set; } = string.Empty;
    [MaxLength(30)] public string VerificationStateCode { get; set; } = "Pending";
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? VerifiedAt { get; set; }
}

public class TenantFeature : BaseTenantEntity
{
    public long FeatureId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public long? LimitOverride { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
}

public class LegalDocument : BaseEntity
{
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Version { get; set; } = string.Empty;
    [Required] public string Content { get; set; } = string.Empty;
    public DateTime EffectiveAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UserLegalAcceptance : BaseEntity
{
    public long UserId { get; set; }
    public long? TenantId { get; set; }
    public long LegalDocumentId { get; set; }
    public DateTime AcceptedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string? IpAddress { get; set; }
    [MaxLength(500)] public string? UserAgent { get; set; }
}
