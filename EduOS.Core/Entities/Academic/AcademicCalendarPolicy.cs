using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.SaaS;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic;

public class AcademicCalendarPolicy : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long AcademicYearId { get; set; }
    public long? CampusId { get; set; }
    public int WeekendDaysMask { get; set; }
    public bool IsActive { get; set; } = true;

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual AcademicYear? AcademicYear { get; set; }
    public virtual Campus? Campus { get; set; }
}
