using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentExitService
{
    /// <summary>
    /// Read the immutable source-tenant certificate and its matching final transfer exit.
    /// Never resolve a certificate solely by a guessable number or across tenant boundaries.
    /// </summary>
    public async Task<ApiResponse<StudentTransferCertificateDto>> GetCertificateAsync(
        Guid studentReference, string certificateNumber, CancellationToken cancellationToken = default)
    {
        if (!CanManage())
            return ApiResponse<StudentTransferCertificateDto>.ErrorResponse("Student certificate permission required.", 403);
        if (studentReference == Guid.Empty || string.IsNullOrWhiteSpace(certificateNumber) ||
            certificateNumber.Length > 50)
            return ApiResponse<StudentTransferCertificateDto>.ErrorResponse("Valid student and certificate references are required.");

        var tenantId = _currentUser.TenantId;
        try
        {
            var student = await _students.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && !x.IsDeleted &&
                    x.PublicId == studentReference, cancellationToken);
            if (student == null)
                return ApiResponse<StudentTransferCertificateDto>.ErrorResponse("Certificate not found.", 404);

            var certificate = await _certificates.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && !x.IsDeleted &&
                    x.StudentId == student.Id && x.CertificateNumber == certificateNumber, cancellationToken);
            if (certificate == null)
                return ApiResponse<StudentTransferCertificateDto>.ErrorResponse("Certificate not found.", 404);

            var enrollment = await _enrollments.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && !x.IsDeleted &&
                    x.Id == certificate.StudentEnrollmentId && x.StudentId == student.Id, cancellationToken);
            if (enrollment == null)
                return ApiResponse<StudentTransferCertificateDto>.ErrorResponse("Certificate not found.", 404);

            var issuedForTransfer = await _exits.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && !x.IsDeleted && x.StudentId == student.Id &&
                x.StudentEnrollmentId == enrollment.Id &&
                x.ClientRequestId == certificate.ClientRequestId &&
                x.ExitType == StudentExitType.Transfer, cancellationToken);
            if (!issuedForTransfer)
                return ApiResponse<StudentTransferCertificateDto>.ErrorResponse("Certificate not found.", 404);

            return ApiResponse<StudentTransferCertificateDto>.SuccessResponse(new StudentTransferCertificateDto
            {
                Reference = certificate.PublicId,
                StudentReference = student.PublicId,
                CertificateNumber = certificate.CertificateNumber,
                IssueDate = certificate.IssueDate,
                StudentCode = student.StudentCode,
                StudentName = student.FullName,
                RollNo = enrollment.RollNo,
                CampusId = enrollment.CampusId,
                AcademicYearId = enrollment.AcademicYearId,
                AcademicProgramId = enrollment.AcademicProgramId,
                Reason = certificate.Reason,
                ConductRemark = certificate.ConductRemark,
                FeesCleared = certificate.FeesCleared
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Certificate retrieval failed for tenant {TenantId}", tenantId);
            return ApiResponse<StudentTransferCertificateDto>.ErrorResponse("Certificate retrieval unavailable.", 500);
        }
    }
}
