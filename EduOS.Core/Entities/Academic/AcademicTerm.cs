using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Academic
{
    /// <summary>
    /// A term/semester within an academic year.
    /// Example: "Term 1", "First Semester", "January Term"
    /// Optional — institutions that don't use terms can skip.
    /// </summary>
    public class AcademicTerm : BaseTenantEntity
    {
        public long AcademicYearId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; }

        // Navigation
        public virtual AcademicYear? AcademicYear { get; set; }
    }
}
