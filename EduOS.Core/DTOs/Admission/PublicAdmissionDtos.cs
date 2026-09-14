using EduOS.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Admission;

public sealed class PublicAdmissionStatusQueryDto
{
    [Required, StringLength(30, MinimumLength = 8)]
    public string Mobile { get; set; } = string.Empty;
}

public sealed class PublicAdmissionStatusDto
{
    public Guid Reference { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;
    public string MaskedMobile { get; set; } = string.Empty;
    public AdmissionApplicationStatus Status { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public string? DecisionNote { get; set; }
}
