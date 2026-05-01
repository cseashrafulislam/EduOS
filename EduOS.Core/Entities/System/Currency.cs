using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class Currency : BaseEntity
    {
        public string Code { get; set; } = string.Empty; // BDT/USD/INR
        public string Name { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public decimal ExchangeRate { get; set; }
        public bool IsBase { get; set; } = false;
        public bool IsActive { get; set; } = true;
    }
}
