using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class Document : BaseTenantEntity
    {
        public int OwnerId { get; set; }
        public string OwnerType { get; set; } = string.Empty; // Student/Employee
        public string DocumentType { get; set; } = string.Empty; // BirthCert/NID/Photo
        public string FileName { get; set; } = string.Empty;
        public string FileUrl { get; set; } = string.Empty;
        public int FileSize { get; set; } // KB
        public DateTime UploadedAt { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }
}
