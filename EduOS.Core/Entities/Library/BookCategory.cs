using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Library
{
    public class BookCategory : TenantEntity
    {
        public string Name { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
