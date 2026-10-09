using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IAssessmentRepository : IGenericRepository<Assessment>
{
    Task<List<Assessment>> GetByYearAsync(long academicYearId);
    Task<List<Assessment>> GetPublishedAsync(long academicYearId);
}
