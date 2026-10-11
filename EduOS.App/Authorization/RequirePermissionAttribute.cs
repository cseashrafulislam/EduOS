using Microsoft.AspNetCore.Authorization;

namespace EduOS.App.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "EduOSPermission:";

    public RequirePermissionAttribute(string permissionCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);
        Policy = PolicyPrefix + permissionCode.Trim().ToUpperInvariant();
    }
}

public sealed record PermissionAccessRequirement(string PermissionCode) : IAuthorizationRequirement;
