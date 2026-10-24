namespace EduOS.Core.DTOs.SaaS;

/// <summary>Last persisted snapshot and local day, not a claim that Hangfire is healthy.</summary>
public sealed class UsageCaptureAutomationDto
{
    public DateOnly LocalDay { get; set; }
    public DateOnly? LastCapturedDay { get; set; }
    public bool CapturedToday { get; set; }
    public string EffectiveTimeZoneId { get; set; } = "UTC";
}
