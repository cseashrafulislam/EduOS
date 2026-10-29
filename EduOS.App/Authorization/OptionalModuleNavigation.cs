using EduOS.Core.Interfaces.IServices;

namespace EduOS.App.Authorization;

/// <summary>
/// The shared shell and cross-module overview use the same current-tenant module
/// selections as route authorization. A failed read hides optional navigation;
/// this is never an authorization grant, and the API policies remain authoritative.
/// </summary>
public static class OptionalModuleNavigation
{
    private static readonly HashSet<string> OptionalCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "LMS", "LIBRARY", "HOSTEL", "TRANSPORT", "INVENTORY"
    };

    public static async Task<IReadOnlySet<string>> GetAvailableAsync(
        ITenantModuleService tenantModuleService, bool pilotCoreOnly,
        bool authenticatedTenantMember)
    {
        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (pilotCoreOnly || !authenticatedTenantMember) return denied;

        // A single bounded module-state read per request, not five independent
        // entitlement queries or a process-wide cache that can leak tenant state.
        var response = await tenantModuleService.GetCurrentTenantModulesAsync();
        if (!response.Success || response.Data is null) return denied;

        return response.Data
            .Where(x => x.IsEnabled && x.IsEntitledByPlan && OptionalCodes.Contains(x.ModuleCode))
            .Select(x => x.ModuleCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
