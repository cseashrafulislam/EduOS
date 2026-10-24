using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentTransferRequestService
{
    public async Task<ApiResponse<StudentTransferEligibilityDto>> GetEligibilityAsync(Guid studentReference, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<StudentTransferEligibilityDto>("Tenant administrator permission required.", 403);
        if (studentReference == Guid.Empty) return Error<StudentTransferEligibilityDto>("Student reference is required.");
        var tenant = _user.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted, ct);
        if (student == null) return Error<StudentTransferEligibilityDto>("Student not found.", 404);
        if (student.StatusCode != "Active")
            return Error<StudentTransferEligibilityDto>("Student must be active to request transfer.", 409);
        var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.StudentId == student.Id && x.IsCurrent &&
            x.State == EnrollmentState.Active && !x.IsDeleted, ct);
        if (enrollment == null) return Error<StudentTransferEligibilityDto>("Current active enrollment not found.", 409);
        if (await _exits.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.StudentId == student.Id &&
            x.StudentEnrollmentId == enrollment.Id && !x.IsDeleted, ct))
            return Error<StudentTransferEligibilityDto>("Student has a final exit record.", 409);
        return ApiResponse<StudentTransferEligibilityDto>.SuccessResponse(new StudentTransferEligibilityDto
        {
            StudentReference = student.PublicId, StudentCode = student.StudentCode, StudentName = student.FullName,
            StudentRowVersion = Convert.ToBase64String(student.RowVersion),
            EnrollmentReference = enrollment.PublicId, EnrollmentRowVersion = Convert.ToBase64String(enrollment.RowVersion),
            CampusId = enrollment.CampusId, RollNo = enrollment.RollNo
        });
    }

    public async Task<ApiResponse<IReadOnlyList<TransferRequestDto>>> GetRequestsAsync(Guid studentReference, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<IReadOnlyList<TransferRequestDto>>("Tenant administrator permission required.", 403);
        if (studentReference == Guid.Empty) return Error<IReadOnlyList<TransferRequestDto>>("Student reference is required.");
        var tenant = _user.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted, ct);
        if (student == null) return Error<IReadOnlyList<TransferRequestDto>>("Student not found.", 404);
        var records = await _requests.GetQueryable().AsNoTracking().Where(x =>
            x.SourceTenantId == tenant && x.SourceStudentId == student.Id && !x.IsDeleted)
            .OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.Id).Take(100).ToListAsync(ct);
        var enrollmentIds = records.Select(x => x.SourceEnrollmentId).Distinct().ToArray();
        var enrollments = await _enrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && enrollmentIds.Contains(x.Id) && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, ct);
        IReadOnlyList<TransferRequestDto> items = records.Where(x => enrollments.ContainsKey(x.SourceEnrollmentId))
            .Select(x => Map(x, student, enrollments[x.SourceEnrollmentId])).ToList();
        return ApiResponse<IReadOnlyList<TransferRequestDto>>.SuccessResponse(items);
    }
}
