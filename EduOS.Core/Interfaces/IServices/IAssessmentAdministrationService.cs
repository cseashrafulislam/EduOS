using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;

namespace EduOS.Core.Interfaces.IServices;

public interface IAssessmentAdministrationService
{
    Task<ApiResponse<IReadOnlyList<AssessmentScopeOptionDto>>> GetAvailableScopesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentMarkRosterDto>> GetMarkRosterAsync(AssessmentMarkRosterQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentResultSheetDto>> PreviewResultsAsync(AssessmentScopeDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentResultSheetDto>> GetResultsAsync(AssessmentScopeDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentDto>> SaveAssessmentAsync(Guid? reference, SaveAssessmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentDto>> GetAssessmentAsync(Guid reference, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentDto>> ChangeAssessmentStateAsync(Guid reference, ChangeAssessmentStateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> SaveMarksRegisterAsync(SaveMarksRegisterRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ResultPublicationDto>> PublishResultAsync(PublishResultRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CertificateIssueDto>> IssueCertificateAsync(IssueCertificateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CertificateIssueDto>> RevokeCertificateAsync(long certificateId, RevokeCertificateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentSubjectDto>> SaveAssessmentSubjectAsync(long? subjectId, SaveAssessmentSubjectRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentScheduleDto>> SaveAssessmentScheduleAsync(long? scheduleId, SaveAssessmentScheduleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GradeSchemeDto>> SaveGradeSchemeAsync(long? gradeSchemeId, SaveGradeSchemeRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentComponentDto>> SaveAssessmentComponentAsync(long? componentId, SaveAssessmentComponentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentAssessmentComponentMarkDto>> SaveComponentMarkAsync(long? componentMarkId, SaveStudentAssessmentComponentMarkRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ResultPublicationDto>> WithdrawResultPublicationAsync(long publicationId, WithdrawResultPublicationRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CertificateTemplateDto>> SaveCertificateTemplateAsync(long? templateId, SaveCertificateTemplateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TranscriptIssueDto>> IssueTranscriptAsync(IssueTranscriptRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TranscriptIssueDto>> RevokeTranscriptAsync(Guid transcriptReference, RevokeTranscriptRequestDto request, CancellationToken cancellationToken = default);
}
