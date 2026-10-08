using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Core.DTOs.System
{
    public class AuditLogFilterDto
    {
        public long? UserId { get; set; }
        public string? TableName { get; set; }
        public string? Action { get; set; } // Create, Update, Delete
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? IpAddress { get; set; }
        public bool? IsSuccess { get; set; }
        private int _page = 1;
        private int _pageSize = 10;
        public int Page { get => _page; set => _page = Math.Clamp(value, 1, 1_000_000); }
        public int PageSize { get => _pageSize; set => _pageSize = Math.Clamp(value, 1, 100); }
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

