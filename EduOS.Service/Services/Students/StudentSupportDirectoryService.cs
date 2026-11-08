using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

/// <summary>Limited tenant-scoped chooser. Never disclose other learners to a portal actor.</summary>
public sealed class StudentSupportDirectoryService(EduOSDbContext db, ICurrentUserService user)
{
    private bool Scoped => user.IsAuthenticated && user.UserId > 0 && user.TenantId > 0 &&
        db.CurrentTenantId == user.TenantId;

    public async Task<ApiResponse<IReadOnlyList<StudentSupportEnrollmentOption>>> EnrollmentsAsync(
        string? search, bool financialOnly, CancellationToken ct = default)
    {
        if (!Scoped) return ApiResponse<IReadOnlyList<StudentSupportEnrollmentOption>>
            .ErrorResponse("Authentication required.", 403);
        var admin = user.IsTenantAdmin || (!financialOnly && user.IsInRole("SafeguardingOfficer"));
        var term = search?.Trim() ?? "";
        if (term.Length > 80 || (admin && term.Length is > 0 and < 2))
            return ApiResponse<IReadOnlyList<StudentSupportEnrollmentOption>>
                .ErrorResponse("Search needs at least two characters (or leave empty for your own enrollments).");
        var tenantId = user.TenantId;
        var query = from enrollment in db.StudentEnrollments.AsNoTracking()
            join student in db.Students.AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where enrollment.TenantId == tenantId && !enrollment.IsDeleted && enrollment.IsCurrent &&
                enrollment.State == EnrollmentState.Active && !student.IsDeleted && student.StatusCode == "Active"
            select new { enrollment, student };
        if (admin)
        {
            if (user.IsInRole("SafeguardingOfficer") && !user.IsTenantAdmin)
                query = query.Where(x => db.UserCampusAccesses.Any(a =>
                    a.TenantId == tenantId && a.CampusId == x.enrollment.CampusId &&
                    a.UserId == user.UserId && a.IsActive && !a.IsDeleted));
        }
        else
            query = query.Where(x => x.student.UserId == user.UserId ||
                (from link in db.StudentGuardians
                 join guardian in db.Guardians on new { link.TenantId, Id = link.GuardianId }
                     equals new { guardian.TenantId, guardian.Id }
                 where link.TenantId == tenantId && link.StudentId == x.student.Id &&
                     !link.IsDeleted && !guardian.IsDeleted && guardian.IsActive &&
                     guardian.UserId == user.UserId && (!financialOnly || link.IsFinancialContact)
                 select link.Id).Any());
        if (term.Length >= 2)
            query = query.Where(x => x.student.StudentCode.StartsWith(term) ||
                x.student.FullName.StartsWith(term));
        var rows = await query.OrderBy(x => x.student.StudentCode).ThenBy(x => x.enrollment.Id)
            .Take(30).Select(x => new StudentSupportEnrollmentOption
            {
                StudentReference = x.student.PublicId, EnrollmentReference = x.enrollment.PublicId,
                StudentCode = x.student.StudentCode, StudentName = x.student.FullName,
                CampusId = x.enrollment.CampusId, RollNo = x.enrollment.RollNo
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<StudentSupportEnrollmentOption>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<StudentSupportAidRuleOption>>> AidRulesAsync(
        CancellationToken ct = default)
    {
        if (!Scoped) return ApiResponse<IReadOnlyList<StudentSupportAidRuleOption>>
            .ErrorResponse("Authentication required.", 403);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rules = await db.DiscountRules.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.IsActive && !x.IsDeleted && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today))
            .OrderBy(x => x.Name).Take(100)
            .Select(x => new StudentSupportAidRuleOption
            {
                Id = x.Id, Name = x.Name, IsPercentage = x.IsPercentage, Value = x.Value,
                EffectiveFrom = x.EffectiveFrom, EffectiveTo = x.EffectiveTo
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<StudentSupportAidRuleOption>>.SuccessResponse(rules);
    }

    public async Task<ApiResponse<IReadOnlyList<StudentSupportCounselorOption>>> CounselorsAsync(
        long campusId, CancellationToken ct = default)
    {
        if (!Scoped || !user.IsInRole("SafeguardingOfficer"))
            return ApiResponse<IReadOnlyList<StudentSupportCounselorOption>>
                .ErrorResponse("Safeguarding officer required.", 403);
        if (campusId <= 0 || !await db.UserCampusAccesses.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && x.CampusId == campusId &&
            x.UserId == user.UserId && x.IsActive && !x.IsDeleted, ct))
            return ApiResponse<IReadOnlyList<StudentSupportCounselorOption>>
                .ErrorResponse("Campus not accessible.", 403);

        var staff = await db.Users.AsNoTracking().Where(x => x.IsActive && !x.IsDeleted &&
            db.TenantMemberships.Any(m => m.TenantId == user.TenantId && m.UserId == x.Id &&
                m.Status == MembershipStatus.Active && !m.IsDeleted) &&
            db.UserCampusAccesses.Any(a => a.TenantId == user.TenantId && a.UserId == x.Id &&
                a.CampusId == campusId && a.IsActive && !a.IsDeleted) &&
            db.UserRoles.Any(ur => ur.UserId == x.Id && db.Roles.Any(r =>
                r.Id == ur.RoleId && r.IsActive && !r.IsDeleted &&
                r.NormalizedName == "COUNSELOR" && (r.TenantId == user.TenantId ||
                    (r.TenantId == null && db.TenantMemberships.Count(m =>
                        m.UserId == x.Id && !m.IsDeleted && m.Status != MembershipStatus.Left) == 1)))))
            .OrderBy(x => x.FullName).ThenBy(x => x.Id).Take(100)
            .Select(x => new StudentSupportCounselorOption
            {
                UserId = x.Id, FullName = x.FullName
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<StudentSupportCounselorOption>>.SuccessResponse(staff);
    }
}
