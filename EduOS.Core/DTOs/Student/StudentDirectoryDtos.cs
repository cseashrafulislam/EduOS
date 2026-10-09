using EduOS.Core.Common;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Student;

public class StudentDirectoryQueryDto
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    [StringLength(100)] public string? Search { get; set; }
    [Range(1, long.MaxValue)] public long? AcademicYearId { get; set; }
    [Range(1, long.MaxValue)] public long? AcademicLevelId { get; set; }
    [StringLength(30)] public string? Status { get; set; }
}

public class StudentDirectoryListItemDto
{
    public Guid Reference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? FullNameBangla { get; set; }
    public string MaskedMobile { get; set; } = string.Empty;
    public string AcademicYear { get; set; } = string.Empty;
    public string AcademicLevelName { get; set; } = string.Empty;
    public string AcademicBatchName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class StudentDirectoryGuardianDto
{
    public Guid Reference { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameBangla { get; set; }
    public string Relation { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool IsPrimary { get; set; }
}

public class StudentDirectoryEnrollmentDto
{
    public long Id { get; set; }
    public string AcademicYear { get; set; } = string.Empty;
    public string? AcademicTerm { get; set; }
    public string? Campus { get; set; }
    public string AcademicLevelName { get; set; } = string.Empty;
    public string AcademicBatchName { get; set; } = string.Empty;
    public string? AcademicTrackName { get; set; }
    public string RollNo { get; set; } = string.Empty;
    public DateOnly EnrollmentDate { get; set; }
    public EnrollmentState State { get; set; }
}

public class StudentDirectoryDetailsDto : StudentDirectoryListItemDto
{
    public DateOnly? DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string PreferredLanguage { get; set; } = string.Empty;
    public DateOnly AdmissionDate { get; set; }
    public List<StudentDirectoryGuardianDto> Guardians { get; set; } = new();
    public List<StudentDirectoryEnrollmentDto> Enrollments { get; set; } = new();
}
