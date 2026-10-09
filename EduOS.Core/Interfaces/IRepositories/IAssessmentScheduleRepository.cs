using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IAssessmentScheduleRepository : IGenericRepository<AssessmentSchedule>
{
    Task<List<AssessmentSchedule>> GetByAssessmentAndLevelAsync(long assessmentId, long academicLevelId);
    Task<List<AssessmentSchedule>> GetByDateAsync(DateTime date, long tenantId);
}
