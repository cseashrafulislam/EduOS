using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Library
{
    public class BookIssue : BaseTenantEntity
    {
        public int BookId { get; set; }
        public int? StudentId { get; set; }
        public int? EmployeeId { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ReturnDate { get; set; }
        public DateTime? ActualReturnDate { get; set; }
        public decimal FineAmount { get; set; } = 0;
        public string Status { get; set; } = "Issued"; // Issued/Returned/Lost

        public virtual Book? Book { get; set; }
        public virtual Student? Student { get; set; }
        public virtual Employee? Employee { get; set; }
    }
}
