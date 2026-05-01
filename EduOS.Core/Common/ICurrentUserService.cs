namespace EduOS.Core.Common
{
    public interface ICurrentUserService
    {
        int UserId { get; }
        int TenantId { get; }
        string UserType { get; }
        string? Email { get; }
        bool IsAuthenticated { get; }
        List<string> Permissions { get; }
        void SetTenant(int tenantId);
    }
}
