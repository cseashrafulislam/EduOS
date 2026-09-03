using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;

namespace EduOS.App.Authorization;

public sealed class ModuleAccessHandler : AuthorizationHandler<ModuleAccessRequirement>
{
    private readonly ITenantModuleService _tenantModuleService;

    public ModuleAccessHandler(ITenantModuleService tenantModuleService)
    {
        _tenantModuleService = tenantModuleService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ModuleAccessRequirement requirement)
    {
        if (await _tenantModuleService.IsCurrentTenantModuleAvailableAsync(requirement.ModuleCode))
            context.Succeed(requirement);
    }
}
