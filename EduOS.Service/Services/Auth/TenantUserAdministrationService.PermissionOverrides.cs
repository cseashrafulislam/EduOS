using System.Data;
using System.Text.Json;
using EduOS.Core.Constants;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Auth;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Auth;

public sealed partial class TenantUserAdministrationService
{
    private static TenantMemberPermissionOverrideDto MapOverride(UserPermission row, string code) => new()
    {
        PermissionId = row.PermissionId,
        PermissionCode = code,
        Mode = row.IsAllowed ? "Allow" : "Deny",
        Reason = row.Reason,
        ExpiresAt = row.ExpiresAt,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    public async Task<ApiResponse<IReadOnlyList<TenantMemberPermissionOverrideDto>>> GetMemberPermissionOverridesAsync(
        long membershipId, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<IReadOnlyList<TenantMemberPermissionOverrideDto>>();
        var member = await _db.TenantMemberships.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == membershipId && x.TenantId == TenantId && !x.IsDeleted, ct);
        if (member == null) return ApiResponse<IReadOnlyList<TenantMemberPermissionOverrideDto>>
            .ErrorResponse("Institution member not found.", 404);
        var rows = await (from entry in _db.UserPermissions.AsNoTracking()
            join permission in _db.Permissions.AsNoTracking() on entry.PermissionId equals permission.Id
            where entry.TenantId == TenantId && entry.UserId == member.UserId &&
                !entry.IsDeleted && !permission.IsDeleted && permission.IsActive
            orderby permission.ModuleCode, permission.Code
            select new { Entry = entry, permission.Code }).ToListAsync(ct);
        IReadOnlyList<TenantMemberPermissionOverrideDto> result = rows
            .Where(x => !x.Code.Equals(PermissionConstants.AuditLog.Delete, StringComparison.OrdinalIgnoreCase))
            .Select(x => MapOverride(x.Entry, x.Code)).ToArray();
        return ApiResponse<IReadOnlyList<TenantMemberPermissionOverrideDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<TenantMemberPermissionOverrideDto>> SetMemberPermissionOverrideAsync(
        long membershipId, long permissionId, SetTenantMemberPermissionOverrideRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanManage) return Denied<TenantMemberPermissionOverrideDto>();
        if (membershipId <= 0 || permissionId <= 0 || request == null ||
            request.Mode is not ("Allow" or "Deny" or "Inherit") ||
            request.Reason?.Length > 500 ||
            !TryVersion(request.MembershipRowVersion, out var membershipVersion) ||
            (request.ExpiresAt.HasValue && (request.ExpiresAt.Value.Kind != DateTimeKind.Utc ||
                request.ExpiresAt.Value <= DateTime.UtcNow)))
            return ApiResponse<TenantMemberPermissionOverrideDto>.ErrorResponse("Invalid permission override or member version.", 400);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (request.Mode == "Inherit" && (reason != null || request.ExpiresAt.HasValue))
            return ApiResponse<TenantMemberPermissionOverrideDto>.ErrorResponse("Inherited permissions cannot have an expiry or reason.", 400);
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var member = await _db.TenantMemberships.FirstOrDefaultAsync(x =>
                    x.Id == membershipId && x.TenantId == TenantId && !x.IsDeleted, token);
                if (member == null) return ApiResponse<TenantMemberPermissionOverrideDto>.ErrorResponse("Institution member not found.", 404);
                if (member.IsOwner || member.UserId == _current.UserId)
                    return ApiResponse<TenantMemberPermissionOverrideDto>.ErrorResponse("Owner and self permission overrides are restricted.", 403);
                if (member.Status != MembershipStatus.Active ||
                    !await _db.Users.AsNoTracking().AnyAsync(x => x.Id == member.UserId && x.IsActive && !x.IsDeleted, token))
                    return ApiResponse<TenantMemberPermissionOverrideDto>.ErrorResponse("Member must be active.", 409);
                if (!member.RowVersion.AsSpan().SequenceEqual(membershipVersion))
                    return ApiResponse<TenantMemberPermissionOverrideDto>.ErrorResponse("Membership changed. Reload before editing.", 409);
                var permission = await _db.Permissions.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.Id == permissionId && x.IsActive && !x.IsDeleted, token);
                if (permission == null || permission.Code.Equals(PermissionConstants.AuditLog.Delete, StringComparison.OrdinalIgnoreCase))
                    return ApiResponse<TenantMemberPermissionOverrideDto>.ErrorResponse("Permission is not assignable.", 403);

                var existing = await _db.UserPermissions.FirstOrDefaultAsync(x =>
                    x.TenantId == TenantId && x.UserId == member.UserId &&
                    x.PermissionId == permissionId && !x.IsDeleted, token);
                var inherit = request.Mode == "Inherit";
                var allowed = request.Mode == "Allow";
                // PUT retries are safe: an already-applied state produces no duplicate audit or write.
                if (existing == null && inherit)
                    return ApiResponse<TenantMemberPermissionOverrideDto>.SuccessResponse(new()
                    { PermissionId = permissionId, PermissionCode = permission.Code, Mode = "Inherit" });
                if (existing != null && !inherit && existing.IsAllowed == allowed &&
                    existing.Reason == reason && existing.ExpiresAt == request.ExpiresAt)
                    return ApiResponse<TenantMemberPermissionOverrideDto>.SuccessResponse(MapOverride(existing, permission.Code));
                if (existing != null)
                {
                    if (!TryVersion(request.OverrideRowVersion, out var version) ||
                        !existing.RowVersion.AsSpan().SequenceEqual(version))
                        return ApiResponse<TenantMemberPermissionOverrideDto>.ErrorResponse("Permission override changed. Reload before editing.", 409);
                }
                else if (!string.IsNullOrEmpty(request.OverrideRowVersion))
                    return ApiResponse<TenantMemberPermissionOverrideDto>.ErrorResponse("Permission override changed. Reload before editing.", 409);

                var now = DateTime.UtcNow;
                var before = existing == null ? null : JsonSerializer.Serialize(new
                { existing.IsAllowed, existing.Reason, existing.ExpiresAt });
                if (inherit)
                {
                    existing!.IsDeleted = true;
                    existing.DeletedAt = now;
                    existing.DeletedBy = _current.UserId;
                    existing.UpdatedAt = now;
                    existing.UpdatedBy = _current.UserId;
                }
                else if (existing == null)
                {
                    existing = new UserPermission
                    {
                        TenantId = TenantId, UserId = member.UserId, PermissionId = permissionId,
                        IsAllowed = allowed, Reason = reason, ExpiresAt = request.ExpiresAt,
                        CreatedBy = _current.UserId
                    };
                    _db.UserPermissions.Add(existing);
                }
                else
                {
                    existing.IsAllowed = allowed;
                    existing.Reason = reason;
                    existing.ExpiresAt = request.ExpiresAt;
                    existing.UpdatedAt = now;
                    existing.UpdatedBy = _current.UserId;
                }
                await _db.SaveChangesAsync(token);
                _db.AuditLogs.Add(new AuditLog
                {
                    TenantId = TenantId, UserId = _current.UserId,
                    Action = "TenantMemberPermissionOverrideChanged",
                    EntityName = nameof(UserPermission), EntityId = existing!.Id,
                    OldValue = before,
                    NewValue = JsonSerializer.Serialize(new
                    { member.UserId, PermissionId = permissionId, request.Mode, Reason = reason, request.ExpiresAt }),
                    OccurredAt = now
                });
                await _db.SaveChangesAsync(token);
                return ApiResponse<TenantMemberPermissionOverrideDto>.SuccessResponse(inherit
                    ? new TenantMemberPermissionOverrideDto
                    { PermissionId = permissionId, PermissionCode = permission.Code, Mode = "Inherit" }
                    : MapOverride(existing, permission.Code));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TenantMemberPermissionOverrideDto>.ErrorResponse("Permission override changed. Reload and retry.", 409);
        }
        catch (DbUpdateException)
        {
            return ApiResponse<TenantMemberPermissionOverrideDto>.ErrorResponse("Permission override conflict. Reload and retry.", 409);
        }
    }
}
