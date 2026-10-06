using EduOS.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Student;

public sealed class PromoteStudentRequestDto
{
    public Guid ClientRequestId { get; set; }

    [Range(1, long.MaxValue)]
    public long SourceEnrollmentId { get; set; }

    [Range(1, long.MaxValue)]
    public long TargetAcademicYearId { get; set; }

    [Range(1, long.MaxValue)]
    public long TargetClassId { get; set; }

    [Range(1, long.MaxValue)]
    public long TargetSectionId { get; set; }

    public long? TargetGroupId { get; set; }

    [Required, StringLength(50)]
    public string TargetRoll { get; set; } = string.Empty;

    public StudentProgressionDecision Decision { get; set; }
    public string StudentRowVersion { get; set; } = string.Empty;
    public string SourceEnrollmentRowVersion { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Note { get; set; }
}

public sealed class StudentPromotionResultDto
{
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public long FromEnrollmentId { get; set; }
    public long ToEnrollmentId { get; set; }
    public StudentProgressionDecision Decision { get; set; }
    public long AcademicYearId { get; set; }
    public long ClassId { get; set; }
    public long SectionId { get; set; }
    public long? GroupId { get; set; }
    public string Roll { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
    public string StudentRowVersion { get; set; } = string.Empty;
    public bool AlreadyProcessed { get; set; }
}

public sealed class StudentPromotionHistoryDto
{
    public Guid Reference { get; set; }
    public StudentProgressionDecision Decision { get; set; }
    public long FromAcademicYearId { get; set; }
    public long ToAcademicYearId { get; set; }
    public long FromClassId { get; set; }
    public long ToClassId { get; set; }
    public long FromSectionId { get; set; }
    public long ToSectionId { get; set; }
    public long? FromGroupId { get; set; }
    public long? ToGroupId { get; set; }
    public string FromRoll { get; set; } = string.Empty;
    public string ToRoll { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
    public string? Note { get; set; }
}
