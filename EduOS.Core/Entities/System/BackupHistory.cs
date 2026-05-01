using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class BackupHistory : BaseTenantEntity
    {
        public string BackupType { get; set; } = "Manual"; // Manual/Auto
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string Status { get; set; } = "Success"; // Success/Failed
        public DateTime BackupDate { get; set; }
    }
}
