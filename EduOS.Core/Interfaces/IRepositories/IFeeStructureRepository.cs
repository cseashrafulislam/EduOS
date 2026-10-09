using EduOS.Core.Entities.Finance;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Resolve applicable fee structures by the canonical program/level/batch and effective business date. Frequency belongs to fee lines.</summary>
public interface IFeeStructureRepository : IGenericRepository<FeeStructure>
{
    Task<List<FeeStructure>> GetApplicableAsync(long campusId, long academicProgramId, long academicLevelId, long academicYearId, long? academicBatchId, DateOnly effectiveOn, CancellationToken cancellationToken = default);
}
