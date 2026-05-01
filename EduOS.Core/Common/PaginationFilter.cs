namespace EduOS.Core.Common
{
    public class PaginationFilter
    {
        private int _pageSize = 10;
        public int Page { get; set; } = 1;
        public int PageSize 
        { 
            get => _pageSize; 
            set => _pageSize = value > 100 ? 100 : value; 
        }
        public string? SearchTerm { get; set; }
        public string? SortBy { get; set; }
        public string SortDirection { get; set; } = "asc";
    }
}
