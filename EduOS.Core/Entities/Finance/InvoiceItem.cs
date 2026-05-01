using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class InvoiceItem : BaseEntity
    {
        public int InvoiceId { get; set; }
        public int FeeHeadId { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }

        public virtual StudentInvoice? Invoice { get; set; }
        public virtual FeeHead? FeeHead { get; set; }
    }
}
