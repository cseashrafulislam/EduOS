namespace EduOS.Core.DTOs.Assessment;

public sealed class GradeSchemeRegisterQueryDto
{
    public string? Search { get; set; }
    public long? AcademicProgramId { get; set; }
    public bool ActiveOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class GradeSchemeRegisterRowDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long? AcademicProgramId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public bool UseCreditWeightedGpa { get; set; }
    public int RuleCount { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class GradeSchemeRegisterPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<GradeSchemeRegisterRowDto> Items { get; set; } =
        Array.Empty<GradeSchemeRegisterRowDto>();
}
