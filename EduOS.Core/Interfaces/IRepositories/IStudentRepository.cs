using EduOS.Core.Entities.Students;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Queries student identity and enrollment membership without duplicating guardian or enrollment ownership.</summary>
public interface IStudentRepository : IGenericRepository<Student>
{
    Task<Student?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<Student?> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default);
    Task<List<Student>> GetByAcademicBatchAsync(long academicBatchId, bool currentOnly = true, CancellationToken cancellationToken = default);
    Task<List<Student>> GetByAcademicYearAsync(long academicYearId, CancellationToken cancellationToken = default);
    Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null, CancellationToken cancellationToken = default);
    Task<bool> IsRollAssignedInBatchAsync(string rollNo, long academicBatchId, long? excludeEnrollmentId = null, CancellationToken cancellationToken = default);
    Task<int> GetActiveCountAsync(long tenantId, CancellationToken cancellationToken = default);
}
