using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>AcademicBatch is campus, year, program and level scoped. Validate uniqueness in that real scope.</summary>
public interface IAcademicBatchRepository : IGenericRepository<AcademicBatch>
{
    Task<(List<AcademicBatch> Items, int TotalCount)> GetByAcademicLevelAsync(long academicLevelId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<bool> IsBatchCodeExistsAsync(long tenantId, long campusId, long academicYearId, string code, long? excludeId = null, CancellationToken cancellationToken = default);
    Task<int> GetTotalCapacityAsync(long tenantId, long campusId, long academicYearId, long academicLevelId, CancellationToken cancellationToken = default);
}
