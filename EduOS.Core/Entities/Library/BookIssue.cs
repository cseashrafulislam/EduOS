using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Library
{
    public class BookIssue : TenantEntity
    {
        public int BookId { get; set; }
        public int StudentId { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public decimal FineAmount { get; set; }
    }
}
