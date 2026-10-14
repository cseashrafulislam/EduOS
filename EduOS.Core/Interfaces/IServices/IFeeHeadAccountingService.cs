using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;

namespace EduOS.Core.Interfaces.IServices;

public interface IFeeHeadAccountingService
{
    Task<ApiResponse<FeeHeadAccountingSetupDto>> GetSetupAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<FeeHeadAccountingDto>> SaveHeadAsync(long? headId, SaveFeeHeadAccountingHeadRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<FeeHeadAccountingDto>> MapAccountsAsync(long headId, SaveFeeHeadAccountMappingRequestDto request, CancellationToken cancellationToken = default);
}
