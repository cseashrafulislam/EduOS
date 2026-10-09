namespace EduOS.Core.Interfaces
{
    public interface ICurrentUserService
    {
      
        bool IsAuthenticated { get; }
        long UserId { get; }
        long TenantId { get; }

        string? FullName { get; }
        string? Email { get; }
        bool IsSuperAdmin { get; }
        bool IsTenantAdmin { get; }
        IReadOnlyList<string> Roles { get; }
        bool IsInRole(string role);
        string? IpAddress { get; }
        string? UserAgent { get; }
    }
}
