using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.SaaS;

namespace EduOS.Core.Entities.Admission;

public class AdmissionTest : BaseTenantEntity
{
    public string Name { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public long CampusId { get; set; }
    public long AcademicUnitId { get; set; }
    public DateTime TestDate { get; set; }
    public decimal TotalMarks { get; set; }
    public decimal PassMarks { get; set; }
    public string? Venue { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public bool IsPublished { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public long? PublishedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual AcademicYear? AcademicYear { get; set; }
    public virtual Campus? Campus { get; set; }
    public virtual Class? AcademicUnit { get; set; }
}
