using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.DTOs.Files;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Self-service and authorized review of student leave applications.</summary>
public interface IStudentLeaveService
{
    Task<ApiResponse<PagedResult<StudentLeaveEnrollmentChoiceDto>>> GetEligibleEnrollmentsAsync(string search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentLeaveApplicationDto>>> GetApplicationsAsync(Guid? enrollmentReference, LeaveState? state, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<FileAssetDto>> UploadAttachmentAsync(Guid enrollmentReference, Guid uploadRequestId, PrivateFileUploadDto upload, CancellationToken cancellationToken = default);
    Task<ApiResponse<DocumentFileDownloadDto>> DownloadAttachmentAsync(long applicationId, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentLeaveApplicationDto>> ApplyAsync(CreateStudentLeaveApplicationRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentLeaveApplicationDto>> CancelAsync(long applicationId, CancelStudentLeaveRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentLeaveApplicationDto>> ReviewAsync(long applicationId, ReviewStudentLeaveRequestDto request, CancellationToken cancellationToken = default);
}
