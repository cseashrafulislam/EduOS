using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentTransferRequestService
{
    public Task<ApiResponse<TransferRequestDto>> ReviewAsync(Guid reference, ReviewTransferRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<TransferRequestDto>("Tenant administrator permission required.", 403));
        if (reference == Guid.Empty || request == null || !TryVersion(request.RowVersion, out var expected) ||
            !string.IsNullOrWhiteSpace(request.Note))
            return Task.FromResult(Error<TransferRequestDto>("Valid request version required; review notes are not supported."));
        var tenant = _user.TenantId;
        return WriteAsync(async token =>
        {
            var item = await _requests.GetQueryable().FirstOrDefaultAsync(x =>
                x.SourceTenantId == tenant && x.PublicId == reference && !x.IsDeleted, token);
            if (item == null) return Error<TransferRequestDto>("Transfer request not found.", 404);
            var student = await _students.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == item.SourceStudentId && !x.IsDeleted, token);
            var enrollment = await _enrollments.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == item.SourceEnrollmentId && x.StudentId == item.SourceStudentId &&
                !x.IsDeleted, token);
            if (student == null || enrollment == null) return Error<TransferRequestDto>("Source records unavailable.", 409);
            var target = request.Approve ? TransferRequestState.SourceApproved : TransferRequestState.Rejected;
            if (item.State == target)
                return ApiResponse<TransferRequestDto>.SuccessResponse(Map(item, student, enrollment), "Already reviewed.");
            if (item.State != TransferRequestState.Submitted)
                return Error<TransferRequestDto>("Only submitted requests can be reviewed.", 409);
            if (!Matches(item.RowVersion, expected))
                return Error<TransferRequestDto>("Transfer request changed. Reload and retry.", 409);
            if (request.Approve)
            {
                if (student.StatusCode != "Active" || !enrollment.IsCurrent || enrollment.State != EnrollmentState.Active)
                    return Error<TransferRequestDto>("Current active enrollment required.", 409);
                if (await _exits.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted, token))
                    return Error<TransferRequestDto>("Student has a final exit.", 409);
            }
            var now = _clock.GetUtcNow().UtcDateTime;
            item.State = target; item.UpdatedAt = now; item.UpdatedBy = _user.UserId;
            if (!request.Approve) { item.RejectedAt = now; item.RejectedByUserId = _user.UserId; }
            // Source approval never changes enrollment or creates a destination student.
            await _uow.SaveChangesAsync(token);
            return ApiResponse<TransferRequestDto>.SuccessResponse(Map(item, student, enrollment),
                request.Approve ? "Source approved." : "Request rejected.");
        }, ct);
    }
}
