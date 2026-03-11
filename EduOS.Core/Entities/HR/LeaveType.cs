using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.HR
{
    public class LeaveType : TenantEntity
    {
        public string Name { get; set; }
        public int MaxDays { get; set; }
        public bool IsPaid { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
