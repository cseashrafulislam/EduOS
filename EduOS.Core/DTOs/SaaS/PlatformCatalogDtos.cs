namespace EduOS.Core.DTOs.SaaS;

public class InstitutionTypeListItemDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameBangla { get; set; }
    public string? Description { get; set; }
    public string AcademicCycleType { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public class InstitutionTypeDetailDto : InstitutionTypeListItemDto
{
    public IReadOnlyDictionary<string, string> Terminology { get; set; } =
        new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> DefaultSettings { get; set; } =
        new Dictionary<string, string>();
    public List<InstitutionTypeModuleDto> Modules { get; set; } = new();
}

public class ProductModuleDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameBangla { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public string? RoutePrefix { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsCore { get; set; }
}

public class InstitutionTypeModuleDto : ProductModuleDto
{
    public bool IsRequired { get; set; }
    public bool IsEnabledByDefault { get; set; }
}
