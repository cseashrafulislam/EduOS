namespace EduOS.Core.Common
{
    public interface ICurrentUserService
    {
        // Identity
        int UserId { get; }
        int TenantId { get; }
        string UserType { get; }
        string? Email { get; }
        string? FullName { get; }

        // Authentication Status
        bool IsAuthenticated { get; }
        bool IsSuperAdmin { get; }

        // Authorization
        List<string> Permissions { get; }
        List<string> Roles { get; }

        // Request Info
        string? IpAddress { get; }
        string? UserAgent { get; }

        // Permission Checks
        bool HasPermission(string permission);
        bool HasAnyPermission(params string[] permissions);
        bool HasAllPermissions(params string[] permissions);
        bool HasRole(string role);

        // Tenant Switching (For SuperAdmin)
        void SetTenant(int tenantId);
        void ClearImpersonation();
    }
}
