using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;

namespace EduOS.Core.Entities.Payroll
{
    public class LoanAdvance : BaseTenantEntity
    {
        public int EmployeeId { get; set; }
        public string Type { get; set; } = "Loan"; // Loan/Advance
        public decimal Amount { get; set; }
        public DateTime RequestDate { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public string Status { get; set; } = "Pending"; // Pending/Approved/Paid/Completed
        public int InstallmentMonths { get; set; }
        public decimal MonthlyDeduction { get; set; }
        public decimal TotalDeducted { get; set; } = 0;
        public decimal RemainingAmount { get; set; }

        public virtual Employee? Employee { get; set; }
    }
}
