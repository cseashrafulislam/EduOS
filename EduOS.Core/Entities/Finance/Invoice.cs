using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class Invoice : BaseTenantEntity
    {
        public string InvoiceNo { get; set; }
        public int StudentId { get; set; }
        public DateTime InvoiceDate { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public string Status { get; set; }
    }
}
