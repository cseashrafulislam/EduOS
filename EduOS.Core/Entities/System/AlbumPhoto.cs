using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class AlbumPhoto : BaseEntity
    {
        public int AlbumId { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public string? Caption { get; set; }
        public DateTime UploadedAt { get; set; }

        public virtual Album? Album { get; set; }
    }
}
