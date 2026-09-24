using EduOS.Core.Entities.Exams;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IExamScheduleRepository : IGenericRepository<ExamSchedule>
    {
        Task<List<ExamSchedule>> GetByExamAndClassAsync(long examId, long classId);
        Task<List<ExamSchedule>> GetByDateAsync(DateTime date, long tenantId);
    }
}
