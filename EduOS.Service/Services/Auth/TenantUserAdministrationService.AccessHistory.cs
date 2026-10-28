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
        long membershipId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<PagedResult<TenantMemberAccessHistoryDto>>();
        if (membershipId <= 0 || page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<TenantMemberAccessHistoryDto>>.ErrorResponse(
                "Invalid membership or paging parameters.", 400);
        var memberExists = await _db.TenantMemberships.AsNoTracking().AnyAsync(x =>
            x.Id == membershipId && x.TenantId == TenantId && !x.IsDeleted, ct);
        if (!memberExists)
            return ApiResponse<PagedResult<TenantMemberAccessHistoryDto>>.ErrorResponse(
                "Institution membership not found.", 404);

        // Never query generic audit records by user ID: one account may belong to
        // multiple institutions. The canonical membership and tenant must both match.
        var query = MemberHistoryQuery(membershipId);
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

    private IQueryable<AuditLog> MemberHistoryQuery(long membershipId) =>
        _db.AuditLogs.AsNoTracking().Where(x =>
            x.TenantId == TenantId && x.EntityName == nameof(TenantMembership) &&
            x.EntityId == membershipId && !x.IsDeleted && x.IsSuccess &&
            (x.Action == "TenantMemberRolesChanged" ||
             x.Action == "TenantMemberCampusesChanged" ||
             x.Action == "TenantMemberStatusChanged"));

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
        long membershipId, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<string>();
        if (membershipId <= 0)
            return ApiResponse<string>.ErrorResponse("Invalid membership.", 400);
        if (!await _db.TenantMemberships.AsNoTracking().AnyAsync(x =>
            x.Id == membershipId && x.TenantId == TenantId && !x.IsDeleted, ct))
            return ApiResponse<string>.ErrorResponse("Institution membership not found.", 404);

        // One ordered SQL query avoids page-shift duplication while concurrent audits are appended.
        // Reject oversize exports rather than silently truncating an administrator's evidence.
        var rows = await MemberHistoryQuery(membershipId)
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
                HistoryCsvCell(row.OccurredAt.ToUniversalTime().ToString("O")),
                HistoryCsvCell(row.Action),
                HistoryCsvCell(actor),
                HistoryCsvCell(row.OldValue),
                HistoryCsvCell(row.NewValue)
            })).Append("\r\n");
        }
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }

}
