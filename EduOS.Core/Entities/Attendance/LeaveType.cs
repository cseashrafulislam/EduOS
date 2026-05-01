using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Attendance
{
    public class LeaveType : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty; // Casual/Sick/Earned/Maternity
        public int MaxDaysPerYear { get; set; }
        public bool IsPaid { get; set; } = true;
        public bool IsActive { get; set; } = true;
    }
}
