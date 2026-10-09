using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.Tenants;

public sealed class OnboardingStatusDto
{
    public long TenantId { get; set; }
    public OnboardingStage CurrentStage { get; set; }
    public bool IsComplete { get; set; }
    public DateTime? CompletedAt { get; set; }
    public IReadOnlyList<OnboardingStageStatusDto> Stages { get; set; } = Array.Empty<OnboardingStageStatusDto>();
    public int TotalStages { get; set; }
    public int CompletedStages { get; set; }
    public int ProgressPercentage { get; set; }
    public string? NextStageCode { get; set; }
    public string? NextStageName { get; set; }
}

public sealed class OnboardingStageStatusDto
{
    public OnboardingStage Stage { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsLocked { get; set; }
    public bool IsSkippable { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class CompleteOnboardingStageRequestDto
{
    public OnboardingStage Stage { get; set; }
    public bool Skipped { get; set; }
}
