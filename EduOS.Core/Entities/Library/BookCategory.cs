using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Library
{
    public class BookCategory : BaseTenantEntity
    {
        public string Name { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
