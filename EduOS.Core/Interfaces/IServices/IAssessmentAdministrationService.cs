using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;

namespace EduOS.Core.Interfaces.IServices;

public interface IAssessmentAdministrationService
{
    Task<ApiResponse<IReadOnlyList<AssessmentScopeOptionDto>>> GetAvailableScopesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<AssessmentScopeOptionDto>>> SearchAvailableScopesAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentAdmitCardPageDto>> SearchAdmitCardsAsync(AssessmentAdmitCardQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentMarkRosterDto>> SearchMarkRosterAsync(AssessmentMarkRosterPageQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentMarkRosterDto>> GetMarkRosterAsync(AssessmentMarkRosterQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentReadinessPageDto>> GetResultReadinessAsync(AssessmentReadinessQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentMarkChangePageDto>> SearchMarkChangesAsync(AssessmentMarkChangeQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentResultSheetDto>> PreviewResultsAsync(AssessmentScopeDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentResultSheetDto>> GetResultsAsync(AssessmentScopeDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentPublicationHistoryPageDto>> SearchPublicationHistoryAsync(AssessmentPublicationHistoryQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PublishedGradeDistributionDto>> GetPublishedGradeDistributionAsync(PublishedGradeDistributionQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PublishedGradeDistributionComparisonDto>> ComparePublishedGradeDistributionsAsync(PublishedGradeDistributionComparisonQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PublishedTabulationPageDto>> SearchPublishedTabulationAsync(PublishedTabulationQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PublishedVersionComparisonPageDto>> SearchPublishedVersionComparisonAsync(PublishedVersionComparisonQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PublishedSnapshotIntegrityPageDto>> SearchPublishedSnapshotIssuesAsync(long publicationId, PublishedSnapshotIntegrityQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentDto>> SaveAssessmentAsync(Guid? reference, SaveAssessmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentDto>> GetAssessmentAsync(Guid reference, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentDto>> ChangeAssessmentStateAsync(Guid reference, ChangeAssessmentStateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> SaveMarksRegisterAsync(SaveMarksRegisterRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<AssessmentMarkReviewQueueItemDto>>> SearchMarkReviewQueueAsync(AssessmentMarkReviewQueueQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentMarkReviewDto>> GetMarkReviewAsync(long subjectId, CancellationToken cancellationToken = default);
    Task<ApiResponse<ResultRescrutinyPageDto>> SearchResultRescrutinyAsync(ResultRescrutinyQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ResultRescrutinyHistoryDto>> GetResultRescrutinyHistoryAsync(Guid caseReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<ResultRescrutinyReviewDto>> GetResultRescrutinyReviewAsync(Guid caseReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<ResultRescrutinyItemDto>> RequestResultRescrutinyAsync(RequestResultRescrutinyDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> DecideResultRescrutinyAsync(Guid caseReference, DecideResultRescrutinyDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentMarkReviewHistoryPageDto>> SearchMarkReviewHistoryAsync(
        long subjectId, AssessmentMarkReviewHistoryQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentMarkReviewDto>> DecideMarkReviewAsync(long subjectId, AssessmentMarkReviewRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ResultPublicationDto>> PublishResultAsync(PublishResultRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<SubjectCompletionBatchDto>>> GetSubjectCompletionBatchesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<SubjectCompletionBatchDto>>> SearchSubjectCompletionBatchesAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<SubjectCompletionPageDto>> SearchSubjectCompletionsAsync(SubjectCompletionQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<SubjectCompletionRowDto>> CompleteSubjectAsync(long id, CompleteSubjectRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<CertificateCandidateDto>>> GetCertificateCandidatesAsync(string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<CertificateCandidateDto>>> SearchCertificateCandidatesAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<CertificateIssueRegisterPageDto>> SearchCertificateIssueRegisterAsync(CertificateIssueRegisterQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CertificateIssueDto>> IssueCertificateAsync(IssueCertificateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CertificateIssueDto>> RevokeCertificateAsync(long certificateId, RevokeCertificateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentSubjectDto>> SaveAssessmentSubjectAsync(long? subjectId, SaveAssessmentSubjectRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentScheduleRegisterPageDto>> SearchAssessmentSchedulesAsync(AssessmentScheduleRegisterQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<AssessmentScheduleRoomDto>>> GetAssessmentScheduleRoomsAsync(long assessmentSubjectId, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentScheduleDto>> SaveAssessmentScheduleAsync(long? scheduleId, SaveAssessmentScheduleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GradeSchemeRegisterPageDto>> SearchGradeSchemesAsync(GradeSchemeRegisterQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GradeSchemeDto>> GetGradeSchemeAsync(long gradeSchemeId, CancellationToken cancellationToken = default);
    Task<ApiResponse<GradeSchemeDto>> SaveGradeSchemeAsync(long? gradeSchemeId, SaveGradeSchemeRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentComponentRegisterPageDto>> SearchAssessmentComponentsAsync(AssessmentComponentRegisterQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentComponentDto>> SaveAssessmentComponentAsync(long? componentId, SaveAssessmentComponentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssessmentComponentMarkRosterDto>> GetComponentMarkRosterAsync(AssessmentComponentMarkRosterQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentAssessmentComponentMarkDto>> SaveComponentMarkAsync(long? componentMarkId, SaveStudentAssessmentComponentMarkRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ResultPublicationDto>> WithdrawResultPublicationAsync(long publicationId, WithdrawResultPublicationRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CertificateTemplateRegisterPageDto>> SearchCertificateTemplatesAsync(CertificateTemplateRegisterQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CertificateTemplateDto>> GetCertificateTemplateAsync(long templateId, CancellationToken cancellationToken = default);
    Task<ApiResponse<CertificateTemplateDto>> SaveCertificateTemplateAsync(long? templateId, SaveCertificateTemplateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<TranscriptCandidateDto>>> GetTranscriptCandidatesAsync(string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TranscriptCandidateDto>>> SearchTranscriptCandidatesAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<TranscriptRegisterPageDto>> SearchTranscriptRegisterAsync(TranscriptRegisterQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TranscriptIssueDto>> IssueTranscriptAsync(IssueTranscriptRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TranscriptIssueDto>> RevokeTranscriptAsync(Guid transcriptReference, RevokeTranscriptRequestDto request, CancellationToken cancellationToken = default);
}
