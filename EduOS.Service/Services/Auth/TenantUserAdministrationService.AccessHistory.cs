using EduOS.Core.Common;
using EduOS.Core.DTOs.Auth;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.System;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Auth;

public sealed partial class TenantUserAdministrationService
{
    public async Task<ApiResponse<PagedResult<TenantMemberAccessHistoryDto>>> GetMemberAccessHistoryAsync(
        long membershipId, int page, int pageSize, CancellationToken ct = default, string? change = null, DateOnly? from = null, DateOnly? to = null)
    {
        if (!CanManage) return Denied<PagedResult<TenantMemberAccessHistoryDto>>();
        if (membershipId <= 0 || page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize || !ValidHistoryChange(change) || !ValidHistoryRange(from, to))
            return ApiResponse<PagedResult<TenantMemberAccessHistoryDto>>.ErrorResponse(
                "Invalid membership or paging parameters.", 400);
        var member = await _db.TenantMemberships.AsNoTracking()
            .Where(x => x.Id == membershipId && x.TenantId == TenantId && !x.IsDeleted)
            .Select(x => new { x.UserId }).SingleOrDefaultAsync(ct);
        if (member == null)
            return ApiResponse<PagedResult<TenantMemberAccessHistoryDto>>.ErrorResponse(
                "Institution membership not found.", 404);

        // Never query generic audit records by user ID: one account may belong to
        // multiple institutions. The canonical membership and tenant must both match.
        var query = MemberHistoryQuery(membershipId, member.UserId, change, from, to);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.OccurredAt, x.Action, x.UserName, x.UserId,
                x.OldValue, x.NewValue }).ToListAsync(ct);
        return ApiResponse<PagedResult<TenantMemberAccessHistoryDto>>.SuccessResponse(new()
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            Items = rows.Select(x => new TenantMemberAccessHistoryDto
            {
                Id = x.Id, OccurredAt = x.OccurredAt, Action = x.Action,
                Actor = x.UserName ?? (x.UserId.HasValue ? "User #" + x.UserId.Value : "System"),
                PreviousValue = x.OldValue, NewValue = x.NewValue
            }).ToList()
        });
    }

    private static bool ValidHistoryChange(string? change) =>
        change is null or "all" or "roles" or "campuses" or "status" or "permissions";

    // Date-only filters are UTC calendar days, inclusive at both ends. Keep old
    // no-date callers compatible, but reject half-ranges and oversized windows.
    private static bool ValidHistoryRange(DateOnly? from, DateOnly? to) =>
        (from is null && to is null) ||
        (from.HasValue && to.HasValue && from.Value <= to.Value && to.Value < DateOnly.MaxValue &&
         to.Value.DayNumber - from.Value.DayNumber <= 365);

    private IQueryable<AuditLog> MemberHistoryQuery(long membershipId, long memberUserId,
        string? change, DateOnly? from, DateOnly? to)
    {
        // New permission audits are membership-linked. Legacy permission audits refer to
        // UserPermission IDs instead; retain soft-deleted rows for historical readback only.
        // Explicit trusted tenant AND membership-user checks constrain IgnoreQueryFilters.
        var history = _db.AuditLogs.AsNoTracking().Where(x =>
            x.TenantId == TenantId && !x.IsDeleted && x.IsSuccess &&
            ((x.EntityName == nameof(TenantMembership) && x.EntityId == membershipId &&
                (x.Action == "TenantMemberRolesChanged" ||
                 x.Action == "TenantMemberCampusesChanged" ||
                 x.Action == "TenantMemberStatusChanged" ||
                 x.Action == "TenantMemberPermissionOverrideChanged")) ||
             (x.EntityName == nameof(UserPermission) &&
                x.Action == "TenantMemberPermissionOverrideChanged" &&
                x.EntityId.HasValue && _db.UserPermissions.IgnoreQueryFilters().Any(permission =>
                    permission.Id == x.EntityId.Value && permission.TenantId == TenantId &&
                    permission.UserId == memberUserId))));
        if (from.HasValue && to.HasValue)
        {
            var startUtc = DateTime.SpecifyKind(from.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var endExclusiveUtc = DateTime.SpecifyKind(to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            history = history.Where(x => x.OccurredAt >= startUtc && x.OccurredAt < endExclusiveUtc);
        }
        return change switch
        {
            "roles" => history.Where(x => x.Action == "TenantMemberRolesChanged"),
            "campuses" => history.Where(x => x.Action == "TenantMemberCampusesChanged"),
            "status" => history.Where(x => x.Action == "TenantMemberStatusChanged"),
            "permissions" => history.Where(x => x.Action == "TenantMemberPermissionOverrideChanged"),
            _ => history
        };
    }

    private static string HistoryCsvCell(string? value)
    {
        var text = value ?? string.Empty;
        var trimmed = text.TrimStart(' ', '\t', '\r', '\n');
        if (trimmed.Length > 0 && (trimmed[0] is '=' or '+' or '-' or '@'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>Bounded, tenant-authorized export of the same canonical member access events.</summary>
    public async Task<ApiResponse<string>> ExportMemberAccessHistoryCsvAsync(
        long membershipId, CancellationToken ct = default, string? change = null,
        DateOnly? from = null, DateOnly? to = null)
    {
        if (!CanManage) return Denied<string>();
        if (membershipId <= 0 || !ValidHistoryChange(change) || !ValidHistoryRange(from, to))
            return ApiResponse<string>.ErrorResponse("Invalid membership or history filter.", 400);
        var member = await _db.TenantMemberships.AsNoTracking()
            .Where(x => x.Id == membershipId && x.TenantId == TenantId && !x.IsDeleted)
            .Select(x => new { x.UserId }).SingleOrDefaultAsync(ct);
        if (member == null)
            return ApiResponse<string>.ErrorResponse("Institution membership not found.", 404);

        // One ordered SQL query avoids page-shift duplication while concurrent audits are appended.
        // Reject oversize exports rather than silently truncating an administrator's evidence.
        var rows = await MemberHistoryQuery(membershipId, member.UserId, change, from, to)
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Take(1001)
            .Select(x => new { x.OccurredAt, x.Action, x.UserName, x.UserId, x.OldValue, x.NewValue })
            .ToListAsync(ct);
        if (rows.Count > 1000)
            return ApiResponse<string>.ErrorResponse("Access history exceeds the 1,000-row export limit.", 413);

        var csv = new StringBuilder("\uFEFFOccurred UTC,Change,Actor,Previous,New\r\n");
        foreach (var row in rows)
        {
            var actor = row.UserName ?? (row.UserId.HasValue ? "User #" + row.UserId.Value : "System");
            csv.Append(string.Join(",", new[]
            {
                HistoryCsvCell(DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc).ToString("O")),
                HistoryCsvCell(row.Action),
                HistoryCsvCell(actor),
                HistoryCsvCell(row.OldValue),
                HistoryCsvCell(row.NewValue)
            })).Append("\r\n");
        }
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }

}
