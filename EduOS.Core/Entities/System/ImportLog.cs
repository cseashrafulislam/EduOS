using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class ImportLog : BaseTenantEntity
    {
        public string ImportType { get; set; } = string.Empty; // Student/Employee
        public string FileName { get; set; } = string.Empty;
        public int TotalRows { get; set; }
        public int SuccessRows { get; set; }
        public int FailedRows { get; set; }
        public string? ErrorLog { get; set; }
        public int ImportedBy { get; set; }
        public DateTime ImportedAt { get; set; }
    }
}
