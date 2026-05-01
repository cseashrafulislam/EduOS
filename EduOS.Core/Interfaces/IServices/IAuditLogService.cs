using EduOS.Core.Common;
using EduOS.Core.DTOs.System;

namespace EduOS.Core.Interfaces.IServices
{
    public interface IAuditLogService
    {
        Task<ApiResponse<PagedResult<AuditLogDto>>> GetAllAsync(AuditLogFilterDto filter);
        Task<ApiResponse<List<AuditLogDto>>> GetByRecordAsync(string tableName, int recordId);
        Task<ApiResponse<List<AuditLogDto>>> GetByUserAsync(int userId);
        Task<ApiResponse<List<AuditLogDto>>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate);
        Task<ApiResponse<List<AuditLogDto>>> GetByActionAsync(string action);
        Task<ApiResponse<List<AuditLogDto>>> GetByTableNameAsync(string tableName);
        Task<ApiResponse<AuditLogStatisticsDto>> GetStatisticsAsync();
        Task<ApiResponse<byte[]>> ExportAsync(AuditLogFilterDto filter);
        Task<ApiResponse<int>> DeleteOldLogsAsync(DateTime olderThan);
    }
}
