using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.SaaS
{
    public class Campus : TenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public string? Code { get; set; }

        public string? CampusType { get; set; }   // Main / Branch / Academic / Administrative
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

        public bool IsMainCampus { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }
}