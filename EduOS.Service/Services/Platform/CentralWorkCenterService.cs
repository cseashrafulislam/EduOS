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
        public string Reference { get; set; } = string.Empty;
        public long? CampusId { get; set; }
        public DateTime SubmittedAtUtc { get; set; }
    }

    public async Task<ApiResponse<CentralWorkCenterPageDto>> GetPendingAsync(
        int page = 1, int pageSize = 25, long? campusId = null, CancellationToken ct = default,
        string? source = null, string? reference = null, string? order = null)
    {
        if (!user.IsAuthenticated || user.UserId <= 0 || user.TenantId <= 0 ||
            db.CurrentTenantId != user.TenantId)
            return ApiResponse<CentralWorkCenterPageDto>.ErrorResponse("Valid institution session required.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || campusId is <= 0 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<CentralWorkCenterPageDto>.ErrorResponse("Invalid work list page or campus.", 400);

        reference = reference?.Trim();
        if (reference is { Length: > 80 } || (reference != null && reference.Any(char.IsControl)) ||
            (order != null && order is not ("oldest" or "newest")))
            return ApiResponse<CentralWorkCenterPageDto>.ErrorResponse("Invalid work search or ordering.", 400);

        if (!string.IsNullOrEmpty(source) && source is not ("ProcurementRequisition" or "ProcurementPurchaseOrder" or "FacilityVisitorPolicy" or "AccountingJournal"))
            return ApiResponse<CentralWorkCenterPageDto>.ErrorResponse("Invalid work source.", 400);

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
        // Institution-wide manual journals have no campus; source Finance still
        // owns approval, rowversion validation and posting, never this inbox.
        var finance = liveFacilitiesRole &&
            await modules.IsCurrentTenantModuleAvailableAsync("FINANCE");

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
                r.CreatedBy.HasValue && r.CreatedBy != user.UserId && visibleCampuses.Contains(r.CampusId) &&
                (!campusId.HasValue || r.CampusId == campusId.Value))
                .Select(r => new WorkIdentity { Kind = 1, SourceId = r.Id, Reference = r.RequestNumber,
                    CampusId = r.CampusId, SubmittedAtUtc = r.SubmittedAt ?? r.CreatedAt });
            var orders = db.ProcurementPurchaseOrders.AsNoTracking().Where(o =>
                o.TenantId == tenant && !o.IsDeleted &&
                (o.StateCode == "Submitted" || o.StateCode == "CancelRequested") &&
                o.CreatedBy.HasValue && o.CreatedBy != user.UserId && visibleCampuses.Contains(o.CampusId) &&
                (!campusId.HasValue || o.CampusId == campusId.Value))
                .Select(o => new WorkIdentity { Kind = 2, SourceId = o.Id, Reference = o.OrderNumber,
                    CampusId = o.CampusId, SubmittedAtUtc = o.SubmittedAt ?? o.CreatedAt });
            pending = requisitions.Concat(orders);
        }
        if (facilities)
        {
            var policyChanges = db.FacilityVisitorPolicyChanges.AsNoTracking().Where(p =>
                p.TenantId == tenant && !p.IsDeleted && p.StateCode == "Submitted" &&
                p.RequestedByUserId != user.UserId && visibleCampuses.Contains(p.CampusId) &&
                (!campusId.HasValue || p.CampusId == campusId.Value))
                .Select(p => new WorkIdentity { Kind = 3, SourceId = p.Id, Reference = p.PolicyCode,
                    CampusId = p.CampusId, SubmittedAtUtc = p.CreatedAt });
            pending = pending == null ? policyChanges : pending.Concat(policyChanges);
        }
        if (finance && !campusId.HasValue)
        {
            var journals = db.Journals.AsNoTracking().Where(j =>
                j.TenantId == tenant && !j.IsDeleted && j.CampusId == null &&
                j.State == JournalState.Submitted && j.CreatedBy.HasValue &&
                j.CreatedBy != user.UserId &&
                ((j.SourceType == "Manual" && j.SourceId == null && j.ReversalOfJournalId == null) ||
                 (j.SourceType == "ManualReversal" && j.SourceId != null && j.ReversalOfJournalId != null)))
                .Select(j => new WorkIdentity { Kind = 4, SourceId = j.Id, Reference = j.JournalNumber,
                    CampusId = j.CampusId, SubmittedAtUtc = j.UpdatedAt ?? j.CreatedAt });
            pending = pending == null ? journals : pending.Concat(journals);
        }

        var result = new CentralWorkCenterPageDto
        {
            InventoryIncluded = inventory, FacilitiesIncluded = facilities,
            FinanceIncluded = finance && !campusId.HasValue,
            Pending = new PagedResult<CentralWorkItemDto> { Page = page, PageSize = pageSize }
        };
        if (pending == null)
            return ApiResponse<CentralWorkCenterPageDto>.SuccessResponse(result);

        // Reference search is applied to the canonical union before counts and paging.
        // The source reference is not a student/person identifier and is never expanded to PII.
        if (!string.IsNullOrEmpty(reference))
            pending = pending.Where(x => x.Reference.Contains(reference));

        // One authoritative union query produces the same scoped counts as the drill-down.
        // Do not compute dashboard totals from a page or from a separate snapshot.
        var grouped = await pending.GroupBy(x => x.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() }).ToListAsync(ct);
        var counts = grouped.ToDictionary(x => x.Kind, x => x.Count);
        if (inventory)
        {
            result.SourceCounts.Add(new CentralWorkSourceCountDto { Source = "ProcurementRequisition", Count = counts.GetValueOrDefault(1) });
            result.SourceCounts.Add(new CentralWorkSourceCountDto { Source = "ProcurementPurchaseOrder", Count = counts.GetValueOrDefault(2) });
        }
        if (facilities)
            result.SourceCounts.Add(new CentralWorkSourceCountDto { Source = "FacilityVisitorPolicy", Count = counts.GetValueOrDefault(3) });
        if (finance && !campusId.HasValue)
            result.SourceCounts.Add(new CentralWorkSourceCountDto { Source = "AccountingJournal", Count = counts.GetValueOrDefault(4) });

        // Filter at the canonical query before Count/Skip/Take; never filter a single page in the browser.
        if (!string.IsNullOrEmpty(source))
        {
            var kind = source switch
            {
                "ProcurementRequisition" => 1,
                "ProcurementPurchaseOrder" => 2,
                "FacilityVisitorPolicy" => 3,
                _ => 4
            };
            pending = pending.Where(x => x.Kind == kind);
        }
        result.Pending.TotalCount = await pending.CountAsync(ct);
        var ordered = order == "newest"
            ? pending.OrderByDescending(x => x.SubmittedAtUtc).ThenBy(x => x.Kind).ThenBy(x => x.SourceId)
            : pending.OrderBy(x => x.SubmittedAtUtc).ThenBy(x => x.Kind).ThenBy(x => x.SourceId);
        var identities = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var requisitionIds = identities.Where(x => x.Kind == 1).Select(x => x.SourceId).ToArray();
        var orderIds = identities.Where(x => x.Kind == 2).Select(x => x.SourceId).ToArray();
        var policyIds = identities.Where(x => x.Kind == 3).Select(x => x.SourceId).ToArray();
        var journalIds = identities.Where(x => x.Kind == 4).Select(x => x.SourceId).ToArray();
        var req = await db.ProcurementRequisitions.AsNoTracking().Where(x =>
            x.TenantId == tenant && requisitionIds.Contains(x.Id) && !x.IsDeleted &&
            x.StateCode == "Submitted" && x.CreatedBy.HasValue && x.CreatedBy != user.UserId &&
            visibleCampuses.Contains(x.CampusId) && (!campusId.HasValue || x.CampusId == campusId.Value))
            .Select(x => new { x.Id, x.RequestNumber }).ToDictionaryAsync(x => x.Id, ct);
        var po = await db.ProcurementPurchaseOrders.AsNoTracking().Where(x =>
            x.TenantId == tenant && orderIds.Contains(x.Id) && !x.IsDeleted &&
            (x.StateCode == "Submitted" || x.StateCode == "CancelRequested") &&
            x.CreatedBy.HasValue && x.CreatedBy != user.UserId &&
            visibleCampuses.Contains(x.CampusId) && (!campusId.HasValue || x.CampusId == campusId.Value))
            .Select(x => new { x.Id, x.OrderNumber, x.StateCode }).ToDictionaryAsync(x => x.Id, ct);
        var policies = await db.FacilityVisitorPolicyChanges.AsNoTracking().Where(x =>
            x.TenantId == tenant && policyIds.Contains(x.Id) && !x.IsDeleted &&
            x.StateCode == "Submitted" && x.RequestedByUserId != user.UserId &&
            visibleCampuses.Contains(x.CampusId) && (!campusId.HasValue || x.CampusId == campusId.Value))
            .Select(x => new { x.Id, x.PolicyCode }).ToDictionaryAsync(x => x.Id, ct);
        var financeJournals = await db.Journals.AsNoTracking().Where(x =>
            x.TenantId == tenant && journalIds.Contains(x.Id) && !x.IsDeleted &&
            x.CampusId == null && x.State == JournalState.Submitted &&
            x.CreatedBy.HasValue && x.CreatedBy != user.UserId &&
            ((x.SourceType == "Manual" && x.SourceId == null && x.ReversalOfJournalId == null) ||
             (x.SourceType == "ManualReversal" && x.SourceId != null && x.ReversalOfJournalId != null)))
            .Select(x => new { x.Id, x.JournalNumber, x.PublicId }).ToDictionaryAsync(x => x.Id, ct);
        if (req.Count != requisitionIds.Length || po.Count != orderIds.Length ||
            policies.Count != policyIds.Length || financeJournals.Count != journalIds.Length)
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
            3 => new CentralWorkItemDto { Source = "FacilityVisitorPolicy", SourceId = x.SourceId,
                CampusId = x.CampusId, Reference = policies[x.SourceId].PolicyCode,
                State = "Submitted", SubmittedAtUtc = DateTime.SpecifyKind(x.SubmittedAtUtc, DateTimeKind.Utc),
                WorkspaceUrl = "/FacilityVisitors/Index" },
            _ => new CentralWorkItemDto { Source = "AccountingJournal", SourceId = x.SourceId,
                SourceReference = financeJournals[x.SourceId].PublicId,
                CampusId = null, Reference = financeJournals[x.SourceId].JournalNumber,
                State = "Submitted", SubmittedAtUtc = DateTime.SpecifyKind(x.SubmittedAtUtc, DateTimeKind.Utc),
                WorkspaceUrl = "/AccountingJournals/Index" }
        }).ToList();
        return ApiResponse<CentralWorkCenterPageDto>.SuccessResponse(result);
    }

    private Task<bool> LiveRoleAsync(string[] names, CancellationToken ct) =>
        CentralPrivilegedAccess.HasRoleAsync(db, user, names, ct);
}
