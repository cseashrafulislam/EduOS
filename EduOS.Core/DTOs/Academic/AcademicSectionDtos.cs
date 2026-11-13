using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Academic;

public sealed class AcademicSectionDto
{
    public long Id { get; set; }
    public long AcademicBatchId { get; set; }
    public string AcademicBatchName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CreateAcademicSectionRequestDto
{
    public Guid ClientRequestId { get; set; }
    public bool IsActive { get; set; } = true;
    [Range(1, long.MaxValue)] public long AcademicBatchId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Range(0, 100000)] public int Capacity { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class UpdateAcademicSectionRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Range(0, 100000)] public int Capacity { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class DeleteAcademicSectionRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
