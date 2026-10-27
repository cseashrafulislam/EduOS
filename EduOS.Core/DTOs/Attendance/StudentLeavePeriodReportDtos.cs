using System.ComponentModel.DataAnnotations;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.Attendance;

/// <summary>Manager-only report scoped to the enrollment that received each leave request.</summary>
public sealed class StudentLeavePeriodReportQueryDto
{
    [Range(1, long.MaxValue)] public long AcademicBatchId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public LeaveState? State { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public sealed class StudentLeavePeriodReportRowDto
{
    public long ApplicationId { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public string RollNo { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    /// <summary>Current student display name, not a historical name snapshot.</summary>
    public string StudentName { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    /// <summary>Calendar days overlapping the requested report period, inclusive.</summary>
    public int PeriodDays { get; set; }
    public LeaveState State { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
