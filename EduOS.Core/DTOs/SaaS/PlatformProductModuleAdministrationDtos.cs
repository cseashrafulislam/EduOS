namespace EduOS.Core.DTOs.SaaS;

public sealed class PlatformProductModuleAdminDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsCore { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SavePlatformProductModuleRequestDto
{
    public Guid RequestId { get; set; }
    [global::System.ComponentModel.DataAnnotations.Required]
    public string RowVersion { get; set; } = string.Empty;
    [global::System.ComponentModel.DataAnnotations.Required]
    [global::System.ComponentModel.DataAnnotations.MaxLength(150)]
    public string Name { get; set; } = string.Empty;
    [global::System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    [global::System.ComponentModel.DataAnnotations.Range(0, 100000)]
    public int DisplayOrder { get; set; }
}
