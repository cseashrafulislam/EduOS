using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Finance;

/// <summary>Prospective stop of a canonical discount assignment; issued invoices remain immutable.</summary>
public sealed class DeactivateStudentDiscountRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
