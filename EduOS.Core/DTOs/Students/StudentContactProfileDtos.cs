using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Students;

public sealed class StudentContactProfileDto
{
    public Guid StudentReference { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string PreferredLanguage { get; set; } = "bn-BD";
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class UpdateStudentContactProfileRequestDto
{
    [Required] public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    [Required, MaxLength(20)] public string PreferredLanguage { get; set; } = "bn-BD";
    [Required, StringLength(500, MinimumLength = 10)] public string Reason { get; set; } = string.Empty;
}
