using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Attendance;

/// <summary>Finalized attendance by historical enrollment, never inferred from current enrollment.</summary>
public sealed class StudentAttendanceReportQueryDto
{
    [Range(1, long.MaxValue)] public long AcademicBatchId { get; set; }
    [Range(1, long.MaxValue)] public long? SubjectOfferingId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    // Optional SQL-side attendance risk register filters. A strict threshold excludes 75.00% at 75.
    [Range(1, 1000)] public int MinMarkedSessions { get; set; } = 1;
    [Range(typeof(decimal), "0", "100")] public decimal? MaxAttendancePercent { get; set; }
    public string Sort { get; set; } = "roll";
}

public sealed class StudentAttendanceReportRowDto
{
    public Guid StudentEnrollmentReference { get; set; }
    public string RollNo { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public int MarkedSessions { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int Leave { get; set; }
    public int Excused { get; set; }
    public decimal AttendancePercent => MarkedSessions == 0 ? 0 :
        Math.Round(100m * (Present + Late) / MarkedSessions, 2);
}

/// <summary>Distinct historical enrollments with finalized marks, before any risk threshold is selected.</summary>
public sealed class StudentAttendanceRiskSummaryDto
{
    public int EligibleEnrollments { get; set; }
    public int Below75 { get; set; }
    public int Below90 { get; set; }
    public int Below100 { get; set; }
}

/// <summary>Finalized attendance grouped by subject offering, excluding general sessions.</summary>
public sealed class StudentAttendanceSubjectSummaryDto
{
    public long SubjectOfferingId { get; set; }
    public string OfferingCode { get; set; } = string.Empty;
    public string SubjectCode { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public int Marked { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int Leave { get; set; }
    public int Excused { get; set; }
    public decimal AttendancePercent => Marked == 0 ? 0 :
        Math.Round(100m * (Present + Late) / Marked, 2);
}

/// <summary>Finalized attendance mark totals for a day; dates with no finalized marks are omitted.</summary>
public sealed class StudentAttendanceDailySummaryDto
{
    public DateOnly AttendanceDate { get; set; }
    public int Marked { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int Leave { get; set; }
    public int Excused { get; set; }
    public decimal AttendancePercent => Marked == 0 ? 0 :
        Math.Round(100m * (Present + Late) / Marked, 2);
}

/// <summary>Historical subject-offering picker for an authorized batch.</summary>
public sealed class StudentAttendanceReportSubjectDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string SubjectCode { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
}
