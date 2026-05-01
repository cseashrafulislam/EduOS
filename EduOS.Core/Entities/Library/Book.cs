using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Library
{
    public class Book : BaseTenantEntity
    {
        public string Title { get; set; } = string.Empty;
        public string? Author { get; set; }
        public string? Publisher { get; set; }
        public string? ISBN { get; set; }
        public string? Category { get; set; }
        public string? Edition { get; set; }
        public int TotalCopies { get; set; }
        public int AvailableCopies { get; set; }
        public string? ShelfNo { get; set; }
        public decimal? Price { get; set; }
        public string? CoverImageUrl { get; set; }
    }
}
