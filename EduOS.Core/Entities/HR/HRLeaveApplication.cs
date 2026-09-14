using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.HR
{
    public class HRLeaveApplication : BaseTenantEntity
    {
        public long EmployeeId { get; set; }
        public long LeaveTypeId { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string Reason { get; set; }
        public string Status { get; set; }
    }
}
