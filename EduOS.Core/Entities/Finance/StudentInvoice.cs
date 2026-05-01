using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Finance
{
    public class StudentInvoice : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public string InvoiceNo { get; set; } = string.Empty;
        public string Month { get; set; } = string.Empty;
        public int Year { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; } = 0;
        public decimal DueAmount { get; set; }
        public decimal? DiscountAmount { get; set; }
        public decimal? FineAmount { get; set; }
        public string Status { get; set; } = "Unpaid"; // Paid/Partial/Unpaid
        public DateTime DueDate { get; set; }
        public DateTime CreatedDate { get; set; }

        public virtual Student? Student { get; set; }
        public virtual ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
    }
}
