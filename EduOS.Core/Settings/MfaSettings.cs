namespace EduOS.Core.Settings;

public sealed class MfaSettings
{
    public const string SectionName = "Mfa";

    public int ChallengeLifetimeMinutes { get; set; } = 5;
    public int RecoveryCodeCount { get; set; } = 10;
}
