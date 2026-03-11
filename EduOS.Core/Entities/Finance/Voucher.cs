using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Finance
{
    public class Voucher : TenantEntity
    {
        public string VoucherNo { get; set; }
        public DateTime VoucherDate { get; set; }
        public string VoucherType { get; set; }
        public string Remarks { get; set; }
    }
}
