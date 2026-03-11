using System;
using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Academic
{
    public class Student : TenantEntity
    {
        public string StudentCode { get; set; }
        public string FullName { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string Gender { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public int? ClassId { get; set; }
        public int? SectionId { get; set; }
        public int? AcademicYearId { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
