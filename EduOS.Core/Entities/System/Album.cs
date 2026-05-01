using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class Album : BaseTenantEntity
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime EventDate { get; set; }
        public string? CoverImageUrl { get; set; }
        public bool IsPublic { get; set; } = true;

        public virtual ICollection<AlbumPhoto> Photos { get; set; } = new List<AlbumPhoto>();
    }
}
