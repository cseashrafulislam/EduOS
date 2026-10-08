using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;

namespace EduOS.Core.Interfaces.IServices;

public interface IAccountingReportingService
{
    Task<ApiResponse<LedgerStatementDto>> GetLedgerStatementAsync(long accountId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<TrialBalanceRowDto>>> GetTrialBalanceAsync(DateOnly asOfDate, CancellationToken cancellationToken = default);
}
