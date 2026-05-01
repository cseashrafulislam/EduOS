using EduOS.Core.Entities.Exams;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IResultRepository : IGenericRepository<ExamResult>
    {
        Task<ExamResult?> GetByExamAndStudentAsync(int examId, int studentId);
        Task<List<ExamResult>> GetByExamAndClassAsync(int examId, int classId);
        Task<List<ExamResult>> GetTopRankersAsync(int examId, int classId, int top = 10);
    }
}
