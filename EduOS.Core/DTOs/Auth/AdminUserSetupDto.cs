namespace EduOS.Core.DTOs.Auth
{
    public class AdminUserSetupDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Password { get; set; } = string.Empty;
        public int RoleId { get; set; }
    }
}