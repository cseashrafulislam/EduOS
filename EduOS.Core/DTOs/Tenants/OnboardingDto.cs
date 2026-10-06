using EduOS.Core.Enums;

namespace EduOS.Core.DTOs.Tenants
{
    /// <summary>
    /// Returns full onboarding state - what step user is on,
    /// what's completed, what's pending.
    /// </summary>
    public class OnboardingStatusDto
    {
        public long TenantId { get; set; }
        public OnboardingStep CurrentStep { get; set; }
        public bool IsComplete { get; set; }
        public DateTime? CompletedAt { get; set; }

        public List<OnboardingStepStatusDto> Steps { get; set; } = new();
        public int TotalSteps { get; set; }
        public int CompletedSteps { get; set; }
        public int ProgressPercentage { get; set; }

        public string? NextStepCode { get; set; }
        public string? NextStepName { get; set; }
        public string? NextStepUrl { get; set; }
    }

    public class OnboardingStepStatusDto
    {
        public OnboardingStep Step { get; set; }
        /// <summary>
        /// Stable language-neutral identifier used by web and mobile clients.
        /// </summary>
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconClass { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsLocked { get; set; }    // can't access yet
        public bool IsSkippable { get; set; }
        public int Order { get; set; }
    }

    /// <summary>
    /// Mark a step as complete and advance to next
    /// </summary>
    public class CompleteStepDto
    {
        public OnboardingStep Step { get; set; }
        public bool Skipped { get; set; }
    }
}
