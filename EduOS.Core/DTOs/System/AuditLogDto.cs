using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Core.DTOs.System
{
    public class AuditLogDto
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public int? UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty; // Create, Update, Delete
        public string TableName { get; set; } = string.Empty;
        public int? RecordId { get; set; }
        public string? OldValue { get; set; } // JSON
        public string? NewValue { get; set; } // JSON
        public string IpAddress { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public TimeSpan ExecutionTime { get; set; }
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AuditLogFilterDto
    {
        public long? UserId { get; set; }
        public string? TableName { get; set; }
        public string? Action { get; set; } // Create, Update, Delete
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? IpAddress { get; set; }
        public bool? IsSuccess { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }

    public class AuditLogStatisticsDto
    {
        public int TotalLogs { get; set; }
        public int TodayLogs { get; set; }
        public int ThisWeekLogs { get; set; }
        public int ThisMonthLogs { get; set; }

        public int CreateActions { get; set; }
        public int UpdateActions { get; set; }
        public int DeleteActions { get; set; }

        public int SuccessfulOperations { get; set; }
        public int FailedOperations { get; set; }

        public List<TableActivityDto> TopTables { get; set; } = new();
        public List<UserActivityDto> TopUsers { get; set; } = new();
    }

    public class TableActivityDto
    {
        public string TableName { get; set; } = string.Empty;
        public int ActivityCount { get; set; }
    }

    public class UserActivityDto
    {
        public long UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public int ActivityCount { get; set; }
    }
}

