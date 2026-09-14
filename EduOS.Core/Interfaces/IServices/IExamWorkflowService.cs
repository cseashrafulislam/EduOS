using EduOS.Core.Common;
using EduOS.Core.DTOs.Exams;

namespace EduOS.Core.Interfaces.IServices;

public interface IExamWorkflowService
{
    Task<ApiResponse<ExamMarkRosterDto>> GetMarkRosterAsync(ExamMarkRosterQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ExamMarkRosterDto>> SaveMarksAsync(SaveExamMarksDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ExamResultSheetDto>> GenerateResultsAsync(ExamScopeDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ExamResultSheetDto>> PublishResultsAsync(ExamScopeDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ExamResultSheetDto>> GetResultsAsync(ExamScopeDto request, CancellationToken cancellationToken = default);
}
