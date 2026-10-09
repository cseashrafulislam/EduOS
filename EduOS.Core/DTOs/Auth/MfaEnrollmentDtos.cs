namespace EduOS.Core.DTOs.Auth;

/// <summary>Ephemeral setup information; never persist or log this response.</summary>
public sealed class MfaEnrollmentStartDto
{
    public string AuthenticatorKey { get; set; } = string.Empty;
    public string OtpAuthUri { get; set; } = string.Empty;
}

/// <summary>New recovery codes are returned once and persisted exclusively as hashes.</summary>
public sealed class MfaRecoveryCodesDto
{
    public IReadOnlyList<string> RecoveryCodes { get; set; } = Array.Empty<string>();
}
