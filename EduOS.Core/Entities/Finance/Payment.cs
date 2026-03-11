using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Finance
{
    public class Payment : TenantEntity
    {
        public int InvoiceId { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string PaymentMethod { get; set; }
        public string ReferenceNo { get; set; }
    }
}
