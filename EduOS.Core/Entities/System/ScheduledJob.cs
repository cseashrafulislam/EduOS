using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class ScheduledJob : BaseTenantEntity
    {
        public string JobName { get; set; } = string.Empty;
        public string JobType { get; set; } = "Daily"; // Daily/Weekly/Monthly
        public string CronExpression { get; set; } = string.Empty;
        public DateTime? LastRun { get; set; }
        public DateTime NextRun { get; set; }
        public string Status { get; set; } = "Active"; // Active/Paused
        public string? LastRunStatus { get; set; }
        public string? LastError { get; set; }
    }
}
