using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Schedules belong to AssessmentSubject; the date is a DateOnly business date.</summary>
public interface IAssessmentScheduleRepository : IGenericRepository<AssessmentSchedule>
{
    Task<List<AssessmentSchedule>> GetByAssessmentSubjectAsync(long assessmentSubjectId, CancellationToken cancellationToken = default);
    Task<(List<AssessmentSchedule> Items, int TotalCount)> GetByDateAsync(DateOnly assessmentDate, int page, int pageSize, CancellationToken cancellationToken = default);
}
