using AutoMapper;
using EduOS.Core.Common;
using EduOS.Core.DTOs.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using global::System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeOpenXml; // For Excel export (EPPlus package)
using System.Text;

namespace EduOS.Service.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<AuditLogService> _logger;
        private readonly IMapper _mapper;

        public AuditLogService(
            IAuditLogRepository auditLogRepository,
            ICurrentUserService currentUser,
            ILogger<AuditLogService> logger,
            IMapper mapper)
        {
            _auditLogRepository = auditLogRepository;
            _currentUser = currentUser;
            _logger = logger;
            _mapper = mapper;
        }

        public async Task<ApiResponse<PagedResult<AuditLogDto>>> GetAllAsync(AuditLogFilterDto filter)
        {
            try
            {
                var query = _auditLogRepository.GetQueryable()
                    .Where(a => a.TenantId == _currentUser.TenantId);

                // Apply filters
                if (filter.UserId.HasValue)
                    query = query.Where(a => a.UserId == filter.UserId.Value);

                if (!string.IsNullOrEmpty(filter.TableName))
                    query = query.Where(a => a.TableName == filter.TableName);

                if (!string.IsNullOrEmpty(filter.Action))
                    query = query.Where(a => a.Action == filter.Action);

                if (filter.FromDate.HasValue)
                    query = query.Where(a => a.CreatedAt >= filter.FromDate.Value);

                if (filter.ToDate.HasValue)
                    query = query.Where(a => a.CreatedAt <= filter.ToDate.Value);

                if (!string.IsNullOrEmpty(filter.IpAddress))
                    query = query.Where(a => a.IpAddress == filter.IpAddress);

                if (filter.IsSuccess.HasValue)
                    query = query.Where(a => a.IsSuccess == filter.IsSuccess.Value);

                var totalCount = await query.CountAsync();

                var items = await query
                    .OrderByDescending(a => a.CreatedAt)
                    .Skip((filter.Page - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .ToListAsync();

                var dtos = _mapper.Map<List<AuditLogDto>>(items);

                var result = new PagedResult<AuditLogDto>
                {
                    Items = dtos,
                    TotalCount = totalCount,
                    Page = filter.Page,
                    PageSize = filter.PageSize
                };

                return ApiResponse<PagedResult<AuditLogDto>>.SuccessResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching audit logs");
                return ApiResponse<PagedResult<AuditLogDto>>.ErrorResponse("Failed to fetch audit logs", 500);
            }
        }

        public async Task<ApiResponse<List<AuditLogDto>>> GetByRecordAsync(string tableName, int recordId)
        {
            try
            {
                var logs = await _auditLogRepository.GetByRecordIdAsync(
                    tableName, recordId, _currentUser.TenantId);

                var dtos = _mapper.Map<List<AuditLogDto>>(logs);
                return ApiResponse<List<AuditLogDto>>.SuccessResponse(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching audit logs for record {Table}/{Id}", tableName, recordId);
                return ApiResponse<List<AuditLogDto>>.ErrorResponse("Failed to fetch audit logs", 500);
            }
        }

        public async Task<ApiResponse<List<AuditLogDto>>> GetByUserAsync(long userId)
        {
            try
            {
                var logs = await _auditLogRepository.GetByUserIdAsync(userId, _currentUser.TenantId);

                var dtos = _mapper.Map<List<AuditLogDto>>(logs);
                return ApiResponse<List<AuditLogDto>>.SuccessResponse(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching audit logs for user {UserId}", userId);
                return ApiResponse<List<AuditLogDto>>.ErrorResponse("Failed to fetch audit logs", 500);
            }
        }

        public async Task<ApiResponse<List<AuditLogDto>>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            try
            {
                var logs = await _auditLogRepository.GetQueryable()
                    .Where(a => a.TenantId == _currentUser.TenantId
                        && a.CreatedAt >= fromDate
                        && a.CreatedAt <= toDate)
                    .OrderByDescending(a => a.CreatedAt)
                    .ToListAsync();

                var dtos = _mapper.Map<List<AuditLogDto>>(logs);
                return ApiResponse<List<AuditLogDto>>.SuccessResponse(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching audit logs for date range");
                return ApiResponse<List<AuditLogDto>>.ErrorResponse("Failed to fetch audit logs", 500);
            }
        }

        public async Task<ApiResponse<List<AuditLogDto>>> GetByActionAsync(string action)
        {
            try
            {
                var logs = await _auditLogRepository.GetQueryable()
                    .Where(a => a.TenantId == _currentUser.TenantId
                        && a.Action == action)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(100)
                    .ToListAsync();

                var dtos = _mapper.Map<List<AuditLogDto>>(logs);
                return ApiResponse<List<AuditLogDto>>.SuccessResponse(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching audit logs for action {Action}", action);
                return ApiResponse<List<AuditLogDto>>.ErrorResponse("Failed to fetch audit logs", 500);
            }
        }

        public async Task<ApiResponse<List<AuditLogDto>>> GetByTableNameAsync(string tableName)
        {
            try
            {
                var logs = await _auditLogRepository.GetByTableNameAsync(tableName, _currentUser.TenantId);

                var dtos = _mapper.Map<List<AuditLogDto>>(logs);
                return ApiResponse<List<AuditLogDto>>.SuccessResponse(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching audit logs for table {Table}", tableName);
                return ApiResponse<List<AuditLogDto>>.ErrorResponse("Failed to fetch audit logs", 500);
            }
        }

        public async Task<ApiResponse<AuditLogStatisticsDto>> GetStatisticsAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                var today = new DateTime(now.Year, now.Month, now.Day);
                var weekAgo = now.AddDays(-7);
                var monthAgo = now.AddMonths(-1);

                var query = _auditLogRepository.GetQueryable()
                    .Where(a => a.TenantId == _currentUser.TenantId);

                var stats = new AuditLogStatisticsDto
                {
                    TotalLogs = await query.CountAsync(),
                    TodayLogs = await query.Where(a => a.CreatedAt >= today).CountAsync(),
                    ThisWeekLogs = await query.Where(a => a.CreatedAt >= weekAgo).CountAsync(),
                    ThisMonthLogs = await query.Where(a => a.CreatedAt >= monthAgo).CountAsync(),

                    CreateActions = await query.Where(a => a.Action == "Create").CountAsync(),
                    UpdateActions = await query.Where(a => a.Action == "Update").CountAsync(),
                    DeleteActions = await query.Where(a => a.Action == "Delete").CountAsync(),

                    SuccessfulOperations = await query.Where(a => a.IsSuccess).CountAsync(),
                    FailedOperations = await query.Where(a => !a.IsSuccess).CountAsync(),

                    TopTables = await query
                        .GroupBy(a => a.TableName)
                        .OrderByDescending(g => g.Count())
                        .Take(10)
                        .Select(g => new TableActivityDto
                        {
                            TableName = g.Key,
                            ActivityCount = g.Count()
                        })
                        .ToListAsync(),

                    TopUsers = await query
                        .Where(a => a.UserId.HasValue)
                        .GroupBy(a => new { a.UserId, a.UserName })
                        .OrderByDescending(g => g.Count())
                        .Take(10)
                        .Select(g => new UserActivityDto
                        {
                            UserId = g.Key.UserId.HasValue ? g.Key.UserId.Value : 0,
                            UserName = g.Key.UserName,
                            ActivityCount = g.Count()
                        })
                        .ToListAsync()
                };

                return ApiResponse<AuditLogStatisticsDto>.SuccessResponse(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching audit log statistics");
                return ApiResponse<AuditLogStatisticsDto>.ErrorResponse("Failed to fetch statistics", 500);
            }
        }

        public async Task<ApiResponse<byte[]>> ExportAsync(AuditLogFilterDto filter)
        {
            try
            {
                var query = _auditLogRepository.GetQueryable()
                    .Where(a => a.TenantId == _currentUser.TenantId);

                // Apply filters (same as GetAllAsync)
                if (filter.UserId.HasValue)
                    query = query.Where(a => a.UserId == filter.UserId.Value);

                if (!string.IsNullOrEmpty(filter.TableName))
                    query = query.Where(a => a.TableName == filter.TableName);

                if (!string.IsNullOrEmpty(filter.Action))
                    query = query.Where(a => a.Action == filter.Action);

                if (filter.FromDate.HasValue)
                    query = query.Where(a => a.CreatedAt >= filter.FromDate.Value);

                if (filter.ToDate.HasValue)
                    query = query.Where(a => a.CreatedAt <= filter.ToDate.Value);

                var logs = await query
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(10000) // Limit export to 10000 records
                    .ToListAsync();

                // Generate Excel file
                //ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("Audit Logs");

                // Headers
                worksheet.Cells[1, 1].Value = "Date";
                worksheet.Cells[1, 2].Value = "User";
                worksheet.Cells[1, 3].Value = "Action";
                worksheet.Cells[1, 4].Value = "Table";
                worksheet.Cells[1, 5].Value = "Record ID";
                worksheet.Cells[1, 6].Value = "IP Address";
                worksheet.Cells[1, 7].Value = "Success";

                // Data
                for (int i = 0; i < logs.Count; i++)
                {
                    var log = logs[i];
                    var row = i + 2;

                    worksheet.Cells[row, 1].Value = log.CreatedAt;
                    worksheet.Cells[row, 2].Value = log.UserName;
                    worksheet.Cells[row, 3].Value = log.Action;
                    worksheet.Cells[row, 4].Value = log.TableName;
                    worksheet.Cells[row, 5].Value = log.RecordId;
                    worksheet.Cells[row, 6].Value = log.IpAddress;
                    worksheet.Cells[row, 7].Value = log.IsSuccess ? "Yes" : "No";
                }

                // Auto-fit columns
                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

                return ApiResponse<byte[]>.SuccessResponse(
                    package.GetAsByteArray(),
                    "Audit logs exported successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting audit logs");
                return ApiResponse<byte[]>.ErrorResponse("Failed to export audit logs", 500);
            }
        }

        public async Task<ApiResponse<int>> DeleteOldLogsAsync(DateTime olderThan)
        {
            try
            {
                // Only SuperAdmin can delete audit logs
                if (!_currentUser.IsSuperAdmin)
                {
                    return ApiResponse<int>.ErrorResponse("Only SuperAdmin can delete audit logs", 403);
                }

                var oldLogs = await _auditLogRepository.GetQueryable()
                    .Where(a => a.CreatedAt < olderThan)
                    .ToListAsync();

                _auditLogRepository.DeleteRange(oldLogs);
                await _auditLogRepository.UnitOfWork.SaveChangesAsync();

                _logger.LogInformation("Deleted {Count} old audit logs older than {Date}",
                    oldLogs.Count, olderThan);

                return ApiResponse<int>.SuccessResponse(oldLogs.Count,
                    $"Deleted {oldLogs.Count} audit logs");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting old audit logs");
                return ApiResponse<int>.ErrorResponse("Failed to delete audit logs", 500);
            }
        }
    }
}

