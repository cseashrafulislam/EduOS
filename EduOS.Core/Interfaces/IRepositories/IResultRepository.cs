using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IResultRepository : IGenericRepository<StudentResultSummary>
{
    Task<StudentResultSummary?> GetByExamAndStudentAsync(long examId, long studentId);
    Task<List<StudentResultSummary>> GetByExamAndClassAsync(long examId, long classId);
    Task<List<StudentResultSummary>> GetTopRankersAsync(long examId, long classId, int top = 10);
}
