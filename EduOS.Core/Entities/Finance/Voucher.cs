using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class Voucher : BaseTenantEntity
    {
        public string VoucherNo { get; set; } = string.Empty;
        public string VoucherType { get; set; } = string.Empty; // Receipt/Payment/Journal/Contra
        public DateTime Date { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Description { get; set; }

        public virtual ICollection<VoucherDetail> Details { get; set; } = new List<VoucherDetail>();
    }
}
