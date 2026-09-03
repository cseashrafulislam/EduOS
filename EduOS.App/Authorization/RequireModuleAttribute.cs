using Microsoft.AspNetCore.Authorization;

namespace EduOS.App.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireModuleAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "EduOSModule:";

    public RequireModuleAttribute(string moduleCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCode);
        Policy = PolicyPrefix + moduleCode.Trim().ToUpperInvariant();
    }
}
