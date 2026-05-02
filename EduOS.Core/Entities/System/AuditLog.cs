using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class AuditLog : BaseTenantEntity
    {
        public long? UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty; // Create, Update, Delete
        public string TableName { get; set; } = string.Empty;
        public long? RecordId { get; set; }
        public string? OldValue { get; set; } // JSON
        public string? NewValue { get; set; } // JSON
        public string IpAddress { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public TimeSpan ExecutionTime { get; set; }
        public bool IsSuccess { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }
}
