using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;

namespace EduOS.App.Authorization;

public sealed class ModuleAccessHandler : AuthorizationHandler<ModuleAccessRequirement>
{
    private readonly ITenantModuleService _tenantModuleService;
    private readonly IConfiguration _configuration;

    // A staging-only safety boundary. Existing tenant module selections and data are not changed.
    // V2 LMS/Library/Hostel/Transport and V3 Inventory remain unavailable during V1 pilot.
    // Inventory includes Asset Lifecycle in the canonical module catalog.
    private static readonly HashSet<string> PilotExcludedModules =
        new(StringComparer.OrdinalIgnoreCase) { "LMS", "LIBRARY", "HOSTEL", "TRANSPORT", "INVENTORY" };

    public ModuleAccessHandler(ITenantModuleService tenantModuleService, IConfiguration configuration)
    {
        _tenantModuleService = tenantModuleService;
        _configuration = configuration;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ModuleAccessRequirement requirement)
    {
        // Enforce at authorization, not just in the menu. A tenant cannot re-enable
        // an excluded module via its existing module-selection API during a pilot.
        if (_configuration.GetValue<bool>("PilotRelease:Enabled") &&
            PilotExcludedModules.Contains(requirement.ModuleCode))
            return;

        if (await _tenantModuleService.IsCurrentTenantModuleAvailableAsync(requirement.ModuleCode))
            context.Succeed(requirement);
    }
}
