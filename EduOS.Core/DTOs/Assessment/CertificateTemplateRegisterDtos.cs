using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

public sealed class CertificateTemplateRegisterQueryDto
{
    [MaxLength(100)] public string? Search { get; set; }
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}
public sealed class CertificateTemplateRegisterRowDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public bool IsLocked { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class CertificateTemplateRegisterPageDto
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<CertificateTemplateRegisterRowDto> Items { get; set; } =
        Array.Empty<CertificateTemplateRegisterRowDto>();
}
