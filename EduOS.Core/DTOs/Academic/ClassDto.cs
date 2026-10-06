using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Core.DTOs.Academic
{
    public class ClassDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int NumericValue { get; set; }
        public bool IsActive { get; set; }
        public int TotalSections { get; set; }
        public int TotalSubjects { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ClassCreateDto
    {
        public string Name { get; set; } = string.Empty;
        public int NumericValue { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class ClassUpdateDto
    {
        public string Name { get; set; } = string.Empty;
        public int NumericValue { get; set; }
        public bool IsActive { get; set; }
    }

    public class ClassListFilterDto
    {
        public string? SearchTerm { get; set; }
        public bool? IsActive { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
