using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Mark identity is (AssessmentSubjectId, StudentSubjectRegistrationId); never key mark edits only by student and date.</summary>
public interface IStudentAssessmentMarkRepository : IGenericRepository<StudentAssessmentMark>
{
    Task<List<StudentAssessmentMark>> GetByAssessmentAndEnrollmentAsync(long assessmentId, long studentEnrollmentId, CancellationToken cancellationToken = default);
    Task<(List<StudentAssessmentMark> Items, int TotalCount)> GetByAssessmentSubjectAsync(long assessmentSubjectId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<StudentAssessmentMark?> GetExistingAsync(long assessmentSubjectId, long studentSubjectRegistrationId, CancellationToken cancellationToken = default);
    Task<bool> AreRequiredMarksEnteredAsync(long assessmentId, long academicBatchId, CancellationToken cancellationToken = default);
}
