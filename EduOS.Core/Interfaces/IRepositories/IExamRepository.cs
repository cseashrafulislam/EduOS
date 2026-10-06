using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IExamRepository : IGenericRepository<Assessment>
{
    Task<List<Assessment>> GetByYearAsync(long academicYearId);
    Task<List<Assessment>> GetPublishedAsync(long academicYearId);
    Task<Assessment?> GetWithSchedulesAsync(long id);
}
