using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Common;

/// <summary>
/// Trusted server-side subscription and tenant admission policy. A verified active
/// subscription permits entry to the ERP without claiming that optional setup is
/// complete; it never grants additional roles or module entitlements.
/// </summary>
public static class OnboardingWorkspacePolicy
{
    public static bool CanActivate(Tenant tenant, TenantSubscription? subscription, DateTime utcNow) =>
        !tenant.IsDeleted &&
        tenant.State is TenantState.Active or TenantState.PendingVerification &&
        tenant.EmailVerifiedAt.HasValue &&
        tenant.InstitutionTypeDefinitionId.GetValueOrDefault() > 0 &&
        !string.IsNullOrWhiteSpace(tenant.Name) &&
        !string.IsNullOrWhiteSpace(tenant.Email) &&
        tenant.OnboardingStage >= OnboardingStage.Payment &&
        subscription != null && !subscription.IsDeleted &&
        subscription.TenantId == tenant.Id &&
        subscription.State is SubscriptionState.Active or SubscriptionState.Trial &&
        subscription.StartsAt <= utcNow && subscription.EndsAt > utcNow;

    public static bool CanUse(Tenant tenant, TenantSubscription? subscription, DateTime utcNow) =>
        tenant.State == TenantState.Active &&
        tenant.OnboardingStage >= OnboardingStage.CampusSetup &&
        CanActivate(tenant, subscription, utcNow);
}
