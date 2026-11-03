using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;

namespace EduOS.Core.Interfaces.IServices;

public interface IResearchQualityExtensionService
{
    Task<ApiResponse<ProgramOutcomeDto>> CreateOutcomeAsync(ProgramOutcomeCreateDto input, CancellationToken ct = default);
    Task<ApiResponse<List<ProgramOutcomeDto>>> OutcomesAsync(long campusId, long programId, CancellationToken ct = default);
    Task<ApiResponse<StandardOutcomeMapDto>> MapOutcomeAsync(StandardOutcomeMapCreateDto input, CancellationToken ct = default);
    Task<ApiResponse<List<StandardOutcomeMapDto>>> StandardOutcomesAsync(long standardId, CancellationToken ct = default);
    Task<ApiResponse<AcademicEvidenceDto>> LinkAcademicEvidenceAsync(Guid assessment, AcademicPublicationEvidenceLinkDto input, CancellationToken ct = default);
    Task<ApiResponse<List<AcademicEvidenceDto>>> AcademicEvidenceAsync(Guid assessment, CancellationToken ct = default);
    Task<ApiResponse<GrantFinanceAccountDto>> ConfigureFinanceAccountAsync(GrantFinanceAccountCreateDto input, CancellationToken ct = default);
    Task<ApiResponse<List<GrantFinanceAccountDto>>> FinanceAccountsAsync(long schemeId, CancellationToken ct = default);
    Task<ApiResponse<GrantJournalLinkDto>> LinkJournalAsync(Guid grant, GrantJournalLinkCreateDto input, CancellationToken ct = default);
    Task<ApiResponse<GrantFinanceReportDto>> FinanceReportAsync(Guid grant, CancellationToken ct = default);
}
