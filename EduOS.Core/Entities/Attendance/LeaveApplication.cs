using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Attendance
{
    public class LeaveApplication : BaseTenantEntity
    {
        public int UserId { get; set; }
        public string UserType { get; set; } = string.Empty; // Student/Employee
        public int LeaveTypeId { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int TotalDays { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public string Status { get; set; } = "Pending"; // Pending/Approved/Rejected
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? Remarks { get; set; }

        public virtual LeaveType? LeaveType { get; set; }
    }
}
