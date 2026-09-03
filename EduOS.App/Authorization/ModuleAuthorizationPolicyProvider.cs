using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace EduOS.App.Authorization;

public sealed class ModuleAuthorizationPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public ModuleAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
        : base(options)
    {
    }

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(RequireModuleAttribute.PolicyPrefix, StringComparison.Ordinal))
            return base.GetPolicyAsync(policyName);

        var moduleCode = policyName[RequireModuleAttribute.PolicyPrefix.Length..];
        if (!IsValidModuleCode(moduleCode))
            return Task.FromResult<AuthorizationPolicy?>(null);

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new ModuleAccessRequirement(moduleCode))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    private static bool IsValidModuleCode(string code)
    {
        return code.Length is > 0 and <= 50
               && code.All(character =>
                   char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }
}
