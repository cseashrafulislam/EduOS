using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.SaaS
{
    public class UsageSnapshot : TenantEntity
    {
        public DateTime SnapshotDate { get; set; }
        public int ActiveStudentCount { get; set; }
        public decimal BillAmount { get; set; }
        public bool IsInvoiced { get; set; } = false;
    }
}