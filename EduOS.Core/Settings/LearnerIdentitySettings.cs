namespace EduOS.Core.Settings;

public sealed class LearnerIdentitySettings
{
    public const string SectionName = "LearnerIdentity";

    /// <summary>
    /// Base64-encoded random key with at least 32 decoded bytes. Configure through
    /// a secret store or environment variable; never commit a production value.
    /// </summary>
    public string LookupKeyBase64 { get; set; } = string.Empty;

    public int ConsentRequestLifetimeHours { get; set; } = 168;
}
