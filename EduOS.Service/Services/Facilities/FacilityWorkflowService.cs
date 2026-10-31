using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Facilities;

/// <summary>
/// Campus-isolated maintenance workflow. One approved request has one work order.
/// Stock and Finance are intentionally NOT posted by this service.
/// </summary>
public sealed class FacilityWorkflowService(
    EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
    ILogger<FacilityWorkflowService> logger) : IFacilityWorkflowService
{
    private bool Admin => user.IsTenantAdmin || user.IsInRole("Principal");
    private bool Manager => Admin || user.IsInRole("FacilitiesManager");
    private bool Approver => Admin || user.IsInRole("FacilitiesApprover");
    private bool Technician => user.IsInRole("FacilitiesTechnician");
    private bool Reader => Manager || Approver || Technician || user.IsInRole("FacilitiesRequester");
    private bool ValidTenant => user.IsAuthenticated && user.UserId > 0 && user.TenantId > 0 &&
                                db.CurrentTenantId == user.TenantId;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Facilities access denied.", 403);
    private IQueryable<long> AllowedCampuses() => db.Campuses.AsNoTracking()
        .Where(c => c.TenantId == user.TenantId && c.IsActive && !c.IsDeleted &&
            (Admin || db.UserCampusAccesses.Any(a => a.TenantId == user.TenantId &&
                a.UserId == user.UserId && a.CampusId == c.Id && a.IsActive && !a.IsDeleted)))
        .Select(c => c.Id);

    private static bool VersionMatches(byte[] current, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static FacilityRequestDto Map(FacilityServiceRequest request, FacilityWorkOrder? order)
        => new()
        {
            Id = request.Id, CampusId = request.CampusId, FacilitySpaceId = request.FacilitySpaceId,
            CategoryCode = request.CategoryCode, PriorityCode = request.PriorityCode,
            Summary = request.Summary, Description = request.Description, StateCode = request.StateCode,
            RequesterUserId = request.RequesterUserId, TriagedByUserId = request.TriagedByUserId,
            ApprovedByUserId = request.ApprovedByUserId, SlaDueAtUtc = request.SlaDueAtUtc,
            CreatedAt = request.CreatedAt, RowVersion = Convert.ToBase64String(request.RowVersion),
            WorkOrderId = order?.Id, WorkOrderState = order?.StateCode,
            WorkOrderRowVersion = order == null ? null : Convert.ToBase64String(order.RowVersion),
            TechnicianUserId = order?.AssignedTechnicianUserId,
            ScheduledStartUtc = order?.ScheduledStartUtc, ScheduledEndUtc = order?.ScheduledEndUtc,
            CompletedAtUtc = order?.CompletedAtUtc, VerifiedAtUtc = order?.VerifiedAtUtc,
            CompletionEvidence = order?.CompletionEvidence,
            SlaBreached = order?.VerifiedAtUtc == null || request.SlaDueAtUtc == null
                ? null : order.CompletedAtUtc > request.SlaDueAtUtc
        };
    private async Task<FacilityRequestDto> ReadAsync(FacilityServiceRequest request, CancellationToken ct)
    {
        var order = await db.Set<FacilityWorkOrder>().AsNoTracking().FirstOrDefaultAsync(
            x => x.TenantId == user.TenantId && x.FacilityServiceRequestId == request.Id && !x.IsDeleted, ct);
        return Map(request, order);
    }

    public async Task<ApiResponse<FacilityRequestDto>> CreateAsync(
        CreateFacilityRequestDto input, CancellationToken ct = default)
    {
        if (!ValidTenant || !Reader) return Denied<FacilityRequestDto>();
        if (input == null || input.ClientRequestId == Guid.Empty || input.CampusId <= 0 ||
            input.FacilitySpaceId <= 0) return ApiResponse<FacilityRequestDto>.ErrorResponse("Invalid request.");
        var category = (input.CategoryCode ?? "").Trim().ToUpperInvariant();
        var priority = (input.PriorityCode ?? "").Trim();
        var summary = (input.Summary ?? "").Trim();
        var description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        if (category.Length is < 1 or > 40 || !category.All(x => char.IsAsciiLetterOrDigit(x) || x == '_') ||
            priority is not ("Low" or "Normal" or "High" or "Urgent") ||
            summary.Length is < 5 or > 200 || description?.Length > 2000)
            return ApiResponse<FacilityRequestDto>.ErrorResponse("Invalid category, priority or description.");
        if (!await AllowedCampuses().ContainsAsync(input.CampusId, ct))
            return Denied<FacilityRequestDto>();
        var spaceValid = await db.FacilitySpaces.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && x.Id == input.FacilitySpaceId &&
            x.CampusId == input.CampusId && x.IsActive && !x.IsDeleted, ct);
        if (!spaceValid) return ApiResponse<FacilityRequestDto>.ErrorResponse("Active campus space not found.", 404);
        var previous = await db.Set<FacilityServiceRequest>().AsNoTracking().FirstOrDefaultAsync(
            x => x.TenantId == user.TenantId && x.ClientRequestId == input.ClientRequestId, ct);
        if (previous != null)
            return previous.RequesterUserId == user.UserId && previous.CampusId == input.CampusId &&
                   previous.FacilitySpaceId == input.FacilitySpaceId && previous.CategoryCode == category &&
                   previous.PriorityCode == priority && previous.Summary == summary &&
                   previous.Description == description
                ? ApiResponse<FacilityRequestDto>.SuccessResponse(await ReadAsync(previous, ct), "Existing request returned.")
                : ApiResponse<FacilityRequestDto>.ErrorResponse("Request ID reused with different details.", 409);

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var request = new FacilityServiceRequest
            {
                TenantId = user.TenantId, CampusId = input.CampusId, FacilitySpaceId = input.FacilitySpaceId,
                ClientRequestId = input.ClientRequestId, CategoryCode = category, PriorityCode = priority,
                Summary = summary, Description = description, RequesterUserId = user.UserId,
                CreatedBy = user.UserId, CreatedAt = Now
            };
            db.Set<FacilityServiceRequest>().Add(request);
            await db.SaveChangesAsync(ct);
            db.Set<FacilityWorkflowEvent>().Add(new FacilityWorkflowEvent
            {
                TenantId = user.TenantId, FacilityServiceRequestId = request.Id,
                ClientCommandId = input.ClientRequestId, ActorUserId = user.UserId,
                ActionCode = "Create", FromState = "None", ToState = "Submitted",
                CreatedBy = user.UserId, CreatedAt = Now
            });
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            var result = ApiResponse<FacilityRequestDto>.SuccessResponse(Map(request, null), "Request submitted.");
            result.StatusCode = 201;
            return result;
        }
        catch (DbUpdateException ex)
        {
            if (transaction != null) await transaction.RollbackAsync(ct);
            logger.LogWarning(ex, "Facility request constraint conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<FacilityRequestDto>.ErrorResponse("Duplicate request or invalid campus.", 409);
        }
    }

    public async Task<ApiResponse<PagedResult<FacilityRequestDto>>> ListAsync(
        long? campusId, string? state, int page, int pageSize, CancellationToken ct = default)
    {
        if (!ValidTenant || !Reader) return Denied<PagedResult<FacilityRequestDto>>();
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100 || campusId is <= 0 ||
            state?.Length > 30) return ApiResponse<PagedResult<FacilityRequestDto>>.ErrorResponse("Invalid filter.");
        var allowed = AllowedCampuses();
        if (campusId.HasValue && !await allowed.ContainsAsync(campusId.Value, ct))
            return Denied<PagedResult<FacilityRequestDto>>();
        var query = db.Set<FacilityServiceRequest>().AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && allowed.Contains(x.CampusId));
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(x => x.StateCode == state.Trim());
        var count = await query.CountAsync(ct);
        var requests = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = requests.Select(x => x.Id).ToArray();
        var orders = await db.Set<FacilityWorkOrder>().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && ids.Contains(x.FacilityServiceRequestId) && !x.IsDeleted)
            .ToDictionaryAsync(x => x.FacilityServiceRequestId, ct);
        return ApiResponse<PagedResult<FacilityRequestDto>>.SuccessResponse(new()
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            Items = requests.Select(x => Map(x, orders.GetValueOrDefault(x.Id))).ToList()
        });
    }

    public async Task<ApiResponse<FacilityRequestDto>> TransitionAsync(
        long id, string action, FacilityTransitionDto command, CancellationToken ct = default)
    {
        if (!ValidTenant || !Reader) return Denied<FacilityRequestDto>();
        if (id <= 0 || command == null || command.ClientCommandId == Guid.Empty ||
            command.Note?.Length > 1000 || string.IsNullOrWhiteSpace(command.RowVersion))
            return ApiResponse<FacilityRequestDto>.ErrorResponse("Command ID, row version and valid details required.");
        action = (action ?? "").Trim().ToLowerInvariant();
        if (action is not ("triage" or "approve" or "reject" or "schedule" or "start" or
                           "pause" or "resume" or "complete" or "verify"))
            return ApiResponse<FacilityRequestDto>.ErrorResponse("Unsupported transition.", 404);
        var request = await db.Set<FacilityServiceRequest>().FirstOrDefaultAsync(
            x => x.Id == id && x.TenantId == user.TenantId && !x.IsDeleted, ct);
        if (request == null) return ApiResponse<FacilityRequestDto>.ErrorResponse("Request not found.", 404);
        if (!await AllowedCampuses().ContainsAsync(request.CampusId, ct)) return Denied<FacilityRequestDto>();
        var order = await db.Set<FacilityWorkOrder>().FirstOrDefaultAsync(
            x => x.TenantId == user.TenantId && x.FacilityServiceRequestId == id && !x.IsDeleted, ct);
        var isWorker = order?.AssignedTechnicianUserId == user.UserId && Technician;
        var allowed = action switch
        {
            "triage" => Manager && user.UserId != request.RequesterUserId,
            "approve" or "reject" => Approver && user.UserId != request.RequesterUserId &&
                                      user.UserId != request.TriagedByUserId,
            "schedule" => Manager && user.UserId != request.RequesterUserId,
            "start" or "pause" or "resume" or "complete" => isWorker,
            "verify" => Approver && user.UserId != request.RequesterUserId &&
                        user.UserId != request.TriagedByUserId &&
                        user.UserId != order?.AssignedTechnicianUserId,
            _ => false
        };
        if (!allowed) return Denied<FacilityRequestDto>();

        var replay = await db.Set<FacilityWorkflowEvent>().AsNoTracking().FirstOrDefaultAsync(
            x => x.TenantId == user.TenantId && x.ClientCommandId == command.ClientCommandId, ct);
        if (replay != null)
            return replay.FacilityServiceRequestId == id &&
                   replay.ActorUserId == user.UserId &&
                   string.Equals(replay.ActionCode, action, StringComparison.OrdinalIgnoreCase)
                ? ApiResponse<FacilityRequestDto>.SuccessResponse(await ReadAsync(request, ct), "Already applied.")
                : ApiResponse<FacilityRequestDto>.ErrorResponse("Command ID reused.", 409);

        var requestAction = action is "triage" or "approve" or "reject";
        if (!VersionMatches(requestAction ? request.RowVersion : order?.RowVersion ?? [], command.RowVersion))
            return ApiResponse<FacilityRequestDto>.ErrorResponse("Record changed; reload.", 409);
        var before = requestAction ? request.StateCode : order?.StateCode ?? "None";
        var note = command.Note?.Trim();
        var now = Now;
        switch (action)
        {
            case "triage":
                if (request.StateCode != "Submitted" || command.SlaDueAtUtc is not DateTime due ||
                    due.Kind != DateTimeKind.Utc || due <= now)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Triage needs a future UTC SLA deadline.");
                request.SlaDueAtUtc = due;
                request.TriagedByUserId = user.UserId;
                request.StateCode = "Triaged";
                break;
            case "approve":
                if (request.StateCode != "Triaged" || order != null)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Request is not awaiting approval.", 409);
                request.ApprovedByUserId = user.UserId; request.ApprovedAtUtc = now;
                request.StateCode = "Approved";
                order = new FacilityWorkOrder
                {
                    TenantId = user.TenantId, FacilityServiceRequestId = id,
                    StateCode = "Approved", CreatedBy = user.UserId, CreatedAt = now
                };
                db.Set<FacilityWorkOrder>().Add(order);
                break;
            case "reject":
                if (request.StateCode != "Triaged" || string.IsNullOrWhiteSpace(note))
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("A triaged request and rejection reason are required.");
                request.StateCode = "Rejected"; request.DecisionReason = note[..Math.Min(500, note.Length)];
                break;
            case "schedule":
                if (order?.StateCode is not ("Approved" or "Scheduled") ||
                    command.TechnicianUserId is not > 0 ||
                    command.ScheduledStartUtc is not DateTime start || start.Kind != DateTimeKind.Utc ||
                    command.ScheduledEndUtc is not DateTime end || end.Kind != DateTimeKind.Utc ||
                    start >= end)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Valid technician and UTC schedule required.");
                var tech = command.TechnicianUserId.Value;
                var eligible = await db.Users.AsNoTracking().AnyAsync(x => x.Id == tech && x.IsActive, ct) &&
                    await db.TenantMemberships.AsNoTracking().AnyAsync(x =>
                        x.TenantId == user.TenantId && x.UserId == tech && !x.IsDeleted &&
                        x.Status == MembershipStatus.Active, ct) &&
                    await db.UserCampusAccesses.AsNoTracking().AnyAsync(x =>
                        x.TenantId == user.TenantId && x.UserId == tech &&
                        x.CampusId == request.CampusId && x.IsActive && !x.IsDeleted, ct);
                if (!eligible) return Denied<FacilityRequestDto>();
                order.AssignedTechnicianUserId = tech;
                order.ScheduledStartUtc = start; order.ScheduledEndUtc = end;
                order.StateCode = "Scheduled";
                break;
            case "start":
                if (order?.StateCode != "Scheduled")
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Work order is not scheduled.", 409);
                order.StartedAtUtc = now; order.StateCode = "InProgress";
                break;
            case "pause":
                if (order?.StateCode != "InProgress" || string.IsNullOrWhiteSpace(note))
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Pause reason required.");
                order.PauseReason = note[..Math.Min(500, note.Length)];
                order.StateCode = "Paused";
                break;
            case "resume":
                if (order?.StateCode != "Paused")
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Work order is not paused.", 409);
                order.StateCode = "InProgress"; order.PauseReason = null;
                break;
            case "complete":
                if (order?.StateCode != "InProgress" || string.IsNullOrWhiteSpace(note) || note.Length < 10)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Completion evidence (10+ characters) required.");
                order.CompletionEvidence = note;
                order.CompletedAtUtc = now; order.StateCode = "PendingVerification";
                break;
            case "verify":
                if (order?.StateCode != "PendingVerification" || string.IsNullOrWhiteSpace(note))
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Independent verification note required.");
                order.VerifiedAtUtc = now; order.VerifiedByUserId = user.UserId;
                order.StateCode = "Closed";
                break;
        }

        if (requestAction) { request.UpdatedAt = now; request.UpdatedBy = user.UserId; }
        else if (order != null) { order.UpdatedAt = now; order.UpdatedBy = user.UserId; }
        db.Set<FacilityWorkflowEvent>().Add(new FacilityWorkflowEvent
        {
            TenantId = user.TenantId, FacilityServiceRequestId = id,
            ClientCommandId = command.ClientCommandId, ActorUserId = user.UserId,
            ActionCode = action, FromState = before,
            ToState = requestAction ? request.StateCode : order!.StateCode,
            Note = note, CreatedAt = now, CreatedBy = user.UserId
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return ApiResponse<FacilityRequestDto>.SuccessResponse(await ReadAsync(request, ct), "Transition recorded.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<FacilityRequestDto>.ErrorResponse("Record changed; reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Facility workflow constraint conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<FacilityRequestDto>.ErrorResponse("Concurrent transition or duplicate command.", 409);
        }
    }

    public async Task<ApiResponse<PagedResult<FacilityEventDto>>> HistoryAsync(
        long id, int page, int pageSize, CancellationToken ct = default)
    {
        if (!ValidTenant || !Reader) return Denied<PagedResult<FacilityEventDto>>();
        if (id <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50)
            return ApiResponse<PagedResult<FacilityEventDto>>.ErrorResponse("Invalid page.");
        var request = await db.Set<FacilityServiceRequest>().AsNoTracking().FirstOrDefaultAsync(
            x => x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
        if (request == null) return ApiResponse<PagedResult<FacilityEventDto>>.ErrorResponse("Request not found.", 404);
        if (!await AllowedCampuses().ContainsAsync(request.CampusId, ct))
            return Denied<PagedResult<FacilityEventDto>>();
        var query = db.Set<FacilityWorkflowEvent>().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.FacilityServiceRequestId == id && !x.IsDeleted);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new FacilityEventDto
            {
                Id = x.Id, ActionCode = x.ActionCode, FromState = x.FromState,
                ToState = x.ToState, ActorUserId = x.ActorUserId,
                OccurredAtUtc = x.CreatedAt, Note = x.Note
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<FacilityEventDto>>.SuccessResponse(new()
        { Page = page, PageSize = pageSize, TotalCount = count, Items = rows });
    }
}