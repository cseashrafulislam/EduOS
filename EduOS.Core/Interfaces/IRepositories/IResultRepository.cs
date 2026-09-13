using EduOS.Core.Entities.Exams;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IResultRepository : IGenericRepository<ExamResult>
    {
        Task<ExamResult?> GetByExamAndStudentAsync(long examId, long studentId);
        Task<List<ExamResult>> GetByExamAndClassAsync(long examId, long classId);
        Task<List<ExamResult>> GetTopRankersAsync(long examId, long classId, int top = 10);
    }
}
