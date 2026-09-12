using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Entities.Students;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Library;

public class BookIssue : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long BookId { get; set; }
    public long? StudentId { get; set; }
    public long? EmployeeId { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime ReturnDate { get; set; }
    public DateTime? ActualReturnDate { get; set; }
    public decimal FineAmount { get; set; }
    public string Status { get; set; } = "Issued";
    public long? IssuedByUserId { get; set; }
    public long? ReturnedByUserId { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public virtual Book? Book { get; set; }
    public virtual Student? Student { get; set; }
    public virtual Employee? Employee { get; set; }
}
