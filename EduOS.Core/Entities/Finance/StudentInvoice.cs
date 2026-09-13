using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Finance
{
    public class StudentInvoice : BaseTenantEntity
    {
        public Guid PublicId { get; set; } = Guid.NewGuid();
        public Guid GenerationRequestId { get; set; }
        public string BillingKey { get; set; } = string.Empty;
        public long StudentId { get; set; }
        public long AcademicYearId { get; set; }
        public long ClassId { get; set; }
        public long SectionId { get; set; }
        public string InvoiceNo { get; set; } = string.Empty;
        public string Month { get; set; } = string.Empty;
        public int Year { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal DueAmount { get; set; }
        public decimal? DiscountAmount { get; set; }
        public decimal? FineAmount { get; set; }
        public string Status { get; set; } = "Unpaid";
        public DateTime DueDate { get; set; }
        public DateTime CreatedDate { get; set; }
        [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public virtual Student? Student { get; set; }
        public virtual ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
    }
}
