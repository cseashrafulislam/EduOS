using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IStudentResultSummaryRepository : IGenericRepository<StudentResultSummary>
{
    Task<StudentResultSummary?> GetByAssessmentAndStudentAsync(long assessmentId, long studentId);
    Task<List<StudentResultSummary>> GetByAssessmentAndLevelAsync(long assessmentId, long academicLevelId);
    Task<List<StudentResultSummary>> GetTopAssessmentRankersAsync(long assessmentId, long academicLevelId, int top = 10);
}
