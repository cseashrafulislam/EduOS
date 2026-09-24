using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;
using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.Entities.Payroll
{
    public class Payroll : BaseTenantEntity
    {
        public Guid PublicId { get; set; } = Guid.NewGuid();
        public Guid GenerationRequestId { get; set; }
        public string BillingKey { get; set; } = string.Empty;
        public long EmployeeId { get; set; }
        public string Month { get; set; } = string.Empty;
        public int Year { get; set; }
        public decimal GrossSalary { get; set; }
        public int AbsentDays { get; set; }
        public decimal AttendanceDeduction { get; set; }
        public decimal LoanDeduction { get; set; }
        public decimal Deductions { get; set; }
        public decimal Bonus { get; set; }
        public decimal NetSalary { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string Status { get; set; } = "Pending";
        public string? PaymentMethod { get; set; }
        public string? Note { get; set; }
        public long? PaidByUserId { get; set; }
        [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public virtual Employee? Employee { get; set; }
    }
}
