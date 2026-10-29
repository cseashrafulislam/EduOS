using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.LMS;

public sealed class LmsPublicVerificationChangeDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class LmsPublicVerificationStatusDto
{
    public bool Enabled { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class LmsPublicVerificationLinkDto
{
    // One-time response. Never persisted in plaintext or included in audit records.
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class LmsPublicVerificationTokenDto
{
    [Required, StringLength(43, MinimumLength = 43)]
    public string Token { get; set; } = string.Empty;
}

public sealed class LmsPublicVerificationResultDto
{
    public string Status { get; set; } = string.Empty;
    public string IssuerName { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public int Revision { get; set; }
}
