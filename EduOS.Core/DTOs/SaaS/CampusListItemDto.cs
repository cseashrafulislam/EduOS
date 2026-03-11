namespace EduOS.Core.DTOs.SaaS
{
    public class CampusListItemDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Code { get; set; }
        public string? CampusType { get; set; }
        public string? ContactNumber { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public bool IsMainCampus { get; set; }
        public bool IsActive { get; set; }
        public int DisplayOrder { get; set; }
    }
}