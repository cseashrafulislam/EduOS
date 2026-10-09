using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Subject is an institution catalog; curriculum and level determine eligibility, not legacy Class/Group IDs.</summary>
public interface ISubjectRepository : IGenericRepository<Subject>
{
    Task<List<Subject>> GetByAcademicLevelAsync(long academicLevelId, CancellationToken cancellationToken = default);
    Task<List<Subject>> GetByCurriculumAndLevelAsync(long academicCurriculumId, long academicLevelId, CancellationToken cancellationToken = default);
    Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null, CancellationToken cancellationToken = default);
}
