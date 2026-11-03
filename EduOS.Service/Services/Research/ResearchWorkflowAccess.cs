using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Research;

/// <summary>Live tenant membership, campus grant and role checks; never trusts token role snapshots.</summary>
public sealed class ResearchWorkflowAccess(EduOSDbContext db, ICurrentUserService user)
{
    public long TenantId => user.TenantId;
    public long UserId => user.UserId;
    public bool Authenticated => user.IsAuthenticated && TenantId > 0 && UserId > 0 &&
        db.CurrentTenantId == TenantId;

    public async Task<HashSet<string>> RolesAsync(CancellationToken ct)
    {
        if (!Authenticated || !await db.Users.AsNoTracking().AnyAsync(u => u.Id == UserId &&
            u.IsActive && !u.IsDeleted, ct) ||
            !await db.TenantMemberships.AsNoTracking().AnyAsync(m => m.TenantId == TenantId &&
                m.UserId == UserId && m.Status == MembershipStatus.Active && !m.IsDeleted, ct))
            return new(StringComparer.OrdinalIgnoreCase);
        // Legacy globally defined non-system roles apply only to single-tenant identities.
        // Multi-tenant identities must have explicit tenant-local role assignments.
        var soleTenant = !await db.TenantMemberships.AsNoTracking().AnyAsync(m => m.UserId == UserId &&
            m.TenantId != TenantId && m.Status == MembershipStatus.Active && !m.IsDeleted, ct);
        var roleNames = await (from ur in db.UserRoles.AsNoTracking()
            join role in db.Roles.IgnoreQueryFilters().AsNoTracking() on ur.RoleId equals role.Id
            where ur.UserId == UserId && role.IsActive && !role.IsDeleted &&
                  (role.TenantId == TenantId || (soleTenant && role.TenantId == null && !role.IsSystemRole))
            select role.NormalizedName).ToListAsync(ct);
        return roleNames.Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool Has(IReadOnlySet<string> roles, params string[] codes) =>
        codes.Any(roles.Contains);

    public static bool ResearchCoordinator(IReadOnlySet<string> roles) =>
        Has(roles, "TENANTADMIN", "PRINCIPAL", "RESEARCHCOORDINATOR");

    public static bool AccreditationCoordinator(IReadOnlySet<string> roles) =>
        Has(roles, "TENANTADMIN", "PRINCIPAL", "ACCREDITATIONCOORDINATOR");

    public Task<bool> CampusAsync(long campus, CancellationToken ct) => campus > 0 && Authenticated
        ? db.Campuses.AsNoTracking().AnyAsync(c => c.Id == campus && c.TenantId == TenantId &&
            c.IsActive && !c.IsDeleted && db.UserCampusAccesses.Any(g => g.TenantId == TenantId &&
                g.UserId == UserId && g.CampusId == campus && g.IsActive && !g.IsDeleted), ct)
        : Task.FromResult(false);

    public Task<bool> EligibleAsync(long target, long campus, string[] roleCodes, CancellationToken ct) =>
        target > 0 && target != UserId && Authenticated && campus > 0
        ? db.Users.AsNoTracking().AnyAsync(u => u.Id == target && u.IsActive && !u.IsDeleted &&
            db.TenantMemberships.Any(m => m.TenantId == TenantId && m.UserId == target &&
                m.Status == MembershipStatus.Active && !m.IsDeleted) &&
            db.UserCampusAccesses.Any(g => g.TenantId == TenantId && g.UserId == target &&
                g.CampusId == campus && g.IsActive && !g.IsDeleted) &&
            db.UserRoles.Any(ur => ur.UserId == target &&
                db.Roles.IgnoreQueryFilters().Any(r => r.Id == ur.RoleId && r.IsActive && !r.IsDeleted &&
                    (r.TenantId == TenantId || (r.TenantId == null && !r.IsSystemRole &&
                        !db.TenantMemberships.Any(other => other.UserId == target && other.TenantId != TenantId &&
                            other.Status == MembershipStatus.Active && !other.IsDeleted))) &&
                    roleCodes.Contains(r.NormalizedName!))), ct)
        : Task.FromResult(false);
}
