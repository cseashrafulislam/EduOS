using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IStudentAssessmentMarkRepository : IGenericRepository<StudentAssessmentMark>
{
    Task<List<StudentAssessmentMark>> GetByAssessmentAndStudentAsync(long assessmentId, long studentId);
    Task<List<StudentAssessmentMark>> GetByAssessmentSubjectAndLevelAsync(long assessmentId, long subjectId, long academicLevelId);
    Task<StudentAssessmentMark?> GetExistingAsync(long assessmentId, long studentId, long subjectId);
    Task<bool> AreAllMarksEnteredAsync(long assessmentId, long academicLevelId);
}
