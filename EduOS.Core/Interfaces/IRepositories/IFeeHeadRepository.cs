using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>FeeHead has DefaultFrequency, not an editable string Type. Financial posting resolves frequency from FeeStructureLine.</summary>
public interface IFeeHeadRepository : IGenericRepository<FeeHead>
{
    Task<List<FeeHead>> GetActiveAsync(long tenantId, CancellationToken cancellationToken = default);
    Task<List<FeeHead>> GetByDefaultFrequencyAsync(long tenantId, FeeFrequencyType frequency, CancellationToken cancellationToken = default);
}
