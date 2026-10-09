namespace EduOS.Core.DTOs.SaaS;

public sealed class RegisterTenantDomainRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(255)] public string HostName { get; set; } = string.Empty;
}

public sealed class TenantDomainRegistrationDto
{
    public TenantDomainDto Domain { get; set; } = new();
    public string VerificationRecordName { get; set; } = string.Empty;
    /// <summary>Ephemeral DNS challenge value. Persist only a hash and never return after initial creation.</summary>
    public string VerificationRecordValue { get; set; } = string.Empty;
}

public sealed class ChangeTenantDomainRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class LegalDocumentDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime EffectiveAt { get; set; }
}

public sealed class UserLegalAcceptanceDto
{
    public long Id { get; set; }
    public long LegalDocumentId { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTime AcceptedAt { get; set; }
}

public sealed class AcceptLegalDocumentRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long LegalDocumentId { get; set; }
    /// <summary>Explicit consent; false must be rejected by the server.</summary>
    public bool Accepted { get; set; }
}
