using EduOS.Core.Entities.Exams;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IMarkEntryRepository : IGenericRepository<MarkEntry>
    {
        Task<List<MarkEntry>> GetByExamAndStudentAsync(long examId, long studentId);
        Task<List<MarkEntry>> GetByExamAndSubjectAsync(long examId, long subjectId, long classId);
        Task<MarkEntry?> GetExistingAsync(long examId, long studentId, long subjectId);
        Task<bool> IsAllMarksEnteredAsync(long examId, long classId);
    }
}
