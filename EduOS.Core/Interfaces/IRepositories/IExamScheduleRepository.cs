using EduOS.Core.Entities.Exams;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IExamScheduleRepository : IGenericRepository<ExamSchedule>
    {
        Task<List<ExamSchedule>> GetByExamAndClassAsync(int examId, int classId);
        Task<List<ExamSchedule>> GetByDateAsync(DateTime date, int tenantId);
    }
}
