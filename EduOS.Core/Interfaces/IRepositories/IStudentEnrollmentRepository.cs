using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Enrollment is the authoritative student-to-batch relationship; the level/year are attributes of the selected batch.</summary>
public interface IStudentEnrollmentRepository : IGenericRepository<StudentEnrollment>
{
    Task<List<StudentEnrollment>> GetByStudentIdAsync(long studentId, CancellationToken cancellationToken = default);
    Task<StudentEnrollment?> GetCurrentAsync(long studentId, CancellationToken cancellationToken = default);
    Task<(List<StudentEnrollment> Items, int TotalCount)> GetByAcademicBatchAsync(long academicBatchId, int page, int pageSize, CancellationToken cancellationToken = default);
}
