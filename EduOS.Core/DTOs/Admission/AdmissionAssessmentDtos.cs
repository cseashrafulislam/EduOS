using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Admission;

public class SaveAdmissionTestDto
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long AcademicYearId { get; set; }

    [Range(1, long.MaxValue)]
    public long CampusId { get; set; }

    [Range(1, long.MaxValue)]
    public long AcademicUnitId { get; set; }

    public DateTime TestDate { get; set; }

    [Range(typeof(decimal), "0.01", "1000000")]
    public decimal TotalMarks { get; set; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal PassMarks { get; set; }

    [StringLength(200)]
    public string? Venue { get; set; }

    [Range(1, 1440)]
    public int DurationMinutes { get; set; } = 60;
}

public class SaveAdmissionResultItemDto
{
    [Range(1, long.MaxValue)]
    public long ApplicantId { get; set; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal ObtainedMarks { get; set; }

    [StringLength(20)]
    public string? Grade { get; set; }

    [StringLength(500)]
    public string? Remarks { get; set; }
}

public class SaveAdmissionResultsDto
{
    [Required, MinLength(1)]
    public List<SaveAdmissionResultItemDto> Results { get; set; } = new();
}

public class AdmissionTestDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public long CampusId { get; set; }
    public long AcademicUnitId { get; set; }
    public DateTime TestDate { get; set; }
    public decimal TotalMarks { get; set; }
    public decimal PassMarks { get; set; }
    public string? Venue { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public class AdmissionResultDto
{
    public long Id { get; set; }
    public long AdmissionTestId { get; set; }
    public long ApplicantId { get; set; }
    public Guid ApplicantReference { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;
    public decimal ObtainedMarks { get; set; }
    public decimal Percentage { get; set; }
    public bool IsPassed { get; set; }
    public int? MeritPosition { get; set; }
    public string ResultStatus { get; set; } = string.Empty;
    public string? Grade { get; set; }
    public string? Remarks { get; set; }
}

public class AdmissionMeritListDto
{
    public AdmissionTestDto Test { get; set; } = new();
    public List<AdmissionResultDto> Results { get; set; } = new();
}
