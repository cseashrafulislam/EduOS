using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class VoucherDetail : BaseEntity
    {
        public int VoucherId { get; set; }
        public int AccountId { get; set; }
        public decimal DebitAmount { get; set; } = 0;
        public decimal CreditAmount { get; set; } = 0;
        public string? Description { get; set; }

        public virtual Voucher? Voucher { get; set; }
        public virtual Account? Account { get; set; }
    }
}
