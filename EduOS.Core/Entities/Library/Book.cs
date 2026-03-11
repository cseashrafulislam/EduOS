using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Library
{
    public class Book : TenantEntity
    {
        public string Title { get; set; }
        public string ISBN { get; set; }
        public int? CategoryId { get; set; }
        public string Author { get; set; }
        public int Quantity { get; set; }
    }
}
