using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;

namespace EduOS.Core.Interfaces.IServices;

public interface IAdmissionIntakeService
{
    Task<ApiResponse<IReadOnlyList<AdmissionIntakeFormDto>>> GetFormsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionIntakeFormDto>> CreateFormAsync(CreateAdmissionIntakeFormDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionIntakeFormDto>> UpdateFormAsync(long id, UpdateAdmissionIntakeFormDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionIntakeFormDto>> PublishFormAsync(long id, AdmissionRowVersionDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionIntakeFormDto>> CloseFormAsync(long id, AdmissionRowVersionDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<AdmissionApplicantDocumentDto>>> GetDocumentsAsync(Guid applicantReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionApplicantDocumentDto>> ReviewDocumentAsync(Guid applicantReference, long documentId, ReviewAdmissionDocumentDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionDocumentContentDto>> GetDocumentContentAsync(Guid applicantReference, long documentId, CancellationToken cancellationToken = default);
}
