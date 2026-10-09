using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Grade thresholds have meaning only within a particular GradeScheme, not a whole tenant.</summary>
public interface IGradeRuleRepository : IGenericRepository<GradeRule>
{
    Task<List<GradeRule>> GetByGradeSchemeAsync(long gradeSchemeId, CancellationToken cancellationToken = default);
    Task<GradeRule?> ResolveGradeAsync(long gradeSchemeId, decimal normalizedMarks, CancellationToken cancellationToken = default);
}
