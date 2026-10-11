using EduOS.Core.Common;
using EduOS.Core.DTOs.Auth;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Auth;

/// <summary>
/// All operations are scoped to the trusted tenant resolved by middleware.
/// Identity role assignments are global rows, so only roles owned by the current
/// institution may be assigned. Ownership is the source of TenantAdmin access.
/// </summary>
public sealed class TenantUserAdministrationService : ITenantUserAdministrationService
{
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly UserManager<ApplicationUser> _users;

    public TenantUserAdministrationService(EduOSDbContext db,
        ICurrentUserService current, UserManager<ApplicationUser> users)
    {
        _db = db;
        _current = current;
        _users = users;
    }

    private bool CanManage => _current.IsAuthenticated && _current.IsTenantAdmin && _current.TenantId > 0;
    private long TenantId => _current.TenantId;
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Institution administrator access is required.", 403);
    private IQueryable<ApplicationRole> LocalRoles => _db.Roles.IgnoreQueryFilters()
        .Where(x => x.TenantId == TenantId && !x.IsDeleted && !x.IsSystemRole);

    public async Task<ApiResponse<PagedResult<TenantUserListItemDto>>> GetMembersAsync(
        int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<PagedResult<TenantUserListItemDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 100)
            return ApiResponse<PagedResult<TenantUserListItemDto>>.ErrorResponse("Invalid paging or search.", 400);
        var query = from member in _db.TenantMemberships.AsNoTracking()
                    join user in _db.Users.AsNoTracking() on member.UserId equals user.Id
                    where member.TenantId == TenantId && !member.IsDeleted && !user.IsDeleted
                    select new { member, user };
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
            query = query.Where(x => x.user.FullName.StartsWith(term) ||
                (x.user.Email != null && x.user.Email.StartsWith(term)));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.user.FullName).ThenBy(x => x.member.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                MembershipId = x.member.Id, x.member.UserId, x.member.IsOwner,
                x.member.Status, x.member.RowVersion, x.user.FullName, x.user.Email,
                x.user.IsActive
            }).ToListAsync(ct);
        var userIds = rows.Select(x => x.UserId).ToArray();
        var assigned = await (from link in _db.UserRoles.AsNoTracking()
                              join role in LocalRoles on link.RoleId equals role.Id
                              where userIds.Contains(link.UserId)
                              select new { link.UserId, RoleId = role.Id }).ToListAsync(ct);
        var rolesByUser = assigned.GroupBy(x => x.UserId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<long>)x.Select(r => r.RoleId).ToArray());
        var result = new PagedResult<TenantUserListItemDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = rows.Select(x => new TenantUserListItemDto
            {
                MembershipId = x.MembershipId, UserId = x.UserId,
                FullName = x.FullName ?? "", Email = x.Email ?? "",
                IsOwner = x.IsOwner, Status = x.Status, IsActive = x.IsActive,
                RowVersion = Convert.ToBase64String(x.RowVersion ?? Array.Empty<byte>()),
                RoleIds = rolesByUser.TryGetValue(x.UserId, out var ids) ? ids : Array.Empty<long>()
            }).ToList()
        };
        return ApiResponse<PagedResult<TenantUserListItemDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<IReadOnlyList<TenantRoleDto>>> GetRolesAsync(CancellationToken ct = default)
    {
        if (!CanManage) return Denied<IReadOnlyList<TenantRoleDto>>();
        var roles = await LocalRoles.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Description, x.IsActive }).ToListAsync(ct);
        var ids = roles.Select(x => x.Id).ToArray();
        var permissions = await _db.RolePermissions.AsNoTracking()
            .Where(x => ids.Contains(x.RoleId) && !x.IsDeleted && x.IsAllowed)
            .Select(x => new { x.RoleId, x.PermissionId }).ToListAsync(ct);
        var lookup = permissions.GroupBy(x => x.RoleId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<long>)x.Select(v => v.PermissionId).ToArray());
        IReadOnlyList<TenantRoleDto> result = roles.Select(x => new TenantRoleDto
        {
            Id = x.Id, Name = x.Name ?? "", Description = x.Description,
            IsActive = x.IsActive,
            PermissionIds = lookup.TryGetValue(x.Id, out var permissionsForRole)
                ? permissionsForRole : Array.Empty<long>()
        }).ToArray();
        return ApiResponse<IReadOnlyList<TenantRoleDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<IReadOnlyList<TenantPermissionDto>>> GetPermissionsAsync(CancellationToken ct = default)
    {
        if (!CanManage) return Denied<IReadOnlyList<TenantPermissionDto>>();
        IReadOnlyList<TenantPermissionDto> data = await _db.Permissions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.ModuleCode).ThenBy(x => x.Name)
            .Select(x => new TenantPermissionDto
            {
                Id = x.Id, Code = x.Code, Name = x.Name, ModuleCode = x.ModuleCode
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<TenantPermissionDto>>.SuccessResponse(data);
    }

    public async Task<ApiResponse<TenantRoleDto>> CreateRoleAsync(
        TenantRoleCreateRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<TenantRoleDto>();
        if (request == null || string.IsNullOrWhiteSpace(request.Name))
            return ApiResponse<TenantRoleDto>.ErrorResponse("Role name is required.", 400);
        var name = request.Name.Trim();
        if (name.Length is < 2 or > 100 || name.Equals("TenantAdmin", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase))
            return ApiResponse<TenantRoleDto>.ErrorResponse("Reserved or invalid role name.", 400);
        var normalized = name.ToUpperInvariant();
        if (await LocalRoles.AnyAsync(x => x.NormalizedName == normalized, ct))
            return ApiResponse<TenantRoleDto>.ErrorResponse("This institution already has that role.", 409);
        var role = new ApplicationRole
        {
            TenantId = TenantId, Name = name, NormalizedName = normalized,
            Description = request.Description?.Trim(),
            IsSystemRole = false, IsActive = true
        };
        _db.Roles.Add(role);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return ApiResponse<TenantRoleDto>.ErrorResponse("Could not create role. It may already exist.", 409);
        }
        return ApiResponse<TenantRoleDto>.SuccessResponse(new TenantRoleDto
        {
            Id = role.Id, Name = role.Name!, Description = role.Description, IsActive = true
        }, "Institution role created.");
    }

    public async Task<ApiResponse<bool>> SetMemberRolesAsync(
        long membershipId, TenantMemberRolesRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<bool>();
        if (request == null || request.RoleIds == null || request.RoleIds.Count > 30 ||
            request.RoleIds.Any(id => id <= 0))
            return ApiResponse<bool>.ErrorResponse("Invalid role selection.", 400);
        if (!TryVersion(request.RowVersion, out var version))
            return ApiResponse<bool>.ErrorResponse("A valid membership row version is required.", 400);
        var selected = request.RoleIds.Distinct().ToArray();
        if (await LocalRoles.CountAsync(x => selected.Contains(x.Id) && x.IsActive, ct) != selected.Length)
            return ApiResponse<bool>.ErrorResponse("One or more roles do not belong to this institution.", 403);
        try
        {
            await _db.ExecuteInTransactionAsync(async token =>
            {
                var member = await _db.TenantMemberships.SingleOrDefaultAsync(x =>
                    x.Id == membershipId && x.TenantId == TenantId && !x.IsDeleted, token);
                if (member == null) throw new AdminRejectException("Membership was not found.", 404);
                if (member.Status != MembershipStatus.Active)
                    throw new AdminRejectException("Only active members may receive roles.", 409);
                _db.Entry(member).Property(x => x.RowVersion).OriginalValue = version;

                var oldRoleIds = await (from link in _db.UserRoles
                                        join role in _db.Roles.IgnoreQueryFilters()
                                            on link.RoleId equals role.Id
                                        where link.UserId == member.UserId &&
                                              ((role.TenantId == TenantId && !role.IsSystemRole) ||
                                               (role.TenantId == null && !role.IsSystemRole))
                                        select link.RoleId).ToListAsync(token);
                var obsolete = oldRoleIds.Where(id => !selected.Contains(id)).ToArray();
                var toRemove = await _db.UserRoles.Where(x => x.UserId == member.UserId &&
                    obsolete.Contains(x.RoleId)).ToListAsync(token);
                _db.UserRoles.RemoveRange(toRemove);
                foreach (var roleId in selected.Except(oldRoleIds))
                    _db.UserRoles.Add(new IdentityUserRole<long> { UserId = member.UserId, RoleId = roleId });

                member.UpdatedAt = DateTime.UtcNow;
                member.UpdatedBy = _current.UserId;
                await _db.SaveChangesAsync(token);
                var user = await _users.FindByIdAsync(member.UserId.ToString());
                if (user == null) throw new AdminRejectException("Member account is unavailable.", 409);
                var stamp = await _users.UpdateSecurityStampAsync(user);
                if (!stamp.Succeeded) throw new AdminRejectException("Account was changed concurrently.", 409);
                return true;
            });
            return ApiResponse<bool>.SuccessResponse(true, "Institution roles updated. User sessions invalidated.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Membership changed. Reload and retry.", 409);
        }
        catch (AdminRejectException ex) { return ApiResponse<bool>.ErrorResponse(ex.Message, ex.StatusCode); }
    }

    public async Task<ApiResponse<bool>> SetMemberStatusAsync(
        long membershipId, TenantMemberStatusRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<bool>();
        if (request == null || request.Status is not (MembershipStatus.Active or MembershipStatus.Suspended or MembershipStatus.Left))
            return ApiResponse<bool>.ErrorResponse("Invalid membership status.", 400);
        if (!TryVersion(request.RowVersion, out var version))
            return ApiResponse<bool>.ErrorResponse("A valid membership row version is required.", 400);
        try
        {
            await _db.ExecuteInTransactionAsync(async token =>
            {
                var member = await _db.TenantMemberships.SingleOrDefaultAsync(x =>
                    x.Id == membershipId && x.TenantId == TenantId && !x.IsDeleted, token);
                if (member == null) throw new AdminRejectException("Membership was not found.", 404);
                if (member.IsOwner || member.UserId == _current.UserId)
                    throw new AdminRejectException("Cannot change an owner or your own membership status.", 403);
                if (member.Status == MembershipStatus.Left && request.Status != MembershipStatus.Left)
                    throw new AdminRejectException("A departed membership requires a new invitation.", 409);
                _db.Entry(member).Property(x => x.RowVersion).OriginalValue = version;
                member.Status = request.Status;
                member.LeftAt = request.Status == MembershipStatus.Left ? DateTime.UtcNow : null;
                member.UpdatedBy = _current.UserId;
                member.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(token);
                var user = await _users.FindByIdAsync(member.UserId.ToString());
                if (user == null) throw new AdminRejectException("Member account is unavailable.", 409);
                var stamp = await _users.UpdateSecurityStampAsync(user);
                if (!stamp.Succeeded) throw new AdminRejectException("Account changed concurrently.", 409);
                return true;
            });
            return ApiResponse<bool>.SuccessResponse(true, "Membership status updated.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Membership changed. Reload and retry.", 409);
        }
        catch (AdminRejectException ex) { return ApiResponse<bool>.ErrorResponse(ex.Message, ex.StatusCode); }
    }

    public async Task<ApiResponse<bool>> SetRolePermissionsAsync(
        long roleId, TenantRolePermissionsRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<bool>();
        if (request?.PermissionIds == null || request.PermissionIds.Count > 1000 || request.PermissionIds.Any(id => id <= 0))
            return ApiResponse<bool>.ErrorResponse("Invalid permissions.", 400);
        var selected = request.PermissionIds.Distinct().ToArray();
        if (await _db.Permissions.CountAsync(x =>
            selected.Contains(x.Id) && !x.IsDeleted && x.IsActive, ct) != selected.Length)
            return ApiResponse<bool>.ErrorResponse("Invalid permission selection.", 400);
        try
        {
            await _db.ExecuteInTransactionAsync(async token =>
            {
                var role = await LocalRoles.SingleOrDefaultAsync(x => x.Id == roleId, token);
                if (role == null || !role.IsActive)
                    throw new AdminRejectException("Institution role not found or inactive.", 404);
                var existing = await _db.RolePermissions.Where(x => x.RoleId == roleId && !x.IsDeleted).ToListAsync(token);
                foreach (var record in existing)
                {
                    record.IsAllowed = selected.Contains(record.PermissionId);
                    record.UpdatedBy = _current.UserId;
                    record.UpdatedAt = DateTime.UtcNow;
                }
                foreach (var id in selected.Except(existing.Select(x => x.PermissionId)))
                    _db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = roleId, PermissionId = id, IsAllowed = true, CreatedBy = _current.UserId
                    });
                await _db.SaveChangesAsync(token);
                var memberIds = await _db.UserRoles.Where(x => x.RoleId == roleId)
                    .Select(x => x.UserId).Distinct().ToListAsync(token);
                foreach (var id in memberIds)
                {
                    var user = await _users.FindByIdAsync(id.ToString());
                    if (user == null) continue;
                    var stamp = await _users.UpdateSecurityStampAsync(user);
                    if (!stamp.Succeeded) throw new AdminRejectException("Account changed concurrently.", 409);
                }
                return true;
            });
            return ApiResponse<bool>.SuccessResponse(true, "Role permissions updated.");
        }
        catch (AdminRejectException ex) { return ApiResponse<bool>.ErrorResponse(ex.Message, ex.StatusCode); }
        catch (DbUpdateException)
        {
            return ApiResponse<bool>.ErrorResponse("Concurrent permission update. Reload and retry.", 409);
        }
    }

    private static bool TryVersion(string? value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { bytes = Convert.FromBase64String(value); return bytes.Length == 8; }
        catch (FormatException) { return false; }
    }

    private sealed class AdminRejectException(string message, int statusCode) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }
}
