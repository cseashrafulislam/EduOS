using EduOS.App.Filters;
using EduOS.Core.Constants;
using EduOS.Core.DTOs.System;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api
{
    [Authorize]
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
        [HasPermission(PermissionConstants.AuditLog.View)]
        public async Task<IActionResult> GetAll([FromQuery] AuditLogFilterDto filter)
        {
            var result = await _auditLogService.GetAllAsync(filter);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("record/{tableName}/{recordId}")]
        [HasPermission(PermissionConstants.AuditLog.View)]
        public async Task<IActionResult> GetByRecord(string tableName, int recordId)
        {
            var result = await _auditLogService.GetByRecordAsync(tableName, recordId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("user/{userId}")]
        [HasPermission(PermissionConstants.AuditLog.View)]
        public async Task<IActionResult> GetByUser(int userId)
        {
            var result = await _auditLogService.GetByUserAsync(userId);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("export")]
        [HasPermission(PermissionConstants.AuditLog.Export)]
        public async Task<IActionResult> Export([FromQuery] AuditLogFilterDto filter)
        {
            // Export to Excel/CSV logic here
            return Ok(new { message = "Export functionality coming soon" });
        }
    }
}
