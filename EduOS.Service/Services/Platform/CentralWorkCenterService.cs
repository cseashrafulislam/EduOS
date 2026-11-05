using EduOS.Core.Common;
using EduOS.Core.DTOs.Platform;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace EduOS.Service.Services.Platform;

/// <summary>
/// Permission-scoped read-side composition; it owns no approval state or decision.
/// Each decision is still made by its source API with the source rowversion.
/// </summary>
public sealed class CentralWorkCenterService(
    EduOSDbContext db, ICurrentUserService user, ITenantModuleService modules,
    IConfiguration configuration) : ICentralWorkCenterService
{
    private sealed class WorkIdentity
    {
        public int Kind { get; set; }
        public long SourceId { get; set; }
        public long CampusId { get; set; }
        public DateTime SubmittedAtUtc { get; set; }
    }

    public async Task<ApiResponse<CentralWorkCenterPageDto>> GetPendingAsync(
        int page = 1, int pageSize = 25, long? campusId = null, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || user.UserId <= 0 || user.TenantId <= 0 ||
            db.CurrentTenantId != user.TenantId)
            return ApiResponse<CentralWorkCenterPageDto>.ErrorResponse("Valid institution session required.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || campusId is <= 0 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<CentralWorkCenterPageDto>.ErrorResponse("Invalid work list page or campus.", 400);

        var active = await db.Users.AsNoTracking().AnyAsync(x =>
            x.Id == user.UserId && x.IsActive && !x.IsDeleted, ct) &&
            await db.TenantMemberships.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.UserId == user.UserId &&
                x.Status == MembershipStatus.Active && !x.IsDeleted, ct);
        if (!active)
            return ApiResponse<CentralWorkCenterPageDto>.ErrorResponse("Institution membership is no longer active.", 403);

        var inventoryRole = user.IsTenantAdmin || user.IsInRole("Principal") ||
            user.IsInRole("InventoryManager");
        var facilitiesRole = user.IsTenantAdmin || user.IsInRole("Principal");
        // A revoked cookie role must not reveal pending cross-module requests.
        var liveInventoryRole = inventoryRole && await LiveRoleAsync(
            new[] { "TENANTADMIN", "PRINCIPAL", "INVENTORYMANAGER" }, ct);
        var liveFacilitiesRole = facilitiesRole && await LiveRoleAsync(
            new[] { "TENANTADMIN", "PRINCIPAL" }, ct);
        if (!liveInventoryRole && !liveFacilitiesRole)
            return ApiResponse<CentralWorkCenterPageDto>.ErrorResponse("Approval review access required.", 403);

        // The pilot policy blocks these modules even if a tenant selection remains enabled.
        var pilot = configuration.GetValue<bool>("PilotRelease:Enabled");
        var inventory = !pilot && liveInventoryRole &&
            await modules.IsCurrentTenantModuleAvailableAsync("INVENTORY");
        var facilities = !pilot && liveFacilitiesRole &&
            await modules.IsCurrentTenantModuleAvailableAsync("FACILITIES");

        var tenant = user.TenantId;
        var allCampuses = (user.IsTenantAdmin &&
            await LiveRoleAsync(new[] { "TENANTADMIN" }, ct)) ||
            (user.IsInRole("Principal") && await LiveRoleAsync(new[] { "PRINCIPAL" }, ct));
        var visibleCampuses = db.Campuses.AsNoTracking().Where(c =>
            c.TenantId == tenant && c.IsActive && !c.IsDeleted &&
            (allCampuses ||
             db.UserCampusAccesses.Any(a => a.TenantId == tenant && a.UserId == user.UserId &&
                 a.CampusId == c.Id && a.IsActive && !a.IsDeleted))).Select(c => c.Id);
        if (campusId.HasValue && !await visibleCampuses.ContainsAsync(campusId.Value, ct))
            return ApiResponse<CentralWorkCenterPageDto>.ErrorResponse("Campus access denied.", 403);

        IQueryable<WorkIdentity>? pending = null;
        if (inventory)
        {
            var requisitions = db.ProcurementRequisitions.AsNoTracking().Where(r =>
                r.TenantId == tenant && !r.IsDeleted && r.StateCode == "Submitted" &&
                r.CreatedBy.HasValue && r.CreatedBy != user.UserId && visibleCampuses.Contains(r.CampusId))
                .Select(r => new WorkIdentity { Kind = 1, SourceId = r.Id,
                    CampusId = r.CampusId, SubmittedAtUtc = r.SubmittedAt ?? r.CreatedAt });
            var orders = db.ProcurementPurchaseOrders.AsNoTracking().Where(o =>
                o.TenantId == tenant && !o.IsDeleted &&
                (o.StateCode == "Submitted" || o.StateCode == "CancelRequested") &&
                o.CreatedBy.HasValue && o.CreatedBy != user.UserId && visibleCampuses.Contains(o.CampusId))
                .Select(o => new WorkIdentity { Kind = 2, SourceId = o.Id,
                    CampusId = o.CampusId, SubmittedAtUtc = o.SubmittedAt ?? o.CreatedAt });
            pending = requisitions.Concat(orders);
        }
        if (facilities)
        {
            var policyChanges = db.FacilityVisitorPolicyChanges.AsNoTracking().Where(p =>
                p.TenantId == tenant && !p.IsDeleted && p.StateCode == "Submitted" &&
                p.RequestedByUserId != user.UserId && visibleCampuses.Contains(p.CampusId))
                .Select(p => new WorkIdentity { Kind = 3, SourceId = p.Id,
                    CampusId = p.CampusId, SubmittedAtUtc = p.CreatedAt });
            pending = pending == null ? policyChanges : pending.Concat(policyChanges);
        }

        var result = new CentralWorkCenterPageDto
        {
            InventoryIncluded = inventory, FacilitiesIncluded = facilities,
            Pending = new PagedResult<CentralWorkItemDto> { Page = page, PageSize = pageSize }
        };
        if (pending == null)
            return ApiResponse<CentralWorkCenterPageDto>.SuccessResponse(result);

        result.Pending.TotalCount = await pending.CountAsync(ct);
        var identities = await pending.OrderBy(x => x.SubmittedAtUtc).ThenBy(x => x.Kind)
            .ThenBy(x => x.SourceId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var requisitionIds = identities.Where(x => x.Kind == 1).Select(x => x.SourceId).ToArray();
        var orderIds = identities.Where(x => x.Kind == 2).Select(x => x.SourceId).ToArray();
        var policyIds = identities.Where(x => x.Kind == 3).Select(x => x.SourceId).ToArray();
        var req = await db.ProcurementRequisitions.AsNoTracking().Where(x =>
            x.TenantId == tenant && requisitionIds.Contains(x.Id) && !x.IsDeleted &&
            x.StateCode == "Submitted" && x.CreatedBy.HasValue && x.CreatedBy != user.UserId)
            .Select(x => new { x.Id, x.RequestNumber }).ToDictionaryAsync(x => x.Id, ct);
        var po = await db.ProcurementPurchaseOrders.AsNoTracking().Where(x =>
            x.TenantId == tenant && orderIds.Contains(x.Id) && !x.IsDeleted &&
            (x.StateCode == "Submitted" || x.StateCode == "CancelRequested") &&
            x.CreatedBy.HasValue && x.CreatedBy != user.UserId)
            .Select(x => new { x.Id, x.OrderNumber, x.StateCode }).ToDictionaryAsync(x => x.Id, ct);
        var policies = await db.FacilityVisitorPolicyChanges.AsNoTracking().Where(x =>
            x.TenantId == tenant && policyIds.Contains(x.Id) && !x.IsDeleted &&
            x.StateCode == "Submitted" && x.RequestedByUserId != user.UserId)
            .Select(x => new { x.Id, x.PolicyCode }).ToDictionaryAsync(x => x.Id, ct);
        if (req.Count != requisitionIds.Length || po.Count != orderIds.Length ||
            policies.Count != policyIds.Length)
            return ApiResponse<CentralWorkCenterPageDto>.ErrorResponse(
                "Approval state changed while loading; refresh the work list.", 409);

        result.Pending.Items = identities.Select(x => x.Kind switch
        {
            1 => new CentralWorkItemDto { Source = "ProcurementRequisition", SourceId = x.SourceId,
                CampusId = x.CampusId, Reference = req[x.SourceId].RequestNumber,
                State = "Submitted", SubmittedAtUtc = DateTime.SpecifyKind(x.SubmittedAtUtc, DateTimeKind.Utc),
                WorkspaceUrl = "/ProcurementRequisitions/Index" },
            2 => new CentralWorkItemDto { Source = "ProcurementPurchaseOrder", SourceId = x.SourceId,
                CampusId = x.CampusId, Reference = po[x.SourceId].OrderNumber,
                State = po[x.SourceId].StateCode, SubmittedAtUtc = DateTime.SpecifyKind(x.SubmittedAtUtc, DateTimeKind.Utc),
                WorkspaceUrl = "/ProcurementPurchaseOrders/Index" },
            _ => new CentralWorkItemDto { Source = "FacilityVisitorPolicy", SourceId = x.SourceId,
                CampusId = x.CampusId, Reference = policies[x.SourceId].PolicyCode,
                State = "Submitted", SubmittedAtUtc = DateTime.SpecifyKind(x.SubmittedAtUtc, DateTimeKind.Utc),
                WorkspaceUrl = "/FacilityVisitors/Index" }
        }).ToList();
        return ApiResponse<CentralWorkCenterPageDto>.SuccessResponse(result);
    }

    private Task<bool> LiveRoleAsync(string[] names, CancellationToken ct) =>
        db.UserRoles.AsNoTracking().AnyAsync(ur => ur.UserId == user.UserId &&
            db.Roles.IgnoreQueryFilters().AsNoTracking().Any(r => r.Id == ur.RoleId &&
                r.IsActive && !r.IsDeleted &&
                (r.TenantId == user.TenantId || (r.TenantId == null && r.IsSystemRole)) &&
                names.Contains(r.NormalizedName!)), ct);
}
