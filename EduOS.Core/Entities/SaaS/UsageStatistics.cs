using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS
{
    public class UsageStatistics : BaseTenantEntity
    {
        public string MetricName { get; set; } = string.Empty; // TotalStudents/TotalTeachers/SmsSent/StorageUsedMB
        public int MetricValue { get; set; }
        public DateTime Date { get; set; }
    }
}
