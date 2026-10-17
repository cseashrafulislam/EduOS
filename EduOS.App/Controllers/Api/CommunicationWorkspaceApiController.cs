using EduOS.Core.DTOs.Communication;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/communication")]
[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CommunicationWorkspaceApiController(ICommunicationService service, IMessageWorkspaceService messages) : ControllerBase
{
    [HttpGet("notices")]
    public async Task<IActionResult> Notices([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] bool? published = null, CancellationToken ct = default)
    { var r = await service.GetNoticesAsync(page, pageSize, published, ct); return StatusCode(r.StatusCode, r); }

    [HttpPost("notices")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> CreateNotice([FromBody] SaveNoticeRequestDto request, CancellationToken ct)
    { var r = await service.SaveNoticeAsync(null, request, ct); return StatusCode(r.StatusCode, r); }

    [HttpPut("notices/{reference:guid}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateNotice(Guid reference, [FromBody] SaveNoticeRequestDto request, CancellationToken ct)
    { var r = await service.SaveNoticeAsync(reference, request, ct); return StatusCode(r.StatusCode, r); }

    [HttpGet("notices/{reference:guid}/read-receipts")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> NoticeReadReceipts(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    { var r = await messages.GetNoticeReadReceiptsAsync(reference, page, pageSize, ct); return StatusCode(r.StatusCode, r); }

    [HttpPost("notices/{reference:guid}/read")]
    public async Task<IActionResult> MarkNoticeRead(Guid reference, CancellationToken ct)
    { var r = await service.MarkNoticeReadAsync(reference, ct); return StatusCode(r.StatusCode, r); }

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    { var r = await service.GetMyNotificationsAsync(page, pageSize, ct); return StatusCode(r.StatusCode, r); }

    [HttpPost("notifications/{id:long}/read")]
    public async Task<IActionResult> MarkNotificationRead(long id, CancellationToken ct)
    { var r = await service.MarkNotificationReadAsync(id, ct); return StatusCode(r.StatusCode, r); }

    [HttpPost("messages/{clientRequestId:guid}")]
    public async Task<IActionResult> SendMessage(Guid clientRequestId, [FromBody] SendMessageRequestDto request, CancellationToken ct)
    { var r = await service.SendMessageAsync(clientRequestId, request, ct); return StatusCode(r.StatusCode, r); }

    [HttpGet("threads")]
    public async Task<IActionResult> MyThreads([FromQuery] int page = 1, [FromQuery] int pageSize = 15, CancellationToken ct = default)
    { var r = await messages.GetMyThreadsAsync(page, pageSize, ct); return StatusCode(r.StatusCode, r); }

    [HttpGet("message-recipients")]
    public async Task<IActionResult> MessageRecipients([FromQuery] string query = "", CancellationToken ct = default)
    { var r = await messages.SearchRecipientsAsync(query, ct); return StatusCode(r.StatusCode, r); }

    [HttpGet("message-thread-for/{id:long}")]
    public async Task<IActionResult> MessageThreadFor(long id, CancellationToken ct)
    { var r = await messages.GetThreadForMessageAsync(id, ct); return StatusCode(r.StatusCode, r); }

    [HttpPost("threads/{reference:guid}/read")]
    public async Task<IActionResult> MarkThreadRead(Guid reference, CancellationToken ct)
    { var r = await messages.MarkThreadReadAsync(reference, ct); return StatusCode(r.StatusCode, r); }

    [HttpGet("threads/{reference:guid}")]
    public async Task<IActionResult> Thread(Guid reference, CancellationToken ct)
    { var r = await service.GetThreadAsync(reference, ct); return StatusCode(r.StatusCode, r); }

    [HttpGet("threads/{reference:guid}/messages")]
    public async Task<IActionResult> ThreadMessages(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    { var r = await service.GetThreadMessagesAsync(reference, page, pageSize, ct); return StatusCode(r.StatusCode, r); }

    [HttpGet("preferences")]
    public async Task<IActionResult> Preferences(CancellationToken ct)
    { var r = await service.GetMyPreferencesAsync(ct); return StatusCode(r.StatusCode, r); }

    [HttpPut("preferences")]
    public async Task<IActionResult> SavePreference([FromBody] SaveNotificationPreferenceRequestDto request, CancellationToken ct)
    { var r = await service.SaveMyPreferenceAsync(request, ct); return StatusCode(r.StatusCode, r); }

    [HttpGet("devices")]
    public async Task<IActionResult> Devices([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    { var r = await messages.GetMyDevicesAsync(page, pageSize, ct); return StatusCode(r.StatusCode, r); }

    [HttpPost("devices")]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceTokenRequestDto request, CancellationToken ct)
    { var r = await service.RegisterDeviceTokenAsync(request, ct); return StatusCode(r.StatusCode, r); }

    [HttpPost("devices/{id:long}/deactivate")]
    public async Task<IActionResult> DeactivateDevice(long id, CancellationToken ct)
    { var r = await service.DeactivateDeviceTokenAsync(id, ct); return StatusCode(r.StatusCode, r); }
}
