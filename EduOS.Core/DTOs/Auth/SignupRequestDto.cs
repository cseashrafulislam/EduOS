namespace EduOS.Core.DTOs.Auth
{
    public class SignupRequestDto
    {
        public string InstitutionName { get; set; } = string.Empty;
        public string InstitutionType { get; set; } = string.Empty;

        public string OwnerFullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNo { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;

        public string? Address { get; set; }

        public int SubscriptionPlanId { get; set; }
    }
}