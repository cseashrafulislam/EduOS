namespace EduOS.Core.DTOs.SaaS;

public sealed class PlatformInstitutionTypeAdminDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameBangla { get; set; }
    public string? Description { get; set; }
    public string AcademicCycleType { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ChangeInstitutionTypeActivationRequestDto
{
    public Guid RequestId { get; set; }
    public bool IsActive { get; set; }
    [global::System.ComponentModel.DataAnnotations.Required]
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveInstitutionTypeMetadataRequestDto
{
    public Guid RequestId { get; set; }
    [global::System.ComponentModel.DataAnnotations.Required]
    public string RowVersion { get; set; } = string.Empty;
    [global::System.ComponentModel.DataAnnotations.Required]
    [global::System.ComponentModel.DataAnnotations.MaxLength(150)]
    public string Name { get; set; } = string.Empty;
    [global::System.ComponentModel.DataAnnotations.MaxLength(150)]
    public string? NameBangla { get; set; }
    [global::System.ComponentModel.DataAnnotations.MaxLength(1000)]
    public string? Description { get; set; }
    [global::System.ComponentModel.DataAnnotations.Required]
    public string AcademicCycleType { get; set; } = string.Empty;
    [global::System.ComponentModel.DataAnnotations.Range(0, 100000)]
    public int DisplayOrder { get; set; }
}
