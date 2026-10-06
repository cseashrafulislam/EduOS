namespace EduOS.Core.DTOs.SaaS;

public class TenantDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long? InstitutionTypeDefinitionId { get; set; }
    public string? InstitutionTypeName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Subdomain { get; set; }
    public string? CustomDomain { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string CountryCode { get; set; } = "BD";
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public string TimeZoneId { get; set; } = "Asia/Dhaka";
    public string DefaultLanguage { get; set; } = "bn-BD";
    public TenantState State { get; set; }
    public OnboardingStage OnboardingStage { get; set; }
    public bool IsOnboardingComplete { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class UpdateTenantProfileRequestDto
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public long? InstitutionTypeDefinitionId { get; set; }
    [Required, EmailAddress, MaxLength(200)] public string Email { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    [Required, MaxLength(10)] public string CountryCode { get; set; } = "BD";
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class UpdateTenantRegionalSettingsRequestDto
{
    [Required, MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    [Required, MaxLength(100)] public string TimeZoneId { get; set; } = "Asia/Dhaka";
    [Required, MaxLength(20)] public string DefaultLanguage { get; set; } = "bn-BD";
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class CampusDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsHeadOffice { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveCampusRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(500)] public string? Address { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    public bool IsHeadOffice { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class InstitutionTypeDefinitionDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameBangla { get; set; }
    public string? Description { get; set; }
    public AcademicCycleType DefaultAcademicCycle { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}

public class TenantSettingDto
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool IsSensitive { get; set; }
    public bool HasValue { get; set; }
    public string? Category { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveTenantSettingRequestDto
{
    [Required, MaxLength(150)] public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool IsSensitive { get; set; }
    [MaxLength(100)] public string? Category { get; set; }
    public string? RowVersion { get; set; }
}

public class TenantTerminologyDto
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string SingularLabel { get; set; } = string.Empty;
    public string PluralLabel { get; set; } = string.Empty;
    public string? SingularLabelBangla { get; set; }
    public string? PluralLabelBangla { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveTenantTerminologyRequestDto
{
    [Required, MaxLength(100)] public string Key { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string SingularLabel { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string PluralLabel { get; set; } = string.Empty;
    [MaxLength(100)] public string? SingularLabelBangla { get; set; }
    [MaxLength(100)] public string? PluralLabelBangla { get; set; }
    public string? RowVersion { get; set; }
}

public class ProductModuleDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsCore { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}

public class FeatureDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

public class TenantModuleDto
{
    public long Id { get; set; }
    public long ProductModuleId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsRequiredByPreset { get; set; }
    public bool IsEntitledByPlan { get; set; }
    public DateTime? EnabledAt { get; set; }
    public DateTime? DisabledAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveTenantModulesRequestDto
{
    public IReadOnlyList<TenantModuleSelectionDto> Modules { get; set; } = Array.Empty<TenantModuleSelectionDto>();
}

public class TenantModuleSelectionDto
{
    public long ProductModuleId { get; set; }
    public bool IsEnabled { get; set; }
    public string? RowVersion { get; set; }
}

public class SubscriptionPlanDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal MonthlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public int TrialDays { get; set; }
    public int MaxStudents { get; set; }
    public int MaxEmployees { get; set; }
    public int MaxCampuses { get; set; }
    public int MaxStorageMb { get; set; }
    public bool IsPublic { get; set; }
    public bool IsActive { get; set; }
    public IReadOnlyList<PlanFeatureDto> Features { get; set; } = Array.Empty<PlanFeatureDto>();
}

public class PlanFeatureDto
{
    public long FeatureId { get; set; }
    public string FeatureCode { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public long? LimitValue { get; set; }
}

public class TenantSubscriptionDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long SubscriptionPlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public SubscriptionState State { get; set; }
    public bool IsTrial { get; set; }
    public decimal PriceSnapshot { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public bool AutoRenew { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class StartSubscriptionRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long SubscriptionPlanId { get; set; }
    [Required, MaxLength(20)] public string BillingCycle { get; set; } = "Monthly";
    public bool StartTrialIfEligible { get; set; }
}

public class SubscriptionInvoiceDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long TenantSubscriptionId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DueAmount { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public InvoiceState State { get; set; }
    public DateTime? PaidAt { get; set; }
    public IReadOnlyList<SubscriptionInvoiceLineDto> Lines { get; set; } = Array.Empty<SubscriptionInvoiceLineDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class SubscriptionInvoiceLineDto
{
    public long Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}

public class SubscriptionPaymentDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long SubscriptionInvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public PaymentState State { get; set; }
    public DateTime InitiatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? PayerBankName { get; set; }
    public string? DepositSlipNumber { get; set; }
    public DateOnly? DepositDate { get; set; }
    public long? DepositSlipFileId { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerificationNote { get; set; }
    public string? FailureReason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class InitiateSubscriptionPaymentRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid InvoiceReference { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
}

public class SubmitManualSubscriptionPaymentRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid InvoiceReference { get; set; }
    [Required, MaxLength(150)] public string PayerBankName { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string DepositSlipNumber { get; set; } = string.Empty;
    public DateOnly DepositDate { get; set; }
    public long DepositSlipFileId { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
}

public class VerifySubscriptionPaymentRequestDto
{
    public Guid PaymentReference { get; set; }
    public bool Approve { get; set; }
    [MaxLength(1000)] public string? VerificationNote { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class UsageStatisticsDto
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
