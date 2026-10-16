using EduOS.Core.Common;
using EduOS.Core.Constants;
using EduOS.Core.DTOs.Auth;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.System;
using EduOS.Core.Entities.SaaS;
using System.Text.Json;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Auth;

/// <summary>
/// All operations are scoped to the trusted tenant resolved by middleware.
/// Identity role assignments are global rows, so only roles owned by the current
/// institution may be assigned. Ownership is the source of TenantAdmin access.
/// </summary>
public sealed partial class TenantUserAdministrationService : ITenantUserAdministrationService
{
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IEmailService _emails;
    private readonly string _portalDomain;

    public TenantUserAdministrationService(EduOSDbContext db,
        ICurrentUserService current, UserManager<ApplicationUser> users,
        IEmailService emails, IOptions<TenantPortalSettings> portal)
    {
        _db = db;
        _current = current;
        _users = users;
        _emails = emails;
        _portalDomain = portal.Value.BaseDomain;
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
            .Select(x => new { x.Id, x.Name, x.Description, x.IsActive, x.ConcurrencyStamp }).ToListAsync(ct);
        var ids = roles.Select(x => x.Id).ToArray();
        var permissions = await _db.RolePermissions.AsNoTracking()
            .Where(x => ids.Contains(x.RoleId) && !x.IsDeleted && x.IsAllowed)
            .Select(x => new { x.RoleId, x.PermissionId }).ToListAsync(ct);
        var lookup = permissions.GroupBy(x => x.RoleId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<long>)x.Select(v => v.PermissionId).ToArray());
        IReadOnlyList<TenantRoleDto> result = roles.Select(x => new TenantRoleDto
        {
            Id = x.Id, Name = x.Name ?? "", Description = x.Description,
            IsActive = x.IsActive, ConcurrencyStamp = x.ConcurrencyStamp ?? "",
            PermissionIds = lookup.TryGetValue(x.Id, out var permissionsForRole)
                ? permissionsForRole : Array.Empty<long>()
        }).ToArray();
        return ApiResponse<IReadOnlyList<TenantRoleDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<IReadOnlyList<TenantPermissionDto>>> GetPermissionsAsync(CancellationToken ct = default)
    {
        if (!CanManage) return Denied<IReadOnlyList<TenantPermissionDto>>();
        IReadOnlyList<TenantPermissionDto> data = await _db.Permissions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive &&
                x.Code != PermissionConstants.AuditLog.Delete)
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
        if (name.Length is < 2 or > 100 ||
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
        try
        {
            await _db.ExecuteInTransactionAsync(async tx =>
            {
                await _db.SaveChangesAsync(tx);
                _db.AuditLogs.Add(new AuditLog
                {
                    TenantId = TenantId, UserId = _current.UserId,
                    Action = "TenantRoleCreated", EntityName = nameof(ApplicationRole),
                    EntityId = role.Id, NewValue = JsonSerializer.Serialize(new { role.Name, role.Description })
                });
                await _db.SaveChangesAsync(tx);
                return true;
            }, ct);
        }
        catch (DbUpdateException)
        {
            return ApiResponse<TenantRoleDto>.ErrorResponse("Could not create role. It may already exist.", 409);
        }
        return ApiResponse<TenantRoleDto>.SuccessResponse(new TenantRoleDto
        {
            Id = role.Id, Name = role.Name!, Description = role.Description,
            IsActive = true, ConcurrencyStamp = role.ConcurrencyStamp ?? ""
        }, "Institution role created.");
    }

    public async Task<ApiResponse<bool>> UpdateRoleAsync(
        long roleId, TenantRoleUpdateRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<bool>();
        if (request == null || string.IsNullOrWhiteSpace(request.ConcurrencyStamp) ||
            request.Description?.Length > 300)
            return ApiResponse<bool>.ErrorResponse("A valid role version and description are required.", 400);
        var role = await LocalRoles.SingleOrDefaultAsync(x => x.Id == roleId, ct);
        if (role == null) return ApiResponse<bool>.ErrorResponse("Role not found.", 404);
        _db.Entry(role).Property(x => x.ConcurrencyStamp).OriginalValue = request.ConcurrencyStamp;
        var previous = JsonSerializer.Serialize(new { role.Description, role.IsActive });
        role.Description = request.Description?.Trim();
        role.IsActive = request.IsActive;
        role.ConcurrencyStamp = Guid.NewGuid().ToString();
        role.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _db.ExecuteInTransactionAsync(async token =>
            {
                await _db.SaveChangesAsync(token);
                _db.AuditLogs.Add(new AuditLog
                {
                    TenantId = TenantId, UserId = _current.UserId,
                    Action = "TenantRoleUpdated", EntityName = nameof(ApplicationRole),
                    EntityId = role.Id, OldValue = previous,
                    NewValue = JsonSerializer.Serialize(new { role.Description, role.IsActive })
                });
                await _db.SaveChangesAsync(token);
                return true;
            }, ct);
            return ApiResponse<bool>.SuccessResponse(true, "Role updated. Affected user access is validated on the next request.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Role was changed by another administrator. Reload and retry.", 409);
        }
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

                // Global legacy Identity grants belong to the account, not to any one
                // institution. Revoke them only when THIS is the user's sole active
                // institution; otherwise preserve unrelated institutions' access.
                var activeMemberships = await _db.TenantMemberships.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.UserId == member.UserId && !x.IsDeleted &&
                        x.Status != MembershipStatus.Left)
                    .Select(x => x.TenantId).Distinct().Take(2).ToListAsync(token);
                var soleInstitution = activeMemberships.Count == 1 &&
                    activeMemberships[0] == TenantId;
                var oldRoleIds = await (from link in _db.UserRoles
                                        join role in _db.Roles.IgnoreQueryFilters()
                                            on link.RoleId equals role.Id
                                        where link.UserId == member.UserId &&
                                              ((role.TenantId == TenantId && !role.IsSystemRole) ||
                                               (soleInstitution && role.TenantId == null && !role.IsSystemRole))
                                        select link.RoleId).ToListAsync(token);
                var obsolete = oldRoleIds.Where(id => !selected.Contains(id)).ToArray();
                var toRemove = await _db.UserRoles.Where(x => x.UserId == member.UserId &&
                    obsolete.Contains(x.RoleId)).ToListAsync(token);
                _db.UserRoles.RemoveRange(toRemove);
                foreach (var roleId in selected.Except(oldRoleIds))
                    _db.UserRoles.Add(new IdentityUserRole<long> { UserId = member.UserId, RoleId = roleId });

                member.UpdatedAt = DateTime.UtcNow;
                member.UpdatedBy = _current.UserId;
                _db.AuditLogs.Add(new AuditLog
                {
                    TenantId = TenantId, UserId = _current.UserId,
                    Action = "TenantMemberRolesChanged", EntityName = nameof(TenantMembership),
                    EntityId = member.Id,
                    OldValue = JsonSerializer.Serialize(oldRoleIds),
                    NewValue = JsonSerializer.Serialize(selected),
                    OccurredAt = DateTime.UtcNow
                });
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
                var priorStatus = member.Status;
                member.Status = request.Status;
                member.LeftAt = request.Status == MembershipStatus.Left ? DateTime.UtcNow : null;
                member.UpdatedBy = _current.UserId;
                member.UpdatedAt = DateTime.UtcNow;
                _db.AuditLogs.Add(new AuditLog
                {
                    TenantId = TenantId, UserId = _current.UserId,
                    Action = "TenantMemberStatusChanged", EntityName = nameof(TenantMembership),
                    EntityId = member.Id, OldValue = priorStatus.ToString(),
                    NewValue = request.Status.ToString()
                });
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
        if (request?.PermissionIds == null || string.IsNullOrWhiteSpace(request.ConcurrencyStamp) ||
            request.PermissionIds.Count > 1000 || request.PermissionIds.Any(id => id <= 0))
            return ApiResponse<bool>.ErrorResponse("Invalid permissions.", 400);
        var selected = request.PermissionIds.Distinct().ToArray();
        if (await _db.Permissions.CountAsync(x =>
            selected.Contains(x.Id) && !x.IsDeleted && x.IsActive &&
            x.Code != PermissionConstants.AuditLog.Delete, ct) != selected.Length)
            return ApiResponse<bool>.ErrorResponse("Invalid permission selection.", 400);
        try
        {
            await _db.ExecuteInTransactionAsync(async token =>
            {
                var role = await LocalRoles.SingleOrDefaultAsync(x => x.Id == roleId, token);
                if (role == null || !role.IsActive)
                    throw new AdminRejectException("Institution role not found or inactive.", 404);
                _db.Entry(role).Property(x => x.ConcurrencyStamp).OriginalValue = request.ConcurrencyStamp;
                role.ConcurrencyStamp = Guid.NewGuid().ToString();
                role.UpdatedAt = DateTime.UtcNow;
                var existing = await _db.RolePermissions.Where(x => x.RoleId == roleId && !x.IsDeleted).ToListAsync(token);
                var oldPermissions = existing.Where(x => x.IsAllowed).Select(x => x.PermissionId).ToArray();
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
                _db.AuditLogs.Add(new AuditLog
                {
                    TenantId = TenantId, UserId = _current.UserId,
                    Action = "TenantRolePermissionsChanged", EntityName = nameof(ApplicationRole),
                    EntityId = roleId, OldValue = JsonSerializer.Serialize(oldPermissions),
                    NewValue = JsonSerializer.Serialize(selected)
                });
                await _db.SaveChangesAsync(token);
                // Permission changes are evaluated through RolePermissions at authorization
                // time. Never rotate thousands of stamps inside a permission transaction.
                return true;
            }, IsolationLevel.Serializable, ct);
            return ApiResponse<bool>.SuccessResponse(true, "Role permissions updated.");
        }
        catch (AdminRejectException ex) { return ApiResponse<bool>.ErrorResponse(ex.Message, ex.StatusCode); }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Role permissions changed. Reload and retry.", 409);
        }
        catch (DbUpdateException)
        {
            return ApiResponse<bool>.ErrorResponse("Concurrent permission update. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<bool>> SendInvitationAsync(
        CreateTenantInvitationRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<bool>();
        if (request == null || string.IsNullOrWhiteSpace(request.Email) ||
            !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email) ||
            request.Email.Length > 200)
            return ApiResponse<bool>.ErrorResponse("Valid email is required.", 400);
        if (string.IsNullOrWhiteSpace(_portalDomain) ||
            Uri.CheckHostName(_portalDomain) != UriHostNameType.Dns)
            return ApiResponse<bool>.ErrorResponse("Trusted institution portal hostname is not configured.", 503);
        var email = request.Email.Trim().ToLowerInvariant();
        if (request.RoleId is > 0 && !await LocalRoles.AnyAsync(x =>
            x.Id == request.RoleId && x.IsActive, ct))
            return ApiResponse<bool>.ErrorResponse("Role does not belong to this institution.", 403);
        if (request.RoleId is < 0 || request.CampusId is < 0)
            return ApiResponse<bool>.ErrorResponse("Invalid institution role or campus.", 400);
        if (request.CampusId is > 0 && !await _db.Campuses.AsNoTracking().AnyAsync(x =>
            x.TenantId == TenantId && x.Id == request.CampusId && x.IsActive && !x.IsDeleted, ct))
            return ApiResponse<bool>.ErrorResponse("Campus does not belong to this institution.", 403);
        if (request.ClientRequestId != Guid.Empty && await _db.TenantInvitations.AsNoTracking()
            .AnyAsync(x => x.TenantId == TenantId && x.PublicId == request.ClientRequestId, ct))
            return ApiResponse<bool>.ErrorResponse("Invitation request already processed. Use a new request to resend.", 409);
        var target = await _users.FindByEmailAsync(email);
        if (target != null && await _db.TenantMemberships.AnyAsync(x =>
            x.TenantId == TenantId && x.UserId == target.Id && !x.IsDeleted, ct))
            return ApiResponse<bool>.ErrorResponse("The user already has an institution membership.", 409);

        // The raw token is intentionally never persisted. Retrying an invitation
        // revokes any earlier unaccepted link for this institution/email.
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var hash = HashInvitationToken(token);
        try
        {
            await _db.ExecuteInTransactionAsync(async tx =>
            {
                var previous = await _db.TenantInvitations.Where(x =>
                    x.TenantId == TenantId && x.Email == email && !x.IsDeleted &&
                    x.AcceptedAt == null && x.RevokedAt == null).ToListAsync(tx);
                foreach (var old in previous)
                {
                    old.RevokedAt = DateTime.UtcNow;
                    old.UpdatedBy = _current.UserId;
                    old.UpdatedAt = DateTime.UtcNow;
                }
                _db.TenantInvitations.Add(new TenantInvitation
                {
                    PublicId = request.ClientRequestId == Guid.Empty ? Guid.NewGuid() : request.ClientRequestId,
                    TenantId = TenantId, Email = email, RoleId = request.RoleId, CampusId = request.CampusId,
                    TokenHash = hash, InvitedByUserId = _current.UserId,
                    ExpiresAt = DateTime.UtcNow.AddHours(48),
                    CreatedBy = _current.UserId
                });
                await _db.SaveChangesAsync(tx);
                return true;
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException)
        {
            return ApiResponse<bool>.ErrorResponse("Invitation conflict. Retry safely.", 409);
        }

        var host = _portalDomain.Trim().ToLowerInvariant();
        var link = "https://" + host + "/TenantUsers/Accept#token=" + Uri.EscapeDataString(token);
        try
        {
            await _emails.SendEmailAsync(email, "EduOS institution invitation",
                "You have been invited to an EduOS institution. Open this secure link to accept within 48 hours:\n" +
                link + "\nIf you did not request this, ignore the email.", isHtml: false);
        }
        catch
        {
            // Link was committed before dispatch; retrying Send Invitation revokes
            // the undelivered token and creates a replacement. Never log raw tokens.
            return ApiResponse<bool>.ErrorResponse("Invitation saved but email delivery failed. Retry the invitation.", 503);
        }
        return ApiResponse<bool>.SuccessResponse(true, "Invitation email sent.");
    }

    public async Task<ApiResponse<bool>> AcceptInvitationAsync(
        AcceptTenantInvitationRequestDto request, CancellationToken ct = default)
    {
        // A signed-in user from institution A must never acquire an implicit
        // system tenant scope for institution B. Ask them to sign out first.
        if (_current.IsAuthenticated)
            return ApiResponse<bool>.ErrorResponse("Sign out before accepting an institution invitation.", 403);
        if (request == null || !ValidInviteToken(request.Token) || !request.AgreeTerms ||
            string.IsNullOrWhiteSpace(request.Password) || request.Password != request.ConfirmPassword)
            return ApiResponse<bool>.ErrorResponse("Invalid invitation or password confirmation.", 400);
        var tokenHash = HashInvitationToken(request.Token);
        var invite = await _db.TenantInvitations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash && !x.IsDeleted &&
                x.AcceptedAt == null && x.RevokedAt == null && x.ExpiresAt > DateTime.UtcNow, ct);
        if (invite == null)
            return ApiResponse<bool>.ErrorResponse("Invitation expired or already used.", 400);

        var now = DateTime.UtcNow;
        var termsId = await _db.LegalDocuments.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.Code.StartsWith("TERMS") && x.EffectiveAt <= now)
            .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.Id)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (termsId <= 0)
            return ApiResponse<bool>.ErrorResponse("Current Terms of Service are unavailable. Contact support.", 503);

        var tenantActive = await _db.Tenants.IgnoreQueryFilters().AsNoTracking().AnyAsync(t =>
            t.Id == invite.TenantId && !t.IsDeleted && (t.State == TenantState.Active ||
            (t.State == TenantState.PendingVerification && t.OnboardingStage != OnboardingStage.Completed)), ct);
        if (!tenantActive)
            return ApiResponse<bool>.ErrorResponse("Institution is not available.", 403);
        if (invite.RoleId.HasValue && !await _db.Roles.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.Id == invite.RoleId && x.TenantId == invite.TenantId &&
                x.IsActive && !x.IsDeleted && !x.IsSystemRole, ct))
            return ApiResponse<bool>.ErrorResponse("Invitation role is no longer available.", 409);
        try
        {
            await _db.ExecuteInTransactionAsync(async tx =>
            {
                // Re-read under Serializable so simultaneous acceptance cannot create
                // duplicate memberships or reuse one token.
                var stored = await _db.TenantInvitations.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.Id == invite.Id && x.TokenHash == tokenHash && x.AcceptedAt == null &&
                    x.RevokedAt == null && x.ExpiresAt > DateTime.UtcNow, tx);
                if (stored == null) throw new AdminRejectException("Invitation already used.", 409);
                using var scope = _db.BeginSystemTenantScope(stored.TenantId);
                var existing = await _users.FindByEmailAsync(stored.Email);
                ApplicationUser user;
                if (existing == null)
                {
                    var name = request.FullName?.Trim();
                    if (string.IsNullOrEmpty(name) || name.Length > 200)
                        throw new AdminRejectException("Full name is required for a new account.", 400);
                    user = new ApplicationUser
                    {
                        UserName = stored.Email, Email = stored.Email, EmailConfirmed = true,
                        FullName = name, IsActive = true
                    };
                    var created = await _users.CreateAsync(user, request.Password);
                    if (!created.Succeeded)
                        throw new AdminRejectException("Unable to create account. Check password requirements.", 400);
                }
                else
                {
                    user = existing;
                    if (!user.IsActive || user.IsDeleted || !user.EmailConfirmed ||
                        !await _users.CheckPasswordAsync(user, request.Password))
                        throw new AdminRejectException("Existing account credentials are invalid.", 400);
                    // Invitation acceptance changes account access. Existing MFA-enabled
                    // identities must complete MFA here too; a valid email link + password
                    // must never weaken an account's enrolled second factor.
                    if (user.TwoFactorEnabled)
                    {
                        if (string.IsNullOrWhiteSpace(request.MfaCode))
                            throw new AdminRejectException("Existing account MFA verification is required.", 400);
                        var validSecondFactor = request.UseRecoveryCode
                            ? (await _users.RedeemTwoFactorRecoveryCodeAsync(
                                user, request.MfaCode.Trim())).Succeeded
                            : await _users.VerifyTwoFactorTokenAsync(user,
                                TokenOptions.DefaultAuthenticatorProvider,
                                request.MfaCode.Replace(" ", "").Replace("-", ""));
                        if (!validSecondFactor)
                            throw new AdminRejectException("Existing account MFA verification is invalid.", 400);
                    }
                }

                if (await _db.TenantMemberships.IgnoreQueryFilters().AnyAsync(x =>
                    x.TenantId == stored.TenantId && x.UserId == user.Id && !x.IsDeleted, tx))
                    throw new AdminRejectException("This user is already a member.", 409);

                if (stored.CampusId.HasValue && !await _db.Campuses.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.Id == stored.CampusId && x.TenantId == stored.TenantId &&
                        x.IsActive && !x.IsDeleted, tx))
                    throw new AdminRejectException("The invited campus is no longer available.", 409);
                _db.TenantMemberships.Add(new TenantMembership
                {
                    TenantId = stored.TenantId, UserId = user.Id,
                    Status = MembershipStatus.Active, IsOwner = false,
                    DefaultCampusId = stored.CampusId, JoinedAt = DateTime.UtcNow
                });
                if (stored.CampusId.HasValue)
                    _db.UserCampusAccesses.Add(new UserCampusAccess
                    {
                        TenantId = stored.TenantId, UserId = user.Id,
                        CampusId = stored.CampusId.Value, IsActive = true, IsDefault = false
                    });
                if (stored.RoleId.HasValue)
                    _db.UserRoles.Add(new IdentityUserRole<long>
                    {
                        UserId = user.Id, RoleId = stored.RoleId.Value
                    });
                _db.UserLegalAcceptances.Add(new UserLegalAcceptance
                {
                    UserId = user.Id, TenantId = stored.TenantId, LegalDocumentId = termsId,
                    AcceptedAt = DateTime.UtcNow,
                    IpAddress = _current.IpAddress is { Length: > 100 } ip ? ip[..100] : _current.IpAddress,
                    UserAgent = _current.UserAgent is { Length: > 500 } ua ? ua[..500] : _current.UserAgent
                });
                stored.AcceptedAt = DateTime.UtcNow;
                stored.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(tx);
                var rotated = await _users.UpdateSecurityStampAsync(user);
                if (!rotated.Succeeded)
                    throw new AdminRejectException("Account changed while accepting invitation.", 409);
                return true;
            }, IsolationLevel.Serializable, ct);
        }
        catch (AdminRejectException ex) { return ApiResponse<bool>.ErrorResponse(ex.Message, ex.StatusCode); }
        catch (DbUpdateException)
        {
            return ApiResponse<bool>.ErrorResponse("Invitation was already accepted or changed. Retry login.", 409);
        }
        return ApiResponse<bool>.SuccessResponse(true, "Invitation accepted. Sign in to select your institution.");
    }

    public async Task<ApiResponse<IReadOnlyList<UserCampusAccessDto>>> GetMemberCampusesAsync(
        long membershipId, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<IReadOnlyList<UserCampusAccessDto>>();
        var member = await _db.TenantMemberships.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == membershipId && x.TenantId == TenantId && !x.IsDeleted, ct);
        if (member == null)
            return ApiResponse<IReadOnlyList<UserCampusAccessDto>>.ErrorResponse("Membership not found.", 404);
        var campuses = await _db.Campuses.AsNoTracking().Where(x => x.TenantId == TenantId &&
            !x.IsDeleted && x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var allowed = await _db.UserCampusAccesses.AsNoTracking().Where(x =>
            x.TenantId == TenantId && x.UserId == member.UserId && x.IsActive && !x.IsDeleted)
            .Select(x => x.CampusId).ToListAsync(ct);
        var ids = allowed.ToHashSet();
        IReadOnlyList<UserCampusAccessDto> results = campuses.Select(x => new UserCampusAccessDto
        {
            CampusId = x.Id, Name = x.Name, IsActive = ids.Contains(x.Id),
            IsDefault = member.DefaultCampusId == x.Id
        }).ToArray();
        return ApiResponse<IReadOnlyList<UserCampusAccessDto>>.SuccessResponse(results);
    }

    public async Task<ApiResponse<bool>> SetMemberCampusesAsync(long membershipId,
        SetMembershipCampusAccessRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return Denied<bool>();
        if (request == null || request.CampusIds == null || request.CampusIds.Count > 200 ||
            request.CampusIds.Any(id => id <= 0) ||
            !TryVersion(request.MembershipRowVersion, out var version))
            return ApiResponse<bool>.ErrorResponse("Invalid campus list or membership version.", 400);
        var selected = request.CampusIds.Distinct().ToArray();
        if (request.DefaultCampusId.HasValue && !selected.Contains(request.DefaultCampusId.Value))
            return ApiResponse<bool>.ErrorResponse("Default campus must be in the selected campuses.", 400);
        if (await _db.Campuses.AsNoTracking().CountAsync(x =>
            x.TenantId == TenantId && selected.Contains(x.Id) && !x.IsDeleted && x.IsActive, ct)
            != selected.Length)
            return ApiResponse<bool>.ErrorResponse("One or more campuses do not belong to this institution.", 403);
        try
        {
            await _db.ExecuteInTransactionAsync(async token =>
            {
                var member = await _db.TenantMemberships.SingleOrDefaultAsync(x =>
                    x.Id == membershipId && x.TenantId == TenantId && !x.IsDeleted, token);
                if (member == null) throw new AdminRejectException("Membership not found.", 404);
                if (member.Status != MembershipStatus.Active)
                    throw new AdminRejectException("Only active members may receive campus access.", 409);
                _db.Entry(member).Property(x => x.RowVersion).OriginalValue = version;
                var entries = await _db.UserCampusAccesses.Where(x =>
                    x.TenantId == TenantId && x.UserId == member.UserId && !x.IsDeleted).ToListAsync(token);
                var previousCampuses = entries.Where(x => x.IsActive).Select(x => x.CampusId).ToArray();
                var previousDefault = member.DefaultCampusId;
                foreach (var entry in entries)
                {
                    entry.IsActive = selected.Contains(entry.CampusId);
                    entry.IsDefault = false; // TenantMembership.DefaultCampusId is authoritative.
                    entry.UpdatedAt = DateTime.UtcNow;
                    entry.UpdatedBy = _current.UserId;
                }
                foreach (var id in selected.Except(entries.Select(x => x.CampusId)))
                    _db.UserCampusAccesses.Add(new UserCampusAccess
                    {
                        TenantId = TenantId, UserId = member.UserId, CampusId = id,
                        IsActive = true, IsDefault = false, CreatedBy = _current.UserId
                    });
                member.DefaultCampusId = request.DefaultCampusId;
                if (member.LastSelectedCampusId.HasValue && !selected.Contains(member.LastSelectedCampusId.Value))
                    member.LastSelectedCampusId = request.DefaultCampusId;
                member.UpdatedAt = DateTime.UtcNow;
                member.UpdatedBy = _current.UserId;
                _db.AuditLogs.Add(new AuditLog
                {
                    TenantId = TenantId, UserId = _current.UserId,
                    Action = "TenantMemberCampusesChanged", EntityName = nameof(TenantMembership),
                    EntityId = member.Id,
                    OldValue = JsonSerializer.Serialize(new { campuses = previousCampuses, defaultCampusId = previousDefault }),
                    NewValue = JsonSerializer.Serialize(new { campuses = selected, defaultCampusId = request.DefaultCampusId })
                });
                await _db.SaveChangesAsync(token);
                var user = await _users.FindByIdAsync(member.UserId.ToString());
                if (user == null) throw new AdminRejectException("Member account is unavailable.", 409);
                var rotated = await _users.UpdateSecurityStampAsync(user);
                if (!rotated.Succeeded) throw new AdminRejectException("Account changed concurrently.", 409);
                return true;
            }, ct);
            return ApiResponse<bool>.SuccessResponse(true, "Campus access updated. User sessions invalidated.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Campus access changed. Reload and retry.", 409);
        }
        catch (AdminRejectException ex) { return ApiResponse<bool>.ErrorResponse(ex.Message, ex.StatusCode); }
    }

    private static string HashInvitationToken(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool ValidInviteToken(string? token) =>
        token is { Length: 64 } && token.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

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
