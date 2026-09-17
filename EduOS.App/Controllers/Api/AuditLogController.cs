using EduOS.Core.DTOs.System;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api
{
    [Authorize(Roles = "TenantAdmin,Principal")]
    [ApiController]
    [Route("api/v1/[controller]")]
    [Produces("application/json")]
    public class AuditLogController : ControllerBase
    {
        private readonly IAuditLogService _auditLogService;

        public AuditLogController(IAuditLogService auditLogService)
        {
            _auditLogService = auditLogService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] AuditLogFilterDto filter)
        {
            var result = await _auditLogService.GetAllAsync(filter);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("record/{tableName}/{recordId:long}")]
        public async Task<IActionResult> GetByRecord(string tableName, long recordId)
        {
            var result = await _auditLogService.GetByRecordAsync(tableName, recordId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("user/{userId:long}")]
        public async Task<IActionResult> GetByUser(long userId)
        {
            var result = await _auditLogService.GetByUserAsync(userId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("export")]
        [EnableRateLimiting("ApiPolicy")]
        public async Task<IActionResult> Export([FromQuery] AuditLogFilterDto filter)
        {
            var result = await _auditLogService.ExportAsync(filter);
            if (!result.Success || result.Data is null)
                return StatusCode(result.StatusCode, result);

            return File(
                result.Data,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"audit-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
        }
    }
}
