using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Assessment publishing is state-controlled; historical published student results are owned by ResultPublication.</summary>
public interface IAssessmentRepository : IGenericRepository<Assessment>
{
    Task<(List<Assessment> Items, int TotalCount)> GetByAcademicYearAsync(long academicYearId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<Assessment> Items, int TotalCount)> GetByStateAsync(long academicYearId, AssessmentState state, int page, int pageSize, CancellationToken cancellationToken = default);
}
