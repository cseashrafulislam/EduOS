using EduOS.Core.Common;
using EduOS.Core.DTOs.Auth;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.System;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Auth;

public sealed partial class TenantUserAdministrationService
{
    public async Task<ApiResponse<PagedResult<TenantInvitationDto>>> GetInvitationsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<PagedResult<TenantInvitationDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<TenantInvitationDto>>.ErrorResponse("Invalid paging.", 400);
        var query = _db.TenantInvitations.AsNoTracking().Where(x => x.TenantId == TenantId && !x.IsDeleted);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.PublicId, x.Email, x.RoleId, x.CampusId, x.ExpiresAt,
                x.AcceptedAt, x.RevokedAt, x.RowVersion }).ToListAsync(ct);
        return ApiResponse<PagedResult<TenantInvitationDto>>.SuccessResponse(new PagedResult<TenantInvitationDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = rows.Select(x => new TenantInvitationDto
            {
                Reference = x.PublicId, Email = x.Email, RoleId = x.RoleId, CampusId = x.CampusId,
                ExpiresAt = x.ExpiresAt, AcceptedAt = x.AcceptedAt, RevokedAt = x.RevokedAt,
                RowVersion = Convert.ToBase64String(x.RowVersion ?? Array.Empty<byte>())
            }).ToList()
        });
    }

    public async Task<ApiResponse<bool>> RevokeInvitationAsync(Guid reference, RevokeTenantInvitationRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<bool>();
        if (reference == Guid.Empty || request == null || !TryVersion(request.RowVersion, out var version))
            return ApiResponse<bool>.ErrorResponse("Invitation reference and valid row version are required.", 400);
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var invitation = await _db.TenantInvitations.SingleOrDefaultAsync(x =>
                    x.TenantId == TenantId && x.PublicId == reference && !x.IsDeleted, token);
                if (invitation == null) return ApiResponse<bool>.ErrorResponse("Invitation not found.", 404);
                if (invitation.AcceptedAt.HasValue)
                    return ApiResponse<bool>.ErrorResponse("Accepted invitation cannot be revoked.", 409);
                if (invitation.RevokedAt.HasValue)
                    return ApiResponse<bool>.SuccessResponse(true, "Invitation already revoked.");
                if (invitation.ExpiresAt <= DateTime.UtcNow)
                    return ApiResponse<bool>.ErrorResponse("Invitation already expired.", 409);
                if (!invitation.RowVersion.SequenceEqual(version))
                    return ApiResponse<bool>.ErrorResponse("Invitation changed. Refresh before revoking.", 409);
                _db.Entry(invitation).Property(x => x.RowVersion).OriginalValue = version;
                var now = DateTime.UtcNow;
                invitation.RevokedAt = now;
                invitation.UpdatedAt = now;
                invitation.UpdatedBy = _current.UserId;
                _db.AuditLogs.Add(new AuditLog
                {
                    TenantId = TenantId, UserId = _current.UserId,
                    Action = "TenantInvitationRevoked", EntityName = nameof(TenantInvitation),
                    EntityId = invitation.Id, OldValue = "Pending", NewValue = "Revoked",
                    OccurredAt = now, IsSuccess = true
                });
                await _db.SaveChangesAsync(token);
                return ApiResponse<bool>.SuccessResponse(true, "Invitation revoked.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        { return ApiResponse<bool>.ErrorResponse("Invitation changed. Refresh before revoking.", 409); }
        catch (DbUpdateException)
        { return ApiResponse<bool>.ErrorResponse("Invitation revocation conflict.", 409); }
    }
}
