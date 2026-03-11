using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Finance
{
    public class VoucherDetail : TenantEntity
    {
        public int VoucherId { get; set; }
        public int LedgerId { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string Remarks { get; set; }
    }
}
