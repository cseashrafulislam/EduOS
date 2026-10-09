using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Current academic year is campus-scoped; implementations must enforce uniqueness per tenant/campus in a transaction.</summary>
public interface IAcademicYearRepository : IGenericRepository<AcademicYear>
{
    Task<AcademicYear?> GetCurrentAsync(long tenantId, long? campusId, CancellationToken cancellationToken = default);
    Task<bool> IsNameExistsAsync(string name, long tenantId, long? campusId, long? excludeId = null, CancellationToken cancellationToken = default);
    Task<List<AcademicYear>> GetActiveYearsAsync(long tenantId, long? campusId, CancellationToken cancellationToken = default);
    Task SetCurrentAsync(long academicYearId, long tenantId, long? campusId, CancellationToken cancellationToken = default);
}
