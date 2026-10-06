namespace EduOS.Core.DTOs.SaaS
{
    public class CampusListItemDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Code { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? HeadName { get; set; }
        public bool IsHeadOffice { get; set; }
        public bool IsActive { get; set; }
    }
}