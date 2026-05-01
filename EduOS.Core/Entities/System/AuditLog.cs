using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class AuditLog : BaseTenantEntity
    {
        public int UserId { get; set; }
        public string Action { get; set; } = string.Empty; // Create/Update/Delete
        public string TableName { get; set; } = string.Empty;
        public int RecordId { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? IpAddress { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
