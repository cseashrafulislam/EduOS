using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class AssetLifecycleService
{
    private async Task<(long? EmployeeId, long? StudentId, long? UnitId, string? Error)> ResolveAssigneeAsync(
        AssignAssetRequestDto r, long campusId, CancellationToken ct)
    {
        var count = (r.EmployeeReference.HasValue ? 1 : 0) + (r.StudentReference.HasValue ? 1 : 0) +
            (r.OrganizationUnitId.HasValue ? 1 : 0);
        if (count != 1) return (null, null, null, "Select exactly one employee, student, or organization unit.");
        if (r.EmployeeReference.HasValue)
        {
            var employeeId = await _db.Employees.AsNoTracking()
                .Where(e => e.TenantId == Tenant && e.PublicId == r.EmployeeReference && !e.IsDeleted &&
                    e.State == EduOS.Core.Enums.Domain.EmployeeState.Active)
                .Select(e => (long?)e.Id).FirstOrDefaultAsync(ct);
            if (!employeeId.HasValue || !await _db.EmployeeCampusAssignments.AsNoTracking().AnyAsync(a =>
                a.TenantId == Tenant && a.EmployeeId == employeeId && a.CampusId == campusId &&
                a.IsCurrent && !a.IsDeleted && a.EffectiveFrom <= r.AssignedOn &&
                (a.EffectiveTo == null || a.EffectiveTo >= r.AssignedOn), ct))
                return (null, null, null, "Employee is not active at the asset campus.");
            return (employeeId, null, null, null);
        }
        if (r.StudentReference.HasValue)
        {
            var studentId = await _db.Students.AsNoTracking().Where(s =>
                s.TenantId == Tenant && s.PublicId == r.StudentReference && !s.IsDeleted && s.StatusCode == "Active")
                .Select(s => (long?)s.Id).FirstOrDefaultAsync(ct);
            if (!studentId.HasValue || !await _db.StudentEnrollments.AsNoTracking().AnyAsync(e =>
                e.TenantId == Tenant && e.StudentId == studentId && e.CampusId == campusId &&
                e.IsCurrent && !e.IsDeleted && e.EnrollmentDate <= r.AssignedOn &&
                (e.EndDate == null || e.EndDate >= r.AssignedOn), ct))
                return (null, null, null, "Student has no current enrollment at the asset campus.");
            return (null, studentId, null, null);
        }
        var unitId = r.OrganizationUnitId!.Value;
        if (!await _db.OrganizationUnits.AsNoTracking().AnyAsync(u => u.TenantId == Tenant &&
            u.Id == unitId && u.CampusId == campusId && u.IsActive && !u.IsDeleted, ct))
            return (null, null, null, "Organization unit must be active at the asset campus.");
        return (null, null, unitId, null);
    }
}
