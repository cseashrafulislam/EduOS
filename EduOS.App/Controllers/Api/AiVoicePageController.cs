using System.Security.Claims;
using EduOS.AI;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EduOS.App.Controllers.Api;

/// <summary>
/// Shared page-aware C3 intent preview. One endpoint for every authorized page.
/// Never treats client-provided field metadata as server-side authorization.
/// No transcript/audio persistence, external provider, field mutation or domain write.
/// </summary>
[ApiController]
[Route("api/ai/voice/page")]
[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiVoicePageController(EduOSDbContext db, ICurrentUserService current) : ControllerBase
{
    [HttpGet("contract")]
    public async Task<IActionResult> Contract(CancellationToken ct)
    {
        if (!await HasActiveTrustedMembershipAsync(ct)) return Forbid();
        return Ok(new
        {
            version = ReviewedVoicePageIntentCompiler.ContractVersion,
            endpoint = "/api/ai/voice/page/preview",
            actions = new[] { "FocusField", "DraftText", "SetFilter", "SelectOption" },
            speech = "browser-local-or-typed-transcript",
            serverAudioUploadEnabled = false,
            remoteSpeechProviderEnabled = false,
            maxTranscriptLength = ReviewedVoicePageIntentCompiler.MaximumUtteranceLength,
            maxTargets = ReviewedVoicePageIntentCompiler.MaximumTargets,
            maxOptionsPerTarget = ReviewedVoicePageIntentCompiler.MaximumOptionsPerTarget,
            requiresExplicitReview = true,
            canSubmitOrPostBusinessRecords = false
        });
    }

    [HttpPost("preview")]
    [RequestSizeLimit(32768)]
    public async Task<IActionResult> Preview([FromBody] VoicePagePreviewRequest? request,
        CancellationToken ct)
    {
        if (!await HasActiveTrustedMembershipAsync(ct)) return Forbid();
        if (request is null) return BadRequest(new { message = "A voice command is required." });

        var plan = ReviewedVoicePageIntentCompiler.Preview(request);
        return Ok(new
        {
            version = ReviewedVoicePageIntentCompiler.ContractVersion,
            decision = plan.Decision.ToString(),
            message = plan.Message,
            action = plan.Action,
            targetKey = plan.TargetKey,
            proposedValue = plan.ProposedValue,
            optionKey = plan.OptionKey,
            pagePath = request.PagePath,
            requiresConfirmation = plan.RequiresConfirmation,
            businessWriteExecuted = plan.BusinessWriteExecuted
        });
    }

    private async Task<bool> HasActiveTrustedMembershipAsync(CancellationToken ct)
    {
        if (!current.IsAuthenticated || current.TenantId <= 0 || current.UserId <= 0 ||
            db.CurrentTenantId != current.TenantId || User.Identity?.IsAuthenticated != true ||
            !long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            userId != current.UserId)
            return false;

        if (!await db.Users.AsNoTracking().AnyAsync(x =>
            x.Id == current.UserId && x.IsActive && !x.IsDeleted, ct))
            return false;

        return await db.TenantMemberships.AsNoTracking().AnyAsync(x =>
            x.TenantId == current.TenantId && x.UserId == current.UserId &&
            x.Status == MembershipStatus.Active && !x.IsDeleted, ct);
    }
}
