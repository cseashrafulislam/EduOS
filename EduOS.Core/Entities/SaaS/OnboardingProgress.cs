using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.SaaS
{
    public class OnboardingProgress : TenantEntity
    {
        public bool AccountCreated { get; set; } = false;
        public bool EmailVerified { get; set; } = false;
        public bool InstitutionProfileCompleted { get; set; } = false;
        public bool CampusSetupCompleted { get; set; } = false;
        public bool AcademicSetupCompleted { get; set; } = false;
        public bool AdminUserSetupCompleted { get; set; } = false;
        public bool RolePermissionSetupCompleted { get; set; } = false;
        public bool SubscriptionSetupCompleted { get; set; } = false;
        public bool FinalCompleted { get; set; } = false;

        public int CurrentStep { get; set; } = 1;
        public DateTime? CompletedAt { get; set; }

        public string? Remarks { get; set; }
    }
}