using EduOS.Core.Entities.Exams;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IMarkEntryRepository : IGenericRepository<MarkEntry>
    {
        Task<List<MarkEntry>> GetByExamAndStudentAsync(int examId, int studentId);
        Task<List<MarkEntry>> GetByExamAndSubjectAsync(int examId, int subjectId, int classId);
        Task<MarkEntry?> GetExistingAsync(int examId, int studentId, int subjectId);
        Task<bool> IsAllMarksEnteredAsync(int examId, int classId);
    }
}
