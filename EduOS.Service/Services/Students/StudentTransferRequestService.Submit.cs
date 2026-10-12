using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentTransferRequestService
{
    public Task<ApiResponse<TransferRequestDto>> SubmitAsync(SubmitStudentTransferRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<TransferRequestDto>("Tenant administrator permission required.", 403));
        if (request == null || request.ClientRequestId == Guid.Empty || request.StudentReference == Guid.Empty ||
            request.EnrollmentReference == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length > 1000 ||
            !TryVersion(request.StudentRowVersion, out var studentVersion) ||
            !TryVersion(request.EnrollmentRowVersion, out var enrollmentVersion) ||
            request.DestinationTenantId.HasValue && request.DestinationTenantId.Value <= 0 ||
            request.DestinationTenantId == _user.TenantId)
            return Task.FromResult(Error<TransferRequestDto>("Valid student, enrollment, reason and row versions are required. Destination must be another tenant."));
        var tenant = _user.TenantId;
        var reason = request.Reason.Trim();
        return WriteAsync(async token =>
        {
            var replay = await _requests.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.SourceTenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, token);
            if (replay != null)
            {
                var priorStudent = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == replay.SourceStudentId && !x.IsDeleted, token);
                var priorEnrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == replay.SourceEnrollmentId && !x.IsDeleted, token);
                if (priorStudent == null || priorEnrollment == null ||
                    priorStudent.PublicId != request.StudentReference ||
                    priorEnrollment.PublicId != request.EnrollmentReference ||
                    replay.DestinationTenantId != request.DestinationTenantId ||
                    !string.Equals(replay.Reason, reason, StringComparison.Ordinal))
                    return Error<TransferRequestDto>("Request ID was already used for different transfer details.", 409);
                return ApiResponse<TransferRequestDto>.SuccessResponse(Map(replay, priorStudent, priorEnrollment),
                    "Transfer request already submitted.");
            }
            var student = await _students.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.StudentReference && !x.IsDeleted, token);
            if (student == null) return Error<TransferRequestDto>("Student not found.", 404);
            var enrollment = await _enrollments.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.EnrollmentReference &&
                x.StudentId == student.Id && !x.IsDeleted, token);
            if (enrollment == null) return Error<TransferRequestDto>("Student enrollment not found.", 404);
            if (student.StatusCode != "Active" || !enrollment.IsCurrent ||
                enrollment.State != EnrollmentState.Active)
                return Error<TransferRequestDto>("Student must have an active current enrollment.", 409);
            if (!Matches(student.RowVersion, studentVersion) || !Matches(enrollment.RowVersion, enrollmentVersion))
                return Error<TransferRequestDto>("Student or enrollment changed. Reload and retry.", 409);
            if (await _exits.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted, token))
                return Error<TransferRequestDto>("Student already has a final exit.", 409);
            if (await _requests.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.SourceTenantId == tenant && x.SourceStudentId == student.Id && !x.IsDeleted &&
                (x.State == TransferRequestState.Submitted || x.State == TransferRequestState.SourceApproved ||
                 x.State == TransferRequestState.DestinationAccepted), token))
                return Error<TransferRequestDto>("Student already has an open transfer request.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var entity = new TransferRequest
            {
                PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId,
                SourceTenantId = tenant, SourceStudentId = student.Id, SourceEnrollmentId = enrollment.Id,
                DestinationTenantId = request.DestinationTenantId, State = TransferRequestState.Submitted,
                Reason = reason, RequestedAt = now, RequestedByUserId = _user.UserId,
                CreatedAt = now, CreatedBy = _user.UserId
            };
            await _requests.AddAsync(entity);
            await _uow.SaveChangesAsync(token);
            return new ApiResponse<TransferRequestDto>
            {
                Success = true, StatusCode = 201, Message = "Transfer request submitted for source review.",
                Data = Map(entity, student, enrollment)
            };
        }, ct);
    }
}
