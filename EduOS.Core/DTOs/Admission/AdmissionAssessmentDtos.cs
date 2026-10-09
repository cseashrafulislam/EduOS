using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Admission;

public class SaveAdmissionTestDto
{
    public Guid? AdmissionIntakeFormReference { get; set; }
    public string? RowVersion { get; set; }

    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long AcademicYearId { get; set; }

    [Range(1, long.MaxValue)]
    public long CampusId { get; set; }

    [Range(1, long.MaxValue)]
    public long AcademicLevelId { get; set; }

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

public class AdmissionMeritListDto
{
    public AdmissionTestDto Test { get; set; } = new();
    public List<AdmissionResultDto> Results { get; set; } = new();
}
