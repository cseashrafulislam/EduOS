using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.HR
{
    public class LeaveApplication : BaseTenantEntity
    {
        public int EmployeeId { get; set; }
        public int LeaveTypeId { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string Reason { get; set; }
        public string Status { get; set; }
    }
}
