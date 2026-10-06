using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IMarkEntryRepository : IGenericRepository<StudentAssessmentMark>
{
    Task<List<StudentAssessmentMark>> GetByExamAndStudentAsync(long examId, long studentId);
    Task<List<StudentAssessmentMark>> GetByExamAndSubjectAsync(long examId, long subjectId, long classId);
    Task<StudentAssessmentMark?> GetExistingAsync(long examId, long studentId, long subjectId);
    Task<bool> IsAllMarksEnteredAsync(long examId, long classId);
}
