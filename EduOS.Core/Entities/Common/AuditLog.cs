using System;

namespace EduOS.Core.Entities.Common
{
    public class AuditLog : BaseEntity
    {
        public string TableName { get; set; }
        public string Action { get; set; }
        public string UserId { get; set; }
        public DateTime ActionDate { get; set; } = DateTime.UtcNow;
        public string Data { get; set; }
    }
}
