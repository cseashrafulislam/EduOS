namespace EduOS.Core.DTOs.SaaS;

public sealed class PlatformInstitutionTypeAdminDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
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
