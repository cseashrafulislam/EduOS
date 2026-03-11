namespace EduOS.Core.DTOs.SaaS
{
    public class CampusSetupDto
    {
        public long? Id { get; set; }

        public string Name { get; set; } = string.Empty;
        public string? Code { get; set; }

        public string? CampusType { get; set; }
        public string? ContactNumber { get; set; }
        public string? Email { get; set; }

        public string? Country { get; set; }
        public string? Division { get; set; }
        public string? District { get; set; }
        public string? Thana { get; set; }
        public string? PostCode { get; set; }
        public string? Address { get; set; }

        public string? PrincipalName { get; set; }
        public string? HeadName { get; set; }

        public bool IsMainCampus { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }
}