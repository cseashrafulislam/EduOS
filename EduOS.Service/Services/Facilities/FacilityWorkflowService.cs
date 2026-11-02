using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Auth;
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

    // Request visibility is scoped by ownership, assignment and authorized campus.
    private IQueryable<FacilityServiceRequest> VisibleRequests() =>
        db.Set<FacilityServiceRequest>().AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && AllowedCampuses().Contains(x.CampusId) &&
            (Manager || Approver || x.RequesterUserId == user.UserId ||
             (Technician && db.Set<FacilityWorkOrder>().Any(w =>
                 w.TenantId == user.TenantId && !w.IsDeleted &&
                 w.FacilityServiceRequestId == x.Id && w.AssignedTechnicianUserId == user.UserId))));

    private static bool VersionMatches(byte[] current, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    // The command ID identifies one immutable payload, not merely an action name.
    // Legacy triage events did not store the deadline; their committed request
    // deadline is checked so exact pre-upgrade retries still work fail-closed.
    // Compare the entire controlled audit suffix from its FIRST delimiter.
    // EndsWith alone could accept a changed note containing another delimiter.
    private static bool ReplayTailMatches(string? audit, string marker, string expected)
    {
        var index = audit?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
        return index >= 0 && string.Equals(audit![index..], expected, StringComparison.Ordinal);
    }

    private static bool ReplayPayloadMatches(FacilityWorkflowEvent replay, string action,
        FacilityTransitionDto command, FacilityServiceRequest request)
    {
        var note = command.Note?.Trim();
        return action switch
        {
            "schedule" => command.TechnicianUserId is > 0 &&
                command.ScheduledStartUtc is DateTime start && start.Kind == DateTimeKind.Utc &&
                command.ScheduledEndUtc is DateTime end && end.Kind == DateTimeKind.Utc &&
                ReplayTailMatches(replay.Note, "; new technician=",
                    $"; new technician={command.TechnicianUserId}, start={start:O}, end={end:O}" +
                    (string.IsNullOrWhiteSpace(note) ? "" : $"; reason={note}")),
            "vendorpropose" => command.VendorSupplierId is > 0 &&
                command.VendorStartUtc is DateTime vendorStart && vendorStart.Kind == DateTimeKind.Utc &&
                command.VendorEndUtc is DateTime vendorEnd && vendorEnd.Kind == DateTimeKind.Utc &&
                !string.IsNullOrWhiteSpace(note) &&
                ReplayTailMatches(replay.Note, "; new vendor=",
                    $"; new vendor={command.VendorSupplierId}, start={vendorStart:O}, end={vendorEnd:O}; reason={note}"),
            "triage" => command.SlaDueAtUtc is DateTime due && due.Kind == DateTimeKind.Utc &&
                (string.Equals(replay.Note,
                    $"SLA due UTC={due:O}" + (string.IsNullOrWhiteSpace(note) ? "" : $"; note={note}"),
                    StringComparison.Ordinal) ||
                 (replay.Note?.StartsWith("SLA due UTC=", StringComparison.Ordinal) != true &&
                  request.SlaDueAtUtc == due && string.Equals(replay.Note, note, StringComparison.Ordinal))),
            "unschedule" => !string.IsNullOrWhiteSpace(note) &&
                ReplayTailMatches(replay.Note, "; reason=", $"; reason={note}"),
            "escalate" => !string.IsNullOrWhiteSpace(note) &&
                string.Equals(replay.Note, "Manual overdue SLA escalation: " + note,
                    StringComparison.Ordinal),
            _ => string.Equals(replay.Note, note, StringComparison.Ordinal)
        };
    }

    private FacilityRequestDto Map(FacilityServiceRequest request, FacilityWorkOrder? order)
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
            VendorSupplierId = Manager || Approver ? order?.VendorSupplierId : null,
            VendorStateCode = order?.VendorStateCode,
            VendorProposedByUserId = Manager || Approver ? order?.VendorProposedByUserId : null,
            VendorApprovedByUserId = Manager || Approver ? order?.VendorApprovedByUserId : null,
            VendorStartUtc = Manager || Approver ? order?.VendorStartUtc : null,
            VendorEndUtc = Manager || Approver ? order?.VendorEndUtc : null,
            ScheduledStartUtc = order?.ScheduledStartUtc, ScheduledEndUtc = order?.ScheduledEndUtc,
            CompletedAtUtc = order?.CompletedAtUtc, VerifiedAtUtc = order?.VerifiedAtUtc,
            CompletionEvidence = order?.CompletionEvidence,
            SlaBreached = order?.VerifiedAtUtc == null || request.SlaDueAtUtc == null
                ? null : order.VerifiedAtUtc > request.SlaDueAtUtc
        };
    private async Task<FacilityRequestDto> ReadAsync(FacilityServiceRequest request, CancellationToken ct)
    {
        var order = await db.Set<FacilityWorkOrder>().AsNoTracking().FirstOrDefaultAsync(
            x => x.TenantId == user.TenantId && x.FacilityServiceRequestId == request.Id && !x.IsDeleted, ct);
        return Map(request, order);
    }

    /// <summary>
    /// Requesters can discover only active campus spaces within their current grants.
    /// This is a bounded read-only lookup; CreateAsync revalidates the selected IDs.
    /// </summary>
    public async Task<ApiResponse<PagedResult<FacilityLocationOptionDto>>> LocationsAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!ValidTenant || !Reader) return Denied<PagedResult<FacilityLocationOptionDto>>();
        if (page is < 1 or > 100000 || pageSize is < 1 or > 50 || search?.Length > 100)
            return ApiResponse<PagedResult<FacilityLocationOptionDto>>.ErrorResponse("Invalid location search.");

        var query = from space in db.FacilitySpaces.AsNoTracking()
                    join campus in db.Campuses.AsNoTracking()
                        on new { space.TenantId, Id = space.CampusId }
                        equals new { campus.TenantId, campus.Id }
                    where space.TenantId == user.TenantId && !space.IsDeleted && space.IsActive &&
                          campus.IsActive && !campus.IsDeleted &&
                          AllowedCampuses().Contains(space.CampusId)
                    select new { space, campus };
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.space.Code.Contains(term) || x.space.Name.Contains(term) ||
                                     x.campus.Code.Contains(term) || x.campus.Name.Contains(term));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.campus.Name).ThenBy(x => x.space.Code)
            .ThenBy(x => x.space.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new FacilityLocationOptionDto
            {
                CampusId = x.campus.Id, CampusCode = x.campus.Code,
                CampusName = x.campus.Name, FacilitySpaceId = x.space.Id,
                SpaceCode = x.space.Code, SpaceName = x.space.Name
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<FacilityLocationOptionDto>>.SuccessResponse(new()
        {
            Page = page, PageSize = pageSize, TotalCount = total, Items = items
        });
    }

    /// <summary>
    /// Read-only approved supplier discovery. This is NOT a vendor assignment,
    /// procurement order, reservation, stock movement or Finance posting.
    /// </summary>
    public async Task<ApiResponse<PagedResult<FacilityVendorOptionDto>>> VendorsAsync(
        long campusId, string? search, int page, int pageSize, CancellationToken ct = default,
        long? workOrderId = null, DateTime? startUtc = null, DateTime? endUtc = null)
    {
        if (!ValidTenant || !Manager) return Denied<PagedResult<FacilityVendorOptionDto>>();
        if (campusId <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50 ||
            search?.Length > 100 || workOrderId is <= 0)
            return ApiResponse<PagedResult<FacilityVendorOptionDto>>.ErrorResponse(
                "Invalid vendor lookup.", 400);
        // Availability is advisory; independent approval locks and revalidates.
        var hasWindow = startUtc.HasValue && endUtc.HasValue;
        if (startUtc.HasValue != endUtc.HasValue ||
            (hasWindow && (!workOrderId.HasValue ||
                startUtc!.Value.Kind != DateTimeKind.Utc ||
                endUtc!.Value.Kind != DateTimeKind.Utc ||
                startUtc.Value <= Now || startUtc.Value >= endUtc.Value)))
            return ApiResponse<PagedResult<FacilityVendorOptionDto>>.ErrorResponse(
                "An authorized work order and future UTC start/end window are required.", 400);
        if (!await AllowedCampuses().ContainsAsync(campusId, ct))
            return Denied<PagedResult<FacilityVendorOptionDto>>();
        // Existing campus-only callers remain compatible. Interactive previews
        // bind to an approved, unstarted order not owned by the requesting manager.
        // Never accept a guessed order from another campus or tenant.
        if (workOrderId.HasValue && !await db.Set<FacilityWorkOrder>().AsNoTracking().AnyAsync(w =>
            w.Id == workOrderId.Value && w.TenantId == user.TenantId && !w.IsDeleted &&
            (w.StateCode == "Approved" || w.StateCode == "Scheduled") &&
            w.StartedAtUtc == null && w.VendorStateCode != "Proposed" &&
            w.VendorStateCode != "Approved" &&
            db.Set<FacilityServiceRequest>().Any(r =>
                r.Id == w.FacilityServiceRequestId && r.TenantId == user.TenantId &&
                !r.IsDeleted && r.CampusId == campusId && r.StateCode == "Approved" &&
                r.RequesterUserId != user.UserId), ct))
            return Denied<PagedResult<FacilityVendorOptionDto>>();

        // A vendor booking that cannot intersect the assigned technician window
        // cannot ever be used to start this work order. Do not offer it as
        // available; authoritative propose/approve enforce the same invariant.
        if (hasWindow && await db.Set<FacilityWorkOrder>().AsNoTracking().AnyAsync(w =>
            w.Id == workOrderId!.Value && w.TenantId == user.TenantId && !w.IsDeleted &&
            w.StateCode == "Scheduled" &&
            (w.ScheduledStartUtc >= endUtc!.Value ||
             w.ScheduledEndUtc <= startUtc!.Value), ct))
            return ApiResponse<PagedResult<FacilityVendorOptionDto>>.ErrorResponse(
                "Vendor booking must overlap the technician schedule; reschedule or choose another window.", 409);

        // Supplier lifecycle remains owned by Procurement. Never disclose
        // submitted/rejected/inactive vendors or their contact/tax information.
        var query = db.ProcurementSuppliers.AsNoTracking().Where(s =>
            s.TenantId == user.TenantId && s.CampusId == campusId &&
            AllowedCampuses().Contains(s.CampusId) &&
            !s.IsDeleted && s.StateCode == "Approved");
        if (hasWindow)
        {
            var from = startUtc!.Value;
            var to = endUtc!.Value;
            var ownOrderId = workOrderId!.Value;
            query = query.Where(s => !db.Set<FacilityWorkOrder>().Any(w =>
                w.TenantId == user.TenantId && !w.IsDeleted && w.Id != ownOrderId &&
                w.VendorSupplierId == s.Id && w.VendorStateCode == "Approved" &&
                w.VendorStartUtc < to && w.VendorEndUtc > from));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(s => s.Name.Contains(term) || s.SupplierCode.Contains(term));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(s => s.Name).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new FacilityVendorOptionDto
            {
                SupplierId = s.Id, SupplierCode = s.SupplierCode, SupplierName = s.Name
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<FacilityVendorOptionDto>>.SuccessResponse(new()
        {
            Page = page, PageSize = pageSize, TotalCount = total, Items = items
        });
    }

    /// <summary>
    /// A manager can discover only currently eligible staff in an authorized campus.
    /// Scheduling independently rechecks grants and concurrency before committing.
    /// </summary>
    // Shared live eligibility: a saved assignment or stale role claim grants no access.
    private IQueryable<ApplicationUser> EligibleTechnicians(long campusId) =>
        db.Users.AsNoTracking().Where(candidate =>
            candidate.IsActive &&
            db.TenantMemberships.Any(member => member.UserId == candidate.Id &&
                member.TenantId == user.TenantId && !member.IsDeleted &&
                member.Status == MembershipStatus.Active) &&
            db.UserCampusAccesses.Any(grant => grant.UserId == candidate.Id &&
                grant.TenantId == user.TenantId && grant.CampusId == campusId &&
                grant.IsActive && !grant.IsDeleted) &&
            db.UserRoles.Any(assignment => assignment.UserId == candidate.Id &&
                db.Roles.IgnoreQueryFilters().Any(role => role.Id == assignment.RoleId &&
                    role.IsActive && !role.IsDeleted &&
                    role.NormalizedName == "FACILITIESTECHNICIAN" &&
                    (role.TenantId == user.TenantId ||
                     (role.TenantId == null && !role.IsSystemRole &&
                      !db.TenantMemberships.IgnoreQueryFilters().Any(other =>
                          other.UserId == candidate.Id && other.TenantId != user.TenantId &&
                          !other.IsDeleted && other.Status != MembershipStatus.Left))))));

    public async Task<ApiResponse<PagedResult<FacilityTechnicianOptionDto>>> TechniciansAsync(
        long campusId, string? search, int page, int pageSize, CancellationToken ct = default,
        DateTime? startUtc = null, DateTime? endUtc = null, long? workOrderId = null)
    {
        if (!ValidTenant || !Manager) return Denied<PagedResult<FacilityTechnicianOptionDto>>();
        if (campusId <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50 ||
            search?.Length > 100)
            return ApiResponse<PagedResult<FacilityTechnicianOptionDto>>.ErrorResponse(
                "Invalid technician lookup.");
        if (!await AllowedCampuses().ContainsAsync(campusId, ct))
            return Denied<PagedResult<FacilityTechnicianOptionDto>>();
        // Availability is advisory; the schedule command still takes its SQL
        // transaction lock and rechecks overlaps before any committed mutation.
        var hasWindow = startUtc.HasValue && endUtc.HasValue;
        if (startUtc.HasValue != endUtc.HasValue || (workOrderId.HasValue && !hasWindow) ||
            workOrderId is <= 0 ||
            (hasWindow && (startUtc!.Value.Kind != DateTimeKind.Utc ||
                           endUtc!.Value.Kind != DateTimeKind.Utc || startUtc.Value >= endUtc.Value ||
                           startUtc.Value <= Now)))
            return ApiResponse<PagedResult<FacilityTechnicianOptionDto>>.ErrorResponse(
                "A future UTC start/end window and work order are required.", 400);
        // Exclusion must never accept a guessed work order from another campus.
        if (workOrderId.HasValue && !await db.Set<FacilityWorkOrder>().AsNoTracking()
            .AnyAsync(w => w.Id == workOrderId.Value && w.TenantId == user.TenantId &&
                !w.IsDeleted && (w.StateCode == "Approved" || w.StateCode == "Scheduled") &&
                db.Set<FacilityServiceRequest>().Any(r =>
                    r.TenantId == user.TenantId && r.Id == w.FacilityServiceRequestId &&
                    !r.IsDeleted && r.StateCode == "Approved" && r.CampusId == campusId &&
                    r.RequesterUserId != user.UserId), ct))
            return Denied<PagedResult<FacilityTechnicianOptionDto>>();

        // An independently approved vendor booking must have a usable overlap
        // with the proposed technician window. A read-only preview cannot
        // promise availability for a schedule that cannot be started.
        if (hasWindow && workOrderId.HasValue &&
            await db.Set<FacilityWorkOrder>().AsNoTracking().AnyAsync(w =>
                w.Id == workOrderId.Value && w.TenantId == user.TenantId && !w.IsDeleted &&
                w.VendorStateCode == "Approved" &&
                (w.VendorStartUtc >= endUtc!.Value ||
                 w.VendorEndUtc <= startUtc!.Value), ct))
            return ApiResponse<PagedResult<FacilityTechnicianOptionDto>>.ErrorResponse(
                "Technician schedule must overlap the approved vendor booking; release or rebook first.", 409);

        // Match the schedule command's active user, membership, campus grant and
        // scoped role. A global non-system role is allowed for one tenant only.
        var query = EligibleTechnicians(campusId);
        if (hasWindow)
        {
            var start = startUtc!.Value;
            var end = endUtc!.Value;
            query = query.Where(candidate => !db.Set<FacilityWorkOrder>().Any(w =>
                w.TenantId == user.TenantId && !w.IsDeleted &&
                (!workOrderId.HasValue || w.Id != workOrderId.Value) &&
                w.AssignedTechnicianUserId == candidate.Id &&
                (w.StateCode == "Scheduled" || w.StateCode == "InProgress" ||
                 w.StateCode == "Paused") &&
                w.ScheduledStartUtc < end && w.ScheduledEndUtc > start));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(candidate => candidate.FullName.Contains(term));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(candidate => candidate.FullName)
            .ThenBy(candidate => candidate.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(candidate => new FacilityTechnicianOptionDto
            {
                UserId = candidate.Id, DisplayName = candidate.FullName
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<FacilityTechnicianOptionDto>>.SuccessResponse(new()
        {
            Page = page, PageSize = pageSize, TotalCount = total, Items = items
        });
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
            if (transaction != null)
            {
                await transaction.RollbackAsync(ct);
                // Release the failed SQL transaction before reading the winning request.
                await transaction.DisposeAsync();
            }
            // A parallel retry may have won the unique tenant/command constraint.
            // A mismatched owner or payload must never be acknowledged.
            var committed = await db.Set<FacilityServiceRequest>().AsNoTracking().FirstOrDefaultAsync(
                x => x.TenantId == user.TenantId && x.ClientRequestId == input.ClientRequestId, ct);
            if (committed != null)
                return committed.RequesterUserId == user.UserId && committed.CampusId == input.CampusId &&
                       committed.FacilitySpaceId == input.FacilitySpaceId && committed.CategoryCode == category &&
                       committed.PriorityCode == priority && committed.Summary == summary &&
                       committed.Description == description
                    ? ApiResponse<FacilityRequestDto>.SuccessResponse(
                        await ReadAsync(committed, ct), "Existing request returned.")
                    : ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "Request ID reused with different details.", 409);
            logger.LogWarning(ex, "Facility request constraint conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<FacilityRequestDto>.ErrorResponse("Duplicate request or invalid campus.", 409);
        }
    }

    public async Task<ApiResponse<PagedResult<FacilityRequestDto>>> ListAsync(
        long? campusId, string? state, int page, int pageSize, CancellationToken ct = default, bool overdueOnly = false)
    {
        if (!ValidTenant || !Reader) return Denied<PagedResult<FacilityRequestDto>>();
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100 || campusId is <= 0 ||
            state?.Length > 30) return ApiResponse<PagedResult<FacilityRequestDto>>.ErrorResponse("Invalid filter.");
        var allowed = AllowedCampuses();
        if (campusId.HasValue && !await allowed.ContainsAsync(campusId.Value, ct))
            return Denied<PagedResult<FacilityRequestDto>>();
        var query = VisibleRequests();
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(x => x.StateCode == state.Trim());
        if (overdueOnly)
        {
            var now = Now;
            query = query.Where(x => x.SlaDueAtUtc != null && x.SlaDueAtUtc < now &&
                db.Set<FacilityWorkOrder>().Any(w => w.TenantId == user.TenantId &&
                    !w.IsDeleted && w.FacilityServiceRequestId == x.Id &&
                    w.StateCode != "Closed" && w.StateCode != "Cancelled"));
        }
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

    public async Task<ApiResponse<FacilityDashboardDto>> DashboardAsync(
        long? campusId, CancellationToken ct = default)
    {
        if (!ValidTenant || !Reader) return Denied<FacilityDashboardDto>();
        if (campusId is <= 0) return ApiResponse<FacilityDashboardDto>.ErrorResponse("Invalid campus.");
        if (campusId.HasValue && !await AllowedCampuses().ContainsAsync(campusId.Value, ct))
            return Denied<FacilityDashboardDto>();
        var requests = VisibleRequests();
        if (campusId.HasValue) requests = requests.Where(x => x.CampusId == campusId.Value);
        var orders = from o in db.Set<FacilityWorkOrder>().AsNoTracking()
                     join r in requests on o.FacilityServiceRequestId equals r.Id
                     where o.TenantId == user.TenantId && !o.IsDeleted
                     select new { Order = o, Request = r };
        var now = Now;
        var data = new FacilityDashboardDto
        {
            TotalRequests = await requests.CountAsync(ct),
            Submitted = await requests.CountAsync(x => x.StateCode == "Submitted", ct),
            AwaitingApproval = await requests.CountAsync(x => x.StateCode == "Triaged", ct),
            Rejected = await requests.CountAsync(x => x.StateCode == "Rejected", ct),
            ApprovedUnscheduled = await orders.CountAsync(x => x.Order.StateCode == "Approved", ct),
            CancellationPending = await orders.CountAsync(x => x.Order.StateCode == "CancellationPending", ct),
            Cancelled = await orders.CountAsync(x => x.Order.StateCode == "Cancelled", ct),
            Scheduled = await orders.CountAsync(x => x.Order.StateCode == "Scheduled", ct),
            InProgress = await orders.CountAsync(x => x.Order.StateCode == "InProgress" ||
                x.Order.StateCode == "Paused", ct),
            PendingVerification = await orders.CountAsync(x => x.Order.StateCode == "PendingVerification", ct),
            Closed = await orders.CountAsync(x => x.Order.StateCode == "Closed", ct),
            OverdueOpen = await orders.CountAsync(x => x.Request.SlaDueAtUtc != null &&
                x.Request.SlaDueAtUtc < now && x.Order.StateCode != "Closed" &&
                x.Order.StateCode != "Cancelled", ct),
            ClosedLate = await orders.CountAsync(x => x.Order.StateCode == "Closed" &&
                x.Request.SlaDueAtUtc != null && x.Order.VerifiedAtUtc != null &&
                x.Order.VerifiedAtUtc > x.Request.SlaDueAtUtc, ct)
        };
        return ApiResponse<FacilityDashboardDto>.SuccessResponse(data);
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
                           "pause" or "resume" or "complete" or "verify" or "return" or "unschedule" or "escalate" or
                            "cancelrequest" or "cancelapprove" or "cancelreject" or
                            "vendorpropose" or "vendorapprove" or "vendorreject" or "vendorrelease"))
            return ApiResponse<FacilityRequestDto>.ErrorResponse("Unsupported transition.", 404);
        // Ignore neither a changed business deadline nor an unrelated assignment
        // payload on retry: non-applicable fields cannot be silently accepted.
        if ((action != "triage" && command.SlaDueAtUtc.HasValue) ||
            (action != "schedule" && (command.TechnicianUserId.HasValue ||
                command.ScheduledStartUtc.HasValue || command.ScheduledEndUtc.HasValue)) ||
            (action != "vendorpropose" && (command.VendorSupplierId.HasValue ||
                command.VendorStartUtc.HasValue || command.VendorEndUtc.HasValue)))
            return ApiResponse<FacilityRequestDto>.ErrorResponse(
                "Unexpected transition fields.", 400);
        var request = await db.Set<FacilityServiceRequest>().FirstOrDefaultAsync(
            x => x.Id == id && x.TenantId == user.TenantId && !x.IsDeleted, ct);
        if (request == null) return ApiResponse<FacilityRequestDto>.ErrorResponse("Request not found.", 404);
        if (!await AllowedCampuses().ContainsAsync(request.CampusId, ct)) return Denied<FacilityRequestDto>();
        var order = await db.Set<FacilityWorkOrder>().FirstOrDefaultAsync(
            x => x.TenantId == user.TenantId && x.FacilityServiceRequestId == id && !x.IsDeleted, ct);
        var isWorker = order?.AssignedTechnicianUserId == user.UserId && Technician;
        // The immutable cancellation-request event is the maker identity. No new
        // mutable shadow approver field or independent cancellation ledger.
        long? cancellationMaker = action is "cancelapprove" or "cancelreject"
            ? await db.Set<FacilityWorkflowEvent>().AsNoTracking()
                .Where(x => x.TenantId == user.TenantId &&
                    x.FacilityServiceRequestId == id && !x.IsDeleted &&
                    x.ActionCode == "cancelrequest")
                .OrderByDescending(x => x.Id).Select(x => (long?)x.ActorUserId)
                .FirstOrDefaultAsync(ct)
            : null;
        var allowed = action switch
        {
            "triage" => Manager && user.UserId != request.RequesterUserId,
            "approve" or "reject" => Approver && user.UserId != request.RequesterUserId &&
                                      user.UserId != request.TriagedByUserId,
            "schedule" or "unschedule" or "escalate" or "cancelrequest" =>
                Manager && user.UserId != request.RequesterUserId,
            "vendorpropose" => Manager && user.UserId != request.RequesterUserId,
            "vendorapprove" or "vendorreject" => Approver &&
                user.UserId != request.RequesterUserId &&
                user.UserId != request.TriagedByUserId &&
                user.UserId != order?.VendorProposedByUserId,
            "vendorrelease" => Approver &&
                user.UserId != request.RequesterUserId &&
                user.UserId != order?.VendorProposedByUserId &&
                user.UserId != order?.VendorApprovedByUserId,
            "cancelapprove" or "cancelreject" => Approver &&
                user.UserId != request.RequesterUserId &&
                user.UserId != request.TriagedByUserId &&
                cancellationMaker.HasValue && cancellationMaker.Value != user.UserId,
            "start" or "pause" or "resume" or "complete" => isWorker,
            "verify" or "return" => Approver && user.UserId != request.RequesterUserId &&
                        user.UserId != request.TriagedByUserId &&
                        user.UserId != order?.AssignedTechnicianUserId,
            _ => false
        };
        if (!allowed) return Denied<FacilityRequestDto>();

        var replay = await db.Set<FacilityWorkflowEvent>().AsNoTracking().FirstOrDefaultAsync(
            x => x.TenantId == user.TenantId && x.ClientCommandId == command.ClientCommandId, ct);
        if (replay != null)
        {
            if (replay.FacilityServiceRequestId != id ||
                replay.ActorUserId != user.UserId ||
                !string.Equals(replay.ActionCode, action, StringComparison.OrdinalIgnoreCase) ||
                !ReplayPayloadMatches(replay, action, command, request))
                return ApiResponse<FacilityRequestDto>.ErrorResponse("Command ID reused.", 409);

            // An exact retry is acknowledged only while it remains the latest
            // committed transition. A later release, reassignment or independent
            // decision supersedes the earlier result even if the payload matches.
            var latestEventId = await db.Set<FacilityWorkflowEvent>().AsNoTracking()
                .Where(x => x.TenantId == user.TenantId &&
                    x.FacilityServiceRequestId == id && !x.IsDeleted)
                .OrderByDescending(x => x.Id).Select(x => x.Id).FirstOrDefaultAsync(ct);
            if (latestEventId != replay.Id)
                return ApiResponse<FacilityRequestDto>.ErrorResponse(
                    "Command superseded; reload current work order.", 409);
            return ApiResponse<FacilityRequestDto>.SuccessResponse(
                await ReadAsync(request, ct), "Already applied.");
        }

        var requestAction = action is "triage" or "approve" or "reject";
        if (!VersionMatches(requestAction ? request.RowVersion : order?.RowVersion ?? [], command.RowVersion))
            return ApiResponse<FacilityRequestDto>.ErrorResponse("Record changed; reload.", 409);
        // SQL Server application lock serializes scheduling for the same tenant/technician.
        // It is held through the work-order update and audit SaveChanges/commit.
        await using var scheduleTransaction = (action is "schedule" or "vendorapprove" or "start") && db.Database.IsSqlServer()
            ? await db.Database.BeginTransactionAsync(ct) : null;
        var before = requestAction ? request.StateCode : order?.StateCode ?? "None";
        var note = command.Note?.Trim();
        string? auditNote = note;
        var now = Now;
        switch (action)
        {
            case "triage":
                if (request.StateCode != "Submitted" || command.SlaDueAtUtc is not DateTime due ||
                    due.Kind != DateTimeKind.Utc || due <= now)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Triage needs a future UTC SLA deadline.");
                if (note?.Length > 500)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "Triage note must be at most 500 characters.");
                auditNote = $"SLA due UTC={due:O}" +
                    (string.IsNullOrWhiteSpace(note) ? "" : $"; note={note}");
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
            case "cancelrequest":
                // Unstarted and unassigned only. A scheduled order must first use
                // the existing audited schedule-release action.
                if (order?.StateCode != "Approved" ||
                    order?.AssignedTechnicianUserId != null ||
                    order?.VendorStateCode is "Proposed" or "Approved" ||
                    order?.ScheduledStartUtc != null || order?.ScheduledEndUtc != null ||
                    string.IsNullOrWhiteSpace(note) || note.Length is < 10 or > 500)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "Release any schedule first; cancellation requires a reason (10-500 characters).", 409);
                order!.StateCode = "CancellationPending";
                break;
            case "cancelapprove":
            case "cancelreject":
                if (order?.StateCode != "CancellationPending" ||
                    string.IsNullOrWhiteSpace(note) || note.Length is < 10 or > 500)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "Pending cancellation and independent decision reason (10-500 characters) required.", 409);
                order!.StateCode = action == "cancelapprove" ? "Cancelled" : "Approved";
                break;
            case "vendorpropose":
                if (order?.StateCode is not ("Approved" or "Scheduled") ||
                    order.StartedAtUtc != null || order.VendorStateCode is "Proposed" or "Approved" ||
                    command.VendorSupplierId is not > 0 ||
                    command.VendorStartUtc is not DateTime vendorStart || vendorStart.Kind != DateTimeKind.Utc ||
                    command.VendorEndUtc is not DateTime vendorEnd || vendorEnd.Kind != DateTimeKind.Utc ||
                    vendorStart <= now || vendorEnd <= vendorStart ||
                    string.IsNullOrWhiteSpace(note) || note.Length is < 10 or > 500)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "An unstarted work order, eligible vendor, future UTC window and reason (10-500 characters) are required.", 400);
                if (order.StateCode == "Scheduled" &&
                    (order.ScheduledStartUtc is not DateTime technicianFrom ||
                     order.ScheduledEndUtc is not DateTime technicianUntil ||
                     technicianFrom >= vendorEnd || technicianUntil <= vendorStart))
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "Vendor booking must overlap the technician schedule; reschedule or choose another window.", 409);
                if (!await db.ProcurementSuppliers.AsNoTracking().AnyAsync(s =>
                    s.Id == command.VendorSupplierId && s.TenantId == user.TenantId &&
                    s.CampusId == request.CampusId && !s.IsDeleted && s.StateCode == "Approved", ct))
                    return Denied<FacilityRequestDto>();
                auditNote = $"Previous vendor={order.VendorSupplierId?.ToString() ?? "none"}, state={order.VendorStateCode ?? "none"}, start={order.VendorStartUtc:O}, end={order.VendorEndUtc:O}" +
                    $"; new vendor={command.VendorSupplierId}, start={vendorStart:O}, end={vendorEnd:O}; reason={note}";
                order.VendorSupplierId = command.VendorSupplierId;
                order.VendorStartUtc = vendorStart; order.VendorEndUtc = vendorEnd;
                order.VendorProposedByUserId = user.UserId;
                order.VendorApprovedByUserId = null;
                order.VendorStateCode = "Proposed";
                break;
            case "vendorapprove":
                if (order?.StateCode is not ("Approved" or "Scheduled") ||
                    order.StartedAtUtc != null || order.VendorStateCode != "Proposed" ||
                    order.VendorSupplierId is not long vendorId || vendorId <= 0 ||
                    order.VendorStartUtc is not DateTime vendorFrom ||
                    order.VendorEndUtc is not DateTime vendorTo ||
                    vendorFrom <= now || vendorTo <= vendorFrom ||
                    string.IsNullOrWhiteSpace(note) || note.Length is < 10 or > 500)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "A current future vendor proposal and independent decision reason are required.", 409);
                if (order.StateCode == "Scheduled" &&
                    (order.ScheduledStartUtc is not DateTime approvedTechnicianFrom ||
                     order.ScheduledEndUtc is not DateTime approvedTechnicianUntil ||
                     approvedTechnicianFrom >= vendorTo || approvedTechnicianUntil <= vendorFrom))
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "Vendor proposal no longer overlaps the technician schedule; reject and repropose.", 409);
                if (scheduleTransaction != null)
                {
                    var resource = $"FacilityVendor:{user.TenantId}:{vendorId}";
                    try
                    {
                        await db.Database.ExecuteSqlInterpolatedAsync(
                            $"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; IF @result < 0 THROW 51000, 'Vendor booking busy', 1;", ct);
                        // Lock the canonical supplier row through commit so Procurement
                        // cannot revoke its approval between validation and booking.
                        await db.Database.ExecuteSqlInterpolatedAsync(
                            $"IF NOT EXISTS (SELECT 1 FROM dbo.ProcurementSuppliers WITH (UPDLOCK, HOLDLOCK) WHERE TenantId={user.TenantId} AND Id={vendorId} AND CampusId={request.CampusId} AND StateCode=N'Approved' AND IsDeleted=0) THROW 51001, 'Vendor not approved', 1;", ct);
                    }
                    catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 51000 or 51001 or 1205 or 1222)
                    {
                        logger.LogWarning(ex, "Facility vendor eligibility or booking conflict for tenant {TenantId}", user.TenantId);
                        return ApiResponse<FacilityRequestDto>.ErrorResponse("Vendor eligibility changed or booking busy; reload.", 409);
                    }
                }
                else if (!await db.ProcurementSuppliers.AsNoTracking().AnyAsync(s =>
                    s.Id == vendorId && s.TenantId == user.TenantId &&
                    s.CampusId == request.CampusId && !s.IsDeleted && s.StateCode == "Approved", ct))
                    return Denied<FacilityRequestDto>();
                // A proposal may become stale while waiting for the SQL vendor lock.
                if (vendorFrom <= Now)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "Vendor booking window elapsed; propose a future window.", 409);
                if (await db.Set<FacilityWorkOrder>().AsNoTracking().AnyAsync(w =>
                    w.TenantId == user.TenantId && !w.IsDeleted && w.Id != order.Id &&
                    w.VendorSupplierId == vendorId && w.VendorStateCode == "Approved" &&
                    w.VendorStartUtc < vendorTo && w.VendorEndUtc > vendorFrom, ct))
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "Vendor has an overlapping approved booking.", 409);
                order.VendorStateCode = "Approved";
                order.VendorApprovedByUserId = user.UserId;
                break;
            case "vendorreject":
                if (order?.StateCode is not ("Approved" or "Scheduled") ||
                    order.StartedAtUtc != null || order.VendorStateCode != "Proposed" ||
                    string.IsNullOrWhiteSpace(note) || note.Length is < 10 or > 500)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "A pending vendor proposal and independent rejection reason are required.", 409);
                order.VendorStateCode = "Rejected";
                break;
            case "vendorrelease":
                if (order?.StateCode is not ("Approved" or "Scheduled") ||
                    order.StartedAtUtc != null || order.VendorStateCode != "Approved" ||
                    string.IsNullOrWhiteSpace(note) || note.Length is < 10 or > 500)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "An unstarted approved vendor booking and independent release reason are required.", 409);
                order.VendorStateCode = "Released";
                break;
            case "schedule":
                if (note?.Length > 500)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Scheduling note must be at most 500 characters.");
                if (order?.StateCode is not ("Approved" or "Scheduled") ||
                    command.TechnicianUserId is not > 0 ||
                    command.ScheduledStartUtc is not DateTime start || start.Kind != DateTimeKind.Utc ||
                    command.ScheduledEndUtc is not DateTime end || end.Kind != DateTimeKind.Utc ||
                    start >= end || start <= now)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Valid technician and future UTC schedule required.");
                var tech = command.TechnicianUserId.Value;
                // Initial scheduling may omit a note. An existing schedule is an
                // auditable decision: require a reason and reject no-op commands.
                if (order.StateCode == "Scheduled")
                {
                    if (order.AssignedTechnicianUserId == tech &&
                        order.ScheduledStartUtc == start && order.ScheduledEndUtc == end)
                        return ApiResponse<FacilityRequestDto>.ErrorResponse(
                            "Schedule unchanged; reload the current work order.", 409);
                    if (string.IsNullOrWhiteSpace(note) || note.Length is < 10 or > 500)
                        return ApiResponse<FacilityRequestDto>.ErrorResponse(
                            "Rescheduling requires a reason (10-500 characters).", 400);
                }
                if (order.VendorStateCode == "Approved" &&
                    (order.VendorStartUtc is not DateTime bookedFrom ||
                     order.VendorEndUtc is not DateTime bookedUntil ||
                     bookedFrom >= end || bookedUntil <= start))
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "Technician schedule must overlap the approved vendor booking; release or rebook first.", 409);
                if (!await EligibleTechnicians(request.CampusId)
                    .AnyAsync(candidate => candidate.Id == tech, ct))
                    return Denied<FacilityRequestDto>();
                if (scheduleTransaction != null)
                {
                    var resource = $"FacilitySchedule:{user.TenantId}:{tech}";
                    try
                    {
                        await db.Database.ExecuteSqlInterpolatedAsync(
                            $"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; IF @result < 0 THROW 51000, 'Technician schedule busy', 1;", ct);
                    }
                    catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 51000 or 1205 or 1222)
                    {
                        logger.LogWarning(ex, "Facility technician schedule contention for tenant {TenantId}", user.TenantId);
                        return ApiResponse<FacilityRequestDto>.ErrorResponse("Technician schedule busy; retry.", 409);
                    }
                }
                var overlaps = await db.Set<FacilityWorkOrder>().AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && !x.IsDeleted && x.Id != order.Id &&
                    x.AssignedTechnicianUserId == tech &&
                    (x.StateCode == "Scheduled" || x.StateCode == "InProgress" || x.StateCode == "Paused") &&
                    x.ScheduledStartUtc < end && x.ScheduledEndUtc > start, ct);
                if (overlaps)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Technician has an overlapping active work order.", 409);
                auditNote = $"Previous technician={order.AssignedTechnicianUserId?.ToString() ?? "none"}, start={order.ScheduledStartUtc:O}, end={order.ScheduledEndUtc:O}; new technician={tech}, start={start:O}, end={end:O}" +
                    (string.IsNullOrWhiteSpace(note) ? "" : $"; reason={note}");
                order.AssignedTechnicianUserId = tech;
                order.ScheduledStartUtc = start; order.ScheduledEndUtc = end;
                order.StateCode = "Scheduled";
                break;
            case "unschedule":
                if (order?.StateCode != "Scheduled" || string.IsNullOrWhiteSpace(note) ||
                    note.Length is < 5 or > 500)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("A scheduled work order and reason (5-500 characters) are required.", 409);
                auditNote = $"Released technician={order.AssignedTechnicianUserId}, start={order.ScheduledStartUtc:O}, end={order.ScheduledEndUtc:O}; reason={note}";
                order.AssignedTechnicianUserId = null;
                order.ScheduledStartUtc = null;
                order.ScheduledEndUtc = null;
                order.StateCode = "Approved";
                break;
            case "escalate":
                // Manual evidence only; the work-order rowversion serializes escalation
                // with concurrent closure. No automatic SLA policy or GL mutation.
                if (order?.StateCode is not ("Approved" or "Scheduled" or "InProgress" or
                    "Paused" or "PendingVerification") ||
                    request.SlaDueAtUtc is not DateTime slaDue || slaDue >= now ||
                    string.IsNullOrWhiteSpace(note) || note.Length is < 10 or > 500)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "Overdue open work order and escalation reason (10-500 characters) required.", 409);
                auditNote = "Manual overdue SLA escalation: " + note;
                break;
            case "start":
                // Revalidate the live Procurement supplier before consuming an
                // approved booking; keep its row locked through start + audit.
                // The worker must enter the authorized scheduled window, not merely
                // start any time before its end. A future assignment is not
                // permission to begin work early.
                if (order?.StateCode != "Scheduled" || order.VendorStateCode == "Proposed" ||
                    order.ScheduledStartUtc is not DateTime scheduleFrom || scheduleFrom > now ||
                    order.ScheduledEndUtc is not DateTime scheduleUntil || scheduleUntil <= now)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse(
                        "Work-order schedule is not active; wait for its start or reload.", 409);
                if (order.VendorStateCode == "Approved")
                {
                    if (order.VendorSupplierId is not long bookedVendorId || bookedVendorId <= 0 ||
                        order.VendorStartUtc is not DateTime activeVendorStart || activeVendorStart > now ||
                         order.VendorEndUtc is not DateTime vendorUntil || vendorUntil <= now)
                        return ApiResponse<FacilityRequestDto>.ErrorResponse(
                            "Vendor booking expired or incomplete; release and rebook before starting.", 409);
                    if (scheduleTransaction != null)
                    {
                        try
                        {
                            await db.Database.ExecuteSqlInterpolatedAsync(
                                $"IF NOT EXISTS (SELECT 1 FROM dbo.ProcurementSuppliers WITH (UPDLOCK, HOLDLOCK) WHERE TenantId={user.TenantId} AND Id={bookedVendorId} AND CampusId={request.CampusId} AND StateCode=N'Approved' AND IsDeleted=0) THROW 51001, 'Booked vendor no longer eligible', 1;", ct);
                        }
                        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 51001 or 1205 or 1222)
                        {
                            logger.LogWarning(ex, "Facility vendor eligibility changed before start for tenant {TenantId}", user.TenantId);
                            return ApiResponse<FacilityRequestDto>.ErrorResponse(
                                "Booked vendor is no longer approved; release and rebook before starting.", 409);
                        }
                    }
                    else if (!await db.ProcurementSuppliers.AsNoTracking().AnyAsync(s =>
                        s.TenantId == user.TenantId && s.Id == bookedVendorId &&
                        s.CampusId == request.CampusId && s.StateCode == "Approved" && !s.IsDeleted, ct))
                        return ApiResponse<FacilityRequestDto>.ErrorResponse(
                            "Booked vendor is no longer approved; release and rebook before starting.", 409);
                    // Reject if the clock advanced while waiting for the SQL lock.
                    if (vendorUntil <= Now || scheduleUntil <= Now)
                        return ApiResponse<FacilityRequestDto>.ErrorResponse(
                            "Work-order or vendor window elapsed; reload before starting.", 409);
                }
                // A technician can lose active membership, campus grant or role
                // after assignment. Validate live authorization, not old claims.
                if (!await EligibleTechnicians(request.CampusId)
                    .AnyAsync(candidate => candidate.Id == user.UserId, ct))
                    return Denied<FacilityRequestDto>();
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
            case "return":
                if (order?.StateCode != "PendingVerification" || string.IsNullOrWhiteSpace(note) ||
                    note.Length is < 10 or > 500)
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Return requires pending verification and reason (10-500 characters).", 409);
                // The prior completion evidence remains in the immutable complete event.
                // Clear only the current projection so a returned job cannot count as complete.
                order.CompletedAtUtc = null;
                order.CompletionEvidence = null;
                order.StateCode = "InProgress";
                break;
            case "verify":
                if (order?.StateCode != "PendingVerification" || string.IsNullOrWhiteSpace(note))
                    return ApiResponse<FacilityRequestDto>.ErrorResponse("Independent verification note required.");
                order.VerifiedAtUtc = now; order.VerifiedByUserId = user.UserId;
                order.StateCode = "Closed";
                break;
        }

        // Locks and eligibility checks may outlast an initially valid window.
        // Re-read the injected clock immediately before persistence. A rejected
        // command changes neither the projection nor its immutable event history.
        if (action is "schedule" or "vendorapprove" or "start")
        {
            var commitNow = Now;
            if (action == "schedule" && order?.ScheduledStartUtc <= commitNow)
                return ApiResponse<FacilityRequestDto>.ErrorResponse(
                    "Schedule start elapsed while validating; choose a future window.", 409);
            if (action == "vendorapprove" && order?.VendorStartUtc <= commitNow)
                return ApiResponse<FacilityRequestDto>.ErrorResponse(
                    "Vendor booking start elapsed while validating; propose a new window.", 409);
            if (action == "start" &&
                (order?.ScheduledStartUtc is not DateTime commitScheduleStart ||
                 commitScheduleStart > commitNow || order.ScheduledEndUtc <= commitNow ||
                 (order.VendorStateCode == "Approved" &&
                  (order.VendorStartUtc is not DateTime commitVendorStart ||
                   commitVendorStart > commitNow || order.VendorEndUtc <= commitNow))))
                return ApiResponse<FacilityRequestDto>.ErrorResponse(
                    "Work-order or vendor window is not active after validation; reload.", 409);
            // Recheck after SQL locks and other awaited validations, before audit.
            if ((action == "start" || action == "schedule") &&
                order?.AssignedTechnicianUserId is long assignedId &&
                !await EligibleTechnicians(request.CampusId)
                    .AnyAsync(candidate => candidate.Id == assignedId, ct))
                return Denied<FacilityRequestDto>();
            if (action == "start") order!.StartedAtUtc = commitNow;
            now = commitNow;
        }

        if (requestAction) { request.UpdatedAt = now; request.UpdatedBy = user.UserId; }
        else if (order != null) { order.UpdatedAt = now; order.UpdatedBy = user.UserId; }
        db.Set<FacilityWorkflowEvent>().Add(new FacilityWorkflowEvent
        {
            TenantId = user.TenantId, FacilityServiceRequestId = id,
            ClientCommandId = command.ClientCommandId, ActorUserId = user.UserId,
            ActionCode = action, FromState = before,
            ToState = requestAction ? request.StateCode : order!.StateCode,
            Note = auditNote, CreatedAt = now, CreatedBy = user.UserId
        });
        try
        {
            await db.SaveChangesAsync(ct);
            if (scheduleTransaction != null) await scheduleTransaction.CommitAsync(ct);
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
        if (!await VisibleRequests().AnyAsync(x => x.Id == id, ct))
            return ApiResponse<PagedResult<FacilityEventDto>>.ErrorResponse("Request not found.", 404);
        var query = db.Set<FacilityWorkflowEvent>().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.FacilityServiceRequestId == id && !x.IsDeleted);
        var count = await query.CountAsync(ct);
        var canReadVendorDetails = Manager || Approver;
        var rows = await query.OrderBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new FacilityEventDto
            {
                Id = x.Id, ActionCode = x.ActionCode, FromState = x.FromState,
                ToState = x.ToState, ActorUserId = x.ActorUserId,
                OccurredAtUtc = x.CreatedAt,
                // Vendor IDs, reasons and commercial booking windows are not
                // disclosed to requesters or technicians through history.
                Note = canReadVendorDetails || !x.ActionCode.StartsWith("vendor")
                    ? x.Note : "Vendor decision recorded."
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<FacilityEventDto>>.SuccessResponse(new()
        { Page = page, PageSize = pageSize, TotalCount = count, Items = rows });
    }
}