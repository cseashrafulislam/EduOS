using EduOS.Core.Common;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Student;

public class StudentDirectoryQueryDto
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    [StringLength(100)] public string? Search { get; set; }
    [Range(1, int.MaxValue)] public int? AcademicYearId { get; set; }
    [Range(1, int.MaxValue)] public int? AcademicUnitId { get; set; }
    [StringLength(30)] public string? Status { get; set; }
}

public class StudentDirectoryListItemDto
{
    public Guid Reference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string Roll { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? FullNameBangla { get; set; }
    public string MaskedMobile { get; set; } = string.Empty;
    public string AcademicYear { get; set; } = string.Empty;
    public string AcademicUnit { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class StudentGuardianDto
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

public class StudentEnrollmentDto
{
    public long Id { get; set; }
    public string AcademicYear { get; set; } = string.Empty;
    public string? AcademicTerm { get; set; }
    public string? Campus { get; set; }
    public string AcademicUnit { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string? Group { get; set; }
    public string Roll { get; set; } = string.Empty;
    public DateTime EnrollmentDate { get; set; }
    public bool IsActive { get; set; }
}

public class StudentDirectoryDetailsDto : StudentDirectoryListItemDto
{
    public DateTime DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string PreferredLanguage { get; set; } = string.Empty;
    public DateTime AdmissionDate { get; set; }
    public List<StudentGuardianDto> Guardians { get; set; } = new();
    public List<StudentEnrollmentDto> Enrollments { get; set; } = new();
}
