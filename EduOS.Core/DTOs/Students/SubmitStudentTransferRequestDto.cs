using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Students;

/// <summary>Extends the canonical transfer request with optimistic concurrency proofs.</summary>
public sealed class SubmitStudentTransferRequestDto : CreateTransferRequestDto
{
    [Required] public string StudentRowVersion { get; set; } = string.Empty;
    [Required] public string EnrollmentRowVersion { get; set; } = string.Empty;
}
