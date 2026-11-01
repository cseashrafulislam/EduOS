using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Alumni;

// Employer verification is a campus-owned Alumni career-network workflow.
// It does not import student contacts, post Finance GL or issue Inventory.
public sealed class CareerEmployerDraftDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    [Required, StringLength(160, MinimumLength = 3)] public string Name { get; set; } = "";
    [StringLength(300)] public string? Website { get; set; }
}
public sealed class CareerEmployerVerifyDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = "";
}
public sealed class CareerEmployerQueryDto
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 20;
    [Range(0, 2)] public int Status { get; set; }
    [StringLength(100)] public string? Search { get; set; }
}
public sealed class CareerEmployerItemDto
{
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public string Name { get; set; } = "";
    public string? Website { get; set; }
    public int Status { get; set; }
    public string RowVersion { get; set; } = "";
    public long CreatedByUserId { get; set; }
    public bool CanVerify { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
