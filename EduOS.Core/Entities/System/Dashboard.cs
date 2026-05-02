using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class Dashboard : BaseEntity
    {
        public long UserId { get; set; }
        public string WidgetType { get; set; } = string.Empty;
        public int Position { get; set; }
        public string? Configuration { get; set; } // JSON
        public bool IsVisible { get; set; } = true;

        public virtual ApplicationUser? User { get; set; }
    }
}
