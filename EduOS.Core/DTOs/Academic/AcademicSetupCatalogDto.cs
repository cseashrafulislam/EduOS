namespace EduOS.Core.DTOs.Academic;

/// <summary>Bounded setup suggestions for small dropdowns; every list is capped by the requested take value.</summary>
public sealed class AcademicSetupCatalogDto
{
    public IReadOnlyList<AcademicProgramDto> Programs { get; set; } = Array.Empty<AcademicProgramDto>();
    public IReadOnlyList<AcademicLevelDto> Levels { get; set; } = Array.Empty<AcademicLevelDto>();
    public IReadOnlyList<AcademicTrackDto> Tracks { get; set; } = Array.Empty<AcademicTrackDto>();
    public IReadOnlyList<SubjectDto> Subjects { get; set; } = Array.Empty<SubjectDto>();
    public IReadOnlyList<AcademicCurriculumDto> Curricula { get; set; } = Array.Empty<AcademicCurriculumDto>();
    public IReadOnlyList<CurriculumSubjectDto> CurriculumSubjects { get; set; } = Array.Empty<CurriculumSubjectDto>();
    public IReadOnlyList<AcademicBatchDto> Batches { get; set; } = Array.Empty<AcademicBatchDto>();
    public IReadOnlyList<RoomDto> Rooms { get; set; } = Array.Empty<RoomDto>();
}
