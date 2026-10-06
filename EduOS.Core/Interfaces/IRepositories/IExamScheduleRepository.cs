using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IExamScheduleRepository : IGenericRepository<AssessmentSchedule>
{
    Task<List<AssessmentSchedule>> GetByExamAndClassAsync(long examId, long classId);
    Task<List<AssessmentSchedule>> GetByDateAsync(DateTime date, long tenantId);
}
