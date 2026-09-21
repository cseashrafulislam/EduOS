using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Admission;

public sealed class AdmissionIntakeForm : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }

    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public long CampusId { get; set; }
    public long AcademicUnitId { get; set; }
    public DateTime OpensAtUtc { get; set; }
    public DateTime ClosesAtUtc { get; set; }
    public decimal ApplicationFee { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "BDT";

    public string FieldsJson { get; set; } = "[]";
    public string DocumentRequirementsJson { get; set; } = "[]";
    public AdmissionIntakeFormStatus Status { get; set; } = AdmissionIntakeFormStatus.Draft;
    public DateTime? PublishedAtUtc { get; set; }
    public long? PublishedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public long? ClosedByUserId { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual AcademicYear? AcademicYear { get; set; }
    public virtual AcademicTerm? AcademicTerm { get; set; }
    public virtual Campus? Campus { get; set; }
    public virtual Class? AcademicUnit { get; set; }
}
