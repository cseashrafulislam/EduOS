namespace EduOS.Core.DTOs.Assessment;

public sealed class AssessmentSetupListQueryDto
{
    public string? Search { get; set; }
    public long? CampusId { get; set; }
    public long? AcademicYearId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;
}

public sealed class AssessmentSetupListItemDto
{
    public Guid Reference { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public AssessmentState State { get; set; }
    public AssessmentAttemptMode AttemptMode { get; set; }
}

public sealed class AssessmentSetupBatchDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
}

public sealed class AssessmentSetupOfferingDto
{
    public Guid Reference { get; set; }
    public string Code { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
}

public sealed class AssessmentSetupReferenceDataDto
{
    public IReadOnlyList<AssessmentSetupCampusDto> Campuses { get; set; } = [];
    public IReadOnlyList<AssessmentSetupYearDto> AcademicYears { get; set; } = [];
    public IReadOnlyList<AssessmentSetupTermDto> AcademicTerms { get; set; } = [];
}

public sealed class AssessmentSetupCampusDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class AssessmentSetupYearDto
{
    public long Id { get; set; }
    public long? CampusId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class AssessmentSetupTermDto
{
    public long Id { get; set; }
    public long AcademicYearId { get; set; }
    public string Name { get; set; } = string.Empty;
}
